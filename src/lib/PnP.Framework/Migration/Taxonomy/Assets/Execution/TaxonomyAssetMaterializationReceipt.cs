using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Taxonomy.Assets.Execution
{
    public enum TaxonomyAssetOwnership
    {
        MigrationOwned = 1,
        External = 2
    }

    public enum TaxonomyAssetReceiptDisposition
    {
        CreatedOwned = 1,
        ReconciledOwned = 2,
        ReuseOwned = 3,
        ReuseExternal = 4
    }

    public sealed class TaxonomyAssetActionReceipt
    {
        public string ActionId { get; set; }

        public TaxonomyAssetKind Kind { get; set; }

        public Guid SourceTenantId { get; set; }

        public Guid SourceTermStoreId { get; set; }

        public Guid SourceTermSetId { get; set; }

        public Guid? SourceTermId { get; set; }

        public Guid TargetTermStoreId { get; set; }

        public Guid? TargetTermGroupId { get; set; }

        public Guid TargetTermSetId { get; set; }

        public Guid? TargetTermId { get; set; }

        public TaxonomyAssetTargetDisposition ReviewedDisposition { get; set; }

        public TaxonomyAssetTargetDisposition PreflightDisposition { get; set; }

        public TaxonomyAssetTargetDisposition FinalDisposition { get; set; }

        public bool ChangedTarget { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public TaxonomyAssetOwnership Ownership { get; set; }

        public TaxonomyAssetReceiptDisposition ExecutionDisposition { get; set; }

        public string SourceIdentity { get; set; }

        public string TargetIdentity { get; set; }

        public string SemanticMappingDigest { get; set; }

        public string ReviewPlanDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class TaxonomyAssetMaterializationReceipt
    {
        public string SchemaVersion { get; set; } = "pnp-taxonomy-asset-materialization-receipt/v2";

        public Guid OperationId { get; set; }

        public string ReviewPlanDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public Guid TargetTermStoreId { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public bool ChangedTarget { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public IList<string> DeferredActionIds { get; set; } = new List<string>();

        public IList<string> RejectedActionIds { get; set; } = new List<string>();

        public IList<TaxonomyAssetActionReceipt> Actions { get; set; } = new List<TaxonomyAssetActionReceipt>();

        public IList<string> Diagnostics { get; set; } = new List<string>();

        public string ReceiptDigest { get; set; }
    }

    public sealed class TaxonomyAssetMigrationExecutionResult
    {
        public Guid OperationId { get; set; }

        public TaxonomyAssetExecutionAdmission Admission { get; set; }

        public TaxonomyAssetMaterializationReceipt Receipt { get; set; }

        public TaxonomyAssetMappingCatalog MappingCatalog { get; set; }

        public IList<MigrationMutationReceipt> Steps { get; set; } = new List<MigrationMutationReceipt>();
    }
}
