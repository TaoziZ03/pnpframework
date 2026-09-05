using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleRunSummaryBuilder
    {
        public static ScaleRunSummary Build(
            ScaleRunManifest manifest,
            ScaleRunControllerOptions options,
            string runAttemptId,
            DateTimeOffset startedAt,
            DateTimeOffset endedAt,
            IEnumerable<ScalePageWorkItem> workItems,
            ScaleRunTelemetry telemetry)
        {
            var pages = workItems.OrderBy(value => value.Page.Ordinal).Select(value => new ScalePageRunSummary
            {
                PageKey = value.Page.PageKey,
                Ordinal = value.Page.Ordinal,
                PageFamily = value.EffectiveProfile?.PageFamily ?? value.Page.PageFamily,
                TargetReferenceKey = value.EffectiveProfile?.TargetReferenceKey ?? value.Page.TargetReferenceKey,
                SupportCohortSignature = value.EffectiveProfile?.SupportCohortSignature ?? value.Page.SupportCohortSignature,
                ExecutionCohortSignature = value.EffectiveProfile?.ExecutionCohortSignature ?? value.Page.ExecutionCohortSignature,
                LoadBucket = value.EffectiveProfile?.LoadBucket ?? value.Page.LoadBucket,
                Disposition = value.Disposition,
                NextAction = value.NextAction,
                Stages = value.Stages.ToList()
            }).ToList();
            var stageSummaries = ScaleRunManifestValidator.Stages.Select(stage =>
            {
                var values = pages.SelectMany(value => value.Stages)
                    .Where(value => value.Stage == stage)
                    .ToArray();
                var requests = values.SelectMany(value => value.RequestDurationsMilliseconds)
                    .OrderBy(value => value)
                    .ToArray();
                return new ScaleStageAggregateSummary
                {
                    Stage = stage,
                    Count = values.Length,
                    SuccessCount = values.Count(value => ScaleStageOutcomeRules.IsSuccessful(value.Outcome)),
                    FailureCount = values.Count(value => !ScaleStageOutcomeRules.IsSuccessful(value.Outcome)),
                    ResumeSkipCount = values.Count(value => value.ResumeSkipped),
                    RetryCount = values.Sum(value => value.RetryCount),
                    RequestCount = values.Sum(value => value.RequestCount),
                    RequestP50Milliseconds = Percentile(requests, 0.50),
                    RequestP95Milliseconds = Percentile(requests, 0.95),
                    Http429Count = values.Sum(value => value.Http429Count),
                    Http503Count = values.Sum(value => value.Http503Count),
                    RetryAfterWaitMilliseconds = values.Sum(value => value.RetryAfterWaitMilliseconds),
                    MaxObservedConcurrency = telemetry.MaxStageConcurrency(stage)
                };
            }).ToList();
            var summary = new ScaleRunSummary
            {
                LoopId = manifest.LoopId,
                RunKey = manifest.RunKey,
                RunAttemptId = runAttemptId,
                ManifestDigest = manifest.ManifestDigest,
                MutationApprovalDigest = manifest.MutationMode == ScaleRunMutationMode.ExplicitApproved
                    ? options.ExplicitMutationConfirmationDigest
                    : null,
                MutationMode = manifest.MutationMode,
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                WallClockMilliseconds = Math.Max(0, (endedAt - startedAt).TotalMilliseconds),
                PageCount = pages.Count,
                AcceptedCount = pages.Count(value => value.Disposition == ScalePageDisposition.Accepted),
                AuthorizationBlockedCount = pages.Count(value => value.Disposition == ScalePageDisposition.AuthorizationBlocked),
                AuthorizationLimitedCount = pages.Count(value => value.Disposition == ScalePageDisposition.AuthorizationLimited),
                IngredientAuthorizationBlockedCount = pages.SelectMany(value => value.Stages)
                    .SelectMany(value => value.Ingredients)
                    .Count(value => value.Outcome == ScaleIngredientOutcome.AuthorizationBlocked),
                IngredientSkippedByDependencyCount = pages.SelectMany(value => value.Stages)
                    .SelectMany(value => value.Ingredients)
                    .Count(value => value.Outcome == ScaleIngredientOutcome.SkippedByDependency),
                RetryableCount = pages.Count(value => value.Disposition == ScalePageDisposition.Retryable),
                NeedsRcaCount = pages.Count(value => value.Disposition == ScalePageDisposition.NeedsRca),
                NeedsPolicyDecisionCount = pages.Count(value => value.Disposition == ScalePageDisposition.NeedsPolicyDecision),
                QuarantinedCount = pages.Count(value => value.Disposition == ScalePageDisposition.Quarantined),
                FailedUnexpectedlyCount = pages.Count(value => value.Disposition == ScalePageDisposition.FailedUnexpectedly),
                ResumeSkipCount = pages.SelectMany(value => value.Stages).Count(value => value.ResumeSkipped),
                OutcomeUnknownRecoveryCount = pages.SelectMany(value => value.Stages)
                    .Count(value => value.Outcome == ScaleStageOutcome.OutcomeUnknownButConverged),
                SharedReceiptReuseCount = pages.SelectMany(value => value.Stages)
                    .Sum(value => value.SharedReceiptReuseCount),
                MaxObservedUnverifiedTargets = telemetry.MaxUnverified,
                StageSummaries = stageSummaries,
                Pages = pages
            };
            var unresolved = summary.PageCount
                - summary.AcceptedCount
                - summary.AuthorizationBlockedCount
                - summary.AuthorizationLimitedCount;
            summary.CatalogProjection = new ScaleLoopCatalogProjection
            {
                LoopId = manifest.LoopId,
                Scope = string.Join(",", pages.Select(value => value.PageFamily)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)),
                Mutation = manifest.MutationMode == ScaleRunMutationMode.Disabled
                    ? "No"
                    : manifest.MutationMode == ScaleRunMutationMode.Simulation ? "Simulation" : "Approved",
                PagesAccepted = summary.AcceptedCount,
                PagesUnresolved = unresolved,
                PagesAuthorizationLimited = summary.AuthorizationLimitedCount + summary.AuthorizationBlockedCount,
                IngredientsAuthorizationBlocked = summary.IngredientAuthorizationBlockedCount,
                IngredientsSkippedByDependency = summary.IngredientSkippedByDependencyCount,
                NewIssueCount = summary.NeedsRcaCount + summary.QuarantinedCount + summary.FailedUnexpectedlyCount,
                Improvements = string.IsNullOrWhiteSpace(options.ImprovementReference)
                    ? "none-recorded"
                    : options.ImprovementReference,
                Gate = unresolved == 0 ? "Advance" : "Hold"
            };
            return summary;
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted == null || sorted.Length == 0)
            {
                return 0;
            }
            var index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }
    }
}
