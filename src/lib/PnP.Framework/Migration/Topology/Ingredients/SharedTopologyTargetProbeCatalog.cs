using PnP.Framework.Migration.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal static class SharedTopologyTargetProbeCatalog
    {
        public static IDictionary<Guid, TopologyWebTargetProbe> Create(
            SharedTopologyPlan plan,
            SharedTopologyGlobalTargetAnalysis analysis)
        {
            SharedTopologyPlanValidator.Validate(plan);
            var probes = analysis?.Probes?.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal)
                ?? new Dictionary<string, PathDerivedTargetWebProbe>(StringComparer.Ordinal);
            return plan.SourceWebBindings
                .Where(value => value.SourceWebId != Guid.Empty)
                .ToDictionary(
                value => value.SourceWebId,
                value => ToLegacyProbe(value, probes[value.TargetGlobalActionKey]));
        }

        private static TopologyWebTargetProbe ToLegacyProbe(
            SourceWebTargetContainerBinding binding,
            PathDerivedTargetWebProbe shared)
        {
            return new TopologyWebTargetProbe
            {
                SourceSiteId = binding.SourceSiteId,
                SourceWebId = binding.SourceWebId,
                TargetWebUrl = binding.TargetWebUrl,
                TargetServerRelativeUrl = binding.TargetServerRelativeUrl,
                Exists = shared.TargetWebId.HasValue,
                TargetSiteId = shared.TargetSiteId,
                TargetWebId = shared.TargetWebId,
                TargetParentWebId = shared.TargetParentWebId,
                Disposition = ToLegacyDisposition(shared.State),
                Issues = shared.Issues.Select(CloneIssue).ToList()
            };
        }

        private static TopologyMaterializationDisposition ToLegacyDisposition(TargetWebContainerState state)
        {
            switch (state)
            {
                case TargetWebContainerState.CreateMissing:
                    return TopologyMaterializationDisposition.CreateOwned;
                case TargetWebContainerState.ReuseOwned:
                    return TopologyMaterializationDisposition.ReuseOwned;
                case TargetWebContainerState.ReuseExplicitApprovedHost:
                    return TopologyMaterializationDisposition.ReuseApprovedHost;
                case TargetWebContainerState.RecoverInterruptedCreate:
                    return TopologyMaterializationDisposition.RecoverInterruptedCreate;
                default:
                    return TopologyMaterializationDisposition.Block;
            }
        }

        private static MigrationIssue CloneIssue(MigrationIssue value)
        {
            return new MigrationIssue
            {
                Code = value.Code,
                Severity = value.Severity,
                Subject = value.Subject,
                Ingredient = value.Ingredient,
                Message = value.Message,
                SourceIdentity = value.SourceIdentity,
                TargetIdentity = value.TargetIdentity
            };
        }
    }
}
