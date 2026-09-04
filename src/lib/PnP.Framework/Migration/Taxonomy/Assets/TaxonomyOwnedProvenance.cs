using System;

namespace PnP.Framework.Migration.Taxonomy.Assets
{
    internal enum TaxonomyOwnedProvenanceState
    {
        NotOwned = 1,
        Exact = 2,
        MappingDigestMissing = 3,
        MappingDigestConflict = 4
    }

    internal static class TaxonomyOwnedProvenance
    {
        public static TaxonomyOwnedProvenanceState Evaluate(
            string existingOriginalIdentifier,
            string expectedOriginalIdentifier,
            string existingMappingDigest,
            string expectedMappingDigest)
        {
            if (!string.Equals(existingOriginalIdentifier, expectedOriginalIdentifier, StringComparison.Ordinal))
            {
                return TaxonomyOwnedProvenanceState.NotOwned;
            }
            if (string.IsNullOrWhiteSpace(existingMappingDigest))
            {
                return TaxonomyOwnedProvenanceState.MappingDigestMissing;
            }
            return string.Equals(existingMappingDigest, expectedMappingDigest, StringComparison.OrdinalIgnoreCase)
                ? TaxonomyOwnedProvenanceState.Exact
                : TaxonomyOwnedProvenanceState.MappingDigestConflict;
        }
    }
}
