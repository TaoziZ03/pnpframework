using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class SharedTopologyGlobalActionDag
    {
        public string SchemaVersion { get; set; } = "pnp-shared-topology-global-action-dag/v1";

        public IList<TargetWebContainerIngredientPlan> Actions { get; set; } = new List<TargetWebContainerIngredientPlan>();

        public IList<string> SourcePlanDigests { get; set; } = new List<string>();

        public IList<string> SupportCohortSignatures { get; set; } = new List<string>();

        public string DagDigest { get; set; }
    }

    public sealed class SharedTopologyGlobalActionDagBuildResult
    {
        public SharedTopologyGlobalActionDag Dag { get; set; }

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public bool IsExecutable => Dag != null && Issues.All(value =>
            value.Severity != MigrationIssueSeverity.Blocker
            && value.Severity != MigrationIssueSeverity.Error);
    }

    public static class SharedTopologyGlobalActionDagCompiler
    {
        public static SharedTopologyGlobalActionDagBuildResult Compile(IEnumerable<SharedTopologyPlan> plans)
        {
            var materialized = (plans ?? Enumerable.Empty<SharedTopologyPlan>()).ToArray();
            if (materialized.Length == 0)
            {
                throw new ArgumentException("At least one shared topology plan is required.", nameof(plans));
            }
            foreach (var plan in materialized)
            {
                SharedTopologyPlanValidator.Validate(plan);
            }

            var issues = new List<MigrationIssue>();
            var actions = new List<TargetWebContainerIngredientPlan>();
            foreach (var slot in materialized
                .SelectMany(value => value.TargetWebContainers)
                .GroupBy(value => value.TargetSlotKey, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var signatures = slot.Select(value => value.ActionSignatureDigest)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (signatures.Length != 1)
                {
                    issues.Add(new MigrationIssue
                    {
                        Code = "SharedTopologyTargetSlotSignatureConflict",
                        Severity = MigrationIssueSeverity.Blocker,
                        Subject = slot.Key,
                        Ingredient = "Topology.GlobalActionDag",
                        Message = "The same target-Web mutation slot has more than one action signature. No plan wins implicitly."
                    });
                    continue;
                }
                var candidates = slot.ToArray();
                if (candidates.Select(value => value.GlobalActionKey).Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    issues.Add(new MigrationIssue
                    {
                        Code = "SharedTopologyGlobalActionIdentityConflict",
                        Severity = MigrationIssueSeverity.Error,
                        Subject = slot.Key,
                        Ingredient = "Topology.GlobalActionDag",
                        Message = "Equivalent target-slot signatures produced different global action keys."
                    });
                    continue;
                }
                actions.Add(candidates[0]);
            }

            if (issues.Count > 0)
            {
                return new SharedTopologyGlobalActionDagBuildResult { Issues = issues };
            }
            foreach (var source in materialized.SelectMany(value => value.SourceWebBindings)
                .GroupBy(value => value.SourceSiteId.ToString("D") + "/" + value.SourceWebId.ToString("D"), StringComparer.Ordinal))
            {
                if (source.Select(value => value.TargetGlobalActionKey).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    issues.Add(new MigrationIssue
                    {
                        Code = "SharedTopologySourceOwnerMappingConflict",
                        Severity = MigrationIssueSeverity.Blocker,
                        Subject = source.Key,
                        Ingredient = "Topology.GlobalActionDag",
                        Message = "The same source Web is bound to more than one target global action."
                    });
                }
            }
            if (issues.Count > 0)
            {
                return new SharedTopologyGlobalActionDagBuildResult { Issues = issues };
            }
            var actionKeys = new HashSet<string>(actions.Select(value => value.GlobalActionKey), StringComparer.Ordinal);
            if (actions.Any(value => !string.IsNullOrWhiteSpace(value.ParentGlobalActionKey)
                && !actionKeys.Contains(value.ParentGlobalActionKey)))
            {
                issues.Add(new MigrationIssue
                {
                    Code = "SharedTopologyGlobalActionParentMissing",
                    Severity = MigrationIssueSeverity.Blocker,
                    Subject = "shared-topology-global-actions",
                    Ingredient = "Topology.GlobalActionDag",
                    Message = "A global target-Web action references a parent action outside the compiled DAG."
                });
                return new SharedTopologyGlobalActionDagBuildResult { Issues = issues };
            }

            var dag = new SharedTopologyGlobalActionDag
            {
                Actions = actions
                    .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                    .ThenBy(value => value.TargetSlotKey, StringComparer.Ordinal)
                    .ToList(),
                SourcePlanDigests = materialized.Select(value => value.PlanDigest)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SupportCohortSignatures = materialized.Select(value => value.SupportCohortSignature)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList()
            };
            dag.DagDigest = ComputeDigest(dag);
            return new SharedTopologyGlobalActionDagBuildResult { Dag = dag, Issues = issues };
        }

        public static string ComputeDigest(SharedTopologyGlobalActionDag dag)
        {
            if (dag == null)
            {
                throw new ArgumentNullException(nameof(dag));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    dag,
                    nameof(SharedTopologyGlobalActionDag.DagDigest)));
        }
    }
}
