using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.EmbedIframe;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Test.IngredientLanes.EmbedIframe
{
    [TestClass]
    public class EmbedIframeMaturityContributorTests
    {
        [TestMethod]
        public void FrozenSourcePassesM0AndM2WhileFreshTargetReadbackRemainsClosed()
        {
            var assessment = Fixture.Create().Evaluate();
            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SourceBinding).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity).Status);
        }

        [TestMethod]
        public void IndependentMappedTargetValuesCloseM1WithoutCopyingNormalizerOutput()
        {
            var fixture = Fixture.Create();
            fixture.AddFrozenTargetObservations();
            Assert.AreNotEqual(fixture.Contract.SourceValueDigests["binding.hostWebPartId"], fixture.Contract.TargetValueDigests["binding.hostWebPartId"]);
            Assert.AreEqual(IngredientMaturityLevel.M5, fixture.Evaluate().AttainedMaturity);
        }

        [TestMethod]
        public void PositiveM3M4M5PathsContributeAllGatesWithoutAssigningAttainedMaturity()
        {
            var fixture = Fixture.Create();
            fixture.AddFrozenTargetObservations();
            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();
            Assert.IsFalse(contribution.GetType().GetProperties().Any(value => string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(IngredientMaturityLevel.M5, assessment.AttainedMaturity);
            foreach (var level in new[] { IngredientMaturityLevel.M3, IngredientMaturityLevel.M4, IngredientMaturityLevel.M5 })
            {
                Assert.IsTrue(assessment.Levels.Single(value => value.Level == level).Gates.All(value => value.Status == IngredientMaturityGateStatus.Passed), level.ToString());
            }
        }

        [TestMethod]
        public void NormalizerPreservesLocatorGeometryMarginsPoliciesAndConditionalExternalBoundary()
        {
            var fixture = Fixture.Create();
            var normalized = fixture.Normalize();
            Assert.AreEqual("https://sway.com/s/BatsRizUdfGTTaJS/embed", normalized.RawLocator);
            Assert.AreEqual(normalized.RawLocator, normalized.ResolvedLocator);
            Assert.AreEqual(normalized.RawLocator, normalized.NormalizedLocator);
            Assert.AreEqual("external", normalized.Classification);
            Assert.AreEqual(fixture.Contract.SemanticSha256, MigrationDigest.ComputeSha256(normalized.SemanticCanonicalJson));
            CollectionAssert.AreEquivalent(fixture.Contract.SourceValueDigests.Keys.ToArray(), normalized.ValueDigests.Keys.ToArray());
            foreach (var expected in fixture.Contract.SourceValueDigests)
            {
                Assert.AreEqual(expected.Value, normalized.ValueDigests[expected.Key], expected.Key);
            }
            Assert.AreEqual(EmbedIframeEvidenceNormalizer.RuntimeRequirement, normalized.Node.RuntimeRequirement);
            Assert.AreEqual(IngredientTechnicalStatus.Conditional, fixture.Context.TechnicalOutcome.Status);
            Assert.IsNull(fixture.Evidence.Source.Reference.ContentBase64);
            Assert.IsNull(fixture.Evidence.Source.Reference.ContentSha256);
        }

        [DataTestMethod]
        [DataRow("missing-src")]
        [DataRow("hyperlink-is-not-iframe")]
        [DataRow("stale-source-version")]
        [DataRow("missing-host-binding")]
        [DataRow("wrong-consumer-binding")]
        [DataRow("wrong-reference-kind")]
        [DataRow("unobservable-content-claimed")]
        [DataRow("corrupt-raw-artifact")]
        [DataRow("semantic-digest-mismatch")]
        public void SourceBindingPolicyAndIntegrityNegativesFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.Mutate(mutation);
            Assert.IsTrue(fixture.Evaluate().Levels.SelectMany(value => value.Gates).Any(value => value.Status == IngredientMaturityGateStatus.Failed));
        }

        [DataTestMethod]
        [DataRow("foreign-ingredient")]
        [DataRow("wrong-plan-target")]
        [DataRow("wrong-action")]
        [DataRow("wrong-target")]
        [DataRow("stale-binding-source-version")]
        [DataRow("wrong-operation")]
        [DataRow("corrupt-plan-digest")]
        public void M3M4ContextBindingNegativesFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.AddFrozenTargetObservations();
            fixture.Mutate(mutation);
            var assessment = fixture.Evaluate();
            Assert.IsTrue(new[] { IngredientMaturityGateCatalog.SnapshotPlanBinding, IngredientMaturityGateCatalog.OperationActionBinding, IngredientMaturityGateCatalog.AdmittedExactPlan }
                .Any(gate => Gate(assessment, gate).Status == IngredientMaturityGateStatus.Failed), mutation);
        }

        [DataTestMethod]
        [DataRow("missing-intent-receipt")]
        [DataRow("corrupt-intent-receipt")]
        [DataRow("missing-apply-receipt")]
        [DataRow("corrupt-apply-receipt")]
        [DataRow("missing-readback-receipt")]
        [DataRow("corrupt-readback-receipt")]
        [DataRow("missing-runtime-receipt")]
        [DataRow("corrupt-runtime-receipt")]
        [DataRow("wrong-runtime-binding")]
        [DataRow("missing-cleanup-receipt")]
        [DataRow("corrupt-cleanup-receipt")]
        public void OperationalReceiptNegativesFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.AddFrozenTargetObservations();
            fixture.Mutate(mutation);
            Assert.IsTrue(fixture.Evaluate().Levels.Single(value => value.Level == IngredientMaturityLevel.M4).Gates
                .Any(value => value.Status == IngredientMaturityGateStatus.Failed), mutation);
        }

        [DataTestMethod]
        [DataRow("geometry.marginwidth")]
        [DataRow("geometry.marginheight")]
        [DataRow("policy.sandbox")]
        [DataRow("policy.allow")]
        [DataRow("policy.referrerpolicy")]
        [DataRow("policy.loading")]
        [DataRow("policy.name")]
        [DataRow("policy.title")]
        [DataRow("policy.frameborder")]
        [DataRow("policy.scrolling")]
        [DataRow("policy.style")]
        public void OneFieldMappedTargetMismatchFailsPerValueFidelity(string valuePath)
        {
            var fixture = Fixture.Create();
            fixture.AddFrozenTargetObservations();
            fixture.CorruptTargetValue(valuePath);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(fixture.Evaluate(), IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [DataTestMethod]
        [DataRow("same-commit-mismatch")]
        [DataRow("wrong-compare-binding")]
        [DataRow("corrupt-compare-digest")]
        [DataRow("access-denied-compared-equal")]
        [DataRow("unavailable-compared-equal")]
        public void ProductizationCompareAndAvailabilityNegativesFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.AddFrozenTargetObservations();
            fixture.Mutate(mutation);
            Assert.IsTrue(fixture.Evaluate().Levels.Single(value => value.Level == IngredientMaturityLevel.M5).Gates
                .Any(value => value.Status == IngredientMaturityGateStatus.Failed), mutation);
        }

        private static IngredientMaturityGateResult Gate(IngredientMaturityAssessment assessment, string gateId)
        {
            return assessment.Levels.SelectMany(value => value.Gates).Single(value => value.GateId == gateId);
        }

        private sealed class Fixture
        {
            private readonly string expectedSourceIdentity;

            private Fixture(FixtureContract contract)
            {
                Contract = contract;
                Evidence = CreateEvidence(contract);
                expectedSourceIdentity = CreateSourceIdentity(contract.Source);
                Context = CreateContext(contract, expectedSourceIdentity);
                AddHigherMaturityEvidence();
            }

            public FixtureContract Contract { get; }
            public IngredientMaturityEvaluationContext Context { get; }
            public EmbedIframeMaturityEvidence Evidence { get; }

            public static Fixture Create() => new Fixture(LoadContract());
            public EmbedIframeNormalization Normalize() => EmbedIframeEvidenceNormalizer.Normalize(Context, Evidence.Source);
            public IngredientMaturityContribution Contribute() => new EmbedIframeMaturityContributor(Evidence).Contribute(Context);
            public IngredientMaturityAssessment Evaluate() => IngredientMaturityEvaluator.Evaluate(Context,
                new IngredientMaturityContributorCatalog(new[] { new EmbedIframeMaturityContributor(Evidence) }));

            public void AddFrozenTargetObservations()
            {
                var targetTime = Contract.SourceObservedAtUtc.AddDays(1);
                foreach (var value in Contract.TargetValueDigests)
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ValuePath = value.Key,
                        ValueDigest = value.Value,
                        ObservedAtUtc = targetTime,
                        Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                        EvidenceReference = "ccd171-runtime-receipt.json#attributes/" + value.Key
                    });
                }
                Evidence.Live.TargetFreshReadback = true;
                Evidence.Live.TargetEvidenceReferences.Add("ccd171-runtime-receipt.json");
            }

            public void CorruptTargetValue(string valuePath)
            {
                Evidence.Live.Observations.Single(value => value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                    && string.Equals(value.ValuePath, valuePath, StringComparison.Ordinal)).ValueDigest = new string('0', 64);
            }

            public void Mutate(string mutation)
            {
                switch (mutation)
                {
                    case "missing-src": ReplaceRaw("<iframe width=\"560px\" height=\"250px\"></iframe>"); break;
                    case "hyperlink-is-not-iframe": ReplaceRaw("<a href=\"https://sway.com/s/BatsRizUdfGTTaJS/embed\">Sway</a>"); break;
                    case "stale-source-version": Evidence.Source.Source.Version += "-stale"; break;
                    case "missing-host-binding": Evidence.Source.Host.PropertyName = null; break;
                    case "wrong-consumer-binding": Evidence.Source.Reference.Consumer = "webpart:00000000-0000-0000-0000-000000000000"; break;
                    case "wrong-reference-kind": Evidence.Source.Reference.Kind = PageReferenceKind.Anchor; break;
                    case "unobservable-content-claimed": Evidence.Source.Reference.ContentBase64 = Evidence.Source.RawArtifactBase64; Evidence.Source.Reference.ContentSha256 = Evidence.Source.RawArtifact.Sha256; break;
                    case "corrupt-raw-artifact": Evidence.Source.RawArtifact.Sha256 = new string('0', 64); break;
                    case "semantic-digest-mismatch": Evidence.Source.SemanticDigest = new string('0', 64); break;
                    case "foreign-ingredient":
                        const string foreignIngredient = "reference:foreign";
                        Evidence.Binding.IngredientId = foreignIngredient;
                        Evidence.Plan.IngredientId = foreignIngredient;
                        Evidence.Plan.Plan.IngredientGraph.Nodes[0].Id = foreignIngredient;
                        Evidence.Plan.Plan.IngredientActions[0].IngredientId = foreignIngredient;
                        Evidence.Operational.IngredientId = foreignIngredient;
                        Evidence.Operational.ImportReceipt.VerifiedIngredientIds[0] = foreignIngredient;
                        Evidence.Productization.CompareReport.Ingredients[0].IngredientId = foreignIngredient;
                        Evidence.Productization.CompareReport.Ingredients[0].Lineage.SourceIngredientId = foreignIngredient;
                        RefreshPlanBindings();
                        break;
                    case "wrong-plan-target": Evidence.Plan.Plan.IngredientActions[0].TargetIdentity = "cupcollect:foreign"; RefreshPlanBindings(); break;
                    case "wrong-action": Evidence.Binding.ActionId = "action:foreign"; break;
                    case "wrong-target": Evidence.Binding.TargetIdentity = "cupcollect:foreign"; break;
                    case "stale-binding-source-version": Evidence.Binding.SourceVersion += "-stale"; break;
                    case "wrong-operation": Evidence.Binding.OperationId = Guid.Parse("99999999-9999-9999-9999-999999999999"); break;
                    case "corrupt-plan-digest": Evidence.Plan.ExpectedPlanDigest = new string('0', 64); break;
                    case "missing-intent-receipt": Evidence.Operational.PlanAdmissionEvidenceReferences.Clear(); break;
                    case "corrupt-intent-receipt": Evidence.Operational.AdmissionPassed = false; break;
                    case "missing-apply-receipt": Evidence.Operational.ImportReceipt = null; break;
                    case "corrupt-apply-receipt": Evidence.Operational.ImportReceipt.Steps[0].ActionSignature = new string('0', 64); break;
                    case "missing-readback-receipt": Evidence.Operational.VerificationReceipt = null; break;
                    case "corrupt-readback-receipt": Evidence.Operational.VerificationReceipt.OperationId = Guid.NewGuid(); break;
                    case "missing-runtime-receipt": Evidence.Operational.RuntimeReceipt = null; break;
                    case "corrupt-runtime-receipt": Evidence.Operational.RuntimeReceipt.Status = RuntimeVerificationStatus.Failed; break;
                    case "wrong-runtime-binding": Evidence.Operational.RuntimeReceipt.TargetIdentity = "cupcollect:foreign"; break;
                    case "missing-cleanup-receipt": Evidence.Operational.RuntimeCleanupRetryEvidenceReferences.Remove(Evidence.Binding.CleanupEvidenceReference); break;
                    case "corrupt-cleanup-receipt": Evidence.Operational.CleanupPassed = false; break;
                    case "same-commit-mismatch": Evidence.Productization.EndToEndCommit = new string('f', 40); break;
                    case "wrong-compare-binding": Evidence.Productization.CompareReport.Ingredients[0].Lineage.TargetIdentity = "cupcollect:foreign"; RefreshCompareDigest(); break;
                    case "corrupt-compare-digest": Evidence.Productization.CompareReportDigest = new string('0', 64); break;
                    case "access-denied-compared-equal": Evidence.Productization.CompareReport.Ingredients[0].TargetEvidenceState = IngredientTargetEvidenceStates.Denied; Evidence.Productization.CompareReport.Ingredients[0].ResultClass = PublishingPageCompareContract.ResultClasses.AuthorizationBlocked; RefreshCompareDigest(); break;
                    case "unavailable-compared-equal": Evidence.Productization.CompareReport.Ingredients[0].TargetEvidenceState = IngredientTargetEvidenceStates.Missing; Evidence.Productization.CompareReport.Ingredients[0].ResultClass = PublishingPageCompareContract.ResultClasses.Unknown; RefreshCompareDigest(); break;
                    default: Assert.Fail("Unknown mutation " + mutation); break;
                }
                Assert.AreEqual(expectedSourceIdentity, Context.Source.PageOrListItemIdentity);
                Assert.AreEqual(Contract.Source.Version, Context.Source.SourceVersion);
            }

            private void AddHigherMaturityEvidence()
            {
                var node = Normalize().Node;
                var actionId = "action:" + Contract.IngredientId;
                var plan = new PublishingPageMigrationPlan
                {
                    SourceSnapshotDigest = Contract.SemanticSha256,
                    SourceWebUrl = Contract.Source.WebUrl,
                    SourcePageServerRelativeUrl = Contract.Source.FileServerRelativeUrl,
                    TargetWebUrl = Contract.TargetOrigin,
                    TargetPageServerRelativeUrl = Contract.TargetPageServerRelativeUrl,
                    RuntimeVerification = new RuntimeVerificationManifest(),
                    IngredientGraph = new CanonicalPageIngredientGraph { Nodes = new List<PageIngredientNode> { node } },
                    IngredientActions = new List<PageIngredientAction>
                    {
                        new PageIngredientAction
                        {
                            ActionId = actionId,
                            IngredientId = Contract.IngredientId,
                            Capability = IngredientCapability.Available,
                            Disposition = IngredientDisposition.Preserve,
                            TargetIdentity = Contract.TargetIdentity,
                            PolicyId = "reference.embed.iframe/conditional-external-v1",
                            PolicyVersion = "v1"
                        }
                    }
                };
                var planDigest = PublishingPageDigest.ComputePlanDigest(plan);
                var operationId = Guid.Parse(Contract.OperationId);
                Evidence.Binding = new EmbedIframeEvidenceBinding
                {
                    IngredientId = Contract.IngredientId,
                    SourceVersion = Contract.Source.Version,
                    TargetProfile = Contract.TargetProfile,
                    TargetIdentity = Contract.TargetIdentity,
                    SourceHostWebPartId = Contract.Host.WebPartId,
                    TargetHostWebPartId = Contract.TargetHostWebPartId,
                    PlanDigest = planDigest,
                    ActionId = actionId,
                    OperationId = operationId,
                    ImplementationCommit = Contract.ImplementationCommit,
                    RuntimeEvidenceReference = "ccd171-runtime-receipt.json",
                    CompareEvidenceReference = "ccd171-compare-receipt.json",
                    CleanupEvidenceReference = "ccd171-webpart-cleanup-receipt.json"
                };
                Evidence.Plan = new IngredientPlanEvidence
                {
                    Plan = plan,
                    ExpectedSourceSnapshotDigest = Contract.SemanticSha256,
                    ExpectedPlanDigest = planDigest,
                    IngredientId = Contract.IngredientId,
                    SnapshotPlanEvidenceReferences = Refs("ccd171-plan.json"),
                    ActionEvidenceReferences = Refs("ccd346-host-handoff-receipt.json"),
                    DependencyPolicyEvidenceReferences = Refs("ccd171-runtime-receipt.json#policy")
                };
                Evidence.Operational = CreateOperationalEvidence(plan, planDigest, actionId, operationId);
                Evidence.Productization = CreateProductizationEvidence(planDigest, actionId);
            }

            private IngredientOperationalEvidence CreateOperationalEvidence(PublishingPageMigrationPlan plan, string planDigest, string actionId, Guid operationId)
            {
                var startedAt = Contract.SourceObservedAtUtc.AddDays(1);
                var completedAt = startedAt.AddMinutes(3);
                var signature = MigrationActionSignature.Create(actionId, "preserve-conditional-external-iframe", Contract.RawArtifact.Sha256,
                    MigrationActionSignature.EmptySelectionReceiptDigest, Contract.TargetIdentity, Contract.SemanticSha256);
                return new IngredientOperationalEvidence
                {
                    Plan = plan,
                    AdmittedPlanDigest = planDigest,
                    AdmissionPassed = true,
                    IngredientId = Contract.IngredientId,
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
                            new MigrationMutationReceipt { OperationId = operationId, PlanDigest = planDigest, ActionId = actionId,
                                ActionSignature = signature.Signature, Outcome = MutationOutcome.Applied, CompletedAtUtc = completedAt }
                        },
                        OwnershipMatched = true,
                        FreshReadbackPassed = true,
                        StorageVerificationStatus = StorageVerificationStatus.Passed,
                        RuntimeVerificationStatus = RuntimeVerificationStatus.Passed,
                        VerifiedIngredientIds = new List<string> { Contract.IngredientId }
                    },
                    JournalState = new MigrationExecutionStateReceipt { OperationId = operationId, PlanDigest = planDigest, RecordedAtUtc = completedAt, Status = MigrationExecutionStatus.Succeeded },
                    VerificationReceipt = new MigrationMutationVerificationReceipt
                    {
                        OperationId = operationId, PlanDigest = planDigest, ActionId = actionId, ActionSignature = signature.Signature,
                        VerifiedAtUtc = completedAt, FreshReadbackPassed = true, Ownership = MigrationTargetOwnership.MigrationOwned, ProvenanceMatched = true
                    },
                    RuntimeReceipt = new RuntimeVerificationReceipt { PlanDigest = planDigest, TargetIdentity = Contract.TargetIdentity, CompletedAtUtc = completedAt, Status = RuntimeVerificationStatus.Passed },
                    ExecutionStartedAtUtc = startedAt,
                    RuntimeRequired = true,
                    CleanupRequired = true,
                    CleanupPassed = true,
                    RetryEvidenceRequired = true,
                    RetryPassed = true,
                    PlanAdmissionEvidenceReferences = Refs("ccd346-intent-receipt.json"),
                    ActionEvidenceReferences = Refs("ccd346-apply-receipt.json"),
                    ReceiptEvidenceReferences = Refs("ccd346-apply-receipt.json", "ccd346-journal-receipt.json"),
                    FreshReadbackEvidenceReferences = Refs("ccd346-fresh-readback-receipt.json"),
                    RuntimeCleanupRetryEvidenceReferences = Refs("ccd171-runtime-receipt.json", "ccd171-webpart-cleanup-receipt.json", "ccd171-retry-negative-receipt.json")
                };
            }

            private IngredientProductizationEvidence CreateProductizationEvidence(string planDigest, string actionId)
            {
                var report = new PublishingPageCompareReport
                {
                    SchemaVersion = PublishingPageCompareContract.SchemaVersion,
                    GeneratedAtUtc = Contract.SourceObservedAtUtc.AddDays(1).AddMinutes(4),
                    Bindings = new CompareBindings { PlanDigestSha256 = planDigest },
                    TargetIdentity = new CompareTargetIdentity { CanonicalIdentity = Contract.TargetIdentity },
                    Storage = new CompareStorageSummary { Status = "passed", FreshReadback = true },
                    Runtime = new CompareRuntimeSummary { Status = "conditional" },
                    Ingredients = new List<IngredientCompareResult>
                    {
                        new IngredientCompareResult
                        {
                            IngredientId = Contract.IngredientId,
                            Kind = "Reference",
                            Material = true,
                            TargetEvidenceState = IngredientTargetEvidenceStates.Fresh,
                            ObservedAtUtc = Contract.SourceObservedAtUtc.AddDays(1),
                            Lineage = new IngredientCompareLineage
                            {
                                SourceIngredientId = Contract.IngredientId, ActionId = actionId, TargetIdentity = Contract.TargetIdentity,
                                PlanDigestSha256 = planDigest, EvidenceRefs = Refs("ccd171-runtime-receipt.json")
                            },
                            Expected = new CompareDigestPair { CanonicalDigestSha256 = Contract.SemanticSha256 },
                            Actual = new CompareDigestPair { CanonicalDigestSha256 = Contract.SemanticSha256 },
                            ResultClass = PublishingPageCompareContract.ResultClasses.Exact,
                            ReasonCode = PublishingPageCompareContract.ReasonCodes.CanonicalRulesEquivalent,
                            Message = "Persisted iframe semantics match; external payload exactness remains conditional."
                        }
                    },
                    Acceptance = new CompareAcceptance { Verdict = "conditional", StorageStatus = "passed", RuntimeStatus = "conditional" }
                };
                report.ReportDigestSha256 = PublishingPageCompareReconciler.ComputeReportDigest(report);
                return new IngredientProductizationEvidence
                {
                    TestId = nameof(EmbedIframeMaturityContributorTests),
                    HermeticUnitTest = true,
                    RedTestEvidenceReference = "ccd374-peer-changes-required.md",
                    GreenTestEvidenceReference = "EmbedIframeMaturityContributorTests.trx",
                    ImplementationCommit = Contract.ImplementationCommit,
                    BuildCommit = Contract.ImplementationCommit,
                    BinaryDigest = Contract.BinaryDigest,
                    BuildEvidenceReference = "ccd383-build-receipt.json",
                    EndToEndCommit = Contract.ImplementationCommit,
                    EndToEndPlanDigest = planDigest,
                    EndToEndEvidenceReference = "ccd171-runtime-receipt.json",
                    CompareReport = report,
                    CompareReportDigest = report.ReportDigestSha256,
                    CompareEvidenceReference = "ccd171-compare-receipt.json",
                    ArchitectReviewVerdict = "APPROVED",
                    ArchitectReviewEvidenceReference = "hermetic-contract-fixture:architect-gate",
                    CtoReviewVerdict = "APPROVED",
                    CtoReviewEvidenceReference = "hermetic-contract-fixture:cto-gate",
                    IndependentVerificationVerdict = "PASS",
                    IndependentVerificationEvidenceReference = "hermetic-contract-fixture:verification-gate",
                    PrReadyCommit = Contract.ImplementationCommit,
                    PrReadyEvidenceReference = "ccd383-pr-ready-receipt.json"
                };
            }

            private void RefreshPlanBindings()
            {
                var planDigest = PublishingPageDigest.ComputePlanDigest(Evidence.Plan.Plan);
                Evidence.Plan.ExpectedPlanDigest = planDigest;
                Evidence.Operational.Plan = Evidence.Plan.Plan;
                Evidence.Operational.AdmittedPlanDigest = planDigest;
                Evidence.Operational.ImportReceipt.ApprovedPlanDigest = planDigest;
                Evidence.Operational.ImportReceipt.Steps[0].PlanDigest = planDigest;
                Evidence.Operational.JournalState.PlanDigest = planDigest;
                Evidence.Operational.VerificationReceipt.PlanDigest = planDigest;
                Evidence.Operational.RuntimeReceipt.PlanDigest = planDigest;
                Evidence.Binding.PlanDigest = planDigest;
                Evidence.Productization.EndToEndPlanDigest = planDigest;
                Evidence.Productization.CompareReport.Bindings.PlanDigestSha256 = planDigest;
                Evidence.Productization.CompareReport.Ingredients[0].Lineage.PlanDigestSha256 = planDigest;
                RefreshCompareDigest();
            }

            private void RefreshCompareDigest()
            {
                var report = Evidence.Productization.CompareReport;
                report.ReportDigestSha256 = PublishingPageCompareReconciler.ComputeReportDigest(report);
                Evidence.Productization.CompareReportDigest = report.ReportDigestSha256;
            }

            private void ReplaceRaw(string raw)
            {
                var bytes = Encoding.UTF8.GetBytes(raw);
                Evidence.Source.RawArtifactBase64 = Convert.ToBase64String(bytes);
                Evidence.Source.RawArtifact.Length = bytes.LongLength;
                Evidence.Source.RawArtifact.Sha256 = MigrationDigest.ComputeSha256(bytes);
            }

            private static EmbedIframeMaturityEvidence CreateEvidence(FixtureContract fixture)
            {
                return new EmbedIframeMaturityEvidence
                {
                    Source = new EmbedIframeSourceEvidence
                    {
                        Source = new EmbedIframeSourceIdentity
                        {
                            WebUrl = fixture.Source.WebUrl, PageUrl = fixture.Source.PageUrl, FileServerRelativeUrl = fixture.Source.FileServerRelativeUrl,
                            ListId = fixture.Source.ListId, ItemId = fixture.Source.ItemId, UniqueId = fixture.Source.UniqueId,
                            Version = fixture.Source.Version, Modified = fixture.Source.Modified
                        },
                        Host = new EmbedIframeHostBinding
                        {
                            IngredientId = fixture.Host.IngredientId, WebPartId = fixture.Host.WebPartId,
                            PropertyName = fixture.Host.PropertyName, ZoneIndex = fixture.Host.ZoneIndex
                        },
                        Reference = new PageReferenceSnapshot
                        {
                            Id = fixture.Reference.Id, OriginalValue = fixture.Reference.OriginalValue,
                            SourceAbsoluteUrl = fixture.Reference.SourceAbsoluteUrl, Consumer = fixture.Reference.Consumer,
                            Kind = Enum.Parse<PageReferenceKind>(fixture.Reference.Kind), IsRenderableResource = fixture.Reference.IsRenderableResource,
                            CaptureStatus = Enum.Parse<PageCaptureStatus>(fixture.Reference.CaptureStatus)
                        },
                        RawArtifact = new ArtifactReference
                        {
                            Sha256 = fixture.RawArtifact.Sha256, Length = fixture.RawArtifact.Length,
                            MediaType = fixture.RawArtifact.MediaType, OriginalName = fixture.RawArtifact.OriginalName
                        },
                        RawArtifactBase64 = fixture.RawArtifact.Base64,
                        SemanticDigest = fixture.SemanticSha256,
                        EvidenceReferences = Refs("ccd110-r00871-v83.fixture.json", "source-collect-receipt.json")
                    },
                    ExpectedSourceValueDigests = new Dictionary<string, string>(fixture.SourceValueDigests, StringComparer.Ordinal),
                    ExpectedTargetValueDigests = new Dictionary<string, string>(fixture.TargetValueDigests, StringComparer.Ordinal),
                    Live = new IngredientLiveEvidence
                    {
                        SourceAuthenticated = true,
                        Observations = fixture.SourceValueDigests.Select(value => new IngredientValueObservation
                        {
                            ValuePath = value.Key, ValueDigest = value.Value, ObservedAtUtc = fixture.SourceObservedAtUtc,
                            Origin = IngredientObservationOrigin.AuthenticatedSource, EvidenceReference = "source-collect-receipt.json#" + value.Key
                        }).ToList(),
                        SourceEvidenceReferences = Refs("source-collect-receipt.json")
                    }
                };
            }

            private static IngredientMaturityEvaluationContext CreateContext(FixtureContract fixture, string sourceIdentity)
            {
                return new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = fixture.ClaimId, Lane = EmbedIframeEvidenceNormalizer.Lane, IngredientId = fixture.IngredientId,
                        Kind = PageIngredientKind.Reference, Subtype = EmbedIframeEvidenceNormalizer.Subtype,
                        SemanticRole = EmbedIframeEvidenceNormalizer.SemanticRole, SourcePredicateId = EmbedIframeEvidenceNormalizer.SourcePredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = sourceIdentity, SourceVersion = fixture.Source.Version,
                        SourceArtifactDigest = fixture.RawArtifact.Sha256, SourceSnapshotDigest = fixture.SemanticSha256
                    },
                    Target = new IngredientMaturityTargetBinding { TargetProfile = fixture.TargetProfile, TargetIdentity = fixture.TargetIdentity },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework", ProducerVersion = "embed.iframe-revision-v2",
                        ImplementationCommit = fixture.ImplementationCommit, BinaryDigest = fixture.BinaryDigest
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.ExecutableWithLoss,
                        Status = IngredientTechnicalStatus.Conditional,
                        ReasonCode = "external-payload-headers-auth-and-in-frame-state-unobservable"
                    }
                };
            }

            private static string CreateSourceIdentity(SourceContract source) => MigrationContractSerializer.SerializeCanonical(new
            {
                fileServerRelativeUrl = source.FileServerRelativeUrl,
                itemId = source.ItemId,
                listId = source.ListId,
                uniqueId = source.UniqueId,
                version = source.Version,
                webUrl = source.WebUrl
            });

            private static FixtureContract LoadContract([CallerFilePath] string callerPath = null)
            {
                var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath),
                    "../../Resources/IngredientLanes/embed.iframe/v1/ccd110-r00871-v83.fixture.json"));
                return JsonSerializer.Deserialize<FixtureContract>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            private static List<string> Refs(params string[] values) => values.OrderBy(value => value, StringComparer.Ordinal).ToList();
        }

        internal sealed class FixtureContract
        {
            public string ClaimId { get; set; }
            public string IngredientId { get; set; }
            public SourceContract Source { get; set; }
            public HostContract Host { get; set; }
            public ReferenceContract Reference { get; set; }
            public RawArtifactContract RawArtifact { get; set; }
            public string SemanticSha256 { get; set; }
            public DateTimeOffset SourceObservedAtUtc { get; set; }
            public string TargetProfile { get; set; }
            public string TargetOrigin { get; set; }
            public string TargetPageServerRelativeUrl { get; set; }
            public string TargetIdentity { get; set; }
            public string TargetHostWebPartId { get; set; }
            public string ObservedPlanDigest { get; set; }
            public string OperationId { get; set; }
            public string ImplementationCommit { get; set; }
            public string BinaryDigest { get; set; }
            public Dictionary<string, string> SourceValueDigests { get; set; }
            public Dictionary<string, string> TargetValueDigests { get; set; }
        }

        internal sealed class SourceContract
        {
            public string WebUrl { get; set; }
            public string PageUrl { get; set; }
            public string FileServerRelativeUrl { get; set; }
            public string ListId { get; set; }
            public int ItemId { get; set; }
            public string UniqueId { get; set; }
            public string Version { get; set; }
            public string Modified { get; set; }
        }

        internal sealed class HostContract
        {
            public string IngredientId { get; set; }
            public string WebPartId { get; set; }
            public string PropertyName { get; set; }
            public int ZoneIndex { get; set; }
        }

        internal sealed class ReferenceContract
        {
            public string Id { get; set; }
            public string OriginalValue { get; set; }
            public string SourceAbsoluteUrl { get; set; }
            public string Consumer { get; set; }
            public string Kind { get; set; }
            public bool IsRenderableResource { get; set; }
            public string CaptureStatus { get; set; }
        }

        internal sealed class RawArtifactContract
        {
            public string Sha256 { get; set; }
            public long Length { get; set; }
            public string MediaType { get; set; }
            public string OriginalName { get; set; }
            public string Base64 { get; set; }
        }
    }
}
