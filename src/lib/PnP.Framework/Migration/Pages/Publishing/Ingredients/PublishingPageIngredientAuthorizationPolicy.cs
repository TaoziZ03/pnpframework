using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Fields.Taxonomy;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    /// <summary>
    /// Establishes the only final ingredient-action boundary that may emit Block:
    /// retained literal wire HTTP 401/403 evidence. Domain planners may use their
    /// own local Block disposition, but that is normalized to Defer before this
    /// policy is applied.
    /// </summary>
    internal static class PublishingPageIngredientAuthorizationPolicy
    {
        public static IReadOnlyDictionary<string, LiteralHttpAuthorizationEvidence> GetEvidence(
            PublishingPageCaptureBundle snapshot)
        {
            return GetEvidence(snapshot, null);
        }

        public static IReadOnlyDictionary<string, LiteralHttpAuthorizationEvidence> GetEvidence(
            PublishingPageCaptureBundle snapshot,
            PublishingPageMigrationPlan plan)
        {
            var result = new Dictionary<string, LiteralHttpAuthorizationEvidence>(StringComparer.Ordinal);
            var layoutEvidence = snapshot?.Layout?.AuthorizationEvidence;
            if (layoutEvidence != null)
            {
                LiteralHttpAuthorizationEvidence.Validate(layoutEvidence);
                Add(result, PublishingPageIngredientIds.Layout, layoutEvidence);
                Add(result, PublishingPageIngredientIds.ContentType, layoutEvidence);
            }

            foreach (var field in (snapshot?.Fields ?? Array.Empty<PageFieldValueSnapshot>())
                         .Where(value => value?.AuthorizationEvidence != null))
            {
                PageTaxonomyFieldAuthorizationEvidence.ValidateSource(snapshot.Source, field);
                Add(
                    result,
                    PublishingPageIngredientIds.Field(field.InternalName),
                    field.AuthorizationEvidence.LiteralEvidence);
            }

            var snapshotById = (snapshot?.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Id))
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (snapshotById.Any(value => value.Value.Length != 1))
            {
                throw new InvalidDataException("Reference authorization evidence cannot bind a duplicate source dependency ID.");
            }
            foreach (var values in snapshotById.Values)
            {
                var reference = values[0];
                if (reference.AuthorizationEvidence == null)
                {
                    continue;
                }
                PageReferenceAuthorizationEvidence.ValidateSource(snapshot.Source, reference);
                Add(result, PublishingPageIngredientIds.Reference(reference.Id), reference.AuthorizationEvidence);
            }

            if (plan?.TargetProbe?.ReferenceVerifications == null)
            {
                return result;
            }
            var actionById = (plan.DependencyActions ?? Array.Empty<PageReferenceAction>())
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.SnapshotDependencyId))
                .GroupBy(value => value.SnapshotDependencyId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (actionById.Any(value => value.Value.Length != 1))
            {
                throw new InvalidDataException("Reference authorization evidence cannot bind a duplicate dependency action ID.");
            }
            foreach (var verification in plan.TargetProbe.ReferenceVerifications.Where(value =>
                         value?.TargetRead?.AuthorizationEvidence != null))
            {
                if (!snapshotById.TryGetValue(verification.SnapshotDependencyId ?? string.Empty, out var references)
                    || !actionById.TryGetValue(verification.SnapshotDependencyId ?? string.Empty, out var actions))
                {
                    throw new InvalidDataException(
                        $"Target authorization evidence references unknown dependency '{verification.SnapshotDependencyId}'.");
                }
                var reference = references[0];
                var action = actions[0];
                PageReferenceAuthorizationEvidence.ValidateTarget(
                    plan.TargetWebUrl,
                    action,
                    verification.TargetRead);
                Add(
                    result,
                    PublishingPageIngredientIds.Reference(reference.Id),
                    verification.TargetRead.AuthorizationEvidence);
            }
            return result;
        }

        public static void Apply(
            PublishingPageCaptureBundle snapshot,
            PublishingPageMigrationPlan plan,
            IDictionary<string, PageIngredientAction> actions)
        {
            foreach (var pair in GetEvidence(snapshot, plan))
            {
                if (actions == null || !actions.TryGetValue(pair.Key, out var action) || action == null)
                {
                    throw new InvalidDataException(
                        $"Literal authorization evidence references unknown ingredient '{pair.Key}'.");
                }

                var evidence = pair.Value;
                action.Capability = IngredientCapability.Missing;
                action.Disposition = IngredientDisposition.Block;
                action.Realization = "none";
                action.PolicyId = "policy.authorization.literal-http";
                action.Reason = $"Ingredient request '{evidence.Operation}' returned literal HTTP {evidence.HttpStatusCode}.";
                action.VerificationAssertions = (action.VerificationAssertions ?? new List<string>())
                    .Concat(new[]
                    {
                        $"Authorization evidence has SHA-256 '{evidence.EvidenceSha256}'."
                    })
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        private static void Add(
            IDictionary<string, LiteralHttpAuthorizationEvidence> evidenceByIngredient,
            string ingredientId,
            LiteralHttpAuthorizationEvidence evidence)
        {
            LiteralHttpAuthorizationEvidence.Validate(evidence);
            if (!evidenceByIngredient.TryGetValue(ingredientId, out var existing))
            {
                evidenceByIngredient.Add(ingredientId, evidence);
                return;
            }
            if (existing.HttpStatusCode != evidence.HttpStatusCode
                || !string.Equals(existing.Operation, evidence.Operation, StringComparison.Ordinal)
                || !string.Equals(existing.RequestUri, evidence.RequestUri, StringComparison.Ordinal)
                || !string.Equals(existing.EvidenceSha256, evidence.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Conflicting literal authorization evidence exists for ingredient '{ingredientId}'.");
            }
        }
    }
}
