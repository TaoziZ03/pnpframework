using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Migration.Scale
{
    internal sealed class ScaleStageRunner
    {
        private readonly ScaleRunManifest manifest;
        private readonly ScaleRunControllerOptions options;
        private readonly string runAttemptId;
        private readonly IReadOnlyDictionary<ScaleRunStage, IScaleRunStageExecutor> executors;
        private readonly ScaleRunJournalRecorder recorder;
        private readonly ScaleRunTelemetry telemetry;
        private readonly IScaleRunClock clock;
        private readonly ScaleStageRecoveryCoordinator recovery;
        private readonly ScaleStageRetryRunner retryRunner;

        public ScaleStageRunner(
            ScaleRunManifest manifest,
            ScaleRunControllerOptions options,
            string runAttemptId,
            IReadOnlyDictionary<ScaleRunStage, IScaleRunStageExecutor> executors,
            MigrationExecutionJournalReadResult priorMutationJournal,
            ScaleStageExecutionJournalReadResult priorStageJournal,
            ScaleRunJournalRecorder recorder,
            ScaleRunTelemetry telemetry,
            IScaleRunClock clock)
        {
            this.manifest = manifest;
            this.options = options;
            this.runAttemptId = runAttemptId;
            this.executors = executors;
            this.recorder = recorder;
            this.telemetry = telemetry;
            this.clock = clock;
            this.recovery = new ScaleStageRecoveryCoordinator(
                manifest,
                options,
                priorMutationJournal,
                priorStageJournal,
                recorder,
                clock);
            this.retryRunner = new ScaleStageRetryRunner(manifest, options, recorder, recovery, telemetry, clock);
        }

        public async Task<ScaleStageRunSummary> ExecuteAsync(
            ScalePageWorkItem item,
            ScaleRunStage stage,
            CancellationToken cancellationToken)
        {
            var executor = executors[stage];
            var profile = item.EffectiveProfile ?? new ScalePageProfile
            {
                PageFamily = item.Page.PageFamily,
                TargetReferenceKey = item.Page.TargetReferenceKey,
                SupportCohortSignature = item.Page.SupportCohortSignature,
                ExecutionCohortSignature = item.Page.ExecutionCohortSignature,
                LoadBucket = item.Page.LoadBucket
            };
            if (stage >= ScaleRunStage.Repro && string.IsNullOrWhiteSpace(profile.TargetReferenceKey))
            {
                throw new InvalidOperationException("TargetReferenceKey is required for mutation and verification stages.");
            }
            if (executor.MutatesTarget && (string.IsNullOrWhiteSpace(profile.SupportCohortSignature) || string.IsNullOrWhiteSpace(profile.ExecutionCohortSignature)))
            {
                throw new InvalidOperationException("Support and execution cohort signatures are required for mutating and verification stages.");
            }
            var inputArtifacts = ScaleStageArtifactRouting.GetInputArtifactsForStage(item, stage);
            var action = ScaleRunIdentity.CreateAction(
                manifest,
                item.Page,
                profile,
                stage,
                executor,
                inputArtifacts,
                item.DependencySignature);
            var stageRoot = ScaleRunStorage.StageRoot(options.OutputRoot, item.Page, stage);
            Directory.CreateDirectory(stageRoot);
            var baseContext = new ScaleRunStageContext
            {
                LoopId = manifest.LoopId,
                RunKey = manifest.RunKey,
                ManifestDigest = manifest.ManifestDigest,
                RunAttemptId = runAttemptId,
                MutationMode = manifest.MutationMode,
                Page = item.Page,
                EffectiveProfile = profile,
                Stage = stage,
                Attempt = 0,
                OutputRoot = Path.GetFullPath(options.OutputRoot),
                StageStorageRoot = stageRoot,
                StageOutputRoot = Path.Combine(stageRoot, "probes", runAttemptId),
                Action = action,
                InputArtifacts = inputArtifacts.ToList()
            };

            var resumed = await recovery.TryResumeAsync(
                item,
                executor,
                action,
                baseContext,
                cancellationToken).ConfigureAwait(false);
            if (resumed != null)
            {
                return resumed;
            }

            if (executor.MutatesTarget)
            {
                if (manifest.MutationMode == ScaleRunMutationMode.Disabled)
                {
                    return ScaleStageSummaryFactory.Terminal(
                        stage,
                        ScaleStageOutcome.NeedsPolicyDecision,
                        "MutationDisabled");
                }
                var probe = await executor.ProbeAsync(baseContext, cancellationToken).ConfigureAwait(false);
                ScaleStageResultValidator.ValidateProbe(
                    options.OutputRoot,
                    action,
                    probe,
                    probe.State != ScaleStageProbeState.Absent);
                if (!probe.FreshProbePerformed || probe.State == ScaleStageProbeState.Unavailable)
                {
                    return recovery.RecordProbeTerminal(
                        item,
                        executor,
                        action,
                        probe,
                        ScaleStageOutcome.RetryableTransient);
                }
                if (probe.State == ScaleStageProbeState.Drifted
                    || probe.State == ScaleStageProbeState.Exact
                        && !ScaleStageRecoveryCoordinator.ExactProbeMatches(action, probe))
                {
                    return recovery.RecordProbeTerminal(
                        item,
                        executor,
                        action,
                        probe,
                        ScaleStageOutcome.NeedsRca);
                }
                if (probe.State == ScaleStageProbeState.Exact)
                {
                    return recovery.RecoverExactProbe(item, executor, action, probe, 0);
                }
                recovery.ReconcileAbsentInterruptedIntent(action);
            }

            return await retryRunner.ExecuteWithRetriesAsync(
                item,
                executor,
                action,
                baseContext,
                cancellationToken).ConfigureAwait(false);
        }

    }
}
