using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Scale
{
    public enum ScalePageDisposition
    {
        Pending = 1,
        Accepted = 2,
        Retryable = 3,
        AuthorizationBlocked = 4,
        NeedsRca = 5,
        NeedsPolicyDecision = 6,
        Quarantined = 7,
        FailedUnexpectedly = 8
    }

    public sealed class ScaleStageRunSummary
    {
        public ScaleRunStage Stage { get; set; }

        public int AttemptCount { get; set; }

        public ScaleStageOutcome Outcome { get; set; }

        public bool ResumeSkipped { get; set; }

        public bool Verified { get; set; }

        public double DurationMilliseconds { get; set; }

        public double BackpressureWaitMilliseconds { get; set; }

        public int RequestCount { get; set; }

        public IList<double> RequestDurationsMilliseconds { get; set; } = new List<double>();

        public int RetryCount { get; set; }

        public int Http429Count { get; set; }

        public int Http503Count { get; set; }

        public double RetryAfterWaitMilliseconds { get; set; }

        public int UnexpectedDifferenceCount { get; set; }

        public int SharedReceiptReuseCount { get; set; }

        public string ArtifactSetDigest { get; set; }

        public string DiagnosticCode { get; set; }
    }

    public sealed class ScalePageRunSummary
    {
        public string PageKey { get; set; }

        public int Ordinal { get; set; }

        public string PageFamily { get; set; }

        public string SupportCohortSignature { get; set; }

        public string ExecutionCohortSignature { get; set; }

        public string LoadBucket { get; set; }

        public ScalePageDisposition Disposition { get; set; }

        public string NextAction { get; set; }

        public IList<ScaleStageRunSummary> Stages { get; set; } = new List<ScaleStageRunSummary>();
    }

    public sealed class ScaleStageAggregateSummary
    {
        public ScaleRunStage Stage { get; set; }

        public int Count { get; set; }

        public int SuccessCount { get; set; }

        public int FailureCount { get; set; }

        public int ResumeSkipCount { get; set; }

        public int RetryCount { get; set; }

        public int RequestCount { get; set; }

        public double RequestP50Milliseconds { get; set; }

        public double RequestP95Milliseconds { get; set; }

        public int Http429Count { get; set; }

        public int Http503Count { get; set; }

        public double RetryAfterWaitMilliseconds { get; set; }

        public int MaxObservedConcurrency { get; set; }
    }

    public sealed class ScaleLoopCatalogProjection
    {
        public const string CurrentSchemaVersion = "pnp-scale-loop-catalog-update/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string LoopId { get; set; }

        public string Scope { get; set; }

        public string Mutation { get; set; }

        public int PagesAccepted { get; set; }

        public int PagesUnresolved { get; set; }

        public int NewIssueCount { get; set; }

        public string Improvements { get; set; }

        public string Gate { get; set; }

        public string SummaryDigest { get; set; }
    }

    public sealed class ScaleRunSummary
    {
        public const string CurrentSchemaVersion = "pnp-scale-run-summary/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string LoopId { get; set; }

        public string RunKey { get; set; }

        public string RunAttemptId { get; set; }

        public string ManifestDigest { get; set; }

        public string MutationApprovalDigest { get; set; }

        public ScaleRunMutationMode MutationMode { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset EndedAtUtc { get; set; }

        public double WallClockMilliseconds { get; set; }

        public int PageCount { get; set; }

        public int AcceptedCount { get; set; }

        public int AuthorizationBlockedCount { get; set; }

        public int RetryableCount { get; set; }

        public int NeedsRcaCount { get; set; }

        public int NeedsPolicyDecisionCount { get; set; }

        public int QuarantinedCount { get; set; }

        public int FailedUnexpectedlyCount { get; set; }

        public int ResumeSkipCount { get; set; }

        public int OutcomeUnknownRecoveryCount { get; set; }

        public int SharedReceiptReuseCount { get; set; }

        public int MaxObservedUnverifiedTargets { get; set; }

        public IList<ScaleStageAggregateSummary> StageSummaries { get; set; } = new List<ScaleStageAggregateSummary>();

        public IList<ScalePageRunSummary> Pages { get; set; } = new List<ScalePageRunSummary>();

        public ScaleLoopCatalogProjection CatalogProjection { get; set; }

        public string SummaryDigest { get; set; }
    }
}
