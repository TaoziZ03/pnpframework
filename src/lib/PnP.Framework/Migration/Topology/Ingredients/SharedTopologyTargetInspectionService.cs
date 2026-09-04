using System;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyTargetInspectionService
    {
        public static SharedTopologyTargetAnalysis Inspect(
            SharedTopologyPlan plan,
            ISharedTopologyTargetRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            return SharedTopologyTargetAnalyzer.Analyze(
                plan,
                runtime.InspectTargetSite(plan),
                runtime.InspectTargetWebContainers(plan));
        }
    }
}
