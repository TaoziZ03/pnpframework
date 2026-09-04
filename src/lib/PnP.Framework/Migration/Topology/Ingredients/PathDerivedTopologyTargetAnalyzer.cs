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
            if (!container.IsTargetSiteRoot
                && expectedParentWebId.HasValue
                && (!probe.TargetParentWebId.HasValue || probe.TargetParentWebId.Value != expectedParentWebId.Value))
            {
                return Block(container, "PathDerivedTargetParentIdentityMismatch", "The observed target Web is not a child of the verified parent global action.");
            }
            return probe;
        }

        public static SharedTopologyGlobalTargetAnalysis Analyze(
            SharedTopologyGlobalActionDag dag,
            IEnumerable<PathDerivedTargetWebObservation> observations)
        {
            SharedTopologyGlobalExecutionValidator.ValidateDag(dag);
            var byAction = (observations ?? Enumerable.Empty<PathDerivedTargetWebObservation>())
                .Where(value => value != null)
                .GroupBy(value => value.GlobalActionKey, StringComparer.Ordinal)
                .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.Ordinal);
            var result = new SharedTopologyGlobalTargetAnalysis { GlobalActionDagDigest = dag.DagDigest };
            var probes = new Dictionary<string, PathDerivedTargetWebProbe>(StringComparer.Ordinal);
            foreach (var container in dag.Actions
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                .ThenBy(value => value.TargetSlotKey, StringComparer.Ordinal))
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
                    skipped.Issues.Add(Issue("PathDerivedTargetParentBlocked", container.TargetSlotKey, "The direct parent global topology action is not executable."));
                    Add(result, probes, container, skipped);
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
                        ? Missing(container, null)
                        : Block(container, "PathDerivedTargetInspectionRequired", "The target-Web action requires a fresh observation.", TargetWebContainerState.TargetInspectionRequired);
                }
                else
                {
                    probe = AnalyzeObservation(container, candidates[0]);
                }
                if (!container.IsTargetSiteRoot
                    && !string.IsNullOrWhiteSpace(container.ParentGlobalActionKey)
                    && probes.TryGetValue(container.ParentGlobalActionKey, out var observedParent)
                    && observedParent.TargetWebId.HasValue
                    && probe.TargetParentWebId.HasValue
                    && observedParent.TargetWebId != probe.TargetParentWebId)
                {
                    probe = Block(container, "PathDerivedTargetParentIdentityMismatch", "The observed target Web is not a child of the verified parent global action.");
                }
                Add(result, probes, container, probe);
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
            return "PnP migration mapping for " + (container ?? throw new ArgumentNullException(nameof(container))).OriginalIdentifier;
        }

        public static bool IsRetryableStatus(int statusCode)
        {
            return statusCode == 408 || statusCode == 409 || statusCode == 423 || statusCode == 429
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
                if (status == 401 || status == 403)
                {
                    try
                    {
                        BoundLiteralHttpAuthorizationEvidence.Validate(
                            observation.AuthorizationEvidence,
                            container.ActionSignature.ActionId);
                        if (observation.AuthorizationEvidence.LiteralEvidence.HttpStatusCode != status)
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
                    probe.Issues.Add(Issue("PathDerivedTargetAuthorizationBlocked", container.TargetSlotKey,
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
                if (container.IsTargetSiteRoot)
                {
                    return Block(container, "PathDerivedTargetRootMissing", "The approved existing target Site root is missing.");
                }
                if (observation.TargetSiteId != container.ExpectedTargetSiteId
                    || !observation.TargetParentWebId.HasValue
                    || !SharedTopologyPath.EqualsUrl(observation.TargetWebUrl, container.TargetWebUrl)
                    || !SharedTopologyPath.EqualsPath(observation.TargetServerRelativeUrl, container.TargetServerRelativeUrl))
                {
                    return Block(container, "PathDerivedTargetMissingIdentityInvalid",
                        "A successful missing-child observation must retain the exact target Site, parent, URL, and path identity.");
                }
                return Missing(container, observation);
            }

            CopyObserved(probe, observation);
            if (!ExactIdentity(container, observation) || !TemplateMatches(container, observation))
            {
                return Block(container, "PathDerivedTargetIdentityCollision", "The occupied target path has a different Site/Web/parent identity or target profile.");
            }
            var noOwnership = string.IsNullOrWhiteSpace(observation.ExistingOriginalIdentifier)
                && string.IsNullOrWhiteSpace(observation.ExistingMappingDigest);
            if (container.ExpectedOwnership == SharedTopologyOwnership.ExternalApprovedHost)
            {
                if (noOwnership
                    && container.ApprovedExistingTargetWebId.HasValue
                    && observation.TargetWebId == container.ApprovedExistingTargetWebId
                    && OwnedShapeMatches(container, observation, requireDescription: false))
                {
                    probe.State = TargetWebContainerState.ReuseExplicitApprovedHost;
                    probe.Ownership = SharedTopologyOwnership.ExternalApprovedHost;
                    probe.ObservedStateDigest = SharedTopologyDigest.ComputeObservedSemanticState(container, observation, probe.Ownership.Value);
                    return SemanticMatches(container, probe);
                }
                return Block(container, "PathDerivedTargetExternalHostMismatch", "The external target Web differs from its exact approved ID, profile, or no-ownership boundary.");
            }

            var exactOwned = string.Equals(observation.ExistingOriginalIdentifier, container.OriginalIdentifier, StringComparison.Ordinal)
                && string.Equals(observation.ExistingMappingDigest, container.SemanticMappingDigest, StringComparison.OrdinalIgnoreCase)
                && OwnedShapeMatches(container, observation, requireDescription: true);
            if (exactOwned)
            {
                probe.State = TargetWebContainerState.ReuseOwned;
                probe.Ownership = SharedTopologyOwnership.MigrationOwned;
                probe.ObservedStateDigest = SharedTopologyDigest.ComputeObservedSemanticState(container, observation, probe.Ownership.Value);
                return SemanticMatches(container, probe);
            }
            if (noOwnership
                && string.Equals(observation.ExistingDescription, InterruptedCreateDescription(container), StringComparison.Ordinal)
                && OwnedShapeMatches(container, observation, requireDescription: true))
            {
                probe.State = TargetWebContainerState.RecoverInterruptedCreate;
                probe.Ownership = SharedTopologyOwnership.MigrationOwned;
                return probe;
            }
            return Block(container, "PathDerivedTargetOwnershipCollision", "The exact path is occupied without matching migration provenance or an explicit approved-host identity.");
        }

        private static PathDerivedTargetWebProbe SemanticMatches(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebProbe probe)
        {
            return string.Equals(probe.ObservedStateDigest, container.ActionSignature.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                ? probe
                : Block(container, "PathDerivedTargetSemanticDrift", "Fresh target state differs from the generic action signature semantic digest.");
        }

        private static bool ExactIdentity(TargetWebContainerIngredientPlan container, PathDerivedTargetWebObservation observation)
        {
            return observation.TargetSiteId.HasValue
                && observation.TargetWebId.HasValue
                && observation.TargetSiteId.Value == container.ExpectedTargetSiteId
                && SharedTopologyPath.EqualsUrl(observation.TargetWebUrl, container.TargetWebUrl)
                && SharedTopologyPath.EqualsPath(observation.TargetServerRelativeUrl, container.TargetServerRelativeUrl)
                && (container.IsTargetSiteRoot || observation.TargetParentWebId.HasValue);
        }

        private static bool TemplateMatches(TargetWebContainerIngredientPlan container, PathDerivedTargetWebObservation observation)
        {
            if (!observation.ExistingConfiguration.HasValue || !observation.ExistingLanguage.HasValue)
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
                && observation.ExistingConfiguration.Value == configuration
                && observation.ExistingLanguage.Value == container.Provisioning.Language;
        }

        private static bool OwnedShapeMatches(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebObservation observation,
            bool requireDescription)
        {
            return string.Equals(observation.ExistingTitle, container.Provisioning.Title, StringComparison.Ordinal)
                && (!requireDescription
                    || string.Equals(observation.ExistingDescription, InterruptedCreateDescription(container), StringComparison.Ordinal))
                && observation.ExistingHasUniqueRoleAssignments.HasValue
                && observation.ExistingHasUniqueRoleAssignments.Value == !container.Provisioning.UseSamePermissionsAsParentWeb;
        }

        private static void CopyObserved(PathDerivedTargetWebProbe probe, PathDerivedTargetWebObservation observation)
        {
            probe.TargetSiteId = observation.TargetSiteId;
            probe.TargetWebId = observation.TargetWebId;
            probe.TargetParentWebId = observation.TargetParentWebId;
            probe.ObservedOriginalIdentifier = observation.ExistingOriginalIdentifier;
            probe.ObservedMappingDigest = observation.ExistingMappingDigest;
            probe.ObservedTitle = observation.ExistingTitle;
            probe.ObservedDescription = observation.ExistingDescription;
            probe.ObservedTemplate = observation.ExistingTemplate;
            probe.ObservedConfiguration = observation.ExistingConfiguration;
            probe.ObservedLanguage = observation.ExistingLanguage;
            probe.ObservedHasUniqueRoleAssignments = observation.ExistingHasUniqueRoleAssignments;
        }

        private static PathDerivedTargetWebProbe Missing(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebObservation observation)
        {
            var probe = Base(container);
            probe.State = TargetWebContainerState.CreateMissing;
            probe.TargetSiteId = observation?.TargetSiteId;
            probe.TargetParentWebId = observation?.TargetParentWebId;
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

        private static void Add(
            SharedTopologyGlobalTargetAnalysis result,
            IDictionary<string, PathDerivedTargetWebProbe> probes,
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebProbe probe)
        {
            probes.Add(container.GlobalActionKey, probe);
            result.Probes.Add(probe);
            foreach (var issue in probe.Issues)
            {
                result.Issues.Add(issue);
            }
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
    }
}
