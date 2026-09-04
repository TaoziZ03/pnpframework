using PnP.Framework.Migration.Taxonomy;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Taxonomy.Assets.Execution;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Taxonomy.Assets
{
    /// <summary>
    /// A page/schema-planning mapping catalog derived only from a sealed
    /// taxonomy approval and its successful, digest-sealed fresh-readback receipt.
    /// </summary>
    public sealed class TaxonomyAssetMappingCatalog
    {
        public string SchemaVersion { get; set; } = "pnp-taxonomy-asset-mapping-catalog/v2";

        public string ReviewPlanDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public string MaterializationReceiptDigest { get; set; }

        public Guid MaterializationOperationId { get; set; }

        public Guid TargetTermStoreId { get; set; }

        public DateTimeOffset GeneratedAtUtc { get; set; }

        public IList<TaxonomyTargetMapping> FieldBindings { get; set; } = new List<TaxonomyTargetMapping>();

        public IList<TaxonomyAssetMappingCatalogEntry> AssetMappings { get; set; } = new List<TaxonomyAssetMappingCatalogEntry>();

        public string CatalogDigest { get; set; }
    }

    public sealed class TaxonomyAssetMappingCatalogEntry
    {
        public string ActionId { get; set; }

        public TaxonomyAssetKind Kind { get; set; }

        public string SourceIdentity { get; set; }

        public string TargetIdentity { get; set; }

        public MigrationTargetOwnership Ownership { get; set; }

        public TaxonomyAssetReceiptDisposition Disposition { get; set; }

        public string SemanticMappingDigest { get; set; }

        public string ActionSignature { get; set; }

        public string ObservedStateDigest { get; set; }

        public bool FreshReadbackPassed { get; set; }
    }
}
