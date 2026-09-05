namespace PnP.Framework.Migration.Scale
{
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
