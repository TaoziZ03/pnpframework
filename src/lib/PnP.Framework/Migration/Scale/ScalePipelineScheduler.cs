using PnP.Framework.Migration.Execution;
using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Migration.Scale
{
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
                    var allExceptions = tasks
                        .Where(value => value.IsFaulted && value.Exception != null)
                        .SelectMany(value => value.Exception.Flatten().InnerExceptions)
                        .Where(value => !(value is OperationCanceledException))
                        .ToList();
                    var firstFault = allExceptions
                        .FirstOrDefault(value => !(value is InvalidOperationException ioe && ioe.Message.Contains("complete for adding")))
                        ?? allExceptions.FirstOrDefault();
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
                        try
                        {
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
                                    try
                                    {
                                        output.Add(item, cancellationToken);
                                    }
                                    catch (InvalidOperationException) when (output.IsAddingCompleted)
                                    {
                                        ReleaseUnverified(item, unverified);
                                        cancellationToken.ThrowIfCancellationRequested();
                                        return;
                                    }
                                }
                                else
                                {
                                    ScaleIngredientOutcomeRules.SetCompletedPageDisposition(item);
                                    ReleaseUnverified(item, unverified);
                                }
                            }
                            else
                            {
                                ScaleStageOutcomeRules.SetTerminalDisposition(item, summary.Outcome);
                                ReleaseUnverified(item, unverified);
                            }
                        }
                        catch
                        {
                            ReleaseUnverified(item, unverified);
                            throw;
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
}
