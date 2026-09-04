using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Migration.Scale
{
    internal sealed class ScalePageWorkItem
    {
        public ScalePageWorkItem(ScaleRunPage page)
        {
            Page = page ?? throw new ArgumentNullException(nameof(page));
        }

        public ScaleRunPage Page { get; }

        public ScalePageDisposition Disposition { get; set; } = ScalePageDisposition.Pending;

        public string NextAction { get; set; } = "Continue";

        public IList<ScaleStageRunSummary> Stages { get; } = new List<ScaleStageRunSummary>();

        public IList<ScaleStageArtifact> InputArtifacts { get; set; } = new List<ScaleStageArtifact>();

        public string DependencySignature { get; set; }

        public bool UnverifiedSlotHeld { get; set; }

        public double PendingBackpressureWaitMilliseconds { get; set; }
    }

    internal sealed class ScalePipelineScheduler
    {
        private readonly ScaleRunManifest manifest;
        private readonly ScaleRunTelemetry telemetry;
        private readonly Func<ScalePageWorkItem, ScaleRunStage, CancellationToken, Task<ScaleStageRunSummary>> executeStage;

        public ScalePipelineScheduler(
            ScaleRunManifest manifest,
            ScaleRunTelemetry telemetry,
            Func<ScalePageWorkItem, ScaleRunStage, CancellationToken, Task<ScaleStageRunSummary>> executeStage)
        {
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            this.executeStage = executeStage ?? throw new ArgumentNullException(nameof(executeStage));
        }

        public async Task RunAsync(
            IReadOnlyList<ScalePageWorkItem> workItems,
            CancellationToken cancellationToken)
        {
            var concurrency = manifest.Policy.StageConcurrency.ToDictionary(
                value => value.Stage,
                value => value.Maximum);
            var queues = ScaleRunManifestValidator.Stages.ToDictionary(
                value => value,
                value => new BlockingCollection<ScalePageWorkItem>(manifest.Policy.QueueCapacity));
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var unverified = new SemaphoreSlim(
                manifest.Policy.MaximumUnverifiedTargets,
                manifest.Policy.MaximumUnverifiedTargets))
            {
                var tasks = new List<Task>();
                for (var index = 0; index < ScaleRunManifestValidator.Stages.Count; index++)
                {
                    var stage = ScaleRunManifestValidator.Stages[index];
                    var next = index + 1 < ScaleRunManifestValidator.Stages.Count
                        ? queues[ScaleRunManifestValidator.Stages[index + 1]]
                        : null;
                    tasks.Add(RunWorkersAsync(
                        stage,
                        queues[stage],
                        next,
                        concurrency[stage],
                        unverified,
                        linked.Token));
                }
                foreach (var task in tasks)
                {
                    _ = task.ContinueWith(
                        _ =>
                        {
                            linked.Cancel();
                            CompleteAll(queues.Values);
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }

                try
                {
                    foreach (var item in workItems)
                    {
                        queues[ScaleRunStage.Collect].Add(item, linked.Token);
                    }
                    TryComplete(queues[ScaleRunStage.Collect]);
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch
                {
                    linked.Cancel();
                    CompleteAll(queues.Values);
                    try
                    {
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Inspect all workers below so cancellation does not hide
                        // the storage/journal/validation fault that triggered it.
                    }
                    var firstFault = tasks
                        .Where(value => value.IsFaulted && value.Exception != null)
                        .SelectMany(value => value.Exception.Flatten().InnerExceptions)
                        .FirstOrDefault(value => !(value is OperationCanceledException));
                    if (firstFault != null)
                    {
                        ExceptionDispatchInfo.Capture(firstFault).Throw();
                    }
                    throw;
                }
                finally
                {
                    CompleteAll(queues.Values);
                    foreach (var queue in queues.Values)
                    {
                        queue.Dispose();
                    }
                }
            }
        }

        private async Task RunWorkersAsync(
            ScaleRunStage stage,
            BlockingCollection<ScalePageWorkItem> input,
            BlockingCollection<ScalePageWorkItem> output,
            int workerCount,
            SemaphoreSlim unverified,
            CancellationToken cancellationToken)
        {
            var workers = Enumerable.Range(0, workerCount)
                .Select(_ => Task.Run(async () =>
                {
                    foreach (var item in input.GetConsumingEnumerable(cancellationToken))
                    {
                        if (item.Disposition != ScalePageDisposition.Pending)
                        {
                            ReleaseUnverified(item, unverified);
                            continue;
                        }
                        if (stage == ScaleRunStage.Repro && !item.UnverifiedSlotHeld)
                        {
                            var wait = Stopwatch.StartNew();
                            await unverified.WaitAsync(cancellationToken).ConfigureAwait(false);
                            wait.Stop();
                            item.UnverifiedSlotHeld = true;
                            telemetry.EnterUnverified();
                            item.PendingBackpressureWaitMilliseconds = wait.Elapsed.TotalMilliseconds;
                        }

                        var summary = await executeStage(item, stage, cancellationToken).ConfigureAwait(false);
                        summary.BackpressureWaitMilliseconds += item.PendingBackpressureWaitMilliseconds;
                        item.PendingBackpressureWaitMilliseconds = 0;
                        item.Stages.Add(summary);
                        if (ScaleStageOutcomeRules.IsSuccessful(summary.Outcome))
                        {
                            if (output != null)
                            {
                                output.Add(item, cancellationToken);
                            }
                            else
                            {
                                item.Disposition = ScalePageDisposition.Accepted;
                                item.NextAction = "AdvanceNextWave";
                                ReleaseUnverified(item, unverified);
                            }
                        }
                        else
                        {
                            ScaleStageOutcomeRules.SetTerminalDisposition(item, summary.Outcome);
                            ReleaseUnverified(item, unverified);
                        }
                    }
                }))
                .ToArray();
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            finally
            {
                TryComplete(output);
            }
        }

        private void ReleaseUnverified(ScalePageWorkItem item, SemaphoreSlim unverified)
        {
            if (!item.UnverifiedSlotHeld)
            {
                return;
            }
            item.UnverifiedSlotHeld = false;
            telemetry.LeaveUnverified();
            unverified.Release();
        }

        private static void CompleteAll(IEnumerable<BlockingCollection<ScalePageWorkItem>> queues)
        {
            foreach (var queue in queues)
            {
                TryComplete(queue);
            }
        }

        private static void TryComplete(BlockingCollection<ScalePageWorkItem> queue)
        {
            if (queue == null || queue.IsAddingCompleted)
            {
                return;
            }
            try
            {
                queue.CompleteAdding();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    internal static class ScaleStageOutcomeRules
    {
        public static bool IsSuccessful(ScaleStageOutcome outcome)
        {
            return outcome == ScaleStageOutcome.Succeeded
                || outcome == ScaleStageOutcome.AlreadySatisfied
                || outcome == ScaleStageOutcome.OutcomeUnknownButConverged;
        }

        public static void SetTerminalDisposition(ScalePageWorkItem item, ScaleStageOutcome outcome)
        {
            switch (outcome)
            {
                case ScaleStageOutcome.RetryableTransient:
                    item.Disposition = ScalePageDisposition.Retryable;
                    item.NextAction = "RetryWithBackoff";
                    break;
                case ScaleStageOutcome.AuthorizationBlocked:
                    item.Disposition = ScalePageDisposition.AuthorizationBlocked;
                    item.NextAction = "AwaitAuthorizationChange";
                    break;
                case ScaleStageOutcome.NeedsPolicyDecision:
                    item.Disposition = ScalePageDisposition.NeedsPolicyDecision;
                    item.NextAction = "AwaitPolicyDecision";
                    break;
                case ScaleStageOutcome.QuarantinedUnexpectedDifference:
                    item.Disposition = ScalePageDisposition.Quarantined;
                    item.NextAction = "QuarantineAndRca";
                    break;
                case ScaleStageOutcome.NeedsRca:
                    item.Disposition = ScalePageDisposition.NeedsRca;
                    item.NextAction = "CollectEvidenceAndRca";
                    break;
                default:
                    item.Disposition = ScalePageDisposition.FailedUnexpectedly;
                    item.NextAction = "CollectEvidenceAndRca";
                    break;
            }
        }
    }
}
