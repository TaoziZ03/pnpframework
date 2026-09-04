using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyPageReferenceFactory
    {
        public static SharedTopologyPageReference Create(
            SharedTopologyPlan plan,
            Guid sourceSiteId,
            Guid sourceWebId)
        {
            SharedTopologyPlanValidator.Validate(plan);
            var binding = plan.SourceWebBindings.SingleOrDefault(value =>
                value.SourceSiteId == sourceSiteId && value.SourceWebId == sourceWebId);
            var fidelity = plan.SourceWebFidelityIngredients.SingleOrDefault(value =>
                value.SourceSiteId == sourceSiteId && value.SourceWebId == sourceWebId);
            if (binding == null || fidelity == null)
            {
                throw new InvalidDataException("The shared topology plan has no source fidelity and target binding for this page Web.");
            }
            var byAction = plan.TargetWebContainers.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            if (!byAction.TryGetValue(binding.TargetGlobalActionKey, out var leaf))
            {
                throw new InvalidDataException("The source-Web binding references an unknown target global action.");
            }
            var required = new List<string>();
            for (var current = leaf; current != null; current = string.IsNullOrWhiteSpace(current.ParentGlobalActionKey)
                ? null
                : byAction[current.ParentGlobalActionKey])
            {
                required.Add(current.GlobalActionKey);
            }
            required.Reverse();
            var reference = new SharedTopologyPageReference
            {
                SupportCohortSignature = plan.SupportCohortSignature,
                SourceSiteId = sourceSiteId,
                SourceWebId = sourceWebId,
                SourceWebFidelityIngredientId = fidelity.IngredientId,
                SourceFidelityState = fidelity.State,
                SourceAuthorizationEvidence = fidelity.AuthorizationEvidence,
                TargetLeafContainerIngredientId = leaf.IngredientId,
                TargetLeafGlobalActionKey = leaf.GlobalActionKey,
                TargetWebUrl = leaf.TargetWebUrl,
                TargetServerRelativeUrl = leaf.TargetServerRelativeUrl,
                RequiredGlobalActionKeys = required
            };
            Validate(reference);
            return reference;
        }

        public static void Validate(SharedTopologyPageReference reference)
        {
            if (reference == null
                || !string.Equals(reference.SchemaVersion, "pnp-shared-topology-page-reference/v2", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(reference.SupportCohortSignature)
                || reference.SourceSiteId == Guid.Empty
                || reference.SourceWebId == Guid.Empty
                || !string.Equals(
                    reference.SourceWebFidelityIngredientId,
                    SharedTopologyIdentity.SourceWebFidelity(reference.SourceSiteId, reference.SourceWebId),
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(reference.SourceWebFidelityIngredientId)
                || string.IsNullOrWhiteSpace(reference.TargetLeafContainerIngredientId)
                || string.IsNullOrWhiteSpace(reference.TargetLeafGlobalActionKey)
                || reference.RequiredGlobalActionKeys == null
                || reference.RequiredGlobalActionKeys.Count == 0
                || reference.RequiredGlobalActionKeys.Any(string.IsNullOrWhiteSpace)
                || reference.RequiredGlobalActionKeys.Distinct(StringComparer.Ordinal).Count() != reference.RequiredGlobalActionKeys.Count
                || !string.Equals(reference.RequiredGlobalActionKeys.Last(), reference.TargetLeafGlobalActionKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The shared topology page reference is incomplete or has an unsupported schema.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(
                reference.TargetWebUrl,
                reference.TargetServerRelativeUrl,
                nameof(reference.TargetWebUrl));
            if (reference.SourceFidelityState == SourceWebFidelityState.AuthorizationBlocked)
            {
                PnP.Framework.Migration.Evidence.LiteralHttpAuthorizationEvidence.Validate(reference.SourceAuthorizationEvidence);
            }
            else if (reference.SourceAuthorizationEvidence != null)
            {
                throw new InvalidDataException("Only authorization-blocked source fidelity may retain literal authorization evidence.");
            }
        }

        public static void ValidateReceipt(
            SharedTopologyPageReference reference,
            SharedTopologyGlobalMaterializationReceipt receipt)
        {
            Validate(reference);
            if (receipt == null
                || !string.Equals(receipt.SchemaVersion, "pnp-shared-topology-global-receipt/v1", StringComparison.Ordinal)
                || receipt.OperationId == Guid.Empty
                || receipt.StartedAtUtc == default(DateTimeOffset)
                || receipt.CompletedAtUtc < receipt.StartedAtUtc
                || !receipt.FreshReadbackPassed
                || receipt.Actions == null
                || receipt.SourceWebMappings == null
                || receipt.Actions.Any(value => value == null
                    || string.IsNullOrWhiteSpace(value.GlobalActionKey)
                    || value.TargetSiteId == Guid.Empty
                    || value.TargetWebId == Guid.Empty
                    || value.TargetParentWebId == Guid.Empty
                    || !value.FreshReadbackPassed)
                || receipt.Actions.Select(value => value.GlobalActionKey).Distinct(StringComparer.Ordinal).Count()
                    != receipt.Actions.Count
                || receipt.SourceWebMappings.Any(value => value == null
                    || value.SourceSiteId == Guid.Empty
                    || value.SourceWebId == Guid.Empty
                    || string.IsNullOrWhiteSpace(value.TargetGlobalActionKey))
                || !string.Equals(
                    receipt.ReceiptDigest,
                    SharedTopologyGlobalExecutionDigest.ComputeReceipt(receipt),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The page requires a freshly verified shared topology receipt.");
            }
            var byAction = receipt.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            foreach (var key in reference.RequiredGlobalActionKeys)
            {
                if (!byAction.TryGetValue(key, out var action) || !action.FreshReadbackPassed)
                {
                    throw new InvalidDataException("The shared topology receipt omits a verified global action required by the page.");
                }
            }
            var leaf = byAction[reference.TargetLeafGlobalActionKey];
            if (!SharedTopologyPath.EqualsUrl(leaf.TargetWebUrl, reference.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(leaf.TargetServerRelativeUrl, reference.TargetServerRelativeUrl))
            {
                throw new InvalidDataException("The shared topology receipt does not verify the page's exact target leaf Web.");
            }
            var mappings = receipt.SourceWebMappings?.Where(value => value != null
                && value.SourceSiteId == reference.SourceSiteId
                && value.SourceWebId == reference.SourceWebId).ToArray()
                ?? Array.Empty<SharedTopologySourceWebMaterializationReceipt>();
            var mapping = mappings.Length == 1 ? mappings[0] : null;
            if (mapping == null
                || !string.Equals(mapping.TargetGlobalActionKey, reference.TargetLeafGlobalActionKey, StringComparison.Ordinal)
                || mapping.TargetSiteId != leaf.TargetSiteId
                || mapping.TargetWebId != leaf.TargetWebId
                || mapping.Ownership != leaf.Ownership
                || !SharedTopologyPath.EqualsUrl(mapping.TargetWebUrl, reference.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(mapping.TargetServerRelativeUrl, reference.TargetServerRelativeUrl))
            {
                throw new InvalidDataException("The shared topology receipt omits the page's exact source-to-target owner mapping.");
            }
        }
    }
}
