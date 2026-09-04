using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public static class PageIngredientPlanEvaluator
    {
        public static PageIngredientPlanEvaluation Evaluate(
            CanonicalPageIngredientGraph graph,
            IEnumerable<PageIngredientAction> actions)
        {
            return Evaluate(graph, actions, null);
        }

        /// <summary>
        /// Evaluates the final ingredient action graph. Defer is a nonterminal
        /// mitigation state. Block is accepted only when the same ingredient has
        /// retained, digest-valid literal wire HTTP 401/403 evidence.
        /// </summary>
        public static PageIngredientPlanEvaluation Evaluate(
            CanonicalPageIngredientGraph graph,
            IEnumerable<PageIngredientAction> actions,
            IReadOnlyDictionary<string, LiteralHttpAuthorizationEvidence> authorizationEvidence)
        {
            var issues = new List<MigrationIssue>();
            var validAuthorizationEvidence = ValidateAuthorizationEvidence(authorizationEvidence, issues);
            var authorizationIngredientIds = new HashSet<string>(validAuthorizationEvidence.Keys, StringComparer.Ordinal);
            if (graph == null)
            {
                issues.Add(Issue(
                    "IngredientGraphMissing",
                    "ingredient-graph",
                    "The canonical page ingredient graph is missing.",
                    MigrationIssueSeverity.Error));
                return Result(PageMigrationOutcome.Invalid, issues, new PageIngredientExecutionFrontier());
            }

            var nodes = graph.Nodes ?? new List<PageIngredientNode>();
            var actionList = (actions ?? Array.Empty<PageIngredientAction>()).ToList();
            var nodeById = UniqueNodes(nodes, issues);
            var actionByIngredient = UniqueActions(actionList, issues);

            foreach (var node in nodes.Where(value => value != null && value.HasContent))
            {
                if (!actionByIngredient.TryGetValue(node.Id, out var action))
                {
                    issues.Add(Issue(
                        "IngredientDispositionMissing",
                        node.Id,
                        "Every nonempty ingredient must have exactly one planned disposition.",
                        MigrationIssueSeverity.Error));
                    continue;
                }

                if (action.Disposition == IngredientDisposition.Undefined)
                {
                    issues.Add(Issue(
                        "IngredientDispositionUndefined",
                        node.Id,
                        "The ingredient action has no semantic disposition.",
                        MigrationIssueSeverity.Error));
                }

                ValidateAuthorizationBinding(action, validAuthorizationEvidence, issues);

                if (action.Disposition == IngredientDisposition.Defer
                    || (action.TerminalStatus == IngredientTerminalStatus.DecisionRequired
                        && action.Disposition != IngredientDisposition.Block))
                {
                    issues.Add(Issue(
                        "IngredientMitigationPending",
                        node.Id,
                        action.Reason ?? "The ingredient requires additional evidence, planning, capability work, or an explicit selection.",
                        MigrationIssueSeverity.Warning));
                }

                if (action.Disposition == IngredientDisposition.Block)
                {
                    issues.Add(authorizationIngredientIds.Contains(node.Id)
                        ? Issue(
                            "IngredientAuthorizationBlocked",
                            node.Id,
                            action.Reason ?? "The ingredient request returned literal HTTP 401/403.",
                            MigrationIssueSeverity.Blocker)
                        : Issue(
                            "IngredientBlockWithoutAuthorizationEvidence",
                            node.Id,
                            "Block is reserved for retained literal wire HTTP 401/403 evidence; use Defer for mitigation work.",
                            MigrationIssueSeverity.Error));
                }

                if (action.TerminalStatus == IngredientTerminalStatus.SatisfiedByPolicy
                    && action.Disposition != IngredientDisposition.EvidenceOnly
                    && action.Disposition != IngredientDisposition.Exclude)
                {
                    issues.Add(Issue(
                        "IngredientPolicySatisfactionInvalid",
                        node.Id,
                        "SatisfiedByPolicy is reserved for an EvidenceOnly or Exclude decision with a sealed policy receipt.",
                        MigrationIssueSeverity.Error));
                }

                if (action.Capability == IngredientCapability.Unknown
                    && action.Disposition != IngredientDisposition.Drop
                    && action.Disposition != IngredientDisposition.Delegate
                    && action.Disposition != IngredientDisposition.Defer
                    && action.Disposition != IngredientDisposition.Block
                    && action.Disposition != IngredientDisposition.EvidenceOnly
                    && action.Disposition != IngredientDisposition.Exclude)
                {
                    issues.Add(Issue(
                        "IngredientCapabilityUnknown",
                        node.Id,
                        "A retained ingredient has unknown target capability.",
                        MigrationIssueSeverity.Error));
                }
            }

            foreach (var action in actionList.Where(value => value != null))
            {
                if (string.IsNullOrWhiteSpace(action.IngredientId) || !nodeById.ContainsKey(action.IngredientId))
                {
                    issues.Add(Issue(
                        "IngredientActionOrphaned",
                        action.IngredientId ?? "ingredient-action",
                        "The action does not reference a captured ingredient.",
                        MigrationIssueSeverity.Error));
                }
            }

            ValidateAuthorizationCoverage(
                authorizationIngredientIds,
                nodeById,
                actionByIngredient,
                issues);
            ValidateDependencyReleases(graph.Edges, nodeById, actionByIngredient, issues);
            ValidateRequiredEdges(
                graph.Edges,
                nodeById,
                actionByIngredient,
                authorizationIngredientIds,
                issues);
            var executionFrontier = BuildExecutionFrontier(
                graph.Edges,
                nodeById,
                actionByIngredient,
                authorizationIngredientIds);

            if (issues.Any(value => value.Severity == MigrationIssueSeverity.Error))
            {
                return Result(PageMigrationOutcome.Invalid, issues, executionFrontier);
            }
            if (executionFrontier.IsPartial)
            {
                return Result(PageMigrationOutcome.PartiallyExecutable, issues, executionFrontier);
            }
            if (executionFrontier.HasAuthorizationBlockedIngredients)
            {
                return Result(PageMigrationOutcome.AuthorizationBlocked, issues, executionFrontier);
            }
            if (executionFrontier.HasDeferredIngredients)
            {
                return Result(PageMigrationOutcome.MitigationPending, issues, executionFrontier);
            }

            var materialActions = actionList.Where(value => value != null
                && nodeById.TryGetValue(value.IngredientId ?? string.Empty, out var node)
                && node.HasContent).ToList();
            if (materialActions.Any(IsSatisfiedByPolicy))
            {
                return Result(PageMigrationOutcome.ExecutableWithApprovedExclusions, issues, executionFrontier);
            }

            if (materialActions.Any(value => value.Disposition == IngredientDisposition.Drop
                || value.Disposition == IngredientDisposition.Delegate))
            {
                return Result(PageMigrationOutcome.ExecutableWithLoss, issues, executionFrontier);
            }

            if (materialActions.Any(value => value.Disposition == IngredientDisposition.Transform
                || value.Disposition == IngredientDisposition.Substitute))
            {
                return Result(PageMigrationOutcome.ExecutableWithTransform, issues, executionFrontier);
            }

            return Result(PageMigrationOutcome.Exact, issues, executionFrontier);
        }

        private static IReadOnlyDictionary<string, LiteralHttpAuthorizationEvidence> ValidateAuthorizationEvidence(
            IReadOnlyDictionary<string, LiteralHttpAuthorizationEvidence> evidence,
            ICollection<MigrationIssue> issues)
        {
            var result = new Dictionary<string, LiteralHttpAuthorizationEvidence>(StringComparer.Ordinal);
            foreach (var pair in evidence ?? new Dictionary<string, LiteralHttpAuthorizationEvidence>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || result.ContainsKey(pair.Key))
                {
                    issues.Add(Issue(
                        "IngredientAuthorizationEvidenceIdentityInvalid",
                        pair.Key ?? "authorization-evidence",
                        "Authorization evidence ingredient identities must be nonempty and unique.",
                        MigrationIssueSeverity.Error));
                    continue;
                }
                try
                {
                    LiteralHttpAuthorizationEvidence.Validate(pair.Value);
                    result.Add(pair.Key, pair.Value);
                }
                catch (InvalidDataException exception)
                {
                    issues.Add(Issue(
                        "IngredientAuthorizationEvidenceInvalid",
                        pair.Key,
                        exception.Message,
                        MigrationIssueSeverity.Error));
                }
            }
            return result;
        }

        private static void ValidateAuthorizationBinding(
            PageIngredientAction action,
            IReadOnlyDictionary<string, LiteralHttpAuthorizationEvidence> evidence,
            ICollection<MigrationIssue> issues)
        {
            evidence.TryGetValue(action.IngredientId ?? string.Empty, out var retained);
            if (action.AuthorizationStatusCode.HasValue
                && (retained == null || action.AuthorizationStatusCode.Value != retained.HttpStatusCode))
            {
                issues.Add(Issue(
                    "IngredientAuthorizationStatusCodeUnretained",
                    action.IngredientId,
                    "An action HTTP status is non-authoritative and must match retained, digest-valid literal wire evidence.",
                    MigrationIssueSeverity.Error));
            }
            if (action.TerminalStatus == IngredientTerminalStatus.AuthorizationBlocked
                && (retained == null || action.Disposition != IngredientDisposition.Block))
            {
                issues.Add(Issue(
                    "IngredientAuthorizationTerminalStatusUnretained",
                    action.IngredientId,
                    "AuthorizationBlocked cannot be asserted by an action; it requires a Block bound to retained literal wire HTTP 401/403 evidence.",
                    MigrationIssueSeverity.Error));
            }
        }

        private static void ValidateAuthorizationCoverage(
            IEnumerable<string> authorizationIngredientIds,
            IDictionary<string, PageIngredientNode> nodes,
            IDictionary<string, PageIngredientAction> actions,
            ICollection<MigrationIssue> issues)
        {
            foreach (var ingredientId in authorizationIngredientIds)
            {
                if (!nodes.TryGetValue(ingredientId, out var node)
                    || !node.HasContent
                    || !actions.TryGetValue(ingredientId, out var action)
                    || action.Disposition != IngredientDisposition.Block)
                {
                    issues.Add(Issue(
                        "IngredientAuthorizationEvidenceUnconsumed",
                        ingredientId,
                        "Every retained literal HTTP 401/403 evidence item must bind an existing content-bearing Block action.",
                        MigrationIssueSeverity.Error));
                }
            }
        }

        private static Dictionary<string, PageIngredientNode> UniqueNodes(
            IEnumerable<PageIngredientNode> nodes,
            ICollection<MigrationIssue> issues)
        {
            var result = new Dictionary<string, PageIngredientNode>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id) || result.ContainsKey(node.Id))
                {
                    issues.Add(Issue(
                        "IngredientIdentityInvalid",
                        node?.Id ?? "ingredient",
                        "Ingredient IDs must be nonempty and unique.",
                        MigrationIssueSeverity.Error));
                    continue;
                }

                result.Add(node.Id, node);
            }
            return result;
        }

        private static Dictionary<string, PageIngredientAction> UniqueActions(
            IEnumerable<PageIngredientAction> actions,
            ICollection<MigrationIssue> issues)
        {
            var result = new Dictionary<string, PageIngredientAction>(StringComparer.Ordinal);
            foreach (var action in actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.IngredientId) || result.ContainsKey(action.IngredientId))
                {
                    issues.Add(Issue(
                        "IngredientActionIdentityInvalid",
                        action?.IngredientId ?? "ingredient-action",
                        "Each ingredient may have exactly one action.",
                        MigrationIssueSeverity.Error));
                    continue;
                }

                result.Add(action.IngredientId, action);
            }
            return result;
        }

        private static void ValidateRequiredEdges(
            IEnumerable<PageIngredientEdge> edges,
            IDictionary<string, PageIngredientNode> nodes,
            IDictionary<string, PageIngredientAction> actions,
            ISet<string> authorizationIngredientIds,
            ICollection<MigrationIssue> issues)
        {
            foreach (var edge in edges ?? Array.Empty<PageIngredientEdge>())
            {
                if (edge == null
                    || !nodes.ContainsKey(edge.FromIngredientId ?? string.Empty)
                    || !nodes.ContainsKey(edge.ToIngredientId ?? string.Empty))
                {
                    issues.Add(Issue(
                        "IngredientEdgeInvalid",
                        edge?.FromIngredientId ?? "ingredient-edge",
                        "Every graph edge must connect two captured ingredients.",
                        MigrationIssueSeverity.Error));
                    continue;
                }

                actions.TryGetValue(edge.FromIngredientId, out var consumer);
                var explicitlyReleased = consumer?.Disposition == IngredientDisposition.Transform
                    && consumer.ReleasedDependencyIngredientIds != null
                    && consumer.ReleasedDependencyIngredientIds.Contains(edge.ToIngredientId, StringComparer.Ordinal);
                if (!RequiresDependency(edge.Requirement)
                    || consumer == null
                    || !IsRetained(consumer.Disposition)
                    || !nodes[edge.ToIngredientId].HasContent
                    || explicitlyReleased)
                {
                    continue;
                }

                if (!actions.TryGetValue(edge.ToIngredientId, out var dependency))
                {
                    issues.Add(Issue(
                        "RequiredIngredientDependencyUnsatisfied",
                        edge.FromIngredientId,
                        $"Retained ingredient '{edge.FromIngredientId}' requires '{edge.ToIngredientId}' as {edge.Requirement}, but the dependency has no action.",
                        MigrationIssueSeverity.Error));
                    continue;
                }
                if (Satisfies(edge.Requirement, dependency))
                {
                    continue;
                }
                if (dependency.Disposition == IngredientDisposition.Block
                    && authorizationIngredientIds.Contains(edge.ToIngredientId))
                {
                    issues.Add(Issue(
                        "RequiredIngredientDependencyAuthorizationBlocked",
                        edge.FromIngredientId,
                        $"Retained ingredient '{edge.FromIngredientId}' depends on authorization-blocked ingredient '{edge.ToIngredientId}'.",
                        MigrationIssueSeverity.Blocker));
                    continue;
                }
                if (dependency.Disposition == IngredientDisposition.Defer
                    || dependency.TerminalStatus == IngredientTerminalStatus.DecisionRequired)
                {
                    issues.Add(Issue(
                        "RequiredIngredientDependencyDeferred",
                        edge.FromIngredientId,
                        $"Retained ingredient '{edge.FromIngredientId}' waits for mitigation of required dependency '{edge.ToIngredientId}'.",
                        MigrationIssueSeverity.Warning));
                    continue;
                }

                issues.Add(Issue(
                    "RequiredIngredientDependencyUnsatisfied",
                    edge.FromIngredientId,
                    $"Retained ingredient '{edge.FromIngredientId}' requires '{edge.ToIngredientId}' as {edge.Requirement}, but the dependency action does not satisfy that contract.",
                    MigrationIssueSeverity.Error));
            }
        }

        private static void ValidateDependencyReleases(
            IEnumerable<PageIngredientEdge> edges,
            IDictionary<string, PageIngredientNode> nodes,
            IDictionary<string, PageIngredientAction> actions,
            ICollection<MigrationIssue> issues)
        {
            var requiredEdges = new HashSet<string>(
                (edges ?? Array.Empty<PageIngredientEdge>())
                    .Where(value => value != null && RequiresDependency(value.Requirement))
                    .Select(value => EdgeIdentity(value.FromIngredientId, value.ToIngredientId)),
                StringComparer.Ordinal);
            foreach (var action in actions.Values.Where(value => value != null))
            {
                var releases = action.ReleasedDependencyIngredientIds ?? Array.Empty<string>();
                var duplicate = releases
                    .GroupBy(value => value, StringComparer.Ordinal)
                    .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
                if (duplicate != null)
                {
                    issues.Add(Issue(
                        "IngredientDependencyReleaseInvalid",
                        action.IngredientId,
                        "Released dependency IDs must be nonempty and unique.",
                        MigrationIssueSeverity.Error));
                }
                if (releases.Count > 0 && action.Disposition != IngredientDisposition.Transform)
                {
                    issues.Add(Issue(
                        "IngredientDependencyReleaseInvalid",
                        action.IngredientId,
                        "Only a Transform action may explicitly release a required dependency.",
                        MigrationIssueSeverity.Error));
                }
                foreach (var dependencyId in releases.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
                {
                    if (!nodes.ContainsKey(dependencyId)
                        || !requiredEdges.Contains(EdgeIdentity(action.IngredientId, dependencyId)))
                    {
                        issues.Add(Issue(
                            "IngredientDependencyReleaseInvalid",
                            action.IngredientId,
                            $"Released ingredient '{dependencyId}' is not a captured required dependency of '{action.IngredientId}'.",
                            MigrationIssueSeverity.Error));
                    }
                }
            }
        }

        private static bool IsRetained(IngredientDisposition disposition)
        {
            return disposition == IngredientDisposition.Preserve
                || disposition == IngredientDisposition.Transform
                || disposition == IngredientDisposition.Substitute;
        }

        private static bool IsSatisfiedByPolicy(PageIngredientAction action)
        {
            return action.Disposition == IngredientDisposition.EvidenceOnly
                || action.Disposition == IngredientDisposition.Exclude
                || action.TerminalStatus == IngredientTerminalStatus.SatisfiedByPolicy;
        }

        private static bool RequiresDependency(PageIngredientRequirement requirement)
        {
            return requirement == PageIngredientRequirement.Required
                || requirement == PageIngredientRequirement.IdentityRequired
                || requirement == PageIngredientRequirement.PayloadRequired
                || requirement == PageIngredientRequirement.HardRequired;
        }

        private static bool Satisfies(PageIngredientRequirement requirement, PageIngredientAction dependency)
        {
            if (dependency == null)
            {
                return false;
            }
            if (IsRetained(dependency.Disposition))
            {
                return true;
            }
            if (requirement == PageIngredientRequirement.IdentityRequired)
            {
                return dependency.Disposition == IngredientDisposition.EvidenceOnly
                    || dependency.SelectedAction?.Action == IngredientSelectableAction.Reference;
            }
            return false;
        }

        private static PageIngredientExecutionFrontier BuildExecutionFrontier(
            IEnumerable<PageIngredientEdge> edges,
            IDictionary<string, PageIngredientNode> nodes,
            IDictionary<string, PageIngredientAction> actions,
            ISet<string> authorizationIngredientIds)
        {
            var decisions = new Dictionary<string, PageIngredientExecutionDecision>(StringComparer.Ordinal);
            var directDeferred = new HashSet<string>(StringComparer.Ordinal);
            var directAuthorizationBlocked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes.Values.Where(value => value != null && value.HasContent))
            {
                if (!actions.TryGetValue(node.Id, out var action))
                {
                    continue;
                }

                var decision = new PageIngredientExecutionDecision { IngredientId = node.Id };
                if (action.Disposition == IngredientDisposition.Block
                    && authorizationIngredientIds.Contains(node.Id))
                {
                    decision.State = PageIngredientExecutionState.AuthorizationBlocked;
                    decision.CauseIngredientIds.Add(node.Id);
                    directAuthorizationBlocked.Add(node.Id);
                }
                else if (action.Disposition == IngredientDisposition.Defer
                    || action.Disposition == IngredientDisposition.Block
                    || action.TerminalStatus == IngredientTerminalStatus.DecisionRequired)
                {
                    decision.State = PageIngredientExecutionState.Deferred;
                    decision.CauseIngredientIds.Add(node.Id);
                    directDeferred.Add(node.Id);
                }
                else if (IsSatisfiedByPolicy(action))
                {
                    decision.State = PageIngredientExecutionState.SatisfiedByPolicy;
                }
                else if (action.Disposition == IngredientDisposition.Drop
                    || action.Disposition == IngredientDisposition.Delegate)
                {
                    decision.State = PageIngredientExecutionState.ExcludedByApprovedDisposition;
                }
                else
                {
                    decision.State = PageIngredientExecutionState.Executable;
                }
                decisions[node.Id] = decision;
            }

            var requiredDependencies = (edges ?? Array.Empty<PageIngredientEdge>())
                .Where(value => value != null
                    && RequiresDependency(value.Requirement)
                    && nodes.TryGetValue(value.ToIngredientId ?? string.Empty, out var dependencyNode)
                    && dependencyNode.HasContent)
                .GroupBy(value => value.FromIngredientId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var decision in decisions.Values.OrderBy(value => value.IngredientId, StringComparer.Ordinal))
                {
                    if (directDeferred.Contains(decision.IngredientId)
                        || directAuthorizationBlocked.Contains(decision.IngredientId)
                        || decision.State == PageIngredientExecutionState.ExcludedByApprovedDisposition
                        || decision.State == PageIngredientExecutionState.SatisfiedByPolicy
                        || !actions.TryGetValue(decision.IngredientId, out var consumer)
                        || !IsRetained(consumer.Disposition)
                        || !requiredDependencies.TryGetValue(decision.IngredientId, out var dependencies))
                    {
                        continue;
                    }

                    var authorizationCauses = new HashSet<string>(StringComparer.Ordinal);
                    var deferredCauses = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var edge in dependencies)
                    {
                        var explicitlyReleased = consumer.Disposition == IngredientDisposition.Transform
                            && consumer.ReleasedDependencyIngredientIds != null
                            && consumer.ReleasedDependencyIngredientIds.Contains(edge.ToIngredientId, StringComparer.Ordinal);
                        if (explicitlyReleased || !decisions.TryGetValue(edge.ToIngredientId, out var dependency))
                        {
                            continue;
                        }

                        if (dependency.State == PageIngredientExecutionState.AuthorizationBlocked
                            || dependency.State == PageIngredientExecutionState.SkippedByAuthorizationDependency)
                        {
                            AddCauses(authorizationCauses, dependency);
                        }
                        else if (dependency.State == PageIngredientExecutionState.Deferred
                            || dependency.State == PageIngredientExecutionState.SkippedByDeferredDependency)
                        {
                            AddCauses(deferredCauses, dependency);
                        }
                    }

                    var nextState = authorizationCauses.Count > 0
                        ? PageIngredientExecutionState.SkippedByAuthorizationDependency
                        : deferredCauses.Count > 0
                            ? PageIngredientExecutionState.SkippedByDeferredDependency
                            : PageIngredientExecutionState.Executable;
                    var nextCauses = authorizationCauses.Count > 0
                        ? authorizationCauses
                        : deferredCauses;
                    if (decision.State != nextState
                        || !new HashSet<string>(decision.CauseIngredientIds, StringComparer.Ordinal).SetEquals(nextCauses))
                    {
                        decision.State = nextState;
                        decision.CauseIngredientIds = nextCauses.OrderBy(value => value, StringComparer.Ordinal).ToList();
                        changed = true;
                    }
                }
            }

            return new PageIngredientExecutionFrontier
            {
                Decisions = decisions.Values
                    .OrderBy(value => value.IngredientId, StringComparer.Ordinal)
                    .ToList()
            };
        }

        private static void AddCauses(ISet<string> causes, PageIngredientExecutionDecision dependency)
        {
            if (dependency.CauseIngredientIds == null || dependency.CauseIngredientIds.Count == 0)
            {
                causes.Add(dependency.IngredientId);
                return;
            }
            foreach (var cause in dependency.CauseIngredientIds.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                causes.Add(cause);
            }
        }

        private static string EdgeIdentity(string from, string to)
        {
            return (from ?? string.Empty) + "\u001f" + (to ?? string.Empty);
        }

        private static MigrationIssue Issue(
            string code,
            string ingredient,
            string message,
            MigrationIssueSeverity severity)
        {
            return new MigrationIssue
            {
                Code = code,
                Severity = severity,
                Subject = ingredient,
                Ingredient = ingredient,
                Message = message
            };
        }

        private static PageIngredientPlanEvaluation Result(
            PageMigrationOutcome outcome,
            IList<MigrationIssue> issues,
            PageIngredientExecutionFrontier executionFrontier)
        {
            return new PageIngredientPlanEvaluation
            {
                Outcome = outcome,
                Issues = issues,
                ExecutionFrontier = executionFrontier ?? new PageIngredientExecutionFrontier()
            };
        }
    }
}
