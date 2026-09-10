using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Content.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Pages.Content.Maturity
{
    [TestClass]
    public class ContentTextMaturityContributorTests
    {
        [TestMethod]
        public void PublishingFixturePassesM0AndM2ButMissingTargetReadbackStopsAtM0()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.SourceBinding, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
            AssertGate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void FreshSemanticallyEquivalentPublishingReadbackClosesM1AndM2()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();

            Assert.IsFalse(contribution.GetType().GetProperties().Any(value =>
                string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual(fixture.Data.SemanticSlice.Canonical, fixture.Normalized.NormalizedBodyValue);
            Assert.AreEqual(fixture.Data.SemanticSlice.SemanticSha256,
                fixture.Normalized.TargetObservationDigests["content.semantic"]);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PerValueObservation, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void TargetReadbackOlderThanSourceFailsFreshness()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations(fixture.SourceObservedAt.AddSeconds(-1));

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void MaterialSemanticDifferenceFailsFreshReadback()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            var targetSemantic = fixture.Evidence.Live.Observations.Single(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                && value.ValuePath == "content.semantic");
            targetSemantic.ValueDigest = MigrationDigest.ComputeSha256("materially different content");

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void WrongPublishingFieldIdentityFailsPrimaryOwnerGate()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.Evidence.Source.FieldId = "11111111-1111-1111-1111-111111111111";
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void PartialCapturedValueFailsClosedInsteadOfLookingEmpty()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.Evidence.Source.Availability = ContentTextAvailability.Partial;
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void MissingListSchemaDependencyFailsPrimaryOwnerGate()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.Evidence.Source.DependencyIngredientIds = fixture.Evidence.Source.DependencyIngredientIds
                .Where(value => !value.StartsWith("list-schema:", StringComparison.Ordinal))
                .ToList();
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [DataTestMethod]
        [DataRow("foreign")]
        [DataRow("empty")]
        public void DependencyProvidersMustMatchTheBoundSource(string mutation)
        {
            var fixture = Fixture.CreatePublishing();
            foreach (var dependency in fixture.Evidence.Source.Dependencies)
            {
                dependency.ProviderIdentity = mutation == "empty" ? string.Empty : "foreign-provider";
            }
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity, mutation);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void ForeignCanonicalIngredientIdDoesNotAliasTheCapturedFile()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.Context.Identity.IngredientId =
                "ccd.ingredient.content.text/v1:99999999-9999-9999-9999-999999999999:PublishingPageContent";
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void WrongWikiFieldIdentityFailsPrimaryOwnerGate()
        {
            var fixture = Fixture.CreateWiki("<p>wrong wiki identity</p>");
            fixture.Evidence.Source.FieldId = "11111111-1111-1111-1111-111111111111";
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void ContradictoryTargetFieldIdentityFailsFreshReadback()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            var reference = "evidence/content-text/target-binding.fieldId-conflict.json";
            fixture.Evidence.Live.TargetEvidenceReferences.Add(reference);
            fixture.Evidence.Live.Observations.Add(new IngredientValueObservation
            {
                ClaimId = fixture.Context.Identity.ClaimId,
                IngredientId = fixture.Context.Identity.IngredientId,
                Source = fixture.Context.Source,
                Target = fixture.Context.Target,
                ValuePath = "binding.fieldId",
                ValueDigest = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                    "11111111-1111-1111-1111-111111111111")),
                ObservedAtUtc = fixture.SourceObservedAt.AddSeconds(1),
                Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                EvidenceReference = reference
            });

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PerValueObservation, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void SameClaimPlanAndOperationalReceiptsReachM4()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            fixture.BindPlanAndOperationalEvidence();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M4, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.SnapshotPlanBinding, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.OperationActionBinding, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.FreshStorage, IngredientMaturityGateStatus.Passed);
        }

        [DataTestMethod]
        [DataRow("missing")]
        [DataRow("foreign")]
        public void VerificationTargetDigestMustBindTheActionTarget(string mutation)
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            fixture.BindPlanAndOperationalEvidence();
            fixture.Evidence.Operational.VerificationReceipt.TargetIdentityDigest =
                mutation == "missing" ? null : MigrationDigest.ComputeSha256("foreign-target");

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M3, assessment.AttainedMaturity, mutation);
        }

        [TestMethod]
        public void BoundSameClaimProductizationReachesM5WithoutChangingConditionalOutcome()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            fixture.BindPlanAndOperationalEvidence();
            fixture.BindProductizationEvidence();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M5, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientTechnicalStatus.Conditional, fixture.Context.TechnicalOutcome.Status);
        }

        [DataTestMethod]
        [DataRow("snapshot")]
        [DataRow("import-receipt")]
        [DataRow("ingredient")]
        [DataRow("source-artifact")]
        [DataRow("action")]
        [DataRow("target")]
        [DataRow("producer")]
        [DataRow("missing-ingredient")]
        [DataRow("denied-instance")]
        public void CompareEvidenceMustRemainJoinedToTheCurrentInstance(string mutation)
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            fixture.BindPlanAndOperationalEvidence();
            fixture.BindProductizationEvidence();
            var report = fixture.Evidence.Productization.CompareReport;
            var row = report.Ingredients.Single();
            switch (mutation)
            {
                case "snapshot":
                    report.Bindings.SnapshotDigestSha256 = MigrationDigest.ComputeSha256("foreign-snapshot");
                    break;
                case "import-receipt":
                    report.Bindings.ImportReceiptDigestSha256 = MigrationDigest.ComputeSha256("foreign-import-receipt");
                    break;
                case "ingredient":
                    row.IngredientId = "content:foreign";
                    row.Lineage.SourceIngredientId = "content:foreign";
                    break;
                case "source-artifact":
                    row.Lineage.SourceArtifactDigestSha256 = MigrationDigest.ComputeSha256("foreign-source");
                    break;
                case "action":
                    row.Lineage.ActionId = "action:foreign";
                    break;
                case "target":
                    row.Lineage.TargetIdentity = "cupcollect:foreign-target";
                    break;
                case "producer":
                    report.Producer.ImplementationRef = new string('b', 40);
                    break;
                case "missing-ingredient":
                    report.Ingredients.Clear();
                    break;
                case "denied-instance":
                    row.TargetEvidenceState = IngredientTargetEvidenceStates.Denied;
                    row.ResultClass = PublishingPageCompareContract.ResultClasses.AuthorizationBlocked;
                    row.ReasonCode = "ACCESS_DENIED_SKIPPED";
                    break;
                default:
                    Assert.Fail("Unknown mutation.");
                    break;
            }
            fixture.ResealCompareReport();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M4, assessment.AttainedMaturity, mutation);
        }

        [DataTestMethod]
        [DataRow("list")]
        [DataRow("list-schema")]
        [DataRow("content-type")]
        [DataRow("field")]
        public void ForeignDependencyReferenceFailsProviderAdmission(string kind)
        {
            var fixture = Fixture.CreatePublishing();
            var dependency = fixture.Evidence.Source.Dependencies.Single(value => value.Kind == kind);
            dependency.EvidenceReference = "evidence/unrelated-page/foreign-" + kind + ".json";
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity, kind);
        }

        [TestMethod]
        public void ChangedFieldSchemaInvalidatesCapturedDependencyEvidence()
        {
            var fixture = Fixture.CreatePublishing();
            var dependency = fixture.Evidence.Source.Dependencies.Single(value => value.Kind == "field");
            var previousDigest = dependency.EvidenceDigest;
            fixture.Evidence.Source.FieldSealed = !fixture.Evidence.Source.FieldSealed;

            var currentDigest = ContentTextEvidenceNormalizer.CreateDependencyEvidenceDigest(
                fixture.Evidence.Source,
                dependency.Kind,
                dependency.ProviderIdentity);
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            Assert.AreNotEqual(previousDigest, currentDigest);
            Assert.IsNull(fixture.Evaluate().AttainedMaturity);
        }

        [DataTestMethod]
        [DataRow("missing-reference")]
        [DataRow("stale-payload")]
        [DataRow("corrupt-artifact-digest")]
        [DataRow("corrupt-evidence-digest")]
        public void DependencyProviderArtifactFailsClosedWhenBindingIsIncomplete(string mutation)
        {
            var fixture = Fixture.CreatePublishing();
            var dependency = fixture.Evidence.Source.Dependencies.Single(value => value.Kind == "field");
            switch (mutation)
            {
                case "missing-reference":
                    fixture.Evidence.Source.EvidenceReferences.Remove(dependency.EvidenceReference);
                    break;
                case "stale-payload":
                    dependency.ProviderArtifactBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"stale\":true}"));
                    break;
                case "corrupt-artifact-digest":
                    dependency.ProviderArtifact.Sha256 = MigrationDigest.ComputeSha256("corrupt-artifact");
                    break;
                case "corrupt-evidence-digest":
                    dependency.EvidenceDigest = MigrationDigest.ComputeSha256("corrupt-binding");
                    break;
                default:
                    Assert.Fail("Unknown mutation.");
                    break;
            }
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            Assert.IsNull(fixture.Evaluate().AttainedMaturity, mutation);
        }

        [TestMethod]
        public void LiveProjectionPreservesNullObservationForSharedV2Rejection()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            fixture.Evidence.Live.Observations.Add(null);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PerValueObservation, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void ForeignHigherLevelBundleStopsAtM2()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            var donor = Fixture.CreateWiki("<p>foreign donor</p>");
            donor.BindPlanAndOperationalEvidence();
            fixture.Evidence.Plan = donor.Evidence.Plan;
            fixture.Evidence.Operational = donor.Evidence.Operational;
            fixture.Evidence.Productization = new IngredientProductizationEvidence();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.SnapshotPlanBinding, IngredientMaturityGateStatus.Missing);
            AssertGate(assessment, IngredientMaturityGateCatalog.OperationActionBinding, IngredientMaturityGateStatus.Missing);
        }

        [TestMethod]
        public void WikiFieldUsesOrdinalBodySemanticsAndKeepsEscapedMarkup()
        {
            var fixture = Fixture.CreateWiki("<p>&lt;script&gt;safe text&lt;/script&gt;</p>");
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual("<p>&lt;script&gt;safe text&lt;/script&gt;</p>", fixture.Normalized.NormalizedBodyValue);
            Assert.IsFalse(fixture.Normalized.NormalizedBodyValue.Contains("<script>", StringComparison.Ordinal));
            Assert.AreEqual(ContentTextEvidenceNormalizer.WikiSubtype, fixture.Normalized.Node.Subtype);
            Assert.AreEqual(ContentTextEvidenceNormalizer.WikiSourcePredicateId, fixture.Normalized.Node.SourcePredicateId);
        }

        private static void AssertGate(
            IngredientMaturityAssessment assessment,
            string gateId,
            IngredientMaturityGateStatus expected)
        {
            var gate = assessment.Levels.SelectMany(value => value.Gates)
                .Single(value => string.Equals(value.GateId, gateId, StringComparison.Ordinal));
            Assert.AreEqual(expected, gate.Status, gateId);
        }

        private sealed class Fixture
        {
            private static readonly DateTimeOffset FixedSourceObservedAt =
                new DateTimeOffset(2026, 9, 9, 19, 29, 35, TimeSpan.Zero);

            private Fixture()
            {
            }

            public FixtureData Data { get; private set; }

            public ContentTextMaturityEvidence Evidence { get; private set; }

            public IngredientMaturityEvaluationContext Context { get; private set; }

            public ContentTextNormalization Normalized { get; private set; }

            public DateTimeOffset SourceObservedAt => FixedSourceObservedAt;

            public static Fixture CreatePublishing()
            {
                var data = LoadFixture();
                var rawBytes = Encoding.UTF8.GetBytes(data.SemanticSlice.Raw);
                var source = new ContentTextSourceEvidence
                {
                    PageUrl = data.Source.PageUrl,
                    WebUrl = data.Source.WebUrl,
                    FileServerRelativeUrl = data.Source.FileServerRelativeUrl,
                    ListId = data.Source.ListId,
                    ListTitle = data.Source.ListTitle,
                    ListBaseTemplate = data.Source.ListBaseTemplate,
                    ItemId = data.Source.ItemId,
                    FileUniqueId = data.Source.FileUniqueId,
                    SourceVersion = data.Source.SourceVersion,
                    ContentTypeId = data.Source.ContentTypeId,
                    ContentTypeName = data.Source.ContentTypeName,
                    FieldId = data.Field.Id,
                    FieldInternalName = data.Field.InternalName,
                    FieldType = data.Field.Type,
                    FieldRequired = data.Field.Required,
                    FieldReadOnly = data.Field.ReadOnly,
                    FieldHidden = data.Field.Hidden,
                    FieldSealed = data.Field.Sealed,
                    Availability = ContentTextAvailability.Captured,
                    RawArtifact = new ArtifactReference
                    {
                        Sha256 = data.SemanticSlice.RawSha256,
                        Length = data.SemanticSlice.RawBytes,
                        MediaType = "text/html",
                        ContentEncoding = "utf-8"
                    },
                    RawArtifactBase64 = Convert.ToBase64String(rawBytes),
                    RawValueSha256 = data.SemanticSlice.RawSha256,
                    SemanticValueSha256 = data.SemanticSlice.SemanticSha256,
                    EvidenceReferences = References("source-artifact.bin", "source-recheck.json")
                };
                BindDependencies(source);
                return Create(data, source, data.IngredientId,
                    ContentTextEvidenceNormalizer.PublishingSubtype,
                    ContentTextEvidenceNormalizer.PublishingSourcePredicateId);
            }

            public static Fixture CreateWiki(string raw)
            {
                var data = LoadFixture();
                var bytes = Encoding.UTF8.GetBytes(raw);
                var digest = MigrationDigest.ComputeSha256(bytes);
                var source = new ContentTextSourceEvidence
                {
                    PageUrl = "https://microsoft.sharepoint.com/sites/ccd-redacted/SitePages/wiki-canary.aspx",
                    WebUrl = "https://microsoft.sharepoint.com/sites/ccd-redacted",
                    FileServerRelativeUrl = "/sites/ccd-redacted/SitePages/wiki-canary.aspx",
                    ListId = "22222222-2222-2222-2222-222222222222",
                    ListTitle = "Site Pages",
                    ListBaseTemplate = 119,
                    ItemId = 17,
                    FileUniqueId = "33333333-3333-3333-3333-333333333333",
                    SourceVersion = "\"{33333333-3333-3333-3333-333333333333},4\"",
                    ContentTypeId = "0x010108",
                    ContentTypeName = "Wiki Page",
                    FieldId = "c33527b4-d920-4587-b791-45024d00068a",
                    FieldInternalName = ContentTextEvidenceNormalizer.WikiFieldInternalName,
                    FieldType = "Note",
                    Availability = ContentTextAvailability.Captured,
                    RawArtifact = new ArtifactReference
                    {
                        Sha256 = digest,
                        Length = bytes.LongLength,
                        MediaType = "text/html",
                        ContentEncoding = "utf-8"
                    },
                    RawArtifactBase64 = Convert.ToBase64String(bytes),
                    RawValueSha256 = digest,
                    SemanticValueSha256 = digest,
                    EvidenceReferences = References("wiki-source-artifact.bin", "wiki-source-recheck.json")
                };
                BindDependencies(source);
                return Create(data, source,
                    "ccd.ingredient.content.text/v1:33333333-3333-3333-3333-333333333333:WikiField",
                    ContentTextEvidenceNormalizer.WikiSubtype,
                    ContentTextEvidenceNormalizer.WikiSourcePredicateId);
            }

            public void RefreshNormalization()
            {
                Context.Source.PageOrListItemIdentity = ContentTextEvidenceNormalizer.CreateSourceIdentity(Evidence.Source);
                Context.Source.SourceVersion = Evidence.Source.SourceVersion;
                Context.Source.SourceArtifactDigest = Evidence.Source.RawValueSha256;
                Context.ObservationWindowStartUtc = FixedSourceObservedAt.AddMinutes(-1);
                Context.ObservationWindowEndUtc = FixedSourceObservedAt.AddMinutes(1);
                Normalized = ContentTextEvidenceNormalizer.Normalize(Context, Evidence.Source);
                Evidence.Live = new IngredientLiveEvidence
                {
                    SourceAuthenticated = true,
                    TargetFreshReadback = false,
                    ReadbackStartedAtUtc = FixedSourceObservedAt.AddMilliseconds(500)
                };
            }

            public void AddSourceObservations()
            {
                AddObservations(
                    IngredientObservationOrigin.AuthenticatedSource,
                    Normalized.SourceObservationDigests,
                    SourceObservedAt,
                    "source");
            }

            public void AddTargetObservations(DateTimeOffset? observedAt = null)
            {
                AddObservations(
                    IngredientObservationOrigin.CupCollectFreshReadback,
                    Normalized.TargetObservationDigests,
                    observedAt ?? SourceObservedAt.AddSeconds(1),
                    "target");
                Evidence.Live.TargetFreshReadback = true;
            }

            public IngredientMaturityContribution Contribute()
            {
                return new ContentTextMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[]
                    {
                        (IIngredientMaturityContributor)new ContentTextMaturityContributor(Evidence)
                    }));
            }

            public void BindPlanAndOperationalEvidence()
            {
                var node = new PageIngredientNode
                {
                    Id = Normalized.Node.Id,
                    Kind = Normalized.Node.Kind,
                    KindId = Normalized.Node.KindId,
                    Subtype = Normalized.Node.Subtype,
                    SemanticRole = Normalized.Node.SemanticRole,
                    SourcePredicateId = Normalized.Node.SourcePredicateId,
                    SourcePageOrListItemIdentity = Normalized.Node.SourcePageOrListItemIdentity,
                    SourceVersionIdentity = Normalized.Node.SourceVersionIdentity,
                    PrimaryOwnerLane = Normalized.Node.PrimaryOwnerLane,
                    Label = Normalized.Node.Label,
                    HasContent = Normalized.Node.HasContent,
                    Ownership = Normalized.Node.Ownership,
                    SourceAuthority = Normalized.Node.SourceAuthority,
                    EvidenceDigest = Normalized.Node.EvidenceDigest,
                    RuntimeRequirement = Normalized.Node.RuntimeRequirement,
                    EvidenceReferences = Normalized.Node.EvidenceReferences.ToList()
                };
                var action = new PageIngredientAction
                {
                    ActionId = "action:content-text:preserve",
                    IngredientId = node.Id,
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Preserve,
                    Realization = "native-sharepoint-field-value",
                    TargetIdentity = Context.Target.TargetIdentity,
                    PolicyId = "content.text.preserve/v1",
                    PolicyVersion = "v1"
                };
                var plan = new PublishingPageMigrationPlan
                {
                    SourceSnapshotDigest = Context.Source.SourceSnapshotDigest,
                    RuntimeVerification = new RuntimeVerificationManifest(),
                    IngredientGraph = new CanonicalPageIngredientGraph
                    {
                        Nodes = new List<PageIngredientNode> { node }
                    },
                    IngredientActions = new List<PageIngredientAction> { action }
                };
                var planDigest = PublishingPageDigest.ComputePlanDigest(plan);
                Evidence.Plan = new IngredientPlanEvidence
                {
                    Plan = plan,
                    ExpectedSourceSnapshotDigest = Context.Source.SourceSnapshotDigest,
                    ExpectedPlanDigest = planDigest,
                    IngredientId = Context.Identity.IngredientId,
                    SnapshotPlanEvidenceReferences = References("m3-plan.json"),
                    ActionEvidenceReferences = References("m3-action.json"),
                    DependencyPolicyEvidenceReferences = References("m3-dependencies.json")
                };

                var completedAt = FixedSourceObservedAt.AddSeconds(10);
                var startedAt = FixedSourceObservedAt.AddSeconds(5);
                var operationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
                var signature = MigrationActionSignature.Create(
                    action.ActionId,
                    "preserve-content-text",
                    Context.Source.SourceArtifactDigest,
                    MigrationActionSignature.EmptySelectionReceiptDigest,
                    Context.Target.TargetIdentity,
                    Normalized.SemanticProjectionSha256);
                Evidence.Operational = new IngredientOperationalEvidence
                {
                    Plan = plan,
                    AdmittedPlanDigest = planDigest,
                    AdmissionPassed = true,
                    IngredientId = Context.Identity.IngredientId,
                    ActionSignature = signature,
                    ImportReceipt = new PublishingPageImportReceipt
                    {
                        OperationId = operationId,
                        StartedAtUtc = startedAt,
                        CompletedAtUtc = completedAt,
                        ApprovedPlanDigest = planDigest,
                        ExecutionStatus = MigrationExecutionStatus.Succeeded,
                        Steps = new List<MigrationMutationReceipt>
                        {
                            new MigrationMutationReceipt
                            {
                                OperationId = operationId,
                                PlanDigest = planDigest,
                                ActionId = action.ActionId,
                                ActionSignature = signature.Signature,
                                Outcome = MutationOutcome.Applied,
                                CompletedAtUtc = completedAt
                            }
                        },
                        OwnershipMatched = true,
                        FreshReadbackPassed = true,
                        StorageVerificationStatus = StorageVerificationStatus.Passed,
                        RuntimeVerificationStatus = RuntimeVerificationStatus.NotRequired,
                        VerifiedIngredientIds = new List<string> { Context.Identity.IngredientId }
                    },
                    JournalState = new MigrationExecutionStateReceipt
                    {
                        OperationId = operationId,
                        PlanDigest = planDigest,
                        Status = MigrationExecutionStatus.Succeeded,
                        RecordedAtUtc = completedAt
                    },
                    VerificationReceipt = new MigrationMutationVerificationReceipt
                    {
                        OperationId = operationId,
                        PlanDigest = planDigest,
                        ActionId = action.ActionId,
                        ActionSignature = signature.Signature,
                        FreshReadbackPassed = true,
                        ObservedStateDigest = Normalized.SemanticProjectionSha256,
                        Ownership = MigrationTargetOwnership.MigrationOwned,
                        TargetIdentityDigest = signature.TargetIdentityDigest,
                        ProvenanceMatched = true,
                        VerifiedAtUtc = completedAt
                    },
                    ExecutionStartedAtUtc = startedAt,
                    CleanupRequired = true,
                    CleanupPassed = true,
                    RetryEvidenceRequired = true,
                    RetryPassed = true,
                    PlanAdmissionEvidenceReferences = References("m4-admission.json"),
                    ActionEvidenceReferences = References("m4-action.json"),
                    ReceiptEvidenceReferences = References("m4-import.json", "m4-journal.json"),
                    FreshReadbackEvidenceReferences = References("m4-readback.json"),
                    RuntimeCleanupRetryEvidenceReferences = References("m4-cleanup-retry.json")
                };
            }

            public void BindProductizationEvidence()
            {
                var implementationCommit = Context.Producer.ImplementationCommit;
                Context.Producer.BinaryDigest = MigrationDigest.ComputeSha256("content-text-test-binary");
                var planDigest = Evidence.Plan.ExpectedPlanDigest;
                var report = new PublishingPageCompareReport
                {
                    SchemaVersion = PublishingPageCompareContract.IngredientContributionSchemaVersion,
                    Producer = new CompareProducer
                    {
                        Id = Context.Producer.ProducerId,
                        Version = Context.Producer.ProducerVersion,
                        ImplementationRef = implementationCommit
                    },
                    Bindings = new CompareBindings
                    {
                        SnapshotDigestSha256 = Context.Source.SourceSnapshotDigest,
                        PlanDigestSha256 = planDigest,
                        ImportReceiptDigestSha256 = MigrationDigest.ComputeSha256(
                            MigrationContractSerializer.SerializeCanonical(Evidence.Operational.ImportReceipt))
                    },
                    Ingredients = new List<IngredientCompareResult>
                    {
                        new IngredientCompareResult
                        {
                            IngredientId = Context.Identity.IngredientId,
                            Kind = "Content",
                            Material = true,
                            Lineage = new IngredientCompareLineage
                            {
                                SourceIngredientId = Context.Identity.IngredientId,
                                SourceArtifactDigestSha256 = Context.Source.SourceArtifactDigest,
                                ActionId = Evidence.Operational.ActionSignature.ActionId,
                                TargetIdentity = Context.Target.TargetIdentity,
                                PlanDigestSha256 = planDigest,
                                EvidenceRefs = References("m5-current-instance.json")
                            },
                            Expected = new CompareDigestPair
                            {
                                RawDigestSha256 = Context.Source.SourceArtifactDigest
                            },
                            Actual = new CompareDigestPair
                            {
                                RawDigestSha256 = Context.Source.SourceArtifactDigest
                            },
                            TargetEvidenceState = IngredientTargetEvidenceStates.Fresh,
                            ObservedAtUtc = Evidence.Operational.VerificationReceipt.VerifiedAtUtc,
                            ResultClass = PublishingPageCompareContract.ResultClasses.Exact,
                            ReasonCode = PublishingPageCompareContract.ReasonCodes.ExactRawDigest
                        }
                    },
                    Acceptance = new CompareAcceptance { Verdict = "conditional" }
                };
                Evidence.Productization = new IngredientProductizationEvidence
                {
                    TestId = nameof(ContentTextMaturityContributorTests),
                    HermeticUnitTest = true,
                    RedTestEvidenceReference = "evidence/content-text/m5-red.trx",
                    GreenTestEvidenceReference = "evidence/content-text/m5-green.trx",
                    ImplementationCommit = implementationCommit,
                    BuildCommit = implementationCommit,
                    BinaryDigest = Context.Producer.BinaryDigest,
                    BuildEvidenceReference = "evidence/content-text/m5-build.json",
                    EndToEndCommit = implementationCommit,
                    EndToEndPlanDigest = planDigest,
                    EndToEndEvidenceReference = "evidence/content-text/m5-e2e.json",
                    CompareReport = report,
                    CompareEvidenceReference = "evidence/content-text/m5-compare.json",
                    ArchitectReviewVerdict = "APPROVED",
                    ArchitectReviewEvidenceReference = "evidence/content-text/m5-architect-review.md",
                    CtoReviewVerdict = "APPROVED",
                    CtoReviewEvidenceReference = "evidence/content-text/m5-cto-review.md",
                    IndependentVerificationVerdict = "PASS",
                    IndependentVerificationEvidenceReference = "evidence/content-text/m5-verification.md",
                    PrReadyCommit = implementationCommit,
                    PrReadyEvidenceReference = "evidence/content-text/m5-pr-ready.json"
                };
                ResealCompareReport();
            }

            public void ResealCompareReport()
            {
                var productization = Evidence.Productization;
                productization.CompareReportDigest = PublishingPageCompareReconciler.ComputeReportDigest(
                    productization.CompareReport);
                productization.CompareReport.ReportDigestSha256 = productization.CompareReportDigest;
            }

            private static Fixture Create(
                FixtureData data,
                ContentTextSourceEvidence source,
                string ingredientId,
                string subtype,
                string sourcePredicateId)
            {
                var fixture = new Fixture
                {
                    Data = data,
                    Evidence = new ContentTextMaturityEvidence { Source = source }
                };
                fixture.Context = new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = data.ClaimId,
                        WorkItemType = IngredientMaturityContract.CanonicalIngredientWorkItem,
                        Lane = ContentTextEvidenceNormalizer.Lane,
                        IngredientId = ingredientId,
                        Kind = PageIngredientKind.Content,
                        Subtype = subtype,
                        SemanticRole = ContentTextEvidenceNormalizer.SemanticRole,
                        SourcePredicateId = sourcePredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = ContentTextEvidenceNormalizer.CreateSourceIdentity(source),
                        SourceVersion = source.SourceVersion,
                        SourceArtifactDigest = source.RawValueSha256,
                        SourceSnapshotDigest = data.SourceClaim.SourceSnapshotSha256
                    },
                    Target = new IngredientMaturityTargetBinding
                    {
                        TargetProfile = "cupcollect-classic-page/v1",
                        TargetIdentity = "cupcollect:ccd-owned:content-text-canary"
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = ContentTextEvidenceNormalizer.ContributorId,
                        ImplementationCommit = "aff892f119a20dd764af6b005087e7af5dad3a1c"
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.ExecutableWithLoss,
                        Status = IngredientTechnicalStatus.Conditional,
                        ReasonCode = "TARGET_NATIVE_FIELD_CAPABILITY_PENDING"
                    }
                };
                fixture.RefreshNormalization();
                return fixture;
            }

            private void AddObservations(
                IngredientObservationOrigin origin,
                IReadOnlyDictionary<string, string> digests,
                DateTimeOffset observedAt,
                string prefix)
            {
                foreach (var pair in digests.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    var evidenceReference = $"evidence/content-text/{prefix}-{pair.Key}.json";
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ClaimId = Context.Identity.ClaimId,
                        IngredientId = Context.Identity.IngredientId,
                        Source = Context.Source,
                        Target = Context.Target,
                        ValuePath = pair.Key,
                        ValueDigest = pair.Value,
                        ObservedAtUtc = observedAt,
                        Origin = origin,
                        EvidenceReference = evidenceReference
                    });
                    var references = origin == IngredientObservationOrigin.AuthenticatedSource
                        ? Evidence.Live.SourceEvidenceReferences
                        : Evidence.Live.TargetEvidenceReferences;
                    references.Add(evidenceReference);
                }
            }

            private static void BindDependencies(ContentTextSourceEvidence source)
            {
                var bindings = new[]
                {
                    (Kind: "list", Identity: source.ListId),
                    (Kind: "list-schema", Identity: source.ListId),
                    (Kind: "content-type", Identity: source.ContentTypeId),
                    (Kind: "field", Identity: source.FieldId)
                };
                source.DependencyIngredientIds = bindings
                    .Select(value => value.Kind + ":" + value.Identity)
                    .ToList();
                source.Dependencies = bindings.Select(value =>
                {
                    var evidenceReference = "evidence/content-text/dependency-" + value.Kind + ".json";
                    var providerSchema = ContentTextEvidenceNormalizer.CreateDependencySchemaCanonicalJson(
                        source,
                        value.Kind,
                        value.Identity);
                    var providerBytes = Encoding.UTF8.GetBytes(providerSchema);
                    var providerDigest = MigrationDigest.ComputeSha256(providerBytes);
                    source.EvidenceReferences.Add(evidenceReference);
                    return new ContentTextDependencyEvidence
                    {
                        Kind = value.Kind,
                        ProviderIdentity = value.Identity,
                        ProviderArtifact = new ArtifactReference
                        {
                            Sha256 = providerDigest,
                            Length = providerBytes.LongLength,
                            MediaType = "application/json",
                            ContentEncoding = "utf-8"
                        },
                        ProviderArtifactBase64 = Convert.ToBase64String(providerBytes),
                        EvidenceDigest = ContentTextEvidenceNormalizer.CreateDependencyEvidenceDigest(
                            source,
                            value.Kind,
                            value.Identity,
                            evidenceReference,
                            providerDigest),
                        EvidenceReference = evidenceReference
                    };
                }).ToList();
                source.EvidenceReferences = source.EvidenceReferences
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
            }

            private static FixtureData LoadFixture([CallerFilePath] string callerFile = null)
            {
                var path = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(callerFile),
                    "../../../../Resources/Migration/Ingredients/ContentText/row-00029-semantic-slice.fixture.v1.json"));
                return JsonSerializer.Deserialize<FixtureData>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            private static List<string> References(params string[] names)
            {
                return names.Select(value => "evidence/content-text/" + value)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
            }
        }

        private sealed class FixtureData
        {
            public string Schema { get; set; }

            public string ClaimId { get; set; }

            public string IngredientId { get; set; }

            public SourceClaimData SourceClaim { get; set; }

            public SourceData Source { get; set; }

            public FieldData Field { get; set; }

            public SemanticSliceData SemanticSlice { get; set; }
        }

        private sealed class SourceClaimData
        {
            public string RawSha256 { get; set; }

            public string SemanticSha256 { get; set; }

            public string SourceSnapshotSha256 { get; set; }
        }

        private sealed class SourceData
        {
            public string PageUrl { get; set; }

            public string WebUrl { get; set; }

            public string FileServerRelativeUrl { get; set; }

            public string ListId { get; set; }

            public string ListTitle { get; set; }

            public int ListBaseTemplate { get; set; }

            public int ItemId { get; set; }

            public string FileUniqueId { get; set; }

            public string SourceVersion { get; set; }

            public string ContentTypeId { get; set; }

            public string ContentTypeName { get; set; }
        }

        private sealed class FieldData
        {
            public string Id { get; set; }

            public string InternalName { get; set; }

            public string Type { get; set; }

            public bool Required { get; set; }

            public bool ReadOnly { get; set; }

            public bool Hidden { get; set; }

            public bool Sealed { get; set; }
        }

        private sealed class SemanticSliceData
        {
            public string Provenance { get; set; }

            public string Raw { get; set; }

            public string Canonical { get; set; }

            public int RawBytes { get; set; }

            public string RawSha256 { get; set; }

            public string SemanticSha256 { get; set; }
        }
    }
}
