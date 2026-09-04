using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Planning;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Lists.Execution
{
    internal static class ListItemMaterializationPolicy
    {
        public static IList<ListItemSnapshot> ReproducedItems(
            IEnumerable<ListItemSnapshot> items,
            ListMaterializationPlan plan)
        {
            var decisions = (plan.ItemDecisions ?? new List<ListItemMaterializationDecision>())
                .ToDictionary(value => value.SourceItemId);
            return (items ?? Enumerable.Empty<ListItemSnapshot>())
                .Where(value => decisions.TryGetValue(value.SourceItemId, out var decision)
                    && decision.Disposition == ListItemMaterializationDisposition.Reproduce)
                .ToList();
        }

        public static IList<ListItemMaterializationDecision> ApprovedExclusions(ListMaterializationPlan plan)
        {
            return (plan.ItemDecisions ?? new List<ListItemMaterializationDecision>())
                .Where(value => value.Disposition == ListItemMaterializationDisposition.ExcludeProtectedAsset)
                .OrderBy(value => value.SourceItemId)
                .ToList();
        }
    }
}
