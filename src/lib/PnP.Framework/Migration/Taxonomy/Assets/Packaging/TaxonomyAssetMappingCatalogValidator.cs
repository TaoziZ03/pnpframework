using PnP.Framework.Migration.Taxonomy;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Taxonomy.Assets.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Taxonomy.Assets.Packaging
{
    public static class TaxonomyAssetMappingCatalogValidator
    {
        public static void Validate(TaxonomyAssetMappingCatalog catalog, bool requireDigest = true)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }
            var errors = new List<string>();
            if (!string.Equals(catalog.SchemaVersion, "pnp-taxonomy-asset-mapping-catalog/v2", StringComparison.Ordinal))
            {
                errors.Add("Unsupported taxonomy asset mapping-catalog schema.");
            }
            if (!IsSha256(catalog.ReviewPlanDigest)
                || !IsSha256(catalog.ApprovalDigest)
                || !IsSha256(catalog.MaterializationReceiptDigest)
                || catalog.MaterializationOperationId == Guid.Empty
                || catalog.TargetTermStoreId == Guid.Empty
                || catalog.GeneratedAtUtc == default(DateTimeOffset))
            {
                errors.Add("The taxonomy mapping catalog lacks its reviewed-plan, approval, receipt, operation, target, or generation boundary.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in catalog.FieldBindings ?? new List<TaxonomyTargetMapping>())
            {
                var key = mapping == null
                    ? null
                    : mapping.SourceTermStoreId.ToString("D") + "/" + mapping.SourceTermSetId.ToString("D");
                if (mapping == null
                    || mapping.SourceTermStoreId == Guid.Empty
                    || mapping.SourceTermSetId == Guid.Empty
                    || mapping.TargetTermStoreId != catalog.TargetTermStoreId
                    || mapping.TargetTermSetId == Guid.Empty
                    || (mapping.Mode == TaxonomyTargetMappingMode.PreserveUnresolvedSourceReference
                        && (!mapping.UnresolvedReferenceTargetVerifiedAbsent
                            || !IsSha256(mapping.UnresolvedReferenceEvidenceSha256)))
                    || !keys.Add(key))
                {
                    errors.Add("A taxonomy mapping catalog entry is null, duplicate, or has an invalid source/target identity.");
                }
            }
            var actionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in catalog.AssetMappings ?? new List<TaxonomyAssetMappingCatalogEntry>())
            {
                if (mapping == null
                    || string.IsNullOrWhiteSpace(mapping.ActionId)
                    || !Enum.IsDefined(typeof(TaxonomyAssetKind), mapping.Kind)
                    || string.IsNullOrWhiteSpace(mapping.SourceIdentity)
                    || string.IsNullOrWhiteSpace(mapping.TargetIdentity)
                    || !Enum.IsDefined(typeof(MigrationTargetOwnership), mapping.Ownership)
                    || !Enum.IsDefined(typeof(TaxonomyAssetReceiptDisposition), mapping.Disposition)
                    || !IsSha256(mapping.SemanticMappingDigest)
                    || !IsSha256(mapping.ActionSignature)
                    || !IsSha256(mapping.ObservedStateDigest)
                    || !mapping.FreshReadbackPassed
                    || mapping.Ownership == MigrationTargetOwnership.External
                        && mapping.Disposition != TaxonomyAssetReceiptDisposition.ReuseExternal
                    || !actionIds.Add(mapping.ActionId))
                {
                    errors.Add("A taxonomy asset mapping entry is null, duplicate, unverified, or has invalid ownership/signature evidence.");
                }
            }
            if (requireDigest && (!IsSha256(catalog.CatalogDigest)
                || !string.Equals(
                    catalog.CatalogDigest,
                    TaxonomyAssetMappingCatalogFactory.ComputeDigest(catalog),
                    StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("The taxonomy asset mapping-catalog digest is absent or invalid.");
            }
            if (errors.Count > 0)
            {
                throw new InvalidDataException("Invalid taxonomy asset mapping catalog: " + string.Join(" ", errors));
            }
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 64
                && value.All(character => character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F');
        }
    }
}
