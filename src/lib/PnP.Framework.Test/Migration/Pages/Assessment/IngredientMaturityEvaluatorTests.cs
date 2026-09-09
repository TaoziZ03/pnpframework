using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PnP.Framework.Test.Migration.Pages.Assessment
{
    [TestClass]
    public class IngredientMaturityEvaluatorTests
    {
        private const string ClaimId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string SourceArtifactDigest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string SourceSnapshotDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        private const string ImplementationCommit = "dddddddddddddddddddddddddddddddddddddddd";
        private const string BinaryDigest = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        private const string TargetIdentity = "cupcollect:/sites/ccd/pages/ingredient.aspx";

        [TestMethod]
        public void ContinuousEvaluatorAttainsM5WithIndependentConditionalOutcome()
        {
            var fixture = Fixture.Create();
            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M5, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityConfidence.High, assessment.Confidence);
            Assert.IsNull(assessment.MissingPromotionGate);
            Assert.AreEqual(IngredientTechnicalStatus.Conditional, assessment.TechnicalOutcome.Status);
            Assert.AreEqual(PageMigrationOutcome.ExecutableWithLoss, assessment.TechnicalOutcome.PnPMigrationOutcome);
            IngredientMaturityEvaluator.ValidateAssessment(assessment);
        }

        [TestMethod]
        public void MissingAndDuplicateIdentityFailClosed()
        {
            var fixture = Fixture.Create();
            fixture.Context.Identity.ClaimId = null;
            Assert.ThrowsException<System.IO.InvalidDataException>(() => fixture.Evaluate());

            var contributor = new TestContributor("one", "content.text", new List<IngredientMaturityGateReceipt>());
            Assert.ThrowsException<ArgumentException>(() => new IngredientMaturityContributorCatalog(new[]
            {
                contributor,
                new TestContributor("two", "content.text", new List<IngredientMaturityGateReceipt>())
            }));
        }

        [TestMethod]
        public void AmbiguousPrimaryOwnerStopsAtM0()
        {
            var fixture = Fixture.Create();
            fixture.ReplaceM0(CreateOwnerRegistry(ambiguous: true));
            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.PrimaryOwner).Status);
            Assert.AreEqual(IngredientMaturityGateCatalog.PrimaryOwner, assessment.MissingPromotionGate);
        }

        [TestMethod]
        public void TamperedArtifactAndSemanticDigestsStopAtM1()
        {
            var fixture = Fixture.Create();
            var integrity = fixture.ContentIntegrity();
            integrity.Artifact.Sha256 = "f".PadLeft(64, 'f');
            integrity.SemanticDigest = "0".PadLeft(64, '0');
            fixture.ReplaceLevel(IngredientMaturityLevel.M2, IngredientMaturityEvidenceValidator.ValidateM2(integrity));
            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M1, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity).Status);
        }

        [TestMethod]
        public void TamperedPlanDigestStopsAtM2()
        {
            var fixture = Fixture.Create();
            var planEvidence = fixture.PlanEvidence();
            planEvidence.ExpectedPlanDigest = new string('0', 64);
            fixture.ReplaceLevel(IngredientMaturityLevel.M3, IngredientMaturityEvidenceValidator.ValidateM3(planEvidence));
            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.SnapshotPlanBinding).Status);
        }

        [TestMethod]
        public void MissingActionAndIllegalDependencyReleaseFailPlanGates()
        {
            var missing = Fixture.Create();
            missing.Plan.IngredientActions.Clear();
            missing.RefreshPlanDigest();
            missing.ReplaceLevel(IngredientMaturityLevel.M3, IngredientMaturityEvidenceValidator.ValidateM3(missing.PlanEvidence()));
            var missingAssessment = missing.Evaluate();
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(missingAssessment, IngredientMaturityGateCatalog.ActionDisposition).Status);

            var illegal = Fixture.Create();
            illegal.Plan.IngredientActions[0].ReleasedDependencyIngredientIds.Add("ingredient:foreign");
            illegal.RefreshPlanDigest();
            illegal.ReplaceLevel(IngredientMaturityLevel.M3, IngredientMaturityEvidenceValidator.ValidateM3(illegal.PlanEvidence()));
            var illegalAssessment = illegal.Evaluate();
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(illegalAssessment, IngredientMaturityGateCatalog.DependencyPolicy).Status);
        }

        [TestMethod]
        public void WrongPlanAndOperationActionMismatchFailOperationalReceipts()
        {
            var wrongPlan = Fixture.Create();
            var operational = wrongPlan.OperationalEvidence();
            operational.ImportReceipt.ApprovedPlanDigest = new string('0', 64);
            wrongPlan.ReplaceLevel(IngredientMaturityLevel.M4, IngredientMaturityEvidenceValidator.ValidateM4(operational));
            Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                Gate(wrongPlan.Evaluate(), IngredientMaturityGateCatalog.MutationJournalVerification).Status);

            var mismatch = Fixture.Create();
            operational = mismatch.OperationalEvidence();
            operational.VerificationReceipt.OperationId = Guid.NewGuid();
            operational.VerificationReceipt.ActionId = "action:foreign";
            mismatch.ReplaceLevel(IngredientMaturityLevel.M4, IngredientMaturityEvidenceValidator.ValidateM4(operational));
            Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                Gate(mismatch.Evaluate(), IngredientMaturityGateCatalog.MutationJournalVerification).Status);
        }

        [TestMethod]
        public void FailedFreshReadbackAndRuntimePendingFailM4()
        {
            var readback = Fixture.Create();
            var operational = readback.OperationalEvidence();
            operational.ImportReceipt.FreshReadbackPassed = false;
            readback.ReplaceLevel(IngredientMaturityLevel.M4, IngredientMaturityEvidenceValidator.ValidateM4(operational));
            Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                Gate(readback.Evaluate(), IngredientMaturityGateCatalog.FreshStorage).Status);

            foreach (var status in new[] { RuntimeVerificationStatus.Pending, RuntimeVerificationStatus.Failed })
            {
                var runtime = Fixture.Create();
                operational = runtime.OperationalEvidence();
                operational.RuntimeRequired = true;
                operational.RuntimeReceipt = new RuntimeVerificationReceipt
                {
                    PlanDigest = runtime.PlanDigest,
                    TargetIdentity = TargetIdentity,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Status = status
                };
                runtime.ReplaceLevel(IngredientMaturityLevel.M4, IngredientMaturityEvidenceValidator.ValidateM4(operational));
                Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                    Gate(runtime.Evaluate(), IngredientMaturityGateCatalog.RuntimeCleanupRetry).Status);
            }
        }

        [TestMethod]
        public void StaleVerificationReceiptFailsOperationalIntegrity()
        {
            var fixture = Fixture.Create();
            var operational = fixture.OperationalEvidence();
            operational.VerificationReceipt.VerifiedAtUtc = operational.ExecutionStartedAtUtc.AddSeconds(-1);
            fixture.ReplaceLevel(IngredientMaturityLevel.M4, IngredientMaturityEvidenceValidator.ValidateM4(operational));

            Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                Gate(fixture.Evaluate(), IngredientMaturityGateCatalog.MutationJournalVerification).Status);
        }

        [TestMethod]
        public void CatalogAcceptsEightAdditiveLanesAndBehaviorDoesNotClaimPersistedKind()
        {
            var lanes = new[]
            {
                "content.text", "webpart.instance", "resource.image", "page.layout",
                "resource.script", "embed.iframe", "dynamic.region", "behavior.interaction"
            };
            var catalog = new IngredientMaturityContributorCatalog(lanes.Select((lane, index) =>
                (IIngredientMaturityContributor)new TestContributor($"contributor-{index}", lane, new List<IngredientMaturityGateReceipt>())));
            Assert.AreEqual(8, catalog.Contributors.Count);

            var context = Fixture.Create().Context;
            context.Identity = new IngredientMaturityIdentity
            {
                ClaimId = ClaimId,
                WorkItemType = IngredientMaturityContract.RuntimeVerificationWorkItem,
                Lane = "behavior.interaction",
                IngredientId = "assertion:interaction-state-transition",
                Kind = null,
                Subtype = "interaction-state-transition",
                SemanticRole = "runtime-verification-assertion",
                SourcePredicateId = "runtime.assertion"
            };
            var receipts = IngredientMaturityEvidenceValidator.ValidateM0(context, null, null, null, Refs("behavior-m0.json"));
            var behavior = new TestContributor("behavior-maturity/v1", "behavior.interaction", receipts.ToList());
            var assessment = IngredientMaturityEvaluator.Evaluate(
                context,
                new IngredientMaturityContributorCatalog(new[] { behavior }));
            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
        }

        [TestMethod]
        public void SameCommitMismatchAndAbsentProductizationEvidenceFailM5()
        {
            var mismatch = Fixture.Create();
            var product = mismatch.ProductizationEvidence();
            product.EndToEndCommit = "1111111111111111111111111111111111111111";
            mismatch.ReplaceLevel(IngredientMaturityLevel.M5,
                IngredientMaturityEvidenceValidator.ValidateM5(product, mismatch.Context.Producer, mismatch.PlanDigest));
            Assert.AreEqual(IngredientMaturityLevel.M4, mismatch.Evaluate().AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                Gate(mismatch.Evaluate(), IngredientMaturityGateCatalog.SameCommitE2E).Status);

            var absent = Fixture.Create();
            absent.RemoveGates(
                IngredientMaturityGateCatalog.HermeticUtRedGreen,
                IngredientMaturityGateCatalog.ExactCodeBuild,
                IngredientMaturityGateCatalog.ArchitectReview);
            var assessment = absent.Evaluate();
            Assert.AreEqual(IngredientMaturityLevel.M4, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Missing, Gate(assessment, IngredientMaturityGateCatalog.HermeticUtRedGreen).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Missing, Gate(assessment, IngredientMaturityGateCatalog.ExactCodeBuild).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Missing, Gate(assessment, IngredientMaturityGateCatalog.ArchitectReview).Status);
        }

        [TestMethod]
        public void HigherEvidenceCannotSkipAFailedLevel()
        {
            var fixture = Fixture.Create();
            fixture.RemoveGates(IngredientMaturityGateCatalog.CupCollectFreshReadback);
            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.IsFalse(assessment.Levels.Single(value => value.Level == IngredientMaturityLevel.M2).Passed);
            Assert.AreEqual(IngredientMaturityGateCatalog.CupCollectFreshReadback, assessment.MissingPromotionGate);
        }

        [TestMethod]
        public void AssessmentDigestDetectsTampering()
        {
            var assessment = Fixture.Create().Evaluate();
            assessment.TechnicalOutcome.ReasonCode = "tampered";
            Assert.ThrowsException<System.IO.InvalidDataException>(() => IngredientMaturityEvaluator.ValidateAssessment(assessment));
        }

        private static IngredientMaturityGateResult Gate(IngredientMaturityAssessment assessment, string gateId)
        {
            return assessment.Levels.SelectMany(value => value.Gates).Single(value => value.GateId == gateId);
        }

        private static PublishingPageIngredientPrimaryOwnerRegistry CreateOwnerRegistry(bool ambiguous = false)
        {
            var entries = new List<PageIngredientPrimaryOwnerDescriptor>
            {
                new PageIngredientPrimaryOwnerDescriptor(
                    "content.text.test",
                    PageIngredientKind.Content,
                    "content.publishing-page-content",
                    "body-content",
                    "content.test",
                    "content.text")
            };
            if (ambiguous)
            {
                entries.Add(new PageIngredientPrimaryOwnerDescriptor(
                    "content.text.duplicate",
                    PageIngredientKind.Content,
                    "content.publishing-page-content",
                    "body-content",
                    "content.test",
                    "content.text"));
            }
            return new PublishingPageIngredientPrimaryOwnerRegistry(
                entries,
                new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>
                {
                    ["content.test"] = _ => true
                });
        }

        private sealed class TestContributor : IIngredientMaturityContributor
        {
            private readonly IList<IngredientMaturityGateReceipt> receipts;

            public TestContributor(string contributorId, string lane, IList<IngredientMaturityGateReceipt> receipts)
            {
                ContributorId = contributorId;
                Lane = lane;
                this.receipts = receipts;
            }

            public string ContributorId { get; }

            public string Lane { get; }

            public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
            {
                return new IngredientMaturityContribution
                {
                    ContributorId = ContributorId,
                    ClaimId = context.Identity.ClaimId,
                    Lane = context.Identity.Lane,
                    IngredientId = context.Identity.IngredientId,
                    GateReceipts = receipts.ToList()
                };
            }
        }

        private sealed class Fixture
        {
            private readonly List<IngredientMaturityGateReceipt> receipts = new List<IngredientMaturityGateReceipt>();
            private readonly byte[] rawBytes = Encoding.UTF8.GetBytes("ingredient raw bytes");

            private Fixture()
            {
            }

            public IngredientMaturityEvaluationContext Context { get; private set; }

            public PageIngredientNode Node { get; private set; }

            public PublishingPageMigrationPlan Plan { get; private set; }

            public string PlanDigest { get; private set; }

            public static Fixture Create()
            {
                var fixture = new Fixture();
                fixture.Context = new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = ClaimId,
                        Lane = "content.text",
                        IngredientId = "content:body",
                        Kind = PageIngredientKind.Content,
                        Subtype = "content.publishing-page-content",
                        SemanticRole = "body-content",
                        SourcePredicateId = "content.test"
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = "source-page:item-3",
                        SourceVersion = "etag:12",
                        SourceArtifactDigest = SourceArtifactDigest,
                        SourceSnapshotDigest = SourceSnapshotDigest
                    },
                    Target = new IngredientMaturityTargetBinding
                    {
                        TargetProfile = "CUPCollect",
                        TargetIdentity = TargetIdentity
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = "test-v1",
                        ImplementationCommit = ImplementationCommit,
                        BinaryDigest = BinaryDigest
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.ExecutableWithLoss,
                        Status = IngredientTechnicalStatus.Conditional,
                        ReasonCode = "policy-limited-but-deterministic"
                    }
                };
                fixture.Node = new PageIngredientNode
                {
                    Id = fixture.Context.Identity.IngredientId,
                    Kind = fixture.Context.Identity.Kind.Value,
                    Subtype = fixture.Context.Identity.Subtype,
                    SemanticRole = fixture.Context.Identity.SemanticRole,
                    SourcePredicateId = fixture.Context.Identity.SourcePredicateId,
                    SourcePageOrListItemIdentity = fixture.Context.Source.PageOrListItemIdentity,
                    SourceVersionIdentity = fixture.Context.Source.SourceVersion,
                    PrimaryOwnerLane = fixture.Context.Identity.Lane,
                    EvidenceDigest = fixture.Context.Source.SourceArtifactDigest,
                    HasContent = true
                };
                fixture.Plan = new PublishingPageMigrationPlan
                {
                    SourceSnapshotDigest = SourceSnapshotDigest,
                    RuntimeVerification = new RuntimeVerificationManifest(),
                    IngredientGraph = new CanonicalPageIngredientGraph
                    {
                        Nodes = new List<PageIngredientNode> { fixture.Node }
                    },
                    IngredientActions = new List<PageIngredientAction>
                    {
                        new PageIngredientAction
                        {
                            ActionId = "action:content-body",
                            IngredientId = fixture.Node.Id,
                            Capability = IngredientCapability.Available,
                            Disposition = IngredientDisposition.Preserve,
                            TargetIdentity = TargetIdentity,
                            PolicyId = "content.preserve/v1",
                            PolicyVersion = "v1"
                        }
                    }
                };
                fixture.RefreshPlanDigest();
                fixture.RebuildAllReceipts();
                return fixture;
            }

            public IngredientMaturityAssessment Evaluate()
            {
                var contributor = new TestContributor("content.text.maturity/v1", "content.text", receipts);
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[] { contributor }));
            }

            public void RefreshPlanDigest()
            {
                PlanDigest = PublishingPageDigest.ComputePlanDigest(Plan);
            }

            public void RebuildAllReceipts()
            {
                receipts.Clear();
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(Context, Node, null, CreateOwnerRegistry(), Refs("m0.json")));
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(LiveEvidence()));
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(ContentIntegrity()));
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM3(PlanEvidence()));
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM4(OperationalEvidence()));
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM5(ProductizationEvidence(), Context.Producer, PlanDigest));
            }

            public void ReplaceM0(PublishingPageIngredientPrimaryOwnerRegistry registry)
            {
                ReplaceLevel(IngredientMaturityLevel.M0,
                    IngredientMaturityEvidenceValidator.ValidateM0(Context, Node, null, registry, Refs("m0.json")));
            }

            public void ReplaceLevel(IngredientMaturityLevel level, IEnumerable<IngredientMaturityGateReceipt> replacements)
            {
                foreach (var existing in receipts.Where(value => value.Level == level).ToArray())
                {
                    receipts.Remove(existing);
                }
                foreach (var replacement in replacements)
                {
                    receipts.Add(replacement);
                }
            }

            public void RemoveGates(params string[] gateIds)
            {
                foreach (var existing in receipts.Where(value => gateIds.Contains(value.GateId, StringComparer.Ordinal)).ToArray())
                {
                    receipts.Remove(existing);
                }
            }

            public IngredientContentIntegrityEvidence ContentIntegrity()
            {
                var semantic = "{\"kind\":\"content.text\",\"value\":\"hello\"}";
                return new IngredientContentIntegrityEvidence
                {
                    Artifact = new ArtifactReference
                    {
                        Sha256 = MigrationDigest.ComputeSha256(rawBytes),
                        Length = rawBytes.Length
                    },
                    ContentBase64 = Convert.ToBase64String(rawBytes),
                    SemanticValueCanonicalJson = semantic,
                    SemanticDigest = MigrationDigest.ComputeSha256(semantic),
                    EvidenceReferences = Refs("m2-artifact.bin", "m2-semantic.json")
                };
            }

            public IngredientPlanEvidence PlanEvidence()
            {
                return new IngredientPlanEvidence
                {
                    Plan = Plan,
                    ExpectedSourceSnapshotDigest = SourceSnapshotDigest,
                    ExpectedPlanDigest = PlanDigest,
                    IngredientId = Node.Id,
                    SnapshotPlanEvidenceReferences = Refs("m3-plan.json"),
                    ActionEvidenceReferences = Refs("m3-action.json"),
                    DependencyPolicyEvidenceReferences = Refs("m3-dependencies.json")
                };
            }

            public IngredientOperationalEvidence OperationalEvidence()
            {
                var now = DateTimeOffset.UtcNow;
                var startedAt = now.AddMinutes(-1);
                var signature = MigrationActionSignature.Create(
                    Plan.IngredientActions[0].ActionId,
                    "preserve-content",
                    SourceArtifactDigest,
                    MigrationActionSignature.EmptySelectionReceiptDigest,
                    TargetIdentity,
                    MigrationDigest.ComputeSha256("semantic"));
                var operationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
                var step = new MigrationMutationReceipt
                {
                    OperationId = operationId,
                    PlanDigest = PlanDigest,
                    ActionId = signature.ActionId,
                    ActionSignature = signature.Signature,
                    Outcome = MutationOutcome.Applied,
                    CompletedAtUtc = now
                };
                return new IngredientOperationalEvidence
                {
                    Plan = Plan,
                    AdmittedPlanDigest = PlanDigest,
                    AdmissionPassed = true,
                    IngredientId = Node.Id,
                    ActionSignature = signature,
                    ImportReceipt = new PublishingPageImportReceipt
                    {
                        OperationId = operationId,
                        StartedAtUtc = startedAt,
                        CompletedAtUtc = now,
                        ApprovedPlanDigest = PlanDigest,
                        ExecutionStatus = MigrationExecutionStatus.Succeeded,
                        Steps = new List<MigrationMutationReceipt> { step },
                        OwnershipMatched = true,
                        FreshReadbackPassed = true,
                        StorageVerificationStatus = StorageVerificationStatus.Passed,
                        RuntimeVerificationStatus = RuntimeVerificationStatus.NotRequired,
                        VerifiedIngredientIds = new List<string> { Node.Id }
                    },
                    JournalState = new MigrationExecutionStateReceipt
                    {
                        OperationId = operationId,
                        PlanDigest = PlanDigest,
                        Status = MigrationExecutionStatus.Succeeded,
                        RecordedAtUtc = now
                    },
                    VerificationReceipt = new MigrationMutationVerificationReceipt
                    {
                        OperationId = operationId,
                        PlanDigest = PlanDigest,
                        ActionId = signature.ActionId,
                        ActionSignature = signature.Signature,
                        FreshReadbackPassed = true,
                        Ownership = MigrationTargetOwnership.MigrationOwned,
                        ProvenanceMatched = true,
                        VerifiedAtUtc = now
                    },
                    ExecutionStartedAtUtc = startedAt,
                    CleanupRequired = true,
                    CleanupPassed = true,
                    RetryEvidenceRequired = true,
                    RetryPassed = true,
                    PlanAdmissionEvidenceReferences = Refs("m4-admission.json"),
                    ActionEvidenceReferences = Refs("m4-signature.json"),
                    ReceiptEvidenceReferences = Refs("m4-journal.json", "m4-receipts.json"),
                    FreshReadbackEvidenceReferences = Refs("m4-readback.json"),
                    RuntimeCleanupRetryEvidenceReferences = Refs("m4-cleanup-retry.json")
                };
            }

            public IngredientProductizationEvidence ProductizationEvidence()
            {
                var report = new PublishingPageCompareReport
                {
                    SchemaVersion = PublishingPageCompareContract.SchemaVersion,
                    Bindings = new CompareBindings { PlanDigestSha256 = PlanDigest },
                    Ingredients = new List<IngredientCompareResult>(),
                    Acceptance = new CompareAcceptance { Verdict = "conditional" }
                };
                report.ReportDigestSha256 = PublishingPageCompareReconciler.ComputeReportDigest(report);
                return new IngredientProductizationEvidence
                {
                    TestId = "IngredientMaturityEvaluatorTests",
                    HermeticUnitTest = true,
                    RedTestEvidenceReference = "m5-red.trx",
                    GreenTestEvidenceReference = "m5-green.trx",
                    ImplementationCommit = ImplementationCommit,
                    BuildCommit = ImplementationCommit,
                    BinaryDigest = BinaryDigest,
                    BuildEvidenceReference = "m5-build.json",
                    EndToEndCommit = ImplementationCommit,
                    EndToEndPlanDigest = PlanDigest,
                    EndToEndEvidenceReference = "m5-e2e.json",
                    CompareReport = report,
                    CompareReportDigest = report.ReportDigestSha256,
                    CompareEvidenceReference = "m5-compare.json",
                    ArchitectReviewVerdict = "APPROVED",
                    ArchitectReviewEvidenceReference = "m5-architect-review.md",
                    CtoReviewVerdict = "APPROVED",
                    CtoReviewEvidenceReference = "m5-cto-review.md",
                    IndependentVerificationVerdict = "PASS",
                    IndependentVerificationEvidenceReference = "m5-verification.md",
                    PrReadyCommit = ImplementationCommit,
                    PrReadyEvidenceReference = "m5-pr-ready.json"
                };
            }

            private IngredientLiveEvidence LiveEvidence()
            {
                return new IngredientLiveEvidence
                {
                    SourceAuthenticated = true,
                    TargetFreshReadback = true,
                    SourceEvidenceReferences = Refs("m1-source.json"),
                    TargetEvidenceReferences = Refs("m1-target.json"),
                    Observations = new List<IngredientValueObservation>
                    {
                        Observation("source.value", IngredientObservationOrigin.AuthenticatedSource, "m1-observation-source.json"),
                        Observation("target.value", IngredientObservationOrigin.CupCollectFreshReadback, "m1-observation-target.json")
                    }
                };
            }

            private static IngredientValueObservation Observation(string path, IngredientObservationOrigin origin, string evidenceReference)
            {
                return new IngredientValueObservation
                {
                    ValuePath = path,
                    ValueDigest = MigrationDigest.ComputeSha256(path),
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                    Origin = origin,
                    EvidenceReference = evidenceReference
                };
            }

            private static List<string> Refs(params string[] values)
            {
                return values.OrderBy(value => value, StringComparer.Ordinal).ToList();
            }
        }

        private static List<string> Refs(params string[] values)
        {
            return values.OrderBy(value => value, StringComparer.Ordinal).ToList();
        }
    }
}
