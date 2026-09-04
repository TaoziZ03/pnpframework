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
                    return RecordProbeTerminal(
                        item,
                        executor,
                        action,
                        probe,
                        ScaleStageOutcome.RetryableTransient);
                }
                if (probe.State == ScaleStageProbeState.Drifted
                    || probe.State == ScaleStageProbeState.Exact && !ExactProbeMatches(action, probe))
                {
                    return RecordProbeTerminal(
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
                ProvenanceMatched = true,
                ObservedStateDigest = checkpoint.ObservedStateDigest,
                TargetIdentityDigest = checkpoint.TargetIdentityDigest,
                DiagnosticCode = "ResumeCheckpointSatisfied",
                Artifacts = checkpoint.Artifacts.ToList(),
                Requests = probe?.Requests?.ToList() ?? new List<ScaleRequestMetric>()
            };
            var operationId = recorder.Start(
                item.Page,
                executor.Stage,
                0,
                action,
                ShouldAuditMutation(executor),
                false,
                "Validate one durable scale stage checkpoint.");
            recorder.Complete(
                operationId,
                item.Page,
                executor.Stage,
                0,
                action,
                result,
                ShouldAuditMutation(executor));
            ApplySuccess(item, action, result);
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
            var unresolved = unresolvedOperationId.HasValue
                ? null
                : FindUnresolvedMutationIntent(action);
            var mutationAudit = ShouldAuditMutation(executor);
            var outcomeUnknown = mutationAudit
                && (unresolvedOperationId.HasValue || unresolved != null);
            var result = new ScaleStageExecutionResult
            {
                Outcome = outcomeUnknown
                    ? ScaleStageOutcome.OutcomeUnknownButConverged
                    : ScaleStageOutcome.AlreadySatisfied,
                Verified = true,
                MutationAttempted = outcomeUnknown,
                ProvenanceMatched = true,
                ObservedStateDigest = probe.ObservedStateDigest,
                TargetIdentityDigest = probe.TargetIdentityDigest,
                DiagnosticCode = outcomeUnknown
                    ? "OutcomeUnknownButFreshProbeConverged"
                    : "FreshProbeAlreadySatisfied",
                Artifacts = probe.Artifacts.ToList(),
                Requests = probe.Requests.ToList()
            };
            ScaleStageResultValidator.ValidateExecutionResult(options.OutputRoot, executor, action, result);
            if (mutationAudit && unresolvedOperationId.HasValue)
            {
                recorder.CompleteMutation(unresolvedOperationId.Value, action, result);
                RecordStageOnly(item, executor, action, result, attempt,
                    "Record fresh convergence after an outcome-unknown mutation attempt.");
            }
            else if (mutationAudit
                && unresolved != null
                && string.Equals(unresolved.PlanDigest, manifest.ManifestDigest, StringComparison.OrdinalIgnoreCase))
            {
                recorder.CompleteMutation(unresolved.OperationId, action, result);
                RecordStageOnly(item, executor, action, result, attempt,
                    "Record fresh convergence of a prior interrupted mutation attempt.");
            }
            else
            {
                var operationId = recorder.Start(
                    item.Page,
                    executor.Stage,
                    attempt,
                    action,
                    mutationAudit: mutationAudit && !outcomeUnknown,
                    writeMutationIntent: false,
                    description: "Record fresh target convergence without mutation replay.");
                recorder.Complete(
                    operationId,
                    item.Page,
                    executor.Stage,
                    attempt,
                    action,
                    result,
                    mutationAudit: mutationAudit && !outcomeUnknown);
            }
            PersistSuccess(item, executor.Stage, action, result);
            return ScaleStageSummaryFactory.Create(executor.Stage, attempt, 0, result, resumeSkipped: true);
        }

        public ScaleStageRunSummary RecordProbeTerminal(
            ScalePageWorkItem item,
            IScaleRunStageExecutor executor,
            MigrationActionSignature action,
            ScaleStageProbeResult probe,
            ScaleStageOutcome outcome)
        {
            var result = new ScaleStageExecutionResult
            {
                Outcome = outcome,
                ProvenanceMatched = probe.ProvenanceMatched,
                ObservedStateDigest = probe.ObservedStateDigest,
                TargetIdentityDigest = probe.TargetIdentityDigest,
                DiagnosticCode = probe.DiagnosticCode,
                Artifacts = probe.Artifacts.ToList(),
                Requests = probe.Requests.ToList()
            };
            ScaleStageResultValidator.ValidateExecutionResult(
                options.OutputRoot,
                executor,
                action,
                result);
            RecordStageOnly(
                item,
                executor,
                action,
                result,
                0,
                "Record a terminal fresh-probe result with retained evidence.");
            return ScaleStageSummaryFactory.Create(
                executor.Stage,
                0,
                0,
                result,
                resumeSkipped: false);
        }

        public void PersistSuccess(
            ScalePageWorkItem item,
            ScaleRunStage stage,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            var checkpoint = new ScaleStageCheckpoint
            {
                PageKey = item.Page.PageKey,
                Stage = stage,
                ActionSignature = action.Signature,
                ArtifactSetDigest = ScaleRunStorage.ComputeArtifactReferenceSetDigest(result.Artifacts),
                Outcome = result.Outcome,
                Verified = result.Verified,
                MutationAttempted = result.MutationAttempted,
                ObservedStateDigest = result.ObservedStateDigest,
                TargetIdentityDigest = result.TargetIdentityDigest,
                DiagnosticCode = result.DiagnosticCode,
                CompletedAtUtc = clock.UtcNow,
                Artifacts = result.Artifacts.ToList(),
                Requests = result.Requests.ToList()
            };
            ScaleRunStorage.WriteCheckpointAtomic(options.OutputRoot, item.Page, checkpoint);
            ApplySuccess(item, action, result);
        }

        public void ReconcileAbsentInterruptedIntent(MigrationActionSignature action)
        {
            var unresolved = FindUnresolvedMutationIntent(action);
            if (unresolved == null
                || !string.Equals(unresolved.PlanDigest, manifest.ManifestDigest, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            recorder.CompleteMutation(unresolved.OperationId, action, new ScaleStageExecutionResult
            {
                Outcome = ScaleStageOutcome.FailedUnexpectedly,
                DiagnosticCode = "FreshProbeProvedTargetAbsent",
                Artifacts = new List<ScaleStageArtifact>(),
                Requests = new List<ScaleRequestMetric>()
            });
        }

        public static bool ExactProbeMatches(
            MigrationActionSignature action,
            ScaleStageProbeResult probe)
        {
            return probe.ProvenanceMatched
                && string.Equals(probe.ObservedStateDigest, action.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(probe.TargetIdentityDigest, action.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase);
        }

        private void RecordStageOnly(
            ScalePageWorkItem item,
            IScaleRunStageExecutor executor,
            MigrationActionSignature action,
            ScaleStageExecutionResult result,
            int attempt,
            string description)
        {
            var stageOperationId = recorder.Start(
                item.Page,
                executor.Stage,
                attempt,
                action,
                mutationAudit: false,
                writeMutationIntent: false,
                description: description);
            recorder.CompleteStage(stageOperationId, item.Page, executor.Stage, attempt, action, result);
        }

        private MigrationExecutionJournalRecord FindUnresolvedMutationIntent(
            MigrationActionSignature action)
        {
            var receipts = new HashSet<string>(priorMutationJournal.Records
                .Where(value => value.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt)
                .Select(value => value.OperationId.ToString("N") + "/" + value.MutationReceipt.Sequence),
                StringComparer.Ordinal);
            return priorMutationJournal.Records.LastOrDefault(value =>
                value.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent
                && string.Equals(value.ActionId, action.ActionId, StringComparison.Ordinal)
                && string.Equals(value.ActionSignature, action.Signature, StringComparison.OrdinalIgnoreCase)
                && !receipts.Contains(value.OperationId.ToString("N") + "/" + value.MutationIntent.Sequence));
        }

        private static void ApplySuccess(
            ScalePageWorkItem item,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            item.InputArtifacts = result.Artifacts.ToList();
            item.DependencySignature = action.Signature;
        }

        private bool ShouldAuditMutation(IScaleRunStageExecutor executor)
        {
            return manifest.MutationMode == ScaleRunMutationMode.ExplicitApproved
                && executor.MutatesTarget
                && executor.AllowsLiveMutation;
        }
    }
}
