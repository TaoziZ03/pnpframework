using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PageIngredientHandlerDescriptor
    {
        public PageIngredientHandlerDescriptor(
            string handlerId,
            PageIngredientLaneDescriptor lane,
            IEnumerable<string> evidenceSchemaVersions,
            string introducedProjectionVersion,
            int orderGroup,
            IEnumerable<PageIngredientIdOwnership> ownedIngredientIds)
        {
            if (string.IsNullOrWhiteSpace(handlerId))
            {
                throw new ArgumentException("A stable ingredient handler ID is required.", nameof(handlerId));
            }
            if (string.IsNullOrWhiteSpace(introducedProjectionVersion))
            {
                throw new ArgumentException("An introduction projection version is required.", nameof(introducedProjectionVersion));
            }

            HandlerId = handlerId;
            Lane = lane ?? throw new ArgumentNullException(nameof(lane));
            EvidenceSchemaVersions = new ReadOnlyCollection<string>(
                (evidenceSchemaVersions ?? throw new ArgumentNullException(nameof(evidenceSchemaVersions)))
                    .Select(value => value?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
            if (EvidenceSchemaVersions.Count == 0)
            {
                throw new ArgumentException("At least one evidence schema version is required.", nameof(evidenceSchemaVersions));
            }

            IntroducedProjectionVersion = introducedProjectionVersion;
            OrderGroup = orderGroup;
            OwnedIngredientIds = new ReadOnlyCollection<PageIngredientIdOwnership>(
                (ownedIngredientIds ?? throw new ArgumentNullException(nameof(ownedIngredientIds)))
                    .Where(value => value != null)
                    .OrderBy(value => value.Value, StringComparer.Ordinal)
                    .ThenBy(value => value.Kind)
                    .ToArray());
            if (OwnedIngredientIds.Count == 0)
            {
                throw new ArgumentException("At least one owned ingredient ID predicate is required.", nameof(ownedIngredientIds));
            }
            for (var leftIndex = 0; leftIndex < OwnedIngredientIds.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < OwnedIngredientIds.Count; rightIndex++)
                {
                    if (OwnedIngredientIds[leftIndex].Overlaps(OwnedIngredientIds[rightIndex]))
                    {
                        throw new ArgumentException(
                            $"Handler '{handlerId}' declares overlapping ingredient ownership at '{OwnedIngredientIds[leftIndex].Value}'.",
                            nameof(ownedIngredientIds));
                    }
                }
            }
        }

        public string HandlerId { get; }

        public PageIngredientLaneDescriptor Lane { get; }

        public IReadOnlyList<string> EvidenceSchemaVersions { get; }

        public string IntroducedProjectionVersion { get; }

        public int OrderGroup { get; }

        public IReadOnlyList<PageIngredientIdOwnership> OwnedIngredientIds { get; }

        internal bool Owns(string ingredientId)
        {
            return OwnedIngredientIds.Any(value => value.Matches(ingredientId));
        }
    }
}
