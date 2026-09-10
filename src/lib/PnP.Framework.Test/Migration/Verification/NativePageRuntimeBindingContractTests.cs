using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using PnP.Framework.Test.ClassicWiki;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Verification
{
    [TestClass]
    public class NativePageRuntimeBindingContractTests
    {
        private const string FixtureOriginEvidencePackageDigest = "40aef6bc9c8b443a93839cdc99b8f06d2e0980e2883625766ac3b81bdf032283";
        private const string FixtureSourceVersionDigest = "7c0ed8c29e1a81c925f93bca41affa5035116f444af278a35c62ca807045f8d5";
        private const string FixtureInputDigest = "9975e98736813227a3eb3d5fc383543f0f8e4b0579dfc3079f9a168ed7f8d058";
        private const long FixtureInputLength = 315;
        private static readonly HashSet<string> SupportedPermanentFixtureRecipes = new HashSet<string>(StringComparer.Ordinal)
        {
            "case:access-denied-terminal",
            "case:capture-before-import",
            "case:consistent-unobserved-target",
            "case:custom-report-only",
            "case:exact-match",
            "case:http-200-denial",
            "case:missing-provenance",
            "case:pending-preservation",
            "case:policy-cannot-upgrade-pending",
            "case:self-authored-verified",
            "case:stale-source-version",
            "case:weakened-manifest",
            "case:wrong-operation",
            "case:wrong-target-file"
        };

        [TestMethod]
        public void PreCaptureBindingSealsPackageManifestImportTargetAndTimeline()
        {
            var fixture = NativeRuntimeTestFixture.Create();

            var digest = fixture.ValidateBinding();

            Assert.AreEqual(fixture.Package.SnapshotDigest, fixture.Binding.SnapshotDigestSha256);
            Assert.AreEqual(fixture.Package.PlanDigest, fixture.Binding.PlanDigest);
            Assert.AreEqual(fixture.AdmittedDigest, fixture.Binding.AdmittedPlanDigestSha256);
            Assert.AreEqual(fixture.ImportAggregate.ReceiptDigestSha256, fixture.Binding.ImportReceiptDigestSha256);
            Assert.AreEqual(fixture.Target.ListId, fixture.Binding.TargetStorageIdentity.ListId);
            Assert.AreEqual(fixture.AdmittedPlan.Operations.RuntimeOperationId, fixture.Binding.Operations.RuntimeOperationId);
            Assert.AreEqual(64, digest.Length);
            Assert.IsTrue(fixture.Binding.CaptureNotBeforeUtc >= fixture.ImportAggregate.ClassicWikiReceipt.CompletedAtUtc);
        }

        [TestMethod]
        public void WeakeningOrReorderingFixedManifestFailsClosedEvenWhenResealed()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.RequirementsManifest.Requirements[0].Required = false;
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);

            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.RequirementsManifest.Requirements = fixture.Binding.RequirementsManifest.Requirements.Reverse().ToList();
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());
        }

        [TestMethod]
        public void StaleSourceWrongOperationAndWrongTargetTupleFailClosed()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.SourceVersion.VersionDigestSha256 = NativeRuntimeTestFixture.Hash("stale-source");
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.Operations.RuntimeOperationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.TargetStorageIdentity.SiteId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());
        }

        [TestMethod]
        public void MissingOrAlteredPackageImportAndIdentityArtifactsFailClosed()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.ArtifactStore.Remove(fixture.Binding.PackageEvidence.Sha256);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ArtifactStore.Corrupt(fixture.Binding.NativeImportEvidence.Sha256);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.TargetIdentityEvidence.Artifact.Locator = "../escape";
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());
        }

        [TestMethod]
        public void ExternalEnvelopeRequiresIndependentStableTargetAndPostImportWindow()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.External.ObservedTargetIdentity.SiteId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.StartedAtUtc = fixture.ImportAggregate.ClassicWikiReceipt.StartedAtUtc.AddYears(-1);
            fixture.External.CompletedAtUtc = fixture.External.StartedAtUtc.AddSeconds(2);
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.PreCaptureTargetReadback.ObservedAtUtc = fixture.Binding.TargetIdentityEvidence.ObservedAtUtc.AddSeconds(-1);
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.PostCaptureTargetReadback.ObservedAtUtc = fixture.Binding.CaptureExpiresAtUtc.AddSeconds(1);
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));
        }

        [TestMethod]
        public void BindingRequiresProviderObservedSourceAndTargetAndPlanWebIdentity()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.TargetStorageIdentity.WebId = Guid.Parse("90000000-0000-0000-0000-000000000002");
            fixture.Binding.TargetStorageIdentity.CanonicalUrl = NativeRuntimeTestFixture.CanonicalTarget(fixture.Binding.TargetStorageIdentity);
            fixture.Binding.TargetIdentityEvidence.Identity = NativePageRuntimeBindingValidator.CopyTarget(fixture.Binding.TargetStorageIdentity);
            fixture.Binding.TargetIdentityEvidence.Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                fixture.Binding.TargetIdentityEvidence.Identity,
                fixture.ArtifactStore,
                "application/vnd.pnp.target-identity+json");
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.SourceIdentityEvidence.ProviderId = null;
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.TargetIdentityEvidence.Artifact = null;
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());
        }

        [TestMethod]
        public void IndependentIdentityVerifierRejectsSameWebReplacementAndSourceListReplacement()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var replacedTarget = NativePageRuntimeBindingValidator.CopyTarget(fixture.Binding.TargetStorageIdentity);
            replacedTarget.SiteId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            replacedTarget.ListId = Guid.Parse("10000000-0000-0000-0000-000000000002");
            replacedTarget.ListItemETag = "\"target,foreign\"";
            var replacedEvidence = new NativePageRuntimeTargetIdentityEvidence
            {
                ObservationId = fixture.Binding.TargetIdentityEvidence.ObservationId,
                ObservedAtUtc = fixture.Binding.TargetIdentityEvidence.ObservedAtUtc,
                OperationId = fixture.Binding.TargetIdentityEvidence.OperationId,
                ProviderId = fixture.Binding.TargetIdentityEvidence.ProviderId,
                ProviderVersion = fixture.Binding.TargetIdentityEvidence.ProviderVersion,
                SourceArtifactSha256 = fixture.Binding.TargetIdentityEvidence.SourceArtifactSha256,
                Identity = replacedTarget,
                Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                    replacedTarget,
                    fixture.ArtifactStore,
                    "application/vnd.pnp.target-identity+json")
            };

            Assert.ThrowsException<InvalidDataException>(() => ClassicWikiRuntimeBindingFactory.CreatePreCapture(
                fixture.Binding.RunId,
                fixture.Package,
                fixture.Binding.SourceIdentityEvidence,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                replacedEvidence,
                NativeRuntimeTestFixture.ImportRef,
                NativeRuntimeTestFixture.ContractRef,
                fixture.ProvenanceManifest,
                fixture.ArtifactStore,
                fixture.IdentityEvidenceVerifier,
                fixture.Binding.IssuedAtUtc,
                fixture.Binding.CaptureExpiresAtUtc));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.SourceIdentity.ListId = Guid.Parse("10000000-0000-0000-0000-000000000003");
            fixture.Binding.SourceIdentityEvidence.Identity.ListId = fixture.Binding.SourceIdentity.ListId;
            fixture.Binding.SourceIdentityEvidence.Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                fixture.Binding.SourceIdentityEvidence.Identity,
                fixture.ArtifactStore,
                "application/vnd.pnp.source-identity+json");
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);

            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());
        }

        [TestMethod]
        public void Semantic200AccessDeniedCannotUseClaimedPass()
        {
            var fixture = NativeRuntimeTestFixture.Create(semanticDenial: true);

            fixture.ValidateExternal(out var status);

            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);
        }

        [TestMethod]
        public void WikiPolicyRecomputesAuthoredSurfaceDecodesDenialAndRequiresImageBytes()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body data-page='classic-wiki'>foreign content</body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out var status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><h1>Access&#32;Denied</h1><p>Sign&#32;in to continue</p></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body data-page='classic-wiki'>approved content</body></html>",
                fixture.CreateDom("approved content"),
                Encoding.UTF8.GetBytes("not an image"));
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><!--approved content--><main>Unrelated page</main></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><script>const unused='approved content';</script><main>Unrelated page</main></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><h1>Access <span>Denied</span></h1><p>approved content</p></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><h1>Access&nbsp;Denied</h1><p>approved content</p></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body>approved content</body></html>",
                fixture.CreateDom("approved content"),
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><p hidden>approved content</p><main>Unrelated page</main></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><p style='display:none'>approved content</p><main>Unrelated page</main></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><h1>Access<br>Denied</h1><p>approved content</p></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body><h1>Access</h1><h1>Denied</h1><p>approved content</p></body></html>",
                fixture.CreateDom("approved content"),
                NativeRuntimeTestFixture.PngBytes());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body>approved content</body></html>",
                fixture.CreateDom("approved content"),
                MutatePngHeader(NativeRuntimeTestFixture.PngBytes(), bitDepth: 8, colorType: 7));
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body>approved content</body></html>",
                fixture.CreateDom("approved content"),
                MutatePngHeader(NativeRuntimeTestFixture.PngBytes(), bitDepth: 0, colorType: 4));
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body>approved content</body></html>",
                fixture.CreateDom("approved content"),
                CorruptPngImageData(NativeRuntimeTestFixture.PngBytes()));
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);

            fixture = NativeRuntimeTestFixture.Create();
            fixture.ReplaceRuntimeArtifacts(
                "<html><body>approved content</body></html>",
                fixture.CreateDom("approved content"),
                JpegWithoutEntropyScan());
            fixture.ValidateExternal(out status);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);
        }

        [TestMethod]
        public void AttemptManifestAndNestedRuntimeWireMustCloseOverConsumedEvidence()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.External.Attempts[0].HttpStatusCode = 403;
            fixture.External.Attempts[0].SemanticResult = "access_denied";
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.Attempts[0].RawEvidence[0].Locator = "../escape";
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.ArtifactManifest.Artifacts = new List<ArtifactReference>
            {
                fixture.ArtifactStore.PutText("unrelated", "text/plain")
            };
            fixture.ResealManifestAndExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.RuntimeReceipt.SchemaVersion = "pnp-migration-runtime-verification-receipt/v2";
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.RuntimeReceipt.Status = (RuntimeVerificationStatus)999;
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.Attempts[0].SemanticResult = "access_denied";
            fixture.ResealExternal();
            fixture.ValidateExternal(out var semanticStatus);
            Assert.AreEqual(RuntimeVerificationStatus.Failed, semanticStatus);

            fixture = NativeRuntimeTestFixture.Create();
            var denial = fixture.ArtifactStore.PutText("<h1>Access Denied</h1>", "text/html");
            fixture.External.Attempts[0].RawEvidence[0] = new NativePageRuntimeArtifactReference
            {
                Sha256 = denial.Sha256,
                Length = denial.Length,
                MediaType = denial.MediaType,
                Locator = "runtime/denial.html"
            };
            fixture.External.ArtifactManifest.Artifacts.Add(denial);
            fixture.ResealManifestAndExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.External.Attempts[0].RequestIdAvailability = "invented-state";
            fixture.ResealExternal();
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateExternal(out _));
        }

        [TestMethod]
        public void ExistingJournalWriterPersistsOperationScopedVerificationEvidenceAndRecoversTail()
        {
            var directory = Path.Combine(Path.GetTempPath(), "ccd271-journal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "verification.jsonl");
                var reference = new MigrationExecutionArtifactReference
                {
                    OperationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    PlanDigest = NativeRuntimeTestFixture.Hash("plan"),
                    WrittenAtUtc = new DateTimeOffset(2026, 9, 10, 0, 20, 0, TimeSpan.Zero),
                    ArtifactKind = MigrationExecutionArtifactKind.VerificationEvidence,
                    ArtifactSchemaVersion = NativePageRuntimeContract.AcceptanceReceiptSchemaVersion,
                    Sha256 = NativeRuntimeTestFixture.Hash("acceptance"),
                    Length = 128,
                    MediaType = "application/vnd.pnp.native-runtime-acceptance+json"
                };
                using (var journal = new JsonLinesMigrationExecutionJournal(path))
                {
                    journal.WriteArtifactReference(reference);
                }
                var first = MigrationExecutionJournalReader.Read(path);
                Assert.AreEqual(1, first.Records.Count);
                Assert.AreEqual(MigrationExecutionJournalRecordKind.ArtifactReference, first.Records[0].RecordKind);
                Assert.IsNull(first.Records[0].ActionId);

                File.AppendAllText(path, "{\"partial\":");
                using (var journal = new JsonLinesMigrationExecutionJournal(path))
                {
                    reference = new MigrationExecutionArtifactReference
                    {
                        OperationId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        PlanDigest = NativeRuntimeTestFixture.Hash("plan-2"),
                        WrittenAtUtc = reference.WrittenAtUtc.AddSeconds(1),
                        ArtifactKind = reference.ArtifactKind,
                        ArtifactSchemaVersion = reference.ArtifactSchemaVersion,
                        Sha256 = NativeRuntimeTestFixture.Hash("acceptance-2"),
                        Length = reference.Length,
                        MediaType = reference.MediaType
                    };
                    journal.WriteArtifactReference(reference);
                }
                var recovered = MigrationExecutionJournalReader.Read(path);
                Assert.AreEqual(2, recovered.Records.Count);
                Assert.AreEqual(1, recovered.InterruptedTails.Count);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void RequiredScreenshotCannotBeOmitted()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var screenshot = fixture.External.RuntimeReceipt.Results.Single(value =>
                value.RequirementId == NativePageRuntimeContract.ScreenshotRequirementId);
            screenshot.ScreenshotArtifactSha256 = null;
            screenshot.ScreenshotArtifactLength = null;
            screenshot.ScreenshotArtifactLocator = null;
            fixture.ResealExternal();

            fixture.ValidateExternal(out var status);

            Assert.AreEqual(RuntimeVerificationStatus.Failed, status);
        }

        [TestMethod]
        public void ProvenanceDefaultIsSealedUnverifiedAndSelfReportIsNotAnAcceptanceInput()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var receipt = new UnverifiedProducerBuildProvenanceVerifier().Verify(fixture.ProvenanceManifest);

            Assert.AreEqual(ProducerBuildProvenanceContract.Unverified, receipt.VerificationStatus);
            Assert.AreEqual(
                fixture.ProvenanceManifest.ContentSha256,
                receipt.ManifestDigestSha256);
            Assert.AreEqual(64, ProducerBuildProvenanceContract.ValidateReceiptAndComputeDigest(
                receipt,
                fixture.ProvenanceManifest).Length);
        }

        [TestMethod]
        public void UnknownSchemaProfileExtensionAndBrokenSealFailClosed()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.SchemaVersion = "pnp-native-page-runtime-binding/v2";
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.ProfileId = "profile.unknown";
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.Extensions["not_namespaced"] = "value";
            NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());

            fixture = NativeRuntimeTestFixture.Create();
            fixture.Binding.ContentSha256 = NativeRuntimeTestFixture.Hash("broken-seal");
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateBinding());
        }

        [TestMethod]
        public void CanonicalExternalWireRoundTripsIntoConsumerAndRejectsWrongNestedTypes()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var json = MigrationContractSerializer.SerializeCanonical(fixture.External);
            var reopened = MigrationContractSerializer.Deserialize<ExternalPageRuntimeEvidence>(json);

            var digest = NativePageRuntimeBindingValidator.ValidateExternalEvidenceAndComputeDigest(
                reopened,
                fixture.Binding,
                new ClassicWikiRuntimeEvidencePolicy(),
                fixture.ArtifactStore,
                out var status);

            Assert.AreEqual(fixture.External.ContentSha256, digest);
            Assert.AreEqual(RuntimeVerificationStatus.Passed, status);
            var wrongType = json.Replace("\"passed\":true", "\"passed\":\"not-a-boolean\"");
            Assert.AreNotEqual(json, wrongType);
            Assert.ThrowsException<JsonException>(() =>
                MigrationContractSerializer.Deserialize<ExternalPageRuntimeEvidence>(wrongType));
        }

        [TestMethod]
        public void PermanentCounterexampleFixturesReopenInputsAndDriveExpectedOutcomes()
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Resources", "Migration", "NativeRuntimeBinding", "v1");
            var files = Directory.GetFiles(root, "*.case.json").OrderBy(value => value).ToArray();

            Assert.AreEqual(14, files.Length);
            foreach (var file in files)
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(file)))
                {
                    Assert.AreEqual("ccd263-conformance-fixture/v1", document.RootElement.GetProperty("fixtureSchemaVersion").GetString());
                    Assert.IsTrue(document.RootElement.TryGetProperty("caseId", out var caseId));
                    Assert.IsFalse(string.IsNullOrWhiteSpace(caseId.GetString()));
                    Assert.IsTrue(document.RootElement.GetProperty("synthetic").GetBoolean());
                    Assert.AreEqual(FixtureOriginEvidencePackageDigest, document.RootElement.GetProperty("originEvidencePackageDigest").GetString());
                    Assert.AreEqual(FixtureSourceVersionDigest, document.RootElement.GetProperty("sourceVersionDigest").GetString());
                    Assert.AreEqual(NativePageRuntimeContract.ClaimId, document.RootElement.GetProperty("claimId").GetString());
                    var recipe = document.RootElement.GetProperty("transformRecipe").GetString();
                    Assert.AreEqual(
                        MigrationDigest.ComputeSha256(recipe),
                        document.RootElement.GetProperty("transformRecipeDigest").GetString());
                    var inputs = document.RootElement.GetProperty("inputArtifacts");
                    Assert.AreEqual(1, inputs.GetArrayLength());
                    string fixtureInput = null;
                    foreach (var input in inputs.EnumerateArray())
                    {
                        Assert.AreEqual("fixture-input.txt", input.GetProperty("path").GetString());
                        Assert.AreEqual(FixtureInputDigest, input.GetProperty("sha256").GetString());
                        Assert.AreEqual(FixtureInputLength, input.GetProperty("length").GetInt64());
                        var path = Path.GetFullPath(Path.Combine(root, input.GetProperty("path").GetString()));
                        Assert.IsTrue(path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
                        var bytes = File.ReadAllBytes(path);
                        Assert.AreEqual(input.GetProperty("length").GetInt64(), bytes.LongLength);
                        Assert.AreEqual(input.GetProperty("sha256").GetString(), MigrationDigest.ComputeSha256(bytes));
                        fixtureInput = Encoding.UTF8.GetString(bytes);
                    }
                    using (var inputDocument = JsonDocument.Parse(fixtureInput))
                    {
                        Assert.AreEqual("ccd263-runtime-fixture-input/v1", inputDocument.RootElement.GetProperty("schemaVersion").GetString());
                        Assert.AreEqual(NativePageRuntimeContract.ClaimId, inputDocument.RootElement.GetProperty("claimId").GetString());
                        Assert.AreEqual(FixtureOriginEvidencePackageDigest, inputDocument.RootElement.GetProperty("originEvidencePackageDigest").GetString());
                        Assert.AreEqual(FixtureSourceVersionDigest, inputDocument.RootElement.GetProperty("sourceVersionDigest").GetString());
                        Assert.AreEqual(4, inputDocument.RootElement.EnumerateObject().Count());
                    }
                    AssertPermanentOutcome(recipe, caseId.GetString(), document.RootElement.GetProperty("expected"));
                }
            }
        }

        [TestMethod]
        public void SyntheticFixtureCreationIsDigestDeterministic()
        {
            var first = NativeRuntimeTestFixture.Create();
            var second = NativeRuntimeTestFixture.Create();

            Assert.AreEqual(first.Package.SnapshotDigest, second.Package.SnapshotDigest);
            Assert.AreEqual(first.Package.PlanDigest, second.Package.PlanDigest);
            Assert.AreEqual(first.AdmittedDigest, second.AdmittedDigest);
            Assert.AreEqual(first.ImportAggregate.ReceiptDigestSha256, second.ImportAggregate.ReceiptDigestSha256);
            Assert.AreEqual(first.Binding.ContentSha256, second.Binding.ContentSha256);
            Assert.AreEqual(first.External.ContentSha256, second.External.ContentSha256);
        }

        [TestMethod]
        public void UnsupportedPermanentFixtureRecipeIsRejectedBeforeConstruction()
        {
            using (var document = JsonDocument.Parse("{\"runtimeStatus\":\"Passed\"}"))
            {
                Assert.ThrowsException<AssertFailedException>(() => AssertPermanentOutcome(
                    "ccd385-unimplemented-transform:exact-match",
                    "exact-match",
                    document.RootElement));
                Assert.ThrowsException<AssertFailedException>(() => AssertPermanentOutcome(
                    "case:unimplemented-case-ccd421",
                    "unimplemented-case-ccd421",
                    document.RootElement));
            }
        }

        private static void AssertPermanentOutcome(string recipe, string caseId, JsonElement expected)
        {
            Assert.IsTrue(SupportedPermanentFixtureRecipes.Contains(recipe), recipe + " is not a supported permanent fixture recipe.");
            Assert.AreEqual("case:" + caseId, recipe, caseId + " uses an unsupported transform recipe.");
            var fixture = NativeRuntimeTestFixture.Create(
                runtimePassed: !string.Equals(caseId, "access-denied-terminal", StringComparison.Ordinal),
                semanticDenial: string.Equals(caseId, "http-200-denial", StringComparison.Ordinal));
            string bindingStatus = NativePageRuntimeContract.BindingValid;
            RuntimeVerificationStatus runtimeStatus = RuntimeVerificationStatus.Pending;
            MigrationAcceptanceStatus acceptanceStatus = MigrationAcceptanceStatus.Pending;
            string provenanceStatus = ProducerBuildProvenanceContract.Unverified;
            string diagnostic = null;
            try
            {
                switch (recipe)
                {
                    case "case:capture-before-import":
                        fixture.External.StartedAtUtc = fixture.ImportAggregate.ClassicWikiReceipt.StartedAtUtc.AddMinutes(-1);
                        fixture.External.CompletedAtUtc = fixture.External.StartedAtUtc.AddSeconds(1);
                        fixture.ResealExternal();
                        fixture.ValidateExternal(out runtimeStatus);
                        break;
                    case "case:consistent-unobserved-target":
                        fixture.External.ObservedTargetIdentity.SiteId = Guid.Parse("10000000-0000-0000-0000-000000000001");
                        fixture.ResealExternal();
                        fixture.ValidateExternal(out runtimeStatus);
                        break;
                    case "case:stale-source-version":
                        fixture.Binding.SourceVersion.VersionDigestSha256 = NativeRuntimeTestFixture.Hash("stale-source");
                        NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
                        fixture.ValidateBinding();
                        break;
                    case "case:weakened-manifest":
                        fixture.Binding.RequirementsManifest.Requirements[0].Required = false;
                        NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
                        fixture.ValidateBinding();
                        break;
                    case "case:wrong-operation":
                        fixture.Binding.Operations.RuntimeOperationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
                        NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
                        fixture.ValidateBinding();
                        break;
                    case "case:wrong-target-file":
                        fixture.Binding.TargetStorageIdentity.FileUniqueId = Guid.Parse("99999999-9999-9999-9999-999999999999");
                        NativePageRuntimeBindingValidator.SealBinding(fixture.Binding);
                        fixture.ValidateBinding();
                        break;
                    case "case:access-denied-terminal":
                    case "case:custom-report-only":
                    case "case:exact-match":
                    case "case:http-200-denial":
                    case "case:missing-provenance":
                    case "case:pending-preservation":
                    case "case:policy-cannot-upgrade-pending":
                    case "case:self-authored-verified":
                        var external = caseId == "custom-report-only"
                            || caseId == "policy-cannot-upgrade-pending"
                            || caseId == "pending-preservation"
                            ? null
                            : fixture.External;
                        IProducerBuildProvenanceVerifier verifier = caseId == "missing-provenance"
                            || caseId == "pending-preservation"
                            ? null
                            : caseId == "self-authored-verified"
                                ? new SelfAuthoredVerifiedProvenanceVerifier()
                                : new VerifiedTestProducerBuildProvenanceVerifier();
                        var policy = caseId == "policy-cannot-upgrade-pending"
                            ? (INativePageRuntimeEvidencePolicy)new AlwaysTrueRuntimePolicy()
                            : new ClassicWikiRuntimeEvidencePolicy();
                        var receipt = EvaluateFixture(fixture, external, verifier, policy);
                        bindingStatus = receipt.BindingValidationStatus;
                        runtimeStatus = receipt.RuntimeVerificationStatus;
                        acceptanceStatus = receipt.AcceptanceStatus;
                        provenanceStatus = receipt.ProvenanceStatus;
                        diagnostic = string.Join(";", receipt.ReasonCodes);
                        break;
                    default:
                        Assert.Fail(recipe + " was admitted without an explicit fixture construction branch.");
                        break;
                }
            }
            catch (InvalidDataException exception)
            {
                bindingStatus = NativePageRuntimeContract.BindingInvalid;
                diagnostic = exception.Message;
            }

            foreach (var property in expected.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "bindingStatus": Assert.AreEqual(property.Value.GetString(), bindingStatus, caseId); break;
                    case "runtimeStatus": Assert.AreEqual(property.Value.GetString(), runtimeStatus.ToString(), caseId); break;
                    case "acceptanceStatus": Assert.AreEqual(property.Value.GetString(), acceptanceStatus.ToString(), caseId); break;
                    case "provenanceStatus": Assert.AreEqual(property.Value.GetString(), provenanceStatus, caseId); break;
                    case "httpStatus": Assert.AreEqual(property.Value.GetInt32(), fixture.External.RuntimeReceipt.Results[0].Http.StatusCode, caseId); break;
                    case "semanticDetector": Assert.AreEqual(property.Value.GetString(), fixture.External.Attempts[0].SemanticResult, caseId); break;
                    case "diagnostic": Assert.IsTrue((diagnostic ?? string.Empty).IndexOf(property.Value.GetString(), StringComparison.OrdinalIgnoreCase) >= 0, caseId + ": " + diagnostic); break;
                    default: Assert.Fail(caseId + " contains an unsupported expected field: " + property.Name); break;
                }
            }
        }

        private static byte[] MutatePngHeader(byte[] source, byte bitDepth, byte colorType)
        {
            var bytes = source.ToArray();
            bytes[24] = bitDepth;
            bytes[25] = colorType;
            var crc = ComputeTestPngCrc(bytes, 12, 17);
            bytes[29] = (byte)(crc >> 24);
            bytes[30] = (byte)(crc >> 16);
            bytes[31] = (byte)(crc >> 8);
            bytes[32] = (byte)crc;
            return bytes;
        }

        private static uint ComputeTestPngCrc(byte[] bytes, int offset, int count)
        {
            var crc = 0xffffffffu;
            for (var index = 0; index < count; index++)
            {
                crc ^= bytes[offset + index];
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
                }
            }
            return crc ^ 0xffffffffu;
        }

        private static byte[] CorruptPngImageData(byte[] source)
        {
            var bytes = source.ToArray();
            var offset = 8;
            while (offset + 12 <= bytes.Length)
            {
                var length = bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
                if (Encoding.ASCII.GetString(bytes, offset + 4, 4) == "IDAT")
                {
                    for (var index = 0; index < length; index++)
                    {
                        bytes[offset + 8 + index] = 0;
                    }
                    var crc = ComputeTestPngCrc(bytes, offset + 4, length + 4);
                    bytes[offset + 8 + length] = (byte)(crc >> 24);
                    bytes[offset + 9 + length] = (byte)(crc >> 16);
                    bytes[offset + 10 + length] = (byte)(crc >> 8);
                    bytes[offset + 11 + length] = (byte)crc;
                    return bytes;
                }
                offset += length + 12;
            }
            Assert.Fail("The PNG control does not contain IDAT data.");
            return null;
        }

        private static byte[] JpegWithoutEntropyScan()
        {
            var bytes = new List<byte> { 0xff, 0xd8, 0xff, 0xdb, 0x00, 0x43, 0x00 };
            bytes.AddRange(Enumerable.Repeat((byte)1, 64));
            bytes.AddRange(new byte[]
            {
                0xff, 0xc0, 0x00, 0x0b, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
                0xff, 0xc4, 0x00, 0x14, 0x00,
                0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0xff, 0xda, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
                0xff, 0xd9
            });
            return bytes.ToArray();
        }

        private static NativePageRuntimeAcceptanceReceipt EvaluateFixture(
            NativeRuntimeTestFixture fixture,
            ExternalPageRuntimeEvidence external,
            IProducerBuildProvenanceVerifier verifier,
            INativePageRuntimeEvidencePolicy policy)
        {
            return ClassicWikiNativeRuntimeAcceptance.Evaluate(
                fixture.Package,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Binding,
                external,
                fixture.ArtifactStore,
                policy,
                fixture.ProvenanceManifest,
                verifier,
                fixture.IdentityEvidenceVerifier,
                "ccd.native-runtime-evaluator",
                NativeRuntimeTestFixture.ContractRef,
                new DateTimeOffset(2026, 9, 10, 0, 20, 0, TimeSpan.Zero));
        }
    }

    internal sealed class NativeRuntimeTestFixture
    {
        public static readonly Guid RunId = Guid.Parse("77fa33e6-9814-434d-bd7f-919747fe3f60");
        public static readonly Guid SourceListId = Guid.Parse("3fed0145-2216-40a0-a29c-56104f25fc7d");
        public static readonly Guid TargetListId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public static readonly string ContractRef = new string('a', 40);
        public static readonly string ImportRef = new string('b', 40);
        public static readonly string CaptureRef = new string('c', 40);

        public ClassicWikiMigrationPackage Package { get; private set; }
        public AdmittedReproExecutionPlan AdmittedPlan { get; private set; }
        public string AdmittedDigest { get; private set; }
        public NativePageImportReceiptAggregate ImportAggregate { get; private set; }
        public NativePageRuntimeTargetIdentity Target { get; private set; }
        public ProducerBuildProvenanceManifest ProvenanceManifest { get; private set; }
        public INativePageRuntimeIdentityEvidenceVerifier IdentityEvidenceVerifier { get; private set; }
        public NativePageRuntimeBinding Binding { get; set; }
        public ExternalPageRuntimeEvidence External { get; set; }
        public MemoryArtifactStore ArtifactStore { get; private set; }

        public static NativeRuntimeTestFixture Create(bool runtimePassed = true, bool semanticDenial = false, bool exclusions = false)
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage(119, deferredField: exclusions);
            MakePackageDeterministic(package);
            var targetUrl = new Uri(package.Plan.TargetLocation.TargetWebUrl).GetLeftPart(UriPartial.Authority)
                + package.Plan.TargetPageServerRelativeUrl;
            var admittedPlan = new AdmittedReproExecutionPlan
            {
                PlanDigest = package.PlanDigest,
                TargetIdentity = targetUrl,
                SourceVersion = new CurrentSourceVersionIdentity
                {
                    IdentityDigestSha256 = Hash("source-identity"),
                    VersionDigestSha256 = Hash("source-version"),
                    ETag = "\"source,3\"",
                    LastModifiedUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
                    VersionLabel = "3.0",
                    ObservedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 1, 0, TimeSpan.Zero)
                },
                Operations = new ReproOperationIds
                {
                    MutationOperationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    ReadbackOperationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    RuntimeOperationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    CleanupOperationId = Guid.Parse("44444444-4444-4444-4444-444444444444")
                }
            };
            var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                targetUrl);
            var importStarted = new DateTimeOffset(2026, 9, 10, 0, 2, 0, TimeSpan.Zero);
            var importReceipt = new ClassicWikiImportReceipt
            {
                StartedAtUtc = importStarted,
                CompletedAtUtc = importStarted.AddSeconds(5),
                OperationId = admittedPlan.Operations.MutationOperationId,
                AdmittedPlanDigestSha256 = admittedDigest,
                SourceVersion = admittedPlan.SourceVersion,
                Operations = admittedPlan.Operations,
                ExecutionStatus = MigrationExecutionStatus.Succeeded,
                MutationStarted = true,
                Steps = new List<MigrationMutationReceipt>
                {
                    new MigrationMutationReceipt
                    {
                        OperationId = admittedPlan.Operations.MutationOperationId,
                        PlanDigest = package.PlanDigest,
                        ActionId = "classic-wiki.apply:ccd271",
                        Sequence = 0,
                        CompletedAtUtc = importStarted.AddSeconds(2),
                        Outcome = MutationOutcome.Applied
                    }
                },
                ApprovedPlanDigest = package.PlanDigest,
                TargetWebUrl = package.Plan.TargetLocation.TargetWebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                TargetFileUniqueId = Guid.Parse("4881af85-37b6-4ae7-b307-df93c52994a7"),
                TargetListItemId = 2,
                TargetVersionLabel = "1.0",
                StoredWikiFieldSha256 = package.Plan.WikiFieldPlan.ExpectedStoredSha256,
                FreshReadbackPassed = true,
                StorageVerificationStatus = StorageVerificationStatus.Passed,
                RuntimeVerificationStatus = RuntimeVerificationStatus.Pending,
                AcceptanceStatus = MigrationAcceptanceStatus.Pending
            };
            var aggregate = NativePageImportReceiptAggregateFactory.Create(importReceipt);
            var target = new NativePageRuntimeTargetIdentity
            {
                SiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                WebId = package.Plan.TargetLocation.TargetWebId,
                ListId = TargetListId,
                WebUrl = importReceipt.TargetWebUrl,
                PageServerRelativeUrl = importReceipt.TargetPageServerRelativeUrl,
                FileUniqueId = importReceipt.TargetFileUniqueId,
                ListItemId = importReceipt.TargetListItemId,
                ListItemVersion = importReceipt.TargetVersionLabel,
                ListItemETag = "\"target,1\"",
                CanonicalUrl = targetUrl
            };
            var store = new MemoryArtifactStore();
            var provenance = CreateProvenanceManifest(store);
            var sourceIdentity = new NativePageRuntimeSourceIdentity
            {
                SiteId = package.Snapshot.Source.SiteId,
                WebId = package.Snapshot.Source.WebId,
                ListId = SourceListId,
                ListItemId = package.Snapshot.Source.ListItemId,
                FileUniqueId = package.Snapshot.Source.FileUniqueId,
                PageServerRelativeUrl = package.Snapshot.Source.PageServerRelativeUrl
            };
            var sourceIdentityEvidence = new NativePageRuntimeSourceIdentityEvidence
            {
                ObservationId = "typed-package-source-identity",
                ObservedAtUtc = admittedPlan.SourceVersion.ObservedAtUtc.AddSeconds(-1),
                OperationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                AcquisitionMethod = "typed-package-source-identity",
                ProviderId = "pnp-framework.classic-wiki-package",
                ProviderVersion = "v1",
                SourceVersionDigestSha256 = admittedPlan.SourceVersion.VersionDigestSha256,
                Identity = sourceIdentity,
                Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                    sourceIdentity,
                    store,
                    "application/vnd.pnp.source-identity+json")
            };
            var targetEvidence = new NativePageRuntimeTargetIdentityEvidence
            {
                ObservationId = "fresh-import-readback",
                ObservedAtUtc = importReceipt.CompletedAtUtc.AddSeconds(1),
                OperationId = admittedPlan.Operations.ReadbackOperationId,
                ProviderId = "pnp-framework.classic-wiki-fresh-readback",
                ProviderVersion = "v1",
                SourceArtifactSha256 = aggregate.ReceiptDigestSha256,
                Identity = NativePageRuntimeBindingValidator.CopyTarget(target),
                Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                    target,
                    store,
                    "application/vnd.pnp.target-identity+json")
            };
            var identityEvidenceVerifier = new VerifiedTestNativePageRuntimeIdentityEvidenceVerifier(
                sourceIdentity,
                target,
                ContractRef);
            var binding = ClassicWikiRuntimeBindingFactory.CreatePreCapture(
                RunId,
                package,
                sourceIdentityEvidence,
                admittedPlan,
                admittedDigest,
                aggregate,
                targetEvidence,
                ImportRef,
                ContractRef,
                provenance,
                store,
                identityEvidenceVerifier,
                importReceipt.CompletedAtUtc.AddSeconds(2),
                importReceipt.CompletedAtUtc.AddMinutes(5));

            var fixture = new NativeRuntimeTestFixture
            {
                Package = package,
                AdmittedPlan = admittedPlan,
                AdmittedDigest = admittedDigest,
                ImportAggregate = aggregate,
                Target = target,
                ProvenanceManifest = provenance,
                IdentityEvidenceVerifier = identityEvidenceVerifier,
                Binding = binding,
                ArtifactStore = store
            };
            fixture.External = fixture.CreateExternal(runtimePassed, semanticDenial);
            return fixture;
        }

        public string ValidateBinding()
        {
            return NativePageRuntimeBindingValidator.ValidateBindingAndComputeDigest(
                Binding,
                Package,
                AdmittedPlan,
                AdmittedDigest,
                ImportAggregate,
                ProvenanceManifest,
                ArtifactStore,
                IdentityEvidenceVerifier);
        }

        public string ValidateExternal(out RuntimeVerificationStatus status)
        {
            return NativePageRuntimeBindingValidator.ValidateExternalEvidenceAndComputeDigest(
                External,
                Binding,
                new ClassicWikiRuntimeEvidencePolicy(),
                ArtifactStore,
                out status);
        }

        public void ResealExternal()
        {
            if (External.RuntimeReceipt != null)
            {
                External.RuntimeReceiptDigestSha256 = MigrationDigest.ComputeSha256(
                    MigrationContractSerializer.SerializeCanonical(External.RuntimeReceipt));
            }
            NativePageRuntimeBindingValidator.SealExternalEvidence(External);
        }

        public void ResealManifestAndExternal()
        {
            External.ArtifactManifest.ContentSha256 = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    External.ArtifactManifest,
                    nameof(MigrationArtifactManifest.ContentSha256)));
            ResealExternal();
        }

        public string CreateDom(string authoredContent)
        {
            return JsonSerializer.Serialize(new
            {
                schemaVersion = "pnp-classic-wiki-runtime-dom/v1",
                surface = "classic-wiki",
                readyState = "complete",
                errorShell = false,
                observedUrl = Target.CanonicalUrl,
                authoredContent
            });
        }

        public void ReplaceRuntimeArtifacts(string htmlText, string domJson, byte[] screenshotBytes)
        {
            var html = ArtifactStore.PutText(htmlText, "text/html");
            var dom = ArtifactStore.PutText(domJson, "application/json");
            var screenshot = ArtifactStore.PutBytes(screenshotBytes, "image/png");
            foreach (var result in External.RuntimeReceipt.Results)
            {
                result.EvidenceArtifactSha256 = html.Sha256;
                result.EvidenceArtifactLength = html.Length;
                result.DomProbeArtifactSha256 = dom.Sha256;
                result.DomProbeArtifactLength = dom.Length;
                result.ScreenshotArtifactSha256 = screenshot.Sha256;
                result.ScreenshotArtifactLength = screenshot.Length;
            }
            External.Attempts[0].RawEvidence = new List<NativePageRuntimeArtifactReference>
            {
                ToNative(html, "runtime/page.html"),
                ToNative(dom, "runtime/dom-probe.json"),
                ToNative(screenshot, "runtime/screenshot.png")
            };
            External.ArtifactManifest.Artifacts = new List<ArtifactReference>
            {
                html,
                dom,
                screenshot,
                ToArtifact(External.PreCaptureTargetReadback.Artifact)
            };
            ResealManifestAndExternal();
        }

        public ExternalPageRuntimeEvidence CreateExternal(bool runtimePassed, bool semanticDenial)
        {
            var started = Binding.CaptureNotBeforeUtc.AddSeconds(2);
            var html = ArtifactStore.PutText(semanticDenial
                ? "<html><body>Access Denied; Sign in to continue</body></html>"
                : "<html><body data-page='classic-wiki'>approved content</body></html>", "text/html");
            var authoredContent = Package.Plan.WikiFieldPlan.ExactValue;
            var dom = ArtifactStore.PutText(JsonSerializer.Serialize(new
            {
                schemaVersion = "pnp-classic-wiki-runtime-dom/v1",
                surface = "classic-wiki",
                readyState = "complete",
                errorShell = semanticDenial,
                observedUrl = Target.CanonicalUrl,
                authoredContent
            }), "application/json");
            var screenshot = ArtifactStore.PutBytes(PngBytes(), "image/png");
            var browser = new RuntimeBrowserContextIdentity
            {
                BrowserProduct = "Edge",
                BrowserVersion = "140.0.0.0",
                ProtocolVersion = "1.3",
                ProfileIdentitySha256 = Hash("browser-profile"),
                BrowserContextId = "context-ccd271",
                TargetId = "target-ccd271",
                FreshContext = true,
                IsIncognito = true,
                CreatedAtUtc = started,
                FirstNavigationAtUtc = started.AddSeconds(1)
            };
            var completed = started.AddSeconds(3);
            var results = Binding.RequirementsManifest.Requirements.Select((requirement, index) =>
                new RuntimeVerificationResult
                {
                    RequirementId = requirement.Id,
                    Passed = runtimePassed,
                    EvidenceArtifactSha256 = html.Sha256,
                    EvidenceArtifactLength = html.Length,
                    EvidenceArtifactLocator = "runtime/page.html",
                    ImplementationRef = ContractRef,
                    BrowserContextId = browser.BrowserContextId,
                    Http = new RuntimeHttpEvidence
                    {
                        RequestedUrl = Target.CanonicalUrl,
                        FinalUrl = Target.CanonicalUrl,
                        Method = "GET",
                        StatusCode = runtimePassed ? 200 : 403,
                        ContentType = "text/html; charset=utf-8",
                        RequestId = "request-ccd271",
                        SharePointRequestGuid = "sprequest-ccd271",
                        ResponseHeadersDigestSha256 = Hash("headers"),
                        EncodedDataLength = html.Length,
                        CapturedAtUtc = started.AddSeconds(2)
                    },
                    Cache = new RuntimeCacheEvidence
                    {
                        RequestMode = "no-store",
                        CacheDisabled = true,
                        RequestCacheControl = "no-store",
                        RequestPragma = "no-cache",
                        FromDiskCache = false,
                        FromServiceWorker = false
                    },
                    DomProbeArtifactSha256 = dom.Sha256,
                    DomProbeArtifactLength = dom.Length,
                    DomProbeArtifactLocator = "runtime/dom-probe.json",
                    ScreenshotArtifactSha256 = screenshot.Sha256,
                    ScreenshotArtifactLength = screenshot.Length,
                    ScreenshotArtifactLocator = "runtime/screenshot.png",
                    Message = runtimePassed ? "claimed pass" : "access denied terminal observation"
                }).ToList();
            var runtimeReceipt = RuntimeVerificationReceiptFactory.Create(
                AdmittedPlan,
                AdmittedDigest,
                ImportAggregate.ReceiptDigestSha256,
                Target.CanonicalUrl,
                Binding.RequirementsManifest,
                ContractRef,
                browser,
                results,
                completed);
            var pre = CreateTargetReadback("browser-pre-capture", started.AddSeconds(-1));
            var post = CreateTargetReadback("browser-post-capture", completed.AddSeconds(1));
            var artifactManifest = new MigrationArtifactManifest
            {
                Artifacts = new List<ArtifactReference>
                {
                    html,
                    dom,
                    screenshot,
                    ToArtifact(pre.Artifact)
                }
            };
            artifactManifest.ContentSha256 = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    artifactManifest,
                    nameof(MigrationArtifactManifest.ContentSha256)));
            var external = new ExternalPageRuntimeEvidence
            {
                RunId = Binding.RunId,
                ClaimId = Binding.ClaimId,
                BindingDigestSha256 = Binding.ContentSha256,
                CaptureProducer = new NativePageRuntimeCaptureProducer
                {
                    AdapterId = "ccd.browser-runtime",
                    Version = "1.0.0",
                    ImplementationRef = CaptureRef,
                    ToolVersion = "Edge/140",
                    ProtocolVersion = "CDP/1.3"
                },
                ProfileClass = Binding.ProfileId,
                StartedAtUtc = started,
                CompletedAtUtc = completed,
                ResultKind = NativePageRuntimeContract.RuntimeResultKind,
                ObservedTargetIdentity = NativePageRuntimeBindingValidator.CopyTarget(Target),
                PreCaptureTargetReadback = pre,
                PostCaptureTargetReadback = post,
                Attempts = new List<NativePageRuntimeAttempt>
                {
                    new NativePageRuntimeAttempt
                    {
                        Sequence = 1,
                        MaximumAttempts = 1,
                        ObservedAtUtc = started.AddSeconds(2),
                        RunId = Binding.RunId,
                        RuntimeOperationId = Binding.Operations.RuntimeOperationId,
                        RequestedUrl = Target.CanonicalUrl,
                        FinalUrl = Target.CanonicalUrl,
                        HttpStatusCode = runtimePassed ? 200 : 403,
                        RequestId = "request-ccd271",
                        RequestIdAvailability = "available",
                        ProviderId = "edge-cdp",
                        ProviderVersion = "140",
                        SemanticDetectorId = "classic-wiki-surface",
                        SemanticDetectorVersion = "v1",
                        SemanticDetectorDigestSha256 = Hash("classic-wiki-surface-v1"),
                        SemanticResult = semanticDenial ? "access_denied" : runtimePassed ? "surface_present" : "forbidden",
                        BrowserContextId = browser.BrowserContextId,
                        RawEvidence = new List<NativePageRuntimeArtifactReference>
                        {
                            ToNative(html, "runtime/page.html"),
                            ToNative(dom, "runtime/dom-probe.json"),
                            ToNative(screenshot, "runtime/screenshot.png")
                        }
                    }
                },
                ArtifactManifest = artifactManifest,
                RuntimeReceipt = runtimeReceipt,
                RuntimeReceiptDigestSha256 = MigrationDigest.ComputeSha256(
                    MigrationContractSerializer.SerializeCanonical(runtimeReceipt))
            };
            NativePageRuntimeBindingValidator.SealExternalEvidence(external);
            return external;
        }

        public static ProducerBuildProvenanceManifest CreateProvenanceManifest(MemoryArtifactStore store)
        {
            var restoreLog = ToNative(store.PutText("restore log", "text/plain"));
            var buildLog = ToNative(store.PutText("build log", "text/plain"));
            var producer = store.PutText("producer binary", "application/octet-stream");
            var framework = store.PutText("framework binary", "application/octet-stream");
            var manifest = new ProducerBuildProvenanceManifest
            {
                ProducerId = "ccd153-native-producer",
                ProducerVersion = "1.0.0",
                SubjectImplementationRef = ContractRef,
                RepositoryIdentity = "pnpframework",
                TreeId = new string('d', 40),
                CleanSourceReceiptDigestSha256 = Hash("clean-source"),
                BuildDriverImplementationRef = ContractRef,
                SourceArtifactDigestsSha256 = new List<string> { Hash("source") },
                InputArtifactDigestsSha256 = new List<string> { Hash("input") },
                Toolchain = new ProducerBuildToolchain
                {
                    SdkVersion = "10.0.400",
                    MsBuildVersion = "18.9.6",
                    RuntimeVersion = "10.0.0",
                    OperatingSystem = "Windows",
                    RuntimeIdentifier = "win-x64",
                    TargetFramework = "net10.0",
                    Configuration = "Debug"
                },
                RestoreSourceIdentities = new List<string> { "nuget.org" },
                LockedDependencyGraphSha256 = Hash("locked-dependencies"),
                PackageHashesSha256 = new Dictionary<string, string> { ["PnP.Core"] = Hash("pnp-core-package") },
                Command = new ProducerBuildCommand
                {
                    Executable = "dotnet",
                    Arguments = new List<string> { "build", "ccd153-native-producer.csproj", "--locked-mode" },
                    NonSecretProperties = new Dictionary<string, string> { ["Configuration"] = "Debug" }
                },
                StartedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
                CompletedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 5, TimeSpan.Zero),
                ExitCode = 0,
                Result = "Succeeded",
                RestoreLog = restoreLog,
                BuildLog = buildLog,
                Outputs = new List<ProducerBuildArtifact>
                {
                    new ProducerBuildArtifact { Role = "producer", Path = "ccd153-native-producer.dll", Length = producer.Length, Sha256 = producer.Sha256 }
                },
                RuntimeLoadClosure = new List<ProducerBuildArtifact>
                {
                    new ProducerBuildArtifact { Role = "producer", Path = "ccd153-native-producer.dll", Length = producer.Length, Sha256 = producer.Sha256 },
                    new ProducerBuildArtifact { Role = "framework", Path = "PnP.Framework.dll", Length = framework.Length, Sha256 = framework.Sha256 }
                },
                RequestedHistoricalBinarySha256 = Hash("historical-producer"),
                SchemaCompatibilitySet = new List<string>
                {
                    NativePageRuntimeContract.BindingSchemaVersion,
                    NativePageRuntimeContract.ExternalEvidenceSchemaVersion,
                    NativePageRuntimeContract.AcceptanceReceiptSchemaVersion
                }
            };
            ProducerBuildProvenanceContract.SealManifest(manifest);
            return manifest;
        }

        public static string Hash(string value) => MigrationDigest.ComputeSha256(value);

        public static string CanonicalTarget(NativePageRuntimeTargetIdentity target)
        {
            return new Uri(target.WebUrl).GetLeftPart(UriPartial.Authority).TrimEnd('/')
                + "/"
                + target.PageServerRelativeUrl.TrimStart('/');
        }

        public static byte[] PngBytes()
        {
            return Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        }

        private NativePageRuntimeTargetIdentityEvidence CreateTargetReadback(string id, DateTimeOffset observedAt)
        {
            var identity = NativePageRuntimeBindingValidator.CopyTarget(Target);
            return new NativePageRuntimeTargetIdentityEvidence
            {
                ObservationId = id,
                ObservedAtUtc = observedAt,
                OperationId = Binding.Operations.RuntimeOperationId,
                ProviderId = "edge-cdp.sharepoint-readback",
                ProviderVersion = "v1",
                SourceArtifactSha256 = Binding.ContentSha256,
                Identity = identity,
                Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                    identity,
                    ArtifactStore,
                    "application/vnd.pnp.target-identity+json")
            };
        }

        private static NativePageRuntimeArtifactReference ToNative(ArtifactReference value, string locator = null)
        {
            return new NativePageRuntimeArtifactReference
            {
                Sha256 = value.Sha256,
                Length = value.Length,
                MediaType = value.MediaType,
                Locator = locator ?? value.Sha256.Substring(0, 2) + "/" + value.Sha256
            };
        }

        private static ArtifactReference ToArtifact(NativePageRuntimeArtifactReference value)
        {
            return new ArtifactReference
            {
                Sha256 = value.Sha256,
                Length = value.Length,
                MediaType = value.MediaType
            };
        }

        private static void MakePackageDeterministic(ClassicWikiMigrationPackage package)
        {
            package.PlannedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
            package.ExportedAtUtc = package.PlannedAtUtc;
            package.Snapshot.Source.SiteId = Guid.Parse("c37b3679-4601-4a1d-a0f8-c8ed3ee477f2");
            package.Snapshot.Source.WebId = Guid.Parse("041e70b3-0d2d-4040-91a3-1f57c3b9df53");
            package.Snapshot.Source.FileUniqueId = Guid.Parse("c3b2c2bb-663d-47ed-8562-840c9fd685fb");
            package.Snapshot.Source.ListItemId = 2;
            package.Snapshot.Source.VersionLabel = "3.0";
            package.Snapshot.Lifecycle.CreatedUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            package.Snapshot.Lifecycle.ModifiedUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
            package.Snapshot.IngredientGraph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode
                    {
                        Id = "node:runtime",
                        Kind = PageIngredientKind.Runtime,
                        Label = "Page Runtime",
                        RuntimeRequirement = PageRuntimeAdapterIds.Wiki
                    }
                }
            };
            package.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
        }
    }

    internal sealed class VerifiedTestProducerBuildProvenanceVerifier : IProducerBuildProvenanceVerifier
    {
        public ProducerBuildProvenanceReceipt Verify(ProducerBuildProvenanceManifest manifest)
        {
            var receipt = new ProducerBuildProvenanceReceipt
            {
                ManifestDigestSha256 = manifest.ContentSha256,
                VerifierId = "ccd.test.trusted-provenance-verifier",
                VerifierImplementationRef = new string('e', 40),
                VerifiedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 10, 0, TimeSpan.Zero),
                SourceBindingStatus = ProducerBuildProvenanceContract.Verified,
                ArtifactHashStatus = ProducerBuildProvenanceContract.Verified,
                RebuildStatus = ProducerBuildProvenanceContract.Verified,
                HistoricalBinaryMatchStatus = ProducerBuildProvenanceContract.Verified,
                VerificationStatus = ProducerBuildProvenanceContract.Verified,
                ActualArtifacts = manifest.Outputs.ToList(),
                OpenedBytes = new List<NativePageRuntimeArtifactReference> { manifest.BuildLog },
                RebuildEvidence = new List<NativePageRuntimeArtifactReference> { manifest.RestoreLog },
                ReasonCodes = new List<string> { "ALL_APPLICABLE_GATES_VERIFIED" }
            };
            ProducerBuildProvenanceContract.SealReceipt(receipt);
            return receipt;
        }
    }

    internal sealed class VerifiedTestNativePageRuntimeIdentityEvidenceVerifier : INativePageRuntimeIdentityEvidenceVerifier
    {
        private readonly NativePageRuntimeSourceIdentity expectedSource;
        private readonly NativePageRuntimeTargetIdentity expectedTarget;

        public VerifiedTestNativePageRuntimeIdentityEvidenceVerifier(
            NativePageRuntimeSourceIdentity expectedSource,
            NativePageRuntimeTargetIdentity expectedTarget,
            string implementationRef)
        {
            this.expectedSource = new NativePageRuntimeSourceIdentity
            {
                SiteId = expectedSource.SiteId,
                WebId = expectedSource.WebId,
                ListId = expectedSource.ListId,
                ListItemId = expectedSource.ListItemId,
                FileUniqueId = expectedSource.FileUniqueId,
                PageServerRelativeUrl = expectedSource.PageServerRelativeUrl
            };
            this.expectedTarget = NativePageRuntimeBindingValidator.CopyTarget(expectedTarget);
            ImplementationRef = implementationRef;
        }

        public string VerifierId => "ccd.test.independent-identity-verifier";

        public string ImplementationRef { get; }

        public void Verify(
            NativePageRuntimeSourceIdentityEvidence sourceEvidence,
            NativePageRuntimeTargetIdentityEvidence targetEvidence,
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan,
            NativePageImportReceiptAggregate importAggregate)
        {
            if (sourceEvidence?.Identity == null
                || targetEvidence?.Identity == null
                || !SameSource(sourceEvidence.Identity, expectedSource)
                || !SameTarget(targetEvidence.Identity, expectedTarget))
            {
                throw new InvalidDataException("Independent source/target identity verification rejected the observation.");
            }
        }

        private static bool SameSource(NativePageRuntimeSourceIdentity left, NativePageRuntimeSourceIdentity right)
        {
            return left.SiteId == right.SiteId
                && left.WebId == right.WebId
                && left.ListId == right.ListId
                && left.ListItemId == right.ListItemId
                && left.FileUniqueId == right.FileUniqueId
                && string.Equals(left.PageServerRelativeUrl, right.PageServerRelativeUrl, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameTarget(NativePageRuntimeTargetIdentity left, NativePageRuntimeTargetIdentity right)
        {
            return left.SiteId == right.SiteId
                && left.WebId == right.WebId
                && left.ListId == right.ListId
                && left.FileUniqueId == right.FileUniqueId
                && left.ListItemId == right.ListItemId
                && string.Equals(left.ListItemVersion, right.ListItemVersion, StringComparison.Ordinal)
                && string.Equals(left.ListItemETag, right.ListItemETag, StringComparison.Ordinal)
                && string.Equals(left.WebUrl, right.WebUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.PageServerRelativeUrl, right.PageServerRelativeUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.CanonicalUrl, right.CanonicalUrl, StringComparison.Ordinal);
        }
    }

    internal sealed class SelfAuthoredVerifiedProvenanceVerifier : IProducerBuildProvenanceVerifier
    {
        public ProducerBuildProvenanceReceipt Verify(ProducerBuildProvenanceManifest manifest)
        {
            var receipt = new ProducerBuildProvenanceReceipt
            {
                ManifestDigestSha256 = manifest.ContentSha256,
                VerifierId = "caller-self-report",
                VerifierImplementationRef = new string('f', 40),
                VerifiedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 10, 0, TimeSpan.Zero),
                VerificationStatus = ProducerBuildProvenanceContract.Verified,
                ReasonCodes = new List<string> { "CALLER_ASSERTED_VERIFIED" }
            };
            ProducerBuildProvenanceContract.SealReceipt(receipt);
            return receipt;
        }
    }

    internal sealed class AlwaysTrueRuntimePolicy : INativePageRuntimeEvidencePolicy
    {
        public string ProfileId => NativePageRuntimeContract.ClassicWikiProfile;
        public string PolicyVersion => NativePageRuntimeContract.ClassicWikiPolicyVersion;
        public void ValidateBinding(NativePageRuntimeBinding binding, IMigrationArtifactStore artifactStore) { }
        public bool VerifyResult(NativePageRuntimeBinding binding, RuntimeVerificationResult result, IMigrationArtifactStore artifactStore) => true;
    }

    internal sealed class MemoryArtifactStore : IMigrationArtifactStore
    {
        private readonly Dictionary<string, byte[]> artifacts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public bool Contains(string sha256) => artifacts.ContainsKey(sha256);

        public Stream OpenRead(string sha256)
        {
            if (!artifacts.TryGetValue(sha256, out var value))
            {
                throw new FileNotFoundException("Artifact not found.", sha256);
            }
            return new MemoryStream(value, writable: false);
        }

        public ArtifactReference Put(Stream content, string mediaType = null, string originalName = null)
        {
            using (var copy = new MemoryStream())
            {
                content.CopyTo(copy);
                var bytes = copy.ToArray();
                var digest = MigrationDigest.ComputeSha256(bytes);
                artifacts[digest] = bytes;
                return new ArtifactReference
                {
                    Sha256 = digest,
                    Length = bytes.LongLength,
                    MediaType = mediaType,
                    OriginalName = originalName
                };
            }
        }

        public ArtifactReference PutText(string value, string mediaType)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(value)))
            {
                return Put(stream, mediaType);
            }
        }

        public ArtifactReference PutBytes(byte[] value, string mediaType)
        {
            using (var stream = new MemoryStream(value, writable: false))
            {
                return Put(stream, mediaType);
            }
        }

        public void Corrupt(string sha256) => artifacts[sha256] = Encoding.UTF8.GetBytes("altered");
        public void Remove(string sha256) => artifacts.Remove(sha256);
    }
}
