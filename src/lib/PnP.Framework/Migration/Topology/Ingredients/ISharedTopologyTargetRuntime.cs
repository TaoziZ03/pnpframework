using System.Collections.Generic;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    /// <summary>
    /// Runtime boundary for fresh target inspection and one-Web creation. The
    /// shared executor owns ordering, approval, journaling, receipts, and final
    /// verification; adapters own transport only.
    /// </summary>
    public interface ISharedTopologyTargetRuntime
    {
        SharedTopologyTargetSiteObservation InspectTargetSite(SharedTopologyPlan plan);

        IList<TargetWebContainerObservation> InspectTargetWebContainers(SharedTopologyPlan plan);

        TargetWebContainerObservation CreateTargetWebContainer(
            SharedTopologyPlan plan,
            TargetWebContainerIngredientPlan container);
    }
}
