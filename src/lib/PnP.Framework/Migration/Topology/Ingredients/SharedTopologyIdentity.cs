using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyIdentity
    {
        private const string LogicalActionPrefix = "topology:logical-action:v1:";

        public static string SourceOwner(
            string sourceSiteCollectionUrl,
            Guid sourceSiteId,
            string sourceServerRelativeUrl)
        {
            if (sourceSiteId == Guid.Empty)
            {
                throw new ArgumentException("A source Site identity is required.", nameof(sourceSiteId));
            }
            var site = new Uri(SharedTopologyPath.NormalizeAbsoluteUrl(sourceSiteCollectionUrl, nameof(sourceSiteCollectionUrl)));
            return "topology:source-owner:v2:"
                + site.Authority.ToLowerInvariant() + ":"
                + sourceSiteId.ToString("N") + ":"
                + CanonicalPath(sourceServerRelativeUrl);
        }

        public static string SourceWebFidelity(Guid siteId, Guid webId)
        {
            if (siteId == Guid.Empty || webId == Guid.Empty)
            {
                throw new ArgumentException("Source Site and Web IDs are required.");
            }
            return "topology:source-web-fidelity:v2:" + siteId.ToString("D") + "/" + webId.ToString("D");
        }

        public static string SourcePathFidelity(string sourceOwnerKey)
        {
            if (string.IsNullOrWhiteSpace(sourceOwnerKey))
            {
                throw new ArgumentException("A source owner key is required.", nameof(sourceOwnerKey));
            }
            return "topology:source-path-fidelity:v2:" + StableDigest(sourceOwnerKey);
        }

        public static string TargetSite(string targetSlotKey)
        {
            return "topology:target-site:v2:" + StableDigest(targetSlotKey);
        }

        public static string TargetWebContainer(string targetSlotKey)
        {
            return "topology:target-web-container:v2:" + StableDigest(targetSlotKey);
        }

        public static string TargetSlot(
            string targetSiteCollectionUrl,
            Guid expectedTargetSiteId,
            string targetSiteServerRelativeUrl,
            string targetServerRelativeUrl)
        {
            if (expectedTargetSiteId == Guid.Empty)
            {
                throw new ArgumentException("A target Site fence is required.", nameof(expectedTargetSiteId));
            }
            var site = new Uri(SharedTopologyPath.NormalizeAbsoluteUrl(targetSiteCollectionUrl, nameof(targetSiteCollectionUrl)));
            var sitePath = CanonicalPath(targetSiteServerRelativeUrl);
            var targetPath = CanonicalPath(targetServerRelativeUrl);
            if (!string.Equals(targetPath, sitePath, StringComparison.Ordinal)
                && !targetPath.StartsWith(sitePath.TrimEnd('/') + "/", StringComparison.Ordinal))
            {
                throw new ArgumentException("The target slot is outside its target Site fence.", nameof(targetServerRelativeUrl));
            }
            return "topology:target-web-slot:v2:"
                + site.Authority.ToLowerInvariant() + ":"
                + expectedTargetSiteId.ToString("N") + ":"
                + sitePath + ":" + targetPath;
        }

        public static string PathDerivedOriginalIdentifier(Guid sourceSiteId, string sourceRelativePath)
        {
            if (sourceSiteId == Guid.Empty)
            {
                throw new ArgumentException("A source Site identity is required.", nameof(sourceSiteId));
            }
            var relative = SharedTopologyPath.NormalizeServerRelativePath(
                "/" + (sourceRelativePath ?? string.Empty).Trim('/'),
                nameof(sourceRelativePath));
            return "urn:pnp:spo-path-web:v2:" + sourceSiteId.ToString("N") + ":" + relative.TrimStart('/').ToLowerInvariant();
        }

        public static string LogicalAction(string logicalActionDigest)
        {
            if (!MigrationActionSignature.IsSha256(logicalActionDigest))
            {
                throw new ArgumentException("A logical action SHA-256 digest is required.", nameof(logicalActionDigest));
            }
            return LogicalActionPrefix + logicalActionDigest.ToLowerInvariant();
        }

        public static string LogicalActionDigest(string logicalActionKey)
        {
            if (string.IsNullOrWhiteSpace(logicalActionKey)
                || !logicalActionKey.StartsWith(LogicalActionPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException("A canonical logical action key is required.", nameof(logicalActionKey));
            }
            var digest = logicalActionKey.Substring(LogicalActionPrefix.Length);
            if (!MigrationActionSignature.IsSha256(digest))
            {
                throw new ArgumentException("A canonical logical action key is required.", nameof(logicalActionKey));
            }
            return digest;
        }

        public static string ExecutionGroup(IEnumerable<string> logicalActionKeys)
        {
            var values = (logicalActionKeys ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (values.Length == 0)
            {
                throw new ArgumentException("At least one logical action key is required.", nameof(logicalActionKeys));
            }
            return StableDigest(string.Join("\n", values));
        }

        internal static string CanonicalPath(string value)
        {
            return SharedTopologyPath.NormalizeServerRelativePath(value, nameof(value)).ToLowerInvariant();
        }

        internal static string StableDigest(string sourceIdentity)
        {
            if (string.IsNullOrWhiteSpace(sourceIdentity))
            {
                throw new ArgumentException("A stable source identity is required.", nameof(sourceIdentity));
            }
            using (var algorithm = SHA256.Create())
            {
                return string.Concat(algorithm.ComputeHash(Encoding.UTF8.GetBytes(sourceIdentity))
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }
    }

    internal static class SharedTopologyDigest
    {
        public static string ComputePlan(SharedTopologyPlan plan)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    plan ?? throw new ArgumentNullException(nameof(plan)),
                    nameof(SharedTopologyPlan.PlanDigest)));
        }

        public static string ComputeContainerMapping(TargetWebContainerIngredientPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            var canonical = new
            {
                schemaVersion = "pnp-shared-topology-container-mapping/v2",
                plan.IsTargetSiteRoot,
                plan.SourceOwnerKey,
                plan.TargetSlotKey,
                plan.OriginalIdentifier,
                plan.ExpectedOwnership,
                plan.ParentLogicalActionKey,
                plan.SourceRelativePath,
                plan.SourcePathSegment,
                plan.PreferredTargetWebUrl,
                plan.PreferredTargetServerRelativeUrl,
                plan.TargetWebUrl,
                plan.TargetServerRelativeUrl,
                plan.TargetParentWebUrl,
                plan.ExpectedTargetSiteId,
                plan.CollisionResolved,
                plan.CollisionResolutionReason,
                plan.ApprovedExistingTargetWebId,
                plan.Provisioning
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(canonical));
        }

        public static string ComputeObservedSemanticState(TargetWebContainerIngredientPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            var external = plan.ExpectedOwnership == SharedTopologyOwnership.ExternalApprovedHost;
            var templateParts = (plan.Provisioning.Template ?? string.Empty).Split('#');
            var template = templateParts[0];
            var configuration = templateParts.Length > 1
                && int.TryParse(templateParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedConfiguration)
                    ? parsedConfiguration
                    : plan.Provisioning.Configuration;
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-shared-topology-observed-web/v2",
                plan.TargetSlotKey,
                plan.TargetWebUrl,
                plan.TargetServerRelativeUrl,
                plan.ExpectedTargetSiteId,
                expectedTargetWebId = external ? plan.ApprovedExistingTargetWebId : null,
                plan.Provisioning.Title,
                template,
                configuration,
                plan.Provisioning.Language,
                plan.Provisioning.UseSamePermissionsAsParentWeb,
                ownership = external ? MigrationTargetOwnership.External : MigrationTargetOwnership.MigrationOwned,
                originalIdentifier = external ? null : plan.OriginalIdentifier,
                mappingDigest = external ? null : plan.SemanticMappingDigest
            }));
        }

        public static string ComputeObservedSemanticState(
            TargetWebContainerIngredientPlan plan,
            PathDerivedTargetWebObservation observation,
            SharedTopologyOwnership ownership)
        {
            if (plan == null || observation == null)
            {
                throw new ArgumentNullException(plan == null ? nameof(plan) : nameof(observation));
            }
            var external = ownership == SharedTopologyOwnership.ExternalApprovedHost;
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-shared-topology-observed-web/v2",
                plan.TargetSlotKey,
                targetWebUrl = observation.TargetWebUrl,
                targetServerRelativeUrl = observation.TargetServerRelativeUrl,
                expectedTargetSiteId = observation.TargetSiteId,
                expectedTargetWebId = external ? observation.TargetWebId : null,
                title = observation.ExistingTitle,
                template = observation.ExistingTemplate,
                configuration = observation.ExistingConfiguration,
                language = observation.ExistingLanguage,
                useSamePermissionsAsParentWeb = observation.ExistingHasUniqueRoleAssignments.HasValue
                    ? !observation.ExistingHasUniqueRoleAssignments.Value
                    : (bool?)null,
                ownership = external ? MigrationTargetOwnership.External : MigrationTargetOwnership.MigrationOwned,
                originalIdentifier = external ? null : observation.ExistingOriginalIdentifier,
                mappingDigest = external ? null : observation.ExistingMappingDigest
            }));
        }

        public static string ComputeLogicalAction(TargetWebContainerIngredientPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-shared-topology-logical-action/v1",
                plan.IsTargetSiteRoot,
                plan.TargetSlotKey,
                plan.ParentLogicalActionKey,
                plan.ExpectedOwnership,
                semanticStateDigest = ComputeObservedSemanticState(plan)
            }));
        }

        public static string ComputeEvidence(PathDerivedSourceTopologyEvidence evidence)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    evidence ?? throw new ArgumentNullException(nameof(evidence)),
                    nameof(PathDerivedSourceTopologyEvidence.EvidenceSha256)));
        }

        public static string ComputeFidelityEvidence(
            PathDerivedSourceTopologyEvidence evidence,
            string sourceOwnerKey,
            SourceWebFidelityState state)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-shared-topology-source-fidelity/v2",
                evidenceDigest = evidence.EvidenceSha256,
                sourceOwnerKey,
                state
            }));
        }

        public static string ComputeSupportCohort(SharedTopologyPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            var fidelity = plan.SourceWebFidelityIngredients
                .OrderBy(value => SharedTopologyPath.Depth(value.SourceServerRelativeUrl))
                .Select(value => new
                {
                    value.IdentityBasis,
                    value.State,
                    hasGuid = value.SourceWebId != Guid.Empty
                });
            var actions = plan.TargetWebContainers
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                .Select(value => new
                {
                    value.IsTargetSiteRoot,
                    value.ExpectedOwnership,
                    value.Provisioning.TitleSource,
                    value.Provisioning.Template,
                    value.Provisioning.Configuration,
                    value.Provisioning.Language,
                    value.Provisioning.UseSamePermissionsAsParentWeb,
                    value.CollisionResolved,
                    approvedExternalHost = value.ApprovedExistingTargetWebId.HasValue
                });
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-shared-topology-support-cohort/v1",
                fidelity,
                actions
            }));
        }
    }
}
