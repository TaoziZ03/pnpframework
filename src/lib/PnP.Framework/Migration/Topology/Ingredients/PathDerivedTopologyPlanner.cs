using PnP.Framework.Migration.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class PathDerivedTopologyPlanner
    {
        public SharedTopologyPlanBuildResult Build(PathDerivedTopologyPlanningRequest request)
        {
            var issues = new List<MigrationIssue>();
            if (request == null)
            {
                issues.Add(Issue("PathDerivedTopologyRequestMissing", "path-derived-topology", "A path-derived topology planning request is required."));
                return Result(null, issues);
            }

            string[] confirmedForeignCollisions;
            try
            {
                PathDerivedSourceTopologyEvidenceFactory.Validate(request.Source);
                ValidatePolicy(request.ProvisioningPolicy);
                SharedTopologyPath.ValidateUrlMatchesPath(request.TargetSiteCollectionUrl, request.TargetSiteServerRelativeUrl, nameof(request.TargetSiteCollectionUrl));
                confirmedForeignCollisions = (request.ConfirmedForeignCollisionServerRelativeUrls ?? Array.Empty<string>())
                    .Select(value => SharedTopologyPath.NormalizeServerRelativePath(value, nameof(request.ConfirmedForeignCollisionServerRelativeUrls)))
                    .ToArray();
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is System.IO.InvalidDataException)
            {
                issues.Add(Issue("PathDerivedTopologyEvidenceInvalid", "path-derived-topology", exception.Message));
                return Result(null, issues);
            }

            var sourceSegments = SharedTopologyPath.RelativeSegments(
                request.Source.SourceSiteServerRelativeUrl,
                request.Source.SourceLeafWebServerRelativeUrl);
            if (sourceSegments.Length == 0)
            {
                issues.Add(Issue(
                    "PathDerivedTopologyRootUnsupported",
                    request.Source.SourceLeafWebServerRelativeUrl,
                    "Path-derived topology is only needed for a leaf Web below the captured Site Collection root."));
                return Result(null, issues);
            }

            var targetSitePath = SharedTopologyPath.NormalizeServerRelativePath(
                request.TargetSiteServerRelativeUrl,
                nameof(request.TargetSiteServerRelativeUrl));
            var targetSiteUrl = SharedTopologyPath.NormalizeAbsoluteUrl(
                request.TargetSiteCollectionUrl,
                nameof(request.TargetSiteCollectionUrl));
            if (confirmedForeignCollisions.Any(value => !SharedTopologyPath.EqualsPath(value, targetSitePath)
                && !SharedTopologyPath.NormalizeServerRelativePath(value, nameof(request.ConfirmedForeignCollisionServerRelativeUrls))
                    .StartsWith(targetSitePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Issue("TargetWebCollisionInventoryInvalid", targetSitePath, "Confirmed collision paths must stay inside the mapped target Site Collection."));
                return Result(null, issues);
            }
            var targetSite = new TargetSiteCollectionIngredientPlan
            {
                IngredientId = SharedTopologyIdentity.TargetSite(targetSitePath),
                TargetSiteCollectionUrl = targetSiteUrl,
                TargetServerRelativeUrl = targetSitePath,
                ExpectedTargetSiteId = request.ExpectedTargetSiteId
            };
            var fidelity = new SourceWebFidelityIngredientPlan
            {
                IngredientId = SharedTopologyIdentity.SourceWebFidelity(request.Source.SourceSiteId, request.Source.SourceLeafWebId),
                SourceSiteId = request.Source.SourceSiteId,
                SourceWebId = request.Source.SourceLeafWebId,
                SourceWebUrl = request.Source.SourceLeafWebUrl,
                SourceServerRelativeUrl = request.Source.SourceLeafWebServerRelativeUrl,
                State = request.Source.FidelityState,
                AuthorizationEvidence = request.Source.AuthorizationEvidence,
                EvidenceSha256 = request.Source.EvidenceSha256
            };

            var overrides = (request.ProvisioningPolicy.Overrides ?? new List<TargetWebProvisioningOverride>())
                .GroupBy(value => NormalizeRelativePath(value?.SourceRelativePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (var duplicate in overrides.Where(value => string.IsNullOrWhiteSpace(value.Key) || value.Value.Length != 1))
            {
                issues.Add(Issue("PathDerivedTopologyOverrideInvalid", duplicate.Key ?? "target-web-override", "Each source-relative path may have exactly one target provisioning override."));
            }
            if (issues.Count > 0)
            {
                return Result(null, issues);
            }
            var approvedHosts = (request.ProvisioningPolicy.ApprovedExistingWebs ?? new List<TargetWebApprovedHost>())
                .GroupBy(value => NormalizeRelativePath(value?.SourceRelativePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (var duplicate in approvedHosts.Where(value => string.IsNullOrWhiteSpace(value.Key)
                || value.Value.Length != 1
                || value.Value[0].ExpectedTargetWebId == Guid.Empty))
            {
                issues.Add(Issue("PathDerivedTopologyApprovedHostInvalid", duplicate.Key ?? "approved-target-web", "Each approved existing target Web path requires exactly one non-empty expected Web ID."));
            }
            if (issues.Count > 0)
            {
                return Result(null, issues);
            }

            var foreignCollisions = new HashSet<string>(
                confirmedForeignCollisions,
                StringComparer.OrdinalIgnoreCase);
            var containers = new List<TargetWebContainerIngredientPlan>();
            var preferredParentPath = targetSitePath;
            var targetParentPath = targetSitePath;
            var targetParentUrl = targetSiteUrl;
            var parentIngredientId = targetSite.IngredientId;
            string parentGlobalActionKey = null;
            var sourceRelativeSegments = new List<string>();
            var usedOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedApprovedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in sourceSegments)
            {
                sourceRelativeSegments.Add(segment);
                var relativePath = string.Join("/", sourceRelativeSegments);
                var preferredPath = SharedTopologyPath.Combine(preferredParentPath, segment);
                var targetPath = SharedTopologyPath.Combine(targetParentPath, segment);
                var collisionResolved = !SharedTopologyPath.EqualsPath(preferredParentPath, targetParentPath);
                string collisionReason = collisionResolved
                    ? "An ancestor segment used the reviewed StableSuffix collision policy; this source segment itself remains unchanged."
                    : null;
                if (foreignCollisions.Any(value => SharedTopologyPath.EqualsPath(value, targetPath)))
                {
                    if (request.ProvisioningPolicy.CollisionPolicy != TargetWebCollisionPolicy.StableSuffix)
                    {
                        issues.Add(Issue(
                            "TargetWebPathCollision",
                            targetPath,
                            "The exact path is a confirmed foreign collision and the reviewed target policy does not allow a suffix."));
                        return Result(null, issues);
                    }
                    targetPath = SharedTopologyPath.AllocateCollisionSuffix(
                        targetPath,
                        fidelity.IngredientId + "/" + relativePath,
                        foreignCollisions);
                    collisionResolved = true;
                    collisionReason = "The reviewed StableSuffix collision policy changed only the confirmed foreign-collision segment.";
                }

                overrides.TryGetValue(relativePath, out var candidates);
                var targetOverride = candidates?.SingleOrDefault();
                if (targetOverride != null)
                {
                    usedOverrides.Add(relativePath);
                }
                approvedHosts.TryGetValue(relativePath, out var approvedHostCandidates);
                var approvedHost = approvedHostCandidates?.SingleOrDefault();
                if (approvedHost != null)
                {
                    usedApprovedHosts.Add(relativePath);
                }
                var title = ResolveTitle(request.ProvisioningPolicy, targetOverride, segment, relativePath, issues);
                var template = string.IsNullOrWhiteSpace(targetOverride?.TargetTemplate)
                    ? request.ProvisioningPolicy.DefaultTargetTemplate
                    : targetOverride.TargetTemplate;
                var configuration = targetOverride?.TargetConfiguration ?? request.ProvisioningPolicy.DefaultTargetConfiguration;
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(template) || configuration < 0)
                {
                    issues.Add(Issue(
                        "TargetWebProvisioningPolicyIncomplete",
                        relativePath,
                        "Every path-derived target Web requires a target-policy title, template, and non-negative configuration."));
                    return Result(null, issues);
                }

                var container = new TargetWebContainerIngredientPlan
                {
                    IngredientId = SharedTopologyIdentity.TargetWebContainer(targetPath),
                    TargetSlotKey = SharedTopologyIdentity.TargetSlot(targetPath),
                    OriginalIdentifier = SharedTopologyIdentity.PathDerivedOriginalIdentifier(request.Source.SourceSiteId, relativePath),
                    ParentIngredientId = parentIngredientId,
                    ParentGlobalActionKey = parentGlobalActionKey,
                    SourceRelativePath = relativePath,
                    SourcePathSegment = segment,
                    PreferredTargetServerRelativeUrl = preferredPath,
                    PreferredTargetWebUrl = SharedTopologyPath.AbsoluteUrl(targetSiteUrl, preferredPath),
                    TargetServerRelativeUrl = targetPath,
                    TargetWebUrl = SharedTopologyPath.AbsoluteUrl(targetSiteUrl, targetPath),
                    TargetParentWebUrl = targetParentUrl,
                    ExpectedTargetSiteId = request.ExpectedTargetSiteId,
                    CollisionResolved = collisionResolved,
                    CollisionResolutionReason = collisionReason,
                    ApprovedExistingTargetWebId = approvedHost?.ExpectedTargetWebId,
                    Provisioning = new TargetWebContainerProvisioningValues
                    {
                        Title = title,
                        TitleSource = string.IsNullOrWhiteSpace(targetOverride?.TargetTitle)
                            ? TargetWebProvisioningValueSource.DerivedFromTargetPathSegment
                            : TargetWebProvisioningValueSource.ExplicitTargetPolicy,
                        Template = template,
                        Configuration = configuration,
                        Language = request.ProvisioningPolicy.DefaultTargetLanguage,
                        UseSamePermissionsAsParentWeb = targetOverride?.UseSamePermissionsAsParentWeb
                            ?? request.ProvisioningPolicy.DefaultUseSamePermissionsAsParentWeb,
                        ExpectedMetadataDifferences = new List<string>
                        {
                            "Source Web Title was not captured; the target title is a reviewed target creation value.",
                            "Source WebTemplate, Configuration, and language were not captured; target values come from the reviewed provisioning policy."
                        }
                    }
                };
                container.ActionSignatureDigest = SharedTopologyDigest.ComputeContainer(container);
                container.IngredientDigest = container.ActionSignatureDigest;
                container.GlobalActionKey = SharedTopologyIdentity.GlobalAction(
                    container.TargetSlotKey,
                    container.ActionSignatureDigest);
                containers.Add(container);
                parentIngredientId = container.IngredientId;
                parentGlobalActionKey = container.GlobalActionKey;
                targetParentPath = targetPath;
                targetParentUrl = container.TargetWebUrl;
                preferredParentPath = preferredPath;
            }

            foreach (var unusedOverride in overrides.Keys.Where(value => !usedOverrides.Contains(value)))
            {
                issues.Add(Issue("PathDerivedTopologyOverrideUnused", unusedOverride, "The target provisioning override does not match a source-relative Web path."));
            }
            foreach (var unusedApprovedHost in approvedHosts.Keys.Where(value => !usedApprovedHosts.Contains(value)))
            {
                issues.Add(Issue("PathDerivedTopologyApprovedHostUnused", unusedApprovedHost, "The approved existing target Web does not match a source-relative Web path."));
            }
            if (issues.Count > 0)
            {
                return Result(null, issues);
            }

            var leaf = containers[containers.Count - 1];
            var plan = new SharedTopologyPlan
            {
                TargetSite = targetSite,
                SourceWebFidelityIngredients = new List<SourceWebFidelityIngredientPlan> { fidelity },
                TargetWebContainers = containers,
                SourceWebBindings = new List<SourceWebTargetContainerBinding>
                {
                    new SourceWebTargetContainerBinding
                    {
                        SourceSiteId = request.Source.SourceSiteId,
                        SourceWebId = request.Source.SourceLeafWebId,
                        SourceWebUrl = request.Source.SourceLeafWebUrl,
                        SourceServerRelativeUrl = request.Source.SourceLeafWebServerRelativeUrl,
                        TargetContainerIngredientId = leaf.IngredientId,
                        TargetGlobalActionKey = leaf.GlobalActionKey,
                        TargetWebUrl = leaf.TargetWebUrl,
                        TargetServerRelativeUrl = leaf.TargetServerRelativeUrl
                    }
                },
                SupportCohortSignature = SharedTopologyIdentity.SupportCohort(containers.Select(value => value.GlobalActionKey))
            };
            plan.PlanDigest = SharedTopologyDigest.ComputePlan(plan);
            SharedTopologyPlanValidator.Validate(plan);
            return Result(plan, issues);
        }

        private static string ResolveTitle(
            PathDerivedTargetWebProvisioningPolicy policy,
            TargetWebProvisioningOverride targetOverride,
            string segment,
            string relativePath,
            ICollection<MigrationIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(targetOverride?.TargetTitle))
            {
                return targetOverride.TargetTitle;
            }
            if (policy.TitlePolicy == TargetWebTitlePolicy.DeriveFromPathSegment)
            {
                return Uri.UnescapeDataString(segment);
            }
            issues.Add(Issue(
                "TargetWebTitlePolicyIncomplete",
                relativePath,
                "The target title policy requires an explicit override for this path."));
            return null;
        }

        private static void ValidatePolicy(PathDerivedTargetWebProvisioningPolicy policy)
        {
            if (policy == null
                || !string.Equals(policy.SchemaVersion, "pnp-path-derived-target-web-policy/v1", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(policy.DefaultTargetTemplate)
                || policy.DefaultTargetConfiguration < 0
                || policy.DefaultTargetLanguage <= 0
                || policy.Overrides == null
                || policy.ApprovedExistingWebs == null)
            {
                throw new System.IO.InvalidDataException("A reviewed path-derived target Web provisioning policy with template, configuration, and language is required.");
            }
        }

        private static string NormalizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            var normalized = SharedTopologyPath.NormalizeServerRelativePath("/" + value.Trim().Trim('/'), nameof(value));
            return normalized.TrimStart('/');
        }

        private static MigrationIssue Issue(string code, string subject, string message)
        {
            return new MigrationIssue
            {
                Code = code,
                Severity = MigrationIssueSeverity.Blocker,
                Subject = subject,
                Ingredient = "Topology.PathDerived",
                Message = message
            };
        }

        private static SharedTopologyPlanBuildResult Result(SharedTopologyPlan plan, IList<MigrationIssue> issues)
        {
            return new SharedTopologyPlanBuildResult
            {
                Plan = plan,
                Issues = issues.OrderBy(value => value.Code, StringComparer.Ordinal)
                    .ThenBy(value => value.Subject, StringComparer.Ordinal)
                    .ToList()
            };
        }
    }
}
