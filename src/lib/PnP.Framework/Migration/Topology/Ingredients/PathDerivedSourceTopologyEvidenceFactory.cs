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
        public const string SourceAncestorReadOperation = "ReadSourceParentWeb";

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
                throw new ArgumentNullException(sourceRootWeb == null ? nameof(sourceRootWeb) : nameof(sourceLeafWeb));
            }
            return CreateAuthorizationBlocked(
                new[] { sourceRootWeb, sourceLeafWeb },
                sourceRootWeb.WebId,
                sourceLeafWeb.WebId,
                expectedOperation,
                expectedRequestUri,
                literalEvidence,
                diagnostics);
        }

        public static PathDerivedSourceTopologyEvidence CreateAuthorizationBlocked(
            IEnumerable<SourceWebSnapshot> capturedWebs,
            Guid sourceRootWebId,
            Guid primaryLeafWebId,
            string expectedOperation,
            string expectedRequestUri,
            LiteralHttpAuthorizationEvidence literalEvidence,
            IEnumerable<string> diagnostics = null)
        {
            LiteralHttpAuthorizationEvidence.Validate(literalEvidence);
            var candidates = (capturedWebs ?? Enumerable.Empty<SourceWebSnapshot>())
                .Where(value => value != null)
                .Select(Clone)
                .ToArray();
            var root = candidates.SingleOrDefault(value => value.WebId == sourceRootWebId);
            var leaf = candidates.SingleOrDefault(value => value.WebId == primaryLeafWebId);
            if (root == null || leaf == null)
            {
                throw new InvalidDataException("Captured source topology must retain the selected root and primary leaf Webs.");
            }
            var chainPaths = ChainPaths(root.ServerRelativeUrl, leaf.ServerRelativeUrl);
            var chain = candidates
                .Where(value => chainPaths.Contains(
                    SharedTopologyPath.NormalizeServerRelativePath(value.ServerRelativeUrl, nameof(capturedWebs)),
                    StringComparer.OrdinalIgnoreCase))
                .OrderBy(value => SharedTopologyPath.Depth(value.ServerRelativeUrl))
                .ToList();
            var capturedPaths = new HashSet<string>(
                chain.Select(value => SharedTopologyPath.NormalizeServerRelativePath(value.ServerRelativeUrl, nameof(capturedWebs))),
                StringComparer.OrdinalIgnoreCase);
            var requestUri = new Uri(expectedRequestUri, UriKind.Absolute).AbsoluteUri;
            var evidence = new PathDerivedSourceTopologyEvidence
            {
                SourceSiteId = root.SiteId,
                SourceRootWebId = root.WebId,
                PrimaryLeafWebId = leaf.WebId,
                CapturedWebs = chain,
                UnknownAncestorPaths = chainPaths.Where(value => !capturedPaths.Contains(value)).ToList(),
                AncestorReadOperation = expectedOperation?.Trim(),
                AncestorReadRequestUri = requestUri,
                AncestorAuthorizationEvidence = BoundLiteralHttpAuthorizationEvidence.Create(
                    SourceAncestorReadActionId,
                    expectedOperation,
                    requestUri,
                    literalEvidence),
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
                || evidence.SourceRootWebId == Guid.Empty
                || evidence.PrimaryLeafWebId == Guid.Empty
                || evidence.SourceRootWebId == evidence.PrimaryLeafWebId
                || evidence.CapturedWebs == null
                || evidence.CapturedWebs.Count < 2
                || evidence.CapturedWebs.Any(value => value == null)
                || evidence.UnknownAncestorPaths == null
                || string.IsNullOrWhiteSpace(evidence.AncestorReadOperation)
                || string.IsNullOrWhiteSpace(evidence.AncestorReadRequestUri)
                || evidence.Diagnostics == null)
            {
                throw new InvalidDataException("Path-derived source topology requires captured root/leaf identities, retained intermediate evidence, and a versioned authorization envelope.");
            }
            var capturedIds = new HashSet<Guid>();
            var capturedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var web in evidence.CapturedWebs)
            {
                ValidateCapturedWeb(web);
                if (web.SiteId != evidence.SourceSiteId
                    || !capturedIds.Add(web.WebId)
                    || !capturedPaths.Add(SharedTopologyPath.NormalizeServerRelativePath(web.ServerRelativeUrl, nameof(web.ServerRelativeUrl))))
                {
                    throw new InvalidDataException("Captured path-derived Web evidence contains a different Site, duplicate Web ID, or duplicate path.");
                }
            }
            var root = Root(evidence);
            var leaf = PrimaryLeaf(evidence);
            var rootSite = SharedTopologyPath.NormalizeAbsoluteUrl(root.SiteCollectionUrl, nameof(root.SiteCollectionUrl));
            var expectedRequestUri = leaf.WebUrl.TrimEnd('/') + "/_vti_bin/client.svc/ProcessQuery";
            var expectedAuthority = new Uri(rootSite).Authority;
            if (!SharedTopologyPath.EqualsUrl(rootSite, root.WebUrl)
                || evidence.CapturedWebs.Any(value => !SharedTopologyPath.EqualsUrl(rootSite, value.SiteCollectionUrl)))
            {
                throw new InvalidDataException("Every captured path-derived Web must share the selected Site Collection root.");
            }
            var chainPaths = ChainPaths(root.ServerRelativeUrl, leaf.ServerRelativeUrl);
            if (capturedPaths.Any(value => !chainPaths.Contains(value, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Captured path-derived Web evidence contains a Web outside the root-to-primary-leaf chain.");
            }
            var expectedUnknown = chainPaths.Where(value => !capturedPaths.Contains(value)).ToArray();
            var actualUnknown = evidence.UnknownAncestorPaths
                .Select(value => SharedTopologyPath.NormalizeServerRelativePath(value, nameof(evidence.UnknownAncestorPaths)))
                .ToArray();
            if (!expectedUnknown.SequenceEqual(actualUnknown, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Unknown ancestor paths do not complement the successfully captured root-to-leaf Web evidence.");
            }
            BoundLiteralHttpAuthorizationEvidence.Validate(
                evidence.AncestorAuthorizationEvidence,
                SourceAncestorReadActionId,
                SourceAncestorReadOperation,
                expectedAuthority,
                expectedRequestUri);
            if (!string.Equals(evidence.AncestorReadOperation, SourceAncestorReadOperation, StringComparison.Ordinal)
                || !string.Equals(
                    new Uri(evidence.AncestorReadRequestUri, UriKind.Absolute).AbsoluteUri,
                    new Uri(expectedRequestUri, UriKind.Absolute).AbsoluteUri,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The source authorization envelope is not bound to the primary leaf parent-read request.");
            }
            if (!PnP.Framework.Migration.Execution.MigrationActionSignature.IsSha256(evidence.EvidenceSha256)
                || !string.Equals(evidence.EvidenceSha256, SharedTopologyDigest.ComputeEvidence(evidence), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The path-derived source topology evidence digest does not match its content.");
            }
        }

        public static SourceWebSnapshot Root(PathDerivedSourceTopologyEvidence evidence)
        {
            return (evidence ?? throw new ArgumentNullException(nameof(evidence))).CapturedWebs
                .Single(value => value.WebId == evidence.SourceRootWebId);
        }

        public static SourceWebSnapshot PrimaryLeaf(PathDerivedSourceTopologyEvidence evidence)
        {
            return (evidence ?? throw new ArgumentNullException(nameof(evidence))).CapturedWebs
                .Single(value => value.WebId == evidence.PrimaryLeafWebId);
        }

        public static string ComputeDigest(PathDerivedSourceTopologyEvidence evidence)
        {
            return SharedTopologyDigest.ComputeEvidence(evidence);
        }

        private static void ValidateCapturedWeb(SourceWebSnapshot web)
        {
            SharedTopologyPath.ValidateUrlMatchesPath(web.WebUrl, web.ServerRelativeUrl, nameof(web.WebUrl));
            if (web.SiteId == Guid.Empty
                || web.WebId == Guid.Empty
                || web.Availability != EvidenceAvailability.Captured
                || string.IsNullOrWhiteSpace(web.Title)
                || string.IsNullOrWhiteSpace(web.WebTemplate)
                || web.Configuration < 0)
            {
                throw new InvalidDataException("Captured source Web fidelity requires identity, path, title, and template evidence.");
            }
        }

        private static IList<string> ChainPaths(string rootPath, string leafPath)
        {
            var result = new List<string>
            {
                SharedTopologyPath.NormalizeServerRelativePath(rootPath, nameof(rootPath))
            };
            var current = result[0];
            foreach (var segment in SharedTopologyPath.RelativeSegments(rootPath, leafPath))
            {
                current = SharedTopologyPath.Combine(current, segment);
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
