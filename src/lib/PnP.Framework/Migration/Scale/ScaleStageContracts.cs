using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Migration.Scale
{
    public enum ScaleStageResumePolicy
    {
        ArtifactCheckpoint = 1,
        FreshProbe = 2,
        AlwaysExecute = 3
    }

    public enum ScaleStageProbeState
    {
        Absent = 1,
        Exact = 2,
        Drifted = 3,
        Unavailable = 4
    }

    public enum ScaleStageOutcome
    {
        Succeeded = 1,
        AlreadySatisfied = 2,
        OutcomeUnknownButConverged = 3,
        RetryableTransient = 4,
        AuthorizationBlocked = 5,
        NeedsRca = 6,
        NeedsPolicyDecision = 7,
        QuarantinedUnexpectedDifference = 8,
        FailedUnexpectedly = 9
    }

    public sealed class ScaleStageProbeResult
    {
        public ScaleStageProbeState State { get; set; }

        public bool FreshProbePerformed { get; set; }

        public bool ProvenanceMatched { get; set; }

        public string ObservedStateDigest { get; set; }

        public string TargetIdentityDigest { get; set; }

        public string DiagnosticCode { get; set; }

        public IList<ScaleStageArtifact> Artifacts { get; set; } = new List<ScaleStageArtifact>();

        public IList<ScaleRequestMetric> Requests { get; set; } = new List<ScaleRequestMetric>();

        public ScalePageProfile DiscoveredProfile { get; set; }
    }

    public sealed class ScaleStageExecutionResult
    {
        public ScaleStageOutcome Outcome { get; set; }

        public bool Verified { get; set; }

        public bool MutationAttempted { get; set; }

        public bool ProvenanceMatched { get; set; }

        public string ObservedStateDigest { get; set; }

        public string TargetIdentityDigest { get; set; }

        public string DiagnosticCode { get; set; }

        public int UnexpectedDifferenceCount { get; set; }

        public int SharedReceiptReuseCount { get; set; }

        public TimeSpan? RetryAfter { get; set; }

        public IList<ScaleStageArtifact> Artifacts { get; set; } = new List<ScaleStageArtifact>();

        public IList<ScaleRequestMetric> Requests { get; set; } = new List<ScaleRequestMetric>();

        public IList<ScaleIngredientRunResult> Ingredients { get; set; } = new List<ScaleIngredientRunResult>();

        public ScalePageProfile DiscoveredProfile { get; set; }
    }

    public sealed class ScaleRunStageContext
    {
        public string LoopId { get; set; }

        public string RunKey { get; set; }

        public string ManifestDigest { get; set; }

        public string RunAttemptId { get; set; }

        public ScaleRunMutationMode MutationMode { get; set; }

        public ScaleRunPage Page { get; set; }

        public ScalePageProfile EffectiveProfile { get; set; }

        public ScaleRunStage Stage { get; set; }

        public int Attempt { get; set; }

        public string OutputRoot { get; set; }

        public string StageStorageRoot { get; set; }

        public string StageOutputRoot { get; set; }

        public MigrationActionSignature Action { get; set; }

        public IList<ScaleStageArtifact> InputArtifacts { get; set; } = new List<ScaleStageArtifact>();
    }

    public interface IScaleRunStageExecutor
    {
        ScaleRunStage Stage { get; }

        string ContractDigest { get; }

        bool MutatesTarget { get; }

        /// <summary>
        /// True only when the executor can perform a real target mutation. Such
        /// an executor is admitted only by a digest-confirmed ExplicitApproved run.
        /// </summary>
        bool AllowsLiveMutation { get; }

        ScaleStageResumePolicy ResumePolicy { get; }

        Task<ScaleStageProbeResult> ProbeAsync(
            ScaleRunStageContext context,
            CancellationToken cancellationToken);

        Task<ScaleStageExecutionResult> ExecuteAsync(
            ScaleRunStageContext context,
            CancellationToken cancellationToken);
    }

    public interface IScaleRunClock
    {
        DateTimeOffset UtcNow { get; }

        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class SystemScaleRunClock : IScaleRunClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}
