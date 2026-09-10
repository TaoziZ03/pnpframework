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
        private static readonly DateTimeOffset EvidenceTime = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

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
            operational.VerificationReceipt.OperationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
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
                    CompletedAtUtc = EvidenceTime,
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

            var fixture = Fixture.Create();
            var context = fixture.Context;
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
            fixture.Plan.IngredientActions[0].VerificationAssertions.Add(context.Identity.IngredientId);
            var receipts = IngredientMaturityEvidenceValidator.ValidateM0(context, null, null, null, Refs("behavior-m0.json"),
                new IngredientRuntimeAssertionEvidence
                {
                    SourcePredicateId = context.Identity.SourcePredicateId,
                    CanonicalIngredient = fixture.Node,
                    Action = fixture.Plan.IngredientActions[0]
                });
            var behavior = new TestContributor("behavior-maturity/v1", "behavior.interaction", receipts.ToList());
            var assessment = IngredientMaturityEvaluator.Evaluate(
                context,
                new IngredientMaturityContributorCatalog(new[] { behavior }));
            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            IngredientMaturityEvaluator.ValidateAssessment(assessment, context,
                new IngredientMaturityContributorCatalog(new[] { behavior }));
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

        [DataTestMethod]
        [DataRow("attained")]
        [DataRow("confidence")]
        [DataRow("missing-promotion")]
        [DataRow("target-enum")]
        [DataRow("technical-status-enum")]
        [DataRow("migration-outcome-enum")]
        [DataRow("technical-reason")]
        [DataRow("identity-predicate")]
        [DataRow("identity-work-item")]
        [DataRow("identity-kind-enum")]
        [DataRow("identity-lane")]
        [DataRow("null-levels")]
        [DataRow("missing-level")]
        [DataRow("duplicate-level")]
        [DataRow("unknown-level")]
        [DataRow("level-order")]
        [DataRow("level-passed")]
        [DataRow("null-gates")]
        [DataRow("missing-gate")]
        [DataRow("duplicate-gate")]
        [DataRow("unknown-gate")]
        [DataRow("gate-order")]
        [DataRow("gate-category")]
        [DataRow("gate-status-enum")]
        [DataRow("gate-validator")]
        [DataRow("gate-validator-version")]
        [DataRow("gate-references")]
        [DataRow("gate-reference-order")]
        [DataRow("gate-reference-duplicate")]
        [DataRow("passed-gate-failure")]
        public void AssessmentRejectsRedigestedSemanticTamper(string mutation)
        {
            var assessment = RoundTrip(Fixture.Create().Evaluate());
            var gate = Gate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity);
            switch (mutation)
            {
                case "attained": assessment.AttainedMaturity = IngredientMaturityLevel.M0; break;
                case "confidence": assessment.Confidence = IngredientMaturityConfidence.None; break;
                case "missing-promotion": assessment.MissingPromotionGate = gate.GateId; break;
                case "target-enum": assessment.TargetMaturity = (IngredientMaturityLevel)99; break;
                case "technical-status-enum": assessment.TechnicalOutcome.Status = (IngredientTechnicalStatus)99; break;
                case "migration-outcome-enum": assessment.TechnicalOutcome.PnPMigrationOutcome = (PageMigrationOutcome)99; break;
                case "technical-reason": assessment.TechnicalOutcome.ReasonCode = null; break;
                case "identity-predicate": assessment.Identity.SourcePredicateId = " "; break;
                case "identity-work-item": assessment.Identity.WorkItemType = "unknown"; break;
                case "identity-kind-enum": assessment.Identity.Kind = (PageIngredientKind)999; break;
                case "identity-lane": assessment.Identity.Lane = "unknown"; break;
                case "null-levels": assessment.Levels = null; break;
                case "missing-level": assessment.Levels.RemoveAt(1); break;
                case "duplicate-level": assessment.Levels.Add(assessment.Levels[0]); break;
                case "unknown-level": assessment.Levels[0].Level = (IngredientMaturityLevel)99; break;
                case "level-order": assessment.Levels = assessment.Levels.Reverse().ToList(); break;
                case "level-passed": assessment.Levels[1].Passed = false; break;
                case "null-gates": assessment.Levels[0].Gates = null; break;
                case "missing-gate": assessment.Levels[0].Gates.RemoveAt(0); break;
                case "duplicate-gate": assessment.Levels[0].Gates.Add(gate); break;
                case "unknown-gate": gate.GateId = "m0.self-awarded"; break;
                case "gate-order": assessment.Levels[0].Gates = assessment.Levels[0].Gates.Reverse().ToList(); break;
                case "gate-category": gate.Category = IngredientMaturityGateCategory.DeliveryProcess; break;
                case "gate-status-enum": gate.Status = (IngredientMaturityGateStatus)99; break;
                case "gate-validator": gate.ValidatorId = "lane-self-award"; break;
                case "gate-validator-version": gate.ValidatorVersion = "unknown"; break;
                case "gate-references": gate.EvidenceReferences.Clear(); break;
                case "gate-reference-order": gate.EvidenceReferences = new List<string> { "z", "a" }; break;
                case "gate-reference-duplicate": gate.EvidenceReferences.Add(gate.EvidenceReferences[0]); break;
                case "passed-gate-failure": gate.FailureReason = "This gate actually failed."; break;
                default: Assert.Fail("Unknown mutation."); break;
            }
            assessment.AssessmentDigest = IngredientMaturityEvaluator.ComputeDigest(assessment);
            var independentlyRead = RoundTrip(assessment);
            Assert.ThrowsException<System.IO.InvalidDataException>(
                () => IngredientMaturityEvaluator.ValidateAssessment(independentlyRead), mutation);
        }

        [TestMethod]
        public void AssessmentRejectsRedigestedContinuityBypass()
        {
            var fixture = Fixture.Create();
            fixture.RemoveGates(IngredientMaturityGateCatalog.CupCollectFreshReadback);
            var assessment = RoundTrip(fixture.Evaluate());
            assessment.AttainedMaturity = IngredientMaturityLevel.M5;
            assessment.Confidence = IngredientMaturityConfidence.High;
            assessment.MissingPromotionGate = null;
            foreach (var level in assessment.Levels)
            {
                level.Passed = true;
            }
            assessment.AssessmentDigest = IngredientMaturityEvaluator.ComputeDigest(assessment);
            Assert.ThrowsException<System.IO.InvalidDataException>(
                () => IngredientMaturityEvaluator.ValidateAssessment(RoundTrip(assessment)));
        }

        private static IngredientMaturityAssessment RoundTrip(IngredientMaturityAssessment assessment)
        {
            return MigrationContractSerializer.Deserialize<IngredientMaturityAssessment>(
                MigrationContractSerializer.SerializeCanonical(assessment));
        }

        [DataTestMethod]
        [DataRow("technical-status")]
        [DataRow("migration-outcome")]
        [DataRow("technical-reason")]
        [DataRow("source-version")]
        [DataRow("source-digest")]
        [DataRow("target-identity")]
        [DataRow("producer-commit")]
        [DataRow("target-maturity")]
        [DataRow("evidence-reference")]
        public void BoundAssessmentRejectsCoherentRedigestedSubstitution(string mutation)
        {
            var fixture = Fixture.Create();
            var assessment = RoundTrip(fixture.Evaluate());
            switch (mutation)
            {
                case "technical-status": assessment.TechnicalOutcome.Status = IngredientTechnicalStatus.Pass; break;
                case "migration-outcome": assessment.TechnicalOutcome.PnPMigrationOutcome = PageMigrationOutcome.Exact; break;
                case "technical-reason": assessment.TechnicalOutcome.ReasonCode = "another-policy"; break;
                case "source-version": assessment.Source.SourceVersion = "etag:13"; break;
                case "source-digest": assessment.Source.SourceArtifactDigest = new string('f', 64); break;
                case "target-identity": assessment.Target.TargetIdentity = "cupcollect:/another-page.aspx"; break;
                case "producer-commit": assessment.Producer.ImplementationCommit = new string('f', 40); break;
                case "target-maturity": assessment.TargetMaturity = IngredientMaturityLevel.M0; break;
                case "evidence-reference":
                    Gate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity).EvidenceReferences = Refs("foreign-evidence.json");
                    break;
                default: Assert.Fail("Unknown substitution."); break;
            }
            assessment.AssessmentDigest = IngredientMaturityEvaluator.ComputeDigest(assessment);
            var independentlyRead = RoundTrip(assessment);
            // Intrinsic validation cannot authenticate a completely coherent substitution.
            IngredientMaturityEvaluator.ValidateAssessment(independentlyRead);
            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                IngredientMaturityEvaluator.ValidateAssessment(independentlyRead, fixture.Context, fixture.Catalog()), mutation);
        }

        [TestMethod]
        public void BoundAssessmentRejectsCoherentlyReawardedMissingGate()
        {
            var fixture = Fixture.Create();
            fixture.RemoveGates(IngredientMaturityGateCatalog.CupCollectFreshReadback);
            var assessment = RoundTrip(fixture.Evaluate());
            var genuine = Fixture.Create().Evaluate();
            var gates = assessment.Levels[1].Gates;
            gates[1] = Gate(genuine, IngredientMaturityGateCatalog.CupCollectFreshReadback);
            foreach (var level in assessment.Levels)
            {
                level.Passed = true;
            }
            assessment.AttainedMaturity = IngredientMaturityLevel.M5;
            assessment.Confidence = IngredientMaturityConfidence.High;
            assessment.MissingPromotionGate = null;
            assessment.AssessmentDigest = IngredientMaturityEvaluator.ComputeDigest(assessment);
            IngredientMaturityEvaluator.ValidateAssessment(RoundTrip(assessment));
            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                IngredientMaturityEvaluator.ValidateAssessment(RoundTrip(assessment), fixture.Context, fixture.Catalog()));
        }

        [TestMethod]
        public void AssessmentOwnsBindingsAndRejectsLegacyEvaluatorVersion()
        {
            var fixture = Fixture.Create();
            var assessment = fixture.Evaluate();
            fixture.Context.Source.SourceVersion = "changed-after-evaluation";
            fixture.Context.TechnicalOutcome.ReasonCode = "changed-after-evaluation";
            Assert.AreEqual("etag:12", assessment.Source.SourceVersion);
            Assert.AreEqual("policy-limited-but-deterministic", assessment.TechnicalOutcome.ReasonCode);
            IngredientMaturityEvaluator.ValidateAssessment(assessment);
            assessment.EvaluatorVersion = "pnp-ingredient-maturity-evaluator/v1";
            assessment.AssessmentDigest = IngredientMaturityEvaluator.ComputeDigest(assessment);
            Assert.ThrowsException<System.IO.InvalidDataException>(() => IngredientMaturityEvaluator.ValidateAssessment(assessment));
        }

        [TestMethod]
        public void EveryContinuousLevelRoundTripsWithRecomputedPromotionGate()
        {
            for (var attained = -1; attained <= 5; attained++)
            {
                var fixture = Fixture.Create();
                if (attained < 5)
                {
                    fixture.RemoveGates(IngredientMaturityGateCatalog.ForLevel((IngredientMaturityLevel)(attained + 1))[0].GateId);
                }
                var assessment = RoundTrip(fixture.Evaluate());
                Assert.AreEqual(attained < 0 ? (IngredientMaturityLevel?)null : (IngredientMaturityLevel)attained, assessment.AttainedMaturity);
                IngredientMaturityEvaluator.ValidateAssessment(assessment, fixture.Context, fixture.Catalog());
            }
        }

        [TestMethod]
        public void TargetMaturityIsAGoalAndDoesNotChangeTechnicalOutcomeOrCapEvidence()
        {
            var fixture = Fixture.Create();
            fixture.Context.TargetMaturity = IngredientMaturityLevel.M0;
            var assessment = fixture.Evaluate();
            Assert.AreEqual(IngredientMaturityLevel.M5, assessment.AttainedMaturity);
            Assert.IsNull(assessment.MissingPromotionGate);
            Assert.AreEqual(IngredientTechnicalStatus.Conditional, assessment.TechnicalOutcome.Status);
            IngredientMaturityEvaluator.ValidateAssessment(assessment, fixture.Context, fixture.Catalog());
        }

        [DataTestMethod]
        [DataRow("foreign-claim")]
        [DataRow("foreign-ingredient")]
        [DataRow("foreign-source-page")]
        [DataRow("foreign-source-version")]
        [DataRow("foreign-source-artifact")]
        [DataRow("foreign-source-snapshot")]
        [DataRow("foreign-target-profile")]
        [DataRow("foreign-target-identity")]
        [DataRow("missing-source-binding")]
        [DataRow("missing-target-binding")]
        [DataRow("missing-fence")]
        [DataRow("reversed-fence")]
        [DataRow("stale-source")]
        [DataRow("late-source")]
        [DataRow("stale-target")]
        [DataRow("future-target")]
        [DataRow("missing-readback-start")]
        [DataRow("unpaired-value")]
        [DataRow("duplicate-value")]
        [DataRow("missing-target-value")]
        [DataRow("invalid-value-digest")]
        [DataRow("unbound-source-reference")]
        [DataRow("unbound-target-reference")]
        [DataRow("unknown-origin")]
        [DataRow("historical-origin")]
        [DataRow("synthetic-origin")]
        [DataRow("substituted-evidence")]
        [DataRow("source-not-authenticated")]
        [DataRow("target-not-fresh")]
        public void M1RejectsForeignUnpairedOrStaleObservations(string mutation)
        {
            var fixture = Fixture.Create();
            var evidence = fixture.LiveEvidence();
            var source = evidence.Observations[0];
            var target = evidence.Observations[1];
            switch (mutation)
            {
                case "foreign-claim": source.ClaimId = new string('f', 64); break;
                case "foreign-ingredient": target.IngredientId = "ingredient:other"; break;
                case "foreign-source-page": source.Source.PageOrListItemIdentity = "another-page"; break;
                case "foreign-source-version": source.Source.SourceVersion = "etag:11"; break;
                case "foreign-source-artifact": source.Source.SourceArtifactDigest = new string('f', 64); break;
                case "foreign-source-snapshot": target.Source.SourceSnapshotDigest = new string('f', 64); break;
                case "foreign-target-profile": target.Target.TargetProfile = "MSIT"; break;
                case "foreign-target-identity": target.Target.TargetIdentity = "cupcollect:/other.aspx"; break;
                case "missing-source-binding": source.Source = null; break;
                case "missing-target-binding": target.Target = null; break;
                case "missing-fence": fixture.Context.ObservationWindowStartUtc = null; break;
                case "reversed-fence": fixture.Context.ObservationWindowEndUtc = fixture.Context.ObservationWindowStartUtc.Value.AddSeconds(-1); break;
                case "stale-source": source.ObservedAtUtc = fixture.Context.ObservationWindowStartUtc.Value.AddSeconds(-1); break;
                case "late-source": source.ObservedAtUtc = evidence.ReadbackStartedAtUtc.AddSeconds(1); break;
                case "stale-target": target.ObservedAtUtc = evidence.ReadbackStartedAtUtc.AddSeconds(-1); break;
                case "future-target": target.ObservedAtUtc = fixture.Context.ObservationWindowEndUtc.Value.AddSeconds(1); break;
                case "missing-readback-start": evidence.ReadbackStartedAtUtc = default; break;
                case "unpaired-value": target.ValuePath = "another-value"; break;
                case "duplicate-value": evidence.Observations.Add(source); break;
                case "missing-target-value": evidence.Observations.RemoveAt(1); break;
                case "invalid-value-digest": target.ValueDigest = "invalid"; break;
                case "unbound-source-reference": source.EvidenceReference = "foreign-source.json"; break;
                case "unbound-target-reference": target.EvidenceReference = "foreign-target.json"; break;
                case "unknown-origin": target.Origin = (IngredientObservationOrigin)99; break;
                case "historical-origin": source.Origin = IngredientObservationOrigin.Historical; break;
                case "synthetic-origin": target.Origin = IngredientObservationOrigin.Synthetic; break;
                case "substituted-evidence": evidence.HistoricalOrSyntheticSubstitution = true; break;
                case "source-not-authenticated": evidence.SourceAuthenticated = false; break;
                case "target-not-fresh": evidence.TargetFreshReadback = false; break;
                default: Assert.Fail("Unknown observation mutation."); break;
            }
            fixture.ReplaceLevel(IngredientMaturityLevel.M1, IngredientMaturityEvidenceValidator.ValidateM1(fixture.Context, evidence));
            var assessment = fixture.Evaluate();
            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity, mutation);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed,
                Gate(assessment, IngredientMaturityGateCatalog.PerValueObservation).Status, mutation);
        }

        [TestMethod]
        public void M1LegacyUnboundOverloadCannotAwardMaturity()
        {
            var fixture = Fixture.Create();
            var receipts = IngredientMaturityEvidenceValidator.ValidateM1(fixture.LiveEvidence());
            Assert.IsTrue(receipts.All(value => !value.Passed));
            fixture.ReplaceLevel(IngredientMaturityLevel.M1, receipts);
            Assert.AreEqual(IngredientMaturityLevel.M0, fixture.Evaluate().AttainedMaturity);
        }

        [TestMethod]
        public void M1RecordsDifferentValueDigestsWithoutInventingAnOutcomeRule()
        {
            var fixture = Fixture.Create();
            var evidence = fixture.LiveEvidence();
            evidence.Observations[1].ValueDigest = MigrationDigest.ComputeSha256("policy-transformed-value");
            var receipts = IngredientMaturityEvidenceValidator.ValidateM1(fixture.Context, evidence);
            Assert.IsTrue(receipts.All(value => value.Passed));
            fixture.ReplaceLevel(IngredientMaturityLevel.M1, receipts);
            var assessment = fixture.Evaluate();
            Assert.AreEqual(IngredientMaturityLevel.M5, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientTechnicalStatus.Conditional, assessment.TechnicalOutcome.Status);
        }

        [DataTestMethod]
        [DataRow("missing-predicate")]
        [DataRow("blank-predicate")]
        [DataRow("foreign-predicate")]
        [DataRow("missing-evidence")]
        [DataRow("foreign-action-ingredient")]
        [DataRow("missing-assertion")]
        [DataRow("duplicate-assertion")]
        [DataRow("foreign-dependency-version")]
        public void RuntimeM0RequiresPredicateAndCanonicalActionDependency(string mutation)
        {
            var fixture = Fixture.Create();
            var context = fixture.Context;
            context.Identity.WorkItemType = IngredientMaturityContract.RuntimeVerificationWorkItem;
            context.Identity.Kind = null;
            context.Identity.Lane = "behavior.interaction";
            context.Identity.IngredientId = "assertion:interaction";
            context.Identity.Subtype = "interaction-state-transition";
            context.Identity.SemanticRole = "runtime-verification-assertion";
            context.Identity.SourcePredicateId = "runtime.assertion";
            var action = fixture.Plan.IngredientActions[0];
            action.VerificationAssertions.Add(context.Identity.IngredientId);
            var evidence = new IngredientRuntimeAssertionEvidence
            {
                SourcePredicateId = context.Identity.SourcePredicateId,
                CanonicalIngredient = fixture.Node,
                Action = action
            };
            switch (mutation)
            {
                case "missing-predicate": context.Identity.SourcePredicateId = null; break;
                case "blank-predicate": context.Identity.SourcePredicateId = " "; break;
                case "foreign-predicate": evidence.SourcePredicateId = "another.predicate"; break;
                case "missing-evidence": evidence = null; break;
                case "foreign-action-ingredient": action.IngredientId = "another-ingredient"; break;
                case "missing-assertion": action.VerificationAssertions.Clear(); break;
                case "duplicate-assertion": action.VerificationAssertions.Add(context.Identity.IngredientId); break;
                case "foreign-dependency-version": fixture.Node.SourceVersionIdentity = "etag:13"; break;
                default: Assert.Fail("Unknown runtime mutation."); break;
            }
            var receipts = IngredientMaturityEvidenceValidator.ValidateM0(context, null, null, null, Refs("runtime-m0.json"), evidence);
            Assert.IsTrue(receipts.Any(value => !value.Passed), mutation);
            var catalog = new IngredientMaturityContributorCatalog(new[]
            {
                new TestContributor("behavior-maturity/v2", "behavior.interaction", receipts.ToList())
            });
            if (mutation == "missing-predicate" || mutation == "blank-predicate")
            {
                Assert.ThrowsException<System.IO.InvalidDataException>(() => IngredientMaturityEvaluator.Evaluate(context, catalog));
            }
            else
            {
                var assessment = IngredientMaturityEvaluator.Evaluate(context, catalog);
                Assert.IsNull(assessment.AttainedMaturity, mutation);
                IngredientMaturityEvaluator.ValidateAssessment(assessment, context, catalog);
            }
        }

        [TestMethod]
        public void ContributorUnknownNullDuplicateAndUnsupportedValidatorReceiptsFailClosed()
        {
            foreach (var mutation in new[] { "unknown", "null", "duplicate", "validator", "version", "missing-failure" })
            {
                var fixture = Fixture.Create();
                var receipts = IngredientMaturityEvidenceValidator.ValidateM0(fixture.Context, fixture.Node, null, CreateOwnerRegistry(), Refs("m0.json")).ToList();
                switch (mutation)
                {
                    case "unknown": receipts[0].GateId = "unknown"; break;
                    case "null": receipts.Add(null); break;
                    case "duplicate": receipts.Add(receipts[0]); break;
                    case "validator": receipts[0].ValidatorId = "self-awarded"; break;
                    case "version": receipts[0].ValidatorVersion = "v1"; break;
                    case "missing-failure": receipts[0].Passed = false; receipts[0].FailureReason = null; break;
                }
                fixture.ReplaceLevel(IngredientMaturityLevel.M0, receipts);
                if (mutation == "unknown" || mutation == "null")
                {
                    Assert.ThrowsException<System.IO.InvalidDataException>(() => fixture.Evaluate(), mutation);
                }
                else
                {
                    var assessment = fixture.Evaluate();
                    Assert.IsNull(assessment.AttainedMaturity, mutation);
                    IngredientMaturityEvaluator.ValidateAssessment(assessment);
                }
            }
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
                    ObservationWindowStartUtc = EvidenceTime.AddMinutes(-2),
                    ObservationWindowEndUtc = EvidenceTime.AddMinutes(1),
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
                var assessment = IngredientMaturityEvaluator.Evaluate(Context, Catalog());
                IngredientMaturityEvaluator.ValidateAssessment(assessment);
                return assessment;
            }

            public IngredientMaturityContributorCatalog Catalog()
            {
                return new IngredientMaturityContributorCatalog(new[]
                {
                    new TestContributor("content.text.maturity/v1", "content.text", receipts)
                });
            }

            public void RefreshPlanDigest()
            {
                PlanDigest = PublishingPageDigest.ComputePlanDigest(Plan);
            }

            public void RebuildAllReceipts()
            {
                receipts.Clear();
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(Context, Node, null, CreateOwnerRegistry(), Refs("m0.json")));
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(Context, LiveEvidence()));
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
                var now = EvidenceTime;
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

            public IngredientLiveEvidence LiveEvidence()
            {
                return new IngredientLiveEvidence
                {
                    SourceAuthenticated = true,
                    TargetFreshReadback = true,
                    ReadbackStartedAtUtc = EvidenceTime.AddMinutes(-1),
                    SourceEvidenceReferences = Refs("m1-observation-source.json"),
                    TargetEvidenceReferences = Refs("m1-observation-target.json"),
                    Observations = new List<IngredientValueObservation>
                    {
                        Observation("body.value", IngredientObservationOrigin.AuthenticatedSource, "m1-observation-source.json"),
                        Observation("body.value", IngredientObservationOrigin.CupCollectFreshReadback, "m1-observation-target.json")
                    }
                };
            }

            private IngredientValueObservation Observation(string path, IngredientObservationOrigin origin, string evidenceReference)
            {
                return new IngredientValueObservation
                {
                    ClaimId = Context.Identity.ClaimId,
                    IngredientId = Context.Identity.IngredientId,
                    Source = MigrationContractSerializer.Deserialize<IngredientMaturitySourceBinding>(
                        MigrationContractSerializer.SerializeCanonical(Context.Source)),
                    Target = MigrationContractSerializer.Deserialize<IngredientMaturityTargetBinding>(
                        MigrationContractSerializer.SerializeCanonical(Context.Target)),
                    ValuePath = path,
                    ValueDigest = MigrationDigest.ComputeSha256(path),
                    ObservedAtUtc = origin == IngredientObservationOrigin.AuthenticatedSource ? EvidenceTime.AddMinutes(-2) : EvidenceTime,
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
