using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PnP.Framework.Migration.Lists.Execution
{
    internal static class ListInformationProtectionVerifier
    {
        public static void Verify(
            ListItemSnapshot sourceItem,
            ListItem targetItem,
            ListItemMaterializationDecision decision,
            ListMaterializationReceipt receipt,
            ICollection<string> diagnostics)
        {
            var selection = decision?.InformationProtectionSelectionReceipt;
            if (selection?.Action != IngredientSelectableAction.Reproduce)
            {
                return;
            }
            var source = sourceItem.Document?.InformationProtection;
            var targetLabel = ReadString(targetItem, "_IpLabelId");
            var targetAssignment = ReadString(targetItem, "_IpLabelAssignmentMethod");
            var matched = source != null
                && string.Equals(source.LabelId ?? string.Empty, targetLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(source.AssignmentMethod ?? string.Empty, targetAssignment ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            receipt.IngredientComparisons.Add(new PageIngredientComparisonResult
            {
                IngredientId = selection.IngredientId,
                Path = sourceItem.Document?.ServerRelativeUrl,
                SourcePresent = source != null,
                TargetPresent = !string.IsNullOrWhiteSpace(targetLabel),
                Outcome = matched ? IngredientComparisonOutcome.Exact : IngredientComparisonOutcome.UnexpectedDifference,
                Difference = matched ? IngredientDifferenceKind.None : IngredientDifferenceKind.UnexpectedAbsent,
                ReasonCode = selection.ReasonCode,
                PolicyId = selection.PolicyId,
                SelectionReceipt = selection
            });
            if (!matched)
            {
                diagnostics.Add("Target Information Protection relationship differs for source item " + sourceItem.SourceItemId + ".");
            }
        }

        private static string ReadString(ListItem item, string field)
        {
            object value;
            return item.FieldValues.TryGetValue(field, out value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;
        }
    }
}
