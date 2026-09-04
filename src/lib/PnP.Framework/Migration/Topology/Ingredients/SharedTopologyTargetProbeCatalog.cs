using PnP.Framework.Migration.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal sealed class TopologyOwnerProbeCatalog
    {
        public IDictionary<Guid, TopologyWebTargetProbe> BySourceWebId { get; } =
            new Dictionary<Guid, TopologyWebTargetProbe>();

        public IDictionary<string, TopologyWebTargetProbe> BySourceOwnerKey { get; } =
            new Dictionary<string, TopologyWebTargetProbe>(StringComparer.Ordinal);

        public bool TryGet(Guid sourceWebId, string sourceOwnerKey, out TopologyWebTargetProbe probe)
        {
            if (!string.IsNullOrWhiteSpace(sourceOwnerKey)
                && BySourceOwnerKey.TryGetValue(sourceOwnerKey, out probe))
            {
                return true;
            }
            probe = null;
            return sourceWebId != Guid.Empty && BySourceWebId.TryGetValue(sourceWebId, out probe);
        }
    }

    internal static class SharedTopologyTargetProbeCatalog
    {
        public static TopologyOwnerProbeCatalog Create(
            SharedTopologyPlan plan,
            SharedTopologyGlobalTargetAnalysis analysis)
        {
            SharedTopologyPlanValidator.Validate(plan);
            var probes = analysis?.Probes?.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal)
                ?? new Dictionary<string, PathDerivedTargetWebProbe>(StringComparer.Ordinal);
            var result = new TopologyOwnerProbeCatalog();
            foreach (var binding in plan.SourceWebBindings)
            {
                var probe = ToLegacyProbe(binding, probes[binding.TargetLogicalActionKey]);
                result.BySourceOwnerKey.Add(binding.SourceOwnerKey, probe);
                if (binding.SourceWebId != Guid.Empty)
                {
                    result.BySourceWebId.Add(binding.SourceWebId, probe);
                }
            }
            return result;
        }

        public static TopologyOwnerProbeCatalog CreateLegacy(TopologyTargetAnalysis analysis)
        {
            if (analysis == null)
            {
                return null;
            }
            var result = new TopologyOwnerProbeCatalog();
            foreach (var probe in analysis.SiteCollections.SelectMany(value => value.Webs))
            {
                result.BySourceWebId.Add(probe.SourceWebId, probe);
                if (!string.IsNullOrWhiteSpace(probe.SourceOwnerKey))
                {
                    result.BySourceOwnerKey.Add(probe.SourceOwnerKey, probe);
                }
            }
            return result;
        }

        private static TopologyWebTargetProbe ToLegacyProbe(
            SourceWebTargetContainerBinding binding,
            PathDerivedTargetWebProbe shared)
        {
            return new TopologyWebTargetProbe
            {
                SourceOwnerKey = binding.SourceOwnerKey,
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
