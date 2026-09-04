using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Scale;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    }
}
