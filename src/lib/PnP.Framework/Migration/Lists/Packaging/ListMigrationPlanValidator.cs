using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Planning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Schema.ContentTypes.Packaging;

namespace PnP.Framework.Migration.Lists.Packaging
{
    internal static class ListMigrationPlanValidator
    {
        public static void Validate(IEnumerable<ListDependencySnapshot> snapshots, ListMigrationPlanSet plan)
        {
            var sources = (snapshots ?? Enumerable.Empty<ListDependencySnapshot>()).ToArray();
            if (sources.Length == 0 && plan == null)
            {
                return;
            }
            if (plan == null || !string.Equals(plan.SchemaVersion, "pnp-list-migration-plan/v2", StringComparison.Ordinal)
                || plan.Lists == null || plan.OrderedSourceListIds == null || plan.Issues == null)
            {
                throw new InvalidDataException("Captured List dependencies require a complete List migration plan.");
            }
            var sourceIds = new HashSet<Guid>(sources.Select(value => value.SourceListId));
            var plannedIds = new HashSet<Guid>(plan.Lists.Select(value => value == null ? Guid.Empty : value.SourceListId));
            if (plan.Lists.Any(value => value == null) || sourceIds.Count != plannedIds.Count || !sourceIds.SetEquals(plannedIds))
            {
                throw new InvalidDataException("The List migration plan must contain exactly one plan for every captured List dependency.");
            }
            foreach (var list in plan.Lists)
            {
                if (list.Fields == null || list.Views == null || list.ItemDecisions == null || list.SiteContentTypes == null || list.Issues == null || string.IsNullOrWhiteSpace(list.OriginalIdentifier)
                    || !string.Equals(ListMigrationPlanFactory.ComputePlanDigest(list), list.PlanDigest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A List materialization plan is incomplete or its semantic digest differs: " + list.SourceListId.ToString("D"));
                }
                var source = sources.Single(value => value.SourceListId == list.SourceListId);
                var sourceItemIds = new HashSet<int>(source.Items.Select(value => value.SourceItemId));
                var decisionIds = new HashSet<int>(list.ItemDecisions.Select(value => value == null ? 0 : value.SourceItemId));
                if (list.ItemDecisions.Any(value => value == null || value.SourceItemId <= 0)
                    || sourceItemIds.Count != decisionIds.Count
                    || !sourceItemIds.SetEquals(decisionIds))
                {
                    throw new InvalidDataException("A List item materialization decision must cover every captured source item exactly once.");
                }
                foreach (var decision in list.ItemDecisions.Where(value => value.Disposition == ListItemMaterializationDisposition.ExcludeProtectedAsset))
                {
                    if (decision.SelectionReceipt == null
                        || decision.SelectionReceipt.Action != Pages.Ingredients.IngredientSelectableAction.Exclude
                        && decision.SelectionReceipt.Action != Pages.Ingredients.IngredientSelectableAction.EvidenceOnly
                        || !string.Equals(decision.BinaryPayloadIngredientId, decision.SelectionReceipt.IngredientId, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("An excluded protected List item requires the matching BinaryPayload selection receipt.");
                    }
                }
                foreach (var decision in list.ItemDecisions.Where(value => !string.IsNullOrWhiteSpace(value.InformationProtectionIngredientId)))
                {
                    if (decision.InformationProtectionSelectionReceipt == null
                        || !string.Equals(
                            decision.InformationProtectionIngredientId,
                            decision.InformationProtectionSelectionReceipt.IngredientId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("A protected List item requires the matching InformationProtectionRelationship selection receipt.");
                    }
                }
                if (list.Disposition != ListMaterializationDisposition.Block && (list.TargetProbe == null || !list.TargetProbe.IsAdmitted))
                {
                    throw new InvalidDataException("An executable List plan has no admitted target probe: " + list.SourceListId.ToString("D"));
                }
                foreach (var contentType in list.SiteContentTypes)
                {
                    if (contentType == null || contentType.Schema == null
                        || !string.Equals(ContentTypeClosurePlanner.ComputeDigest(contentType), contentType.PlanDigest, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("A site content type closure node is incomplete or its digest differs.");
                    }
                    ContentTypeSchemaContractValidator.ValidatePlan(contentType.Schema);
                    if (list.Disposition != ListMaterializationDisposition.Block
                        && !contentType.DeferredUntilTopologyMaterialization
                        && (contentType.TargetAdmission == null || !contentType.TargetAdmission.IsEligible))
                    {
                        throw new InvalidDataException("An executable site content type plan has no admitted target analysis: " + contentType.Schema.ContentTypeId);
                    }
                }
            }
            if (!string.Equals(ListMigrationPlanFactory.ComputeSetDigest(plan), plan.PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The List migration plan-set digest differs from its sealed content.");
            }
        }
    }
}
