using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal sealed class IngredientMaturityContributorCatalog
    {
        private static readonly ISet<string> KnownLanes = new HashSet<string>(StringComparer.Ordinal)
        {
            "content.text",
            "webpart.instance",
            "resource.image",
            "page.layout",
            "resource.script",
            "embed.iframe",
            "dynamic.region",
            "behavior.interaction"
        };

        private readonly IReadOnlyDictionary<string, IIngredientMaturityContributor> byLane;

        public IngredientMaturityContributorCatalog(IEnumerable<IIngredientMaturityContributor> contributors)
        {
            var values = (contributors ?? throw new ArgumentNullException(nameof(contributors)))
                .Select(value => value ?? throw new ArgumentException("The maturity contributor catalog cannot contain null entries.", nameof(contributors)))
                .OrderBy(value => value.Lane, StringComparer.Ordinal)
                .ToArray();
            var invalid = values.FirstOrDefault(value => string.IsNullOrWhiteSpace(value.ContributorId)
                || string.IsNullOrWhiteSpace(value.Lane)
                || !KnownLanes.Contains(value.Lane));
            if (invalid != null)
            {
                throw new ArgumentException("Every maturity contributor requires a stable ID and one recognized lane.", nameof(contributors));
            }
            var duplicateId = values.GroupBy(value => value.ContributorId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() != 1);
            var duplicateLane = values.GroupBy(value => value.Lane, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() != 1);
            if (duplicateId != null || duplicateLane != null)
            {
                throw new ArgumentException("Maturity contributor IDs and lanes must be unique.", nameof(contributors));
            }

            Contributors = new ReadOnlyCollection<IIngredientMaturityContributor>(values);
            byLane = new ReadOnlyDictionary<string, IIngredientMaturityContributor>(
                values.ToDictionary(value => value.Lane, StringComparer.Ordinal));
        }

        public IReadOnlyList<IIngredientMaturityContributor> Contributors { get; }

        internal static bool IsKnownLane(string lane)
        {
            return lane != null && KnownLanes.Contains(lane);
        }

        public IIngredientMaturityContributor Resolve(string lane)
        {
            if (string.IsNullOrWhiteSpace(lane) || !byLane.TryGetValue(lane, out var contributor))
            {
                throw new InvalidDataException($"No maturity contributor is registered for lane '{lane}'.");
            }
            return contributor;
        }
    }
}
