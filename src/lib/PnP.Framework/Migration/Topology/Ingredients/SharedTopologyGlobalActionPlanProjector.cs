using System;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyGlobalActionPlanProjector
    {
        public static SharedTopologyGlobalActionPlan Project(
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis analysis)
        {
            SharedTopologyGlobalExecutionValidator.ValidateAnalysis(dag, analysis);
            var probes = analysis.Probes.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var plan = new SharedTopologyGlobalActionPlan
            {
                GlobalActionDagDigest = dag.DagDigest,
                TargetAnalysisDigest = analysis.AnalysisDigest,
                Actions = dag.Actions.Select(container =>
                {
                    var probe = probes[container.LogicalActionKey];
                    return new SharedTopologyGlobalAction
                    {
                        TargetSlotKey = container.TargetSlotKey,
                        LogicalActionKey = container.LogicalActionKey,
                        ParentLogicalActionKey = container.ParentLogicalActionKey,
                        ExecutionGrant = container.ExecutionGrants.OrderBy(value => value.Signature, StringComparer.Ordinal).First(),
                        ReviewedState = probe.State,
                        SelectedAction = SelectedAction(probe.State),
                        ExpectedOwnership = container.ExpectedOwnership,
                        ApprovedExistingTargetWebId = container.ApprovedExistingTargetWebId,
                        Reason = Reason(probe.State)
                    };
                }).OrderBy(value => value.TargetSlotKey, StringComparer.Ordinal).ToList()
            };
            plan.ActionPlanDigest = SharedTopologyGlobalExecutionDigest.ComputeActionPlan(plan);
            SharedTopologyGlobalExecutionValidator.ValidateActionPlan(dag, analysis, plan);
            return plan;
        }

        private static SharedTopologyActionKind SelectedAction(TargetWebContainerState state)
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

        private static string Reason(TargetWebContainerState state)
        {
            return state == TargetWebContainerState.CreateMissing
                ? "Create the absent target Web with the reviewed target-only signature."
                : state == TargetWebContainerState.ReuseOwned
                    ? "Reuse the exact migration-owned target Web."
                    : state == TargetWebContainerState.ReuseExplicitApprovedHost
                        ? "Reuse the exact explicitly approved external target Web."
                        : state == TargetWebContainerState.RecoverInterruptedCreate
                            ? "Complete ownership stamping for an exact interrupted create."
                            : "The target Web global action is not executable.";
        }
    }
}
