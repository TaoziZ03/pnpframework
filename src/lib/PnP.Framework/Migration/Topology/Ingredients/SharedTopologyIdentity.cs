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
        public static string SourceWebFidelity(Guid siteId, Guid webId)
        {
            if (siteId == Guid.Empty || webId == Guid.Empty)
            {
                throw new ArgumentException("Source Site and Web IDs are required.");
            }
            return "topology:source-web-fidelity:" + siteId.ToString("D") + "/" + webId.ToString("D");
        }

        public static string TargetSite(string targetServerRelativeUrl)
        {
            return "topology:target-site:" + CanonicalPath(targetServerRelativeUrl);
        }

        public static string TargetWebContainer(string targetServerRelativeUrl)
        {
            return "topology:target-web-container:" + CanonicalPath(targetServerRelativeUrl);
        }

        public static string TargetSlot(string targetServerRelativeUrl)
        {
            return "topology:target-web-slot:" + CanonicalPath(targetServerRelativeUrl);
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
            return "urn:pnp:spo-path-web:v1:" + sourceSiteId.ToString("N") + ":" + relative.TrimStart('/').ToLowerInvariant();
        }

        public static string GlobalAction(string targetSlotKey, string actionSignatureDigest)
        {
            if (string.IsNullOrWhiteSpace(targetSlotKey) || string.IsNullOrWhiteSpace(actionSignatureDigest))
            {
                throw new ArgumentException("A target slot key and action signature digest are required.");
            }
            return "topology:global-action:" + StableDigest(targetSlotKey + "\n" + actionSignatureDigest);
        }

        public static string SupportCohort(IEnumerable<string> globalActionKeys)
        {
            var values = (globalActionKeys ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (values.Length == 0)
            {
                throw new ArgumentException("At least one global action key is required.", nameof(globalActionKeys));
            }
            return "topology:support-cohort:" + StableDigest(string.Join("\n", values));
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
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            var canonical = new SharedTopologyPlan
            {
                SchemaVersion = plan.SchemaVersion,
                TargetSite = plan.TargetSite,
                SourceWebFidelityIngredients = plan.SourceWebFidelityIngredients,
                TargetWebContainers = plan.TargetWebContainers,
                SourceWebBindings = plan.SourceWebBindings,
                SupportCohortSignature = plan.SupportCohortSignature,
                PlanDigest = null
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(canonical));
        }

        public static string ComputeContainer(TargetWebContainerIngredientPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            var canonical = new TargetWebContainerIngredientPlan
            {
                IngredientId = plan.IngredientId,
                TargetSlotKey = plan.TargetSlotKey,
                ActionSignatureDigest = null,
                GlobalActionKey = null,
                OriginalIdentifier = plan.OriginalIdentifier,
                IdentityBasis = plan.IdentityBasis,
                ParentIngredientId = plan.ParentIngredientId,
                ParentGlobalActionKey = plan.ParentGlobalActionKey,
                SourceRelativePath = plan.SourceRelativePath,
                SourcePathSegment = plan.SourcePathSegment,
                PreferredTargetWebUrl = plan.PreferredTargetWebUrl,
                PreferredTargetServerRelativeUrl = plan.PreferredTargetServerRelativeUrl,
                TargetWebUrl = plan.TargetWebUrl,
                TargetServerRelativeUrl = plan.TargetServerRelativeUrl,
                TargetParentWebUrl = plan.TargetParentWebUrl,
                ExpectedTargetSiteId = plan.ExpectedTargetSiteId,
                CollisionResolved = plan.CollisionResolved,
                CollisionResolutionReason = plan.CollisionResolutionReason,
                ApprovedExistingTargetWebId = plan.ApprovedExistingTargetWebId,
                Provisioning = plan.Provisioning,
                IngredientDigest = null
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(canonical));
        }

        public static string ComputeEvidence(PathDerivedSourceTopologyEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }
            var canonical = new PathDerivedSourceTopologyEvidence
            {
                SchemaVersion = evidence.SchemaVersion,
                SourceSiteId = evidence.SourceSiteId,
                SourceSiteCollectionUrl = evidence.SourceSiteCollectionUrl,
                SourceSiteServerRelativeUrl = evidence.SourceSiteServerRelativeUrl,
                SourceLeafWebId = evidence.SourceLeafWebId,
                SourceLeafWebUrl = evidence.SourceLeafWebUrl,
                SourceLeafWebServerRelativeUrl = evidence.SourceLeafWebServerRelativeUrl,
                FidelityState = evidence.FidelityState,
                AuthorizationEvidence = evidence.AuthorizationEvidence,
                Diagnostics = evidence.Diagnostics,
                EvidenceSha256 = null
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(canonical));
        }

    }
}
