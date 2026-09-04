using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Pages.ClassicWebParts.Bindings;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Topology.Ingredients;
using PnP.Framework.Migration.Pages.Ingredients;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Planning
{
    internal sealed class PublishingPageDependencyPlan
    {
        public TopologyPlan Topology { get; set; }

        public TopologyTargetAnalysis TopologyTargetAnalysis { get; set; }

        public SharedTopologyPageReference SharedTopologyReference { get; set; }

        public CanonicalPageIngredientGraph IngredientGraph { get; set; }

        public ListMigrationPlanSet ListMigration { get; set; }

        public IList<ClassicWebPartAction> WebPartActions { get; set; } = new List<ClassicWebPartAction>();
    }
}
