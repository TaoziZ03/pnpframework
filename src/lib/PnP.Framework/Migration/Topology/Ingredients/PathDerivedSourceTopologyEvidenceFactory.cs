using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class PathDerivedSourceTopologyEvidenceFactory
    {
        public static PathDerivedSourceTopologyEvidence CreateAuthorizationBlocked(
            Guid sourceSiteId,
            string sourceSiteCollectionUrl,
            string sourceSiteServerRelativeUrl,
            Guid sourceLeafWebId,
            string sourceLeafWebUrl,
            string sourceLeafWebServerRelativeUrl,
            string operation,
            string requestUri,
            int httpStatusCode,
            DateTimeOffset observedAtUtc,
            IEnumerable<string> diagnostics = null)
        {
            var authorization = LiteralHttpAuthorizationEvidence.Create(
                operation,
                requestUri,
                httpStatusCode,
                observedAtUtc);
            var evidence = new PathDerivedSourceTopologyEvidence
            {
                SourceSiteId = sourceSiteId,
                SourceSiteCollectionUrl = sourceSiteCollectionUrl,
                SourceSiteServerRelativeUrl = sourceSiteServerRelativeUrl,
                SourceLeafWebId = sourceLeafWebId,
                SourceLeafWebUrl = sourceLeafWebUrl,
                SourceLeafWebServerRelativeUrl = sourceLeafWebServerRelativeUrl,
                FidelityState = SourceWebFidelityState.AuthorizationBlocked,
                AuthorizationEvidence = authorization,
                Diagnostics = (diagnostics ?? Enumerable.Empty<string>()).ToList()
            };
            evidence.EvidenceSha256 = SharedTopologyDigest.ComputeEvidence(evidence);
            Validate(evidence);
            return evidence;
        }

        public static void Validate(PathDerivedSourceTopologyEvidence evidence)
        {
            if (evidence == null)
            {
                throw new InvalidDataException("Path-derived source topology evidence is missing.");
            }
            if (!string.Equals(evidence.SchemaVersion, "pnp-path-derived-source-topology-evidence/v1", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsupported path-derived source topology evidence schema '" + evidence.SchemaVersion + "'.");
            }
            if (evidence.SourceSiteId == Guid.Empty || evidence.SourceLeafWebId == Guid.Empty)
            {
                throw new InvalidDataException("Path-derived source topology evidence requires real source Site and leaf-Web IDs.");
            }
            try
            {
                SharedTopologyPath.ValidateUrlMatchesPath(evidence.SourceSiteCollectionUrl, evidence.SourceSiteServerRelativeUrl, nameof(evidence.SourceSiteCollectionUrl));
                SharedTopologyPath.ValidateUrlMatchesPath(evidence.SourceLeafWebUrl, evidence.SourceLeafWebServerRelativeUrl, nameof(evidence.SourceLeafWebUrl));
                var siteUri = new Uri(SharedTopologyPath.NormalizeAbsoluteUrl(evidence.SourceSiteCollectionUrl, nameof(evidence.SourceSiteCollectionUrl)));
                var webUri = new Uri(SharedTopologyPath.NormalizeAbsoluteUrl(evidence.SourceLeafWebUrl, nameof(evidence.SourceLeafWebUrl)));
                if (!string.Equals(siteUri.Authority, webUri.Authority, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The source Site Collection and leaf Web must share one authority.");
                }
                SharedTopologyPath.RelativeSegments(evidence.SourceSiteServerRelativeUrl, evidence.SourceLeafWebServerRelativeUrl);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Path-derived source topology evidence has invalid URL/path facts: " + exception.Message, exception);
            }
            if (evidence.Diagnostics == null)
            {
                throw new InvalidDataException("Path-derived source topology diagnostics cannot be null.");
            }
            if (evidence.FidelityState != SourceWebFidelityState.AuthorizationBlocked)
            {
                throw new InvalidDataException("Path-derived source topology is admitted only for retained literal HTTP 401/403 ancestor evidence.");
            }
            LiteralHttpAuthorizationEvidence.Validate(evidence.AuthorizationEvidence);
            if (!IsSha256(evidence.EvidenceSha256)
                || !string.Equals(evidence.EvidenceSha256, SharedTopologyDigest.ComputeEvidence(evidence), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The path-derived source topology evidence digest does not match its content.");
            }
        }

        public static bool IsLiteralAuthorizationStatus(int httpStatusCode)
        {
            return httpStatusCode == 401 || httpStatusCode == 403;
        }

        public static string ComputeDigest(PathDerivedSourceTopologyEvidence evidence)
        {
            return SharedTopologyDigest.ComputeEvidence(evidence);
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 64
                && value.All(character => character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F');
        }
    }
}
