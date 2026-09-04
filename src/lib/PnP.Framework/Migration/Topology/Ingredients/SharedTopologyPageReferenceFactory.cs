using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyPageReferenceFactory
    {
        public static SharedTopologyPageReference Create(
            SharedTopologyPlan plan,
            SharedTopologyTargetAnalysis analysis,
            SharedTopologyActionPlan actionPlan,
            Guid sourceSiteId,
            Guid sourceWebId)
        {
            SharedTopologyExecutionValidator.ValidateActionPlan(plan, analysis, actionPlan);
            var binding = plan.SourceWebBindings.SingleOrDefault(value => value.SourceSiteId == sourceSiteId && value.SourceWebId == sourceWebId);
            var fidelity = plan.SourceWebFidelityIngredients.SingleOrDefault(value => value.SourceSiteId == sourceSiteId && value.SourceWebId == sourceWebId);
            if (binding == null || fidelity == null)
            {
                throw new InvalidDataException("The shared topology plan has no source fidelity and target-container binding for this page Web.");
            }
            var containerById = plan.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            if (!containerById.TryGetValue(binding.TargetContainerIngredientId, out var leaf))
            {
                throw new InvalidDataException("The shared topology source binding references an unknown target-Web container.");
            }
            var required = plan.TargetWebContainers
                .Where(value => IsAncestorOrSelf(value, leaf, containerById))
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                .Select(value => value.IngredientId)
                .ToList();
            return new SharedTopologyPageReference
            {
                SharedTopologyPlanDigest = plan.PlanDigest,
                TargetAnalysisDigest = analysis.AnalysisDigest,
                ActionPlanDigest = actionPlan.ActionPlanDigest,
                SourceWebFidelityIngredientId = fidelity.IngredientId,
                TargetLeafContainerIngredientId = leaf.IngredientId,
                TargetWebUrl = leaf.TargetWebUrl,
                TargetServerRelativeUrl = leaf.TargetServerRelativeUrl,
                RequiredTargetContainerIngredientIds = required
            };
        }

        public static void Validate(
            SharedTopologyPageReference reference,
            SharedTopologyPlan plan,
            SharedTopologyTargetAnalysis analysis,
            SharedTopologyActionPlan actionPlan)
        {
            if (reference == null
                || !string.Equals(reference.SchemaVersion, SharedTopologyPageReference.CurrentSchemaVersion, StringComparison.Ordinal)
                || reference.RequiredTargetContainerIngredientIds == null
                || !string.Equals(reference.SharedTopologyPlanDigest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.TargetAnalysisDigest, analysis.AnalysisDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.ActionPlanDigest, actionPlan.ActionPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology page reference is missing or stale.");
            }
            var required = new HashSet<string>(reference.RequiredTargetContainerIngredientIds, StringComparer.Ordinal);
            if (reference.RequiredTargetContainerIngredientIds.Count != required.Count
                || !required.Contains(reference.TargetLeafContainerIngredientId)
                || !plan.SourceWebFidelityIngredients.Any(value => value.IngredientId == reference.SourceWebFidelityIngredientId)
                || required.Any(value => !plan.TargetWebContainers.Any(container => container.IngredientId == value)))
            {
                throw new InvalidDataException("The shared topology page reference contains invalid ingredient identities.");
            }
        }

        public static void ValidateReceipt(SharedTopologyPageReference reference, SharedTopologyMaterializationReceipt receipt)
        {
            if (reference == null || receipt == null || !receipt.FreshReadbackPassed
                || receipt.Webs == null
                || !string.Equals(reference.SharedTopologyPlanDigest, receipt.SharedTopologyPlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.ActionPlanDigest, receipt.ActionPlanDigest, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(receipt.ReceiptDigest)
                || !string.Equals(receipt.ReceiptDigest, SharedTopologyExecutionDigest.ComputeReceipt(receipt), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The page requires a freshly verified receipt for its shared topology plan.");
            }
            if (receipt.Webs.Any(value => value == null)
                || receipt.Webs.GroupBy(value => value.IngredientId, StringComparer.Ordinal).Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            {
                throw new InvalidDataException("The shared topology receipt contains a missing or duplicate Web ingredient receipt.");
            }
            var receipts = new HashSet<string>(receipt.Webs.Select(value => value.IngredientId), StringComparer.Ordinal);
            if (reference.RequiredTargetContainerIngredientIds.Any(value => !receipts.Contains(value)))
            {
                throw new InvalidDataException("The shared topology receipt does not cover every target-Web container required by the page.");
            }
            var leaf = receipt.Webs.SingleOrDefault(value => string.Equals(value.IngredientId, reference.TargetLeafContainerIngredientId, StringComparison.Ordinal));
            if (leaf == null
                || leaf.TargetSiteId == Guid.Empty
                || leaf.TargetWebId == Guid.Empty
                || leaf.TargetParentWebId == Guid.Empty
                || !SharedTopologyPath.EqualsUrl(leaf.TargetWebUrl, reference.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(leaf.TargetServerRelativeUrl, reference.TargetServerRelativeUrl))
            {
                throw new InvalidDataException("The shared topology receipt does not verify the page's exact target leaf Web container.");
            }
        }

        private static bool IsAncestorOrSelf(
            TargetWebContainerIngredientPlan candidate,
            TargetWebContainerIngredientPlan leaf,
            System.Collections.Generic.IReadOnlyDictionary<string, TargetWebContainerIngredientPlan> byId)
        {
            for (var current = leaf; current != null; current = byId.TryGetValue(current.ParentIngredientId ?? string.Empty, out var parent) ? parent : null)
            {
                if (string.Equals(current.IngredientId, candidate.IngredientId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
