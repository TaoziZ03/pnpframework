using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;

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
                || !string.Equals(analysis.SchemaVersion, "pnp-shared-topology-global-target-analysis/v1", StringComparison.Ordinal)
                || analysis.Probes == null
                || analysis.Probes.Any(value => value == null)
                || analysis.Issues == null
                || !string.Equals(analysis.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology target analysis is missing or references another global action DAG.");
            }
            ValidateCoverage(dag.Actions.Select(value => value.GlobalActionKey), analysis.Probes.Select(value => value?.GlobalActionKey), "target analysis");
            if (!string.Equals(analysis.AnalysisDigest, SharedTopologyGlobalExecutionDigest.ComputeAnalysis(analysis), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology target analysis digest is stale.");
            }
            foreach (var probe in analysis.Probes)
            {
                if (probe.State == TargetWebContainerState.AuthorizationBlocked)
                {
                    PnP.Framework.Migration.Evidence.LiteralHttpAuthorizationEvidence.Validate(probe.AuthorizationEvidence);
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
                || !string.Equals(plan.SchemaVersion, "pnp-shared-topology-global-action-plan/v1", StringComparison.Ordinal)
                || plan.Actions == null
                || plan.Actions.Any(value => value == null)
                || !string.Equals(plan.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plan.TargetAnalysisDigest, analysis.AnalysisDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology action plan is missing or stale.");
            }
            ValidateCoverage(dag.Actions.Select(value => value.GlobalActionKey), plan.Actions.Select(value => value?.GlobalActionKey), "action plan");
            var containers = dag.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            var probes = analysis.Probes.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            foreach (var action in plan.Actions)
            {
                var container = containers[action.GlobalActionKey];
                var probe = probes[action.GlobalActionKey];
                if (!string.Equals(action.TargetSlotKey, container.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(action.ParentGlobalActionKey, container.ParentGlobalActionKey, StringComparison.Ordinal)
                    || !string.Equals(action.ActionSignatureDigest, container.ActionSignatureDigest, StringComparison.OrdinalIgnoreCase)
                    || action.ReviewedState != probe.State
                    || action.SelectedAction != ExpectedAction(probe.State)
                    || action.ApprovedExistingTargetWebId != container.ApprovedExistingTargetWebId)
                {
                    throw new InvalidDataException("A shared topology global action differs from its target slot, signature, or reviewed probe.");
                }
            }
            if (!string.Equals(plan.ActionPlanDigest, SharedTopologyGlobalExecutionDigest.ComputeActionPlan(plan), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology action-plan digest is stale.");
            }
        }

        public static void ValidateReceipt(
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalActionPlan actionPlan,
            SharedTopologyGlobalMaterializationReceipt receipt)
        {
            ValidateDag(dag);
            if (actionPlan == null
                || !string.Equals(actionPlan.SchemaVersion, "pnp-shared-topology-global-action-plan/v1", StringComparison.Ordinal)
                || actionPlan.Actions == null
                || !string.Equals(actionPlan.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase)
                || actionPlan.Actions.Any(value => value == null
                    || value.SelectedAction != ExpectedAction(value.ReviewedState))
                || !string.Equals(
                    actionPlan.ActionPlanDigest,
                    SharedTopologyGlobalExecutionDigest.ComputeActionPlan(actionPlan),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology action plan is missing, stale, or internally inconsistent.");
            }
            if (receipt == null
                || !string.Equals(receipt.SchemaVersion, "pnp-shared-topology-global-receipt/v1", StringComparison.Ordinal)
                || receipt.OperationId == Guid.Empty
                || receipt.StartedAtUtc == default(DateTimeOffset)
                || receipt.CompletedAtUtc < receipt.StartedAtUtc
                || receipt.Actions == null
                || receipt.Actions.Any(value => value == null)
                || receipt.SourceWebMappings == null
                || receipt.SourceWebMappings.Count == 0
                || receipt.Diagnostics == null
                || !receipt.FreshReadbackPassed
                || !string.Equals(receipt.GlobalActionDagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(receipt.ActionPlanDigest, actionPlan.ActionPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology receipt is incomplete, unverified, or references another action plan.");
            }
            ValidateCoverage(actionPlan.Actions.Select(value => value.GlobalActionKey), receipt.Actions.Select(value => value?.GlobalActionKey), "receipt");
            var actionByKey = actionPlan.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            var containerByKey = dag.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            var receiptByKey = receipt.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            var targetWebIds = new HashSet<Guid>();
            foreach (var item in receipt.Actions)
            {
                var action = actionByKey[item.GlobalActionKey];
                var container = containerByKey[item.GlobalActionKey];
                var expectedOwnership = action.SelectedAction == SharedTopologyActionKind.ReuseExplicitApprovedHost
                    ? SharedTopologyOwnership.ExternalApprovedHost
                    : SharedTopologyOwnership.MigrationOwned;
                var templateParts = (container.Provisioning.Template ?? string.Empty).Split('#');
                var expectedTemplate = templateParts[0];
                var expectedConfiguration = templateParts.Length > 1
                    && int.TryParse(
                        templateParts[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedConfiguration)
                        ? parsedConfiguration
                        : container.Provisioning.Configuration;
                if (item.TargetSiteId == Guid.Empty
                    || item.TargetWebId == Guid.Empty
                    || item.TargetParentWebId == Guid.Empty
                    || !targetWebIds.Add(item.TargetWebId)
                    || !item.FreshReadbackPassed
                    || item.SelectedAction != action.SelectedAction
                    || item.Ownership != expectedOwnership
                    || !string.Equals(item.TargetSlotKey, container.TargetSlotKey, StringComparison.Ordinal)
                    || !string.Equals(item.ActionSignatureDigest, container.ActionSignatureDigest, StringComparison.OrdinalIgnoreCase)
                    || !SharedTopologyPath.EqualsUrl(item.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(item.TargetServerRelativeUrl, container.TargetServerRelativeUrl)
                    || expectedOwnership == SharedTopologyOwnership.MigrationOwned
                        && (item.FinalState != TargetWebContainerState.ReuseOwned
                            || !string.Equals(item.ObservedOriginalIdentifier, container.OriginalIdentifier, StringComparison.Ordinal)
                            || !string.Equals(item.ObservedMappingDigest, container.ActionSignatureDigest, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(item.ObservedTitle, container.Provisioning.Title, StringComparison.Ordinal)
                            || !string.Equals(item.ObservedDescription, PathDerivedTopologyTargetAnalyzer.InterruptedCreateDescription(container), StringComparison.Ordinal)
                            || !string.Equals(item.ObservedTemplate, expectedTemplate, StringComparison.OrdinalIgnoreCase)
                            || item.ObservedConfiguration != expectedConfiguration
                            || item.ObservedHasUniqueRoleAssignments != !container.Provisioning.UseSamePermissionsAsParentWeb)
                    || expectedOwnership == SharedTopologyOwnership.ExternalApprovedHost
                        && (item.FinalState != TargetWebContainerState.ReuseExplicitApprovedHost
                            || item.TargetWebId != container.ApprovedExistingTargetWebId
                            || !string.IsNullOrWhiteSpace(item.ObservedOriginalIdentifier)
                            || !string.IsNullOrWhiteSpace(item.ObservedMappingDigest)))
                {
                    throw new InvalidDataException("A shared topology action receipt differs from its approved action or target container.");
                }
                if (container.ExpectedTargetSiteId.HasValue
                    && item.TargetSiteId != container.ExpectedTargetSiteId.Value)
                {
                    throw new InvalidDataException("A shared topology action receipt differs from the expected target Site identity.");
                }
                if (!string.IsNullOrWhiteSpace(container.ParentGlobalActionKey))
                {
                    var parent = receiptByKey[container.ParentGlobalActionKey];
                    if (item.TargetSiteId != parent.TargetSiteId
                        || item.TargetParentWebId != parent.TargetWebId)
                    {
                        throw new InvalidDataException("A shared topology action receipt differs from its verified direct parent identity.");
                    }
                }
            }
            var sourceIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in receipt.SourceWebMappings)
            {
                if (mapping == null
                    || mapping.SourceSiteId == Guid.Empty
                    || mapping.SourceWebId == Guid.Empty
                    || !sourceIdentities.Add(mapping.SourceSiteId.ToString("D") + "/" + mapping.SourceWebId.ToString("D"))
                    || !receiptByKey.TryGetValue(mapping.TargetGlobalActionKey ?? string.Empty, out var target)
                    || mapping.TargetSiteId != target.TargetSiteId
                    || mapping.TargetWebId != target.TargetWebId
                    || mapping.Ownership != target.Ownership
                    || !SharedTopologyPath.EqualsUrl(mapping.TargetWebUrl, target.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(mapping.TargetServerRelativeUrl, target.TargetServerRelativeUrl))
                {
                    throw new InvalidDataException("A shared topology source-Web mapping receipt is missing, duplicated, or differs from its global action receipt.");
                }
            }
            if (!string.Equals(receipt.ReceiptDigest, SharedTopologyGlobalExecutionDigest.ComputeReceipt(receipt), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology receipt digest is stale.");
            }
        }

        internal static void ValidateDag(SharedTopologyGlobalActionDag dag)
        {
            if (dag == null
                || !string.Equals(dag.SchemaVersion, "pnp-shared-topology-global-action-dag/v1", StringComparison.Ordinal)
                || dag.Actions == null
                || dag.SourcePlanDigests == null
                || dag.SourcePlanDigests.Count == 0
                || dag.SupportCohortSignatures == null
                || dag.SupportCohortSignatures.Count == 0
                || dag.Actions.Any(value => value == null
                    || string.IsNullOrWhiteSpace(value.TargetSlotKey)
                    || string.IsNullOrWhiteSpace(value.GlobalActionKey)
                    || string.IsNullOrWhiteSpace(value.ActionSignatureDigest)
                    || !string.Equals(
                        value.GlobalActionKey,
                        SharedTopologyIdentity.GlobalAction(value.TargetSlotKey, value.ActionSignatureDigest),
                        StringComparison.Ordinal))
                || dag.Actions.Select(value => value.TargetSlotKey).Distinct(StringComparer.Ordinal).Count() != dag.Actions.Count
                || dag.Actions.Select(value => value.GlobalActionKey).Distinct(StringComparer.Ordinal).Count() != dag.Actions.Count
                || dag.Actions.Any(value => !string.IsNullOrWhiteSpace(value.ParentGlobalActionKey)
                    && !dag.Actions.Any(parent => string.Equals(parent.GlobalActionKey, value.ParentGlobalActionKey, StringComparison.Ordinal)))
                || !string.Equals(dag.DagDigest, SharedTopologyGlobalActionDagCompiler.ComputeDigest(dag), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology global action DAG is missing or stale.");
            }
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
    }
}
