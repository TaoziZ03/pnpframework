using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleIngredientOutcomeRules
    {
        public static void SetCompletedPageDisposition(ScalePageWorkItem item)
        {
            var ingredients = item.Stages.SelectMany(value => value.Ingredients).ToArray();
            if (ingredients.Any(value => value.Outcome == ScaleIngredientOutcome.QuarantinedUnexpectedDifference))
            {
                item.Disposition = ScalePageDisposition.Quarantined;
                item.NextAction = "QuarantineAndRca";
                return;
            }
            if (ingredients.Any(value => value.Outcome == ScaleIngredientOutcome.NeedsRca
                || value.Outcome == ScaleIngredientOutcome.NeedsCapability))
            {
                item.Disposition = ScalePageDisposition.NeedsRca;
                item.NextAction = "CollectIngredientEvidenceAndRca";
                return;
            }
            if (ingredients.Any(value => value.Outcome == ScaleIngredientOutcome.NeedsPolicyDecision))
            {
                item.Disposition = ScalePageDisposition.NeedsPolicyDecision;
                item.NextAction = "AwaitIngredientPolicyDecision";
                return;
            }
            if (ingredients.Where(value => value.Outcome == ScaleIngredientOutcome.SkippedByDependency)
                .Any() && !ingredients.Any(value => value.Outcome == ScaleIngredientOutcome.AuthorizationBlocked))
            {
                item.Disposition = ScalePageDisposition.NeedsRca;
                item.NextAction = "ResolveIngredientDependency";
                return;
            }
            if (ingredients.Any(value => value.Outcome == ScaleIngredientOutcome.AuthorizationBlocked))
            {
                item.Disposition = ScalePageDisposition.AuthorizationLimited;
                item.NextAction = "AwaitIngredientAuthorizationChange";
                return;
            }
            item.Disposition = ScalePageDisposition.Accepted;
            item.NextAction = "AdvanceNextWave";
        }
    }
}
