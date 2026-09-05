using PnP.Framework.Migration.Execution.Journaling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Migration.Scale
{
    /// <summary>
    /// Public facade for one bounded, durable page-migration loop attempt.
    /// SharePoint access, browser access, credentials, and URLs remain owned by
    /// host-provided stage executors.
    /// </summary>
    public sealed class ScaleRunController
    {
        private readonly IReadOnlyDictionary<ScaleRunStage, IScaleRunStageExecutor> executors;
        private readonly IScaleRunClock clock;

        public ScaleRunController(
            IEnumerable<IScaleRunStageExecutor> executors,
            IScaleRunClock clock = null)
        {
            var values = (executors ?? Enumerable.Empty<IScaleRunStageExecutor>()).ToArray();
            if (values.Any(value => value == null)
                || values.Select(value => value.Stage).Distinct().Count() != values.Length)
            {
                throw new ArgumentException(
                    "Scale stage executors must be non-null and unique by stage.",
                    nameof(executors));
            }
            this.executors = values.ToDictionary(value => value.Stage);
            this.clock = clock ?? new SystemScaleRunClock();
        }

        public async Task<ScaleRunSummary> RunAsync(
            ScaleRunManifest manifest,
            ScaleRunControllerOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ScaleRunManifestValidator.Validate(manifest);
            ScaleRunAdmission.Validate(manifest, options, executors);

            var outputRoot = Path.GetFullPath(options.OutputRoot);
            Directory.CreateDirectory(outputRoot);
            var priorMutationJournal = MigrationExecutionJournalReader.Read(
                ScaleRunStorage.JournalPath(outputRoot));
            var priorStageJournal = ScaleStageExecutionJournalReader.Read(
                ScaleRunStorage.StageJournalPath(outputRoot));
            var runAttemptId = clock.UtcNow.ToString("yyyyMMddTHHmmssfffZ")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var startedAt = clock.UtcNow;
            var telemetry = new ScaleRunTelemetry();
            var workItems = manifest.Pages.Select(value => new ScalePageWorkItem(value)).ToArray();

            Exception pipelineFailure = null;
            try
            {
                using (var mutationJournal = new JsonLinesMigrationExecutionJournal(
                    ScaleRunStorage.JournalPath(outputRoot)))
                using (var stageJournal = new JsonLinesScaleStageExecutionJournal(
                    ScaleRunStorage.StageJournalPath(outputRoot)))
                {
                    var recorder = new ScaleRunJournalRecorder(
                        manifest,
                        mutationJournal,
                        stageJournal,
                        clock);
                    var runner = new ScaleStageRunner(
                        manifest,
                        options,
                        runAttemptId,
                        executors,
                        priorMutationJournal,
                        priorStageJournal,
                        recorder,
                        telemetry,
                        clock);
                    var scheduler = new ScalePipelineScheduler(
                        manifest,
                        telemetry,
                        runner.ExecuteAsync);
                    await scheduler.RunAsync(workItems, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                pipelineFailure = exception;
                foreach (var item in workItems.Where(value =>
                    value.Disposition == ScalePageDisposition.Pending))
                {
                    item.Disposition = ScalePageDisposition.FailedUnexpectedly;
                    item.NextAction = "ResumeAfterPipelineFault";
                }
            }

            var summary = ScaleRunSummaryBuilder.Build(
                manifest,
                options,
                runAttemptId,
                startedAt,
                clock.UtcNow,
                workItems,
                telemetry);
            ScaleRunStorage.WriteSummaryAtomic(outputRoot, summary);
            if (pipelineFailure != null)
            {
                ExceptionDispatchInfo.Capture(pipelineFailure).Throw();
            }
            return summary;
        }
    }
}
