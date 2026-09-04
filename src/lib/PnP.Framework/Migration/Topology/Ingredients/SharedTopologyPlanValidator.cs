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
            if (plan == null)
            {
                throw new InvalidDataException("The shared topology plan is missing.");
            }
            if (!string.Equals(plan.SchemaVersion, SharedTopologyPlan.CurrentSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsupported shared topology plan schema '" + plan.SchemaVersion + "'.");
            }
            if (plan.TargetSite == null
                || plan.SourceWebFidelityIngredients == null
                || plan.TargetWebContainers == null
                || plan.SourceWebBindings == null)
            {
                throw new InvalidDataException("The shared topology plan is missing a required collection or target Site Collection.");
            }
            ValidateTargetSite(plan.TargetSite);

            var allIds = new HashSet<string>(StringComparer.Ordinal);
            AddUnique(allIds, plan.TargetSite.IngredientId, "target Site Collection");
            foreach (var fidelity in plan.SourceWebFidelityIngredients)
            {
                ValidateFidelity(fidelity);
                AddUnique(allIds, fidelity.IngredientId, "source-Web fidelity ingredient");
            }

            var containers = new Dictionary<string, TargetWebContainerIngredientPlan>(StringComparer.Ordinal);
            foreach (var container in plan.TargetWebContainers.OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl)))
            {
                ValidateContainer(plan.TargetSite, container, containers);
                AddUnique(allIds, container.IngredientId, "target-Web container ingredient");
                containers.Add(container.IngredientId, container);
            }

            var bindingIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in plan.SourceWebBindings)
            {
                if (binding == null
                    || binding.SourceSiteId == Guid.Empty
                    || binding.SourceWebId == Guid.Empty
                    || !containers.TryGetValue(binding.TargetContainerIngredientId ?? string.Empty, out var container)
                    || !SharedTopologyPath.EqualsUrl(binding.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(binding.TargetServerRelativeUrl, container.TargetServerRelativeUrl))
                {
                    throw new InvalidDataException("A source-Web binding does not resolve to one planned target-Web container.");
                }
                var identity = binding.SourceSiteId.ToString("D") + "/" + binding.SourceWebId.ToString("D");
                if (!bindingIdentities.Add(identity))
                {
                    throw new InvalidDataException("The shared topology plan contains a duplicate source-Web binding '" + identity + "'.");
                }
            }
            if (!IsSha256(plan.PlanDigest)
                || !string.Equals(plan.PlanDigest, SharedTopologyDigest.ComputePlan(plan), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology plan digest differs from its canonical content.");
            }
        }

        public static string ComputeDigest(SharedTopologyPlan plan)
        {
            return SharedTopologyDigest.ComputePlan(plan);
        }

        private static void ValidateTargetSite(TargetSiteCollectionIngredientPlan site)
        {
            if (site.IdentityBasis != SharedTopologyIdentityBasis.TargetSiteRoot
                || !string.Equals(site.IngredientId, SharedTopologyIdentity.TargetSite(site.TargetServerRelativeUrl), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The target Site Collection ingredient identity is not canonical.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(site.TargetSiteCollectionUrl, site.TargetServerRelativeUrl, nameof(site.TargetSiteCollectionUrl));
        }

        private static void ValidateFidelity(SourceWebFidelityIngredientPlan fidelity)
        {
            if (fidelity == null
                || fidelity.IdentityBasis != SharedTopologyIdentityBasis.CapturedSourceWeb
                || fidelity.SourceSiteId == Guid.Empty
                || fidelity.SourceWebId == Guid.Empty
                || !string.Equals(fidelity.IngredientId, SharedTopologyIdentity.SourceWebFidelity(fidelity.SourceSiteId, fidelity.SourceWebId), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(fidelity.EvidenceSha256))
            {
                throw new InvalidDataException("The shared topology plan contains invalid source-Web fidelity evidence.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(fidelity.SourceWebUrl, fidelity.SourceServerRelativeUrl, nameof(fidelity.SourceWebUrl));
            if (fidelity.State == SourceWebFidelityState.AuthorizationBlocked)
            {
                if (fidelity.AuthorizationEvidence == null
                    || !PathDerivedSourceTopologyEvidenceFactory.IsLiteralAuthorizationStatus(fidelity.AuthorizationEvidence.HttpStatusCode)
                    || string.IsNullOrWhiteSpace(fidelity.AuthorizationEvidence.Operation)
                    || fidelity.AuthorizationEvidence.ObservedAtUtc == default(DateTimeOffset)
                    || !string.Equals(
                        fidelity.AuthorizationEvidence.EvidenceSha256,
                        SharedTopologyDigest.ComputeHttpFailure(fidelity.AuthorizationEvidence),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Authorization-blocked source-Web fidelity requires literal HTTP 401/403 evidence.");
                }
            }
        }

        private static void ValidateContainer(
            TargetSiteCollectionIngredientPlan site,
            TargetWebContainerIngredientPlan container,
            IReadOnlyDictionary<string, TargetWebContainerIngredientPlan> priorContainers)
        {
            if (container == null
                || container.IdentityBasis != SharedTopologyIdentityBasis.ExactRelativePath
                || container.Provisioning == null
                || container.Provisioning.ExpectedMetadataDifferences == null
                || string.IsNullOrWhiteSpace(container.Provisioning.Title)
                || string.IsNullOrWhiteSpace(container.Provisioning.Template)
                || container.Provisioning.Configuration < 0
                || container.Provisioning.Language <= 0
                || !string.Equals(container.IngredientId, SharedTopologyIdentity.TargetWebContainer(container.TargetServerRelativeUrl), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The shared topology plan contains an invalid target-Web container.");
            }
            var parentIsTargetSite = string.Equals(container.ParentIngredientId, site.IngredientId, StringComparison.Ordinal);
            if (!parentIsTargetSite && !priorContainers.ContainsKey(container.ParentIngredientId ?? string.Empty))
            {
                throw new InvalidDataException("Target-Web container '" + container.IngredientId + "' references an unknown or later parent ingredient.");
            }
            var parentPath = parentIsTargetSite
                ? site.TargetServerRelativeUrl
                : priorContainers[container.ParentIngredientId].TargetServerRelativeUrl;
            var expectedPath = SharedTopologyPath.Combine(parentPath, SharedTopologyPath.Leaf(container.TargetServerRelativeUrl));
            if (!SharedTopologyPath.EqualsPath(expectedPath, container.TargetServerRelativeUrl)
                || !SharedTopologyPath.EqualsUrl(container.TargetParentWebUrl,
                    parentIsTargetSite
                        ? site.TargetSiteCollectionUrl
                        : priorContainers[container.ParentIngredientId].TargetWebUrl))
            {
                throw new InvalidDataException("Target-Web container '" + container.IngredientId + "' is not a direct child of its declared parent.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(container.TargetWebUrl, container.TargetServerRelativeUrl, nameof(container.TargetWebUrl));
            SharedTopologyPath.ValidateUrlMatchesPath(container.PreferredTargetWebUrl, container.PreferredTargetServerRelativeUrl, nameof(container.PreferredTargetWebUrl));
            var sourceRelativePath = SharedTopologyPath.NormalizeServerRelativePath("/" + (container.SourceRelativePath ?? string.Empty).Trim('/'), nameof(container.SourceRelativePath));
            if (!string.Equals(SharedTopologyPath.Leaf(sourceRelativePath), container.SourcePathSegment, StringComparison.Ordinal)
                || !SharedTopologyPath.EqualsPath(
                    site.TargetServerRelativeUrl.TrimEnd('/') + sourceRelativePath,
                    container.PreferredTargetServerRelativeUrl))
            {
                throw new InvalidDataException("Target-Web container '" + container.IngredientId + "' does not preserve its source-relative path segment-by-segment.");
            }
            if (!IsSha256(container.IngredientDigest)
                || !string.Equals(container.IngredientDigest, SharedTopologyDigest.ComputeContainer(container), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Target-Web container '" + container.IngredientId + "' has a stale ingredient digest.");
            }
            if (!container.CollisionResolved
                && (!SharedTopologyPath.EqualsPath(container.PreferredTargetServerRelativeUrl, container.TargetServerRelativeUrl)
                    || !SharedTopologyPath.EqualsUrl(container.PreferredTargetWebUrl, container.TargetWebUrl)))
            {
                throw new InvalidDataException("A target-Web path differs from its exact preferred path without an explicit collision decision.");
            }
            if (container.CollisionResolved && string.IsNullOrWhiteSpace(container.CollisionResolutionReason))
            {
                throw new InvalidDataException("A collision-resolved target-Web path requires a reviewable reason.");
            }
        }

        private static void AddUnique(ISet<string> values, string value, string description)
        {
            if (string.IsNullOrWhiteSpace(value) || !values.Add(value))
            {
                throw new InvalidDataException("The shared topology plan contains a missing or duplicate " + description + " ID.");
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
