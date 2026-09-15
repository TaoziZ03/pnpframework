using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientPrimaryOwnerRegistry
    {
        public const string SchemaVersion = "pnp-page-ingredient-primary-owner-registry/v1";

        private readonly IReadOnlyDictionary<string, Func<IngredientOwnershipSourceContext, bool>> predicates;

        public PublishingPageIngredientPrimaryOwnerRegistry(
            IEnumerable<PageIngredientPrimaryOwnerDescriptor> entries,
            IReadOnlyDictionary<string, Func<IngredientOwnershipSourceContext, bool>> sourcePredicates)
        {
            var frozenEntries = (entries ?? throw new ArgumentNullException(nameof(entries)))
                .Select(value => value ?? throw new ArgumentException("The primary-owner registry cannot contain null entries.", nameof(entries)))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
            var duplicate = frozenEntries.GroupBy(value => value.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException($"Duplicate primary-owner registry entry ID '{duplicate.Key}'.", nameof(entries));
            }

            var suppliedPredicates = sourcePredicates
                ?? throw new ArgumentNullException(nameof(sourcePredicates));
            var missingPredicate = frozenEntries.FirstOrDefault(value =>
                !suppliedPredicates.TryGetValue(value.SourcePredicateId, out var predicate) || predicate == null);
            if (missingPredicate != null)
            {
                throw new ArgumentException(
                    $"Primary-owner entry '{missingPredicate.Id}' has unbound source predicate '{missingPredicate.SourcePredicateId}'.",
                    nameof(sourcePredicates));
            }

            Entries = new ReadOnlyCollection<PageIngredientPrimaryOwnerDescriptor>(frozenEntries);
            predicates = new ReadOnlyDictionary<string, Func<IngredientOwnershipSourceContext, bool>>(
                suppliedPredicates.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
        }

        public IReadOnlyList<PageIngredientPrimaryOwnerDescriptor> Entries { get; }

        public PageIngredientPrimaryOwnerDescriptor Resolve(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            if (node == null)
            {
                throw new InvalidDataException("A primary-owner claim requires an ingredient node.");
            }

            var context = new IngredientOwnershipSourceContext(snapshot, node);
            // The minimal registry resolves already-bound enum-kind claims. KindId is
            // additive wire data, not a second, implicitly trusted kind catalogue.
            if (!Enum.IsDefined(typeof(PageIngredientKind), node.Kind) || node.KindId != null
                || !context.HasBoundSourceIdentity)
            {
                throw new InvalidDataException(
                    "Primary ownership requires a known enum kind and bound source identity/version; string KindId resolution is not supported.");
            }

            var matches = Entries.Where(value => value.MatchesTuple(node)
                    && predicates[value.SourcePredicateId](context))
                .ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidDataException(
                    $"Ingredient '{node.Id}' has no primary owner for tuple "
                    + $"({node.Kind}, {node.Subtype}, {node.SemanticRole}, {node.SourcePredicateId}).");
            }
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Ingredient '{node.Id}' has overlapping primary-owner predicates: "
                    + string.Join(", ", matches.Select(value => value.Id)) + ".");
            }
            if (!string.Equals(node.PrimaryOwnerLane, matches[0].PrimaryOwnerLane, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Ingredient '{node.Id}' declares a different or missing primary owner lane.");
            }
            return matches[0];
        }
    }
}
