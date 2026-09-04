using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class SharedTopologyGlobalActionDag
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-global-action-dag/v2";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public IList<TargetWebContainerIngredientPlan> Actions { get; set; } = new List<TargetWebContainerIngredientPlan>();

        public IList<string> SourcePlanDigests { get; set; } = new List<string>();

        public IList<string> ExecutionGroupDigests { get; set; } = new List<string>();

        public IList<string> SupportCohortDigests { get; set; } = new List<string>();

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
            foreach (var slot in materialized.SelectMany(value => value.TargetWebContainers)
                .GroupBy(value => value.TargetSlotKey, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var signatures = slot.Select(value => value.ActionSignature.Signature)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (signatures.Length != 1)
                {
                    issues.Add(Issue(
                        "SharedTopologyTargetSlotSignatureConflict",
                        slot.Key,
                        "The same authority/Site/path target slot has more than one generic action signature. No plan wins implicitly."));
                    continue;
                }
                var candidates = slot.ToArray();
                if (candidates.Select(value => value.GlobalActionKey).Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    issues.Add(Issue(
                        "SharedTopologyGlobalActionIdentityConflict",
                        slot.Key,
                        "Equivalent target-slot signatures produced different global action keys.",
                        MigrationIssueSeverity.Error));
                    continue;
                }
                actions.Add(candidates[0]);
            }
            foreach (var source in materialized.SelectMany(value => value.SourceWebBindings)
                .GroupBy(value => value.SourceOwnerKey, StringComparer.Ordinal))
            {
                if (source.Select(value => value.TargetGlobalActionKey).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    issues.Add(Issue(
                        "SharedTopologySourceOwnerMappingConflict",
                        source.Key,
                        "The same source owner is bound to more than one target global action."));
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
                issues.Add(Issue(
                    "SharedTopologyGlobalActionParentMissing",
                    "shared-topology-global-actions",
                    "A global target-Web action references a parent action outside the compiled DAG."));
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
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList(),
                ExecutionGroupDigests = materialized.Select(value => value.ExecutionGroupDigest)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList(),
                SupportCohortDigests = materialized.Select(value => value.SupportCohortDigest)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList()
            };
            dag.DagDigest = ComputeDigest(dag);
            return new SharedTopologyGlobalActionDagBuildResult { Dag = dag, Issues = issues };
        }

        public static string ComputeDigest(SharedTopologyGlobalActionDag dag)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    dag ?? throw new ArgumentNullException(nameof(dag)),
                    nameof(SharedTopologyGlobalActionDag.DagDigest)));
        }

        private static MigrationIssue Issue(
            string code,
            string subject,
            string message,
            MigrationIssueSeverity severity = MigrationIssueSeverity.Blocker)
        {
            return new MigrationIssue
            {
                Code = code,
                Severity = severity,
                Subject = subject,
                Ingredient = "Topology.GlobalActionDag",
                Message = message
            };
        }
    }
}
