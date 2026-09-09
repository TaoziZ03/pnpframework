using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PageIngredientLaneDescriptor
    {
        public PageIngredientLaneDescriptor(string laneId, IEnumerable<string> supportedPageFamilies)
        {
            if (string.IsNullOrWhiteSpace(laneId))
            {
                throw new ArgumentException("A stable ingredient lane ID is required.", nameof(laneId));
            }

            LaneId = laneId;
            SupportedPageFamilies = new ReadOnlyCollection<string>(
                (supportedPageFamilies ?? throw new ArgumentNullException(nameof(supportedPageFamilies)))
                    .Select(value => value?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
            if (SupportedPageFamilies.Count == 0)
            {
                throw new ArgumentException("At least one supported page family is required.", nameof(supportedPageFamilies));
            }
        }

        public string LaneId { get; }

        public IReadOnlyList<string> SupportedPageFamilies { get; }
    }
}
