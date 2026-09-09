using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    /// <summary>
    /// Projects the executable page-ingredient frontier into stable action
    /// signatures. The package plan digest remains the aggregate approval
    /// boundary; each signature deliberately excludes unrelated sibling actions.
    /// </summary>
    internal static class PublishingPageIngredientActionSignatureFactory
    {
        private const string SemanticSchemaVersion = "pnp-publishing-ingredient-expected-state/v1";
        private const string TargetSchemaVersion = "pnp-publishing-ingredient-target/v1";

        public static IReadOnlyDictionary<string, PublishingPageIngredientExecutionBinding> Create(
            PublishingPageMigrationPackage package)
        {
            ValidatePackageBoundary(package);

            var graph = package.Plan.IngredientGraph;
            var nodes = UniqueBy(
                graph.Nodes,
                value => value?.Id,
                "ingredient node");
            var actions = UniqueBy(
                package.Plan.IngredientActions,
                value => value?.ActionId,
                "ingredient action");
            var actionsByIngredient = UniqueBy(
                package.Plan.IngredientActions,
                value => value?.IngredientId,
                "ingredient action owner");
            var executable = new HashSet<string>(
                package.Plan.ExecutionFrontier.Decisions
                    .Where(value => value != null && value.State == PageIngredientExecutionState.Executable)
                    .Select(value => value.IngredientId),
                StringComparer.Ordinal);
            var external = UniqueBy(
                graph.ExternalReferences ?? Array.Empty<PageIngredientExternalReference>(),
                value => value?.IngredientId,
                "external ingredient reference");
            var edges = (graph.Edges ?? Array.Empty<PageIngredientEdge>())
                .Where(value => value != null && value.Requirement == PageIngredientRequirement.Required)
                .GroupBy(value => value.FromIngredientId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.OrderBy(item => item.ToIngredientId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
            var bindings = new Dictionary<string, PublishingPageIngredientExecutionBinding>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);

            foreach (var ingredientId in executable.OrderBy(value => value, StringComparer.Ordinal))
            {
                Build(ingredientId);
            }
            return bindings;

            PublishingPageIngredientExecutionBinding Build(string ingredientId)
            {
                if (bindings.Values.FirstOrDefault(value =>
                        string.Equals(value.IngredientId, ingredientId, StringComparison.Ordinal)) is PublishingPageIngredientExecutionBinding existing)
                {
                    return existing;
                }
                if (!visiting.Add(ingredientId))
                {
                    throw new InvalidDataException(
                        "The executable Publishing ingredient action graph contains a dependency cycle at '" + ingredientId + "'.");
                }
                try
                {
                    if (!nodes.TryGetValue(ingredientId, out var node)
                        || node == null
                        || !node.HasContent
                        || !actionsByIngredient.TryGetValue(ingredientId, out var action)
                        || action == null
                        || !actions.ContainsKey(action.ActionId))
                    {
                        throw new InvalidDataException(
                            "Every executable Publishing ingredient requires one content-bearing node and one uniquely identified action.");
                    }

                    var dependencySignatures = new List<string>();
                    if (edges.TryGetValue(ingredientId, out var dependencies))
                    {
                        foreach (var dependency in dependencies)
                        {
                            if (action.Disposition == IngredientDisposition.Transform
                                && (action.ReleasedDependencyIngredientIds ?? Array.Empty<string>())
                                    .Contains(dependency.ToIngredientId, StringComparer.Ordinal))
                            {
                                continue;
                            }
                            if (executable.Contains(dependency.ToIngredientId))
                            {
                                dependencySignatures.Add(Build(dependency.ToIngredientId).ActionSignature.Signature);
                                continue;
                            }
                            if (external.TryGetValue(dependency.ToIngredientId, out var externalDependency)
                                && externalDependency.State == PageExternalIngredientState.PlannedGlobalAction
                                && MigrationActionSignature.IsSha256(externalDependency.ExecutionGrantSignature))
                            {
                                dependencySignatures.Add(externalDependency.ExecutionGrantSignature);
                                continue;
                            }
                            throw new InvalidDataException(
                                "Executable Publishing ingredient '" + ingredientId
                                + "' has a non-executable sealed dependency '" + dependency.ToIngredientId + "'.");
                        }
                    }

                    var ownership = ExpectedOwnership(node, action);
                    var signature = MigrationActionSignature.Create(
                        action.ActionId,
                        "Publishing.Ingredient." + node.Kind,
                        node.EvidenceDigest,
                        package.SelectionDigest,
                        TargetIdentity(package, action, ingredientId),
                        ExpectedStateDigest(node, action, ownership),
                        dependencySignatures);
                    var binding = new PublishingPageIngredientExecutionBinding
                    {
                        IngredientId = ingredientId,
                        ExpectedOwnership = ownership,
                        ActionSignature = signature
                    };
                    bindings.Add(action.ActionId, binding);
                    return binding;
                }
                finally
                {
                    visiting.Remove(ingredientId);
                }
            }
        }

        private static void ValidatePackageBoundary(PublishingPageMigrationPackage package)
        {
            if (package?.Plan?.IngredientGraph?.Nodes == null
                || package.Plan.IngredientGraph.Edges == null
                || package.Plan.IngredientActions == null
                || package.Plan.ExecutionFrontier?.Decisions == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            if (string.IsNullOrWhiteSpace(package.PlanDigest)
                || !string.Equals(
                    package.PlanDigest,
                    PublishingPageDigest.ComputePlanDigest(package.Plan),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Publishing ingredient execution requires the exact digest-sealed package plan.");
            }
        }

        private static Dictionary<string, T> UniqueBy<T>(
            IEnumerable<T> values,
            Func<T, string> keySelector,
            string subject)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<T>())
            {
                var key = keySelector(value);
                if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key))
                {
                    throw new InvalidDataException("The Publishing " + subject + " identity is missing or duplicated.");
                }
                result.Add(key, value);
            }
            return result;
        }

        private static string TargetIdentity(
            PublishingPageMigrationPackage package,
            PageIngredientAction action,
            string ingredientId)
        {
            var projection = new
            {
                schemaVersion = TargetSchemaVersion,
                package.Plan.TargetWebUrl,
                package.Plan.TargetPageServerRelativeUrl,
                ingredientId,
                actionTargetIdentity = action.TargetIdentity
            };
            return "urn:pnp:publishing-ingredient-target:v1:"
                + MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(projection));
        }

        private static string ExpectedStateDigest(
            PageIngredientNode node,
            PageIngredientAction action,
            MigrationTargetOwnership ownership)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = SemanticSchemaVersion,
                ingredientId = node.Id,
                ingredientKind = node.Kind,
                action.Capability,
                action.Disposition,
                action.Realization,
                action.TargetIdentity,
                action.PolicyId,
                action.PolicyVersion,
                releasedDependencyIngredientIds = (action.ReleasedDependencyIngredientIds ?? new List<string>())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                verificationAssertions = (action.VerificationAssertions ?? new List<string>())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                ownership
            }));
        }

        private static MigrationTargetOwnership ExpectedOwnership(
            PageIngredientNode node,
            PageIngredientAction action)
        {
            if (node.Ownership == PageIngredientOwnership.TargetRuntime
                || IsExternalRealization(action.Realization))
            {
                return MigrationTargetOwnership.External;
            }
            return MigrationTargetOwnership.MigrationOwned;
        }

        private static bool IsExternalRealization(string realization)
        {
            return !string.IsNullOrWhiteSpace(realization)
                && (realization.IndexOf("target-runtime", StringComparison.OrdinalIgnoreCase) >= 0
                    || realization.IndexOf("target-stock", StringComparison.OrdinalIgnoreCase) >= 0
                    || realization.IndexOf("approved-host", StringComparison.OrdinalIgnoreCase) >= 0
                    || realization.IndexOf("preserve-external", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
