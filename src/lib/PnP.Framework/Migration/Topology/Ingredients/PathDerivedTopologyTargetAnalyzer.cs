using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class PathDerivedTopologyTargetAnalyzer
    {
        public static PathDerivedTargetWebProbe AnalyzeContainer(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebObservation observation,
            Guid? expectedParentWebId = null)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }
            if (observation == null
                || !string.Equals(observation.GlobalActionKey, container.GlobalActionKey, StringComparison.Ordinal))
            {
                throw new System.IO.InvalidDataException("A fresh target observation must identify the requested global action exactly.");
            }

            var probe = AnalyzeObservation(container, observation);
            if (expectedParentWebId.HasValue
                && (!probe.TargetParentWebId.HasValue
                    || probe.TargetParentWebId.Value != expectedParentWebId.Value))
            {
                return Block(
                    container,
                    "PathDerivedTargetParentIdentityMismatch",
                    "The observed target Web is not a child of the verified parent global action.");
            }
            return probe;
        }

        public static SharedTopologyGlobalTargetAnalysis Analyze(
            SharedTopologyGlobalActionDag dag,
            IEnumerable<PathDerivedTargetWebObservation> observations)
        {
            ValidateDag(dag);
            var byAction = (observations ?? Enumerable.Empty<PathDerivedTargetWebObservation>())
                .Where(value => value != null)
                .GroupBy(value => value.GlobalActionKey, StringComparer.Ordinal)
                .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.Ordinal);
            var result = new SharedTopologyGlobalTargetAnalysis
            {
                GlobalActionDagDigest = dag.DagDigest
            };
            var probes = new Dictionary<string, PathDerivedTargetWebProbe>(StringComparer.Ordinal);
            foreach (var container in dag.Actions.OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl)))
            {
                if (!string.IsNullOrWhiteSpace(container.ParentGlobalActionKey)
                    && probes.TryGetValue(container.ParentGlobalActionKey, out var parent)
                    && !parent.IsExecutable)
                {
                    var skipped = Base(container);
                    skipped.State = TargetWebContainerState.SkippedByDependency;
                    skipped.CauseGlobalActionKeys.Add(parent.GlobalActionKey);
                    foreach (var cause in parent.CauseGlobalActionKeys)
                    {
                        skipped.CauseGlobalActionKeys.Add(cause);
                    }
                    skipped.Issues.Add(Issue(
                        "PathDerivedTargetParentBlocked",
                        container.TargetSlotKey,
                        "The direct parent global topology action is not executable."));
                    probes.Add(container.GlobalActionKey, skipped);
                    result.Probes.Add(skipped);
                    foreach (var issue in skipped.Issues)
                    {
                        result.Issues.Add(issue);
                    }
                    continue;
                }

                byAction.TryGetValue(container.GlobalActionKey, out var candidates);
                PathDerivedTargetWebProbe probe;
                if (candidates != null && candidates.Length > 1)
                {
                    probe = Block(container, "PathDerivedTargetObservationDuplicate", "The target slot has more than one fresh observation.");
                }
                else if (candidates == null || candidates.Length == 0)
                {
                    var parentWillBeCreated = !string.IsNullOrWhiteSpace(container.ParentGlobalActionKey)
                        && probes.TryGetValue(container.ParentGlobalActionKey, out var plannedParent)
                        && (plannedParent.State == TargetWebContainerState.CreateMissing
                            || plannedParent.State == TargetWebContainerState.RecoverInterruptedCreate);
                    probe = parentWillBeCreated
                        ? Missing(container)
                        : Block(container, "PathDerivedTargetInspectionRequired", "The target-Web container requires a fresh observation.", TargetWebContainerState.TargetInspectionRequired);
                }
                else
                {
                    probe = AnalyzeObservation(container, candidates[0]);
                }
                if (!string.IsNullOrWhiteSpace(container.ParentGlobalActionKey)
                    && probes.TryGetValue(container.ParentGlobalActionKey, out var observedParent)
                    && observedParent.TargetWebId.HasValue
                    && probe.TargetParentWebId.HasValue
                    && observedParent.TargetWebId != probe.TargetParentWebId)
                {
                    probe = Block(container, "PathDerivedTargetParentIdentityMismatch", "The observed target Web is not a child of the verified parent global action.");
                }
                probes.Add(container.GlobalActionKey, probe);
                result.Probes.Add(probe);
                foreach (var issue in probe.Issues)
                {
                    result.Issues.Add(issue);
                }
            }
            result.Probes = result.Probes.OrderBy(value => value.TargetSlotKey, StringComparer.Ordinal).ToList();
            result.Issues = result.Issues.OrderBy(value => value.Code, StringComparer.Ordinal)
                .ThenBy(value => value.Subject, StringComparer.Ordinal)
                .ToList();
            result.AnalysisDigest = SharedTopologyGlobalExecutionDigest.ComputeAnalysis(result);
            return result;
        }

        public static string InterruptedCreateDescription(TargetWebContainerIngredientPlan container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }
            return "PnP migration mapping for " + container.OriginalIdentifier;
        }

        public static bool IsRetryableStatus(int statusCode)
        {
            return statusCode == 408
                || statusCode == 409
                || statusCode == 423
                || statusCode == 429
                || statusCode >= 500 && statusCode <= 599;
        }

        private static PathDerivedTargetWebProbe AnalyzeObservation(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebObservation observation)
        {
            var probe = Base(container);
            if (observation.HttpStatusCode.HasValue)
            {
                var status = observation.HttpStatusCode.Value;
                if (status == 404)
                {
                    return Missing(container, observation);
                }
                if (status == 401 || status == 403)
                {
                    try
                    {
                        LiteralHttpAuthorizationEvidence.Validate(observation.AuthorizationEvidence);
                        if (observation.AuthorizationEvidence.HttpStatusCode != status)
                        {
                            throw new System.IO.InvalidDataException("Authorization evidence status differs from the observation.");
                        }
                    }
                    catch (System.IO.InvalidDataException exception)
                    {
                        return Block(container, "PathDerivedTargetAuthorizationEvidenceInvalid", exception.Message);
                    }
                    probe.State = TargetWebContainerState.AuthorizationBlocked;
                    probe.AuthorizationEvidence = observation.AuthorizationEvidence;
                    probe.CauseGlobalActionKeys.Add(container.GlobalActionKey);
                    probe.Issues.Add(Issue(
                        "PathDerivedTargetAuthorizationBlocked",
                        container.TargetSlotKey,
                        "Target Web inspection returned literal HTTP " + status.ToString(CultureInfo.InvariantCulture) + "."));
                    return probe;
                }
                return Block(
                    container,
                    IsRetryableStatus(status) ? "PathDerivedTargetRetryRequired" : "PathDerivedTargetInspectionFailed",
                    "Target Web inspection returned HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
                    IsRetryableStatus(status) ? TargetWebContainerState.RetryRequired : TargetWebContainerState.CollisionBlocked);
            }
            if (observation.IdentityConflict)
            {
                return Block(container, "PathDerivedTargetIdentityCollision", observation.Diagnostic ?? "Target identity differs from the approved slot.");
            }
            if (observation.InspectionFailed)
            {
                return Block(container, "PathDerivedTargetRetryRequired", observation.Diagnostic ?? "Target inspection failed without an HTTP response.", TargetWebContainerState.RetryRequired);
            }
            if (!observation.Exists)
            {
                return Missing(container, observation);
            }

            probe.TargetSiteId = observation.TargetSiteId;
            probe.TargetWebId = observation.TargetWebId;
            probe.TargetParentWebId = observation.TargetParentWebId;
            probe.ObservedOriginalIdentifier = observation.ExistingOriginalIdentifier;
            probe.ObservedMappingDigest = observation.ExistingMappingDigest;
            probe.ObservedTitle = observation.ExistingTitle;
            probe.ObservedDescription = observation.ExistingDescription;
            probe.ObservedTemplate = observation.ExistingTemplate;
            probe.ObservedConfiguration = observation.ExistingConfiguration;
            probe.ObservedHasUniqueRoleAssignments = observation.ExistingHasUniqueRoleAssignments;
            if (!ExactIdentity(container, observation) || !TemplateMatches(container, observation))
            {
                return Block(container, "PathDerivedTargetIdentityCollision", "The occupied target path has a different Site/Web/parent identity or template shape.");
            }

            var exactOwned = string.Equals(observation.ExistingOriginalIdentifier, container.OriginalIdentifier, StringComparison.Ordinal)
                && string.Equals(observation.ExistingMappingDigest, container.ActionSignatureDigest, StringComparison.OrdinalIgnoreCase)
                && OwnedShapeMatches(container, observation);
            if (exactOwned)
            {
                probe.State = TargetWebContainerState.ReuseOwned;
                probe.Ownership = SharedTopologyOwnership.MigrationOwned;
                return probe;
            }
            var noOwnership = string.IsNullOrWhiteSpace(observation.ExistingOriginalIdentifier)
                && string.IsNullOrWhiteSpace(observation.ExistingMappingDigest);
            if (noOwnership
                && string.Equals(observation.ExistingDescription, InterruptedCreateDescription(container), StringComparison.Ordinal)
                && OwnedShapeMatches(container, observation))
            {
                probe.State = TargetWebContainerState.RecoverInterruptedCreate;
                probe.Ownership = SharedTopologyOwnership.MigrationOwned;
                return probe;
            }
            if (noOwnership
                && container.ApprovedExistingTargetWebId.HasValue
                && observation.TargetWebId == container.ApprovedExistingTargetWebId)
            {
                probe.State = TargetWebContainerState.ReuseExplicitApprovedHost;
                probe.Ownership = SharedTopologyOwnership.ExternalApprovedHost;
                return probe;
            }
            return Block(container, "PathDerivedTargetOwnershipCollision", "The exact path is occupied without matching migration provenance or an explicit approved-host identity.");
        }

        private static bool ExactIdentity(TargetWebContainerIngredientPlan container, PathDerivedTargetWebObservation observation)
        {
            return observation.TargetSiteId.HasValue
                && observation.TargetWebId.HasValue
                && observation.TargetParentWebId.HasValue
                && (!container.ExpectedTargetSiteId.HasValue || container.ExpectedTargetSiteId == observation.TargetSiteId)
                && SharedTopologyPath.EqualsUrl(observation.TargetWebUrl, container.TargetWebUrl)
                && SharedTopologyPath.EqualsPath(observation.TargetServerRelativeUrl, container.TargetServerRelativeUrl);
        }

        private static bool TemplateMatches(TargetWebContainerIngredientPlan container, PathDerivedTargetWebObservation observation)
        {
            if (!observation.ExistingConfiguration.HasValue)
            {
                return false;
            }
            var parts = (container.Provisioning.Template ?? string.Empty).Split('#');
            var template = parts[0];
            var configuration = parts.Length > 1
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : container.Provisioning.Configuration;
            return string.Equals(observation.ExistingTemplate, template, StringComparison.OrdinalIgnoreCase)
                && observation.ExistingConfiguration.Value == configuration;
        }

        private static bool OwnedShapeMatches(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebObservation observation)
        {
            return string.Equals(observation.ExistingTitle, container.Provisioning.Title, StringComparison.Ordinal)
                && string.Equals(observation.ExistingDescription, InterruptedCreateDescription(container), StringComparison.Ordinal)
                && observation.ExistingHasUniqueRoleAssignments.HasValue
                && observation.ExistingHasUniqueRoleAssignments.Value
                    == !container.Provisioning.UseSamePermissionsAsParentWeb;
        }

        private static PathDerivedTargetWebProbe Missing(TargetWebContainerIngredientPlan container)
        {
            var probe = Base(container);
            probe.State = TargetWebContainerState.CreateMissing;
            return probe;
        }

        private static PathDerivedTargetWebProbe Missing(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebObservation observation)
        {
            var probe = Missing(container);
            probe.TargetSiteId = observation.TargetSiteId;
            probe.TargetParentWebId = observation.TargetParentWebId;
            return probe;
        }

        private static PathDerivedTargetWebProbe Block(
            TargetWebContainerIngredientPlan container,
            string code,
            string message,
            TargetWebContainerState state = TargetWebContainerState.CollisionBlocked)
        {
            var probe = Base(container);
            probe.State = state;
            probe.CauseGlobalActionKeys.Add(container.GlobalActionKey);
            probe.Issues.Add(Issue(code, container.TargetSlotKey, message));
            return probe;
        }

        private static PathDerivedTargetWebProbe Base(TargetWebContainerIngredientPlan container)
        {
            return new PathDerivedTargetWebProbe
            {
                TargetSlotKey = container.TargetSlotKey,
                GlobalActionKey = container.GlobalActionKey,
                ParentGlobalActionKey = container.ParentGlobalActionKey
            };
        }

        private static MigrationIssue Issue(string code, string subject, string message)
        {
            return new MigrationIssue
            {
                Code = code,
                Severity = MigrationIssueSeverity.Blocker,
                Subject = subject,
                Ingredient = "Topology.PathDerivedTargetWeb",
                Message = message
            };
        }

        private static void ValidateDag(SharedTopologyGlobalActionDag dag)
        {
            SharedTopologyGlobalExecutionValidator.ValidateDag(dag);
        }
    }
}
