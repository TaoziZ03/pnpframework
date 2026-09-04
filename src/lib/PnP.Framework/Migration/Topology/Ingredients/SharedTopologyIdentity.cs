using PnP.Framework.Migration.Packaging;
using System;
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
                IdentityBasis = plan.IdentityBasis,
                ParentIngredientId = plan.ParentIngredientId,
                SourceRelativePath = plan.SourceRelativePath,
                SourcePathSegment = plan.SourcePathSegment,
                PreferredTargetWebUrl = plan.PreferredTargetWebUrl,
                PreferredTargetServerRelativeUrl = plan.PreferredTargetServerRelativeUrl,
                TargetWebUrl = plan.TargetWebUrl,
                TargetServerRelativeUrl = plan.TargetServerRelativeUrl,
                TargetParentWebUrl = plan.TargetParentWebUrl,
                CollisionResolved = plan.CollisionResolved,
                CollisionResolutionReason = plan.CollisionResolutionReason,
                AllowReuseExistingExactPath = plan.AllowReuseExistingExactPath,
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

        public static string ComputeHttpFailure(TopologyHttpFailureEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }
            var canonical = string.Join("\n", new[]
            {
                evidence.Operation?.Trim() ?? string.Empty,
                SharedTopologyPath.NormalizeAbsoluteUrl(evidence.RequestUri, nameof(evidence.RequestUri)),
                evidence.HttpStatusCode.ToString(CultureInfo.InvariantCulture),
                evidence.ObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            }) + "\n";
            return MigrationDigest.ComputeSha256(canonical);
        }
    }
}
