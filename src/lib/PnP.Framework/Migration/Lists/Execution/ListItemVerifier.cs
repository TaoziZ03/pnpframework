using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Planning;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PnP.Framework.Migration.Lists.Execution
{
    internal static class ListItemVerifier
    {
        public static void Verify(
            ClientContext context,
            List list,
            ListDependencySnapshot source,
            ListMaterializationPlan plan,
            ListMaterializationReceipt receipt,
            IDictionary<Guid, ListMaterializationReceipt> dependencyReceipts,
            ListMaterializationExecutionScope.ListSelection selection,
            ICollection<string> diagnostics)
        {
            var exclusions = (plan.ApprovedProtectedDocumentExclusions
                    ?? Array.Empty<ListProtectedDocumentExclusionPlan>())
                .OrderBy(value => value.SourceItemId)
                .ToList();
            var excludedItemIds = new HashSet<int>(exclusions.Select(value => value.SourceItemId));
            var droppedLookupConsumerIds = DroppedItemDependencyPlanner
                .DroppedConsumerItemIds(plan.DroppedItemDependencies);
            excludedItemIds.UnionWith(droppedLookupConsumerIds);
            var reproducedItems = source.Items
                .Where(value => !excludedItemIds.Contains(value.SourceItemId))
                .OrderBy(value => value.SourceItemId)
                .ToList();
            var owned = !RequiresOwnedItemRead(receipt, plan)
                ? new Dictionary<int, ListItem>()
                : ReadOwnedItems(context, list, diagnostics);
            var exactInventory = selection == null || selection.ExactItemInventory;
            if (exactInventory && list.ItemCount != reproducedItems.Count)
            {
                diagnostics.Add("Target List ItemCount " + list.ItemCount
                    + " differs from approved reproduced item count " + reproducedItems.Count + ".");
            }
            if ((exactInventory && owned.Count != reproducedItems.Count)
                || receipt.TargetItemIds.Count != reproducedItems.Count)
            {
                diagnostics.Add("Target source-to-item mapping count differs from the approved reproduced item count.");
            }
            receipt.ProtectedDocumentExclusionVerifications = exclusions.Count == 0
                ? null
                : new List<ListProtectedDocumentExclusionVerification>();
            foreach (var exclusion in exclusions)
            {
                var verification = ProtectedDocumentTargetAbsenceProbe.Inspect(context, exclusion);
                if (owned.ContainsKey(exclusion.SourceItemId))
                {
                    verification.Status = ProtectedDocumentTargetAbsenceStatus.Present;
                    verification.Diagnostic = "A migration-owned target item exists for the excluded protected source item.";
                }
                receipt.ProtectedDocumentExclusionVerifications.Add(verification);
                if (!verification.Passed)
                {
                    diagnostics.Add("Protected-document exclusion verification returned "
                        + verification.Status + " for '" + exclusion.TargetServerRelativeUrl + "'"
                        + (verification.HttpStatusCode.HasValue
                            ? " (HTTP " + verification.HttpStatusCode.Value + ")"
                            : string.Empty)
                        + ": " + verification.Diagnostic);
                }
            }
            receipt.DroppedDependentItemVerifications = droppedLookupConsumerIds.Count == 0
                ? null
                : new List<ListDroppedDependentItemVerification>();
            foreach (var droppedConsumerId in droppedLookupConsumerIds.OrderBy(value => value))
            {
                var present = owned.ContainsKey(droppedConsumerId);
                receipt.DroppedDependentItemVerifications.Add(
                    new ListDroppedDependentItemVerification
                    {
                        SourceItemId = droppedConsumerId,
                        Status = present
                            ? DroppedDependentTargetIdentityStatus.Present
                            : DroppedDependentTargetIdentityStatus.Absent
                    });
                if (present)
                {
                    diagnostics.Add("A migration-owned target item exists for dropped dependent source item "
                        + droppedConsumerId + " even though the fixed-point policy excludes it.");
                }
            }
            var allReceipts = new Dictionary<Guid, ListMaterializationReceipt>(dependencyReceipts);
            allReceipts[source.SourceListId] = receipt;
            foreach (var sourceItem in reproducedItems)
            {
                int targetId;
                ListItem target;
                if (!receipt.TargetItemIds.TryGetValue(sourceItem.SourceItemId, out targetId)
                    || !owned.TryGetValue(sourceItem.SourceItemId, out target)
                    || target.Id != targetId)
                {
                    diagnostics.Add("Target item mapping is missing or inconsistent for source item " + sourceItem.SourceItemId + ".");
                    continue;
                }
                context.Load(target);
                var verifyAttachments = source.EnableAttachments || sourceItem.Attachments.Count > 0;
                if (verifyAttachments)
                {
                    context.Load(target.AttachmentFiles, values => values.Include(value => value.FileName, value => value.ServerRelativeUrl));
                }
                context.ExecuteQueryRetry();
                var includeItem = selection == null || selection.ItemIds.Contains(sourceItem.SourceItemId);
                if (includeItem)
                {
                    var expectedDigest = ListItemMaterializer.ComputeItemDigest(sourceItem);
                    if (!string.Equals(ReadString(target, ListItemMaterializer.OriginalItemDigestFieldName), expectedDigest, StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostics.Add("Target item provenance digest differs for source item " + sourceItem.SourceItemId + ".");
                    }
                    VerifyValues(sourceItem, target, plan, receipt.TargetContentTypeIds, allReceipts, diagnostics);
                    receipt.VerifiedItemCount++;
                }
                ListBinaryVerifier.VerifyDocument(context, source, plan, sourceItem, receipt, diagnostics);
                if (verifyAttachments)
                {
                    ListBinaryVerifier.VerifyAttachments(
                        context,
                        sourceItem,
                        target,
                        receipt,
                        selection == null || selection.ExactAttachmentInventoryItemIds.Contains(sourceItem.SourceItemId),
                        diagnostics);
                }
            }
        }

        internal static bool RequiresOwnedItemRead(
            ListMaterializationReceipt receipt,
            ListMaterializationPlan plan)
        {
            return receipt?.TargetItemIds?.Count > 0
                || (plan?.ApprovedProtectedDocumentExclusions?.Count ?? 0) > 0
                || DroppedItemDependencyPlanner.DroppedConsumerItemIds(
                    plan?.DroppedItemDependencies).Count > 0;
        }

        private static IDictionary<int, ListItem> ReadOwnedItems(ClientContext context, List list, ICollection<string> diagnostics)
        {
            var result = new Dictionary<int, ListItem>();
            ListItemCollectionPosition position = null;
            do
            {
                var items = list.GetItems(new CamlQuery
                {
                    ViewXml = "<View Scope='RecursiveAll'><ViewFields><FieldRef Name='ID'/><FieldRef Name='" + ListItemMaterializer.OriginalItemIdFieldName
                        + "'/><FieldRef Name='" + ListItemMaterializer.OriginalItemDigestFieldName + "'/></ViewFields><RowLimit Paged='TRUE'>2000</RowLimit></View>",
                    ListItemCollectionPosition = position
                });
                context.Load(items);
                context.ExecuteQueryRetry();
                foreach (var item in items)
                {
                    object raw;
                    int sourceId;
                    if (item.FieldValues.TryGetValue(ListItemMaterializer.OriginalItemIdFieldName, out raw)
                        && int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out sourceId))
                    {
                        if (result.ContainsKey(sourceId))
                        {
                            diagnostics.Add("Target List contains duplicate migration source item ID " + sourceId + ".");
                        }
                        else
                        {
                            result[sourceId] = item;
                        }
                    }
                }
                position = items.ListItemCollectionPosition;
            }
            while (position != null);
            return result;
        }

        private static void VerifyValues(
            ListItemSnapshot sourceItem,
            ListItem targetItem,
            ListMaterializationPlan plan,
            IDictionary<string, string> contentTypeIds,
            IDictionary<Guid, ListMaterializationReceipt> receipts,
            ICollection<string> diagnostics)
        {
            var fields = plan.Fields.ToDictionary(value => value.InternalName, StringComparer.OrdinalIgnoreCase);
            foreach (var sourceValue in sourceItem.Values.Where(value => value.Kind != ListItemValueKind.Null))
            {
                if (string.Equals(sourceValue.InternalName, "ContentTypeId", StringComparison.OrdinalIgnoreCase))
                {
                    VerifyContentTypeValue(sourceItem.SourceItemId, sourceValue, targetItem, contentTypeIds, diagnostics);
                    continue;
                }
                ListFieldMaterializationPlan fieldPlan;
                if (!fields.TryGetValue(sourceValue.InternalName, out fieldPlan) || !WritesValue(fieldPlan.Disposition))
                {
                    continue;
                }
                var clearedLookupProviderItemIds = fieldPlan.Disposition == ListFieldMaterializationDisposition.MapLookup
                    ? DroppedItemDependencyPlanner.ClearedLookupProviderItemIds(
                        plan.DroppedItemDependencies,
                        sourceItem.SourceItemId,
                        sourceValue.InternalName,
                        fieldPlan.SourceLookupListId)
                    : null;
                object actualValue;
                if (!targetItem.FieldValues.TryGetValue(sourceValue.InternalName, out actualValue))
                {
                    if (clearedLookupProviderItemIds?.Count > 0
                        && sourceValue.Kind == ListItemValueKind.Lookup
                        && (sourceValue.LookupValues ?? Array.Empty<ListItemLookupValueSnapshot>()).All(value => value != null
                            && clearedLookupProviderItemIds.Contains(value.LookupId)))
                    {
                        actualValue = null;
                    }
                    else
                    {
                        diagnostics.Add("Target value is missing for item " + sourceItem.SourceItemId + ", field '" + sourceValue.InternalName + "'.");
                        continue;
                    }
                }
                var actual = ListItemValueSerializer.Serialize(sourceValue.InternalName, actualValue);
                string mismatch;
                if (!ListItemValueComparer.Matches(
                        sourceValue,
                        actual,
                        fieldPlan,
                        receipts,
                        clearedLookupProviderItemIds,
                        out mismatch))
                {
                    diagnostics.Add("Target value differs for item " + sourceItem.SourceItemId + ", field '" + sourceValue.InternalName + "': " + mismatch);
                }
            }
        }

        private static void VerifyContentTypeValue(
            int sourceItemId,
            ListItemValueSnapshot sourceValue,
            ListItem targetItem,
            IDictionary<string, string> contentTypeIds,
            ICollection<string> diagnostics)
        {
            var sourceId = sourceValue.ScalarValue ?? sourceValue.RawValue;
            string expected;
            object actual;
            if (!string.IsNullOrWhiteSpace(sourceId)
                && contentTypeIds.TryGetValue(sourceId, out expected)
                && (!targetItem.FieldValues.TryGetValue("ContentTypeId", out actual)
                    || !string.Equals(ContentTypeIdValue(actual), expected, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add("Target ContentTypeId differs for source item " + sourceItemId + ".");
            }
        }

        private static string ContentTypeIdValue(object value)
        {
            var contentTypeId = value as ContentTypeId;
            return contentTypeId == null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : contentTypeId.StringValue;
        }

        private static bool WritesValue(ListFieldMaterializationDisposition value)
        {
            return value == ListFieldMaterializationDisposition.RequireTargetRuntimeAndCopyValue
                || value == ListFieldMaterializationDisposition.CreateOrReuseOwnedAndCopyValue
                || value == ListFieldMaterializationDisposition.MapLookup
                || value == ListFieldMaterializationDisposition.MapTaxonomy;
        }

        private static string ReadString(ListItem item, string field)
        {
            object value;
            return item.FieldValues.TryGetValue(field, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }
    }
}
