using PnP.Framework.Migration.Taxonomy;
using System.Collections.Generic;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Topology.Ingredients;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Planning
{
    public sealed class PagePlanningOptions
    {
        public string TargetPageServerRelativeUrl { get; set; }

        public bool RequireInheritedPermissions { get; set; } = true;

        public bool BlockOnManagedMetadata { get; set; } = true;

        public bool AllowExternalResourceReferences { get; set; } = true;

        public bool CreateOnly { get; set; } = true;

        public IList<TaxonomyTargetMapping> TaxonomySchemaMappings { get; set; } = new List<TaxonomyTargetMapping>();

        public TopologyPlanningPolicy TopologyPolicy { get; set; } = new TopologyPlanningPolicy();

        public IList<ListTargetOverride> ListTargetOverrides { get; set; } = new List<ListTargetOverride>();

        /// <summary>
        /// Planning-only shared topology inputs. They are executed and receipted once
        /// at bundle scope and are intentionally not copied into each page policy.
        /// </summary>
        [JsonIgnore]
        public SharedTopologyPlan SharedTopologyPlan { get; set; }

        [JsonIgnore]
        public SharedTopologyTargetAnalysis SharedTopologyTargetAnalysis { get; set; }

        [JsonIgnore]
        public SharedTopologyActionPlan SharedTopologyActionPlan { get; set; }
    }
}
