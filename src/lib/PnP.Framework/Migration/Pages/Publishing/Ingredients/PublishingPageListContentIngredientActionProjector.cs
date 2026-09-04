using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageListContentIngredientActionProjector
    {
        public static void Project(
            ListDependencySnapshot source,
            ListMaterializationPlan listPlan,
            bool listBlocked,
            IDictionary<string, PageIngredientAction> actions,
            bool transactionDependencyProjection)
        {
            var fieldPlans = listPlan.Fields.ToDictionary(value => value.InternalName, StringComparer.OrdinalIgnoreCase);
            foreach (var item in source.Items.Where(value => value != null))
            {
                var exclusion = (listPlan.ApprovedProtectedDocumentExclusions
                        ?? Array.Empty<ListProtectedDocumentExclusionPlan>())
                    .SingleOrDefault(value => value.SourceItemId == item.SourceItemId);
                var droppedLookupDependencies = (listPlan.DroppedLookupValueDependencies
                        ?? Array.Empty<ListDroppedLookupValueDependencyPlan>())
                    .Where(value => value.ConsumerSourceItemId == item.SourceItemId)
                    .ToArray();
                AddItem(source, listPlan, item, fieldPlans, listBlocked, actions, transactionDependencyProjection, exclusion, droppedLookupDependencies);
                if (item.Document != null)
                {
                    AddDocument(source, listPlan, item, listBlocked, actions, transactionDependencyProjection, exclusion, droppedLookupDependencies);
                    if (ProtectedAssetCaptureGate.HasItemProtection(item.Document.InformationProtection))
                    {
                        AddInformationProtection(source, item, actions, exclusion, droppedLookupDependencies);
                    }
                }
                foreach (var attachment in item.Attachments.Where(value => value != null))
                {
                    AddAttachment(source, listPlan, item, attachment, listBlocked, actions, transactionDependencyProjection, exclusion, droppedLookupDependencies);
                }
            }
        }

        private static void AddItem(
            ListDependencySnapshot source,
            ListMaterializationPlan listPlan,
            ListItemSnapshot item,
            IDictionary<string, ListFieldMaterializationPlan> fieldPlans,
            bool listBlocked,
            IDictionary<string, PageIngredientAction> actions,
            bool transactionDependencyProjection,
            ListProtectedDocumentExclusionPlan exclusion,
            IList<ListDroppedLookupValueDependencyPlan> droppedLookupDependencies)
        {
            if (exclusion != null)
            {
                PublishingPageIngredientActionFactory.Add(actions, ExclusionAction(
                    PublishingPageIngredientIds.ListItem(source.SourceWebId, source.SourceListId, item.SourceItemId),
                    exclusion,
                    "document-backed List item"));
                return;
            }
            var lookupAction = LookupConsumerAction(
                PublishingPageIngredientIds.ListItem(source.SourceWebId, source.SourceListId, item.SourceItemId),
                droppedLookupDependencies,
                "dependent List item");
            if (lookupAction != null
                && lookupAction.Disposition != IngredientDisposition.Transform)
            {
                PublishingPageIngredientActionFactory.Add(actions, lookupAction);
                return;
            }
            var mapping = MapItem(item, fieldPlans, listBlocked, transactionDependencyProjection);
            var targetIdentity = listPlan.TargetRootFolderServerRelativeUrl + "#source-item:" + item.SourceItemId;
            var action = PublishingPageIngredientActionFactory.Create(
                PublishingPageIngredientIds.ListItem(source.SourceWebId, source.SourceListId, item.SourceItemId),
                mapping.Capability,
                lookupAction?.Disposition ?? mapping.Disposition,
                lookupAction?.Realization ?? mapping.Realization,
                lookupAction?.PolicyId ?? "policy.list-item.current-state",
                lookupAction?.Reason ?? mapping.Reason,
                mapping.Disposition == IngredientDisposition.Block ? null : targetIdentity,
                mapping.Disposition == IngredientDisposition.Block
                    ? null
                    : $"The List receipt contains a source-to-target item ID mapping for source item '{item.SourceItemId}'.",
                mapping.Disposition == IngredientDisposition.Block
                    ? null
                    : lookupAction == null
                        ? "Fresh readback verifies every approved value and the item provenance digest."
                        : "Fresh readback verifies the cleared lookup field, every other approved value, and the item provenance digest.");
            if (lookupAction != null)
            {
                action.ReleasedDependencyIngredientIds = lookupAction.ReleasedDependencyIngredientIds;
            }
            PublishingPageIngredientActionFactory.Add(actions, action);
        }

        private static void AddDocument(
            ListDependencySnapshot source,
            ListMaterializationPlan listPlan,
            ListItemSnapshot item,
            bool listBlocked,
            IDictionary<string, PageIngredientAction> actions,
            bool transactionDependencyProjection,
            ListProtectedDocumentExclusionPlan exclusion,
            IList<ListDroppedLookupValueDependencyPlan> droppedLookupDependencies)
        {
            var document = item.Document;
            if (exclusion != null)
            {
                PublishingPageIngredientActionFactory.Add(actions, ExclusionAction(
                    PublishingPageIngredientIds.ListDocument(source.SourceWebId, source.SourceListId, item.SourceItemId),
                    exclusion,
                    "protected document payload"));
                return;
            }
            var lookupAction = LookupConsumerAction(
                PublishingPageIngredientIds.ListDocument(source.SourceWebId, source.SourceListId, item.SourceItemId),
                droppedLookupDependencies,
                "document owned by the lookup-dependent item");
            if (lookupAction != null && lookupAction.Disposition != IngredientDisposition.Transform)
            {
                PublishingPageIngredientActionFactory.Add(actions, lookupAction);
                return;
            }
            var binaryUnavailable = document.Kind == ListDocumentObjectKind.File
                && (document.Content == null
                    || document.Content.Availability != EvidenceAvailability.Captured
                    || document.Content.Artifact == null);
            var rightsManaged = document.Kind == ListDocumentObjectKind.File
                && ListMigrationPlanFactory.IsRightsManagedEnvelope(document.Content);
            var unclassified = document.Kind == ListDocumentObjectKind.File
                && ListMigrationPlanFactory.IsUnclassifiedBinary(document.Content);
            var legacyBlocked = !transactionDependencyProjection && listBlocked;
            var deferred = binaryUnavailable || rightsManaged || unclassified;
            var blocked = legacyBlocked || deferred;
            PublishingPageIngredientActionFactory.Add(actions, PublishingPageIngredientActionFactory.Create(
                PublishingPageIngredientIds.ListDocument(source.SourceWebId, source.SourceListId, item.SourceItemId),
                blocked
                    ? rightsManaged || unclassified ? IngredientCapability.Unknown : IngredientCapability.Incompatible
                    : IngredientCapability.Available,
                legacyBlocked
                    ? IngredientDisposition.Block
                    : deferred ? IngredientDisposition.Defer : IngredientDisposition.Preserve,
                blocked
                    ? rightsManaged
                        ? "retain-envelope-and-logical-identity-pending-replay-evidence"
                        : unclassified
                            ? "fresh-capture-to-classify-binary-representation"
                        : "none"
                    : document.Kind == ListDocumentObjectKind.Folder ? "create-or-reuse-folder" : "copy-exact-bytes-create-only",
                "policy.list-document.current-state",
                blocked
                    ? rightsManaged
                        ? "The exact rights-managed response envelope is retained, but repeated source reads prove that raw envelope SHA is not a stable logical-content identity and cross-site replay has not been verified."
                        : unclassified
                            ? "The immutable legacy snapshot seals exact returned bytes but predates ordinary-file versus rights-managed-envelope classification, so this binary branch remains deferred until a fresh capture classifies it."
                        : "The document object cannot be replayed because its owning List is blocked or exact file bytes are unavailable."
                    : "Materialize the captured current document or folder object under the target-owned List path.",
                blocked ? null : MapListOwnedPath(source, listPlan, document.ServerRelativeUrl),
                blocked
                    ? null
                    : document.Kind == ListDocumentObjectKind.Folder
                        ? "Fresh readback verifies the target folder path and item provenance."
                        : $"Fresh readback verifies target file bytes with SHA-256 '{document.Content?.Artifact?.Sha256}'."));
        }

        private static void AddInformationProtection(
            ListDependencySnapshot source,
            ListItemSnapshot item,
            IDictionary<string, PageIngredientAction> actions,
            ListProtectedDocumentExclusionPlan exclusion,
            IList<ListDroppedLookupValueDependencyPlan> droppedLookupDependencies)
        {
            var informationProtection = item.Document.InformationProtection;
            if (exclusion != null)
            {
                PublishingPageIngredientActionFactory.Add(actions, ExclusionAction(
                    PublishingPageIngredientIds.ListDocumentInformationProtection(
                        source.SourceWebId,
                        source.SourceListId,
                        item.SourceItemId),
                    exclusion,
                    "target Information Protection relationship"));
                return;
            }
            var lookupAction = LookupConsumerAction(
                PublishingPageIngredientIds.ListDocumentInformationProtection(
                    source.SourceWebId,
                    source.SourceListId,
                    item.SourceItemId),
                droppedLookupDependencies,
                "Information Protection relationship owned by the lookup-dependent item");
            if (lookupAction != null && lookupAction.Disposition != IngredientDisposition.Transform)
            {
                PublishingPageIngredientActionFactory.Add(actions, lookupAction);
                return;
            }
            var libraryIrmState = source.InformationRightsManagement == null
                ? "not captured"
                : source.InformationRightsManagement.IrmEnabled ? "enabled" : "disabled";
            PublishingPageIngredientActionFactory.Add(actions, PublishingPageIngredientActionFactory.Create(
                PublishingPageIngredientIds.ListDocumentInformationProtection(
                    source.SourceWebId,
                    source.SourceListId,
                    item.SourceItemId),
                IngredientCapability.Unknown,
                IngredientDisposition.Defer,
                "preserve-source-label-assignment-pending-cross-tenant-replay-evidence",
                "policy.list-document-information-protection.current-state",
                "The source document retains Information Protection label '"
                    + informationProtection.LabelId
                    + "' while source library IRM is " + libraryIrmState
                    + ". The relationship is captured exactly, but cross-tenant label availability, protected-payload replay, and target usability have not been proven.",
                null));
        }

        private static void AddAttachment(
            ListDependencySnapshot source,
            ListMaterializationPlan listPlan,
            ListItemSnapshot item,
            ListAttachmentSnapshot attachment,
            bool listBlocked,
            IDictionary<string, PageIngredientAction> actions,
            bool transactionDependencyProjection,
            ListProtectedDocumentExclusionPlan exclusion,
            IList<ListDroppedLookupValueDependencyPlan> droppedLookupDependencies)
        {
            if (exclusion != null)
            {
                PublishingPageIngredientActionFactory.Add(actions, ExclusionAction(
                    PublishingPageIngredientIds.ListAttachment(
                        source.SourceWebId,
                        source.SourceListId,
                        item.SourceItemId,
                        attachment.FileName),
                    exclusion,
                    "attachment owned by the excluded document-backed item"));
                return;
            }
            var lookupAction = LookupConsumerAction(
                PublishingPageIngredientIds.ListAttachment(
                    source.SourceWebId,
                    source.SourceListId,
                    item.SourceItemId,
                    attachment.FileName),
                droppedLookupDependencies,
                "attachment owned by the lookup-dependent item");
            if (lookupAction != null && lookupAction.Disposition != IngredientDisposition.Transform)
            {
                PublishingPageIngredientActionFactory.Add(actions, lookupAction);
                return;
            }
            var blocked = (!transactionDependencyProjection && listBlocked)
                || attachment.Content == null
                || attachment.Content.Availability != EvidenceAvailability.Captured
                || attachment.Content.Artifact == null;
            var unclassified = ListMigrationPlanFactory.IsUnclassifiedBinary(attachment.Content);
            var legacyBlocked = !transactionDependencyProjection && listBlocked;
            var deferred = !legacyBlocked && (blocked || unclassified);
            blocked = legacyBlocked || deferred;
            PublishingPageIngredientActionFactory.Add(actions, PublishingPageIngredientActionFactory.Create(
                PublishingPageIngredientIds.ListAttachment(source.SourceWebId, source.SourceListId, item.SourceItemId, attachment.FileName),
                blocked
                    ? unclassified ? IngredientCapability.Unknown : IngredientCapability.Incompatible
                    : IngredientCapability.Available,
                legacyBlocked
                    ? IngredientDisposition.Block
                    : deferred ? IngredientDisposition.Defer : IngredientDisposition.Preserve,
                blocked
                    ? unclassified ? "fresh-capture-to-classify-binary-representation" : "none"
                    : "copy-exact-bytes-create-only",
                "policy.list-attachment.current-state",
                blocked
                    ? unclassified
                        ? "The immutable legacy snapshot seals exact returned bytes but predates ordinary-file versus rights-managed-envelope classification, so this attachment branch remains deferred until a fresh capture classifies it."
                        : "The attachment cannot be replayed because its owning List is blocked or exact bytes are unavailable."
                    : "Copy the exact captured attachment bytes to the materialized target item.",
                blocked ? null : listPlan.TargetRootFolderServerRelativeUrl + "#source-item:" + item.SourceItemId + "/attachment:" + attachment.FileName,
                blocked ? null : $"Fresh readback verifies attachment bytes with SHA-256 '{attachment.Content?.Artifact?.Sha256}'."));
        }

        private static PageIngredientAction ExclusionAction(
            string ingredientId,
            ListProtectedDocumentExclusionPlan exclusion,
            string subject)
        {
            return PublishingPageIngredientActionFactory.Create(
                ingredientId,
                IngredientCapability.Available,
                IngredientDisposition.Drop,
                "exclude-protected-document-backed-item",
                exclusion.PolicyId,
                "The explicit protected-asset capture decision retains source metadata but excludes the "
                    + subject + " from target materialization. " + exclusion.Reason,
                null,
                "No target mutation is performed for this approved protected-asset exclusion.",
                "Fresh target readback requires the excluded document path to remain absent.");
        }

        private static PageIngredientAction LookupConsumerAction(
            string ingredientId,
            IEnumerable<ListDroppedLookupValueDependencyPlan> dependencies,
            string subject)
        {
            var values = (dependencies ?? Array.Empty<ListDroppedLookupValueDependencyPlan>())
                .Where(value => value != null)
                .ToArray();
            if (values.Length == 0)
            {
                return null;
            }

            var unresolved = values.Any(value =>
                value.Disposition == DroppedLookupValueDisposition.NeedsPolicyDecision);
            var drop = !unresolved && values.Any(value =>
                value.Disposition == DroppedLookupValueDisposition.DropDependentItem);
            var disposition = unresolved
                ? IngredientDisposition.Defer
                : drop ? IngredientDisposition.Drop : IngredientDisposition.Transform;
            var policyIds = values.Select(value => value.PolicyId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var action = PublishingPageIngredientActionFactory.Create(
                ingredientId,
                unresolved ? IngredientCapability.Unknown : IngredientCapability.Available,
                disposition,
                unresolved
                    ? "select-dropped-lookup-value-policy"
                    : drop ? "exclude-lookup-dependent-item" : "clear-lookup-value-with-excluded-provider",
                policyIds.Length == 1 ? policyIds[0] : "policy.list-item.lookup-provider-excluded",
                unresolved
                    ? "The " + subject + " references an intentionally excluded protected document-backed item and requires an explicit clear-value or drop-dependent-item policy before execution."
                    : drop
                        ? "The reviewed dropped-lookup-value policy excludes the " + subject + " because its captured value depends on an intentionally excluded protected document-backed item."
                        : "The reviewed dropped-lookup-value policy keeps the " + subject + " but clears each lookup field that references an intentionally excluded protected document-backed item.");
            if (disposition == IngredientDisposition.Transform)
            {
                action.ReleasedDependencyIngredientIds = values
                    .Select(value => PublishingPageIngredientIds.ListItem(
                        value.LookupSourceWebId,
                        value.LookupSourceListId,
                        value.DroppedLookupSourceItemId))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
            }
            return action;
        }

        private static (IngredientCapability Capability, IngredientDisposition Disposition, string Realization, string Reason) MapItem(
            ListItemSnapshot item,
            IDictionary<string, ListFieldMaterializationPlan> fieldPlans,
            bool listBlocked,
            bool transactionDependencyProjection)
        {
            if (listBlocked && !transactionDependencyProjection)
            {
                return (IngredientCapability.Incompatible, IngredientDisposition.Block, "none", "The owning List has no executable materialization plan.");
            }

            var transformed = false;
            var snapshotOnlyValues = new List<string>();
            foreach (var value in item.Values.Where(value => value != null && value.Kind != ListItemValueKind.Null))
            {
                if (string.Equals(value.InternalName, "ContentTypeId", StringComparison.OrdinalIgnoreCase))
                {
                    transformed = true;
                    continue;
                }
                if (!fieldPlans.TryGetValue(value.InternalName, out var fieldPlan)
                    || fieldPlan.Disposition == ListFieldMaterializationDisposition.Block
                    || fieldPlan.Disposition == ListFieldMaterializationDisposition.EvidenceOnly)
                {
                    if (!transactionDependencyProjection)
                    {
                        return (
                            IngredientCapability.Incompatible,
                            IngredientDisposition.Block,
                            "none",
                            $"Captured value '{value.InternalName}' has no approved replay or substitution action.");
                    }
                    snapshotOnlyValues.Add(value.InternalName);
                    transformed = true;
                    continue;
                }
                if (fieldPlan.Disposition == ListFieldMaterializationDisposition.MapLookup
                    || fieldPlan.Disposition == ListFieldMaterializationDisposition.MapTaxonomy
                    || fieldPlan.Disposition == ListFieldMaterializationDisposition.RequireTargetRuntime
                    || fieldPlan.Disposition == ListFieldMaterializationDisposition.CreateOrReuseOwnedCalculated
                    || fieldPlan.Disposition == ListFieldMaterializationDisposition.CreateOrReuseOwnedSchemaOnly)
                {
                    transformed = true;
                }
            }

            return snapshotOnlyValues.Count > 0
                ? (IngredientCapability.Available, IngredientDisposition.Transform, "replay-recognized-values-retain-others-snapshot-only",
                    "Replay recognized values and retain deferred or intentionally omitted values only in the immutable snapshot: "
                    + string.Join(", ", snapshotOnlyValues.Distinct(StringComparer.OrdinalIgnoreCase)) + ".")
                : transformed
                ? (IngredientCapability.Available, IngredientDisposition.Transform, "replay-approved-values-and-substitute-runtime-values",
                    "Replay recognized business values while remapping identity-bound values and allowing reviewed target-runtime values to be regenerated.")
                : (IngredientCapability.Available, IngredientDisposition.Preserve, "replay-approved-current-values",
                    "Replay every nonempty captured value through an approved lossless field action.");
        }

        private static string MapListOwnedPath(
            ListDependencySnapshot source,
            ListMaterializationPlan plan,
            string sourcePath)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath)
                && !string.IsNullOrWhiteSpace(source.RootFolderServerRelativeUrl)
                && sourcePath.StartsWith(source.RootFolderServerRelativeUrl.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            {
                return plan.TargetRootFolderServerRelativeUrl.TrimEnd('/')
                    + sourcePath.Substring(source.RootFolderServerRelativeUrl.TrimEnd('/').Length);
            }
            return plan.TargetRootFolderServerRelativeUrl;
        }
    }
}
