using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyGlobalExecutionValidator
    {
        public static void ValidateAnalysis(
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis analysis)
        {
            ValidateDag(dag);
            if (analysis == null
                || !string.Equals(analysis.SchemaVersion, "pnp-shared-topology-global-target-analysis/v3", StringComparison.Ordinal)
                || analysis.Probes == null
                || analysis.Probes.Any(value => value == null)
                || analysis.Issues == null
                || !string.Equals(analysis.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology target analysis is missing or references another global action DAG.");
            }
            ValidateCoverage(dag.Actions.Select(value => value.LogicalActionKey), analysis.Probes.Select(value => value.LogicalActionKey), "target analysis");
            if (!string.Equals(analysis.AnalysisDigest, SharedTopologyGlobalExecutionDigest.ComputeAnalysis(analysis), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology target analysis digest is stale.");
            }
            var containers = dag.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            foreach (var probe in analysis.Probes)
            {
                var container = containers[probe.LogicalActionKey];
                if (!string.Equals(probe.TargetSlotKey, container.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(probe.ParentLogicalActionKey, container.ParentLogicalActionKey, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A shared topology target probe differs from its global action identity.");
                }
                if (probe.State == TargetWebContainerState.AuthorizationBlocked)
                {
                    BoundLiteralHttpAuthorizationEvidence.Validate(
                        probe.AuthorizationEvidence,
                        container.LogicalActionKey,
                        PathDerivedTopologyTargetAnalyzer.TargetInspectionOperation,
                        new Uri(PathDerivedTopologyTargetAnalyzer.ExpectedInspectionRequestUri(container)).Authority,
                        PathDerivedTopologyTargetAnalyzer.ExpectedInspectionRequestUri(container));
                }
                if ((probe.State == TargetWebContainerState.ReuseOwned
                        || probe.State == TargetWebContainerState.ReuseExplicitApprovedHost)
                    && !string.Equals(probe.ObservedStateDigest, SharedTopologyDigest.ComputeObservedSemanticState(container), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A reusable target probe differs from its generic action semantic digest.");
                }
            }
        }

        public static void ValidateActionPlan(
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis analysis,
            SharedTopologyGlobalActionPlan plan)
        {
            ValidateAnalysis(dag, analysis);
            if (plan == null
                || !string.Equals(plan.SchemaVersion, "pnp-shared-topology-global-action-plan/v3", StringComparison.Ordinal)
                || plan.Actions == null
                || plan.Actions.Any(value => value == null)
                || !string.Equals(plan.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plan.TargetAnalysisDigest, analysis.AnalysisDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology action plan is missing or stale.");
            }
            ValidateCoverage(dag.Actions.Select(value => value.LogicalActionKey), plan.Actions.Select(value => value.LogicalActionKey), "action plan");
            var containers = dag.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var probes = analysis.Probes.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            foreach (var action in plan.Actions)
            {
                var container = containers[action.LogicalActionKey];
                var probe = probes[action.LogicalActionKey];
                MigrationActionSignature.Validate(action.ExecutionGrant);
                if (!string.Equals(action.TargetSlotKey, container.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(action.ParentLogicalActionKey, container.ParentLogicalActionKey, StringComparison.Ordinal)
                    || !container.ExecutionGrants.Any(value => string.Equals(value.Signature, action.ExecutionGrant.Signature, StringComparison.OrdinalIgnoreCase))
                    || action.ReviewedState != probe.State
                    || action.SelectedAction != ExpectedAction(probe.State)
                    || action.ExpectedOwnership != container.ExpectedOwnership
                    || action.ApprovedExistingTargetWebId != container.ApprovedExistingTargetWebId)
                {
                    throw new InvalidDataException("A shared topology global action differs from its slot, generic signature, ownership, or reviewed probe.");
                }
            }
            if (!string.Equals(plan.ActionPlanDigest, SharedTopologyGlobalExecutionDigest.ComputeActionPlan(plan), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology action-plan digest is stale.");
            }
        }

        public static void ValidateReceipt(
            IEnumerable<SharedTopologyPlan> sourcePlans,
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalActionPlan actionPlan,
            SharedTopologyGlobalMaterializationReceipt receipt)
        {
            ValidateDag(dag);
            var plans = ValidateSourcePlans(sourcePlans, dag);
            ValidateActionPlanShape(dag, actionPlan);
            if (receipt == null
                || !string.Equals(receipt.SchemaVersion, "pnp-shared-topology-global-receipt/v3", StringComparison.Ordinal)
                || receipt.OperationId == Guid.Empty
                || receipt.StartedAtUtc == default(DateTimeOffset)
                || receipt.CompletedAtUtc < receipt.StartedAtUtc
                || receipt.SourcePlanDigests == null
                || !IsDistinctDigestSet(receipt.SourcePlanDigests)
                || receipt.ExecutionGroupDigests == null
                || !IsDistinctDigestSet(receipt.ExecutionGroupDigests)
                || receipt.SupportCohortDigests == null
                || !IsDistinctDigestSet(receipt.SupportCohortDigests)
                || receipt.Actions == null
                || receipt.Actions.Any(value => value == null)
                || receipt.SourceWebMappings == null
                || receipt.SourceWebMappings.Count == 0
                || receipt.SourceWebMappings.Any(value => value == null || string.IsNullOrWhiteSpace(value.SourceOwnerKey))
                || receipt.Diagnostics == null
                || !receipt.FreshReadbackPassed
                || !SequenceSetEquals(receipt.SourcePlanDigests, dag.SourcePlanDigests)
                || !SequenceSetEquals(receipt.ExecutionGroupDigests, dag.ExecutionGroupDigests)
                || !SequenceSetEquals(receipt.SupportCohortDigests, dag.SupportCohortDigests)
                || !string.Equals(receipt.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(receipt.ActionPlanDigest, actionPlan.ActionPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology receipt is incomplete, unverified, or references another approval boundary.");
            }
            ValidateCoverage(actionPlan.Actions.Select(value => value.LogicalActionKey), receipt.Actions.Select(value => value.LogicalActionKey), "receipt");
            var actionByKey = actionPlan.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var containerByKey = dag.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var receiptByKey = receipt.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var targetWebIds = new HashSet<Guid>();
            foreach (var item in receipt.Actions)
            {
                var action = actionByKey[item.LogicalActionKey];
                var container = containerByKey[item.LogicalActionKey];
                if (item.TargetSiteId == Guid.Empty
                    || item.TargetWebId == Guid.Empty
                    || !targetWebIds.Add(item.TargetWebId)
                    || !item.FreshReadbackPassed
                    || item.SelectedAction != action.SelectedAction
                    || item.Ownership != container.ExpectedOwnership
                    || !string.Equals(item.TargetSlotKey, container.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(item.ExecutionGrantSignature, action.ExecutionGrant.Signature, StringComparison.OrdinalIgnoreCase)
                    || !SharedTopologyPath.EqualsUrl(item.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(item.TargetServerRelativeUrl, container.TargetServerRelativeUrl)
                    || !string.Equals(item.ObservedStateDigest, SharedTopologyDigest.ComputeObservedSemanticState(container), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(item.ReceiptDigest, SharedTopologyGlobalExecutionDigest.ComputeActionReceipt(item), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A shared topology action receipt differs from its approved generic action or target slot.");
                }
                ValidateActionObservedState(container, item);
                ValidateExecutionOutcome(container, item);
                ValidateVerificationCheckpoint(receipt, action.ExecutionGrant, item);
                if (container.IsTargetSiteRoot)
                {
                    if (item.TargetParentWebId != Guid.Empty || item.TargetWebId != container.ApprovedExistingTargetWebId)
                    {
                        throw new InvalidDataException("The target root receipt differs from its exact Site/root fence.");
                    }
                }
                else
                {
                    var parent = receiptByKey[container.ParentLogicalActionKey];
                    if (item.TargetParentWebId == Guid.Empty
                        || item.TargetSiteId != parent.TargetSiteId
                        || item.TargetParentWebId != parent.TargetWebId)
                    {
                        throw new InvalidDataException("A shared topology action receipt differs from its verified direct parent identity.");
                    }
                }
            }

            ValidateSourceMappings(plans, receipt, receiptByKey);
            var expectedLimited = plans.SelectMany(value => value.SourceWebFidelityIngredients)
                .Any(value => value.State == SourceWebFidelityState.AuthorizationBlocked);
            if (receipt.SourceFidelityAuthorizationLimited != expectedLimited
                || !string.Equals(receipt.ReceiptDigest, SharedTopologyGlobalExecutionDigest.ComputeReceipt(receipt), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology aggregate receipt has stale fidelity or digest evidence.");
            }
        }

        internal static void ValidateDag(SharedTopologyGlobalActionDag dag)
        {
            if (dag == null
                || !string.Equals(dag.SchemaVersion, SharedTopologyGlobalActionDag.CurrentSchemaVersion, StringComparison.Ordinal)
                || dag.Actions == null
                || dag.Actions.Count == 0
                || dag.SourcePlanDigests == null
                || dag.SourcePlanDigests.Count == 0
                || !IsDistinctDigestSet(dag.SourcePlanDigests)
                || dag.ExecutionGroupDigests == null
                || dag.ExecutionGroupDigests.Count == 0
                || !IsDistinctDigestSet(dag.ExecutionGroupDigests)
                || dag.SupportCohortDigests == null
                || dag.SupportCohortDigests.Count == 0
                || !IsDistinctDigestSet(dag.SupportCohortDigests)
                || dag.Actions.Any(value => value == null)
                || dag.Actions.Select(value => value.TargetSlotKey).Distinct(StringComparer.Ordinal).Count() != dag.Actions.Count
                || dag.Actions.Select(value => value.LogicalActionKey).Distinct(StringComparer.Ordinal).Count() != dag.Actions.Count
                || !string.Equals(dag.DagDigest, SharedTopologyGlobalActionDagCompiler.ComputeDigest(dag), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology global action DAG is missing, structurally invalid, or stale.");
            }
            var actionKeys = new HashSet<string>(dag.Actions.Select(value => value.LogicalActionKey), StringComparer.Ordinal);
            foreach (var action in dag.Actions)
            {
                if (action.ExecutionGrants == null
                    || action.ExecutionGrants.Count == 0
                    || action.ExecutionGrants.Select(value => value.Signature).Distinct(StringComparer.OrdinalIgnoreCase).Count() != action.ExecutionGrants.Count
                    || !string.Equals(action.LogicalActionDigest, SharedTopologyDigest.ComputeLogicalAction(action), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(action.LogicalActionKey, SharedTopologyIdentity.LogicalAction(action.LogicalActionDigest), StringComparison.Ordinal)
                    || !string.IsNullOrWhiteSpace(action.ParentLogicalActionKey) && !actionKeys.Contains(action.ParentLogicalActionKey))
                {
                    throw new InvalidDataException("A global topology action has a stale normalized identity, grant set, or parent edge.");
                }
                var expectedDependencies = action.IsTargetSiteRoot
                    ? Array.Empty<string>()
                    : new[] { dag.Actions.Single(value => value.LogicalActionKey == action.ParentLogicalActionKey).LogicalActionDigest };
                foreach (var grant in action.ExecutionGrants)
                {
                    MigrationActionSignature.Validate(grant);
                    if (!string.Equals(grant.ActionId, "topology.target-web." + SharedTopologyIdentity.StableDigest(action.LogicalActionKey), StringComparison.Ordinal)
                        || !string.Equals(grant.ActionKind, action.IsTargetSiteRoot ? "Topology.TargetSiteRoot" : "Topology.ChildWeb", StringComparison.Ordinal)
                        || !string.Equals(grant.TargetIdentity, action.TargetSlotKey, StringComparison.Ordinal)
                        || !string.Equals(grant.SemanticDigest, SharedTopologyDigest.ComputeObservedSemanticState(action), StringComparison.OrdinalIgnoreCase)
                        || !grant.DependencySignatures.SequenceEqual(expectedDependencies, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("A global topology execution grant differs from its logical action identity or dependency edge.");
                    }
                }
            }
        }

        private static SharedTopologyPlan[] ValidateSourcePlans(
            IEnumerable<SharedTopologyPlan> sourcePlans,
            SharedTopologyGlobalActionDag dag)
        {
            var plans = (sourcePlans ?? Enumerable.Empty<SharedTopologyPlan>()).ToArray();
            if (plans.Length == 0)
            {
                throw new InvalidDataException("The source plans sealed into the global action DAG are required for receipt validation.");
            }
            foreach (var plan in plans)
            {
                SharedTopologyPlanValidator.Validate(plan);
            }
            var distinctPlanDigests = plans.Select(value => value.PlanDigest)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var compiled = SharedTopologyGlobalActionDagCompiler.Compile(plans);
            if (distinctPlanDigests != plans.Length
                || !compiled.IsExecutable
                || !string.Equals(compiled.Dag?.DagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase)
                || !SequenceSetEquals(plans.Select(value => value.PlanDigest), dag.SourcePlanDigests)
                || !SequenceSetEquals(plans.Select(value => value.ExecutionGroupDigest), dag.ExecutionGroupDigests)
                || !SequenceSetEquals(plans.Select(value => value.SupportCohortDigest), dag.SupportCohortDigests))
            {
                throw new InvalidDataException("The supplied distinct source plans do not recompile to the exact global action DAG.");
            }
            return plans;
        }

        internal static void ValidateActionPlanShape(
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalActionPlan actionPlan)
        {
            if (actionPlan == null
                || !string.Equals(actionPlan.SchemaVersion, "pnp-shared-topology-global-action-plan/v3", StringComparison.Ordinal)
                || actionPlan.Actions == null
                || actionPlan.Actions.Any(value => value == null)
                || !string.Equals(actionPlan.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase)
                || actionPlan.Actions.Any(value => value.SelectedAction != ExpectedAction(value.ReviewedState))
                || !string.Equals(actionPlan.ActionPlanDigest, SharedTopologyGlobalExecutionDigest.ComputeActionPlan(actionPlan), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology action plan is missing, stale, or internally inconsistent.");
            }
            ValidateCoverage(dag.Actions.Select(value => value.LogicalActionKey), actionPlan.Actions.Select(value => value.LogicalActionKey), "action plan");
            var containers = dag.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            foreach (var action in actionPlan.Actions)
            {
                var container = containers[action.LogicalActionKey];
                MigrationActionSignature.Validate(action.ExecutionGrant);
                if (!string.Equals(action.TargetSlotKey, container.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(action.ParentLogicalActionKey, container.ParentLogicalActionKey, StringComparison.Ordinal)
                    || !container.ExecutionGrants.Any(value => string.Equals(value.Signature, action.ExecutionGrant.Signature, StringComparison.OrdinalIgnoreCase))
                    || action.ExpectedOwnership != container.ExpectedOwnership
                    || action.ApprovedExistingTargetWebId != container.ApprovedExistingTargetWebId)
                {
                    throw new InvalidDataException("The action plan differs from its global DAG slot, generic signature, or ownership boundary.");
                }
            }
        }

        private static void ValidateActionObservedState(
            TargetWebContainerIngredientPlan container,
            SharedTopologyGlobalActionReceipt item)
        {
            var external = container.ExpectedOwnership == SharedTopologyOwnership.ExternalApprovedHost;
            var expectedTemplate = Template(container, out var expectedConfiguration);
            if (!string.Equals(item.ObservedTitle, container.Provisioning.Title, StringComparison.Ordinal)
                || !string.Equals(item.ObservedTemplate, expectedTemplate, StringComparison.OrdinalIgnoreCase)
                || item.ObservedConfiguration != expectedConfiguration
                || item.ObservedLanguage != container.Provisioning.Language
                || item.ObservedHasUniqueRoleAssignments != !container.Provisioning.UseSamePermissionsAsParentWeb
                || external && (item.FinalState != TargetWebContainerState.ReuseExplicitApprovedHost
                    || item.TargetWebId != container.ApprovedExistingTargetWebId
                    || !string.IsNullOrWhiteSpace(item.ObservedOriginalIdentifier)
                    || !string.IsNullOrWhiteSpace(item.ObservedMappingDigest))
                || !external && (item.FinalState != TargetWebContainerState.ReuseOwned
                    || !string.Equals(item.ObservedDescription, PathDerivedTopologyTargetAnalyzer.InterruptedCreateDescription(container), StringComparison.Ordinal)
                    || !string.Equals(item.ObservedOriginalIdentifier, container.OriginalIdentifier, StringComparison.Ordinal)
                    || !string.Equals(item.ObservedMappingDigest, container.SemanticMappingDigest, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("A shared topology action receipt differs from the expected persisted target state.");
            }
            var observation = new PathDerivedTargetWebObservation
            {
                TargetSiteId = item.TargetSiteId,
                TargetWebId = item.TargetWebId,
                TargetParentWebId = item.TargetParentWebId == Guid.Empty ? (Guid?)null : item.TargetParentWebId,
                TargetWebUrl = item.TargetWebUrl,
                TargetServerRelativeUrl = item.TargetServerRelativeUrl,
                ExistingTitle = item.ObservedTitle,
                ExistingDescription = item.ObservedDescription,
                ExistingTemplate = item.ObservedTemplate,
                ExistingConfiguration = item.ObservedConfiguration,
                ExistingLanguage = item.ObservedLanguage,
                ExistingHasUniqueRoleAssignments = item.ObservedHasUniqueRoleAssignments,
                ExistingOriginalIdentifier = item.ObservedOriginalIdentifier,
                ExistingMappingDigest = item.ObservedMappingDigest
            };
            var observedDigest = SharedTopologyDigest.ComputeObservedSemanticState(container, observation, item.Ownership);
            if (!string.Equals(observedDigest, item.ObservedStateDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A resealed action receipt does not match its own observed-state fields.");
            }
        }

        private static void ValidateExecutionOutcome(
            TargetWebContainerIngredientPlan container,
            SharedTopologyGlobalActionReceipt item)
        {
            var valid = item.SelectedAction == SharedTopologyActionKind.CreateMissing
                    && (item.MutationAttempted
                        && (item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.Applied
                            || item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.RecoveredInterruptedCreate
                            || item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged)
                        || !item.MutationAttempted
                            && item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.AlreadySatisfied)
                || item.SelectedAction == SharedTopologyActionKind.RecoverInterruptedCreate
                    && (item.MutationAttempted
                        && (item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.RecoveredInterruptedCreate
                            || item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged)
                        || !item.MutationAttempted
                            && item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.AlreadySatisfied)
                || item.SelectedAction == SharedTopologyActionKind.ReuseOwned
                    && !item.MutationAttempted
                    && item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.AlreadySatisfied
                || item.SelectedAction == SharedTopologyActionKind.ReuseExplicitApprovedHost
                    && !item.MutationAttempted
                    && item.ExecutionOutcome == SharedTopologyActionExecutionOutcome.ReusedExternal;
            if (!valid)
            {
                throw new InvalidDataException("A topology action receipt has an inconsistent mutation-attempt/outcome pair.");
            }
        }

        private static void ValidateVerificationCheckpoint(
            SharedTopologyGlobalMaterializationReceipt receipt,
            MigrationActionSignature executionGrant,
            SharedTopologyGlobalActionReceipt item)
        {
            var verification = item.VerificationCheckpoint;
            var expectedOwnership = item.Ownership == SharedTopologyOwnership.ExternalApprovedHost
                ? MigrationTargetOwnership.External
                : MigrationTargetOwnership.MigrationOwned;
            if (verification == null
                || !string.Equals(verification.SchemaVersion, MigrationMutationVerificationReceipt.CurrentSchemaVersion, StringComparison.Ordinal)
                || verification.OperationId != receipt.OperationId
                || !string.Equals(verification.PlanDigest, receipt.ActionPlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(verification.ActionId, executionGrant.ActionId, StringComparison.Ordinal)
                || !string.Equals(verification.ActionSignature, executionGrant.Signature, StringComparison.OrdinalIgnoreCase)
                || verification.VerifiedAtUtc == default(DateTimeOffset)
                || verification.VerifiedAtUtc < receipt.StartedAtUtc
                || verification.VerifiedAtUtc > receipt.CompletedAtUtc
                || !verification.FreshReadbackPassed
                || !string.Equals(verification.ObservedStateDigest, item.ObservedStateDigest, StringComparison.OrdinalIgnoreCase)
                || verification.Ownership != expectedOwnership
                || !string.Equals(verification.TargetIdentityDigest, executionGrant.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase)
                || verification.ProvenanceMatched != (item.Ownership == SharedTopologyOwnership.ExternalApprovedHost
                    || !string.IsNullOrWhiteSpace(item.ObservedOriginalIdentifier)))
            {
                throw new InvalidDataException("A shared topology action is missing its matching signed fresh-verification checkpoint.");
            }
        }

        private static void ValidateSourceMappings(
            IEnumerable<SharedTopologyPlan> plans,
            SharedTopologyGlobalMaterializationReceipt receipt,
            IReadOnlyDictionary<string, SharedTopologyGlobalActionReceipt> actions)
        {
            var expected = new Dictionary<string, SourceWebTargetContainerBinding>(StringComparer.Ordinal);
            foreach (var group in plans.SelectMany(value => value.SourceWebBindings)
                .GroupBy(value => value.SourceOwnerKey, StringComparer.Ordinal))
            {
                var identities = group.Select(SharedTopologySourceBindingIdentity.Compute)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (identities.Length != 1)
                {
                    throw new InvalidDataException("One source owner carries conflicting source or target binding evidence.");
                }
                expected.Add(group.Key, group
                    .OrderBy(MigrationContractSerializer.SerializeCanonical, StringComparer.Ordinal)
                    .First());
            }
            var actual = receipt.SourceWebMappings
                .GroupBy(value => value.SourceOwnerKey, StringComparer.Ordinal)
                .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.Ordinal);
            if (actual.Any(value => string.IsNullOrWhiteSpace(value.Key) || value.Value.Length != 1)
                || !new HashSet<string>(expected.Keys, StringComparer.Ordinal).SetEquals(actual.Keys))
            {
                throw new InvalidDataException("The shared topology source-owner mapping receipt coverage is incomplete or duplicated.");
            }
            foreach (var pair in expected)
            {
                var binding = pair.Value;
                var mapping = actual[pair.Key][0];
                if (!actions.TryGetValue(binding.TargetLogicalActionKey, out var target)
                    || mapping.SourceSiteId != binding.SourceSiteId
                    || mapping.SourceWebId != binding.SourceWebId
                    || !string.Equals(mapping.SourceWebUrl, binding.SourceWebUrl, StringComparison.OrdinalIgnoreCase)
                    || !SharedTopologyPath.EqualsPath(mapping.SourceServerRelativeUrl, binding.SourceServerRelativeUrl)
                    || !string.Equals(mapping.TargetLogicalActionKey, binding.TargetLogicalActionKey, StringComparison.Ordinal)
                    || mapping.TargetSiteId != target.TargetSiteId
                    || mapping.TargetWebId != target.TargetWebId
                    || mapping.Ownership != target.Ownership
                    || !SharedTopologyPath.EqualsUrl(mapping.TargetWebUrl, target.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(mapping.TargetServerRelativeUrl, target.TargetServerRelativeUrl)
                    || !string.Equals(mapping.ReceiptDigest, SharedTopologyGlobalExecutionDigest.ComputeSourceMappingReceipt(mapping), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A shared topology source-owner mapping differs from its plan binding or action receipt.");
                }
            }
        }

        private static string Template(TargetWebContainerIngredientPlan container, out int configuration)
        {
            var parts = (container.Provisioning.Template ?? string.Empty).Split('#');
            configuration = parts.Length > 1
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : container.Provisioning.Configuration;
            return parts[0];
        }

        private static SharedTopologyActionKind ExpectedAction(TargetWebContainerState state)
        {
            switch (state)
            {
                case TargetWebContainerState.CreateMissing:
                    return SharedTopologyActionKind.CreateMissing;
                case TargetWebContainerState.ReuseOwned:
                    return SharedTopologyActionKind.ReuseOwned;
                case TargetWebContainerState.ReuseExplicitApprovedHost:
                    return SharedTopologyActionKind.ReuseExplicitApprovedHost;
                case TargetWebContainerState.RecoverInterruptedCreate:
                    return SharedTopologyActionKind.RecoverInterruptedCreate;
                case TargetWebContainerState.SkippedByDependency:
                    return SharedTopologyActionKind.SkipByDependency;
                default:
                    return SharedTopologyActionKind.Block;
            }
        }

        private static void ValidateCoverage(IEnumerable<string> expected, IEnumerable<string> actual, string subject)
        {
            var expectedValues = (expected ?? Enumerable.Empty<string>()).ToArray();
            var actualValues = (actual ?? Enumerable.Empty<string>()).ToArray();
            var expectedSet = new HashSet<string>(expectedValues, StringComparer.Ordinal);
            var actualSet = new HashSet<string>(actualValues, StringComparer.Ordinal);
            if (expectedValues.Any(string.IsNullOrWhiteSpace)
                || actualValues.Any(string.IsNullOrWhiteSpace)
                || expectedValues.Length != expectedSet.Count
                || actualValues.Length != actualSet.Count
                || !expectedSet.SetEquals(actualSet))
            {
                throw new InvalidDataException("The shared topology " + subject + " must cover every global action exactly once.");
            }
        }

        private static bool SequenceSetEquals(IEnumerable<string> left, IEnumerable<string> right)
        {
            return new HashSet<string>(left ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase)
                .SetEquals(right ?? Enumerable.Empty<string>());
        }

        private static bool IsDistinctDigestSet(IList<string> values)
        {
            return values != null
                && values.Count > 0
                && values.All(MigrationActionSignature.IsSha256)
                && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count;
        }
    }
}
