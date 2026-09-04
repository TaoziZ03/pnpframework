using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal sealed class TopologyWebOwnerMapping
    {
        public string SourceOwnerKey { get; set; }

        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public string SourceWebUrl { get; set; }

        public string SourceServerRelativeUrl { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetSiteCollectionUrl { get; set; }

        public Guid? ExpectedTargetSiteId { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string TargetContainerIngredientId { get; set; }

        public string TargetLogicalActionKey { get; set; }
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
                SourceOwnerKey = LegacySourceOwnerKey(value),
                SourceSiteId = value.SourceSiteId,
                SourceWebId = value.SourceWebId,
                SourceWebUrl = value.SourceWebUrl,
                SourceServerRelativeUrl = value.SourceServerRelativeUrl,
                TargetWebUrl = value.TargetWebUrl,
                TargetSiteCollectionUrl = topology.SiteCollections.Single(site => site.SourceSiteId == value.SourceSiteId).TargetSiteCollectionUrl,
                ExpectedTargetSiteId = topology.SiteCollections.Single(site => site.SourceSiteId == value.SourceSiteId).ExpectedTargetSiteId,
                TargetServerRelativeUrl = value.TargetServerRelativeUrl
            }).ToList();
        }

        private static string LegacySourceOwnerKey(WebMappingPlan value)
        {
            var sourceUrl = !string.IsNullOrWhiteSpace(value.SourceSiteCollectionUrl)
                ? value.SourceSiteCollectionUrl
                : value.SourceWebUrl;
            return !string.IsNullOrWhiteSpace(sourceUrl)
                ? SharedTopologyIdentity.SourceOwner(sourceUrl, value.SourceSiteId, value.SourceServerRelativeUrl)
                : "topology:legacy-source-owner:" + value.SourceSiteId.ToString("N") + ":"
                    + SharedTopologyIdentity.CanonicalPath(value.SourceServerRelativeUrl);
        }

        public static IList<TopologyWebOwnerMapping> FromShared(SharedTopologyPlan topology)
        {
            SharedTopologyPlanValidator.Validate(topology);
            return topology.SourceWebBindings.Select(value => new TopologyWebOwnerMapping
            {
                SourceOwnerKey = value.SourceOwnerKey,
                SourceSiteId = value.SourceSiteId,
                SourceWebId = value.SourceWebId,
                SourceWebUrl = value.SourceWebUrl,
                SourceServerRelativeUrl = value.SourceServerRelativeUrl,
                TargetWebUrl = value.TargetWebUrl,
                TargetSiteCollectionUrl = topology.TargetSite.TargetSiteCollectionUrl,
                ExpectedTargetSiteId = topology.TargetSite.ExpectedTargetSiteId,
                TargetServerRelativeUrl = value.TargetServerRelativeUrl,
                TargetContainerIngredientId = value.TargetContainerIngredientId,
                TargetLogicalActionKey = value.TargetLogicalActionKey
            }).ToList();
        }
    }
}
