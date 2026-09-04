using PnP.Framework.Migration.Pages.Ingredients;
using System;

namespace PnP.Framework.Migration.Lists.Execution
{
    internal static class ProtectedAssetExecutionPolicy
    {
        public static PageIngredientActionSelectionReceipt Execute(
            PageIngredientActionSelectionReceipt receipt,
            Action mutation)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            if (receipt.Action == IngredientSelectableAction.Exclude
                || receipt.Action == IngredientSelectableAction.EvidenceOnly)
            {
                return receipt;
            }
            if (receipt.Action == IngredientSelectableAction.Defer)
            {
                throw new InvalidOperationException("A deferred protected-asset action cannot execute.");
            }
            if (mutation == null)
            {
                throw new ArgumentNullException(nameof(mutation));
            }
            mutation();
            return receipt;
        }
    }
}
