using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal sealed class TopologyWebOwnerMapping
    {
        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public string SourceWebUrl { get; set; }

        public string SourceServerRelativeUrl { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetSiteCollectionUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string TargetContainerIngredientId { get; set; }

        public string TargetGlobalActionKey { get; set; }
    }

    internal static class TopologyWebOwnerMappingCatalog
    {
        public static IList<TopologyWebOwnerMapping> FromLegacy(TopologyPlan topology)
        {
            if (topology == null)
            {
                throw new ArgumentNullException(nameof(topology));
            }
            return topology.SiteCollections.SelectMany(value => value.Webs).Select(value => new TopologyWebOwnerMapping
            {
                SourceSiteId = value.SourceSiteId,
                SourceWebId = value.SourceWebId,
                SourceWebUrl = value.SourceWebUrl,
                SourceServerRelativeUrl = value.SourceServerRelativeUrl,
                TargetWebUrl = value.TargetWebUrl,
                TargetSiteCollectionUrl = topology.SiteCollections.Single(site => site.SourceSiteId == value.SourceSiteId).TargetSiteCollectionUrl,
                TargetServerRelativeUrl = value.TargetServerRelativeUrl
            }).ToList();
        }

        public static IList<TopologyWebOwnerMapping> FromShared(SharedTopologyPlan topology)
        {
            SharedTopologyPlanValidator.Validate(topology);
            return topology.SourceWebBindings.Select(value => new TopologyWebOwnerMapping
            {
                SourceSiteId = value.SourceSiteId,
                SourceWebId = value.SourceWebId,
                SourceWebUrl = value.SourceWebUrl,
                SourceServerRelativeUrl = value.SourceServerRelativeUrl,
                TargetWebUrl = value.TargetWebUrl,
                TargetSiteCollectionUrl = topology.TargetSite.TargetSiteCollectionUrl,
                TargetServerRelativeUrl = value.TargetServerRelativeUrl,
                TargetContainerIngredientId = value.TargetContainerIngredientId,
                TargetGlobalActionKey = value.TargetGlobalActionKey
            }).ToList();
        }
    }
}
