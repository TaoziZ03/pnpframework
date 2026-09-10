using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Assessment.Maturity.BehaviorInteraction;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Ingredients.BehaviorInteraction
{
    [TestClass]
    public class BehaviorInteractionMaturityContributorTests
    {
        [TestMethod]
        public void SearchSubmitFixturePassesM0AndM2WithoutPersistedIngredientNode()
        {
            var fixture = Fixture.Create();
            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();

            Assert.IsFalse(fixture.Context.Identity.Kind.HasValue);
            Assert.AreEqual(IngredientMaturityContract.RuntimeVerificationWorkItem, fixture.Context.Identity.WorkItemType);
            Assert.IsFalse(contribution.GetType().GetProperties().Any(value =>
                string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
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
        public void MappedFreshTargetTopologyClosesM1ThroughSharedEvaluator()
        {
            var fixture = Fixture.Create();
            fixture.AddMappedTargetTopologyReadback();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.PerValueObservation).Status);
        }

        [DataTestMethod]
        [DataRow("missing-provider")]
        [DataRow("wrong-mapping")]
        [DataRow("wrong-group")]
        [DataRow("wrong-searchbox-config")]
        [DataRow("stale-readback")]
        [DataRow("cleanup-before-runtime")]
        public void InvalidResultProviderTopologyStopsBeforeSubmit(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.MutateTargetTopology(mutation);
            fixture.AddMappedTargetTopologyReadback();

            var normalized = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(
                fixture.Context,
                fixture.Evidence.Source);
            var assessment = fixture.Evaluate();
            var targetGate = Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback);

            Assert.IsFalse(normalized.TargetRuntimePreconditionPassed);
            Assert.AreEqual(
                BehaviorInteractionSearchSubmitEvidenceNormalizer.DependentResultProviderMissing,
                normalized.TargetRuntimePreconditionReasonCode);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, targetGate.Status);
            StringAssert.StartsWith(
                targetGate.FailureReason,
                BehaviorInteractionSearchSubmitEvidenceNormalizer.DependentResultProviderMissing + ":");
        }

        [DataTestMethod]
        [DataRow("wrong-target-searchbox")]
        [DataRow("searchbox-is-provider")]
        [DataRow("unrelated-target-page")]
        [DataRow("wrong-source-page-mapping")]
        [DataRow("unrelated-lease-id")]
        [DataRow("unrelated-claim-lease")]
        public void IndependentTargetConsumerAndLeaseBindingsFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.MutateTargetTopology(mutation);
            fixture.AddMappedTargetTopologyReadback();

            var normalized = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(
                fixture.Context,
                fixture.Evidence.Source);
            var assessment = fixture.Evaluate();

            Assert.IsFalse(normalized.TargetRuntimePreconditionPassed);
            Assert.AreEqual(
                IngredientMaturityGateStatus.Failed,
                Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [TestMethod]
        public void TargetOnlyRefreshDoesNotResealSourceSemanticSnapshot()
        {
            var fixture = Fixture.Create();
            var sourceDigest = fixture.Evidence.Source.SemanticDigest;
            var sourceSnapshotDigest = fixture.Context.Source.SourceSnapshotDigest;
            var before = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(
                fixture.Context,
                fixture.Evidence.Source).SemanticCanonicalJson;

            fixture.Evidence.Source.ResultScriptTopology.TargetProvider.ObservedAtUtc =
                fixture.Evidence.Source.ResultScriptTopology.TargetProvider.ObservedAtUtc.AddSeconds(5);
            fixture.Evidence.Source.ResultScriptTopology.TargetSearchBox.ObservedAtUtc =
                fixture.Evidence.Source.ResultScriptTopology.TargetSearchBox.ObservedAtUtc.AddSeconds(5);
            fixture.ReplaceMappedTargetTopologyReadback();

            var after = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(
                fixture.Context,
                fixture.Evidence.Source).SemanticCanonicalJson;
            var assessment = fixture.Evaluate();

            Assert.AreEqual(sourceDigest, fixture.Evidence.Source.SemanticDigest);
            Assert.AreEqual(sourceSnapshotDigest, fixture.Context.Source.SourceSnapshotDigest);
            Assert.AreEqual(before, after);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [TestMethod]
        public void SourceDiscoverabilitySurvivesMissingTargetOperationalEvidence()
        {
            var fixture = Fixture.Create();
            fixture.RemoveTargetOperationalEvidence();

            var normalized = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(
                fixture.Context,
                fixture.Evidence.Source);
            var assessment = fixture.Evaluate();

            Assert.IsTrue(normalized.SourcePredicateMatched);
            Assert.IsFalse(normalized.TargetRuntimePreconditionPassed);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity).Status);
        }

        [TestMethod]
        public void MismatchedFreshTargetTransitionValueFailsClosed()
        {
            var fixture = Fixture.Create();
            fixture.AddMappedTargetTopologyReadback();
            fixture.Evidence.Live.Observations.Single(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                && value.ValuePath == "action.maximumAttempts").ValueDigest = new string('0', 64);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [TestMethod]
        public void HistoricalSubstitutionCannotCloseFreshTargetGate()
        {
            var fixture = Fixture.Create();
            fixture.AddMappedTargetTopologyReadback();
            fixture.Evidence.Live.HistoricalOrSyntheticSubstitution = true;

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [DataTestMethod]
        [DataRow("wrong-trigger-owner")]
        [DataRow("canonical-locator")]
        [DataRow("unbounded-timeout")]
        [DataRow("too-many-attempts")]
        [DataRow("missing-forbidden-boundary")]
        [DataRow("missing-typed-verdict")]
        [DataRow("foreign-action")]
        [DataRow("stale-version")]
        [DataRow("wrong-source-digest")]
        [DataRow("corrupt-raw-artifact")]
        [DataRow("wrong-semantic-digest")]
        public void UnsafeStaleCorruptAndWrongBindingCasesFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.Mutate(mutation);

            var assessment = fixture.Evaluate();
            var failed = assessment.Levels.SelectMany(value => value.Gates)
                .Where(value => value.Status == IngredientMaturityGateStatus.Failed)
                .Select(value => value.GateId)
                .ToArray();

            Assert.IsTrue(failed.Contains(IngredientMaturityGateCatalog.CanonicalIdentity, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.SourceBinding, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.RawArtifactIntegrity, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.SemanticIntegrity, StringComparer.Ordinal));
        }

        private static IngredientMaturityGateResult Gate(IngredientMaturityAssessment assessment, string gateId)
        {
            return assessment.Levels.SelectMany(value => value.Gates).Single(value => value.GateId == gateId);
        }

        private sealed class Fixture
        {
            private Fixture(
                IngredientMaturityEvaluationContext context,
                BehaviorInteractionMaturityEvidence evidence)
            {
                Context = context;
                Evidence = evidence;
            }

            public IngredientMaturityEvaluationContext Context { get; }

            public BehaviorInteractionMaturityEvidence Evidence { get; }

            public static Fixture Create()
            {
                var fixtureJson = LoadFixtureText();
                using var document = JsonDocument.Parse(fixtureJson);
                var root = document.RootElement;
                var source = root.GetProperty("sourceBinding");
                var assertionValue = root.GetProperty("assertion");
                var references = assertionValue.GetProperty("attachedActionReferences")
                    .EnumerateArray()
                    .Select(value => new RuntimeVerificationActionReference
                    {
                        IngredientId = value.GetProperty("ingredientId").GetString(),
                        ActionId = value.GetProperty("actionId").GetString(),
                        DependencyRole = value.GetProperty("dependencyRole").GetString()
                    })
                    .ToList();
                var actionValue = assertionValue.GetProperty("action");
                var assertion = new RuntimeVerificationAssertion
                {
                    AssertionId = assertionValue.GetProperty("assertionId").GetString(),
                    AssertionOwnerLane = assertionValue.GetProperty("assertionOwnerLane").GetString(),
                    Subtype = root.GetProperty("subtype").GetString(),
                    SemanticRole = root.GetProperty("semanticRole").GetString(),
                    SourcePredicateId = root.GetProperty("sourcePredicate").GetProperty("id").GetString(),
                    SourcePredicateVersion = root.GetProperty("sourcePredicate").GetProperty("version").GetString(),
                    SourcePageOrListItemIdentity = source.GetProperty("pageOrListItemIdentity").GetString(),
                    SourceVersionIdentity = source.GetProperty("sourceVersionIdentity").GetString(),
                    SourceEvidenceDigestSha256 = source.GetProperty("sourceEvidenceDigestSha256").GetString(),
                    StableAssertionKey = assertionValue.GetProperty("stableAssertionKey").GetString(),
                    TargetProfileId = assertionValue.GetProperty("targetProfileId").GetString(),
                    FixtureContractVersion = root.GetProperty("schemaVersion").GetString(),
                    AttachedActionReferences = references,
                    Intent = new RuntimeVerificationAssertionIntent
                    {
                        InitialState = State(assertionValue.GetProperty("initialState")),
                        Action = new RuntimeVerificationActionIntent
                        {
                            ActionId = actionValue.GetProperty("actionId").GetString(),
                            Kind = actionValue.GetProperty("kind").GetString(),
                            Selector = actionValue.GetProperty("selector").GetString(),
                            Input = actionValue.GetProperty("input").GetString(),
                            TimeoutMilliseconds = actionValue.GetProperty("timeoutMilliseconds").GetInt32()
                        },
                        ExpectedFinalState = State(assertionValue.GetProperty("expectedFinalState"))
                    }
                };
                var nodes = references.Select(reference => new PageIngredientNode
                {
                    Id = reference.IngredientId,
                    Kind = reference.IngredientId.Contains("dynamic.region", StringComparison.Ordinal)
                        ? PageIngredientKind.Runtime
                        : PageIngredientKind.WebPart,
                    HasContent = true,
                    PrimaryOwnerLane = reference.IngredientId.Contains("dynamic.region", StringComparison.Ordinal)
                        ? "dynamic.region"
                        : "webpart.instance"
                }).ToList();
                var actions = references.Select(reference => new PageIngredientAction
                {
                    ActionId = reference.ActionId,
                    IngredientId = reference.IngredientId,
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Preserve,
                    TargetIdentity = root.GetProperty("resultScriptConsumerTopology")
                        .GetProperty("targetMapping").GetProperty("targetIdentity").GetString()
                }).ToList();
                var trigger = assertionValue.GetProperty("trigger");
                var target = assertionValue.GetProperty("target");
                var boundary = assertionValue.GetProperty("runtimeBoundary");
                var verdicts = assertionValue.GetProperty("typedVerdictPolicy");
                var configuration = root.GetProperty("sourceConfiguration");
                var topology = root.GetProperty("resultScriptConsumerTopology");
                var bytes = Encoding.UTF8.GetBytes(fixtureJson);
                var sourceEvidence = new BehaviorInteractionSearchSubmitSourceEvidence
                {
                    Assertion = assertion,
                    IngredientGraph = new CanonicalPageIngredientGraph
                    {
                        SchemaVersion = CanonicalPageIngredientGraph.SchemaVersionV2,
                        Nodes = nodes
                    },
                    IngredientActions = actions,
                    Trigger = new BehaviorInteractionEndpointEvidence
                    {
                        CanonicalOwnerInstanceId = trigger.GetProperty("canonicalOwnerInstanceId").GetString(),
                        LogicalRole = trigger.GetProperty("logicalControlRole").GetString(),
                        SourceObservationLocator = trigger.GetProperty("sourceObservationLocator").GetString(),
                        LocatorIsCanonicalIdentity = trigger.GetProperty("locatorIsCanonicalIdentity").GetBoolean()
                    },
                    Target = new BehaviorInteractionEndpointEvidence
                    {
                        CanonicalOwnerInstanceId = target.GetProperty("canonicalOwnerInstanceId").GetString(),
                        LogicalRole = target.GetProperty("logicalRegionRole").GetString(),
                        SourceObservationLocator = target.GetProperty("sourceObservationLocator").GetString(),
                        LocatorIsCanonicalIdentity = target.GetProperty("locatorIsCanonicalIdentity").GetBoolean()
                    },
                    MaximumAttempts = actionValue.GetProperty("maximumAttempts").GetInt32(),
                    RuntimeBoundary = new BehaviorInteractionRuntimeBoundaryEvidence
                    {
                        Allowed = Strings(boundary.GetProperty("allowed")),
                        Forbidden = Strings(boundary.GetProperty("forbidden"))
                    },
                    TypedVerdictPolicy = new BehaviorInteractionTypedVerdictPolicy
                    {
                        Pass = verdicts.GetProperty("pass").GetString(),
                        Conditional = verdicts.GetProperty("conditional").GetString(),
                        Unsupported = verdicts.GetProperty("unsupported").GetString(),
                        Unknown = verdicts.GetProperty("unknown").GetString(),
                        Fail = verdicts.GetProperty("fail").GetString()
                    },
                    SearchConfiguration = Configuration(configuration),
                    ResultScriptTopology = ResultScriptTopology(topology),
                    RawArtifact = new ArtifactReference
                    {
                        Sha256 = MigrationDigest.ComputeSha256(bytes),
                        Length = bytes.Length,
                        MediaType = "application/json",
                        OriginalName = "search-submit.fixture.v1.json"
                    },
                    RawArtifactBase64 = Convert.ToBase64String(bytes),
                    SemanticDigest = source.GetProperty("freshNormalizedSemanticDigestSha256").GetString(),
                    EvidenceReferences = new List<string> { "search-submit.fixture.v1.json" }
                };
                var context = new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = root.GetProperty("claimId").GetString(),
                        WorkItemType = IngredientMaturityContract.RuntimeVerificationWorkItem,
                        Lane = BehaviorInteractionSearchSubmitEvidenceNormalizer.Lane,
                        IngredientId = assertion.AssertionId,
                        Kind = null,
                        Subtype = assertion.Subtype,
                        SemanticRole = assertion.SemanticRole,
                        SourcePredicateId = assertion.SourcePredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = assertion.SourcePageOrListItemIdentity,
                        SourceVersion = assertion.SourceVersionIdentity,
                        SourceArtifactDigest = assertion.SourceEvidenceDigestSha256,
                        SourceSnapshotDigest = source.GetProperty("freshNormalizedSemanticDigestSha256").GetString()
                    },
                    Target = new IngredientMaturityTargetBinding
                    {
                        TargetProfile = assertion.TargetProfileId,
                        TargetIdentity = topology.GetProperty("targetMapping").GetProperty("targetIdentity").GetString()
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = "ccd-182-v1",
                        ImplementationCommit = "da82fac00012e504f085d16518f781e2fc7a2525"
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.MitigationPending,
                        Status = IngredientTechnicalStatus.Unverified,
                        ReasonCode = "fresh-cupcollect-runtime-receipt-required"
                    }
                };
                var normalized = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(context, sourceEvidence);
                var recomputedSemanticDigest = MigrationDigest.ComputeSha256(normalized.SemanticCanonicalJson);
                if (!string.Equals(sourceEvidence.SemanticDigest, recomputedSemanticDigest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The fixture source semantic digest does not match the v2 projection. Expected "
                        + recomputedSemanticDigest + ".");
                }
                var observations = normalized.SourceValueDigests.Select(value => new IngredientValueObservation
                {
                    ValuePath = value.Key,
                    ValueDigest = value.Value,
                    ObservedAtUtc = DateTimeOffset.Parse("2026-09-09T19:28:07.652Z"),
                    Origin = IngredientObservationOrigin.AuthenticatedSource,
                    EvidenceReference = "source-observation.json#" + value.Key
                }).ToList();
                return new Fixture(
                    context,
                    new BehaviorInteractionMaturityEvidence
                    {
                        Source = sourceEvidence,
                        Live = new IngredientLiveEvidence
                        {
                            SourceAuthenticated = true,
                            TargetFreshReadback = false,
                            HistoricalOrSyntheticSubstitution = false,
                            Observations = observations,
                            SourceEvidenceReferences = new List<string> { "source-observation.json" }
                        }
                    });
            }

            public IngredientMaturityContribution Contribute()
            {
                return new BehaviorInteractionMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                var contributor = new BehaviorInteractionMaturityContributor(Evidence);
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[] { contributor }));
            }

            public void AddMappedTargetTopologyReadback()
            {
                var normalized = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(Context, Evidence.Source);
                foreach (var target in normalized.TargetValueDigests)
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ValuePath = target.Key,
                        ValueDigest = target.Value,
                        ObservedAtUtc = Evidence.Source.ResultScriptTopology.TargetProvider?.ObservedAtUtc
                            ?? Evidence.Source.ResultScriptTopology.RuntimeFinalEvidenceAtUtc,
                        Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                        EvidenceReference = "cupcollect-runtime-readback.json#" + target.Key
                    });
                }
                Evidence.Live.TargetFreshReadback = true;
                Evidence.Live.TargetEvidenceReferences.Add("cupcollect-runtime-readback.json");
            }

            public void ReplaceMappedTargetTopologyReadback()
            {
                Evidence.Live.Observations = Evidence.Live.Observations
                    .Where(value => value.Origin != IngredientObservationOrigin.CupCollectFreshReadback)
                    .ToList();
                Evidence.Live.TargetEvidenceReferences.Clear();
                Evidence.Live.TargetFreshReadback = false;
                AddMappedTargetTopologyReadback();
            }

            public void RemoveTargetOperationalEvidence()
            {
                var topology = Evidence.Source.ResultScriptTopology;
                topology.TargetProvider = null;
                topology.TargetSearchBox = null;
                topology.TargetMapping = null;
                topology.Lease = null;
                Evidence.Live.TargetFreshReadback = false;
            }

            public void MutateTargetTopology(string mutation)
            {
                var topology = Evidence.Source.ResultScriptTopology;
                switch (mutation)
                {
                    case "missing-provider":
                        topology.TargetProvider = null;
                        break;
                    case "wrong-mapping":
                        topology.TargetMapping.TargetProviderInstanceId = "11111111-1111-1111-1111-111111111111";
                        break;
                    case "wrong-group":
                        topology.TargetProvider.QueryGroupName = "WrongGroup";
                        break;
                    case "wrong-searchbox-config":
                        topology.TargetSearchBox.Configuration.TryInplaceQuery = false;
                        break;
                    case "stale-readback":
                        topology.TargetProvider.ObservedAtUtc = topology.TargetReadbackNotBeforeUtc.AddSeconds(-1);
                        break;
                    case "cleanup-before-runtime":
                        topology.TargetProvider.Availability = BehaviorInteractionProviderAvailability.Cleaned;
                        topology.TargetProvider.CleanupObservedAtUtc = topology.RuntimeFinalEvidenceAtUtc.AddMinutes(-1);
                        topology.Lease.Status = "released";
                        topology.Lease.ReleasedAtUtc = topology.TargetProvider.CleanupObservedAtUtc;
                        break;
                    case "wrong-target-searchbox":
                        topology.TargetMapping.TargetSearchBoxInstanceId = "11111111-1111-1111-1111-111111111111";
                        break;
                    case "searchbox-is-provider":
                        topology.TargetMapping.TargetSearchBoxInstanceId = topology.TargetProvider.CanonicalProviderInstanceId;
                        topology.TargetSearchBox.CanonicalSearchBoxInstanceId = topology.TargetProvider.CanonicalProviderInstanceId;
                        topology.Lease.SearchBoxInstanceId = topology.TargetProvider.CanonicalProviderInstanceId;
                        break;
                    case "unrelated-target-page":
                        const string unrelatedPage = "https://a830edad9050849cupcollect.sharepoint.com/sites/unrelated/Pages/Search.aspx";
                        topology.TargetProvider.PageIdentity = unrelatedPage;
                        topology.TargetSearchBox.PageIdentity = unrelatedPage;
                        topology.TargetMapping.TargetPageIdentity = unrelatedPage;
                        break;
                    case "wrong-source-page-mapping":
                        topology.TargetMapping.SourcePageIdentity = "urn:sha256:1111111111111111111111111111111111111111111111111111111111111111";
                        topology.TargetMapping.SourcePageVersion = "\"{11111111-1111-1111-1111-111111111111},1\"";
                        break;
                    case "unrelated-claim-lease":
                        topology.Lease.LeaseId = "unrelated-claim-lease-v1";
                        topology.Lease.ClaimId = new string('1', 64);
                        topology.Lease.EvidenceReference = "ccd.shared-target-consumer-lease/v1#unrelated-claim-lease-v1";
                        break;
                    case "unrelated-lease-id":
                        topology.Lease.LeaseId = "unrelated-lease-id-v1";
                        topology.Lease.EvidenceReference = "ccd.shared-target-consumer-lease/v1#unrelated-lease-id-v1";
                        break;
                    default:
                        Assert.Fail("Unknown target topology mutation " + mutation);
                        break;
                }
            }

            public void Mutate(string mutation)
            {
                switch (mutation)
                {
                    case "wrong-trigger-owner":
                        Evidence.Source.Trigger.CanonicalOwnerInstanceId = "11111111-1111-1111-1111-111111111111";
                        break;
                    case "canonical-locator":
                        Evidence.Source.Trigger.LocatorIsCanonicalIdentity = true;
                        break;
                    case "unbounded-timeout":
                        Evidence.Source.Assertion.Intent.Action.TimeoutMilliseconds = 15001;
                        break;
                    case "too-many-attempts":
                        Evidence.Source.MaximumAttempts = 3;
                        break;
                    case "missing-forbidden-boundary":
                        Evidence.Source.RuntimeBoundary.Forbidden.Remove("destructive form submission");
                        break;
                    case "missing-typed-verdict":
                        Evidence.Source.TypedVerdictPolicy.Unknown = null;
                        break;
                    case "foreign-action":
                        Evidence.Source.Assertion.Intent.Action.ActionId = "action:foreign";
                        break;
                    case "stale-version":
                        Evidence.Source.Assertion.SourceVersionIdentity += "-stale";
                        break;
                    case "wrong-source-digest":
                        Evidence.Source.Assertion.SourceEvidenceDigestSha256 = new string('0', 64);
                        break;
                    case "corrupt-raw-artifact":
                        Evidence.Source.RawArtifact.Sha256 = new string('0', 64);
                        break;
                    case "wrong-semantic-digest":
                        Evidence.Source.SemanticDigest = new string('0', 64);
                        break;
                    default:
                        Assert.Fail("Unknown mutation " + mutation);
                        break;
                }
            }

            private static BehaviorInteractionResultScriptConsumerTopologyEvidence ResultScriptTopology(
                JsonElement value)
            {
                var source = value.GetProperty("sourceProvider");
                var target = value.GetProperty("targetProvider");
                var searchBox = value.GetProperty("targetSearchBox");
                var mapping = value.GetProperty("targetMapping");
                var lease = value.GetProperty("lease");
                return new BehaviorInteractionResultScriptConsumerTopologyEvidence
                {
                    SourceProvider = Provider(source),
                    TargetProvider = Provider(target),
                    TargetSearchBox = new BehaviorInteractionSearchBoxTargetEvidence
                    {
                        CanonicalSearchBoxInstanceId = searchBox.GetProperty("canonicalSearchBoxInstanceId").GetString(),
                        PageIdentity = searchBox.GetProperty("pageIdentity").GetString(),
                        PageVersion = searchBox.GetProperty("pageVersion").GetString(),
                        Configuration = Configuration(searchBox.GetProperty("configuration")),
                        ObservedAtUtc = searchBox.GetProperty("observedAtUtc").GetDateTimeOffset(),
                        OperationReference = searchBox.GetProperty("operationReference").GetString(),
                        MarkerReference = searchBox.GetProperty("markerReference").GetString()
                    },
                    TargetMapping = new BehaviorInteractionResultScriptTargetMappingEvidence
                    {
                        SourceSearchBoxInstanceId = mapping.GetProperty("sourceSearchBoxInstanceId").GetString(),
                        TargetSearchBoxInstanceId = mapping.GetProperty("targetSearchBoxInstanceId").GetString(),
                        SourceProviderInstanceId = mapping.GetProperty("sourceProviderInstanceId").GetString(),
                        TargetProviderInstanceId = mapping.GetProperty("targetProviderInstanceId").GetString(),
                        SourceDynamicRegionId = mapping.GetProperty("sourceDynamicRegionId").GetString(),
                        TargetDynamicRegionId = mapping.GetProperty("targetDynamicRegionId").GetString(),
                        SourcePageIdentity = mapping.GetProperty("sourcePageIdentity").GetString(),
                        SourcePageVersion = mapping.GetProperty("sourcePageVersion").GetString(),
                        TargetPageIdentity = mapping.GetProperty("targetPageIdentity").GetString(),
                        TargetPageVersion = mapping.GetProperty("targetPageVersion").GetString(),
                        TargetIdentity = mapping.GetProperty("targetIdentity").GetString(),
                        SearchBoxActionId = mapping.GetProperty("searchBoxActionId").GetString(),
                        ProviderActionId = mapping.GetProperty("providerActionId").GetString(),
                        AdmittedPlanDigest = mapping.GetProperty("admittedPlanDigest").GetString(),
                        EvidenceReference = mapping.GetProperty("evidenceReference").GetString()
                    },
                    Lease = new BehaviorInteractionResultScriptLeaseEvidence
                    {
                        LeaseId = lease.GetProperty("leaseId").GetString(),
                        Status = lease.GetProperty("status").GetString(),
                        ActiveFromUtc = lease.GetProperty("activeFromUtc").GetDateTimeOffset(),
                        RetainThroughUtc = lease.GetProperty("retainThroughUtc").GetDateTimeOffset(),
                        ReleasedAtUtc = NullableDate(lease.GetProperty("releasedAtUtc")),
                        ClaimId = lease.GetProperty("claimId").GetString(),
                        SourceVersion = lease.GetProperty("sourceVersion").GetString(),
                        TargetIdentity = lease.GetProperty("targetIdentity").GetString(),
                        SearchBoxInstanceId = lease.GetProperty("searchBoxInstanceId").GetString(),
                        ProviderInstanceId = lease.GetProperty("providerInstanceId").GetString(),
                        OperationReference = lease.GetProperty("operationReference").GetString(),
                        MarkerReference = lease.GetProperty("markerReference").GetString(),
                        PlanDigest = lease.GetProperty("planDigest").GetString(),
                        EvidenceReference = lease.GetProperty("evidenceReference").GetString()
                    },
                    SearchBoxQueryGroupName = value.GetProperty("searchBoxQueryGroupName").GetString(),
                    AdmittedReviewedConfigurationDigest = value.GetProperty("admittedReviewedConfigurationDigest").GetString(),
                    AdmittedTargetConfigurationDigest = value.GetProperty("admittedTargetConfigurationDigest").GetString(),
                    AdmittedPlanDigest = value.GetProperty("admittedPlanDigest").GetString(),
                    TargetReadbackNotBeforeUtc = value.GetProperty("targetReadbackNotBeforeUtc").GetDateTimeOffset(),
                    RuntimeFinalEvidenceAtUtc = value.GetProperty("runtimeFinalEvidenceAtUtc").GetDateTimeOffset(),
                    ProviderInventoryEvidenceReference = value.GetProperty("providerInventoryEvidenceReference").GetString()
                };
            }

            private static BehaviorInteractionResultScriptProviderEvidence Provider(JsonElement value)
            {
                return new BehaviorInteractionResultScriptProviderEvidence
                {
                    CanonicalProviderInstanceId = value.GetProperty("canonicalProviderInstanceId").GetString(),
                    CanonicalDynamicRegionId = value.GetProperty("canonicalDynamicRegionId").GetString(),
                    PageIdentity = value.GetProperty("pageIdentity").GetString(),
                    PageVersion = value.GetProperty("pageVersion").GetString(),
                    ProviderType = value.GetProperty("providerType").GetString(),
                    QueryGroupName = value.GetProperty("queryGroupName").GetString(),
                    UpdateAjaxNavigate = value.GetProperty("updateAjaxNavigate").GetBoolean(),
                    ConfigurationDigest = value.GetProperty("configurationDigest").GetString(),
                    ObservedAtUtc = value.GetProperty("observedAtUtc").GetDateTimeOffset(),
                    Availability = ProviderAvailability(value.GetProperty("availability").GetString()),
                    CleanupObservedAtUtc = value.TryGetProperty("cleanupObservedAtUtc", out var cleanup)
                        ? NullableDate(cleanup)
                        : null,
                    OperationReference = value.GetProperty("operationReference").GetString(),
                    MarkerReference = value.TryGetProperty("markerReference", out var marker)
                        ? marker.GetString()
                        : null
                };
            }

            private static BehaviorInteractionProviderAvailability ProviderAvailability(string value)
            {
                switch (value)
                {
                    case "available":
                        return BehaviorInteractionProviderAvailability.Available;
                    case "missing":
                        return BehaviorInteractionProviderAvailability.Missing;
                    case "cleaned":
                        return BehaviorInteractionProviderAvailability.Cleaned;
                    default:
                        return BehaviorInteractionProviderAvailability.Unknown;
                }
            }

            private static BehaviorInteractionSearchConfiguration Configuration(JsonElement value)
            {
                return new BehaviorInteractionSearchConfiguration
                {
                    AllowEmptySearch = value.GetProperty("allowEmptySearch").GetBoolean(),
                    MaintainQueryState = value.GetProperty("maintainQueryState").GetBoolean(),
                    QueryGroupNames = Strings(value.GetProperty("queryGroupNames")),
                    ResultsPageAddress = value.GetProperty("resultsPageAddress").ValueKind == JsonValueKind.Null
                        ? null
                        : value.GetProperty("resultsPageAddress").GetString(),
                    TryInplaceQuery = value.GetProperty("tryInplaceQuery").GetBoolean(),
                    UpdatePageTitle = value.GetProperty("updatePageTitle").GetBoolean(),
                    MsBeforeShowingProgress = value.GetProperty("msBeforeShowingProgress").GetInt32()
                };
            }

            private static DateTimeOffset? NullableDate(JsonElement value)
            {
                return value.ValueKind == JsonValueKind.Null ? null : value.GetDateTimeOffset();
            }

            private static RuntimeVerificationStateExpectation State(JsonElement value)
            {
                return new RuntimeVerificationStateExpectation
                {
                    StateId = value.GetProperty("stateId").GetString(),
                    Predicate = value.GetProperty("predicate").GetString(),
                    EvidenceKind = value.GetProperty("evidenceKind").GetString()
                };
            }

            private static IList<string> Strings(JsonElement value)
            {
                return value.EnumerateArray().Select(item => item.GetString()).ToList();
            }

            private static string LoadFixtureText([CallerFilePath] string sourceFilePath = null)
            {
                var testDirectory = Path.GetDirectoryName(sourceFilePath);
                var fixturePath = Path.GetFullPath(Path.Combine(
                    testDirectory,
                    "..", "..", "..", "Resources", "Migration", "Ingredients", "BehaviorInteraction",
                    "search-submit.fixture.v1.json"));
                return File.ReadAllText(fixturePath);
            }
        }
    }
}
