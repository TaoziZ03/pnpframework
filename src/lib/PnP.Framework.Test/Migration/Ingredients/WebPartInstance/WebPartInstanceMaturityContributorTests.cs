using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Ingredients.WebPartInstance;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Ingredients.WebPartInstance
{
    [TestClass]
    public class WebPartInstanceMaturityContributorTests
    {
        [TestMethod]
        public void AuthoritativeListViewFixturePassesM0AndM2ButMissingTargetStopsAtM0()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();

            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();

            Assert.IsFalse(contribution.GetType().GetProperties().Any(value =>
                string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(fixture.Data.Instance.NormalizedRestPayloadSha256, fixture.Normalized.RawPayloadSha256);
            AssertGate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.SourceBinding, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
            AssertGate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void FreshExactTargetReadbackClosesM1AndM2()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PerValueObservation, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void MissingAuthoritativeDefinitionFailsClosed()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.DefinitionTypeName = null;
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void WrongInstanceGuidFailsTheOwnerPredicateEvenWithRecomputedPayload()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.InstanceId = "11111111-1111-1111-1111-111111111111";
            fixture.RebindSourceAndPayload();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void PropertyTypeCollapseFailsEvenWhenPayloadDigestIsRecomputed()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.NormalizedPropertiesCanonicalJson =
                fixture.Evidence.Source.NormalizedPropertiesCanonicalJson.Replace(
                    "\"AllowClose\":true",
                    "\"AllowClose\":\"true\"");
            fixture.RebindSourceAndPayload();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
            AssertGate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void WrongBoundViewFailsClosedWithoutMaterializingTheDependency()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.BoundViewId = "22222222-2222-2222-2222-222222222222";
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void MissingListSchemaDependencyFailsClosed()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.DependencyIngredientIds = fixture.Evidence.Source.DependencyIngredientIds
                .Where(value => !value.StartsWith("list-schema:", StringComparison.Ordinal))
                .ToList();
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void TargetOrderDriftFailsFreshReadback()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            var targetOrder = fixture.Evidence.Live.Observations.Single(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                && value.ValuePath == "placement.zoneIndex");
            targetOrder.ValueDigest = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonical(1));

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void UnexpectedTargetObservationFailsFreshReadback()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            var targetObservedAt = fixture.Evidence.Live.Observations.First(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback).ObservedAtUtc;
            fixture.Evidence.Live.Observations.Add(new IngredientValueObservation
            {
                ValuePath = "unexpected.persisted-state",
                ValueDigest = MigrationDigest.ComputeSha256(
                    MigrationContractSerializer.SerializeCanonical("unexpected")),
                ObservedAtUtc = targetObservedAt,
                Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                EvidenceReference = "evidence/webpart-instance/target-unexpected.persisted-state.json"
            });

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void MissingTargetObservationPathFailsFreshReadbackWithoutThrowing()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            fixture.Evidence.Live.Observations.First(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback).ValuePath = null;

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void StaleSourceVersionBindingFailsM0()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.SourceVersion = "\"{D4FDA766-A38D-4122-98CD-DDD3406D5A0A},594\"";
            fixture.Normalized = WebPartInstanceEvidenceNormalizer.Normalize(
                fixture.Context,
                fixture.Evidence.Source,
                fixture.Evidence.TargetExpectation);
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.SourceBinding, IngredientMaturityGateStatus.Failed);
        }

        private static void AssertGate(
            IngredientMaturityAssessment assessment,
            string gateId,
            IngredientMaturityGateStatus expected)
        {
            var gate = assessment.Levels.SelectMany(value => value.Gates)
                .Single(value => string.Equals(value.GateId, gateId, StringComparison.Ordinal));
            Assert.AreEqual(expected, gate.Status, gateId + ": " + gate.FailureReason);
        }

        private sealed class Fixture
        {
            private static readonly DateTimeOffset SourceObservedAt =
                new DateTimeOffset(2026, 9, 9, 19, 29, 0, TimeSpan.Zero);

            public FixtureData Data { get; private set; }

            public WebPartInstanceMaturityEvidence Evidence { get; private set; }

            public IngredientMaturityEvaluationContext Context { get; private set; }

            public WebPartInstanceNormalization Normalized { get; set; }

            public static Fixture Create()
            {
                var data = LoadFixture();
                var source = new WebPartInstanceSourceEvidence
                {
                    PageListId = data.Source.PageListId,
                    PageItemId = data.Source.PageItemId,
                    PageUniqueId = data.Source.PageUniqueId,
                    PageServerRelativeUrl = data.Source.PageServerRelativeUrl,
                    SourceVersion = data.Source.SourceVersion,
                    InstanceId = data.Instance.InstanceId,
                    Title = data.Instance.Title,
                    RestTypeName = data.Instance.RestTypeName,
                    DefinitionTypeName = data.Instance.DefinitionTypeName,
                    AssemblyIdentity = data.Instance.AssemblyIdentity,
                    Family = data.Instance.Family,
                    Scope = data.Instance.Scope,
                    ZoneId = data.Instance.ZoneId,
                    ZoneIndex = data.Instance.ZoneIndex,
                    NormalizedPropertiesCanonicalJson =
                        MigrationContractSerializer.SerializeCanonical(data.Instance.NormalizedProperties),
                    NormalizedRestPayloadSha256 = data.Instance.NormalizedRestPayloadSha256,
                    BoundListId = data.Binding.BoundListId,
                    BoundViewId = data.Binding.BoundViewId,
                    ViewType = data.Binding.ViewType,
                    BaseViewId = data.Binding.BaseViewId,
                    Availability = WebPartInstanceAvailability.Captured,
                    DependencyIngredientIds = data.DependencyIngredientIds.ToList(),
                    EvidenceReferences = data.EvidenceReferences.ToList()
                };
                var fixture = new Fixture
                {
                    Data = data,
                    Evidence = new WebPartInstanceMaturityEvidence
                    {
                        Source = source,
                        TargetExpectation = new WebPartInstanceTargetExpectation
                        {
                            TargetInstanceId = "33333333-3333-3333-3333-333333333333",
                            TargetListId = "44444444-4444-4444-4444-444444444444",
                            TargetViewId = "55555555-5555-5555-5555-555555555555"
                        }
                    }
                };
                fixture.Context = new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = data.ClaimId,
                        WorkItemType = IngredientMaturityContract.CanonicalIngredientWorkItem,
                        Lane = WebPartInstanceEvidenceNormalizer.Lane,
                        IngredientId = data.CanonicalIngredientId,
                        Kind = PageIngredientKind.WebPart,
                        Subtype = WebPartInstanceEvidenceNormalizer.Subtype,
                        SemanticRole = WebPartInstanceEvidenceNormalizer.SemanticRole,
                        SourcePredicateId = WebPartInstanceEvidenceNormalizer.SourcePredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = WebPartInstanceEvidenceNormalizer.CreateSourceIdentity(source),
                        SourceVersion = source.SourceVersion,
                        SourceArtifactDigest = source.NormalizedRestPayloadSha256,
                        SourceSnapshotDigest = data.SourceSnapshotSha256
                    },
                    Target = new IngredientMaturityTargetBinding
                    {
                        TargetProfile = "cupcollect-classic-page/v1",
                        TargetIdentity = "cupcollect:ccd-owned:webpart-instance-canary"
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = WebPartInstanceEvidenceNormalizer.ContributorId,
                        ImplementationCommit = "aff892f119a20dd764af6b005087e7af5dad3a1c"
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.ExecutableWithLoss,
                        Status = IngredientTechnicalStatus.Conditional,
                        ReasonCode = "TARGET_NATIVE_LIST_VIEW_RECEIPTS_PENDING"
                    }
                };
                fixture.RefreshNormalization();
                return fixture;
            }

            public void RefreshNormalization()
            {
                Normalized = WebPartInstanceEvidenceNormalizer.Normalize(
                    Context,
                    Evidence.Source,
                    Evidence.TargetExpectation);
                Evidence.Live = new IngredientLiveEvidence
                {
                    SourceAuthenticated = true,
                    TargetFreshReadback = false,
                    SourceEvidenceReferences = References("source-live.json"),
                    TargetEvidenceReferences = References("target-live.json")
                };
            }

            public void RebindSourceAndPayload()
            {
                var provisional = WebPartInstanceEvidenceNormalizer.Normalize(
                    Context,
                    Evidence.Source,
                    Evidence.TargetExpectation);
                Evidence.Source.NormalizedRestPayloadSha256 = provisional.RawPayloadSha256;
                Context.Source.PageOrListItemIdentity = WebPartInstanceEvidenceNormalizer.CreateSourceIdentity(Evidence.Source);
                Context.Source.SourceArtifactDigest = provisional.RawPayloadSha256;
                RefreshNormalization();
            }

            public void AddSourceObservations()
            {
                AddObservations(
                    IngredientObservationOrigin.AuthenticatedSource,
                    Normalized.SourceObservationDigests,
                    SourceObservedAt,
                    "source");
            }

            public void AddTargetObservations()
            {
                AddObservations(
                    IngredientObservationOrigin.CupCollectFreshReadback,
                    Normalized.TargetObservationDigests,
                    SourceObservedAt.AddSeconds(1),
                    "target");
                Evidence.Live.TargetFreshReadback = true;
            }

            public IngredientMaturityContribution Contribute()
            {
                return new WebPartInstanceMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[]
                    {
                        (IIngredientMaturityContributor)new WebPartInstanceMaturityContributor(Evidence)
                    }));
            }

            private void AddObservations(
                IngredientObservationOrigin origin,
                IReadOnlyDictionary<string, string> digests,
                DateTimeOffset observedAt,
                string prefix)
            {
                foreach (var pair in digests.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ValuePath = pair.Key,
                        ValueDigest = pair.Value,
                        ObservedAtUtc = observedAt,
                        Origin = origin,
                        EvidenceReference = "evidence/webpart-instance/" + prefix + "-" + pair.Key + ".json"
                    });
                }
            }

            private static FixtureData LoadFixture([CallerFilePath] string callerFile = null)
            {
                var path = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(callerFile),
                    "../../../Resources/Migration/Ingredients/WebPartInstance/ccd109-row00032-v595-listview.fixture.v1.json"));
                return JsonSerializer.Deserialize<FixtureData>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            private static List<string> References(params string[] names)
            {
                return names.Select(value => "evidence/webpart-instance/" + value)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
            }
        }

        private sealed class FixtureData
        {
            public string Schema { get; set; }

            public string ClaimId { get; set; }

            public string CanonicalIngredientId { get; set; }

            public string SourceSnapshotSha256 { get; set; }

            public SourceData Source { get; set; }

            public InstanceData Instance { get; set; }

            public BindingData Binding { get; set; }

            public IList<string> DependencyIngredientIds { get; set; }

            public IList<string> EvidenceReferences { get; set; }
        }

        private sealed class SourceData
        {
            public string PageListId { get; set; }

            public int PageItemId { get; set; }

            public string PageUniqueId { get; set; }

            public string PageServerRelativeUrl { get; set; }

            public string SourceVersion { get; set; }
        }

        private sealed class InstanceData
        {
            public string InstanceId { get; set; }

            public string Title { get; set; }

            public string RestTypeName { get; set; }

            public string DefinitionTypeName { get; set; }

            public string AssemblyIdentity { get; set; }

            public string Family { get; set; }

            public string Scope { get; set; }

            public string ZoneId { get; set; }

            public int ZoneIndex { get; set; }

            public string NormalizedRestPayloadSha256 { get; set; }

            public JsonElement NormalizedProperties { get; set; }
        }

        private sealed class BindingData
        {
            public string BoundListId { get; set; }

            public string BoundViewId { get; set; }

            public string ViewType { get; set; }

            public int BaseViewId { get; set; }
        }
    }
}
