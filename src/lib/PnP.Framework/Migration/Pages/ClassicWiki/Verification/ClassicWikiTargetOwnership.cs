using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public static class ClassicWikiTargetOwnership
    {
        public const string OriginalIdentifierPropertyName = "pnp_reserved_page_original_identifier";

        public const string SourceSnapshotDigestPropertyName = "pnp_reserved_page_source_snapshot_digest";

        public const string PlanDigestPropertyName = "pnp_reserved_page_migration_digest";

        public static bool MatchesApprovedPlan(
            IDictionary<string, object> properties,
            string originalIdentifier,
            string sourceSnapshotDigest,
            string planDigest)
        {
            if (properties == null) return false;

            return Matches(properties, OriginalIdentifierPropertyName, originalIdentifier, StringComparison.Ordinal)
                && Matches(properties, SourceSnapshotDigestPropertyName, sourceSnapshotDigest, StringComparison.OrdinalIgnoreCase)
                && Matches(properties, PlanDigestPropertyName, planDigest, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Matches(
            IDictionary<string, object> properties,
            string key,
            string expected,
            StringComparison comparison)
        {
            if (string.IsNullOrWhiteSpace(expected)) return false;
            if (properties.TryGetValue(key, out var value) && value != null)
            {
                return string.Equals(value.ToString(), expected, comparison);
            }
            return false;
        }
    }
}
