using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyExecutionValidator
    {
        public static void ValidateAnalysis(SharedTopologyPlan plan, SharedTopologyTargetAnalysis analysis)
        {
            SharedTopologyPlanValidator.Validate(plan);
            if (analysis == null
                || !string.Equals(analysis.SchemaVersion, SharedTopologyTargetAnalysis.CurrentSchemaVersion, StringComparison.Ordinal)
                || !string.Equals(analysis.SharedTopologyPlanDigest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || analysis.TargetSite == null
                || analysis.TargetWebContainers == null
                || analysis.Issues == null)
            {
                throw new InvalidDataException("The shared topology target analysis is missing or references a different plan.");
            }
            ValidateCoverage(
                plan.TargetWebContainers.Select(value => value.IngredientId),
                analysis.TargetWebContainers.Select(value => value?.IngredientId),
                "target analysis");
            var containers = plan.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(analysis.AnalysisDigest)
                || !string.Equals(analysis.AnalysisDigest, SharedTopologyExecutionDigest.ComputeAnalysis(analysis), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology target-analysis digest is stale.");
            }
            foreach (var probe in analysis.TargetWebContainers)
            {
                if (probe == null || probe.CauseIngredientIds == null || probe.Issues == null)
                {
                    throw new InvalidDataException("A shared topology target probe is incomplete.");
                }
                var container = containers[probe.IngredientId];
                if (!string.Equals(probe.ParentIngredientId, container.ParentIngredientId, StringComparison.Ordinal)
                    || !SharedTopologyPath.EqualsUrl(probe.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(probe.TargetServerRelativeUrl, container.TargetServerRelativeUrl))
                {
                    throw new InvalidDataException("A shared topology target probe differs from its planned target-Web container.");
                }
                if (probe.State == TargetWebContainerState.AuthorizationBlocked
                    && (!probe.HttpStatusCode.HasValue || !SharedTopologyTargetAnalyzer.IsAuthorizationStatus(probe.HttpStatusCode.Value)))
                {
                    throw new InvalidDataException("AuthorizationBlocked target-Web state requires literal HTTP 401/403.");
                }
                if (probe.State == TargetWebContainerState.RetryableFailure
                    && probe.HttpStatusCode.HasValue
                    && SharedTopologyTargetAnalyzer.IsAuthorizationStatus(probe.HttpStatusCode.Value))
                {
                    throw new InvalidDataException("Literal HTTP 401/403 cannot be represented as a retryable target-Web failure.");
                }
            }
        }

        public static void ValidateActionPlan(
            SharedTopologyPlan plan,
            SharedTopologyTargetAnalysis analysis,
            SharedTopologyActionPlan actionPlan)
        {
            ValidateAnalysis(plan, analysis);
            if (actionPlan == null
                || !string.Equals(actionPlan.SchemaVersion, SharedTopologyActionPlan.CurrentSchemaVersion, StringComparison.Ordinal)
                || !string.Equals(actionPlan.SharedTopologyPlanDigest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(actionPlan.TargetAnalysisDigest, analysis.AnalysisDigest, StringComparison.OrdinalIgnoreCase)
                || actionPlan.Actions == null)
            {
                throw new InvalidDataException("The shared topology action plan is missing or references stale target analysis.");
            }
            ValidateCoverage(
                plan.TargetWebContainers.Select(value => value.IngredientId),
                actionPlan.Actions.Select(value => value?.IngredientId),
                "action plan");
            var probes = analysis.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            var containers = plan.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            foreach (var action in actionPlan.Actions)
            {
                if (action == null || action.CauseIngredientIds == null)
                {
                    throw new InvalidDataException("A shared topology ingredient action is incomplete.");
                }
                var probe = probes[action.IngredientId];
                var container = containers[action.IngredientId];
                if (action.SourceState != probe.State
                    || action.Action != ExpectedAction(probe.State)
                    || !string.Equals(action.ParentIngredientId, container.ParentIngredientId, StringComparison.Ordinal)
                    || !SharedTopologyPath.EqualsUrl(action.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(action.TargetServerRelativeUrl, container.TargetServerRelativeUrl))
                {
                    throw new InvalidDataException("A shared topology ingredient action differs from its target probe or planned container.");
                }
            }
            if (string.IsNullOrWhiteSpace(actionPlan.ActionPlanDigest)
                || !string.Equals(actionPlan.ActionPlanDigest, SharedTopologyExecutionDigest.ComputeActionPlan(actionPlan), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology action-plan digest is stale.");
            }
        }

        public static void ValidateReceipt(
            SharedTopologyPlan plan,
            SharedTopologyActionPlan actionPlan,
            SharedTopologyMaterializationReceipt receipt)
        {
            if (receipt == null
                || !string.Equals(receipt.SchemaVersion, SharedTopologyMaterializationReceipt.CurrentSchemaVersion, StringComparison.Ordinal)
                || !string.Equals(receipt.SharedTopologyPlanDigest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(receipt.ActionPlanDigest, actionPlan.ActionPlanDigest, StringComparison.OrdinalIgnoreCase)
                || receipt.Webs == null
                || receipt.Diagnostics == null)
            {
                throw new InvalidDataException("The shared topology receipt is missing or references a different approved plan.");
            }
            var executable = actionPlan.Actions.Where(value => value.Action == SharedTopologyActionKind.Reuse
                || value.Action == SharedTopologyActionKind.CreateMissing).Select(value => value.IngredientId);
            ValidateCoverage(executable, receipt.Webs.Select(value => value?.IngredientId), "materialization receipt");
            var containerById = plan.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            foreach (var web in receipt.Webs)
            {
                if (web == null
                    || web.TargetSiteId == Guid.Empty
                    || web.TargetWebId == Guid.Empty
                    || web.TargetParentWebId == Guid.Empty
                    || !containerById.TryGetValue(web.IngredientId, out var container)
                    || !SharedTopologyPath.EqualsUrl(web.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(web.TargetServerRelativeUrl, container.TargetServerRelativeUrl)
                    || !string.Equals(web.IngredientDigest, container.IngredientDigest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A shared topology Web receipt differs from its planned target container.");
                }
            }
            if (string.IsNullOrWhiteSpace(receipt.ReceiptDigest)
                || !string.Equals(receipt.ReceiptDigest, SharedTopologyExecutionDigest.ComputeReceipt(receipt), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The shared topology receipt digest is stale.");
            }
        }

        public static string ComputeAnalysisDigest(SharedTopologyTargetAnalysis analysis)
        {
            return SharedTopologyExecutionDigest.ComputeAnalysis(analysis);
        }

        public static string ComputeActionPlanDigest(SharedTopologyActionPlan actionPlan)
        {
            return SharedTopologyExecutionDigest.ComputeActionPlan(actionPlan);
        }

        public static string ComputeReceiptDigest(SharedTopologyMaterializationReceipt receipt)
        {
            return SharedTopologyExecutionDigest.ComputeReceipt(receipt);
        }

        private static void ValidateCoverage(IEnumerable<string> expected, IEnumerable<string> actual, string description)
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
                throw new InvalidDataException("The shared topology " + description + " must cover every required ingredient exactly once.");
            }
        }

        private static SharedTopologyActionKind ExpectedAction(TargetWebContainerState state)
        {
            switch (state)
            {
                case TargetWebContainerState.Reuse:
                    return SharedTopologyActionKind.Reuse;
                case TargetWebContainerState.CreateMissing:
                    return SharedTopologyActionKind.CreateMissing;
                case TargetWebContainerState.SkippedByDependency:
                    return SharedTopologyActionKind.SkipByDependency;
                default:
                    return SharedTopologyActionKind.Block;
            }
        }
    }
}
