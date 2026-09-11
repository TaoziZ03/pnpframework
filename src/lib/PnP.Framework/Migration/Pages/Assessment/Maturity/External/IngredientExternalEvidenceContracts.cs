using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.External
{
    // Internal evidence intake only. None of these types is a plan, execution
    // receipt, native acceptance authority, or a replacement public contract.
    internal static class IngredientExternalEvidenceContract
    {
        public const string EnvelopeSchema = "pnp-ingredient-external-evidence/v1";
        public const string AdmissionSchema = "pnp-ingredient-external-evidence-admission/v1";
        public const string BindingSchema = "pnp-ingredient-external-plan-binding/v1";
        public const string ReceiptSetSchema = "pnp-ingredient-external-receipt-set/v1";
        public const string BatchPlanSchema = "ccd.batch1-repro-plan/v1";
        public const string LifecycleInputSchema = "ccd.shared-target-lifecycle-input/v1";
        public const string LifecycleReceiptSchema = "ccd.shared-target-lifecycle-receipt/v2";
    }

    internal sealed class IngredientExternalEvidenceAdmission
    {
        public string SchemaVersion { get; set; } = IngredientExternalEvidenceContract.AdmissionSchema;
        public string PlanArtifactSha256 { get; set; }
        public string PlanDigest { get; set; }
        public string PlanProducerCommit { get; set; }
        public string BindingArtifactSha256 { get; set; }
        public string ReceiptSetArtifactSha256 { get; set; }
        public DateTimeOffset ExecutionWindowStartUtc { get; set; }
        public DateTimeOffset ExecutionWindowEndUtc { get; set; }
        public IngredientExternalTargetIdentity Target { get; set; }
    }

    internal sealed class IngredientExternalPlanEvidence
    {
        public string SchemaVersion { get; set; } = IngredientExternalEvidenceContract.EnvelopeSchema;
        public ArtifactReference PlanArtifact { get; set; }
        public ArtifactReference BindingArtifact { get; set; }
        public IMigrationArtifactStore ArtifactStore { get; set; }
    }

    internal sealed class IngredientExternalOperationalEvidence
    {
        public string SchemaVersion { get; set; } = IngredientExternalEvidenceContract.EnvelopeSchema;
        public IngredientExternalPlanEvidence PlanEvidence { get; set; }
        public ArtifactReference ReceiptSetArtifact { get; set; }
    }

    internal sealed class IngredientExternalPageIdentity
    {
        public string PageUrl { get; set; }
        public string FileServerRelativeUrl { get; set; }
        public string ListId { get; set; }
        public int ItemId { get; set; }
        public string FileUniqueId { get; set; }
        public string ETag { get; set; }
    }

    internal sealed class IngredientExternalTargetIdentity
    {
        public string SiteId { get; set; }
        public string WebId { get; set; }
        public string ListId { get; set; }
        public string FileUniqueId { get; set; }
        public int ItemId { get; set; }
        public string ETag { get; set; }
    }

    internal sealed class IngredientExternalProducerReference
    {
        public string Kind { get; set; }
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }

    // A separately admitted annex supplies what a page-only external plan does
    // not contain. It never changes the external plan's bytes or digest. The
    // graph/actions are the existing PnP types, supplied by their domain owners.
    internal sealed class IngredientExternalPlanBinding
    {
        public string SchemaVersion { get; set; } = IngredientExternalEvidenceContract.BindingSchema;
        public IngredientMaturityIdentity Identity { get; set; }
        public IngredientMaturitySourceBinding Source { get; set; }
        public IngredientMaturityTargetBinding Target { get; set; }
        public IngredientMaturityProducerBinding Producer { get; set; }
        public string PlanSchema { get; set; }
        public string PlanArtifactSha256 { get; set; }
        public string PlanDigest { get; set; }
        public string PlanProducerCommit { get; set; }
        public ArtifactReference SourceSnapshotArtifact { get; set; }
        public IngredientExternalPageIdentity SourcePage { get; set; }
        public string ExternalPlanOperationId { get; set; }
        public string PlannedTargetUrl { get; set; }
        public string TargetOrigin { get; set; }
        public string TargetPath { get; set; }
        public string TargetWebPath { get; set; }
        public string TargetSitePath { get; set; }
        public string TargetListPath { get; set; }
        public ArtifactReference LifecycleInputArtifact { get; set; }
        public string LifecycleDigest { get; set; }
        public string TargetMappingDigest { get; set; }
        public string RunId { get; set; }
        public string RowId { get; set; }
        public int RowNumber { get; set; }
        public string NativeOperationId { get; set; }
        public string NativeActionId { get; set; }
        public string OwnershipMarker { get; set; }
        public IngredientExternalProducerReference LifecycleProducer { get; set; }
        public DateTimeOffset AdmittedAtUtc { get; set; }
        public CanonicalPageIngredientGraph Graph { get; set; }
        public IList<PageIngredientAction> Actions { get; set; } = new List<PageIngredientAction>();
        public MigrationActionSignature ActionSignature { get; set; }
        public RuntimeVerificationManifest RuntimeManifest { get; set; }
    }

    // A manifest of original artifacts, not another lifecycle journal. Ordered
    // phases are recovered and validated from the producer's original receipts.
    internal sealed class IngredientExternalReceiptSet
    {
        public string SchemaVersion { get; set; } = IngredientExternalEvidenceContract.ReceiptSetSchema;
        public string BindingArtifactSha256 { get; set; }
        public IList<ArtifactReference> Receipts { get; set; } = new List<ArtifactReference>();
    }
}
