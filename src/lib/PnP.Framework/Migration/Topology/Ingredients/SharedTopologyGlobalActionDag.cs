using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class SharedTopologyGlobalActionDag
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-global-action-dag/v3";

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
            var supplied = (plans ?? Enumerable.Empty<SharedTopologyPlan>()).ToArray();
            if (supplied.Length == 0)
            {
                throw new ArgumentException("At least one shared topology plan is required.", nameof(plans));
            }
            foreach (var plan in supplied)
            {
                SharedTopologyPlanValidator.Validate(plan);
            }
            var materialized = supplied
                .OrderBy(value => value.PlanDigest, StringComparer.Ordinal)
                .ThenBy(value => MigrationContractSerializer.SerializeCanonical(value), StringComparer.Ordinal)
                .ToArray();

            var issues = new List<MigrationIssue>();
            var actions = new List<TargetWebContainerIngredientPlan>();
            foreach (var slot in materialized.SelectMany(value => value.TargetWebContainers)
                .GroupBy(value => value.TargetSlotKey, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var logicalDigests = slot.Select(value => value.LogicalActionDigest)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (logicalDigests.Length != 1)
                {
                    issues.Add(Issue(
                        "SharedTopologyTargetSlotSignatureConflict",
                        slot.Key,
                        "The same authority/Site/path target slot has more than one normalized semantic logical action. No plan wins implicitly."));
                    continue;
                }
                var candidates = slot.ToArray();
                if (candidates.Select(value => value.LogicalActionKey).Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    issues.Add(Issue(
                        "SharedTopologyGlobalActionIdentityConflict",
                        slot.Key,
                        "Equivalent normalized target-slot actions produced different logical action keys.",
                        MigrationIssueSeverity.Error));
                    continue;
                }
                var producerPayloads = candidates
                    .Select(value => MigrationContractSerializer.SerializeCanonical(ProjectGlobalProducer(value)))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (producerPayloads.Length != 1)
                {
                    issues.Add(Issue(
                        "SharedTopologyGlobalActionIdentityConflict",
                        slot.Key,
                        "Equivalent logical actions produced different source-neutral global producer payloads.",
                        MigrationIssueSeverity.Error));
                    continue;
                }
                var logicalAction = MigrationContractSerializer.Deserialize<TargetWebContainerIngredientPlan>(producerPayloads[0]);
                logicalAction.ExecutionGrants = candidates
                    .SelectMany(value => value.ExecutionGrants)
                    .GroupBy(value => value.Signature, StringComparer.OrdinalIgnoreCase)
                    .Select(value => MigrationContractSerializer.Deserialize<MigrationActionSignature>(
                        value.Select(MigrationContractSerializer.SerializeCanonical)
                            .OrderBy(payload => payload, StringComparer.Ordinal)
                            .First()))
                    .OrderBy(value => value.Signature, StringComparer.Ordinal)
                    .ToList();
                actions.Add(logicalAction);
            }
            foreach (var source in materialized.SelectMany(value => value.SourceWebBindings)
                .GroupBy(value => value.SourceOwnerKey, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                if (source.Select(SharedTopologySourceBindingIdentity.Compute)
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1)
                {
                    issues.Add(Issue(
                        "SourceOwnerEvidenceConflict",
                        source.Key,
                        "The same source owner carries conflicting source or target binding evidence."));
                }
            }
            if (issues.Count > 0)
            {
                return new SharedTopologyGlobalActionDagBuildResult { Issues = issues };
            }

            var actionKeys = new HashSet<string>(actions.Select(value => value.LogicalActionKey), StringComparer.Ordinal);
            if (actions.Any(value => !string.IsNullOrWhiteSpace(value.ParentLogicalActionKey)
                && !actionKeys.Contains(value.ParentLogicalActionKey)))
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

        private static TargetWebContainerIngredientPlan ProjectGlobalProducer(TargetWebContainerIngredientPlan source)
        {
            var external = source.ExpectedOwnership == SharedTopologyOwnership.ExternalApprovedHost;
            return new TargetWebContainerIngredientPlan
            {
                IngredientId = SharedTopologyIdentity.TargetWebContainer(source.TargetSlotKey),
                IsTargetSiteRoot = source.IsTargetSiteRoot,
                TargetSlotKey = source.TargetSlotKey,
                LogicalActionKey = source.LogicalActionKey,
                LogicalActionDigest = source.LogicalActionDigest,
                ExecutionGrants = new List<MigrationActionSignature>(),
                SemanticMappingDigest = external ? null : source.SemanticMappingDigest,
                OriginalIdentifier = external ? null : source.OriginalIdentifier,
                ExpectedOwnership = source.ExpectedOwnership,
                IdentityBasis = source.IsTargetSiteRoot
                    ? SharedTopologyIdentityBasis.TargetSiteRoot
                    : SharedTopologyIdentityBasis.ExactRelativePath,
                ParentIngredientId = source.ParentIngredientId,
                ParentLogicalActionKey = source.ParentLogicalActionKey,
                TargetWebUrl = source.TargetWebUrl,
                TargetServerRelativeUrl = source.TargetServerRelativeUrl,
                TargetParentWebUrl = source.TargetParentWebUrl,
                ExpectedTargetSiteId = source.ExpectedTargetSiteId,
                ApprovedExistingTargetWebId = source.ApprovedExistingTargetWebId,
                Provisioning = new TargetWebContainerProvisioningValues
                {
                    Title = source.Provisioning.Title,
                    TitleSource = TargetWebProvisioningValueSource.ExplicitTargetPolicy,
                    Template = source.Provisioning.Template,
                    TemplateSource = TargetWebProvisioningValueSource.ExplicitTargetPolicy,
                    Configuration = source.Provisioning.Configuration,
                    ConfigurationSource = TargetWebProvisioningValueSource.ExplicitTargetPolicy,
                    Language = source.Provisioning.Language,
                    LanguageSource = TargetWebProvisioningValueSource.ExplicitTargetPolicy,
                    UseSamePermissionsAsParentWeb = source.Provisioning.UseSamePermissionsAsParentWeb,
                    PermissionsSource = TargetWebProvisioningValueSource.ExplicitTargetPolicy,
                    ExpectedMetadataDifferences = new List<string>()
                }
            };
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

    internal static class SharedTopologySourceBindingIdentity
    {
        public static string Compute(SourceWebTargetContainerBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            return MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-shared-topology-source-binding-identity/v1",
                binding.SourceOwnerKey,
                binding.SourceSiteId,
                binding.SourceWebId,
                sourceWebUrl = SharedTopologyPath.NormalizeAbsoluteUrl(binding.SourceWebUrl, nameof(binding.SourceWebUrl)).ToLowerInvariant(),
                sourceServerRelativeUrl = SharedTopologyIdentity.CanonicalPath(binding.SourceServerRelativeUrl),
                binding.TargetContainerIngredientId,
                binding.TargetLogicalActionKey,
                targetWebUrl = SharedTopologyPath.NormalizeAbsoluteUrl(binding.TargetWebUrl, nameof(binding.TargetWebUrl)).ToLowerInvariant(),
                targetServerRelativeUrl = SharedTopologyIdentity.CanonicalPath(binding.TargetServerRelativeUrl)
            });
        }
    }
}
