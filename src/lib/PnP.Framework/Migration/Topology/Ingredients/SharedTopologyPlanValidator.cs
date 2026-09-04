using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyPlanValidator
    {
        public static void Validate(SharedTopologyPlan plan)
        {
            if (plan == null
                || !string.Equals(plan.SchemaVersion, SharedTopologyPlan.CurrentSchemaVersion, StringComparison.Ordinal)
                || plan.TargetSite == null
                || plan.SourceWebFidelityIngredients == null
                || plan.SourceWebFidelityIngredients.Count < 2
                || plan.TargetWebContainers == null
                || plan.TargetWebContainers.Count != plan.SourceWebFidelityIngredients.Count
                || plan.SourceWebBindings == null)
            {
                throw new InvalidDataException("The v2 shared topology plan is missing its target fence, partial source fidelity, or per-level target actions.");
            }
            ValidateTargetSite(plan.TargetSite);

            var fidelityByOwner = new Dictionary<string, SourceWebFidelityIngredientPlan>(StringComparer.Ordinal);
            foreach (var fidelity in plan.SourceWebFidelityIngredients
                .OrderBy(value => SharedTopologyPath.Depth(value.SourceServerRelativeUrl)))
            {
                ValidateFidelity(fidelity);
                if (fidelityByOwner.ContainsKey(fidelity.SourceOwnerKey))
                {
                    throw new InvalidDataException("The shared topology plan contains a duplicate source owner fidelity key.");
                }
                fidelityByOwner.Add(fidelity.SourceOwnerKey, fidelity);
            }
            var orderedFidelity = plan.SourceWebFidelityIngredients
                .OrderBy(value => SharedTopologyPath.Depth(value.SourceServerRelativeUrl))
                .ToArray();
            if (orderedFidelity.First().State != SourceWebFidelityState.Captured
                || orderedFidelity.Last().State != SourceWebFidelityState.Captured
                || orderedFidelity.Skip(1).Take(orderedFidelity.Length - 2)
                    .Any(value => value.State != SourceWebFidelityState.AuthorizationBlocked))
            {
                throw new InvalidDataException("Partial topology must retain captured root and leaf Webs with one authorization-limited fidelity ingredient per unknown ancestor.");
            }

            var containersById = new Dictionary<string, TargetWebContainerIngredientPlan>(StringComparer.Ordinal);
            var containersByOwner = new Dictionary<string, TargetWebContainerIngredientPlan>(StringComparer.Ordinal);
            foreach (var container in plan.TargetWebContainers
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl)))
            {
                ValidateContainer(plan.TargetSite, container, fidelityByOwner, containersById);
                if (containersById.ContainsKey(container.IngredientId)
                    || containersByOwner.ContainsKey(container.SourceOwnerKey))
                {
                    throw new InvalidDataException("The shared topology plan contains duplicate target container or source-owner mappings.");
                }
                containersById.Add(container.IngredientId, container);
                containersByOwner.Add(container.SourceOwnerKey, container);
            }
            if (plan.TargetWebContainers.Count(value => value.IsTargetSiteRoot) != 1)
            {
                throw new InvalidDataException("The shared topology plan requires exactly one external target Site root action.");
            }

            var bindingOwners = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in plan.SourceWebBindings)
            {
                if (binding == null
                    || string.IsNullOrWhiteSpace(binding.SourceOwnerKey)
                    || !bindingOwners.Add(binding.SourceOwnerKey)
                    || !fidelityByOwner.TryGetValue(binding.SourceOwnerKey, out var fidelity)
                    || !containersByOwner.TryGetValue(binding.SourceOwnerKey, out var container)
                    || binding.SourceSiteId != fidelity.SourceSiteId
                    || binding.SourceWebId != fidelity.SourceWebId
                    || !string.Equals(binding.SourceWebUrl, fidelity.SourceWebUrl, StringComparison.OrdinalIgnoreCase)
                    || !SharedTopologyPath.EqualsPath(binding.SourceServerRelativeUrl, fidelity.SourceServerRelativeUrl)
                    || !string.Equals(binding.TargetContainerIngredientId, container.IngredientId, StringComparison.Ordinal)
                    || !string.Equals(binding.TargetGlobalActionKey, container.GlobalActionKey, StringComparison.Ordinal)
                    || !SharedTopologyPath.EqualsUrl(binding.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(binding.TargetServerRelativeUrl, container.TargetServerRelativeUrl))
                {
                    throw new InvalidDataException("A source-owner binding does not resolve one fidelity ingredient to its exact target action.");
                }
            }
            if (bindingOwners.Count != fidelityByOwner.Count)
            {
                throw new InvalidDataException("The shared topology plan must bind every retained source level exactly once.");
            }
            if (!string.Equals(
                    plan.ExecutionGroupDigest,
                    SharedTopologyIdentity.ExecutionGroup(plan.TargetWebContainers.Select(value => value.ActionSignature)),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plan.SupportCohortDigest, SharedTopologyDigest.ComputeSupportCohort(plan), StringComparison.OrdinalIgnoreCase)
                || !MigrationActionSignature.IsSha256(plan.PlanDigest)
                || !string.Equals(plan.PlanDigest, SharedTopologyDigest.ComputePlan(plan), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology plan has a stale execution-group, support-cohort, or plan digest.");
            }
        }

        public static string ComputeDigest(SharedTopologyPlan plan)
        {
            return SharedTopologyDigest.ComputePlan(plan);
        }

        private static void ValidateTargetSite(TargetSiteCollectionIngredientPlan site)
        {
            var slot = SharedTopologyIdentity.TargetSlot(
                site.TargetSiteCollectionUrl,
                site.ExpectedTargetSiteId,
                site.TargetServerRelativeUrl,
                site.TargetServerRelativeUrl);
            if (site.IdentityBasis != SharedTopologyIdentityBasis.TargetSiteRoot
                || site.ExpectedTargetSiteId == Guid.Empty
                || site.ExpectedTargetRootWebId == Guid.Empty
                || !string.Equals(site.IngredientId, SharedTopologyIdentity.TargetSite(slot), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The target Site/root fence is incomplete or not canonical.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(site.TargetSiteCollectionUrl, site.TargetServerRelativeUrl, nameof(site.TargetSiteCollectionUrl));
        }

        private static void ValidateFidelity(SourceWebFidelityIngredientPlan fidelity)
        {
            if (fidelity == null
                || fidelity.SourceSiteId == Guid.Empty
                || string.IsNullOrWhiteSpace(fidelity.SourceOwnerKey)
                || !string.Equals(
                    fidelity.SourceOwnerKey,
                    SharedTopologyIdentity.SourceOwner(fidelity.SourceWebUrl, fidelity.SourceSiteId, fidelity.SourceServerRelativeUrl),
                    StringComparison.Ordinal)
                || !MigrationActionSignature.IsSha256(fidelity.EvidenceSha256))
            {
                throw new InvalidDataException("The shared topology plan contains invalid source-owner fidelity evidence.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(fidelity.SourceWebUrl, fidelity.SourceServerRelativeUrl, nameof(fidelity.SourceWebUrl));
            if (fidelity.State == SourceWebFidelityState.Captured)
            {
                if (fidelity.IdentityBasis != SharedTopologyIdentityBasis.CapturedSourceWeb
                    || fidelity.SourceWebId == Guid.Empty
                    || fidelity.AuthorizationEvidence != null
                    || !string.Equals(
                        fidelity.IngredientId,
                        SharedTopologyIdentity.SourceWebFidelity(fidelity.SourceSiteId, fidelity.SourceWebId),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Captured source fidelity requires a real Web GUID and no authorization blocker.");
                }
            }
            else if (fidelity.State == SourceWebFidelityState.AuthorizationBlocked)
            {
                BoundLiteralHttpAuthorizationEvidence.Validate(
                    fidelity.AuthorizationEvidence,
                    PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadActionId);
                if (fidelity.IdentityBasis != SharedTopologyIdentityBasis.ExactRelativePath
                    || fidelity.SourceWebId != Guid.Empty
                    || !string.Equals(fidelity.IngredientId, SharedTopologyIdentity.SourcePathFidelity(fidelity.SourceOwnerKey), StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Unknown ancestor fidelity must be path-identified and bound to literal authorization evidence.");
                }
            }
            else
            {
                throw new InvalidDataException("Unsupported source Web fidelity state.");
            }
        }

        private static void ValidateContainer(
            TargetSiteCollectionIngredientPlan site,
            TargetWebContainerIngredientPlan container,
            IReadOnlyDictionary<string, SourceWebFidelityIngredientPlan> fidelityByOwner,
            IReadOnlyDictionary<string, TargetWebContainerIngredientPlan> priorContainers)
        {
            if (container == null
                || container.Provisioning == null
                || container.Provisioning.ExpectedMetadataDifferences == null
                || string.IsNullOrWhiteSpace(container.Provisioning.Title)
                || string.IsNullOrWhiteSpace(container.Provisioning.Template)
                || container.Provisioning.Configuration < 0
                || container.Provisioning.Language <= 0
                || !fidelityByOwner.TryGetValue(container.SourceOwnerKey ?? string.Empty, out var fidelity)
                || container.ExpectedTargetSiteId != site.ExpectedTargetSiteId
                || !Enum.IsDefined(typeof(SharedTopologyOwnership), container.ExpectedOwnership))
            {
                throw new InvalidDataException("The shared topology plan contains an incomplete target Web action.");
            }
            var expectedSlot = SharedTopologyIdentity.TargetSlot(
                site.TargetSiteCollectionUrl,
                site.ExpectedTargetSiteId,
                site.TargetServerRelativeUrl,
                container.TargetServerRelativeUrl);
            if (!string.Equals(container.TargetSlotKey, expectedSlot, StringComparison.Ordinal)
                || !string.Equals(container.IngredientId, SharedTopologyIdentity.TargetWebContainer(expectedSlot), StringComparison.Ordinal)
                || !SharedTopologyPath.EqualsUrl(container.TargetWebUrl, container.PreferredTargetWebUrl)
                    && !container.CollisionResolved
                || !SharedTopologyPath.EqualsPath(container.TargetServerRelativeUrl, container.PreferredTargetServerRelativeUrl)
                    && !container.CollisionResolved)
            {
                throw new InvalidDataException("The target Web action has a stale slot, ingredient identity, or unexplained path change.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(container.TargetWebUrl, container.TargetServerRelativeUrl, nameof(container.TargetWebUrl));
            SharedTopologyPath.ValidateUrlMatchesPath(container.PreferredTargetWebUrl, container.PreferredTargetServerRelativeUrl, nameof(container.PreferredTargetWebUrl));

            if (container.IsTargetSiteRoot)
            {
                if (container.IdentityBasis != SharedTopologyIdentityBasis.TargetSiteRoot
                    || !SharedTopologyPath.EqualsPath(container.TargetServerRelativeUrl, site.TargetServerRelativeUrl)
                    || !SharedTopologyPath.EqualsUrl(container.TargetWebUrl, site.TargetSiteCollectionUrl)
                    || container.ParentGlobalActionKey != null
                    || !string.Equals(container.ParentIngredientId, site.IngredientId, StringComparison.Ordinal)
                    || container.ExpectedOwnership != SharedTopologyOwnership.ExternalApprovedHost
                    || container.ApprovedExistingTargetWebId != site.ExpectedTargetRootWebId
                    || fidelity.State != SourceWebFidelityState.Captured)
                {
                    throw new InvalidDataException("The target Site root action must be an exact external host bound to the captured source root.");
                }
            }
            else
            {
                if (container.IdentityBasis != SharedTopologyIdentityBasis.ExactRelativePath
                    || string.IsNullOrWhiteSpace(container.ParentIngredientId)
                    || !priorContainers.TryGetValue(container.ParentIngredientId, out var parent)
                    || !string.Equals(container.ParentGlobalActionKey, parent.GlobalActionKey, StringComparison.Ordinal)
                    || !SharedTopologyPath.EqualsUrl(container.TargetParentWebUrl, parent.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(
                        SharedTopologyPath.Combine(parent.TargetServerRelativeUrl, SharedTopologyPath.Leaf(container.TargetServerRelativeUrl)),
                        container.TargetServerRelativeUrl)
                    || container.ExpectedOwnership == SharedTopologyOwnership.MigrationOwned && container.ApprovedExistingTargetWebId.HasValue
                    || container.ExpectedOwnership == SharedTopologyOwnership.ExternalApprovedHost && !container.ApprovedExistingTargetWebId.HasValue)
                {
                    throw new InvalidDataException("A target child-Web action has an invalid direct parent or ownership boundary.");
                }
            }
            if (!string.Equals(container.SemanticMappingDigest, SharedTopologyDigest.ComputeContainerMapping(container), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A target Web action has a stale semantic mapping digest.");
            }
            MigrationActionSignature.Validate(container.ActionSignature);
            var expectedDependencies = container.IsTargetSiteRoot
                ? Array.Empty<string>()
                : new[] { priorContainers[container.ParentIngredientId].ActionSignature.Signature };
            if (!string.Equals(container.ActionSignature.ActionId, "topology.target-web." + SharedTopologyIdentity.StableDigest(container.TargetSlotKey), StringComparison.Ordinal)
                || !string.Equals(container.ActionSignature.ActionKind, container.IsTargetSiteRoot ? "Topology.TargetSiteRoot" : "Topology.ChildWeb", StringComparison.Ordinal)
                || !string.Equals(container.ActionSignature.SourceEvidenceDigest, fidelity.EvidenceSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(container.ActionSignature.TargetIdentity, container.TargetSlotKey, StringComparison.Ordinal)
                || !string.Equals(container.ActionSignature.SemanticDigest, SharedTopologyDigest.ComputeObservedSemanticState(container), StringComparison.OrdinalIgnoreCase)
                || !container.ActionSignature.DependencySignatures.SequenceEqual(expectedDependencies, StringComparer.OrdinalIgnoreCase)
                || !string.Equals(container.GlobalActionKey, SharedTopologyIdentity.GlobalAction(container.ActionSignature), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(container.OriginalIdentifier))
            {
                throw new InvalidDataException("A target Web action differs from its generic migration action signature.");
            }
            if (container.CollisionResolved && string.IsNullOrWhiteSpace(container.CollisionResolutionReason))
            {
                throw new InvalidDataException("A collision-resolved target Web path requires a reviewable reason.");
            }
        }
    }
}
