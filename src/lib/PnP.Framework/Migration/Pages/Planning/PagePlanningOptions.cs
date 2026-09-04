using PnP.Framework.Migration.Taxonomy;
using System.Collections.Generic;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Taxonomy.Assets;
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

        public TaxonomyAssetMappingCatalog TaxonomyAssetMappingCatalog { get; set; }

        public TopologyPlanningPolicy TopologyPolicy { get; set; } = new TopologyPlanningPolicy();

        public IList<ListTargetOverride> ListTargetOverrides { get; set; } = new List<ListTargetOverride>();

        /// <summary>
        /// Optional exact per-edge decisions for lookup values whose provider
        /// items enter the dropped-item closure. Missing active edges default to
        /// NeedsPolicyDecision; folder hierarchy decisions are structural.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IList<DroppedLookupValueDecision> DroppedLookupValueDecisions { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SharedTopologyPlan SharedTopologyPlan { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SharedTopologyGlobalActionDag SharedTopologyGlobalActionDag { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SharedTopologyGlobalTargetAnalysis SharedTopologyTargetAnalysis { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SharedTopologyGlobalActionPlan SharedTopologyActionPlan { get; set; }
    }
}
