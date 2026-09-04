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
            if ((approvedExclusions ?? Enumerable.Empty<PageIngredientActionSelectionReceipt>()).Any())
            {
                return PageReproductionOutcome.ReproducedWithApprovedExclusions;
            }
            return plannedOutcome == PageMigrationOutcome.ExecutableWithTransform
                ? PageReproductionOutcome.ReproducedWithTransform
                : plannedOutcome == PageMigrationOutcome.ExecutableWithLoss
                    ? PageReproductionOutcome.ReproducedWithKnownGaps
                : PageReproductionOutcome.ExactReproduction;
        }
    }
}
