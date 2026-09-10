using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Comparison;
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
        [TestMethod]
        public void ExactNativeEvidenceBindsSourcePlanImportTargetAndRuntimeBytes()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var binding = fixture.CreateNativeBinding();

            var digest = NativePageRuntimeBindingValidator.ValidateCoreAndComputeDigest(
                binding,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Target);
            NativePageRuntimeBindingValidator.ValidateNativeEvidence(
                binding,
                fixture.RuntimeReceipt,
                fixture.RuntimeManifest,
                fixture.ImplementationRef,
                fixture.ArtifactStore,
                fixture.AdmittedPlan);

            Assert.AreEqual(fixture.AdmittedPlan.Operations.RuntimeOperationId, binding.RuntimeOperationId);
            Assert.AreEqual(fixture.AdmittedPlan.SourceVersion.VersionDigestSha256, binding.SourceVersionDigestSha256);
            Assert.AreEqual(fixture.Target.SiteId, binding.Target.SiteId);
            Assert.AreEqual(fixture.Target.WebId, binding.Target.WebId);
            Assert.AreEqual(fixture.Target.FileUniqueId, binding.Target.FileUniqueId);
            Assert.AreEqual(fixture.Target.ListItemETag, binding.Target.ListItemETag);
            Assert.AreEqual(fixture.RuntimeReceipt.TargetIdentity, binding.RequestedUrl);
            Assert.AreEqual(fixture.RuntimeReceipt.TargetIdentity, binding.FinalUrl);
            Assert.AreEqual(64, digest.Length);
        }

        [TestMethod]
        public void StaleSourceVersionAndWrongRuntimeOperationFailClosed()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var binding = fixture.CreateNativeBinding();
            binding.SourceVersionDigestSha256 = NativeRuntimeTestFixture.Hash("stale-source");
            Assert.ThrowsException<InvalidDataException>(() =>
                fixture.ValidateCore(binding));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.RuntimeReceipt.OperationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
            Assert.ThrowsException<InvalidDataException>(() => fixture.CreateNativeBinding());
        }

        [TestMethod]
        public void WrongTargetSiteWebFileItemVersionOrETagFailsClosed()
        {
            var mutations = new Action<NativePageRuntimeTargetIdentity>[]
            {
                value => value.SiteId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                value => value.WebId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                value => value.FileUniqueId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
                value => value.ListItemId++,
                value => value.ListItemVersion = "2.0",
                value => value.ListItemETag = "\"foreign,2\""
            };

            foreach (var mutate in mutations)
            {
                var fixture = NativeRuntimeTestFixture.Create();
                var binding = fixture.CreatePendingBinding();
                mutate(binding.Target);
                Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateCore(binding));
            }
        }

        [TestMethod]
        public void ImportDigestMismatchAndAlteredRuntimeArtifactFailClosed()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var binding = fixture.CreateNativeBinding();
            binding.ImportReceiptDigestSha256 = NativeRuntimeTestFixture.Hash("foreign-import");
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateCore(binding));

            fixture = NativeRuntimeTestFixture.Create();
            binding = fixture.CreateNativeBinding();
            fixture.ArtifactStore.Corrupt(fixture.RuntimeReceipt.Results[0].EvidenceArtifactSha256);
            Assert.ThrowsException<InvalidDataException>(() =>
                NativePageRuntimeBindingValidator.ValidateNativeEvidence(
                    binding,
                    fixture.RuntimeReceipt,
                    fixture.RuntimeManifest,
                    fixture.ImplementationRef,
                    fixture.ArtifactStore,
                    fixture.AdmittedPlan));

            fixture = NativeRuntimeTestFixture.Create();
            binding = fixture.CreateNativeBinding();
            fixture.ArtifactStore.Remove(fixture.RuntimeReceipt.Results[0].DomProbeArtifactSha256);
            Assert.ThrowsException<InvalidDataException>(() =>
                NativePageRuntimeBindingValidator.ValidateNativeEvidence(
                    binding,
                    fixture.RuntimeReceipt,
                    fixture.RuntimeManifest,
                    fixture.ImplementationRef,
                    fixture.ArtifactStore,
                    fixture.AdmittedPlan));

            fixture = NativeRuntimeTestFixture.Create();
            fixture.RuntimeManifest.Requirements[0].Description = "stale manifest";
            Assert.ThrowsException<InvalidDataException>(() => fixture.CreateNativeBinding());
        }

        [TestMethod]
        public void UnknownSchemaProfileAndEnumFailClosed()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var binding = fixture.CreatePendingBinding();
            binding.SchemaVersion = "pnp-native-page-runtime-binding/v2";
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateCore(binding));

            binding = fixture.CreatePendingBinding();
            binding.ProfileId = "profile.unknown";
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateCore(binding));

            binding = fixture.CreatePendingBinding();
            binding.RuntimeVerificationStatus = (RuntimeVerificationStatus)999;
            Assert.ThrowsException<InvalidDataException>(() => fixture.ValidateCore(binding));
        }

        [TestMethod]
        public void ProducerBuildProvenanceDefaultsToUnverified()
        {
            var manifest = NativeRuntimeTestFixture.CreateProvenanceManifest();
            var receipt = new UnverifiedProducerBuildProvenanceVerifier().Verify(manifest);

            Assert.AreEqual(ProducerBuildProvenanceContract.Unverified, receipt.VerificationStatus);
            Assert.AreEqual(
                ProducerBuildProvenanceContract.ValidateManifestAndComputeDigest(manifest),
                receipt.ManifestDigestSha256);
            Assert.AreEqual(64, ProducerBuildProvenanceContract.ValidateReceiptAndComputeDigest(receipt, manifest).Length);
        }

        [TestMethod]
        public void PermanentFixtureMatrixIsCopiedAndValidJson()
        {
            var root = Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                "Migration",
                "NativeRuntimeBinding",
                "v1");
            var files = Directory.GetFiles(root, "*.case.json").OrderBy(value => value).ToArray();

            Assert.AreEqual(7, files.Length);
            foreach (var file in files)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                Assert.IsTrue(document.RootElement.TryGetProperty("caseId", out var caseId));
                Assert.IsFalse(string.IsNullOrWhiteSpace(caseId.GetString()));
            }
        }
    }

    internal sealed class NativeRuntimeTestFixture
    {
        public string ImplementationRef { get; private set; }

        public AdmittedReproExecutionPlan AdmittedPlan { get; private set; }

        public string AdmittedDigest { get; private set; }

        public NativePageImportReceiptAggregate ImportAggregate { get; private set; }

        public NativePageRuntimeTargetIdentity Target { get; private set; }

        public RuntimeVerificationManifest RuntimeManifest { get; private set; }

        public RuntimeVerificationReceipt RuntimeReceipt { get; set; }

        public MemoryArtifactStore ArtifactStore { get; private set; }

        public static NativeRuntimeTestFixture Create(bool runtimePassed = true)
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var implementationRef = new string('a', 40);
            var admittedPlan = new AdmittedReproExecutionPlan
            {
                PlanDigest = package.PlanDigest,
                TargetIdentity = new Uri(package.Plan.TargetLocation.TargetWebUrl).GetLeftPart(UriPartial.Authority)
                    + package.Plan.TargetPageServerRelativeUrl,
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
                admittedPlan.TargetIdentity);
            var startedAt = new DateTimeOffset(2026, 9, 10, 0, 2, 0, TimeSpan.Zero);
            var importReceipt = new ClassicWikiImportReceipt
            {
                StartedAtUtc = startedAt,
                CompletedAtUtc = startedAt.AddSeconds(5),
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
                        CompletedAtUtc = startedAt.AddSeconds(2),
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
                SiteId = Guid.Parse("c37b3679-4601-4a1d-a0f8-c8ed3ee477f2"),
                WebId = package.Plan.TargetLocation.TargetWebId,
                WebUrl = importReceipt.TargetWebUrl,
                PageServerRelativeUrl = importReceipt.TargetPageServerRelativeUrl,
                FileUniqueId = importReceipt.TargetFileUniqueId,
                ListItemId = importReceipt.TargetListItemId,
                ListItemVersion = importReceipt.TargetVersionLabel,
                ListItemETag = "\"target,1\""
            };
            var store = new MemoryArtifactStore();
            var html = store.PutBytes("<html><body>classic wiki</body></html>");
            var dom = store.PutBytes("{\"surface\":\"classic-wiki\",\"errorShell\":false}");
            var manifest = new RuntimeVerificationManifest
            {
                Requirements = new List<RuntimeVerificationRequirement>
                {
                    new RuntimeVerificationRequirement
                    {
                        Id = "runtime.wiki.surface",
                        Kind = RuntimeVerificationRequirementKind.AuthoredDomEquality,
                        Required = true,
                        Description = "Classic Wiki authored DOM is present."
                    }
                }
            };
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
                CreatedAtUtc = startedAt.AddSeconds(6),
                FirstNavigationAtUtc = startedAt.AddSeconds(7)
            };
            var completedAt = startedAt.AddSeconds(9);
            var result = new RuntimeVerificationResult
            {
                RequirementId = "runtime.wiki.surface",
                Passed = runtimePassed,
                EvidenceArtifactSha256 = html.Sha256,
                EvidenceArtifactLength = html.Length,
                EvidenceArtifactLocator = "runtime/page.html",
                ImplementationRef = implementationRef,
                BrowserContextId = browser.BrowserContextId,
                Http = new RuntimeHttpEvidence
                {
                    RequestedUrl = admittedPlan.TargetIdentity,
                    FinalUrl = admittedPlan.TargetIdentity,
                    Method = "GET",
                    StatusCode = runtimePassed ? 200 : 403,
                    ContentType = runtimePassed ? "text/html; charset=utf-8" : "text/html",
                    RequestId = "request-ccd271",
                    SharePointRequestGuid = "sprequest-ccd271",
                    ResponseHeadersDigestSha256 = Hash("headers"),
                    EncodedDataLength = html.Length,
                    CapturedAtUtc = startedAt.AddSeconds(8)
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
                Message = runtimePassed ? "passed" : "access denied terminal observation"
            };
            var runtimeReceipt = RuntimeVerificationReceiptFactory.Create(
                admittedPlan,
                admittedDigest,
                aggregate.ReceiptDigestSha256,
                admittedPlan.TargetIdentity,
                manifest,
                implementationRef,
                browser,
                new[] { result },
                completedAt);

            return new NativeRuntimeTestFixture
            {
                ImplementationRef = implementationRef,
                AdmittedPlan = admittedPlan,
                AdmittedDigest = admittedDigest,
                ImportAggregate = aggregate,
                Target = target,
                RuntimeManifest = manifest,
                RuntimeReceipt = runtimeReceipt,
                ArtifactStore = store
            };
        }

        public NativePageRuntimeBinding CreateNativeBinding()
        {
            return ClassicWikiRuntimeBindingFactory.CreateNative(
                AdmittedPlan,
                AdmittedDigest,
                ImportAggregate,
                Target,
                RuntimeReceipt,
                RuntimeManifest,
                ImplementationRef,
                ArtifactStore);
        }

        public NativePageRuntimeBinding CreatePendingBinding(ExternalPageRuntimeEvidence evidence = null)
        {
            return ClassicWikiRuntimeBindingFactory.CreatePending(
                AdmittedPlan,
                AdmittedDigest,
                ImportAggregate,
                Target,
                evidence);
        }

        public string ValidateCore(NativePageRuntimeBinding binding)
        {
            return NativePageRuntimeBindingValidator.ValidateCoreAndComputeDigest(
                binding,
                AdmittedPlan,
                AdmittedDigest,
                ImportAggregate,
                Target);
        }

        public ExternalPageRuntimeEvidence CreateExternalEvidence(RuntimeVerificationStatus claimedStatus)
        {
            var evidence = new ExternalPageRuntimeEvidence
            {
                ReportId = "custom-report-ccd271",
                RuntimeOperationId = AdmittedPlan.Operations.RuntimeOperationId,
                SourceVersionDigestSha256 = AdmittedPlan.SourceVersion.VersionDigestSha256,
                AdmittedPlanDigestSha256 = AdmittedDigest,
                ImportReceiptDigestSha256 = ImportAggregate.ReceiptDigestSha256,
                Target = NativePageRuntimeBindingValidator.CopyTarget(Target),
                RequestedUrl = AdmittedPlan.TargetIdentity,
                FinalUrl = AdmittedPlan.TargetIdentity,
                EvidenceDigestSha256 = Hash("custom-external-report-bytes"),
                ClaimedStatus = claimedStatus,
                ObservedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 10, 0, TimeSpan.Zero)
            };
            NativePageRuntimeBindingValidator.SealExternalEvidence(evidence);
            return evidence;
        }

        public static ProducerBuildProvenanceManifest CreateProvenanceManifest()
        {
            return new ProducerBuildProvenanceManifest
            {
                ProducerId = "ccd153-native-producer",
                ProducerVersion = "1.0.0",
                ImplementationRef = new string('a', 40),
                BinaryName = "ccd153-native-producer.dll",
                BinarySha256 = Hash("producer-binary"),
                TargetFramework = "net10.0",
                BuildConfiguration = "Debug"
            };
        }

        public static string Hash(string value)
        {
            return MigrationDigest.ComputeSha256(value);
        }
    }

    internal sealed class MemoryArtifactStore : IMigrationArtifactStore
    {
        private readonly Dictionary<string, byte[]> artifacts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public bool Contains(string sha256)
        {
            return artifacts.ContainsKey(sha256);
        }

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

        public ArtifactReference PutBytes(string value)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(value)))
            {
                return Put(stream, "application/octet-stream");
            }
        }

        public void Corrupt(string sha256)
        {
            artifacts[sha256] = Encoding.UTF8.GetBytes("altered");
        }

        public void Remove(string sha256)
        {
            artifacts.Remove(sha256);
        }
    }
}
