using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Taxonomy.Assets.Execution
{
    internal static class TaxonomyAssetReceiptIdentity
    {
        public static IReadOnlyDictionary<string, MigrationActionSignature> CreateActionSignatures(
            TaxonomyAssetReviewPlan plan,
            TaxonomyAssetApprovalManifest approval)
        {
            if (plan == null || approval == null)
            {
                throw new ArgumentNullException(plan == null ? nameof(plan) : nameof(approval));
            }
            var approved = approval.Actions
                .Where(value => value.Decision == TaxonomyAssetApprovalDecision.Approve)
                .ToDictionary(value => value.ActionId, StringComparer.Ordinal);
            var result = new Dictionary<string, MigrationActionSignature>(StringComparer.Ordinal);

            foreach (var group in plan.TermGroups.OrderBy(value => value.Source.TermStoreId))
            {
                var actionId = TaxonomyAssetApprovalFactory.TermGroupActionId(group.Source.TenantId, group.Source.TermStoreId);
                if (approved.TryGetValue(actionId, out var action))
                {
                    result[actionId] = Create(group, action, Array.Empty<string>());
                }
            }
            foreach (var set in plan.TermSets.OrderBy(value => value.Source.TermStoreId).ThenBy(value => value.Source.TermSetId))
            {
                var actionId = TaxonomyAssetApprovalFactory.TermSetActionId(set.Source.TermStoreId, set.Source.TermSetId);
                if (!approved.TryGetValue(actionId, out var action))
                {
                    continue;
                }
                var groupActionId = TaxonomyAssetApprovalFactory.TermGroupActionId(set.Source.TenantId, set.Source.TermStoreId);
                var dependencies = action.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse
                    || !result.TryGetValue(groupActionId, out var groupSignature)
                        ? Array.Empty<string>()
                        : new[] { groupSignature.Signature };
                result[actionId] = Create(set, action, dependencies);
            }
            foreach (var term in TaxonomyAssetMaterializationCoordinator.OrderTerms(plan.Terms))
            {
                var actionId = TaxonomyAssetApprovalFactory.TermActionId(
                    term.Source.TermStoreId,
                    term.Source.TermSetId,
                    term.Source.TermId);
                if (!approved.TryGetValue(actionId, out var action))
                {
                    continue;
                }
                var dependencies = new List<string>();
                var setActionId = TaxonomyAssetApprovalFactory.TermSetActionId(term.Source.TermStoreId, term.Source.TermSetId);
                if (result.TryGetValue(setActionId, out var setSignature))
                {
                    dependencies.Add(setSignature.Signature);
                }
                if (term.TargetParentTermId.HasValue)
                {
                    var parentPlan = plan.Terms.SingleOrDefault(value => value != null
                        && value.TargetTermSetId == term.TargetTermSetId
                        && value.PreferredTargetTermId == term.TargetParentTermId.Value);
                    var parentActionId = parentPlan == null
                        ? null
                        : TaxonomyAssetApprovalFactory.TermActionId(
                            parentPlan.Source.TermStoreId,
                            parentPlan.Source.TermSetId,
                            parentPlan.Source.TermId);
                    if (parentActionId != null && result.TryGetValue(parentActionId, out var parentSignature))
                    {
                        dependencies.Add(parentSignature.Signature);
                    }
                }
                result[actionId] = Create(term, action, dependencies);
            }
            return result;
        }

        public static void Populate(
            TaxonomyAssetActionReceipt receipt,
            TaxonomyAssetActionApproval approval,
            MigrationActionSignature signature,
            string observedStateDigest)
        {
            if (receipt == null || approval == null || signature == null)
            {
                throw new ArgumentNullException(receipt == null ? nameof(receipt) : approval == null ? nameof(approval) : nameof(signature));
            }
            receipt.Ownership = approval.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse
                ? MigrationTargetOwnership.External
                : MigrationTargetOwnership.MigrationOwned;
            receipt.ExecutionDisposition = ExecutionDisposition(approval, receipt.ChangedTarget);
            receipt.SourceIdentity = SourceIdentity(approval);
            receipt.TargetIdentity = TargetIdentity(approval);
            receipt.SemanticMappingDigest = SemanticMappingDigest(approval);
            receipt.ActionSignature = signature.Signature;
            receipt.ObservedStateDigest = observedStateDigest;
        }

        public static string ObservedStateDigest(
            TaxonomyAssetActionApproval action,
            TaxonomyAssetReviewPlan freshInspection)
        {
            if (action.Kind == TaxonomyAssetKind.TermGroup)
            {
                var probe = freshInspection.TermGroupProbes.Single(value =>
                    value.SourceTenantId == action.SourceTenantId
                    && value.SourceTermStoreId == action.SourceTermStoreId);
                return Digest(new
                {
                    schemaVersion = "pnp-taxonomy-observed-termgroup/v1",
                    action.Kind,
                    action.TargetTermStoreId,
                    targetTermGroupId = probe.ResolvedTargetGroupId ?? action.TargetTermGroupId,
                    name = probe.ExistingName,
                    ownership = MigrationTargetOwnership.MigrationOwned
                });
            }
            if (action.Kind == TaxonomyAssetKind.TermSet)
            {
                var probe = freshInspection.TermSetProbes.Single(value =>
                    value.SourceTermStoreId == action.SourceTermStoreId
                    && value.SourceTermSetId == action.SourceTermSetId);
                return Digest(new
                {
                    schemaVersion = "pnp-taxonomy-observed-termset/v1",
                    action.Kind,
                    action.TargetTermStoreId,
                    action.TargetTermGroupId,
                    targetTermSetId = probe.ResolvedTargetTermSetId ?? action.TargetTermSetId,
                    name = probe.ExistingName,
                    probe.ExistingIsOpenForTermCreation,
                    probe.ExistingIsAvailableForTagging,
                    probe.ExistingOriginalIdentifier,
                    probe.ExistingMappingDigest,
                    ownership = action.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse
                        ? MigrationTargetOwnership.External
                        : MigrationTargetOwnership.MigrationOwned
                });
            }

            var termProbe = freshInspection.TermProbes.Single(value =>
                value.SourceTermStoreId == action.SourceTermStoreId
                && value.SourceTermSetId == action.SourceTermSetId
                && value.SourceTermId == action.SourceTermId);
            return Digest(new
            {
                schemaVersion = "pnp-taxonomy-observed-term/v1",
                action.Kind,
                action.TargetTermStoreId,
                targetTermSetId = termProbe.ExistingTermSetId ?? action.TargetTermSetId,
                targetTermId = termProbe.ResolvedTargetTermId ?? action.TargetTermId,
                name = termProbe.ExistingName,
                path = termProbe.ExistingPath,
                parent = termProbe.ExistingParentTermId,
                termProbe.ExistingIsAvailableForTagging,
                termProbe.ExistingIsReused,
                termProbe.ExistingIsSourceTerm,
                termProbe.ExistingReuseSourceTermId,
                termSetIds = (termProbe.ExistingTermSetIds ?? new List<Guid>()).OrderBy(value => value).ToArray(),
                termProbe.ExistingPinSourceTermSetId,
                termProbe.ExistingOriginalIdentifier,
                termProbe.ExistingMappingDigest,
                ownership = action.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse
                    ? MigrationTargetOwnership.External
                    : MigrationTargetOwnership.MigrationOwned
            });
        }

        public static string SemanticMappingDigest(TaxonomyAssetActionApproval action)
        {
            return Digest(new
            {
                schemaVersion = "pnp-taxonomy-approved-asset-mapping/v1",
                action.ActionId,
                action.Kind,
                action.SourceTenantId,
                action.SourceTermStoreId,
                action.SourceTermSetId,
                action.SourceTermId,
                action.TargetTermStoreId,
                action.TargetTermGroupId,
                action.TargetTermSetId,
                action.TargetTermId,
                action.ReviewedDisposition
            });
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
            var prefix = action.Kind == TaxonomyAssetKind.TermGroup
                ? "urn:pnp:spo-target-termgroup:v1:"
                : action.Kind == TaxonomyAssetKind.TermSet
                    ? "urn:pnp:spo-target-termset:v1:"
                    : "urn:pnp:spo-target-term:v1:";
            return prefix + action.TargetTermStoreId.ToString("N")
                + (action.Kind == TaxonomyAssetKind.TermGroup
                    ? ":" + action.TargetTermGroupId.GetValueOrDefault().ToString("N")
                    : ":" + action.TargetTermSetId.ToString("N"))
                + (action.Kind == TaxonomyAssetKind.Term
                    ? ":" + action.TargetTermId.GetValueOrDefault().ToString("N")
                    : string.Empty);
        }

        private static MigrationActionSignature Create(
            TaxonomyTermGroupMaterializationPlan plan,
            TaxonomyAssetActionApproval action,
            IEnumerable<string> dependencies)
        {
            return MigrationActionSignature.Create(
                action.ActionId,
                "Taxonomy.TermGroup",
                Digest(plan.Source),
                SelectionDigest(action),
                TargetIdentity(action),
                Digest(new
                {
                    schemaVersion = "pnp-taxonomy-observed-termgroup/v1",
                    action.Kind,
                    action.TargetTermStoreId,
                    action.TargetTermGroupId,
                    name = plan.TargetGroupName,
                    ownership = MigrationTargetOwnership.MigrationOwned
                }),
                dependencies);
        }

        private static MigrationActionSignature Create(
            TaxonomyTermSetMaterializationPlan plan,
            TaxonomyAssetActionApproval action,
            IEnumerable<string> dependencies)
        {
            var external = action.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse;
            return MigrationActionSignature.Create(
                action.ActionId,
                "Taxonomy.TermSet",
                plan.SourceEvidenceSha256,
                SelectionDigest(action),
                TargetIdentity(action),
                Digest(new
                {
                    schemaVersion = "pnp-taxonomy-observed-termset/v1",
                    action.Kind,
                    action.TargetTermStoreId,
                    action.TargetTermGroupId,
                    action.TargetTermSetId,
                    name = external ? plan.SourceTermSetName : plan.TargetTermSetName,
                    existingIsOpenForTermCreation = plan.IsOpenForTermCreation,
                    existingIsAvailableForTagging = plan.IsAvailableForTagging,
                    existingOriginalIdentifier = external ? null : plan.OriginalIdentifier,
                    existingMappingDigest = external ? null : plan.MappingDigest,
                    ownership = external ? MigrationTargetOwnership.External : MigrationTargetOwnership.MigrationOwned
                }),
                dependencies);
        }

        private static MigrationActionSignature Create(
            TaxonomyTermMaterializationPlan plan,
            TaxonomyAssetActionApproval action,
            IEnumerable<string> dependencies)
        {
            var external = action.ReviewedDisposition == TaxonomyAssetTargetDisposition.ReviewExternalReuse;
            return MigrationActionSignature.Create(
                action.ActionId,
                "Taxonomy.Term",
                plan.SourceEvidenceSha256,
                SelectionDigest(action),
                TargetIdentity(action),
                Digest(new
                {
                    schemaVersion = "pnp-taxonomy-observed-term/v1",
                    action.Kind,
                    action.TargetTermStoreId,
                    action.TargetTermSetId,
                    action.TargetTermId,
                    name = plan.Name,
                    path = plan.SourcePath,
                    parent = plan.TargetParentTermId,
                    existingIsAvailableForTagging = plan.IsAvailableForTagging,
                    existingIsReused = plan.SourceIsReused,
                    existingIsSourceTerm = plan.SourceIsSourceTerm,
                    existingReuseSourceTermId = plan.SourceReuseSourceTermId,
                    termSetIds = (plan.SourceTermSetIds ?? new List<Guid>())
                        .Select(value => value == plan.Source.TermSetId ? plan.TargetTermSetId : value)
                        .Distinct()
                        .OrderBy(value => value)
                        .ToArray(),
                    existingPinSourceTermSetId = plan.SourcePinSourceTermSetId.HasValue
                        && plan.SourcePinSourceTermSetId.Value == plan.Source.TermSetId
                            ? plan.TargetTermSetId
                            : plan.SourcePinSourceTermSetId,
                    existingOriginalIdentifier = external ? null : plan.OriginalIdentifier,
                    existingMappingDigest = external ? null : plan.MappingDigest,
                    ownership = external ? MigrationTargetOwnership.External : MigrationTargetOwnership.MigrationOwned
                }),
                dependencies);
        }

        private static string SelectionDigest(TaxonomyAssetActionApproval action)
        {
            return Digest(new
            {
                schemaVersion = "pnp-taxonomy-action-approval-selection/v1",
                action.ActionId,
                action.Kind,
                action.SourceTenantId,
                action.SourceTermStoreId,
                action.SourceTermSetId,
                action.SourceTermId,
                action.TargetTermStoreId,
                action.TargetTermGroupId,
                action.TargetTermSetId,
                action.TargetTermId,
                action.ReviewedDisposition,
                action.Decision,
                action.RequiresExplicitReview,
                action.ExternalMutationApproved
            });
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

        private static string Digest<T>(T value)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value));
        }
    }
}
