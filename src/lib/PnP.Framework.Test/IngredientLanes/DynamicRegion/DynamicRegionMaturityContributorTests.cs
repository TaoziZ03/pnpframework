using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.DynamicRegion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PnP.Framework.Test.IngredientLanes.DynamicRegion
{
    [TestClass]
    public class DynamicRegionMaturityContributorTests
    {
        [TestMethod]
        public void SourceFixturePassesM0AndM2WhileFreshTargetRuntimeRemainsClosed()
        {
            var fixture = Fixture.Create();
            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.PrimaryOwner).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SourceBinding).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity).Status);
        }

        [TestMethod]
        public void MatchingFreshRuntimeValuesCloseM1WithoutContributorAssignedMaturity()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();

            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();

            Assert.IsFalse(contribution.GetType().GetProperties().Any(value =>
                string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.PerValueObservation).Status);
        }

        [TestMethod]
        public void WrongFreshRuntimeValueFailsClosedAtM1()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();
            fixture.Evidence.Live.Observations.Single(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                && value.ValuePath == "region.definition.providerBinding").ValueDigest = new string('0', 64);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [DataTestMethod]
        [DataRow("wrong-source-version", IngredientMaturityGateCatalog.SourceBinding)]
        [DataRow("wrong-source-host", IngredientMaturityGateCatalog.PrimaryOwner)]
        [DataRow("wrong-provider-instance", IngredientMaturityGateCatalog.PrimaryOwner)]
        [DataRow("missing-config-digest", IngredientMaturityGateCatalog.PrimaryOwner)]
        [DataRow("missing-dom-boundary", IngredientMaturityGateCatalog.PrimaryOwner)]
        [DataRow("missing-provider-dependency", IngredientMaturityGateCatalog.PrimaryOwner)]
        [DataRow("historical-target-observation", IngredientMaturityGateCatalog.CupCollectFreshReadback)]
        [DataRow("corrupt-raw-artifact", IngredientMaturityGateCatalog.RawArtifactIntegrity)]
        [DataRow("legacy-newline-semantic-digest", IngredientMaturityGateCatalog.SemanticIntegrity)]
        public void WrongBindingPartialAndCorruptEvidenceFailClosed(string mutation, string expectedGate)
        {
            var fixture = Fixture.Create();
            fixture.Mutate(mutation);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, expectedGate).Status);
            Assert.IsFalse(assessment.Levels.Single(value => value.Level == IngredientMaturityLevel.M1).Passed);
        }

        [TestMethod]
        public void FixturePreservesLegacyDigestWithoutTreatingItAsPnpCanonicalDigest()
        {
            var fixture = Fixture.Create();

            Assert.AreEqual("1302815f0b22f2f5d8ebd80025c5a228286e7a2ffd343d9dc725e826438b7e2e",
                fixture.Evidence.Source.LegacyLineTerminatedSemanticDigest);
            Assert.AreEqual("18004b9fe69ab8ef4179d594d2af9d4fab5274b11edaef7be208cd97c155891f",
                fixture.Evidence.Source.SemanticDigest);
            Assert.AreNotEqual(fixture.Evidence.Source.LegacyLineTerminatedSemanticDigest,
                fixture.Evidence.Source.SemanticDigest);
        }

        private static IngredientMaturityGateResult Gate(IngredientMaturityAssessment assessment, string gateId)
        {
            return assessment.Levels.SelectMany(value => value.Gates).Single(value => value.GateId == gateId);
        }

        private sealed class Fixture
        {
            private readonly FixtureContract contract;
            private readonly string expectedSourceVersion;

            private Fixture(FixtureContract contract)
            {
                this.contract = contract;
                contract.Source.RawRegion = contract.Region;
                Evidence = CreateEvidence(contract.Source);
                Context = CreateContext(contract, contract.Source);
                expectedSourceVersion = contract.Source.SourceVersion;
            }

            public DynamicRegionMaturityEvidence Evidence { get; }

            public IngredientMaturityEvaluationContext Context { get; }

            public static Fixture Create()
            {
                return new Fixture(LoadContract());
            }

            public IngredientMaturityContribution Contribute()
            {
                return new DynamicRegionMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                var contributor = new DynamicRegionMaturityContributor(Evidence);
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[] { contributor }));
            }

            public void AddMatchingTargetObservations()
            {
                var targetTime = contract.Source.ObservedAtUtc.AddMinutes(1);
                foreach (var source in Evidence.Live.Observations.ToArray())
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ValuePath = source.ValuePath,
                        ValueDigest = source.ValueDigest,
                        ObservedAtUtc = targetTime,
                        Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                        EvidenceReference = "cupcollect-runtime.json#" + source.ValuePath
                    });
                }
                Evidence.Live.TargetFreshReadback = true;
                Evidence.Live.TargetEvidenceReferences.Add("cupcollect-runtime.json");
            }

            public void Mutate(string mutation)
            {
                switch (mutation)
                {
                    case "wrong-source-version":
                        Evidence.Source.SourceVersion = expectedSourceVersion + "-stale";
                        break;
                    case "wrong-source-host":
                        Evidence.Source.PageUrl = "https://microsoft.evil.sharepoint.com/sites/example/Pages/Search.aspx";
                        break;
                    case "wrong-provider-instance":
                        Evidence.Source.ProviderInstanceId = "11111111-1111-1111-1111-111111111111";
                        break;
                    case "missing-config-digest":
                        Evidence.Source.ConfigSha256 = null;
                        break;
                    case "missing-dom-boundary":
                        Evidence.Source.Boundary = null;
                        break;
                    case "missing-provider-dependency":
                        Evidence.Source.Dependencies.Remove(Evidence.Source.ProviderIngredientId);
                        break;
                    case "historical-target-observation":
                        AddMatchingTargetObservations();
                        foreach (var target in Evidence.Live.Observations.Where(value =>
                            value.Origin == IngredientObservationOrigin.CupCollectFreshReadback))
                        {
                            target.Origin = IngredientObservationOrigin.Historical;
                        }
                        break;
                    case "corrupt-raw-artifact":
                        Evidence.Source.RawArtifactSha256 = new string('0', 64);
                        break;
                    case "legacy-newline-semantic-digest":
                        Evidence.Source.SemanticDigest = Evidence.Source.LegacyLineTerminatedSemanticDigest;
                        break;
                    default:
                        Assert.Fail("Unknown mutation " + mutation);
                        break;
                }
            }

            private static DynamicRegionMaturityEvidence CreateEvidence(DynamicRegionSourceEvidence source)
            {
                var normalized = DynamicRegionEvidenceNormalizer.Normalize(null, source);
                return new DynamicRegionMaturityEvidence
                {
                    Source = source,
                    Live = new IngredientLiveEvidence
                    {
                        SourceAuthenticated = true,
                        TargetFreshReadback = false,
                        Observations = normalized.ValueDigests.Select(value => new IngredientValueObservation
                        {
                            ValuePath = value.Key,
                            ValueDigest = value.Value,
                            ObservedAtUtc = source.ObservedAtUtc,
                            Origin = IngredientObservationOrigin.AuthenticatedSource,
                            EvidenceReference = "search-results-row-00001-v41.json#" + value.Key
                        }).ToList(),
                        SourceEvidenceReferences = new List<string>
                        {
                            "search-results-row-00001-v41.json"
                        }
                    }
                };
            }

            private static IngredientMaturityEvaluationContext CreateContext(
                FixtureContract fixture,
                DynamicRegionSourceEvidence source)
            {
                return new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = fixture.ClaimId,
                        Lane = DynamicRegionEvidenceNormalizer.Lane,
                        IngredientId = fixture.IngredientId,
                        Kind = PageIngredientKind.Runtime,
                        Subtype = DynamicRegionEvidenceNormalizer.Subtype,
                        SemanticRole = DynamicRegionEvidenceNormalizer.SemanticRole,
                        SourcePredicateId = DynamicRegionEvidenceNormalizer.SourcePredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = DynamicRegionEvidenceNormalizer.CreateSourceIdentity(source),
                        SourceVersion = source.SourceVersion,
                        SourceArtifactDigest = source.SourceArtifactSha256,
                        SourceSnapshotDigest = source.LegacyLineTerminatedSemanticDigest
                    },
                    Target = new IngredientMaturityTargetBinding
                    {
                        TargetProfile = "cupcollect-classic-page/v1",
                        TargetIdentity = "runtime-region:cupcollect/search.aspx/search-results"
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = "ccd-182-v1",
                        ImplementationCommit = "aff892f119a20dd764af6b005087e7af5dad3a1c"
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.MitigationPending,
                        Status = IngredientTechnicalStatus.Conditional,
                        ReasonCode = "fresh-cupcollect-runtime-required"
                    }
                };
            }

            private static FixtureContract LoadContract([CallerFilePath] string callerPath = null)
            {
                var path = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(callerPath),
                    "../../Resources/IngredientLanes/dynamic.region/v1/search-results-row-00001-v41.json"));
                return JsonSerializer.Deserialize<FixtureContract>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }

        private sealed class FixtureContract
        {
            public string ClaimId { get; set; }

            public string IngredientId { get; set; }

            public DynamicRegionSourceEvidence Source { get; set; }

            public JsonElement Region { get; set; }
        }
    }
}
