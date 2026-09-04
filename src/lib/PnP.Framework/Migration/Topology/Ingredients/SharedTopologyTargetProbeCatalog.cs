using PnP.Framework.Migration.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyTargetProbeCatalog
    {
        public static IDictionary<Guid, TopologyWebTargetProbe> Create(
            SharedTopologyPlan plan,
            SharedTopologyTargetAnalysis analysis)
        {
            SharedTopologyExecutionValidator.ValidateAnalysis(plan, analysis);
            var probes = analysis.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            return plan.SourceWebBindings.ToDictionary(
                value => value.SourceWebId,
                value => ToLegacyProbe(value, probes[value.TargetContainerIngredientId]));
        }

        private static TopologyWebTargetProbe ToLegacyProbe(
            SourceWebTargetContainerBinding binding,
            TargetWebContainerProbe sharedProbe)
        {
            var disposition = sharedProbe.State == TargetWebContainerState.Reuse
                ? TopologyMaterializationDisposition.ReuseApprovedHost
                : sharedProbe.State == TargetWebContainerState.CreateMissing
                    ? TopologyMaterializationDisposition.CreateOwned
                    : TopologyMaterializationDisposition.Block;
            return new TopologyWebTargetProbe
            {
                SourceSiteId = binding.SourceSiteId,
                SourceWebId = binding.SourceWebId,
                TargetWebUrl = binding.TargetWebUrl,
                Exists = sharedProbe.Exists,
                TargetSiteId = sharedProbe.TargetSiteId,
                TargetWebId = sharedProbe.TargetWebId,
                TargetParentWebId = sharedProbe.TargetParentWebId,
                Disposition = disposition,
                Issues = sharedProbe.Issues.Select(value => new MigrationIssue
                {
                    Code = value.Code,
                    Severity = value.Severity,
                    Subject = value.Subject,
                    Ingredient = value.Ingredient,
                    Message = value.Message,
                    SourceIdentity = value.SourceIdentity,
                    TargetIdentity = value.TargetIdentity
                }).ToList()
            };
        }
    }
}
