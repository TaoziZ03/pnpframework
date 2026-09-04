using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class PathDerivedSourceTopologyEvidenceFactory
    {
        public const string SourceAncestorReadActionId = "topology.source.read-ancestor-closure";

        public static PathDerivedSourceTopologyEvidence CreateAuthorizationBlocked(
            SourceWebSnapshot sourceRootWeb,
            SourceWebSnapshot sourceLeafWeb,
            string expectedOperation,
            string expectedRequestUri,
            LiteralHttpAuthorizationEvidence literalEvidence,
            IEnumerable<string> diagnostics = null)
        {
            if (sourceRootWeb == null || sourceLeafWeb == null)
            {
                throw new ArgumentNullException(nameof(sourceRootWeb));
            }
            LiteralHttpAuthorizationEvidence.Validate(literalEvidence);
            var evidence = new PathDerivedSourceTopologyEvidence
            {
                SourceSiteId = sourceRootWeb.SiteId,
                SourceRootWeb = Clone(sourceRootWeb),
                SourceLeafWeb = Clone(sourceLeafWeb),
                AncestorAuthorizationEvidence = BoundLiteralHttpAuthorizationEvidence.Create(
                    SourceAncestorReadActionId,
                    expectedOperation,
                    expectedRequestUri,
                    literalEvidence),
                UnknownAncestorServerRelativeUrls = UnknownAncestors(
                    sourceRootWeb.ServerRelativeUrl,
                    sourceLeafWeb.ServerRelativeUrl),
                Diagnostics = (diagnostics ?? Enumerable.Empty<string>()).ToList()
            };
            evidence.EvidenceSha256 = SharedTopologyDigest.ComputeEvidence(evidence);
            Validate(evidence);
            return evidence;
        }

        public static void Validate(PathDerivedSourceTopologyEvidence evidence)
        {
            if (evidence == null
                || !string.Equals(evidence.SchemaVersion, PathDerivedSourceTopologyEvidence.CurrentSchemaVersion, StringComparison.Ordinal)
                || evidence.SourceSiteId == Guid.Empty
                || evidence.SourceRootWeb == null
                || evidence.SourceLeafWeb == null
                || evidence.SourceRootWeb.SiteId != evidence.SourceSiteId
                || evidence.SourceLeafWeb.SiteId != evidence.SourceSiteId
                || evidence.SourceRootWeb.WebId == Guid.Empty
                || evidence.SourceLeafWeb.WebId == Guid.Empty
                || evidence.SourceRootWeb.WebId == evidence.SourceLeafWeb.WebId
                || evidence.UnknownAncestorServerRelativeUrls == null
                || evidence.Diagnostics == null)
            {
                throw new InvalidDataException("Path-derived source topology requires distinct captured root/leaf Web identities and a versioned evidence envelope.");
            }
            ValidateCapturedWeb(evidence.SourceRootWeb, "root");
            ValidateCapturedWeb(evidence.SourceLeafWeb, "leaf");
            var rootSite = SharedTopologyPath.NormalizeAbsoluteUrl(
                evidence.SourceRootWeb.SiteCollectionUrl,
                nameof(evidence.SourceRootWeb.SiteCollectionUrl));
            if (!SharedTopologyPath.EqualsUrl(rootSite, evidence.SourceRootWeb.WebUrl)
                || !SharedTopologyPath.EqualsUrl(rootSite, evidence.SourceLeafWeb.SiteCollectionUrl))
            {
                throw new InvalidDataException("The captured root must identify the Site Collection root and the leaf must share that Site Collection.");
            }
            var expectedUnknown = UnknownAncestors(
                evidence.SourceRootWeb.ServerRelativeUrl,
                evidence.SourceLeafWeb.ServerRelativeUrl);
            if (!expectedUnknown.SequenceEqual(evidence.UnknownAncestorServerRelativeUrls, StringComparer.Ordinal))
            {
                throw new InvalidDataException("The unknown ancestor fidelity list does not match the exact root-to-leaf path.");
            }
            BoundLiteralHttpAuthorizationEvidence.Validate(
                evidence.AncestorAuthorizationEvidence,
                SourceAncestorReadActionId);
            if (!PnP.Framework.Migration.Execution.MigrationActionSignature.IsSha256(evidence.EvidenceSha256)
                || !string.Equals(evidence.EvidenceSha256, SharedTopologyDigest.ComputeEvidence(evidence), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The path-derived source topology evidence digest does not match its content.");
            }
        }

        public static string ComputeDigest(PathDerivedSourceTopologyEvidence evidence)
        {
            return SharedTopologyDigest.ComputeEvidence(evidence);
        }

        private static void ValidateCapturedWeb(SourceWebSnapshot web, string role)
        {
            SharedTopologyPath.ValidateUrlMatchesPath(web.WebUrl, web.ServerRelativeUrl, role + "WebUrl");
            if (web.Availability != EvidenceAvailability.Captured
                || string.IsNullOrWhiteSpace(web.Title)
                || string.IsNullOrWhiteSpace(web.WebTemplate)
                || web.Configuration < 0)
            {
                throw new InvalidDataException("The source " + role + " Web requires captured identity, path, title, and template evidence.");
            }
        }

        private static IList<string> UnknownAncestors(string rootPath, string leafPath)
        {
            var segments = SharedTopologyPath.RelativeSegments(rootPath, leafPath);
            var result = new List<string>();
            var current = SharedTopologyPath.NormalizeServerRelativePath(rootPath, nameof(rootPath));
            for (var index = 0; index < segments.Length - 1; index++)
            {
                current = SharedTopologyPath.Combine(current, segments[index]);
                result.Add(current);
            }
            return result;
        }

        private static SourceWebSnapshot Clone(SourceWebSnapshot value)
        {
            return new SourceWebSnapshot
            {
                SiteId = value.SiteId,
                WebId = value.WebId,
                ParentWebId = value.ParentWebId,
                SiteCollectionUrl = value.SiteCollectionUrl,
                WebUrl = value.WebUrl,
                ServerRelativeUrl = value.ServerRelativeUrl,
                Title = value.Title,
                WebTemplate = value.WebTemplate,
                Configuration = value.Configuration,
                Availability = value.Availability,
                Diagnostics = (value.Diagnostics ?? new List<string>()).ToList()
            };
        }
    }
}
