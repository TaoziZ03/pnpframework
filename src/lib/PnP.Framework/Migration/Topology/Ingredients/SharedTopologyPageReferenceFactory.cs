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
            var byAction = plan.TargetWebContainers.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            if (!byAction.TryGetValue(binding.TargetLogicalActionKey, out var leaf))
            {
                throw new InvalidDataException("The page Web binding references an unknown logical target action.");
            }
            var requiredContainers = new List<TargetWebContainerIngredientPlan>();
            for (var current = leaf; current != null; current = string.IsNullOrWhiteSpace(current.ParentLogicalActionKey)
                ? null
                : byAction[current.ParentLogicalActionKey])
            {
                requiredContainers.Add(current);
            }
            requiredContainers.Reverse();
            var dagByAction = dag.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var approvedByAction = actionPlan.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            foreach (var container in requiredContainers)
            {
                var grant = container.ExecutionGrants.Single();
                if (!dagByAction.TryGetValue(container.LogicalActionKey, out var logicalAction)
                    || !logicalAction.ExecutionGrants.Any(value => string.Equals(value.Signature, grant.Signature, StringComparison.OrdinalIgnoreCase))
                    || !approvedByAction.TryGetValue(container.LogicalActionKey, out var approved)
                    || approved.SelectedAction == SharedTopologyActionKind.Block
                    || approved.SelectedAction == SharedTopologyActionKind.SkipByDependency)
                {
                    throw new InvalidDataException("The page references a blocked logical action or an execution grant absent from the approved DAG.");
                }
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
                    SourceWebUrl = value.SourceWebUrl,
                    SourceServerRelativeUrl = value.SourceServerRelativeUrl,
                    State = value.State,
                    AuthorizationEvidence = value.AuthorizationEvidence,
                    AuthorizationOperation = value.AuthorizationOperation,
                    AuthorizationRequestUri = value.AuthorizationRequestUri,
                    EvidenceDigest = value.EvidenceSha256
                }).ToList(),
                TargetLeafContainerIngredientId = leaf.IngredientId,
                TargetLeafLogicalActionKey = leaf.LogicalActionKey,
                TargetWebUrl = leaf.TargetWebUrl,
                TargetServerRelativeUrl = leaf.TargetServerRelativeUrl,
                RequiredActions = requiredContainers.Select(value => new SharedTopologyRequiredActionReference
                {
                    TargetSlotKey = value.TargetSlotKey,
                    LogicalActionKey = value.LogicalActionKey,
                    ExecutionGrant = value.ExecutionGrants.Single(),
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
                || !string.Equals(reference.SchemaVersion, "pnp-shared-topology-page-reference/v4", StringComparison.Ordinal)
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
                || string.IsNullOrWhiteSpace(reference.TargetLeafLogicalActionKey))
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
                SharedTopologyPath.ValidateUrlMatchesPath(fidelity.SourceWebUrl, fidelity.SourceServerRelativeUrl, nameof(fidelity.SourceWebUrl));
                if (!string.Equals(
                    fidelity.SourceOwnerKey,
                    SharedTopologyIdentity.SourceOwner(fidelity.SourceWebUrl, reference.SourceSiteId, fidelity.SourceServerRelativeUrl),
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A page source-fidelity reference has a non-canonical source owner key.");
                }
                if (fidelity.State == SourceWebFidelityState.AuthorizationBlocked)
                {
                    BoundLiteralHttpAuthorizationEvidence.Validate(
                        fidelity.AuthorizationEvidence,
                        PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadActionId,
                        PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadOperation,
                        new Uri(fidelity.SourceWebUrl).Authority,
                        fidelity.AuthorizationRequestUri);
                    if (!string.Equals(fidelity.AuthorizationOperation, PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadOperation, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("A page source-fidelity authorization operation is not canonical.");
                    }
                }
                else if (fidelity.AuthorizationEvidence != null
                    || fidelity.AuthorizationOperation != null
                    || fidelity.AuthorizationRequestUri != null)
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
                    || string.IsNullOrWhiteSpace(action.LogicalActionKey)
                    || !actionKeys.Add(action.LogicalActionKey)
                    || string.IsNullOrWhiteSpace(action.OriginalIdentifier))
                {
                    throw new InvalidDataException("The shared topology page reference has a missing or duplicate required logical action.");
                }
                MigrationActionSignature.Validate(action.ExecutionGrant);
                var logicalDigest = SharedTopologyIdentity.LogicalActionDigest(action.LogicalActionKey);
                if (!string.Equals(action.ExecutionGrant.ActionId, "topology.target-web." + SharedTopologyIdentity.StableDigest(action.LogicalActionKey), StringComparison.Ordinal)
                    || !string.Equals(action.ExecutionGrant.ActionKind, index == 0 ? "Topology.TargetSiteRoot" : "Topology.ChildWeb", StringComparison.Ordinal)
                    || !string.Equals(action.ExecutionGrant.TargetIdentity, action.TargetSlotKey, StringComparison.Ordinal)
                    || index == 0 && action.ExecutionGrant.DependencySignatures.Count != 0
                    || index > 0 && (action.ExecutionGrant.DependencySignatures.Count != 1
                        || !action.ExecutionGrant.DependencySignatures.Contains(
                            SharedTopologyIdentity.LogicalActionDigest(reference.RequiredActions[index - 1].LogicalActionKey),
                            StringComparer.OrdinalIgnoreCase))
                    || !Enum.IsDefined(typeof(SharedTopologyOwnership), action.ExpectedOwnership)
                    || !MigrationActionSignature.IsSha256(logicalDigest))
                {
                    throw new InvalidDataException("A required page topology grant differs from its target slot or logical parent edge.");
                }
                SharedTopologyPath.ValidateUrlMatchesPath(action.TargetWebUrl, action.TargetServerRelativeUrl, nameof(action.TargetWebUrl));
            }
            var leaf = reference.RequiredActions.Last();
            if (!string.Equals(leaf.LogicalActionKey, reference.TargetLeafLogicalActionKey, StringComparison.Ordinal)
                || !SharedTopologyPath.EqualsUrl(leaf.TargetWebUrl, reference.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(leaf.TargetServerRelativeUrl, reference.TargetServerRelativeUrl))
            {
                throw new InvalidDataException("The shared topology page reference leaf differs from its required logical action chain.");
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
            var matchingPlans = plans.Where(value => string.Equals(value.PlanDigest, reference.SharedPlanDigest, StringComparison.OrdinalIgnoreCase)).ToArray();
            var plan = matchingPlans.Length == 1 ? matchingPlans[0] : null;
            if (plan == null
                || !string.Equals(reference.GlobalActionDagDigest, dag?.DagDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.ActionPlanDigest, actionPlan?.ActionPlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.ExecutionGroupDigest, plan.ExecutionGroupDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(reference.SupportCohortDigest, plan.SupportCohortDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The page reference does not match the supplied shared plan, DAG, or action-plan approval boundary.");
            }
            SharedTopologyGlobalExecutionValidator.ValidateReceipt(plans, dag, actionPlan, receipt);
            ValidateSourceFidelity(reference, plan);
            var planActions = plan.TargetWebContainers.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var dagActions = dag.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var approvedActions = actionPlan.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var receiptActions = receipt.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            if (reference.RequiredActions.Count != planActions.Count
                || !new HashSet<string>(reference.RequiredActions.Select(value => value.LogicalActionKey), StringComparer.Ordinal).SetEquals(planActions.Keys))
            {
                throw new InvalidDataException("The page reference does not cover its shared plan execution group exactly.");
            }
            foreach (var required in reference.RequiredActions)
            {
                if (!planActions.TryGetValue(required.LogicalActionKey, out var planned)
                    || !dagActions.TryGetValue(required.LogicalActionKey, out var logical)
                    || !approvedActions.ContainsKey(required.LogicalActionKey)
                    || !receiptActions.TryGetValue(required.LogicalActionKey, out var actual)
                    || !string.Equals(required.TargetSlotKey, planned.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(required.ExecutionGrant.Signature, planned.ExecutionGrants.Single().Signature, StringComparison.OrdinalIgnoreCase)
                    || !logical.ExecutionGrants.Any(value => string.Equals(value.Signature, required.ExecutionGrant.Signature, StringComparison.OrdinalIgnoreCase))
                    || !string.Equals(required.OriginalIdentifier, planned.OriginalIdentifier, StringComparison.Ordinal)
                    || required.ExpectedOwnership != planned.ExpectedOwnership
                    || !SharedTopologyPath.EqualsUrl(required.TargetWebUrl, planned.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(required.TargetServerRelativeUrl, planned.TargetServerRelativeUrl)
                    || actual.Ownership != required.ExpectedOwnership
                    || !actual.FreshReadbackPassed)
                {
                    throw new InvalidDataException("A required page topology action differs across reference, plan, DAG, approval, and receipt.");
                }
            }
            var leaf = receiptActions[reference.TargetLeafLogicalActionKey];
            if (!string.Equals(planActions[reference.TargetLeafLogicalActionKey].IngredientId, reference.TargetLeafContainerIngredientId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The page reference leaf ingredient differs from the supplied shared plan.");
            }
            var mappings = receipt.SourceWebMappings.Where(value => value != null
                && value.SourceSiteId == reference.SourceSiteId
                && value.SourceWebId == reference.SourceWebId).ToArray();
            if (mappings.Length != 1
                || !string.Equals(mappings[0].TargetLogicalActionKey, reference.TargetLeafLogicalActionKey, StringComparison.Ordinal)
                || mappings[0].TargetSiteId != leaf.TargetSiteId
                || mappings[0].TargetWebId != leaf.TargetWebId
                || !SharedTopologyPath.EqualsUrl(mappings[0].TargetWebUrl, reference.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(mappings[0].TargetServerRelativeUrl, reference.TargetServerRelativeUrl))
            {
                throw new InvalidDataException("The complete shared topology receipt omits the page's exact source-to-target leaf mapping.");
            }
        }

        public static IList<PathDerivedTargetWebProbe> ValidateFreshTarget(
            SharedTopologyPageReference reference,
            SharedTopologyExecutionProof proof,
            IEnumerable<PathDerivedTargetWebObservation> freshObservations)
        {
            ValidateReceipt(reference, proof?.SourcePlans, proof?.GlobalActionDag, proof?.ActionPlan, proof?.Receipt);
            var requiredKeys = new HashSet<string>(reference.RequiredActions.Select(value => value.LogicalActionKey), StringComparer.Ordinal);
            var observationValues = (freshObservations ?? Enumerable.Empty<PathDerivedTargetWebObservation>()).ToArray();
            if (observationValues.Any(value => value == null || string.IsNullOrWhiteSpace(value.LogicalActionKey)))
            {
                throw new InvalidDataException("Page admission received an unidentified fresh topology observation.");
            }
            var observations = observationValues
                .GroupBy(value => value.LogicalActionKey, StringComparer.Ordinal)
                .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.Ordinal);
            if (!requiredKeys.SetEquals(observations.Keys) || observations.Any(value => value.Value.Length != 1))
            {
                throw new InvalidDataException("Page admission requires exactly one fresh observation for every required logical topology action.");
            }
            var actions = proof.GlobalActionDag.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var prior = proof.Receipt.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var result = new List<PathDerivedTargetWebProbe>();
            Guid? expectedParentWebId = null;
            foreach (var required in reference.RequiredActions)
            {
                var action = actions[required.LogicalActionKey];
                if (!action.ExecutionGrants.Any(value => string.Equals(value.Signature, required.ExecutionGrant.Signature, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("The page execution grant is absent from its logical DAG action.");
                }
                var probe = PathDerivedTopologyTargetAnalyzer.AnalyzeContainer(action, observations[required.LogicalActionKey][0], expectedParentWebId);
                var expectedState = required.ExpectedOwnership == SharedTopologyOwnership.ExternalApprovedHost
                    ? TargetWebContainerState.ReuseExplicitApprovedHost
                    : TargetWebContainerState.ReuseOwned;
                var previous = prior[required.LogicalActionKey];
                if (probe.State != expectedState
                    || probe.Ownership != required.ExpectedOwnership
                    || !probe.TargetSiteId.HasValue
                    || !probe.TargetWebId.HasValue
                    || probe.TargetSiteId.Value != previous.TargetSiteId
                    || probe.TargetWebId.Value != previous.TargetWebId
                    || action.IsTargetSiteRoot && probe.TargetParentWebId.HasValue
                    || !action.IsTargetSiteRoot && probe.TargetParentWebId != expectedParentWebId)
                {
                    throw new InvalidDataException("A required topology ancestor drifted after the prior receipt; page import requires replan/reapproval.");
                }
                result.Add(probe);
                expectedParentWebId = probe.TargetWebId;
            }
            return result;
        }

        private static void ValidateSourceFidelity(SharedTopologyPageReference reference, SharedTopologyPlan plan)
        {
            var plannedFidelity = plan.SourceWebFidelityIngredients.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            if (reference.SourceFidelity.Count != plannedFidelity.Count)
            {
                throw new InvalidDataException("The page reference does not cover its shared plan source fidelity exactly.");
            }
            foreach (var fidelity in reference.SourceFidelity)
            {
                if (!plannedFidelity.TryGetValue(fidelity.IngredientId, out var planned)
                    || !string.Equals(fidelity.SourceOwnerKey, planned.SourceOwnerKey, StringComparison.Ordinal)
                    || fidelity.SourceWebId != planned.SourceWebId
                    || !SharedTopologyPath.EqualsUrl(fidelity.SourceWebUrl, planned.SourceWebUrl)
                    || !SharedTopologyPath.EqualsPath(fidelity.SourceServerRelativeUrl, planned.SourceServerRelativeUrl)
                    || fidelity.State != planned.State
                    || !string.Equals(fidelity.AuthorizationOperation, planned.AuthorizationOperation, StringComparison.Ordinal)
                    || !string.Equals(fidelity.AuthorizationRequestUri, planned.AuthorizationRequestUri, StringComparison.Ordinal)
                    || !string.Equals(fidelity.EvidenceDigest, planned.EvidenceSha256, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(fidelity.AuthorizationEvidence?.EvidenceSha256, planned.AuthorizationEvidence?.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A page source-fidelity reference differs from its sealed shared plan ingredient.");
                }
            }
        }
    }
}
