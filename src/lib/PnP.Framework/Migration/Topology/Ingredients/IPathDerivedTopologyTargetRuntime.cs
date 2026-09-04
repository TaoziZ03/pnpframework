using System.Collections.Generic;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public interface IPathDerivedTopologyTargetRuntime
    {
        IList<PathDerivedTargetWebObservation> Inspect(
            IEnumerable<TargetWebContainerIngredientPlan> containers);

        PathDerivedTargetWebObservation Create(TargetWebContainerIngredientPlan container);

        PathDerivedTargetWebObservation RecoverOwnership(TargetWebContainerIngredientPlan container);
    }
}
