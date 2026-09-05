using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Scale;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static PnP.Framework.Test.Migration.Scale.ScaleRunTestFixture;

namespace PnP.Framework.Test.Migration.Scale
{
    [TestClass]
    public class ScaleRunControllerTests
    {
        private readonly IList<string> temporaryRoots = new List<string>();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var root in temporaryRoots)
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [TestMethod]
        public void ManifestIsCanonicalAndRejectsUrlShapedReferences()
        {
            var manifest = Manifest(2);
            manifest.Pages = manifest.Pages.Reverse().ToList();
            ScaleRunManifestValidator.Seal(manifest);
            ScaleRunManifestValidator.Validate(manifest);
            Assert.AreEqual(0, manifest.Pages[0].Ordinal);

            manifest = Manifest(1);
            manifest.Pages[0].SourceReferenceKey = "https://source.example/page";
            Assert.ThrowsException<InvalidDataException>(() => ScaleRunManifestValidator.Seal(manifest));

            manifest = Manifest(1);
            manifest.Pages[0].SourceReferenceKey = "password=not-manifest-data";
            Assert.ThrowsException<InvalidDataException>(() => ScaleRunManifestValidator.Seal(manifest));
        }

        [TestMethod]
        public async Task NestedGateMembershipOrderAndConcurrencyDoNotInvalidatePageCheckpoints()
        {
            var root = TemporaryRoot();
            var executors = Executors();
            var gateOne = Manifest(1);
            await new ScaleRunController(executors).RunAsync(
                gateOne,
                new ScaleRunControllerOptions { OutputRoot = root });

            var gateTwo = Manifest(2);
            gateTwo.LoopId = "loop-002";
            gateTwo.Pages.Single(value => value.PageKey == "page-000").Ordinal = 20;
            gateTwo.Pages.Single(value => value.PageKey == "page-000").LoadBucket = "heavy";
            gateTwo.Pages.Single(value => value.PageKey == "page-001").Ordinal = 10;
            gateTwo.Policy.QueueCapacity = 7;
            gateTwo.Policy.StageConcurrency.Single(value => value.Stage == ScaleRunStage.Plan).Maximum = 2;
            ScaleRunManifestValidator.Seal(gateTwo);
            var second = await new ScaleRunController(executors).RunAsync(
                gateTwo,
                new ScaleRunControllerOptions { OutputRoot = root });

            var existing = second.Pages.Single(value => value.PageKey == "page-000");
            Assert.IsTrue(existing.Stages.Single(value => value.Stage == ScaleRunStage.Collect).ResumeSkipped);
            Assert.AreEqual(1, executors.Single(value => value.Stage == ScaleRunStage.Repro).ExecuteCount("page-000"));
        }

        [TestMethod]
        public void TargetSlotIdentityIsCampaignNeutralWhileActionSelectionIsNot()
        {
            var first = Manifest(1);
            var second = Manifest(1);
            second.RunKey = "another-campaign";
            ScaleRunManifestValidator.Seal(second);
            var executor = Executors().Single(value => value.Stage == ScaleRunStage.Collect);
            var firstAction = ScaleRunIdentity.CreateAction(
                first,
                first.Pages[0],
                ScaleRunStage.Collect,
                executor,
                Array.Empty<ScaleStageArtifact>(),
                null);
            var secondAction = ScaleRunIdentity.CreateAction(
                second,
                second.Pages[0],
                ScaleRunStage.Collect,
                executor,
                Array.Empty<ScaleStageArtifact>(),
                null);

            Assert.AreEqual(firstAction.TargetIdentityDigest, secondAction.TargetIdentityDigest);
            Assert.AreNotEqual(firstAction.Signature, secondAction.Signature);
        }

        [TestMethod]
        public async Task PipelineWritesStageJournalAndSimulationUsesNoMutationReceipts()
        {
            var root = TemporaryRoot();
            var summary = await new ScaleRunController(Executors()).RunAsync(
                Manifest(3),
                new ScaleRunControllerOptions { OutputRoot = root, ImprovementReference = "pr-10" });

            Assert.AreEqual(3, summary.AcceptedCount);
            Assert.AreEqual(6, summary.StageSummaries.Count);
            Assert.AreEqual("Advance", summary.CatalogProjection.Gate);
            Assert.AreEqual(summary.SummaryDigest, ScaleRunStorage.ComputeSummaryDigest(summary));
            var stages = ScaleStageExecutionJournalReader.Read(ScaleRunStorage.StageJournalPath(root));
            Assert.IsFalse(stages.HasInterruptedTail);
            Assert.AreEqual(18, stages.Records.Count(value =>
                value.RecordKind == ScaleStageExecutionJournalRecordKind.AttemptCompleted));
            var mutations = MigrationExecutionJournalReader.Read(ScaleRunStorage.JournalPath(root));
            Assert.AreEqual(0, mutations.Records.Count(value =>
                value.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent));
            Assert.AreEqual(0, mutations.Records.Count(value =>
                value.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt));
        }

        [TestMethod]
        public async Task ResumeSkipsArtifactStagesFreshProbesReproAndAlwaysRecapturesTarget()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            var executors = Executors();
            var controller = new ScaleRunController(executors);
            await controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root });
            var second = await controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root });

            Assert.AreEqual(1, second.AcceptedCount);
            Assert.AreEqual(5, second.ResumeSkipCount);
            Assert.AreEqual(1, executors.Single(value => value.Stage == ScaleRunStage.Repro).ExecuteCount("page-000"));
            Assert.AreEqual(2, executors.Single(value => value.Stage == ScaleRunStage.Repro).ProbeCount("page-000"));
            Assert.AreEqual(2, executors.Single(value => value.Stage == ScaleRunStage.TargetRecapture).ExecuteCount("page-000"));
            var journal = ScaleStageExecutionJournalReader.Read(ScaleRunStorage.StageJournalPath(root));
            var resumedRepro = journal.Records.Last(value =>
                value.RecordKind == ScaleStageExecutionJournalRecordKind.AttemptCompleted
                && value.Stage == ScaleRunStage.Repro
                && value.Attempt == 0);
            Assert.AreEqual("ResumeCheckpointFreshProbeSatisfied", resumedRepro.DiagnosticCode);
            Assert.IsTrue(resumedRepro.Artifacts.Any(value => value.SchemaVersion == "fake-probe/v1"));
        }

        [TestMethod]
        public async Task ExactFreshTargetConvergesWithoutMutationAndCreatesCheckpoint()
        {
            var root = TemporaryRoot();
            var executors = Executors();
            var repro = executors.Single(value => value.Stage == ScaleRunStage.Repro);
            repro.MarkTarget("page-000");

            var summary = await new ScaleRunController(executors).RunAsync(
                Manifest(1),
                new ScaleRunControllerOptions { OutputRoot = root });

            var stage = summary.Pages.Single().Stages.Single(value => value.Stage == ScaleRunStage.Repro);
            Assert.AreEqual(ScaleStageOutcome.AlreadySatisfied, stage.Outcome);
            Assert.AreEqual(0, repro.ExecuteCount("page-000"));
            Assert.IsTrue(File.Exists(ScaleRunStorage.CheckpointPath(
                root,
                Manifest(1).Pages[0],
                ScaleRunStage.Repro)));
        }

        [TestMethod]
        public async Task FreshTargetDriftRequiresRcaAndDoesNotRunMutation()
        {
            var root = TemporaryRoot();
            var executors = Executors();
            var repro = executors.Single(value => value.Stage == ScaleRunStage.Repro);
            repro.ProbeStateOverride = ScaleStageProbeState.Drifted;

            var summary = await new ScaleRunController(executors).RunAsync(
                Manifest(1),
                new ScaleRunControllerOptions { OutputRoot = root });

            Assert.AreEqual(1, summary.NeedsRcaCount);
            Assert.AreEqual(0, repro.ExecuteCount("page-000"));
            var journal = ScaleStageExecutionJournalReader.Read(ScaleRunStorage.StageJournalPath(root));
            Assert.IsTrue(journal.Records.Any(value =>
                value.RecordKind == ScaleStageExecutionJournalRecordKind.AttemptCompleted
                && value.Stage == ScaleRunStage.Repro
                && value.Outcome == ScaleStageOutcome.NeedsRca));
        }

        [TestMethod]
        public async Task ResponseLossFreshProbeRecordsOutcomeUnknownButConverged()
        {
            var root = TemporaryRoot();
            var executors = Executors();
            var repro = executors.Single(value => value.Stage == ScaleRunStage.Repro);
            repro.ResponseLossOnFirstRepro = true;
            repro.AllowLiveMutation = true;
            var manifest = Manifest(1);
            manifest.MutationMode = ScaleRunMutationMode.ExplicitApproved;
            ScaleRunManifestValidator.Seal(manifest);

            var summary = await new ScaleRunController(executors).RunAsync(
                manifest,
                new ScaleRunControllerOptions
                {
                    OutputRoot = root,
                    ExplicitMutationConfirmationDigest = manifest.ManifestDigest
                });

            var stage = summary.Pages.Single().Stages.Single(value => value.Stage == ScaleRunStage.Repro);
            Assert.AreEqual(ScaleStageOutcome.OutcomeUnknownButConverged, stage.Outcome);
            Assert.AreEqual(1, summary.OutcomeUnknownRecoveryCount);
            Assert.AreEqual(1, repro.ExecuteCount("page-000"));
        }

        [TestMethod]
        public async Task VerificationBackpressureAndStageConcurrencyStayBounded()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(8);
            manifest.Policy.MaximumUnverifiedTargets = 2;
            SetConcurrency(manifest, ScaleRunStage.Repro, 8);
            SetConcurrency(manifest, ScaleRunStage.TargetRecapture, 1);
            SetConcurrency(manifest, ScaleRunStage.BrowserAcceptance, 1);
            var executors = Executors();
            executors.Single(value => value.Stage == ScaleRunStage.TargetRecapture).DelayMilliseconds = 20;
            executors.Single(value => value.Stage == ScaleRunStage.BrowserAcceptance).DelayMilliseconds = 20;

            var summary = await new ScaleRunController(executors).RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });

            Assert.AreEqual(2, summary.MaxObservedUnverifiedTargets);
            foreach (var aggregate in summary.StageSummaries)
            {
                Assert.IsTrue(aggregate.MaxObservedConcurrency <= manifest.Policy.StageConcurrency
                    .Single(value => value.Stage == aggregate.Stage).Maximum);
            }
        }

        [TestMethod]
        public async Task RetryHonorsRetryAfterAndRetainsEvidenceMetrics()
        {
            var root = TemporaryRoot();
            var clock = new RecordingClock();
            var executors = Executors();
            executors.Single(value => value.Stage == ScaleRunStage.Plan).RetryFirstAttempt = true;

            var summary = await new ScaleRunController(executors, clock).RunAsync(
                Manifest(1),
                new ScaleRunControllerOptions { OutputRoot = root });
            var stage = summary.Pages.Single().Stages.Single(value => value.Stage == ScaleRunStage.Plan);

            Assert.AreEqual(2, stage.AttemptCount);
            Assert.AreEqual(1, stage.RetryCount);
            Assert.AreEqual(1, stage.Http429Count);
            Assert.AreEqual(2, stage.RequestCount);
            Assert.AreEqual(25d, clock.Delays.Single().TotalMilliseconds);
        }

        [TestMethod]
        public async Task AuthorizationBlockedRequiresDurableLiteral401Or403Evidence()
        {
            var root = TemporaryRoot();
            var executors = Executors();
            executors.Single(value => value.Stage == ScaleRunStage.Collect).AuthorizationStatusCode = 403;
            var summary = await new ScaleRunController(executors).RunAsync(
                Manifest(1),
                new ScaleRunControllerOptions { OutputRoot = root });

            Assert.AreEqual(1, summary.AuthorizationBlockedCount);
            Assert.AreEqual(0, summary.CatalogProjection.PagesUnresolved);
            Assert.AreEqual(1, summary.CatalogProjection.PagesAuthorizationLimited);
            Assert.AreEqual("Advance", summary.CatalogProjection.Gate);
            var evidence = Directory.GetFiles(root, "http-authorization.json", SearchOption.AllDirectories).Single();
            var contract = ScaleRunContractSerializer.Deserialize<ScaleHttpAuthorizationEvidence>(File.ReadAllText(evidence));
            Assert.AreEqual(403, contract.HttpStatusCode);
            var stageJournal = ScaleStageExecutionJournalReader.Read(ScaleRunStorage.StageJournalPath(root));
            Assert.IsTrue(stageJournal.Records.Any(value =>
                value.RecordKind == ScaleStageExecutionJournalRecordKind.AttemptCompleted
                && value.Outcome == ScaleStageOutcome.AuthorizationBlocked));

            var invalidRoot = TemporaryRoot();
            var invalidExecutors = Executors();
            invalidExecutors.Single(value => value.Stage == ScaleRunStage.Collect).AuthorizationStatusCode = 500;
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new ScaleRunController(invalidExecutors).RunAsync(
                    Manifest(1),
                    new ScaleRunControllerOptions { OutputRoot = invalidRoot }));
        }

        [TestMethod]
        public async Task IngredientAuthorizationDoesNotStopThePagePipelineOrHoldTheGate()
        {
            var root = TemporaryRoot();
            var executors = Executors();
            var collect = executors.Single(value => value.Stage == ScaleRunStage.Collect);
            collect.AuthorizationLimitedPageKey = "page-000";
            collect.AuthorizationLimitedIngredientId = "ingredient.protected-payload";
            collect.AuthorizationLimitedDependentIngredientId = "ingredient.protected-payload-consumer";
            collect.AuthorizationLimitedStatusCode = 403;
            var manifest = Manifest(2);
            var controller = new ScaleRunController(executors);

            var summary = await controller.RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });

            Assert.AreEqual(1, summary.AcceptedCount);
            Assert.AreEqual(1, summary.AuthorizationLimitedCount);
            Assert.AreEqual(1, summary.IngredientAuthorizationBlockedCount);
            Assert.AreEqual(1, summary.IngredientSkippedByDependencyCount);
            Assert.AreEqual(0, summary.CatalogProjection.PagesUnresolved);
            Assert.AreEqual(1, summary.CatalogProjection.PagesAuthorizationLimited);
            Assert.AreEqual(1, summary.CatalogProjection.IngredientsAuthorizationBlocked);
            Assert.AreEqual(1, summary.CatalogProjection.IngredientsSkippedByDependency);
            Assert.AreEqual("Advance", summary.CatalogProjection.Gate);
            var limited = summary.Pages.Single(value => value.PageKey == "page-000");
            Assert.AreEqual(ScalePageDisposition.AuthorizationLimited, limited.Disposition);
            Assert.AreEqual(6, limited.Stages.Count);
            Assert.AreEqual(
                ScaleIngredientOutcome.AuthorizationBlocked,
                limited.Stages.Single(value => value.Stage == ScaleRunStage.Collect)
                    .Ingredients.Single(value => value.IngredientId == "ingredient.protected-payload").Outcome);
            Assert.AreEqual(
                ScalePageDisposition.Accepted,
                summary.Pages.Single(value => value.PageKey == "page-001").Disposition);

            var resumed = await controller.RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });
            Assert.AreEqual(1, collect.ExecuteCount("page-000"));
            Assert.AreEqual(ScalePageDisposition.AuthorizationLimited,
                resumed.Pages.Single(value => value.PageKey == "page-000").Disposition);
            Assert.AreEqual(2,
                resumed.Pages.Single(value => value.PageKey == "page-000").Stages
                    .Single(value => value.Stage == ScaleRunStage.Collect).Ingredients.Count);
        }

        [TestMethod]
        public async Task IngredientAuthorizationRejectsNon401Or403Evidence()
        {
            var executors = Executors();
            var collect = executors.Single(value => value.Stage == ScaleRunStage.Collect);
            collect.AuthorizationLimitedPageKey = "page-000";
            collect.AuthorizationLimitedStatusCode = 500;

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new ScaleRunController(executors).RunAsync(
                    Manifest(1),
                    new ScaleRunControllerOptions { OutputRoot = TemporaryRoot() }));
        }

        [TestMethod]
        public async Task IngredientAuthorizationCheckpointTamperFailsClosedOnResume()
        {
            var root = TemporaryRoot();
            var executors = Executors();
            var collect = executors.Single(value => value.Stage == ScaleRunStage.Collect);
            collect.AuthorizationLimitedPageKey = "page-000";
            var manifest = Manifest(1);
            var controller = new ScaleRunController(executors);
            await controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root });

            var checkpointPath = ScaleRunStorage.CheckpointPath(
                root, manifest.Pages[0], ScaleRunStage.Collect);
            var checkpoint = ScaleRunContractSerializer.Deserialize<ScaleStageCheckpoint>(
                File.ReadAllText(checkpointPath));
            checkpoint.Ingredients.Single(value =>
                value.Outcome == ScaleIngredientOutcome.AuthorizationBlocked).IngredientId = "ingredient.tampered";
            ScaleRunStorage.WriteCheckpointAtomic(root, manifest.Pages[0], checkpoint);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));
        }

        [TestMethod]
        public async Task LegacyV1StageStateIsIsolatedFromTheV2ContractNamespace()
        {
            var root = TemporaryRoot();
            File.WriteAllText(Path.Combine(root, "scale-stage-journal.jsonl"), "{\"schemaVersion\":\"pnp-scale-stage-journal-record/v1\",\"legacy\":true}\n");
            var legacyCheckpointRoot = Path.Combine(root, "items", "legacy", "stages", "collect");
            Directory.CreateDirectory(legacyCheckpointRoot);
            File.WriteAllText(Path.Combine(legacyCheckpointRoot, "stage-checkpoint.json"), "{\"schemaVersion\":\"pnp-scale-stage-checkpoint/v1\"}\n");

            var summary = await new ScaleRunController(Executors()).RunAsync(
                Manifest(1),
                new ScaleRunControllerOptions { OutputRoot = root, Resume = false });

            Assert.AreEqual(1, summary.AcceptedCount);
            StringAssert.Contains(ScaleRunStorage.StageJournalPath(root), "contracts-v2");
            Assert.IsTrue(File.Exists(ScaleRunStorage.StageJournalPath(root)));
        }

        [TestMethod]
        public async Task ResealedCheckpointCannotEraseJournaledDiscoveredProfile()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            var page = manifest.Pages[0];
            var executors = Executors();
            var planExecutor = executors.Single(value => value.Stage == ScaleRunStage.Plan);
            var profile = ScalePageProfile.Seal(new ScalePageProfile
            {
                PageFamily = page.PageFamily,
                TargetReferenceKey = page.TargetReferenceKey,
                SupportCohortSignature = page.SupportCohortSignature,
                ExecutionCohortSignature = page.ExecutionCohortSignature,
                LoadBucket = page.LoadBucket
            });
            executors.Remove(planExecutor);
            var allExecutors = executors.Concat(new IScaleRunStageExecutor[]
            {
                new DiscoveringPlanExecutor(planExecutor, profile)
            }).ToList();
            var controller = new ScaleRunController(allExecutors);
            await controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root });

            var path = ScaleRunStorage.CheckpointPath(root, page, ScaleRunStage.Plan);
            var checkpoint = ScaleRunContractSerializer.Deserialize<ScaleStageCheckpoint>(File.ReadAllText(path));
            Assert.IsNotNull(checkpoint.DiscoveredProfile);
            checkpoint.DiscoveredProfile = null;
            ScaleRunStorage.WriteCheckpointAtomic(root, page, checkpoint);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root, Resume = true }));
        }

        [TestMethod]
        public async Task DependencySkipMustResolveToAuthorizationWithinTheSameStage()
        {
            var executors = Executors();
            var collect = executors.Single(value => value.Stage == ScaleRunStage.Collect);
            collect.AuthorizationLimitedPageKey = "page-000";
            collect.AuthorizationLimitedDependentCauseIngredientId = "ingredient.unreported-cause";

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new ScaleRunController(executors).RunAsync(
                    Manifest(1),
                    new ScaleRunControllerOptions { OutputRoot = TemporaryRoot() }));
        }

        [TestMethod]
        public async Task MutationDisabledAndLiveMutationApprovalAreFailClosed()
        {
            var disabled = Manifest(1);
            disabled.MutationMode = ScaleRunMutationMode.Disabled;
            ScaleRunManifestValidator.Seal(disabled);
            var disabledExecutors = Executors();
            var disabledSummary = await new ScaleRunController(disabledExecutors).RunAsync(
                disabled,
                new ScaleRunControllerOptions { OutputRoot = TemporaryRoot() });
            Assert.AreEqual(1, disabledSummary.NeedsPolicyDecisionCount);
            Assert.AreEqual(3, disabledSummary.Pages.Single().Stages.Count);

            var approved = Manifest(1);
            approved.MutationMode = ScaleRunMutationMode.ExplicitApproved;
            ScaleRunManifestValidator.Seal(approved);
            var liveExecutors = Executors();
            liveExecutors.Single(value => value.Stage == ScaleRunStage.Repro).AllowLiveMutation = true;
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                new ScaleRunController(liveExecutors).RunAsync(
                    approved,
                    new ScaleRunControllerOptions { OutputRoot = TemporaryRoot() }));
            var liveRoot = TemporaryRoot();
            var accepted = await new ScaleRunController(liveExecutors).RunAsync(
                approved,
                new ScaleRunControllerOptions
                {
                    OutputRoot = liveRoot,
                    ExplicitMutationConfirmationDigest = approved.ManifestDigest
                });
            Assert.AreEqual(1, accepted.AcceptedCount);
            Assert.AreEqual(approved.ManifestDigest, accepted.MutationApprovalDigest);
            var mutationJournal = MigrationExecutionJournalReader.Read(
                ScaleRunStorage.JournalPath(liveRoot));
            Assert.AreEqual(1, mutationJournal.Records.Count(value =>
                value.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent));
        }

        [TestMethod]
        public async Task StageFaultCancelsBoundedPipelineWithoutDeadlock()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(20);
            manifest.Policy.QueueCapacity = 1;
            ScaleRunManifestValidator.Seal(manifest);
            var executors = Executors();
            executors.Single(value => value.Stage == ScaleRunStage.Collect).ReturnInvalidArtifact = true;
            var run = new ScaleRunController(executors).RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });

            var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(run, completed, "The pipeline did not cancel upstream producers after a stage fault.");
            await Assert.ThrowsExceptionAsync<InvalidDataException>(async () => await run);
            Assert.IsTrue(File.Exists(Path.Combine(root, "run-summary.json")));
            var summary = ScaleRunContractSerializer.Deserialize<ScaleRunSummary>(
                File.ReadAllText(Path.Combine(root, "run-summary.json")));
            Assert.IsTrue(summary.FailedUnexpectedlyCount > 0);
        }

        [TestMethod]
        public async Task IntentWithoutReceiptFreshProbesAndDoesNotBlindReplay()
        {
            var root = TemporaryRoot();
            var planningExecutors = Executors();
            var planningManifest = Manifest(1);
            planningManifest.MutationMode = ScaleRunMutationMode.Disabled;
            ScaleRunManifestValidator.Seal(planningManifest);
            await new ScaleRunController(planningExecutors).RunAsync(
                planningManifest,
                new ScaleRunControllerOptions { OutputRoot = root });
            var manifest = Manifest(1);
            manifest.MutationMode = ScaleRunMutationMode.ExplicitApproved;
            ScaleRunManifestValidator.Seal(manifest);
            var executors = Executors();
            var repro = executors.Single(value => value.Stage == ScaleRunStage.Repro);
            repro.AllowLiveMutation = true;
            var planCheckpoint = ScaleRunContractSerializer.Deserialize<ScaleStageCheckpoint>(File.ReadAllText(
                ScaleRunStorage.CheckpointPath(root, manifest.Pages[0], ScaleRunStage.Plan)));
            var action = ScaleRunIdentity.CreateAction(
                manifest,
                manifest.Pages[0],
                ScaleRunStage.Repro,
                repro,
                planCheckpoint.Artifacts,
                planCheckpoint.ActionSignature);
            using (var journal = new JsonLinesMigrationExecutionJournal(ScaleRunStorage.JournalPath(root)))
            {
                journal.WriteIntent(new MigrationMutationIntent
                {
                    OperationId = Guid.NewGuid(),
                    PlanDigest = manifest.ManifestDigest,
                    ActionId = action.ActionId,
                    ActionSignature = action.Signature,
                    Sequence = 0,
                    WrittenAtUtc = DateTimeOffset.UtcNow,
                    Description = "simulated crash"
                });
            }

            repro.MarkTarget("page-000");

            var summary = await new ScaleRunController(executors).RunAsync(
                manifest,
                new ScaleRunControllerOptions
                {
                    OutputRoot = root,
                    ExplicitMutationConfirmationDigest = manifest.ManifestDigest
                });
            Assert.AreEqual(1, summary.AcceptedCount);
            Assert.AreEqual(0, repro.ExecuteCount("page-000"));
            Assert.AreEqual(
                ScaleStageOutcome.OutcomeUnknownButConverged,
                summary.Pages.Single().Stages.Single(value => value.Stage == ScaleRunStage.Repro).Outcome);
            Assert.IsTrue(repro.ProbeCount("page-000") >= 1);
        }

        [TestMethod]
        public async Task TamperedArtifactFailsClosedOnResume()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            var executors = Executors();
            var controller = new ScaleRunController(executors);
            await controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root });
            var checkpoint = ScaleRunContractSerializer.Deserialize<ScaleStageCheckpoint>(File.ReadAllText(
                ScaleRunStorage.CheckpointPath(root, manifest.Pages[0], ScaleRunStage.Collect)));
            File.AppendAllText(
                ScaleRunStorage.ResolveArtifactPath(root, checkpoint.Artifacts.Single().RelativePath),
                "tampered");

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));
        }

        [TestMethod]
        public async Task ResealedCheckpointMetadataTamperFailsAgainstStageJournal()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            var executors = Executors();
            var controller = new ScaleRunController(executors);
            await controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root });
            var checkpoint = ScaleRunContractSerializer.Deserialize<ScaleStageCheckpoint>(File.ReadAllText(
                ScaleRunStorage.CheckpointPath(root, manifest.Pages[0], ScaleRunStage.Collect)));
            checkpoint.DiagnosticCode = "ResealedTamper";
            ScaleRunStorage.WriteCheckpointAtomic(root, manifest.Pages[0], checkpoint);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));
        }

        [TestMethod]
        public async Task CheckpointWithUnknownJsonPropertyFailsClosed()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            var controller = new ScaleRunController(Executors());
            await controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root });
            var path = ScaleRunStorage.CheckpointPath(root, manifest.Pages[0], ScaleRunStage.Collect);
            var raw = File.ReadAllText(path);
            File.WriteAllText(path, "{\"unknown\":true," + raw.TrimStart('{'));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));
        }

        [TestMethod]
        public void StageJournalRollsForwardAfterTruncatedTailAndRejectsMiddleTamper()
        {
            var root = TemporaryRoot();
            var path = ScaleRunStorage.StageJournalPath(root);
            var action = MigrationActionSignature.Create(
                "scale.test.collect",
                "Scale.Collect",
                MigrationDigest.ComputeSha256("source"),
                MigrationDigest.ComputeSha256("selection"),
                "scale-slot/v1/campaign/page/collect",
                MigrationDigest.ComputeSha256("semantic"));
            var firstOperation = Guid.NewGuid();
            using (var journal = new JsonLinesScaleStageExecutionJournal(path))
            {
                journal.Write(StartRecord(firstOperation, action));
                journal.Write(CompletedRecord(firstOperation, action));
            }
            File.AppendAllText(path, "{\"truncated\"");
            var secondOperation = Guid.NewGuid();
            using (var journal = new JsonLinesScaleStageExecutionJournal(path))
            {
                journal.Write(StartRecord(secondOperation, action));
            }
            using (var journal = new JsonLinesScaleStageExecutionJournal(path))
            {
                journal.Write(CompletedRecord(secondOperation, action));
            }
            var recovered = ScaleStageExecutionJournalReader.Read(path);
            Assert.IsTrue(recovered.HasInterruptedTail);
            Assert.AreEqual(1, recovered.InterruptedTails.Count);
            Assert.IsTrue(MigrationActionSignature.IsSha256(recovered.InterruptedTails[0].Sha256));
            Assert.AreEqual(4, recovered.Records.Count);
            Assert.IsFalse(File.Exists(MigrationExecutionJournalReader.SegmentPath(path, 2)));

            var corruptRoot = TemporaryRoot();
            var corruptPath = ScaleRunStorage.StageJournalPath(corruptRoot);
            using (var journal = new JsonLinesScaleStageExecutionJournal(corruptPath))
            {
                journal.Write(StartRecord(Guid.NewGuid(), action));
            }
            var canonical = File.ReadAllText(corruptPath);
            File.WriteAllText(corruptPath, canonical.Replace("scale.test.collect", "scale.test.changed"));
            Assert.ThrowsException<InvalidDataException>(() =>
                ScaleStageExecutionJournalReader.Read(corruptPath));
        }

        private string TemporaryRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "pnp-scale-tests", Guid.NewGuid().ToString("N"));
            temporaryRoots.Add(root);
            Directory.CreateDirectory(root);
            return root;
        }


        [TestMethod]
        public async Task ManifestCollectDecoupledFromTargetReferenceAndCohortSignatures()
        {
            var root = TemporaryRoot();
            var manifest = new ScaleRunManifest
            {
                LoopId = "loop-001",
                RunKey = "campaign-enterprise-wiki",
                MutationMode = ScaleRunMutationMode.Disabled,
                Policy = new ScaleRunPolicy
                {
                    QueueCapacity = 2,
                    MaximumAttemptsPerStage = 3,
                    RetryBaseDelayMilliseconds = 1,
                    MaximumUnverifiedTargets = 2
                },
                Pages = new List<ScaleRunPage>
                {
                    new ScaleRunPage
                    {
                        PageKey = "page-discovery-001",
                        Ordinal = 0,
                        PageFamily = "enterprise-wiki",
                        SourceReferenceKey = "source/page-discovery-001",
                        TargetReferenceKey = null,
                        SupportCohortSignature = null,
                        ExecutionCohortSignature = null,
                        LoadBucket = "normal"
                    }
                }
            };
            ScaleRunManifestValidator.Seal(manifest);
            Assert.IsTrue(MigrationActionSignature.IsSha256(manifest.ManifestDigest));

            var executors = Executors();
            var collectExecutor = executors.Single(value => value.Stage == ScaleRunStage.Collect);
            var action1 = ScaleRunIdentity.CreateAction(
                manifest,
                manifest.Pages[0],
                ScaleRunStage.Collect,
                collectExecutor,
                Array.Empty<ScaleStageArtifact>(),
                null);

            manifest.Pages[0].TargetReferenceKey = "target/reassigned-slot";
            manifest.Pages[0].SupportCohortSignature = MigrationDigest.ComputeSha256("support/v2");
            manifest.Pages[0].ExecutionCohortSignature = MigrationDigest.ComputeSha256("execution/v2");
            ScaleRunManifestValidator.Seal(manifest);

            var action2 = ScaleRunIdentity.CreateAction(
                manifest,
                manifest.Pages[0],
                ScaleRunStage.Collect,
                collectExecutor,
                Array.Empty<ScaleStageArtifact>(),
                null);

            Assert.AreEqual(action1.Signature, action2.Signature);
            Assert.AreEqual(action1.TargetIdentityDigest, action2.TargetIdentityDigest);
        }

        [TestMethod]
        public async Task CumulativeArtifactsFlowToPackageCompareAndBindSourcePackage()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            var executors = Executors();
            ScaleRunStageContext capturedCompareContext = null;
            executors.Single(e => e.Stage == ScaleRunStage.PackageCompare).OnExecute = ctx => capturedCompareContext = ctx;

            var summary = await new ScaleRunController(executors).RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });

            Assert.AreEqual(1, summary.AcceptedCount);
            Assert.IsNotNull(capturedCompareContext);

            var inputArtifacts = capturedCompareContext.InputArtifacts;
            Assert.IsTrue(inputArtifacts.Any(a => a.RelativePath.Contains("collect")), "Missing source collect artifact");
            Assert.IsTrue(inputArtifacts.Any(a => a.RelativePath.Contains("targetrecapture")), "Missing target recapture artifact");

            var expectedDigest = ScaleRunIdentity.ComputeArtifactSetDigest(inputArtifacts);
            Assert.AreEqual(expectedDigest, capturedCompareContext.Action.SourceEvidenceDigest);
        }

        [TestMethod]
        public async Task UnverifiedSemaphoreDoesNotLeakOnStageFault()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(5);
            manifest.Policy.MaximumUnverifiedTargets = 2;
            ScaleRunManifestValidator.Seal(manifest);

            var executors = Executors();
            executors.Single(e => e.Stage == ScaleRunStage.PackageCompare).ReturnInvalidArtifact = true;

            var controller = new ScaleRunController(executors);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));

            var validExecutors = Executors();
            var validSummary = await new ScaleRunController(validExecutors).RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });
            Assert.AreEqual(5, validSummary.AcceptedCount);
        }

        [TestMethod]
        public async Task PipelineCancellationPreservesRootCauseFaultWithoutMaskingByQueueClosure()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(10);
            manifest.Policy.QueueCapacity = 1;
            ScaleRunManifestValidator.Seal(manifest);

            var executors = Executors();
            executors.Single(e => e.Stage == ScaleRunStage.PackageCompare).ReturnInvalidArtifact = true;

            var controller = new ScaleRunController(executors);
            var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));

            StringAssert.Contains(exception.Message, "content-addressed reference");
        }

        [TestMethod]
        public async Task PlanDiscoversFullProfileAndPersistsForReproAndResume()
        {
            var root = TemporaryRoot();
            var manifest = new ScaleRunManifest
            {
                LoopId = "loop-discovery-001",
                RunKey = "campaign-discovery-flow",
                MutationMode = ScaleRunMutationMode.Simulation,
                Policy = new ScaleRunPolicy
                {
                    QueueCapacity = 2,
                    MaximumAttemptsPerStage = 3,
                    RetryBaseDelayMilliseconds = 1,
                    MaximumUnverifiedTargets = 2
                },
                Pages = new List<ScaleRunPage>
                {
                    new ScaleRunPage
                    {
                        PageKey = "page-discovery-002",
                        Ordinal = 0,
                        PageFamily = "generic-hint",
                        SourceReferenceKey = "source/page-discovery-002",
                        TargetReferenceKey = null,
                        SupportCohortSignature = null,
                        ExecutionCohortSignature = null,
                        LoadBucket = "normal"
                    }
                }
            };
            ScaleRunManifestValidator.Seal(manifest);

            var expectedSupport = MigrationDigest.ComputeSha256("discovered/support");
            var expectedExec = MigrationDigest.ComputeSha256("discovered/exec");
            var executors = Executors();

            // Plan executor discovers and sets DiscoveredProfile
            var planExecutor = executors.Single(e => e.Stage == ScaleRunStage.Plan);
            planExecutor.OnExecute = ctx =>
            {
                // In FakeStageExecutor, we can simulate Plan discovering profile on context
            };

            // To simulate Plan returning DiscoveredProfile, wrap planExecutor in CustomPlanExecutor
            var planWrapper = new DiscoveringPlanExecutor(planExecutor, ScalePageProfile.Seal(new ScalePageProfile
            {
                PageFamily = "enterprise-wiki",
                TargetReferenceKey = "target/resolved-plan-slot",
                SupportCohortSignature = expectedSupport,
                ExecutionCohortSignature = expectedExec,
                LoadBucket = "heavy"
            }));
            executors.Remove(planExecutor);

            // Also track Repro context to verify it receives the discovered profile
            ScaleRunStageContext reproContext = null;
            var reproExecutor = executors.Single(e => e.Stage == ScaleRunStage.Repro);
            reproExecutor.OnExecute = ctx => reproContext = ctx;

            var allExecutors = executors.Concat(new IScaleRunStageExecutor[] { planWrapper }).ToList();

            var summary = await new ScaleRunController(allExecutors).RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });

            Assert.AreEqual(1, summary.AcceptedCount);
            Assert.IsNotNull(reproContext);
            Assert.AreEqual("enterprise-wiki", reproContext.EffectiveProfile.PageFamily);
            Assert.AreEqual("target/resolved-plan-slot", reproContext.EffectiveProfile.TargetReferenceKey);
            Assert.AreEqual(expectedSupport, reproContext.EffectiveProfile.SupportCohortSignature);
            Assert.AreEqual(expectedExec, reproContext.EffectiveProfile.ExecutionCohortSignature);
            Assert.AreEqual("heavy", reproContext.EffectiveProfile.LoadBucket);

            // Crucial: verify that the Page object in the sealed manifest was NOT mutated
            Assert.AreEqual("generic-hint", reproContext.Page.PageFamily);
            Assert.IsNull(reproContext.Page.TargetReferenceKey);
            Assert.IsNull(reproContext.Page.SupportCohortSignature);
            Assert.IsNull(reproContext.Page.ExecutionCohortSignature);
            Assert.AreEqual("normal", reproContext.Page.LoadBucket);

            // Verify checkpoint on disk contains DiscoveredProfile
            var planCheckpointPath = ScaleRunStorage.CheckpointPath(root, manifest.Pages[0], ScaleRunStage.Plan);
            var planCheckpoint = ScaleRunContractSerializer.Deserialize<ScaleStageCheckpoint>(File.ReadAllText(planCheckpointPath));
            Assert.IsNotNull(planCheckpoint.DiscoveredProfile);
            Assert.AreEqual("target/resolved-plan-slot", planCheckpoint.DiscoveredProfile.TargetReferenceKey);
            Assert.AreEqual(expectedSupport, planCheckpoint.DiscoveredProfile.SupportCohortSignature);

            // Now verify Resume restores the DiscoveredProfile onto a fresh controller run
            ScaleRunStageContext resumedReproContext = null;
            reproExecutor.OnExecute = ctx => resumedReproContext = ctx;
            var freshManifest = new ScaleRunManifest
            {
                LoopId = "loop-discovery-001",
                RunKey = "campaign-discovery-flow",
                MutationMode = ScaleRunMutationMode.Simulation,
                Policy = manifest.Policy,
                Pages = new List<ScaleRunPage>
                {
                    new ScaleRunPage
                    {
                        PageKey = "page-discovery-002",
                        Ordinal = 0,
                        PageFamily = "generic-hint",
                        SourceReferenceKey = "source/page-discovery-002",
                        TargetReferenceKey = null,
                        SupportCohortSignature = null,
                        ExecutionCohortSignature = null,
                        LoadBucket = "normal"
                    }
                }
            };
            ScaleRunManifestValidator.Seal(freshManifest);
            var resumeSummary = await new ScaleRunController(allExecutors).RunAsync(
                freshManifest,
                new ScaleRunControllerOptions { OutputRoot = root, Resume = true });
            Assert.AreEqual(1, resumeSummary.AcceptedCount);
            Assert.AreEqual(5, resumeSummary.ResumeSkipCount);
        }

        private sealed class DiscoveringPlanExecutor : IScaleRunStageExecutor
        {
            private readonly IScaleRunStageExecutor inner;
            private readonly ScalePageProfile profile;

            public DiscoveringPlanExecutor(IScaleRunStageExecutor inner, ScalePageProfile profile)
            {
                this.inner = inner;
                this.profile = profile;
            }

            public ScaleRunStage Stage => inner.Stage;
            public string ContractDigest => inner.ContractDigest;
            public bool MutatesTarget => inner.MutatesTarget;
            public bool AllowsLiveMutation => inner.AllowsLiveMutation;
            public ScaleStageResumePolicy ResumePolicy => inner.ResumePolicy;

            public Task<ScaleStageProbeResult> ProbeAsync(ScaleRunStageContext context, CancellationToken cancellationToken)
                => inner.ProbeAsync(context, cancellationToken);

            public async Task<ScaleStageExecutionResult> ExecuteAsync(ScaleRunStageContext context, CancellationToken cancellationToken)
            {
                var result = await inner.ExecuteAsync(context, cancellationToken);
                result.DiscoveredProfile = profile;
                return result;
            }
        }

        [TestMethod]
        public async Task ManifestRemainsByteForByteImmutableDuringAndAfterRun()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(3);
            var beforeCanonical = ScaleRunContractSerializer.SerializeCanonical(manifest);
            var beforeDigest = manifest.ManifestDigest;

            var executors = Executors();
            var summary = await new ScaleRunController(executors).RunAsync(
                manifest,
                new ScaleRunControllerOptions { OutputRoot = root });

            var afterCanonical = ScaleRunContractSerializer.SerializeCanonical(manifest);
            var afterDigest = manifest.ManifestDigest;

            Assert.AreEqual(beforeCanonical, afterCanonical);
            Assert.AreEqual(beforeDigest, afterDigest);
            Assert.AreEqual(beforeDigest, ScaleRunManifestValidator.ComputeDigest(manifest));
        }


        [TestMethod]
        public async Task ArtifactPathConflictBetweenStagesFailsClosedInRouting()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            var executors = Executors();

            // Cause Collect and TargetRecapture to produce an artifact with the exact same RelativePath but differing hashes
            var collect = executors.Single(e => e.Stage == ScaleRunStage.Collect);
            var recapture = executors.Single(e => e.Stage == ScaleRunStage.TargetRecapture);

            var conflictingExecutors = executors.Select(e =>
            {
                if (e.Stage == ScaleRunStage.Collect)
                {
                    return (IScaleRunStageExecutor)new ConflictingArtifactExecutor(e, "shared-conflict.json", "content-A");
                }
                if (e.Stage == ScaleRunStage.TargetRecapture)
                {
                    return (IScaleRunStageExecutor)new ConflictingArtifactExecutor(e, "shared-conflict.json", "content-B");
                }
                return e;
            }).ToList();

            var controller = new ScaleRunController(conflictingExecutors);
            var ex = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));

            StringAssert.Contains(ex.Message, "differing content hashes");
        }

        [TestMethod]
        public async Task PlanDiscoveredProfileMismatchWithDeclaredManifestFailsClosed()
        {
            var root = TemporaryRoot();
            var manifest = Manifest(1);
            // Manifest declares an expected support cohort signature
            var declaredSupport = MigrationDigest.ComputeSha256("declared/support");
            manifest.Pages[0].SupportCohortSignature = declaredSupport;
            ScaleRunManifestValidator.Seal(manifest);

            var executors = Executors();
            var planExecutor = executors.Single(e => e.Stage == ScaleRunStage.Plan);
            // Plan discovers a contradictory support cohort signature
            var conflictingPlan = new DiscoveringPlanExecutor(planExecutor, ScalePageProfile.Seal(new ScalePageProfile
            {
                PageFamily = manifest.Pages[0].PageFamily,
                TargetReferenceKey = manifest.Pages[0].TargetReferenceKey,
                SupportCohortSignature = MigrationDigest.ComputeSha256("conflicting/support"),
                ExecutionCohortSignature = manifest.Pages[0].ExecutionCohortSignature,
                LoadBucket = manifest.Pages[0].LoadBucket
            }));
            executors.Remove(planExecutor);
            var allExecutors = executors.Cast<IScaleRunStageExecutor>().Concat(new[] { conflictingPlan }).ToList();

            var controller = new ScaleRunController(allExecutors);
            var ex = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                controller.RunAsync(manifest, new ScaleRunControllerOptions { OutputRoot = root }));

            StringAssert.Contains(ex.Message, "mismatch on support cohort signature");
        }

        private sealed class ConflictingArtifactExecutor : IScaleRunStageExecutor
        {
            private readonly IScaleRunStageExecutor inner;
            private readonly string relativePath;
            private readonly string content;

            public ConflictingArtifactExecutor(IScaleRunStageExecutor inner, string relativePath, string content)
            {
                this.inner = inner;
                this.relativePath = relativePath;
                this.content = content;
            }

            public ScaleRunStage Stage => inner.Stage;
            public string ContractDigest => inner.ContractDigest;
            public bool MutatesTarget => inner.MutatesTarget;
            public bool AllowsLiveMutation => inner.AllowsLiveMutation;
            public ScaleStageResumePolicy ResumePolicy => inner.ResumePolicy;

            public Task<ScaleStageProbeResult> ProbeAsync(ScaleRunStageContext context, CancellationToken cancellationToken)
                => inner.ProbeAsync(context, cancellationToken);

            public async Task<ScaleStageExecutionResult> ExecuteAsync(ScaleRunStageContext context, CancellationToken cancellationToken)
            {
                var result = await inner.ExecuteAsync(context, cancellationToken);
                var fullPath = Path.Combine(context.OutputRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, content);
                result.Artifacts.Add(new ScaleStageArtifact
                {
                    Kind = ScaleStageArtifactKind.Output,
                    RelativePath = relativePath,
                    Sha256 = ScaleRunStorage.ComputeFileSha256(fullPath),
                    Length = new FileInfo(fullPath).Length,
                    MediaType = "application/json",
                    SchemaVersion = "conflict-test/v1"
                });
                return result;
            }
        }
    }
}
