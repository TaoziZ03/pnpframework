using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
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
                ValidatePolicy(request);
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

            var sourceRoot = PathDerivedSourceTopologyEvidenceFactory.Root(request.Source);
            var sourceLeaf = PathDerivedSourceTopologyEvidenceFactory.PrimaryLeaf(request.Source);
            var sourceSegments = SharedTopologyPath.RelativeSegments(sourceRoot.ServerRelativeUrl, sourceLeaf.ServerRelativeUrl);
            if (sourceSegments.Length == 0)
            {
                issues.Add(Issue("PathDerivedTopologyRootUnsupported", sourceLeaf.ServerRelativeUrl, "Path-derived topology requires a leaf Web below the captured Site Collection root."));
                return Result(null, issues);
            }

            var targetSitePath = SharedTopologyPath.NormalizeServerRelativePath(request.TargetSiteServerRelativeUrl, nameof(request.TargetSiteServerRelativeUrl));
            var targetSiteUrl = SharedTopologyPath.NormalizeAbsoluteUrl(request.TargetSiteCollectionUrl, nameof(request.TargetSiteCollectionUrl));
            if (confirmedForeignCollisions.Any(value => !SharedTopologyPath.EqualsPath(value, targetSitePath)
                && !value.StartsWith(targetSitePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Issue("TargetWebCollisionInventoryInvalid", targetSitePath, "Confirmed collision paths must stay inside the mapped target Site Collection."));
                return Result(null, issues);
            }

            var fidelity = CreateFidelityIngredients(request.Source);
            var fidelityByPath = fidelity.ToDictionary(value => value.SourceServerRelativeUrl, StringComparer.OrdinalIgnoreCase);
            var rootTargetSlot = SharedTopologyIdentity.TargetSlot(targetSiteUrl, request.ExpectedTargetSiteId, targetSitePath, targetSitePath);
            var targetSite = new TargetSiteCollectionIngredientPlan
            {
                IngredientId = SharedTopologyIdentity.TargetSite(rootTargetSlot),
                TargetSiteCollectionUrl = targetSiteUrl,
                TargetServerRelativeUrl = targetSitePath,
                ExpectedTargetSiteId = request.ExpectedTargetSiteId,
                ExpectedTargetRootWebId = request.ExpectedTargetRootWebId
            };

            var overrides = GroupOverrides(request.ProvisioningPolicy.Overrides, issues);
            var approvedHosts = GroupApprovedHosts(request.ProvisioningPolicy.ApprovedExistingWebs, issues);
            if (issues.Count > 0)
            {
                return Result(null, issues);
            }

            var rootFidelity = fidelityByPath[sourceRoot.ServerRelativeUrl];
            var root = new TargetWebContainerIngredientPlan
            {
                IsTargetSiteRoot = true,
                SourceOwnerKey = rootFidelity.SourceOwnerKey,
                TargetSlotKey = rootTargetSlot,
                OriginalIdentifier = TopologyPlanner.WebOriginalIdentifier(request.Source.SourceSiteId, sourceRoot.WebId),
                ExpectedOwnership = SharedTopologyOwnership.ExternalApprovedHost,
                IdentityBasis = SharedTopologyIdentityBasis.TargetSiteRoot,
                ParentIngredientId = targetSite.IngredientId,
                SourceRelativePath = string.Empty,
                SourcePathSegment = string.Empty,
                PreferredTargetWebUrl = targetSiteUrl,
                PreferredTargetServerRelativeUrl = targetSitePath,
                TargetWebUrl = targetSiteUrl,
                TargetServerRelativeUrl = targetSitePath,
                ExpectedTargetSiteId = request.ExpectedTargetSiteId,
                ApprovedExistingTargetWebId = request.ExpectedTargetRootWebId,
                Provisioning = new TargetWebContainerProvisioningValues
                {
                    Title = request.TargetRootTitle,
                    TitleSource = TargetWebProvisioningValueSource.FreshTargetRootProbe,
                    Template = request.TargetRootTemplate,
                    TemplateSource = TargetWebProvisioningValueSource.FreshTargetRootProbe,
                    Configuration = request.TargetRootConfiguration,
                    ConfigurationSource = TargetWebProvisioningValueSource.FreshTargetRootProbe,
                    Language = request.TargetRootLanguage,
                    LanguageSource = TargetWebProvisioningValueSource.FreshTargetRootProbe,
                    UseSamePermissionsAsParentWeb = !request.TargetRootHasUniqueRoleAssignments,
                    PermissionsSource = TargetWebProvisioningValueSource.FreshTargetRootProbe
                }
            };
            SealAction(root, rootFidelity, null);
            var containers = new List<TargetWebContainerIngredientPlan> { root };

            var foreignCollisions = new HashSet<string>(confirmedForeignCollisions, StringComparer.OrdinalIgnoreCase);
            var preferredParentPath = targetSitePath;
            var targetParentPath = targetSitePath;
            var targetParentUrl = targetSiteUrl;
            var parent = root;
            var sourceCurrentPath = sourceRoot.ServerRelativeUrl;
            var sourceRelativeSegments = new List<string>();
            var usedOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedApprovedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in sourceSegments)
            {
                sourceCurrentPath = SharedTopologyPath.Combine(sourceCurrentPath, segment);
                sourceRelativeSegments.Add(segment);
                var relativePath = string.Join("/", sourceRelativeSegments);
                var preferredPath = SharedTopologyPath.Combine(preferredParentPath, segment);
                var targetPath = SharedTopologyPath.Combine(targetParentPath, segment);
                var collisionResolved = !SharedTopologyPath.EqualsPath(preferredParentPath, targetParentPath);
                string collisionReason = collisionResolved
                    ? "An ancestor used the reviewed stable-suffix collision policy; this segment remains unchanged."
                    : null;
                if (foreignCollisions.Contains(targetPath))
                {
                    if (request.ProvisioningPolicy.CollisionPolicy != TargetWebCollisionPolicy.StableSuffix)
                    {
                        issues.Add(Issue("TargetWebPathCollision", targetPath, "The exact path is a confirmed foreign collision and no reviewed suffix action is allowed."));
                        return Result(null, issues);
                    }
                    targetPath = SharedTopologyPath.AllocateCollisionSuffix(targetPath, fidelityByPath[sourceCurrentPath].SourceOwnerKey, foreignCollisions);
                    collisionResolved = true;
                    collisionReason = "The reviewed stable-suffix policy changed only the confirmed foreign-collision segment.";
                }

                overrides.TryGetValue(relativePath, out var overrideCandidates);
                var targetOverride = overrideCandidates?.SingleOrDefault();
                if (targetOverride != null)
                {
                    usedOverrides.Add(relativePath);
                }
                approvedHosts.TryGetValue(relativePath, out var hostCandidates);
                var approvedHost = hostCandidates?.SingleOrDefault();
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
                    issues.Add(Issue("TargetWebProvisioningPolicyIncomplete", relativePath, "Every path-derived target Web requires a target-policy title, template, and non-negative configuration."));
                    return Result(null, issues);
                }

                var sourceFidelity = fidelityByPath[sourceCurrentPath];
                var expectedOwnership = approvedHost == null
                    ? SharedTopologyOwnership.MigrationOwned
                    : SharedTopologyOwnership.ExternalApprovedHost;
                var container = new TargetWebContainerIngredientPlan
                {
                    SourceOwnerKey = sourceFidelity.SourceOwnerKey,
                    TargetSlotKey = SharedTopologyIdentity.TargetSlot(targetSiteUrl, request.ExpectedTargetSiteId, targetSitePath, targetPath),
                    OriginalIdentifier = sourceFidelity.SourceWebId == Guid.Empty
                        ? SharedTopologyIdentity.PathDerivedOriginalIdentifier(request.Source.SourceSiteId, relativePath)
                        : TopologyPlanner.WebOriginalIdentifier(request.Source.SourceSiteId, sourceFidelity.SourceWebId),
                    ExpectedOwnership = expectedOwnership,
                    IdentityBasis = SharedTopologyIdentityBasis.ExactRelativePath,
                    ParentIngredientId = parent.IngredientId,
                    ParentLogicalActionKey = parent.LogicalActionKey,
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
                            sourceFidelity.State == SourceWebFidelityState.AuthorizationBlocked
                                ? "Source ancestor Web metadata was authorization-blocked; all target creation values are reviewed target policy."
                                : "Target creation values are explicitly reviewed and may differ from captured source Web metadata."
                        }
                    }
                };
                SealAction(container, sourceFidelity, parent.LogicalActionDigest);
                containers.Add(container);
                parent = container;
                targetParentPath = targetPath;
                targetParentUrl = container.TargetWebUrl;
                preferredParentPath = preferredPath;
            }

            AddUnusedPolicyIssues(overrides.Keys, usedOverrides, "PathDerivedTopologyOverrideUnused", issues);
            AddUnusedPolicyIssues(approvedHosts.Keys, usedApprovedHosts, "PathDerivedTopologyApprovedHostUnused", issues);
            if (issues.Count > 0)
            {
                return Result(null, issues);
            }

            var containerBySourceOwner = containers.ToDictionary(value => value.SourceOwnerKey, StringComparer.Ordinal);
            var bindings = fidelity.Select(value =>
            {
                var target = containerBySourceOwner[value.SourceOwnerKey];
                return new SourceWebTargetContainerBinding
                {
                    SourceOwnerKey = value.SourceOwnerKey,
                    SourceSiteId = value.SourceSiteId,
                    SourceWebId = value.SourceWebId,
                    SourceWebUrl = value.SourceWebUrl,
                    SourceServerRelativeUrl = value.SourceServerRelativeUrl,
                    TargetContainerIngredientId = target.IngredientId,
                    TargetLogicalActionKey = target.LogicalActionKey,
                    TargetWebUrl = target.TargetWebUrl,
                    TargetServerRelativeUrl = target.TargetServerRelativeUrl
                };
            }).ToList();
            var plan = new SharedTopologyPlan
            {
                TargetSite = targetSite,
                SourceWebFidelityIngredients = fidelity,
                TargetWebContainers = containers,
                SourceWebBindings = bindings,
                ExecutionGroupDigest = SharedTopologyIdentity.ExecutionGroup(containers.Select(value => value.LogicalActionKey))
            };
            plan.SupportCohortDigest = SharedTopologyDigest.ComputeSupportCohort(plan);
            plan.PlanDigest = SharedTopologyDigest.ComputePlan(plan);
            SharedTopologyPlanValidator.Validate(plan);
            return Result(plan, issues);
        }

        private static IList<SourceWebFidelityIngredientPlan> CreateFidelityIngredients(PathDerivedSourceTopologyEvidence evidence)
        {
            var result = new List<SourceWebFidelityIngredientPlan>();
            foreach (var captured in evidence.CapturedWebs)
            {
                result.Add(CreateCapturedFidelity(evidence, captured));
            }
            var root = PathDerivedSourceTopologyEvidenceFactory.Root(evidence);
            foreach (var path in evidence.UnknownAncestorPaths)
            {
                var ownerKey = SharedTopologyIdentity.SourceOwner(root.SiteCollectionUrl, evidence.SourceSiteId, path);
                result.Add(new SourceWebFidelityIngredientPlan
                {
                    IngredientId = SharedTopologyIdentity.SourcePathFidelity(ownerKey),
                    SourceOwnerKey = ownerKey,
                    IdentityBasis = SharedTopologyIdentityBasis.ExactRelativePath,
                    SourceSiteId = evidence.SourceSiteId,
                    SourceWebUrl = SharedTopologyPath.AbsoluteUrl(root.SiteCollectionUrl, path),
                    SourceServerRelativeUrl = path,
                    State = SourceWebFidelityState.AuthorizationBlocked,
                    AuthorizationEvidence = evidence.AncestorAuthorizationEvidence,
                    AuthorizationOperation = evidence.AncestorReadOperation,
                    AuthorizationRequestUri = evidence.AncestorReadRequestUri,
                    EvidenceSha256 = SharedTopologyDigest.ComputeFidelityEvidence(evidence, ownerKey, SourceWebFidelityState.AuthorizationBlocked)
                });
            }
            return result.OrderBy(value => SharedTopologyPath.Depth(value.SourceServerRelativeUrl)).ToList();
        }

        private static SourceWebFidelityIngredientPlan CreateCapturedFidelity(
            PathDerivedSourceTopologyEvidence evidence,
            SourceWebSnapshot web)
        {
            var ownerKey = SharedTopologyIdentity.SourceOwner(web.SiteCollectionUrl, web.SiteId, web.ServerRelativeUrl);
            return new SourceWebFidelityIngredientPlan
            {
                IngredientId = SharedTopologyIdentity.SourceWebFidelity(web.SiteId, web.WebId),
                SourceOwnerKey = ownerKey,
                IdentityBasis = SharedTopologyIdentityBasis.CapturedSourceWeb,
                SourceSiteId = web.SiteId,
                SourceWebId = web.WebId,
                SourceWebUrl = web.WebUrl,
                SourceServerRelativeUrl = web.ServerRelativeUrl,
                State = SourceWebFidelityState.Captured,
                EvidenceSha256 = SharedTopologyDigest.ComputeFidelityEvidence(evidence, ownerKey, SourceWebFidelityState.Captured)
            };
        }

        private static void SealAction(
            TargetWebContainerIngredientPlan container,
            SourceWebFidelityIngredientPlan fidelity,
            string parentLogicalActionDigest)
        {
            container.IngredientId = SharedTopologyIdentity.TargetWebContainer(container.TargetSlotKey);
            container.SemanticMappingDigest = SharedTopologyDigest.ComputeContainerMapping(container);
            container.LogicalActionDigest = SharedTopologyDigest.ComputeLogicalAction(container);
            container.LogicalActionKey = SharedTopologyIdentity.LogicalAction(container.LogicalActionDigest);
            var selectionDigest = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-shared-topology-action-selection/v2",
                container.ExpectedOwnership,
                container.CollisionResolved,
                container.CollisionResolutionReason,
                container.ApprovedExistingTargetWebId,
                container.SemanticMappingDigest
            }));
            var grant = MigrationActionSignature.Create(
                "topology.target-web." + SharedTopologyIdentity.StableDigest(container.LogicalActionKey),
                container.IsTargetSiteRoot ? "Topology.TargetSiteRoot" : "Topology.ChildWeb",
                fidelity.EvidenceSha256,
                selectionDigest,
                container.TargetSlotKey,
                SharedTopologyDigest.ComputeObservedSemanticState(container),
                parentLogicalActionDigest == null ? null : new[] { parentLogicalActionDigest });
            container.ExecutionGrants = new List<MigrationActionSignature> { grant };
        }

        private static IDictionary<string, TargetWebProvisioningOverride[]> GroupOverrides(
            IEnumerable<TargetWebProvisioningOverride> values,
            ICollection<MigrationIssue> issues)
        {
            var result = (values ?? Enumerable.Empty<TargetWebProvisioningOverride>())
                .GroupBy(value => NormalizeRelativePath(value?.SourceRelativePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (var duplicate in result.Where(value => string.IsNullOrWhiteSpace(value.Key) || value.Value.Length != 1))
            {
                issues.Add(Issue("PathDerivedTopologyOverrideInvalid", duplicate.Key ?? "target-web-override", "Each source-relative path may have exactly one target provisioning override."));
            }
            return result;
        }

        private static IDictionary<string, TargetWebApprovedHost[]> GroupApprovedHosts(
            IEnumerable<TargetWebApprovedHost> values,
            ICollection<MigrationIssue> issues)
        {
            var result = (values ?? Enumerable.Empty<TargetWebApprovedHost>())
                .GroupBy(value => NormalizeRelativePath(value?.SourceRelativePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (var duplicate in result.Where(value => string.IsNullOrWhiteSpace(value.Key)
                || value.Value.Length != 1
                || value.Value[0].ExpectedTargetWebId == Guid.Empty))
            {
                issues.Add(Issue("PathDerivedTopologyApprovedHostInvalid", duplicate.Key ?? "approved-target-web", "Each approved existing target Web path requires exactly one non-empty expected Web ID."));
            }
            return result;
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
                return segment;
            }
            issues.Add(Issue("TargetWebTitlePolicyIncomplete", relativePath, "The target title policy requires an explicit override for this path."));
            return null;
        }

        private static void ValidatePolicy(PathDerivedTopologyPlanningRequest request)
        {
            var policy = request.ProvisioningPolicy;
            if (policy == null
                || !string.Equals(policy.SchemaVersion, "pnp-path-derived-target-web-policy/v2", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(policy.DefaultTargetTemplate)
                || policy.DefaultTargetConfiguration < 0
                || policy.DefaultTargetLanguage <= 0
                || policy.Overrides == null
                || policy.ApprovedExistingWebs == null
                || request.ExpectedTargetSiteId == Guid.Empty
                || request.ExpectedTargetRootWebId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.TargetRootTitle)
                || string.IsNullOrWhiteSpace(request.TargetRootTemplate)
                || request.TargetRootConfiguration < 0
                || request.TargetRootLanguage <= 0)
            {
                throw new System.IO.InvalidDataException("A reviewed v2 target Site/root fence and child-Web provisioning policy are required.");
            }
        }

        private static string NormalizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            return SharedTopologyPath.NormalizeServerRelativePath("/" + value.Trim().Trim('/'), nameof(value)).TrimStart('/');
        }

        private static void AddUnusedPolicyIssues(
            IEnumerable<string> candidates,
            ISet<string> used,
            string code,
            ICollection<MigrationIssue> issues)
        {
            foreach (var candidate in candidates.Where(value => !used.Contains(value)))
            {
                issues.Add(Issue(code, candidate, "The target policy entry does not match a source-relative Web path."));
            }
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
