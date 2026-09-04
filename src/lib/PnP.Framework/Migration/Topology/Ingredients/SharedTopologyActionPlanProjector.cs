using System;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyActionPlanProjector
    {
        public static SharedTopologyActionPlan Project(SharedTopologyPlan plan, SharedTopologyTargetAnalysis analysis)
        {
            SharedTopologyPlanValidator.Validate(plan);
            SharedTopologyExecutionValidator.ValidateAnalysis(plan, analysis);
            var result = new SharedTopologyActionPlan
            {
                SharedTopologyPlanDigest = plan.PlanDigest,
                TargetAnalysisDigest = analysis.AnalysisDigest,
                Actions = analysis.TargetWebContainers.Select(probe => new SharedTopologyIngredientAction
                {
                    IngredientId = probe.IngredientId,
                    ParentIngredientId = probe.ParentIngredientId,
                    TargetWebUrl = probe.TargetWebUrl,
                    TargetServerRelativeUrl = probe.TargetServerRelativeUrl,
                    SourceState = probe.State,
                    Action = ToAction(probe.State),
                    Reason = Reason(probe.State),
                    CauseIngredientIds = probe.CauseIngredientIds.OrderBy(value => value, StringComparer.Ordinal).ToList()
                }).OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                    .ThenBy(value => value.TargetServerRelativeUrl, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            result.ActionPlanDigest = SharedTopologyExecutionDigest.ComputeActionPlan(result);
            SharedTopologyExecutionValidator.ValidateActionPlan(plan, analysis, result);
            return result;
        }

        private static SharedTopologyActionKind ToAction(TargetWebContainerState state)
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

        private static string Reason(TargetWebContainerState state)
        {
            switch (state)
            {
                case TargetWebContainerState.Reuse:
                    return "Reuse the exact inspected target Web container.";
                case TargetWebContainerState.CreateMissing:
                    return "Create the missing exact-path target Web with reviewed target-only provisioning values.";
                case TargetWebContainerState.AuthorizationBlocked:
                    return "The target Web container returned literal HTTP 401/403.";
                case TargetWebContainerState.RetryableFailure:
                    return "The target Web inspection failed with a retryable response; it is not classified as authorization blocked.";
                case TargetWebContainerState.SkippedByDependency:
                    return "A hard-required target parent container is not actionable.";
                default:
                    return "The target Web container is not executable under the reviewed plan.";
            }
        }
    }
}
