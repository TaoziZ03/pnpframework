using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageProtectedAssetScopeResolver
    {
        public static void Apply(
            IList<PageIngredientAction> actions,
            IDictionary<string, PageIngredientActionSelectionRequest[]> requests,
            string snapshotDigest,
            PublishingPageProtectedAssetContext context)
        {
            var assetId = context.IngredientId(PageIngredientKind.ProtectedAsset);
            var payloadId = context.IngredientId(PageIngredientKind.BinaryPayload);
            var asset = actions.Single(value => string.Equals(value.IngredientId, assetId, StringComparison.Ordinal));
            var payloadIndex = IndexOf(actions, payloadId);
            var payload = actions[payloadIndex];
            if (asset.SelectedAction.Action == IngredientSelectableAction.Exclude
                && asset.SelectedAction.Scope == IngredientActionScope.PayloadOnly)
            {
                payload = ApplyPayloadExclusion(actions, requests, snapshotDigest, context, asset, payloadIndex, payload);
            }
            if (asset.SelectedAction.Action != IngredientSelectableAction.Reproduce
                && payload.SelectedAction.Action != IngredientSelectableAction.Reproduce)
            {
                ForceEvidenceOnly(actions, requests, snapshotDigest, context, PageIngredientKind.DocumentIdentity, asset.SelectedAction);
                if (context.Kinds().Contains(PageIngredientKind.InformationProtectionRelationship))
                {
                    ForceEvidenceOnly(actions, requests, snapshotDigest, context, PageIngredientKind.InformationProtectionRelationship, asset.SelectedAction);
                }
            }
            var childIds = context.Kinds().Skip(1).Select(context.IngredientId).ToArray();
            if (asset.SelectedAction.Action == IngredientSelectableAction.Reproduce
                && asset.SelectedAction.Scope == IngredientActionScope.Subtree
                && actions.Where(value => childIds.Contains(value.IngredientId, StringComparer.Ordinal))
                    .Any(value => value.SelectedAction.Action != IngredientSelectableAction.Reproduce))
            {
                throw new InvalidDataException("The ProtectedAsset Subtree reproduction conflicts with a child ingredient selection.");
            }
        }

        private static PageIngredientAction ApplyPayloadExclusion(
            IList<PageIngredientAction> actions,
            IDictionary<string, PageIngredientActionSelectionRequest[]> requests,
            string snapshotDigest,
            PublishingPageProtectedAssetContext context,
            PageIngredientAction asset,
            int payloadIndex,
            PageIngredientAction payload)
        {
            if (requests.ContainsKey(payload.IngredientId) && payload.SelectedAction.Action != IngredientSelectableAction.Exclude)
            {
                throw new InvalidDataException("The ProtectedAsset PayloadOnly exclusion conflicts with an explicit BinaryPayload selection.");
            }
            if (payload.SelectedAction.Action == IngredientSelectableAction.Exclude)
            {
                return payload;
            }
            var replacement = SelectChild(
                payload,
                IngredientSelectableAction.Exclude,
                snapshotDigest,
                asset.SelectedAction,
                context.Document.ServerRelativeUrl,
                "Fresh target comparison requires this protected BinaryPayload path to be absent.");
            actions[payloadIndex] = replacement;
            return replacement;
        }

        private static void ForceEvidenceOnly(
            IList<PageIngredientAction> actions,
            IDictionary<string, PageIngredientActionSelectionRequest[]> requests,
            string snapshotDigest,
            PublishingPageProtectedAssetContext context,
            PageIngredientKind kind,
            PageIngredientSelectedAction parentSelection)
        {
            var ingredientId = context.IngredientId(kind);
            var index = IndexOf(actions, ingredientId);
            var current = actions[index];
            if (requests.ContainsKey(ingredientId) && current.SelectedAction.Action != IngredientSelectableAction.EvidenceOnly)
            {
                throw new InvalidDataException("The ProtectedAsset PayloadOnly exclusion conflicts with an explicit child reproduction selection.");
            }
            if (current.SelectedAction.Action != IngredientSelectableAction.EvidenceOnly)
            {
                actions[index] = SelectChild(
                    current,
                    IngredientSelectableAction.EvidenceOnly,
                    snapshotDigest,
                    parentSelection,
                    context.Document.ServerRelativeUrl,
                    "The sealed source metadata remains available without a target mutation claim.");
            }
        }

        private static PageIngredientAction SelectChild(
            PageIngredientAction current,
            IngredientSelectableAction selected,
            string snapshotDigest,
            PageIngredientSelectedAction parentSelection,
            string sourcePath,
            string assertion)
        {
            var candidate = current.CandidateActions.Single(value => value.Action == selected);
            var replacement = PageIngredientActionSelectionPolicy.Select(
                current.IngredientId,
                current.CandidateActions,
                new PageIngredientActionSelectionRequest
                {
                    IngredientId = current.IngredientId,
                    CandidateActionId = candidate.CandidateActionId,
                    SnapshotDigest = snapshotDigest,
                    SelectedBy = parentSelection.SelectedBy,
                    SelectedAtUtc = parentSelection.SelectedAtUtc,
                    ApprovalReference = parentSelection.ApprovalReference
                },
                snapshotDigest);
            replacement.TargetIdentity = sourcePath;
            replacement.VerificationAssertions.Add(assertion);
            return replacement;
        }

        private static int IndexOf(IList<PageIngredientAction> actions, string ingredientId)
        {
            return actions.Select((value, index) => new { value, index })
                .Single(value => string.Equals(value.value.IngredientId, ingredientId, StringComparison.Ordinal)).index;
        }
    }
}
