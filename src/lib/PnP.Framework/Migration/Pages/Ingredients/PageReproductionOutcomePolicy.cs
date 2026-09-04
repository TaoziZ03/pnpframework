using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    internal static class PageReproductionOutcomePolicy
    {
        public static PageReproductionOutcome Evaluate(
            bool verificationPassed,
            PageMigrationOutcome plannedOutcome,
            IEnumerable<PageIngredientActionSelectionReceipt> approvedExclusions)
        {
            if (!verificationPassed)
            {
                return PageReproductionOutcome.Rejected;
            }
            if (plannedOutcome == PageMigrationOutcome.ExecutableWithApprovedExclusions
                || (approvedExclusions ?? Enumerable.Empty<PageIngredientActionSelectionReceipt>()).Any())
            {
                return PageReproductionOutcome.ReproducedWithApprovedExclusions;
            }
            if (plannedOutcome == PageMigrationOutcome.Unknown
                || plannedOutcome == PageMigrationOutcome.Blocked
                || plannedOutcome == PageMigrationOutcome.MitigationPending
                || plannedOutcome == PageMigrationOutcome.AuthorizationBlocked
                || plannedOutcome == PageMigrationOutcome.Invalid
                || plannedOutcome == PageMigrationOutcome.PartiallyExecutable)
            {
                return PageReproductionOutcome.Rejected;
            }
            return plannedOutcome == PageMigrationOutcome.ExecutableWithTransform
                ? PageReproductionOutcome.ReproducedWithTransform
                : plannedOutcome == PageMigrationOutcome.ExecutableWithLoss
                    ? PageReproductionOutcome.ReproducedWithKnownGaps
                : PageReproductionOutcome.ExactReproduction;
        }
    }
}
