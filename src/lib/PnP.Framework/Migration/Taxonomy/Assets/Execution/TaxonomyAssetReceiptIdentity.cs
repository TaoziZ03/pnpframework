using PnP.Framework.Migration.Packaging;
using System;

namespace PnP.Framework.Migration.Taxonomy.Assets.Execution
{
    internal static class TaxonomyAssetReceiptIdentity
    {
        public static void Populate(
            TaxonomyAssetActionReceipt receipt,
            TaxonomyAssetActionApproval approval,
            string reviewPlanDigest,
            string approvalDigest)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            if (approval == null)
            {
                throw new ArgumentNullException(nameof(approval));
            }
            receipt.Ownership = approval.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse
                ? TaxonomyAssetOwnership.External
                : TaxonomyAssetOwnership.MigrationOwned;
            receipt.ExecutionDisposition = ExecutionDisposition(approval, receipt.ChangedTarget);
            receipt.SourceIdentity = SourceIdentity(approval);
            receipt.TargetIdentity = TargetIdentity(approval);
            receipt.SemanticMappingDigest = SemanticMappingDigest(approval);
            receipt.ReviewPlanDigest = reviewPlanDigest;
            receipt.ApprovalDigest = approvalDigest;
        }

        public static string SemanticMappingDigest(TaxonomyAssetActionApproval action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-taxonomy-approved-asset-mapping/v1",
                actionId = action.ActionId,
                kind = action.Kind,
                sourceTenantId = action.SourceTenantId,
                sourceTermStoreId = action.SourceTermStoreId,
                sourceTermSetId = action.SourceTermSetId,
                sourceTermId = action.SourceTermId,
                targetTermStoreId = action.TargetTermStoreId,
                targetTermGroupId = action.TargetTermGroupId,
                targetTermSetId = action.TargetTermSetId,
                targetTermId = action.TargetTermId,
                reviewedDisposition = action.ReviewedDisposition
            }));
        }

        public static string SourceIdentity(TaxonomyAssetActionApproval action)
        {
            if (action.Kind == TaxonomyAssetKind.TermGroup)
            {
                return TaxonomyAssetIdentity.TermGroup(new TaxonomyTermGroupSourceIdentity
                {
                    TenantId = action.SourceTenantId,
                    TermStoreId = action.SourceTermStoreId
                });
            }
            if (action.Kind == TaxonomyAssetKind.TermSet)
            {
                return TaxonomyAssetIdentity.TermSet(new TaxonomyTermSetSourceIdentity
                {
                    TenantId = action.SourceTenantId,
                    TermStoreId = action.SourceTermStoreId,
                    TermSetId = action.SourceTermSetId
                });
            }
            return TaxonomyAssetIdentity.Term(new TaxonomyTermSourceIdentity
            {
                TenantId = action.SourceTenantId,
                TermStoreId = action.SourceTermStoreId,
                TermSetId = action.SourceTermSetId,
                TermId = action.SourceTermId.GetValueOrDefault()
            });
        }

        public static string TargetIdentity(TaxonomyAssetActionApproval action)
        {
            if (action.Kind == TaxonomyAssetKind.TermGroup)
            {
                return "urn:pnp:spo-target-termgroup:v1:"
                    + action.TargetTermStoreId.ToString("N") + ":"
                    + action.TargetTermGroupId.GetValueOrDefault().ToString("N");
            }
            if (action.Kind == TaxonomyAssetKind.TermSet)
            {
                return "urn:pnp:spo-target-termset:v1:"
                    + action.TargetTermStoreId.ToString("N") + ":"
                    + action.TargetTermSetId.ToString("N");
            }
            return "urn:pnp:spo-target-term:v1:"
                + action.TargetTermStoreId.ToString("N") + ":"
                + action.TargetTermSetId.ToString("N") + ":"
                + action.TargetTermId.GetValueOrDefault().ToString("N");
        }

        private static TaxonomyAssetReceiptDisposition ExecutionDisposition(
            TaxonomyAssetActionApproval approval,
            bool changed)
        {
            if (approval.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse)
            {
                return TaxonomyAssetReceiptDisposition.ReuseExternal;
            }
            if (!changed)
            {
                return TaxonomyAssetReceiptDisposition.ReuseOwned;
            }
            return approval.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReconcileOwnedPlanDrift
                ? TaxonomyAssetReceiptDisposition.ReconciledOwned
                : TaxonomyAssetReceiptDisposition.CreatedOwned;
        }
    }
}
