using PnP.Framework.Migration.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyTargetAnalyzer
    {
        public static SharedTopologyTargetAnalysis Analyze(
            SharedTopologyPlan plan,
            SharedTopologyTargetSiteObservation targetSite,
            IEnumerable<TargetWebContainerObservation> observations)
        {
            SharedTopologyPlanValidator.Validate(plan);
            var result = new SharedTopologyTargetAnalysis
            {
                SharedTopologyPlanDigest = plan.PlanDigest,
                TargetSite = targetSite
            };
            if (!ValidateTargetSite(plan, targetSite, result.Issues))
            {
                foreach (var container in plan.TargetWebContainers.OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl)))
                {
                    result.TargetWebContainers.Add(Skipped(container, plan.TargetSite.IngredientId));
                }
                result.AnalysisDigest = SharedTopologyExecutionDigest.ComputeAnalysis(result);
                return result;
            }

            var observationById = (observations ?? Enumerable.Empty<TargetWebContainerObservation>())
                .Where(value => value != null)
                .GroupBy(value => value.IngredientId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var duplicate in observationById.Where(value => string.IsNullOrWhiteSpace(value.Key) || value.Value.Length != 1))
            {
                result.Issues.Add(Issue(
                    "TargetWebObservationInvalid",
                    duplicate.Key ?? "target-web-observation",
                    "Each target-Web container may have at most one observation.",
                    MigrationIssueSeverity.Error));
            }

            var probes = new Dictionary<string, TargetWebContainerProbe>(StringComparer.Ordinal);
            foreach (var container in plan.TargetWebContainers.OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl)))
            {
                if (!string.Equals(container.ParentIngredientId, plan.TargetSite.IngredientId, StringComparison.Ordinal)
                    && probes.TryGetValue(container.ParentIngredientId, out var parent)
                    && !parent.IsActionable)
                {
                    var skipped = Skipped(container, parent.IngredientId, parent.CauseIngredientIds);
                    probes.Add(container.IngredientId, skipped);
                    result.TargetWebContainers.Add(skipped);
                    continue;
                }

                observationById.TryGetValue(container.IngredientId, out var candidates);
                var observation = candidates?.SingleOrDefault();
                TargetWebContainerProbe probe;
                if (observation == null)
                {
                    var parentWillBeCreated = probes.TryGetValue(container.ParentIngredientId, out var parentProbe)
                        && parentProbe.State == TargetWebContainerState.CreateMissing;
                    probe = parentWillBeCreated
                        ? Missing(container, "The direct parent is planned for creation, so this exact child path is also planned as missing.")
                        : Pending(container);
                }
                else
                {
                    probe = AnalyzeObservation(plan, container, observation);
                }
                probes.Add(container.IngredientId, probe);
                result.TargetWebContainers.Add(probe);
                foreach (var issue in probe.Issues)
                {
                    result.Issues.Add(issue);
                }
            }

            result.TargetWebContainers = result.TargetWebContainers
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                .ThenBy(value => value.TargetServerRelativeUrl, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Issues = result.Issues
                .OrderBy(value => value.Code, StringComparer.Ordinal)
                .ThenBy(value => value.Subject, StringComparer.Ordinal)
                .ToList();
            result.AnalysisDigest = SharedTopologyExecutionDigest.ComputeAnalysis(result);
            return result;
        }

        public static bool IsAuthorizationStatus(int statusCode)
        {
            return statusCode == 401 || statusCode == 403;
        }

        public static bool IsRetryableStatus(int statusCode)
        {
            return statusCode == 408
                || statusCode == 409
                || statusCode == 423
                || statusCode == 429
                || statusCode >= 500 && statusCode <= 599;
        }

        private static bool ValidateTargetSite(
            SharedTopologyPlan plan,
            SharedTopologyTargetSiteObservation observation,
            ICollection<MigrationIssue> issues)
        {
            if (observation == null)
            {
                issues.Add(Issue("TargetSiteInspectionRequired", plan.TargetSite.IngredientId, "The target Site Collection has not been freshly inspected.", MigrationIssueSeverity.Blocker));
                return false;
            }
            if (observation.HttpStatusCode.HasValue)
            {
                var status = observation.HttpStatusCode.Value;
                issues.Add(Issue(
                    IsAuthorizationStatus(status) ? "TargetSiteAuthorizationBlocked" : IsRetryableStatus(status) ? "TargetSiteRetryableFailure" : "TargetSiteInspectionFailed",
                    plan.TargetSite.IngredientId,
                    "Target Site Collection inspection returned HTTP " + status.ToString(CultureInfo.InvariantCulture) + ". " + observation.Diagnostic,
                    MigrationIssueSeverity.Blocker));
                return false;
            }
            if (observation.InspectionFailed)
            {
                issues.Add(Issue("TargetSiteInspectionFailed", plan.TargetSite.IngredientId, observation.Diagnostic ?? "Target Site Collection inspection failed without a literal HTTP status.", MigrationIssueSeverity.Blocker));
                return false;
            }
            if (!observation.Exists
                || !observation.TargetSiteId.HasValue
                || !observation.TargetRootWebId.HasValue
                || !SharedTopologyPath.EqualsUrl(observation.TargetSiteCollectionUrl, plan.TargetSite.TargetSiteCollectionUrl)
                || plan.TargetSite.ExpectedTargetSiteId.HasValue && plan.TargetSite.ExpectedTargetSiteId != observation.TargetSiteId)
            {
                issues.Add(Issue("TargetSiteIdentityMismatch", plan.TargetSite.IngredientId, "The observed target Site Collection differs from the reviewed target identity.", MigrationIssueSeverity.Blocker));
                return false;
            }
            return true;
        }

        private static TargetWebContainerProbe AnalyzeObservation(
            SharedTopologyPlan plan,
            TargetWebContainerIngredientPlan container,
            TargetWebContainerObservation observation)
        {
            var probe = Base(container);
            probe.HttpStatusCode = observation.HttpStatusCode;
            if (observation.HttpStatusCode.HasValue)
            {
                var status = observation.HttpStatusCode.Value;
                if (status == 404)
                {
                    return Missing(container, observation.Diagnostic);
                }
                if (IsAuthorizationStatus(status))
                {
                    probe.State = TargetWebContainerState.AuthorizationBlocked;
                    probe.CauseIngredientIds.Add(container.IngredientId);
                    probe.Issues.Add(Issue(
                        "TargetWebAuthorizationBlocked",
                        container.IngredientId,
                        "Target Web inspection returned literal HTTP " + status.ToString(CultureInfo.InvariantCulture) + ". " + observation.Diagnostic,
                        MigrationIssueSeverity.Blocker));
                    return probe;
                }
                probe.State = IsRetryableStatus(status)
                    ? TargetWebContainerState.RetryableFailure
                    : TargetWebContainerState.CollisionBlocked;
                probe.CauseIngredientIds.Add(container.IngredientId);
                probe.Issues.Add(Issue(
                    IsRetryableStatus(status) ? "TargetWebRetryableFailure" : "TargetWebInspectionFailed",
                    container.IngredientId,
                    "Target Web inspection returned HTTP " + status.ToString(CultureInfo.InvariantCulture) + ". " + observation.Diagnostic,
                    MigrationIssueSeverity.Blocker));
                return probe;
            }
            if (observation.InspectionFailed)
            {
                probe.State = TargetWebContainerState.RetryableFailure;
                probe.CauseIngredientIds.Add(container.IngredientId);
                probe.Issues.Add(Issue(
                    "TargetWebInspectionFailed",
                    container.IngredientId,
                    observation.Diagnostic ?? "Target Web inspection failed without a literal HTTP status.",
                    MigrationIssueSeverity.Blocker));
                return probe;
            }
            if (!observation.Exists)
            {
                return Missing(container, observation.Diagnostic);
            }

            probe.Exists = true;
            probe.TargetSiteId = observation.TargetSiteId;
            probe.TargetWebId = observation.TargetWebId;
            probe.TargetParentWebId = observation.TargetParentWebId;
            var identityMatches = observation.TargetWebId.HasValue
                && observation.TargetParentWebId.HasValue
                && SharedTopologyPath.EqualsUrl(observation.TargetWebUrl, container.TargetWebUrl)
                && SharedTopologyPath.EqualsPath(observation.TargetServerRelativeUrl, container.TargetServerRelativeUrl);
            var templateMatches = TemplateMatches(
                observation.ExistingTemplate,
                observation.ExistingConfiguration,
                container.Provisioning.Template,
                container.Provisioning.Configuration);
            var owned = string.Equals(observation.ExistingIngredientId, container.IngredientId, StringComparison.Ordinal)
                && string.Equals(observation.ExistingPlanDigest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase);
            probe.IsMigrationOwned = owned;
            if (identityMatches && templateMatches && (owned || container.AllowReuseExistingExactPath))
            {
                probe.State = TargetWebContainerState.Reuse;
                if (!string.Equals(observation.ExistingTitle, container.Provisioning.Title, StringComparison.Ordinal))
                {
                    probe.Issues.Add(Issue(
                        "TargetWebTitleExpectedDifference",
                        container.IngredientId,
                        "The reusable target path has title '" + observation.ExistingTitle + "'; the target-only creation title '" + container.Provisioning.Title + "' is not applied to an existing Web.",
                        MigrationIssueSeverity.Warning));
                }
                return probe;
            }

            probe.State = TargetWebContainerState.CollisionBlocked;
            probe.CauseIngredientIds.Add(container.IngredientId);
            probe.Issues.Add(Issue(
                "TargetWebPathCollision",
                container.IngredientId,
                "The exact target path is occupied by a Web that cannot be reused under the reviewed target provisioning policy.",
                MigrationIssueSeverity.Blocker));
            return probe;
        }

        private static bool TemplateMatches(string observedTemplate, int? observedConfiguration, string expectedTemplate, int expectedConfiguration)
        {
            if (!observedConfiguration.HasValue)
            {
                return false;
            }
            var parts = (expectedTemplate ?? string.Empty).Split('#');
            var template = parts[0];
            var configuration = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : expectedConfiguration;
            return string.Equals(observedTemplate, template, StringComparison.OrdinalIgnoreCase)
                && observedConfiguration.Value == configuration;
        }

        private static TargetWebContainerProbe Pending(TargetWebContainerIngredientPlan container)
        {
            var result = Base(container);
            result.State = TargetWebContainerState.TargetInspectionRequired;
            result.CauseIngredientIds.Add(container.IngredientId);
            result.Issues.Add(Issue(
                "TargetWebInspectionRequired",
                container.IngredientId,
                "The exact target Web path has no fresh observation.",
                MigrationIssueSeverity.Blocker));
            return result;
        }

        private static TargetWebContainerProbe Missing(TargetWebContainerIngredientPlan container, string diagnostic)
        {
            var result = Base(container);
            result.State = TargetWebContainerState.CreateMissing;
            result.Exists = false;
            return result;
        }

        private static TargetWebContainerProbe Skipped(
            TargetWebContainerIngredientPlan container,
            string cause,
            IEnumerable<string> inheritedCauses = null)
        {
            var result = Base(container);
            result.State = TargetWebContainerState.SkippedByDependency;
            foreach (var value in (inheritedCauses ?? Array.Empty<string>()).Concat(new[] { cause }).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
            {
                result.CauseIngredientIds.Add(value);
            }
            result.Issues.Add(Issue(
                "TargetWebParentBlocked",
                container.IngredientId,
                "The direct target parent container is not actionable.",
                MigrationIssueSeverity.Blocker));
            return result;
        }

        private static TargetWebContainerProbe Base(TargetWebContainerIngredientPlan container)
        {
            return new TargetWebContainerProbe
            {
                IngredientId = container.IngredientId,
                ParentIngredientId = container.ParentIngredientId,
                TargetWebUrl = container.TargetWebUrl,
                TargetServerRelativeUrl = container.TargetServerRelativeUrl
            };
        }

        private static MigrationIssue Issue(string code, string subject, string message, MigrationIssueSeverity severity)
        {
            return new MigrationIssue
            {
                Code = code,
                Severity = severity,
                Subject = subject,
                Ingredient = "Topology.TargetWebContainer",
                Message = message
            };
        }
    }
}
