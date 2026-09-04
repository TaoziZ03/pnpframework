using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Evidence;
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
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalActionPlan actionPlan,
            Guid sourceSiteId,
            Guid sourceWebId)
        {
            SharedTopologyPlanValidator.Validate(plan);
            SharedTopologyGlobalExecutionValidator.ValidateDag(dag);
            SharedTopologyGlobalExecutionValidator.ValidateActionPlanShape(dag, actionPlan);
            if (!dag.SourcePlanDigests.Contains(plan.PlanDigest, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The global action DAG does not contain the page's shared topology plan.");
            }
            var binding = plan.SourceWebBindings.SingleOrDefault(value =>
                value.SourceSiteId == sourceSiteId && value.SourceWebId == sourceWebId);
            if (binding == null)
            {
                throw new InvalidDataException("The shared topology plan has no captured source-to-target binding for this page Web.");
            }
            var byAction = plan.TargetWebContainers.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            if (!byAction.TryGetValue(binding.TargetGlobalActionKey, out var leaf))
            {
                throw new InvalidDataException("The page Web binding references an unknown target global action.");
            }
            var requiredContainers = new List<TargetWebContainerIngredientPlan>();
            for (var current = leaf; current != null; current = string.IsNullOrWhiteSpace(current.ParentGlobalActionKey)
                ? null
                : byAction[current.ParentGlobalActionKey])
            {
                requiredContainers.Add(current);
            }
            requiredContainers.Reverse();
            var actionByKey = actionPlan.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            if (requiredContainers.Any(value => !actionByKey.TryGetValue(value.GlobalActionKey, out var action)
                || action.SelectedAction == SharedTopologyActionKind.Block
                || action.SelectedAction == SharedTopologyActionKind.SkipByDependency))
            {
                throw new InvalidDataException("The page references a blocked or missing global topology action.");
            }

            var reference = new SharedTopologyPageReference
            {
                SharedPlanDigest = plan.PlanDigest,
                GlobalActionDagDigest = dag.DagDigest,
                ActionPlanDigest = actionPlan.ActionPlanDigest,
                ExecutionGroupDigest = plan.ExecutionGroupDigest,
                SupportCohortDigest = plan.SupportCohortDigest,
                SourceSiteId = sourceSiteId,
                SourceWebId = sourceWebId,
                SourceFidelity = plan.SourceWebFidelityIngredients.Select(value => new SharedTopologySourceFidelityReference
                {
                    IngredientId = value.IngredientId,
                    SourceOwnerKey = value.SourceOwnerKey,
                    SourceWebId = value.SourceWebId,
                    SourceServerRelativeUrl = value.SourceServerRelativeUrl,
                    State = value.State,
                    AuthorizationEvidence = value.AuthorizationEvidence,
                    EvidenceDigest = value.EvidenceSha256
                }).ToList(),
                TargetLeafContainerIngredientId = leaf.IngredientId,
                TargetLeafGlobalActionKey = leaf.GlobalActionKey,
                TargetWebUrl = leaf.TargetWebUrl,
                TargetServerRelativeUrl = leaf.TargetServerRelativeUrl,
                RequiredActions = requiredContainers.Select(value => new SharedTopologyRequiredActionReference
                {
                    TargetSlotKey = value.TargetSlotKey,
                    GlobalActionKey = value.GlobalActionKey,
                    ActionSignature = value.ActionSignature,
                    OriginalIdentifier = value.OriginalIdentifier,
                    ExpectedOwnership = value.ExpectedOwnership,
                    TargetWebUrl = value.TargetWebUrl,
                    TargetServerRelativeUrl = value.TargetServerRelativeUrl
                }).ToList()
            };
            Validate(reference);
            return reference;
        }

        public static void Validate(SharedTopologyPageReference reference)
        {
            if (reference == null
                || !string.Equals(reference.SchemaVersion, "pnp-shared-topology-page-reference/v3", StringComparison.Ordinal)
                || !MigrationActionSignature.IsSha256(reference.SharedPlanDigest)
                || !MigrationActionSignature.IsSha256(reference.GlobalActionDagDigest)
                || !MigrationActionSignature.IsSha256(reference.ActionPlanDigest)
                || !MigrationActionSignature.IsSha256(reference.ExecutionGroupDigest)
                || !MigrationActionSignature.IsSha256(reference.SupportCohortDigest)
                || reference.SourceSiteId == Guid.Empty
                || reference.SourceWebId == Guid.Empty
                || reference.SourceFidelity == null
                || reference.SourceFidelity.Count < 2
                || reference.RequiredActions == null
                || reference.RequiredActions.Count == 0
                || string.IsNullOrWhiteSpace(reference.TargetLeafContainerIngredientId)
                || string.IsNullOrWhiteSpace(reference.TargetLeafGlobalActionKey))
            {
                throw new InvalidDataException("The shared topology page reference is incomplete or has an unsupported schema.");
            }
            SharedTopologyPath.ValidateUrlMatchesPath(reference.TargetWebUrl, reference.TargetServerRelativeUrl, nameof(reference.TargetWebUrl));
            var fidelityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fidelity in reference.SourceFidelity)
            {
                if (fidelity == null
                    || string.IsNullOrWhiteSpace(fidelity.IngredientId)
                    || !fidelityIds.Add(fidelity.IngredientId)
                    || string.IsNullOrWhiteSpace(fidelity.SourceOwnerKey)
                    || !MigrationActionSignature.IsSha256(fidelity.EvidenceDigest))
                {
                    throw new InvalidDataException("The shared topology page reference has invalid source fidelity coverage.");
                }
                if (fidelity.State == SourceWebFidelityState.AuthorizationBlocked)
                {
                    BoundLiteralHttpAuthorizationEvidence.Validate(
                        fidelity.AuthorizationEvidence,
                        PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadActionId);
                }
                else if (fidelity.AuthorizationEvidence != null)
                {
                    throw new InvalidDataException("Captured source fidelity cannot carry authorization evidence.");
                }
            }
            var actionKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < reference.RequiredActions.Count; index++)
            {
                var action = reference.RequiredActions[index];
                if (action == null
                    || string.IsNullOrWhiteSpace(action.TargetSlotKey)
                    || string.IsNullOrWhiteSpace(action.GlobalActionKey)
                    || !actionKeys.Add(action.GlobalActionKey)
                    || string.IsNullOrWhiteSpace(action.OriginalIdentifier))
                {
                    throw new InvalidDataException("The shared topology page reference has a missing or duplicate required action.");
                }
                MigrationActionSignature.Validate(action.ActionSignature);
                if (!string.Equals(action.ActionSignature.TargetIdentity, action.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(action.GlobalActionKey, SharedTopologyIdentity.GlobalAction(action.ActionSignature), StringComparison.Ordinal)
                    || index == 0 && action.ActionSignature.DependencySignatures.Count != 0
                    || index > 0 && (action.ActionSignature.DependencySignatures.Count != 1
                        || !action.ActionSignature.DependencySignatures.Contains(
                            reference.RequiredActions[index - 1].ActionSignature.Signature,
                            StringComparer.OrdinalIgnoreCase))
                    || !Enum.IsDefined(typeof(SharedTopologyOwnership), action.ExpectedOwnership))
                {
                    throw new InvalidDataException("A required page topology action differs from its generic action signature or parent edge.");
                }
                SharedTopologyPath.ValidateUrlMatchesPath(action.TargetWebUrl, action.TargetServerRelativeUrl, nameof(action.TargetWebUrl));
            }
            var leaf = reference.RequiredActions.Last();
            if (!string.Equals(leaf.GlobalActionKey, reference.TargetLeafGlobalActionKey, StringComparison.Ordinal)
                || !SharedTopologyPath.EqualsUrl(leaf.TargetWebUrl, reference.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(leaf.TargetServerRelativeUrl, reference.TargetServerRelativeUrl))
            {
                throw new InvalidDataException("The shared topology page reference leaf differs from its required action chain.");
            }
        }

        public static void ValidateReceipt(
            SharedTopologyPageReference reference,
            IEnumerable<SharedTopologyPlan> sourcePlans,
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalActionPlan actionPlan,
            SharedTopologyGlobalMaterializationReceipt receipt)
        {
            Validate(reference);
            var plans = (sourcePlans ?? Enumerable.Empty<SharedTopologyPlan>()).ToArray();
            var plan = plans.SingleOrDefault(value => string.Equals(value.PlanDigest, reference.SharedPlanDigest, StringComparison.OrdinalIgnoreCase));
            if (plan == null
                || !string.Equals(reference.GlobalActionDagDigest, dag?.DagDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.ActionPlanDigest, actionPlan?.ActionPlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.ExecutionGroupDigest, plan.ExecutionGroupDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.SupportCohortDigest, plan.SupportCohortDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The page reference does not match the supplied shared plan, DAG, or action plan approval boundary.");
            }
            SharedTopologyGlobalExecutionValidator.ValidateReceipt(plans, dag, actionPlan, receipt);
            var plannedFidelity = plan.SourceWebFidelityIngredients
                .ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            if (reference.SourceFidelity.Count != plannedFidelity.Count)
            {
                throw new InvalidDataException("The page reference does not cover its shared plan source fidelity exactly.");
            }
            foreach (var fidelity in reference.SourceFidelity)
            {
                if (!plannedFidelity.TryGetValue(fidelity.IngredientId, out var planned)
                    || !string.Equals(fidelity.SourceOwnerKey, planned.SourceOwnerKey, StringComparison.Ordinal)
                    || fidelity.SourceWebId != planned.SourceWebId
                    || !SharedTopologyPath.EqualsPath(fidelity.SourceServerRelativeUrl, planned.SourceServerRelativeUrl)
                    || fidelity.State != planned.State
                    || !string.Equals(fidelity.EvidenceDigest, planned.EvidenceSha256, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        fidelity.AuthorizationEvidence?.EvidenceSha256,
                        planned.AuthorizationEvidence?.EvidenceSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A page source-fidelity reference differs from its sealed shared plan ingredient.");
                }
            }
            var planActions = plan.TargetWebContainers.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            var approvedActions = actionPlan.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            var receiptActions = receipt.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            if (reference.RequiredActions.Count != planActions.Count
                || !new HashSet<string>(reference.RequiredActions.Select(value => value.GlobalActionKey), StringComparer.Ordinal)
                    .SetEquals(planActions.Keys))
            {
                throw new InvalidDataException("The page reference does not cover its shared plan execution group exactly.");
            }
            foreach (var required in reference.RequiredActions)
            {
                if (!planActions.TryGetValue(required.GlobalActionKey, out var planned)
                    || !approvedActions.TryGetValue(required.GlobalActionKey, out var approved)
                    || !receiptActions.TryGetValue(required.GlobalActionKey, out var actual)
                    || !string.Equals(required.TargetSlotKey, planned.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(required.ActionSignature.Signature, planned.ActionSignature.Signature, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(required.ActionSignature.Signature, approved.ActionSignature.Signature, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(required.ActionSignature.Signature, actual.ActionSignature, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(required.OriginalIdentifier, planned.OriginalIdentifier, StringComparison.Ordinal)
                    || required.ExpectedOwnership != planned.ExpectedOwnership
                    || actual.Ownership != required.ExpectedOwnership
                    || !actual.FreshReadbackPassed)
                {
                    throw new InvalidDataException("A required page topology action differs across reference, plan, approval, and receipt.");
                }
            }
            var leaf = receiptActions[reference.TargetLeafGlobalActionKey];
            if (!string.Equals(
                planActions[reference.TargetLeafGlobalActionKey].IngredientId,
                reference.TargetLeafContainerIngredientId,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("The page reference leaf ingredient differs from the supplied shared plan.");
            }
            var mappings = receipt.SourceWebMappings.Where(value => value != null
                && value.SourceSiteId == reference.SourceSiteId
                && value.SourceWebId == reference.SourceWebId).ToArray();
            if (mappings.Length != 1
                || mappings[0].TargetGlobalActionKey != reference.TargetLeafGlobalActionKey
                || mappings[0].TargetSiteId != leaf.TargetSiteId
                || mappings[0].TargetWebId != leaf.TargetWebId
                || !SharedTopologyPath.EqualsUrl(leaf.TargetWebUrl, reference.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(leaf.TargetServerRelativeUrl, reference.TargetServerRelativeUrl))
            {
                throw new InvalidDataException("The complete shared topology receipt omits the page's exact source-to-target leaf mapping.");
            }
        }
    }
}
