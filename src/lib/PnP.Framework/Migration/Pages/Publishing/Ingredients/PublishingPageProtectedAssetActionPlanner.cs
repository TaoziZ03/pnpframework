using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageProtectedAssetActionPlanner
    {
        public static ProtectedAssetActionPlan Create(
            PublishingPageCaptureBundle snapshot,
            string snapshotDigest,
            IEnumerable<PageIngredientActionSelectionRequest> selections)
        {
            return Create(snapshot, snapshotDigest, selections, null);
        }

        public static ProtectedAssetActionPlan Create(
            PublishingPageCaptureBundle snapshot,
            string snapshotDigest,
            IEnumerable<PageIngredientActionSelectionRequest> selections,
            PageIngredientSelectionAudit defaultAudit)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (string.IsNullOrWhiteSpace(snapshotDigest))
            {
                throw new ArgumentException("A source snapshot digest is required.", nameof(snapshotDigest));
            }

            var requests = SelectionRequests(selections);
            var actions = new List<PageIngredientAction>();
            foreach (var context in PublishingPageProtectedAssetContext.Create(snapshot))
            {
                foreach (var kind in context.Kinds())
                {
                    actions.Add(Select(context, kind, requests, snapshotDigest, defaultAudit));
                }
                PublishingPageProtectedAssetScopeResolver.Apply(actions, requests, snapshotDigest, context);
            }

            var knownIds = new HashSet<string>(actions.Select(value => value.IngredientId), StringComparer.Ordinal);
            var orphan = requests.Keys.FirstOrDefault(value => !knownIds.Contains(value));
            if (orphan != null)
            {
                throw new InvalidDataException("The protected-asset selection references an ingredient that is not present in the source snapshot: " + orphan);
            }
            return new ProtectedAssetActionPlan
            {
                SourceSnapshotDigest = snapshotDigest,
                Actions = actions.OrderBy(value => value.IngredientId, StringComparer.Ordinal).ToList()
            };
        }

        public static void Validate(
            PublishingPageCaptureBundle snapshot,
            string snapshotDigest,
            ProtectedAssetActionPlan plan)
        {
            Validate(snapshot, snapshotDigest, plan, null, null);
        }

        public static void Validate(
            PublishingPageCaptureBundle snapshot,
            string snapshotDigest,
            ProtectedAssetActionPlan plan,
            IEnumerable<PageIngredientActionSelectionRequest> selections,
            PageIngredientSelectionAudit defaultAudit)
        {
            var contexts = PublishingPageProtectedAssetContext.Create(snapshot).ToList();
            if (contexts.Count == 0)
            {
                ValidateEmptyPlan(plan, snapshotDigest);
                return;
            }
            if (plan == null
                || !string.Equals(plan.SchemaVersion, ProtectedAssetActionPlan.ContractVersion, StringComparison.Ordinal)
                || !string.Equals(plan.SourceSnapshotDigest, snapshotDigest, StringComparison.OrdinalIgnoreCase)
                || plan.Actions == null)
            {
                throw new InvalidDataException("The protected-asset action plan is missing, stale, or uses an unsupported schema.");
            }

            var expected = contexts.SelectMany(value => value.Kinds().Select(kind => new
            {
                Context = value,
                Kind = kind,
                Id = value.IngredientId(kind)
            })).ToDictionary(value => value.Id, StringComparer.Ordinal);
            if (plan.Actions.Any(value => value == null || string.IsNullOrWhiteSpace(value.IngredientId)))
            {
                throw new InvalidDataException("The protected-asset action plan contains a null or unnamed action.");
            }
            var actual = plan.Actions.GroupBy(value => value.IngredientId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (actual.Any(value => value.Value.Length != 1)
                || expected.Count != actual.Count
                || !new HashSet<string>(expected.Keys, StringComparer.Ordinal).SetEquals(actual.Keys))
            {
                throw new InvalidDataException("The protected-asset action plan does not cover each protected asset ingredient exactly once.");
            }

            foreach (var value in expected.Values)
            {
                PageIngredientActionSelectionPolicy.Validate(
                    actual[value.Id][0],
                    PublishingPageProtectedAssetCandidatePolicy.Candidates(value.Context, value.Kind),
                    snapshotDigest);
            }
            var derived = Create(snapshot, snapshotDigest, selections, defaultAudit);
            if (!string.Equals(
                MigrationContractSerializer.SerializeCanonical(derived),
                MigrationContractSerializer.SerializeCanonical(plan),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("The protected-asset selections and receipts do not match the sealed planning inputs and active policy.");
            }
        }

        private static PageIngredientAction Select(
            PublishingPageProtectedAssetContext context,
            PageIngredientKind kind,
            IDictionary<string, PageIngredientActionSelectionRequest[]> requests,
            string snapshotDigest,
            PageIngredientSelectionAudit defaultAudit)
        {
            var ingredientId = context.IngredientId(kind);
            PageIngredientActionSelectionRequest[] request;
            var action = PageIngredientActionSelectionPolicy.Select(
                ingredientId,
                PublishingPageProtectedAssetCandidatePolicy.Candidates(context, kind),
                requests.TryGetValue(ingredientId, out request) ? request[0] : null,
                snapshotDigest,
                defaultAudit);
            action.TargetIdentity = context.Document.ServerRelativeUrl;
            action.VerificationAssertions.Add(action.SelectionReceipt.ComparisonRule == IngredientComparisonRule.ExpectedAbsent
                ? "Fresh target comparison requires this protected BinaryPayload path to be absent."
                : action.SelectionReceipt.ComparisonRule == IngredientComparisonRule.EvidenceOnly
                    ? "The sealed source metadata remains available without a target mutation claim."
                    : "Fresh target comparison verifies the selected protected-asset fidelity action.");
            return action;
        }

        private static IDictionary<string, PageIngredientActionSelectionRequest[]> SelectionRequests(
            IEnumerable<PageIngredientActionSelectionRequest> selections)
        {
            var values = (selections ?? Enumerable.Empty<PageIngredientActionSelectionRequest>()).ToList();
            if (values.Any(value => value == null || string.IsNullOrWhiteSpace(value.IngredientId)))
            {
                throw new InvalidDataException("Protected-asset selections must be non-null and name an ingredient.");
            }
            var requests = values.GroupBy(value => value.IngredientId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (requests.Any(value => value.Value.Length != 1))
            {
                throw new InvalidDataException("Protected-asset selections must name one unique ingredient each.");
            }
            return requests;
        }

        private static void ValidateEmptyPlan(ProtectedAssetActionPlan plan, string snapshotDigest)
        {
            if (plan == null
                || !string.Equals(plan.SchemaVersion, ProtectedAssetActionPlan.ContractVersion, StringComparison.Ordinal)
                || plan.Actions == null
                || plan.Actions.Count != 0
                || !string.IsNullOrWhiteSpace(plan.SourceSnapshotDigest)
                    && !string.Equals(plan.SourceSnapshotDigest, snapshotDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A snapshot without protected assets must have an empty protected-asset action plan.");
            }
        }
    }
}
