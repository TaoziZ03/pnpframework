using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            recovery = new ScaleStageRecoveryCoordinator(
                manifest,
                options,
                priorMutationJournal,
                priorStageJournal,
                recorder,
                clock);
        }

        public async Task<ScaleStageRunSummary> ExecuteAsync(
            ScalePageWorkItem item,
            ScaleRunStage stage,
            CancellationToken cancellationToken)
        {
            var executor = executors[stage];
            var action = ScaleRunIdentity.CreateAction(
                manifest,
                item.Page,
                stage,
                executor,
                item.InputArtifacts,
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
                Stage = stage,
                Attempt = 0,
                OutputRoot = Path.GetFullPath(options.OutputRoot),
                StageStorageRoot = stageRoot,
                StageOutputRoot = Path.Combine(stageRoot, "probes", runAttemptId),
                Action = action,
                InputArtifacts = item.InputArtifacts.ToList()
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

            return await ExecuteWithRetriesAsync(
                item,
                executor,
                action,
                baseContext,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<ScaleStageRunSummary> ExecuteWithRetriesAsync(
            ScalePageWorkItem item,
            IScaleRunStageExecutor executor,
            MigrationActionSignature action,
            ScaleRunStageContext baseContext,
            CancellationToken cancellationToken)
        {
            ScaleStageRunSummary last = null;
            var retryCount = 0;
            var retryWaitMilliseconds = 0d;
            var totalDurationMilliseconds = 0d;
            var accumulatedRequests = new List<ScaleRequestMetric>();
            var accumulatedUnexpectedDifferences = 0;
            var accumulatedSharedReceiptReuse = 0;
            for (var attempt = 1; attempt <= manifest.Policy.MaximumAttemptsPerStage; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = CloneContext(baseContext, attempt);
                var stopwatch = Stopwatch.StartNew();
                telemetry.EnterStage(executor.Stage);
                var operationId = recorder.Start(
                    item.Page,
                    executor.Stage,
                    attempt,
                    action,
                    ShouldAuditMutation(executor),
                    ShouldAuditMutation(executor),
                    "Execute one sealed scale stage action.");
                ScaleStageExecutionResult result;
                try
                {
                    try
                    {
                        result = await executor.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        result = ScaleStageResultValidator.CreateSanitizedFailure(
                            options.OutputRoot,
                            context.StageOutputRoot,
                            executor.Stage,
                            attempt,
                            action,
                            exception,
                            clock.UtcNow);
                    }
                    ScaleStageResultValidator.ValidateExecutionResult(options.OutputRoot, executor, action, result);
                    var deferMutationReceipt = ShouldAuditMutation(executor)
                        && result.MutationAttempted
                        && result.Outcome == ScaleStageOutcome.RetryableTransient;
                    if (deferMutationReceipt)
                    {
                        recorder.CompleteStage(operationId, item.Page, executor.Stage, attempt, action, result);
                    }
                    else
                    {
                        recorder.Complete(
                            operationId,
                            item.Page,
                            executor.Stage,
                            attempt,
                            action,
                            result,
                            ShouldAuditMutation(executor));
                    }
                    if (ScaleStageOutcomeRules.IsSuccessful(result.Outcome))
                    {
                        recovery.PersistSuccess(item, executor.Stage, action, result);
                    }
                }
                finally
                {
                    telemetry.LeaveStage(executor.Stage);
                    stopwatch.Stop();
                }

                totalDurationMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
                accumulatedRequests.AddRange(result.Requests);
                accumulatedUnexpectedDifferences += result.UnexpectedDifferenceCount;
                accumulatedSharedReceiptReuse += result.SharedReceiptReuseCount;
                last = ScaleStageSummaryFactory.Create(
                    executor.Stage,
                    attempt,
                    totalDurationMilliseconds,
                    result,
                    resumeSkipped: false);
                last.RetryCount = retryCount;
                last.RetryAfterWaitMilliseconds += retryWaitMilliseconds;
                last.RequestCount = accumulatedRequests.Count;
                last.RequestDurationsMilliseconds = accumulatedRequests.Select(value => value.DurationMilliseconds).ToList();
                last.Http429Count = accumulatedRequests.Count(value => value.HttpStatusCode == 429);
                last.Http503Count = accumulatedRequests.Count(value => value.HttpStatusCode == 503);
                last.RetryAfterWaitMilliseconds += accumulatedRequests.Sum(value => value.RetryAfterWaitMilliseconds)
                    - result.Requests.Sum(value => value.RetryAfterWaitMilliseconds);
                last.UnexpectedDifferenceCount = accumulatedUnexpectedDifferences;
                last.SharedReceiptReuseCount = accumulatedSharedReceiptReuse;
                if (result.Outcome == ScaleStageOutcome.RetryableTransient
                    && executor.MutatesTarget
                    && result.MutationAttempted)
                {
                    var probe = await executor.ProbeAsync(baseContext, cancellationToken).ConfigureAwait(false);
                    ScaleStageResultValidator.ValidateProbe(
                        options.OutputRoot,
                        action,
                        probe,
                        probe.State != ScaleStageProbeState.Absent);
                    if (probe.State == ScaleStageProbeState.Exact
                        && ScaleStageRecoveryCoordinator.ExactProbeMatches(action, probe))
                    {
                        var recovered = recovery.RecoverExactProbe(
                            item,
                            executor,
                            action,
                            probe,
                            attempt,
                            operationId);
                        recovered.DurationMilliseconds += totalDurationMilliseconds;
                        recovered.RequestCount += accumulatedRequests.Count;
                        recovered.RequestDurationsMilliseconds = accumulatedRequests
                            .Select(value => value.DurationMilliseconds)
                            .Concat(recovered.RequestDurationsMilliseconds)
                            .ToList();
                        recovered.RetryCount = retryCount;
                        return recovered;
                    }
                    if (probe.State == ScaleStageProbeState.Drifted
                        || probe.State == ScaleStageProbeState.Exact)
                    {
                        return recovery.RecordProbeTerminal(
                            item,
                            executor,
                            action,
                            probe,
                            ScaleStageOutcome.NeedsRca);
                    }
                    if (!probe.FreshProbePerformed || probe.State == ScaleStageProbeState.Unavailable)
                    {
                        return recovery.RecordProbeTerminal(
                            item,
                            executor,
                            action,
                            probe,
                            ScaleStageOutcome.RetryableTransient);
                    }
                    recorder.CompleteMutation(operationId, action, result);
                }

                if (result.Outcome != ScaleStageOutcome.RetryableTransient
                    || attempt >= manifest.Policy.MaximumAttemptsPerStage)
                {
                    return last;
                }

                retryCount++;
                var retryDelay = result.RetryAfter
                    ?? TimeSpan.FromMilliseconds(manifest.Policy.RetryBaseDelayMilliseconds * attempt);
                retryWaitMilliseconds += retryDelay.TotalMilliseconds;
                await clock.DelayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            return last ?? ScaleStageSummaryFactory.Terminal(
                executor.Stage,
                ScaleStageOutcome.FailedUnexpectedly,
                "NoStageAttempt");
        }

        private static ScaleRunStageContext CloneContext(ScaleRunStageContext source, int attempt)
        {
            return new ScaleRunStageContext
            {
                LoopId = source.LoopId,
                RunKey = source.RunKey,
                ManifestDigest = source.ManifestDigest,
                RunAttemptId = source.RunAttemptId,
                MutationMode = source.MutationMode,
                Page = source.Page,
                Stage = source.Stage,
                Attempt = attempt,
                OutputRoot = source.OutputRoot,
                StageStorageRoot = source.StageStorageRoot,
                StageOutputRoot = Path.Combine(
                    source.StageStorageRoot,
                    "attempts",
                    source.RunAttemptId,
                    attempt.ToString("D2")),
                Action = source.Action,
                InputArtifacts = source.InputArtifacts.ToList()
            };
        }

        private bool ShouldAuditMutation(IScaleRunStageExecutor executor)
        {
            return manifest.MutationMode == ScaleRunMutationMode.ExplicitApproved
                && executor.MutatesTarget
                && executor.AllowsLiveMutation;
        }

    }
}
