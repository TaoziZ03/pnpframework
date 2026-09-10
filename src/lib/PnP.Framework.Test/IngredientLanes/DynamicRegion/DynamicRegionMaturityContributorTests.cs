using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.DynamicRegion;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Verification;
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
        [DataRow("foreign-canonical-ingredient", IngredientMaturityGateCatalog.CanonicalIdentity)]
        [DataRow("foreign-provider-dependency", IngredientMaturityGateCatalog.PrimaryOwner)]
        [DataRow("source-url-file-mismatch", IngredientMaturityGateCatalog.SourceBinding)]
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

        [TestMethod]
        public void FreshObservationsCannotBeRelabelledToAnotherTarget()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();
            fixture.Context.Target.TargetIdentity = "cupcollect:/sites/unrelated/Pages/Other.aspx#different-provider";
            fixture.Context.Target.TargetProfile = "unrelated-target-profile/v1";

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
        }

        [TestMethod]
        public void BorrowedForeignPlanCannotCloseM3()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();
            fixture.Evidence.Plan = CreateForeignPlanEvidence();
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Evidence.Plan).All(value => value.Passed));

            var assessment = fixture.Evaluate();

            Assert.IsTrue(assessment.Levels.Single(value => value.Level == IngredientMaturityLevel.M3)
                .Gates.All(value => value.Status == IngredientMaturityGateStatus.Failed));
        }

        [TestMethod]
        public void BoundDynamicRegionPlanClosesM3ThroughSharedValidator()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();
            fixture.Evidence.Plan = CreateBoundPlanEvidence(fixture);
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Evidence.Plan).All(value => value.Passed));

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M3, assessment.AttainedMaturity);
            Assert.IsTrue(assessment.Levels.Single(value => value.Level == IngredientMaturityLevel.M3)
                .Gates.All(value => value.Status == IngredientMaturityGateStatus.Passed));
        }

        [TestMethod]
        public void BorrowedForeignPlanAndRuntimeWaiverCannotCloseM4()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();
            fixture.Evidence.Plan = CreateForeignPlanEvidence();
            fixture.Evidence.Operational = CreateForeignOperationalEvidence(fixture.Evidence.Plan);
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Evidence.Operational).All(value => value.Passed));
            Assert.IsFalse(fixture.Evidence.Operational.RuntimeRequired);

            var assessment = fixture.Evaluate();

            Assert.IsTrue(assessment.Levels.Single(value => value.Level == IngredientMaturityLevel.M4)
                .Gates.All(value => value.Status == IngredientMaturityGateStatus.Failed));
        }

        [DataTestMethod]
        [DataRow("missing-provider-binding")]
        [DataRow("null-source")]
        public void MalformedInstanceProducesReceiptsAndDoesNotStopNextInstance(string mutation)
        {
            var malformed = Fixture.Create();
            malformed.Mutate(mutation);
            var valid = Fixture.Create();
            var assessments = new List<IngredientMaturityAssessment>();

            foreach (var fixture in new[] { malformed, valid })
            {
                assessments.Add(fixture.Evaluate());
            }

            Assert.IsNull(assessments[0].AttainedMaturity);
            Assert.IsTrue(assessments[0].Levels.Single(value => value.Level == IngredientMaturityLevel.M0)
                .Gates.All(value => value.Status == IngredientMaturityGateStatus.Failed));
            Assert.AreEqual(IngredientMaturityLevel.M0, assessments[1].AttainedMaturity);
        }

        private static IngredientPlanEvidence CreateForeignPlanEvidence()
        {
            var ingredientId = "content:body";
            var sourceDigest = new string('a', 64);
            var plan = new PublishingPageMigrationPlan
            {
                SourceSnapshotDigest = sourceDigest,
                RuntimeVerification = new RuntimeVerificationManifest(),
                IngredientGraph = new CanonicalPageIngredientGraph
                {
                    Nodes = new List<PageIngredientNode>
                    {
                        new PageIngredientNode
                        {
                            Id = ingredientId,
                            Kind = PageIngredientKind.Content,
                            Subtype = "text",
                            SemanticRole = "body",
                            SourcePredicateId = "content.body/v1",
                            SourcePageOrListItemIdentity = "foreign-source",
                            SourceVersionIdentity = "foreign-version",
                            PrimaryOwnerLane = "content.text",
                            EvidenceDigest = new string('b', 64),
                            HasContent = true
                        }
                    }
                },
                IngredientActions = new List<PageIngredientAction>
                {
                    new PageIngredientAction
                    {
                        ActionId = "action:content-body",
                        IngredientId = ingredientId,
                        Capability = IngredientCapability.Available,
                        Disposition = IngredientDisposition.Preserve,
                        TargetIdentity = "foreign-target",
                        PolicyId = "content.preserve/v1",
                        PolicyVersion = "v1"
                    }
                }
            };
            var digest = PublishingPageDigest.ComputePlanDigest(plan);
            return new IngredientPlanEvidence
            {
                Plan = plan,
                ExpectedSourceSnapshotDigest = sourceDigest,
                ExpectedPlanDigest = digest,
                IngredientId = ingredientId,
                SnapshotPlanEvidenceReferences = new List<string> { "foreign-plan.json" },
                ActionEvidenceReferences = new List<string> { "foreign-action.json" },
                DependencyPolicyEvidenceReferences = new List<string> { "foreign-dependencies.json" }
            };
        }

        private static IngredientPlanEvidence CreateBoundPlanEvidence(Fixture fixture)
        {
            var normalized = DynamicRegionEvidenceNormalizer.Normalize(fixture.Context, fixture.Evidence.Source);
            var target = fixture.Evidence.Target;
            var plan = new PublishingPageMigrationPlan
            {
                SourceSnapshotDigest = fixture.Context.Source.SourceSnapshotDigest,
                SourceWebUrl = fixture.Evidence.Source.WebUrl,
                SourcePageServerRelativeUrl = fixture.Evidence.Source.FileServerRelativeUrl,
                RuntimeVerification = new RuntimeVerificationManifest(),
                IngredientGraph = new CanonicalPageIngredientGraph
                {
                    Nodes = new List<PageIngredientNode> { normalized.Node },
                    Edges = new List<PageIngredientEdge>
                    {
                        new PageIngredientEdge
                        {
                            FromIngredientId = normalized.CanonicalIngredientId,
                            ToIngredientId = normalized.CanonicalProviderIngredientId,
                            Relationship = PageIngredientRelationship.RendersThrough,
                            Requirement = PageIngredientRequirement.Required
                        }
                    },
                    ExternalReferences = new List<PageIngredientExternalReference>
                    {
                        new PageIngredientExternalReference
                        {
                            IngredientId = normalized.CanonicalProviderIngredientId,
                            Kind = PageIngredientKind.WebPart,
                            Ownership = PageIngredientOwnership.Shared,
                            SharedPlanDigest = target.ReviewedProviderPlanDigest,
                            ExecutionGroupDigest = target.ProviderMappingDigest,
                            SupportCohortDigest = target.ProviderMappingDigest,
                            TargetSlotKey = target.TargetProviderInstanceId,
                            LogicalActionKey = target.ReviewedProviderActionId,
                            ExecutionGrantSignature = target.ProviderMappingDigest,
                            OriginalIdentifier = target.SourceProviderInstanceId,
                            ExpectedOwnership = "provider-owner",
                            State = PageExternalIngredientState.PlannedGlobalAction,
                            TargetIdentity = target.TargetProviderInstanceId,
                            EvidenceDigest = target.ProviderMappingDigest
                        }
                    }
                },
                IngredientActions = new List<PageIngredientAction>
                {
                    new PageIngredientAction
                    {
                        ActionId = "action:" + normalized.CanonicalIngredientId,
                        IngredientId = normalized.CanonicalIngredientId,
                        Capability = IngredientCapability.Available,
                        Disposition = IngredientDisposition.Preserve,
                        TargetIdentity = fixture.Context.Target.TargetIdentity,
                        PolicyId = "dynamic-region.preserve/v1",
                        PolicyVersion = "v1",
                        VerificationAssertions = new List<string>
                        {
                            "provider-mapping:" + target.ProviderMappingDigest
                        }
                    }
                }
            };
            var digest = PublishingPageDigest.ComputePlanDigest(plan);
            return new IngredientPlanEvidence
            {
                Plan = plan,
                ExpectedSourceSnapshotDigest = fixture.Context.Source.SourceSnapshotDigest,
                ExpectedPlanDigest = digest,
                IngredientId = normalized.CanonicalIngredientId,
                SnapshotPlanEvidenceReferences = new List<string> { "dynamic-region-plan.json" },
                ActionEvidenceReferences = new List<string> { "dynamic-region-action.json" },
                DependencyPolicyEvidenceReferences = new List<string> { "provider-handoff.json" }
            };
        }

        private static IngredientOperationalEvidence CreateForeignOperationalEvidence(IngredientPlanEvidence planEvidence)
        {
            var now = DateTimeOffset.Parse("2026-09-10T02:36:24.865Z");
            var startedAt = now.AddMinutes(-1);
            var action = planEvidence.Plan.IngredientActions.Single();
            var signature = MigrationActionSignature.Create(
                action.ActionId,
                "preserve-content",
                new string('b', 64),
                MigrationActionSignature.EmptySelectionReceiptDigest,
                action.TargetIdentity,
                MigrationDigest.ComputeSha256("foreign-semantic"));
            var operationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            return new IngredientOperationalEvidence
            {
                Plan = planEvidence.Plan,
                AdmittedPlanDigest = planEvidence.ExpectedPlanDigest,
                AdmissionPassed = true,
                IngredientId = planEvidence.IngredientId,
                ActionSignature = signature,
                ImportReceipt = new PublishingPageImportReceipt
                {
                    OperationId = operationId,
                    StartedAtUtc = startedAt,
                    CompletedAtUtc = now,
                    ApprovedPlanDigest = planEvidence.ExpectedPlanDigest,
                    ExecutionStatus = MigrationExecutionStatus.Succeeded,
                    Steps = new List<MigrationMutationReceipt>
                    {
                        new MigrationMutationReceipt
                        {
                            OperationId = operationId,
                            PlanDigest = planEvidence.ExpectedPlanDigest,
                            ActionId = signature.ActionId,
                            ActionSignature = signature.Signature,
                            Outcome = MutationOutcome.Applied,
                            CompletedAtUtc = now
                        }
                    },
                    OwnershipMatched = true,
                    FreshReadbackPassed = true,
                    StorageVerificationStatus = StorageVerificationStatus.Passed,
                    RuntimeVerificationStatus = RuntimeVerificationStatus.NotRequired,
                    VerifiedIngredientIds = new List<string> { planEvidence.IngredientId }
                },
                JournalState = new MigrationExecutionStateReceipt
                {
                    OperationId = operationId,
                    PlanDigest = planEvidence.ExpectedPlanDigest,
                    Status = MigrationExecutionStatus.Succeeded,
                    RecordedAtUtc = now
                },
                VerificationReceipt = new MigrationMutationVerificationReceipt
                {
                    OperationId = operationId,
                    PlanDigest = planEvidence.ExpectedPlanDigest,
                    ActionId = signature.ActionId,
                    ActionSignature = signature.Signature,
                    FreshReadbackPassed = true,
                    Ownership = MigrationTargetOwnership.MigrationOwned,
                    ProvenanceMatched = true,
                    VerifiedAtUtc = now
                },
                ExecutionStartedAtUtc = startedAt,
                RuntimeRequired = false,
                CleanupRequired = true,
                CleanupPassed = true,
                RetryEvidenceRequired = true,
                RetryPassed = true,
                PlanAdmissionEvidenceReferences = new List<string> { "foreign-admission.json" },
                ActionEvidenceReferences = new List<string> { "foreign-signature.json" },
                ReceiptEvidenceReferences = new List<string> { "foreign-receipts.json" },
                FreshReadbackEvidenceReferences = new List<string> { "foreign-readback.json" },
                RuntimeCleanupRetryEvidenceReferences = new List<string> { "foreign-cleanup.json" }
            };
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
                Evidence = CreateEvidence(contract.Source, contract.Target);
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
                var targetTime = contract.Target.ObservedAtUtc;
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
                    case "foreign-canonical-ingredient":
                        Context.Identity.IngredientId = "ccd.ingredient.dynamic.region/v1:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:00002:11111111-1111-1111-1111-111111111111";
                        break;
                    case "foreign-provider-dependency":
                        var originalProvider = Evidence.Source.ProviderIngredientId;
                        Evidence.Source.ProviderIngredientId = "ccd.ingredient.webpart.instance/v1:ff758937-6594-4e7d-be6a-e9f22d541889:11111111-1111-1111-1111-111111111111";
                        Evidence.Source.Dependencies.Remove(originalProvider);
                        Evidence.Source.Dependencies.Add(Evidence.Source.ProviderIngredientId);
                        break;
                    case "source-url-file-mismatch":
                        Evidence.Source.PageUrl = "https://microsoft.sharepoint.com/sites/unrelated/Pages/Other.aspx";
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
                    case "missing-provider-binding":
                        using (var document = JsonDocument.Parse(Evidence.Source.RawRegion.GetRawText()))
                        {
                            var root = System.Text.Json.Nodes.JsonNode.Parse(document.RootElement.GetRawText());
                            root["definition"].AsObject().Remove("providerBinding");
                            Evidence.Source.RawRegion = JsonSerializer.SerializeToElement(root);
                        }
                        break;
                    case "null-source":
                        Evidence.Source = null;
                        break;
                    default:
                        Assert.Fail("Unknown mutation " + mutation);
                        break;
                }
            }

            private static DynamicRegionMaturityEvidence CreateEvidence(
                DynamicRegionSourceEvidence source,
                DynamicRegionTargetEvidence target)
            {
                var normalized = DynamicRegionEvidenceNormalizer.Normalize(null, source);
                return new DynamicRegionMaturityEvidence
                {
                    Source = source,
                    Target = target,
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

            public DynamicRegionTargetEvidence Target { get; set; }

            public JsonElement Region { get; set; }
        }
    }
}
