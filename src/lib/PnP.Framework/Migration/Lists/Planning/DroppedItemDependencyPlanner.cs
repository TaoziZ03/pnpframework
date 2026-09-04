using PnP.Framework.Migration.Lists.Capture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Lists.Planning
{
    internal sealed class DroppedItemDependencyProjection
    {
        public IList<ListItemDependencyEdge> SourceEdges { get; set; } = new List<ListItemDependencyEdge>();

        public IDictionary<Guid, IList<ListDroppedItemDependencyPlan>> PlansByConsumerList { get; set; }
            = new Dictionary<Guid, IList<ListDroppedItemDependencyPlan>>();

        public ISet<string> DroppedItemKeys { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    }

    internal static class DroppedItemDependencyPlanner
    {
        private const string DefaultDecisionPolicyId = "policy.list-item.dropped-dependency.needs-decision";
        private const string FolderPathPolicyId = "policy.list-folder.parent-path";

        public static DroppedItemDependencyProjection Project(
            IEnumerable<ListDependencySnapshot> sources,
            IEnumerable<ListLookupDependency> lookupDependencies,
            IEnumerable<ListMaterializationPlan> plans,
            IEnumerable<DroppedLookupValueDecision> decisions)
        {
            var sourceValues = (sources ?? Array.Empty<ListDependencySnapshot>())
                .Where(value => value != null)
                .ToArray();
            var planValues = (plans ?? Array.Empty<ListMaterializationPlan>())
                .Where(value => value != null)
                .ToArray();
            var decisionByEdge = DroppedLookupValueDecision.ValidateAndIndex(decisions);
            var dropped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var plan in planValues)
            {
                foreach (var exclusion in plan.ApprovedProtectedDocumentExclusions
                             ?? Array.Empty<ListProtectedDocumentExclusionPlan>())
                {
                    dropped.Add(ListItemDependencyEdge.ItemKey(plan.SourceListId, exclusion.SourceItemId));
                }
            }
            if (dropped.Count == 0 && decisionByEdge.Count == 0)
            {
                return new DroppedItemDependencyProjection
                {
                    DroppedItemKeys = dropped
                };
            }

            var edges = ListItemDependencyGraph.Build(sourceValues, lookupDependencies);
            var edgeByKey = edges.ToDictionary(value => value.Key, StringComparer.Ordinal);
            ValidateDecisions(decisionByEdge, edgeByKey);

            var active = new Dictionary<string, ListDroppedItemDependencyPlan>(StringComparer.Ordinal);
            var adjacency = edges
                .GroupBy(value => value.ProviderItemKey, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.Ordinal);
            var pendingProviders = new Queue<string>(dropped.OrderBy(value => value, StringComparer.Ordinal));
            var processedProviders = new HashSet<string>(StringComparer.Ordinal);
            while (pendingProviders.Count > 0)
            {
                var providerItemKey = pendingProviders.Dequeue();
                if (!processedProviders.Add(providerItemKey)
                    || !adjacency.TryGetValue(providerItemKey, out var outgoing))
                {
                    continue;
                }
                foreach (var edge in outgoing)
                {
                    if (active.ContainsKey(edge.Key))
                    {
                        continue;
                    }
                    var dependencyPlan = CreatePlan(edge, decisionByEdge);
                    active.Add(edge.Key, dependencyPlan);
                    if (dependencyPlan.Disposition == DroppedItemDependencyDisposition.DropDependentItem
                        && dropped.Add(edge.ConsumerItemKey))
                    {
                        pendingProviders.Enqueue(edge.ConsumerItemKey);
                    }
                }
            }

            return new DroppedItemDependencyProjection
            {
                SourceEdges = edges,
                DroppedItemKeys = dropped,
                PlansByConsumerList = active.Values
                    .GroupBy(value => value.ConsumerSourceListId)
                    .ToDictionary(
                        group => group.Key,
                        group => (IList<ListDroppedItemDependencyPlan>)group
                            .OrderBy(value => value.ConsumerSourceItemId)
                            .ThenBy(value => value.Kind)
                            .ThenBy(value => value.ConsumerFieldInternalName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(value => value.ProviderSourceListId)
                            .ThenBy(value => value.ProviderSourceItemId)
                            .ToList())
            };
        }

        public static bool HasUnresolvedRetainedConsumer(
            IEnumerable<ListDroppedItemDependencyPlan> plans)
        {
            return (plans ?? Array.Empty<ListDroppedItemDependencyPlan>())
                .Where(value => value != null)
                .GroupBy(value => value.ConsumerSourceItemId)
                .Any(group => !group.Any(value =>
                        value.Disposition == DroppedItemDependencyDisposition.DropDependentItem)
                    && group.Any(value =>
                        value.Disposition == DroppedItemDependencyDisposition.NeedsPolicyDecision));
        }

        public static ISet<int> DroppedConsumerItemIds(
            IEnumerable<ListDroppedItemDependencyPlan> plans)
        {
            return new HashSet<int>((plans ?? Array.Empty<ListDroppedItemDependencyPlan>())
                .Where(value => value?.Disposition == DroppedItemDependencyDisposition.DropDependentItem)
                .Select(value => value.ConsumerSourceItemId));
        }

        public static ISet<int> ClearedLookupProviderItemIds(
            IEnumerable<ListDroppedItemDependencyPlan> plans,
            int consumerSourceItemId,
            string consumerFieldInternalName,
            Guid? providerSourceListId)
        {
            return new HashSet<int>((plans ?? Array.Empty<ListDroppedItemDependencyPlan>())
                .Where(value => value != null
                    && value.Kind == ListItemDependencyKind.LookupValue
                    && value.Disposition == DroppedItemDependencyDisposition.ClearValue
                    && value.ConsumerSourceItemId == consumerSourceItemId
                    && string.Equals(
                        value.ConsumerFieldInternalName,
                        consumerFieldInternalName,
                        StringComparison.OrdinalIgnoreCase)
                    && (!providerSourceListId.HasValue
                        || value.ProviderSourceListId == providerSourceListId.Value))
                .Select(value => value.ProviderSourceItemId));
        }

        private static void ValidateDecisions(
            IReadOnlyDictionary<string, DroppedLookupValueDecision> decisions,
            IReadOnlyDictionary<string, ListItemDependencyEdge> edges)
        {
            foreach (var pair in decisions)
            {
                if (!edges.TryGetValue(pair.Key, out var edge)
                    || edge.Kind != ListItemDependencyKind.LookupValue)
                {
                    throw new InvalidDataException(
                        "A dropped lookup-value decision does not match one exact captured lookup edge: " + pair.Key);
                }
                if (edge.ConsumerEffectiveRequired
                    && pair.Value.Disposition == DroppedItemDependencyDisposition.ClearValue)
                {
                    throw new InvalidDataException(
                        "Required lookup field '" + edge.ConsumerFieldInternalName
                        + "' cannot use ClearValue for source item " + edge.ConsumerSourceItemId + ".");
                }
                if (!edge.ConsumerRequirementKnown
                    && pair.Value.Disposition == DroppedItemDependencyDisposition.ClearValue)
                {
                    throw new InvalidDataException(
                        "Lookup field '" + edge.ConsumerFieldInternalName
                        + "' cannot use ClearValue for source item " + edge.ConsumerSourceItemId
                        + " because the captured ContentType requirement could not be determined.");
                }
            }
        }

        private static ListDroppedItemDependencyPlan CreatePlan(
            ListItemDependencyEdge edge,
            IReadOnlyDictionary<string, DroppedLookupValueDecision> decisions)
        {
            if (edge.Kind == ListItemDependencyKind.FolderPath)
            {
                return FromEdge(
                    edge,
                    DroppedItemDependencyDisposition.DropDependentItem,
                    FolderPathPolicyId,
                    "A document-library child requires its nearest captured parent folder path; dropping that folder structurally drops the descendant item.");
            }

            decisions.TryGetValue(edge.Key, out var decision);
            var disposition = decision?.Disposition
                ?? DroppedItemDependencyDisposition.NeedsPolicyDecision;
            if (edge.ConsumerEffectiveRequired
                && disposition == DroppedItemDependencyDisposition.ClearValue)
            {
                throw new InvalidDataException(
                    "Required lookup field '" + edge.ConsumerFieldInternalName
                    + "' cannot use ClearValue for source item " + edge.ConsumerSourceItemId + ".");
            }
            if (!edge.ConsumerRequirementKnown
                && disposition == DroppedItemDependencyDisposition.ClearValue)
            {
                throw new InvalidDataException(
                    "Lookup field '" + edge.ConsumerFieldInternalName
                    + "' cannot use ClearValue for source item " + edge.ConsumerSourceItemId
                    + " because the captured ContentType requirement could not be determined.");
            }
            return FromEdge(
                edge,
                disposition,
                decision?.PolicyId ?? DefaultDecisionPolicyId,
                disposition == DroppedItemDependencyDisposition.ClearValue
                    ? "The exact reviewed edge decision clears this lookup value because its provider item is dropped."
                    : disposition == DroppedItemDependencyDisposition.DropDependentItem
                        ? "The exact reviewed edge decision drops this consumer because its lookup provider item is dropped."
                        : "This exact lookup edge requires a reviewed ClearValue or DropDependentItem decision before its retained consumer can execute.");
        }

        private static ListDroppedItemDependencyPlan FromEdge(
            ListItemDependencyEdge edge,
            DroppedItemDependencyDisposition disposition,
            string policyId,
            string reason)
        {
            return new ListDroppedItemDependencyPlan
            {
                Kind = edge.Kind,
                ConsumerSourceListId = edge.ConsumerSourceListId,
                ConsumerSourceItemId = edge.ConsumerSourceItemId,
                ConsumerFieldInternalName = edge.ConsumerFieldInternalName,
                ConsumerListFieldRequired = edge.ConsumerListFieldRequired,
                ConsumerContentTypeId = edge.ConsumerContentTypeId,
                ConsumerContentTypeResolved = edge.ConsumerContentTypeResolved,
                ConsumerContentTypeFieldLinkRequired = edge.ConsumerContentTypeFieldLinkRequired,
                ConsumerEffectiveRequired = edge.ConsumerEffectiveRequired,
                ConsumerRequirementKnown = edge.ConsumerRequirementKnown,
                ProviderSourceWebId = edge.ProviderSourceWebId,
                ProviderSourceListId = edge.ProviderSourceListId,
                ProviderSourceItemId = edge.ProviderSourceItemId,
                Disposition = disposition,
                PolicyId = policyId,
                Reason = reason
            };
        }
    }
}
