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
    internal sealed class ScaleStageRecoveryCoordinator
    {
        private readonly ScaleRunManifest manifest;
        private readonly ScaleRunControllerOptions options;
        private readonly MigrationExecutionJournalReadResult priorMutationJournal;
        private readonly ScaleStageExecutionJournalReadResult priorStageJournal;
        private readonly ScaleRunJournalRecorder recorder;
        private readonly IScaleRunClock clock;
        private readonly ScaleStageRecoveryReconciler reconciler;

        public ScaleStageRecoveryCoordinator(
            ScaleRunManifest manifest,
            ScaleRunControllerOptions options,
            MigrationExecutionJournalReadResult priorMutationJournal,
            ScaleStageExecutionJournalReadResult priorStageJournal,
            ScaleRunJournalRecorder recorder,
            IScaleRunClock clock)
        {
            this.manifest = manifest;
            this.options = options;
            this.priorMutationJournal = priorMutationJournal;
            this.priorStageJournal = priorStageJournal;
            this.recorder = recorder;
            this.clock = clock;
            this.reconciler = new ScaleStageRecoveryReconciler(manifest, options, priorMutationJournal, recorder, clock);
        }

        public async Task<ScaleStageRunSummary> TryResumeAsync(
            ScalePageWorkItem item,
            IScaleRunStageExecutor executor,
            MigrationActionSignature action,
            ScaleRunStageContext context,
            CancellationToken cancellationToken)
        {
            if (!options.Resume
                || executor.ResumePolicy == ScaleStageResumePolicy.AlwaysExecute
                || !ScaleRunStorage.TryReadValidatedCheckpoint(
                    options.OutputRoot,
                    item.Page,
                    executor.Stage,
                    action,
                    priorStageJournal,
                    out var checkpoint))
            {
                return null;
            }

            ScaleStageProbeResult probe = null;
            if (executor.ResumePolicy == ScaleStageResumePolicy.FreshProbe)
            {
                probe = await executor.ProbeAsync(context, cancellationToken).ConfigureAwait(false);
                ScaleStageResultValidator.ValidateProbe(
                    options.OutputRoot,
                    action,
                    probe,
                    probe.State != ScaleStageProbeState.Absent);
                if (!probe.FreshProbePerformed || probe.State == ScaleStageProbeState.Unavailable)
                {
                    return reconciler.RecordProbeTerminal(
                        item,
                        executor,
                        action,
                        probe,
                        ScaleStageOutcome.RetryableTransient);
                }
                if (probe.State == ScaleStageProbeState.Drifted
                    || probe.State == ScaleStageProbeState.Exact && !ExactProbeMatches(action, probe))
                {
                    return reconciler.RecordProbeTerminal(
                        item,
                        executor,
                        action,
                        probe,
                        ScaleStageOutcome.NeedsRca);
                }
                if (probe.State == ScaleStageProbeState.Absent)
                {
                    return null;
                }
            }

            var result = new ScaleStageExecutionResult
            {
                Outcome = ScaleStageOutcome.AlreadySatisfied,
                Verified = true,
                MutationAttempted = checkpoint.MutationAttempted,
                ProvenanceMatched = probe?.ProvenanceMatched ?? true,
                ObservedStateDigest = probe?.ObservedStateDigest ?? checkpoint.ObservedStateDigest,
                TargetIdentityDigest = probe?.TargetIdentityDigest ?? checkpoint.TargetIdentityDigest,
                DiagnosticCode = probe == null ? "ResumeCheckpointSatisfied" : "ResumeCheckpointFreshProbeSatisfied",
                Artifacts = ScaleStageArtifactRouting.MergeArtifactsWithoutConflict(
                    checkpoint.Artifacts.Concat(probe?.Artifacts ?? Array.Empty<ScaleStageArtifact>())),
                Requests = probe?.Requests?.ToList() ?? new List<ScaleRequestMetric>(),
                Ingredients = checkpoint.Ingredients.ToList(),
                DiscoveredProfile = checkpoint.DiscoveredProfile
            };
            var operationId = recorder.Start(
                item.Page,
                executor.Stage,
                0,
                action,
                reconciler.ShouldAuditMutation(executor),
                false,
                "Validate one durable scale stage checkpoint.");
            recorder.Complete(
                operationId,
                item.Page,
                executor.Stage,
                0,
                action,
                result,
                reconciler.ShouldAuditMutation(executor));
            ScaleStageRecoveryReconciler.ApplySuccess(
                item,
                executor.Stage,
                action,
                result,
                checkpoint.Artifacts);
            return ScaleStageSummaryFactory.Create(executor.Stage, 0, 0, result, resumeSkipped: true);
        }


        public ScaleStageRunSummary RecoverExactProbe(
            ScalePageWorkItem item,
            IScaleRunStageExecutor executor,
            MigrationActionSignature action,
            ScaleStageProbeResult probe,
            int attempt,
            Guid? unresolvedOperationId = null)
        {
            return reconciler.RecoverExactProbe(item, executor, action, probe, attempt, unresolvedOperationId);
        }

        public ScaleStageRunSummary RecordProbeTerminal(
            ScalePageWorkItem item,
            IScaleRunStageExecutor executor,
            MigrationActionSignature action,
            ScaleStageProbeResult probe,
            ScaleStageOutcome outcome)
        {
            return reconciler.RecordProbeTerminal(item, executor, action, probe, outcome);
        }

        public void PersistSuccess(
            ScalePageWorkItem item,
            ScaleRunStage stage,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            reconciler.PersistSuccess(item, stage, action, result);
        }

        public void ReconcileAbsentInterruptedIntent(MigrationActionSignature action)
        {
            reconciler.ReconcileAbsentInterruptedIntent(action);
        }

        public static bool ExactProbeMatches(
            MigrationActionSignature action,
            ScaleStageProbeResult probe)
        {
            return ScaleStageRecoveryReconciler.ExactProbeMatches(action, probe);
        }
    }
}
