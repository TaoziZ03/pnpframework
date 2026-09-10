using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.EmbedIframe
{
    internal sealed class EmbedIframeNormalization
    {
        public PublishingPageCaptureBundle Snapshot { get; set; }

        public PageIngredientNode Node { get; set; }

        public string SemanticCanonicalJson { get; set; }

        public bool SourcePredicateMatched { get; set; }

        public IReadOnlyDictionary<string, string> ValueDigests { get; set; }

        public string RawLocator { get; set; }

        public string ResolvedLocator { get; set; }

        public string NormalizedLocator { get; set; }

        public string Classification { get; set; }
    }

    internal static class EmbedIframeEvidenceNormalizer
    {
        public const string Lane = "embed.iframe";
        public const string Subtype = "reference.embed.iframe";
        public const string SemanticRole = "embedded-frame-reference";
        public const string SourcePredicateId = "reference.kind-iframe";
        public const string ContributorId = "pnp-embed-iframe-maturity/v1";
        public const string RuntimeRequirement = "browser-runtime-load-and-policy-verification";

        private static readonly string[] RequiredValuePaths =
        {
            "binding.consumer",
            "binding.hostWebPartId",
            "binding.propertyName",
            "binding.zoneIndex",
            "capture.status",
            "geometry.height",
            "geometry.width",
            "locator.classification",
            "locator.normalized",
            "locator.raw",
            "locator.resolved",
            "policy.allow",
            "policy.frameborder",
            "policy.loading",
            "policy.name",
            "policy.referrerpolicy",
            "policy.sandbox",
            "policy.scrolling",
            "policy.style",
            "policy.title"
        };

        public static EmbedIframeNormalization Normalize(
            IngredientMaturityEvaluationContext context,
            EmbedIframeSourceEvidence evidence)
        {
            var reference = evidence?.Reference;
            var source = evidence?.Source;
            var host = evidence?.Host;
            var rawBytes = Decode(evidence?.RawArtifactBase64);
            var rawHtml = rawBytes == null ? null : Encoding.UTF8.GetString(rawBytes);
            var frames = ParseFrames(rawHtml);
            var frame = frames.Length == 1 ? frames[0] : null;
            var rawLocator = frame?.GetAttribute("src");
            var resolved = Resolve(source?.PageUrl, rawLocator);
            var resolvedLocator = resolved?.AbsoluteUri;
            var normalizedLocator = NormalizeLocator(resolved);
            var classification = Classify(source?.PageUrl, rawLocator, resolved);
            var ingredientId = reference == null ? null : "reference:" + reference.Id;
            var sourceIdentity = MigrationContractSerializer.SerializeCanonical(new SourceIdentityProjection
            {
                FileServerRelativeUrl = source?.FileServerRelativeUrl,
                ItemId = source?.ItemId ?? 0,
                ListId = source?.ListId,
                UniqueId = source?.UniqueId,
                Version = source?.Version,
                WebUrl = source?.WebUrl
            });
            var semantic = new SemanticProjection
            {
                Host = new HostProjection
                {
                    IngredientId = host?.IngredientId,
                    Kind = "WebPart",
                    Property = host?.PropertyName,
                    ZoneIndex = host?.ZoneIndex ?? 0
                },
                Reference = new ReferenceProjection
                {
                    CaptureStatus = reference?.CaptureStatus.ToString(),
                    Classification = classification,
                    Consumer = reference?.Consumer,
                    Frameborder = frame?.GetAttribute("frameborder"),
                    Height = frame?.GetAttribute("height"),
                    Id = reference?.Id,
                    IngredientId = ingredientId,
                    Kind = reference?.Kind.ToString(),
                    Marginheight = frame?.GetAttribute("marginheight"),
                    Marginwidth = frame?.GetAttribute("marginwidth"),
                    OriginalValue = rawLocator,
                    Scrolling = frame?.GetAttribute("scrolling"),
                    SourceAbsoluteUrl = resolvedLocator,
                    Style = frame?.GetAttribute("style"),
                    Width = frame?.GetAttribute("width")
                },
                Schema = "ccd.embed-iframe-source-observation/v1",
                Source = new SourceProjection
                {
                    ItemId = source?.ItemId ?? 0,
                    ListId = source?.ListId,
                    Modified = source?.Modified,
                    UniqueId = source?.UniqueId,
                    Url = source?.PageUrl,
                    Version = source?.Version
                }
            };
            var canonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var expectedConsumer = string.IsNullOrWhiteSpace(host?.WebPartId)
                ? null
                : "webpart:" + host.WebPartId;
            var expectedReferenceId = string.IsNullOrWhiteSpace(expectedConsumer)
                || string.IsNullOrWhiteSpace(resolvedLocator)
                ? null
                : MigrationDigest.ComputeSha256(expectedConsumer + "\n" + resolvedLocator);
            var valid = frames.Length == 1
                && reference != null
                && reference.Kind == PageReferenceKind.IFrame
                && reference.IsRenderableResource
                && reference.CaptureStatus == PageCaptureStatus.CapturedWithLimitations
                && string.IsNullOrWhiteSpace(reference.ContentBase64)
                && string.IsNullOrWhiteSpace(reference.ContentSha256)
                && !string.IsNullOrWhiteSpace(rawLocator)
                && !string.IsNullOrWhiteSpace(resolvedLocator)
                && string.Equals(rawLocator, reference.OriginalValue, StringComparison.Ordinal)
                && string.Equals(resolvedLocator, reference.SourceAbsoluteUrl, StringComparison.Ordinal)
                && string.Equals(normalizedLocator, reference.SourceAbsoluteUrl, StringComparison.Ordinal)
                && string.Equals(reference.Id, expectedReferenceId, StringComparison.Ordinal)
                && string.Equals(reference.Consumer, expectedConsumer, StringComparison.Ordinal)
                && string.Equals(classification, "external", StringComparison.Ordinal)
                && source != null
                && Uri.TryCreate(source.WebUrl, UriKind.Absolute, out _)
                && Uri.TryCreate(source.PageUrl, UriKind.Absolute, out _)
                && !string.IsNullOrWhiteSpace(source.FileServerRelativeUrl)
                && !string.IsNullOrWhiteSpace(source.ListId)
                && source.ItemId > 0
                && !string.IsNullOrWhiteSpace(source.UniqueId)
                && !string.IsNullOrWhiteSpace(source.Version)
                && !string.IsNullOrWhiteSpace(source.Modified)
                && host != null
                && !string.IsNullOrWhiteSpace(host.IngredientId)
                && !string.IsNullOrWhiteSpace(host.WebPartId)
                && string.Equals(host.PropertyName, "Content", StringComparison.Ordinal)
                && host.ZoneIndex >= 0;

            var snapshot = new PublishingPageCaptureBundle
            {
                Dependencies = reference == null
                    ? new List<PageReferenceSnapshot>()
                    : new List<PageReferenceSnapshot> { reference }
            };
            return new EmbedIframeNormalization
            {
                Snapshot = snapshot,
                Node = new PageIngredientNode
                {
                    Id = ingredientId,
                    Kind = PageIngredientKind.Reference,
                    Subtype = Subtype,
                    SemanticRole = SemanticRole,
                    SourcePredicateId = SourcePredicateId,
                    SourcePageOrListItemIdentity = sourceIdentity,
                    SourceVersionIdentity = source?.Version,
                    PrimaryOwnerLane = Lane,
                    Label = rawLocator,
                    HasContent = true,
                    Ownership = PageIngredientOwnership.SourceOwned,
                    SourceAuthority = reference?.Consumer,
                    EvidenceDigest = evidence?.RawArtifact?.Sha256,
                    RuntimeRequirement = RuntimeRequirement,
                    EvidenceReferences = (evidence?.EvidenceReferences ?? Array.Empty<string>())
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToList()
                },
                SemanticCanonicalJson = canonical,
                SourcePredicateMatched = valid,
                RawLocator = rawLocator,
                ResolvedLocator = resolvedLocator,
                NormalizedLocator = normalizedLocator,
                Classification = classification,
                ValueDigests = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["binding.consumer"] = ScalarDigest(reference?.Consumer),
                    ["binding.hostWebPartId"] = ScalarDigest(host?.WebPartId),
                    ["binding.propertyName"] = ScalarDigest(host?.PropertyName),
                    ["binding.zoneIndex"] = ScalarDigest(host?.ZoneIndex),
                    ["capture.status"] = ScalarDigest(reference?.CaptureStatus.ToString()),
                    ["geometry.height"] = ScalarDigest(frame?.GetAttribute("height")),
                    ["geometry.width"] = ScalarDigest(frame?.GetAttribute("width")),
                    ["locator.classification"] = ScalarDigest(classification),
                    ["locator.normalized"] = ScalarDigest(normalizedLocator),
                    ["locator.raw"] = ScalarDigest(rawLocator),
                    ["locator.resolved"] = ScalarDigest(resolvedLocator),
                    ["policy.allow"] = ScalarDigest(frame?.GetAttribute("allow")),
                    ["policy.frameborder"] = ScalarDigest(frame?.GetAttribute("frameborder")),
                    ["policy.loading"] = ScalarDigest(frame?.GetAttribute("loading")),
                    ["policy.name"] = ScalarDigest(frame?.GetAttribute("name")),
                    ["policy.referrerpolicy"] = ScalarDigest(frame?.GetAttribute("referrerpolicy")),
                    ["policy.sandbox"] = ScalarDigest(frame?.GetAttribute("sandbox")),
                    ["policy.scrolling"] = ScalarDigest(frame?.GetAttribute("scrolling")),
                    ["policy.style"] = ScalarDigest(frame?.GetAttribute("style")),
                    ["policy.title"] = ScalarDigest(frame?.GetAttribute("title"))
                }
            };
        }

        public static IngredientLiveEvidence ProjectLiveEvidence(
            IngredientLiveEvidence evidence,
            EmbedIframeNormalization normalized)
        {
            if (evidence == null)
            {
                return null;
            }
            var observations = (evidence.Observations ?? Array.Empty<IngredientValueObservation>())
                .Where(value => value != null)
                .ToList();
            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated
                    && ValuesMatch(observations, IngredientObservationOrigin.AuthenticatedSource, normalized?.ValueDigests),
                TargetFreshReadback = evidence.TargetFreshReadback
                    && ValuesMatch(observations, IngredientObservationOrigin.CupCollectFreshReadback, normalized?.ValueDigests),
                HistoricalOrSyntheticSubstitution = evidence.HistoricalOrSyntheticSubstitution,
                Observations = observations,
                SourceEvidenceReferences = (evidence.SourceEvidenceReferences ?? Array.Empty<string>()).ToList(),
                TargetEvidenceReferences = (evidence.TargetEvidenceReferences ?? Array.Empty<string>()).ToList()
            };
        }

        private static bool ValuesMatch(
            IEnumerable<IngredientValueObservation> observations,
            IngredientObservationOrigin origin,
            IReadOnlyDictionary<string, string> expected)
        {
            if (expected == null)
            {
                return false;
            }
            foreach (var path in RequiredValuePaths)
            {
                var values = observations.Where(value => value.Origin == origin
                    && string.Equals(value.ValuePath, path, StringComparison.Ordinal)).ToArray();
                if (values.Length != 1
                    || !expected.TryGetValue(path, out var digest)
                    || !string.Equals(values[0].ValueDigest, digest, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static IElement[] ParseFrames(string rawHtml)
        {
            if (string.IsNullOrWhiteSpace(rawHtml))
            {
                return Array.Empty<IElement>();
            }
            try
            {
                return new HtmlParser().ParseDocument(rawHtml).QuerySelectorAll("iframe").ToArray();
            }
            catch
            {
                return Array.Empty<IElement>();
            }
        }

        private static byte[] Decode(string value)
        {
            try
            {
                return string.IsNullOrWhiteSpace(value) ? null : Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static Uri Resolve(string pageUrl, string rawLocator)
        {
            if (string.IsNullOrWhiteSpace(rawLocator)
                || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
            {
                return null;
            }
            return Uri.TryCreate(page, rawLocator, out var resolved) ? resolved : null;
        }

        private static string NormalizeLocator(Uri locator)
        {
            if (locator == null
                || locator.Scheme != Uri.UriSchemeHttp && locator.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }
            return locator.AbsoluteUri;
        }

        private static string Classify(string pageUrl, string rawLocator, Uri resolved)
        {
            if (string.IsNullOrWhiteSpace(rawLocator) || resolved == null)
            {
                return "invalid";
            }
            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
            {
                return "opaque";
            }
            if (!Uri.TryCreate(rawLocator, UriKind.Absolute, out _))
            {
                return "source-relative";
            }
            if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)
                && string.Equals(page.Host, resolved.Host, StringComparison.OrdinalIgnoreCase))
            {
                return "sharepoint-internal";
            }
            return "external";
        }

        private static string ScalarDigest(object value)
        {
            var canonical = value == null ? "null" : MigrationContractSerializer.SerializeCanonical(value);
            return MigrationDigest.ComputeSha256(canonical);
        }

        private sealed class SemanticProjection
        {
            public HostProjection Host { get; set; }

            public ReferenceProjection Reference { get; set; }

            public string Schema { get; set; }

            public SourceProjection Source { get; set; }
        }

        private sealed class HostProjection
        {
            public string IngredientId { get; set; }

            public string Kind { get; set; }

            public string Property { get; set; }

            public int ZoneIndex { get; set; }
        }

        private sealed class ReferenceProjection
        {
            public string CaptureStatus { get; set; }

            public string Classification { get; set; }

            public string Consumer { get; set; }

            public string Frameborder { get; set; }

            public string Height { get; set; }

            public string Id { get; set; }

            public string IngredientId { get; set; }

            public string Kind { get; set; }

            public string Marginheight { get; set; }

            public string Marginwidth { get; set; }

            public string OriginalValue { get; set; }

            public string Scrolling { get; set; }

            public string SourceAbsoluteUrl { get; set; }

            public string Style { get; set; }

            public string Width { get; set; }
        }

        private sealed class SourceProjection
        {
            public int ItemId { get; set; }

            public string ListId { get; set; }

            public string Modified { get; set; }

            public string UniqueId { get; set; }

            public string Url { get; set; }

            public string Version { get; set; }
        }

        private sealed class SourceIdentityProjection
        {
            public string FileServerRelativeUrl { get; set; }

            public int ItemId { get; set; }

            public string ListId { get; set; }

            public string UniqueId { get; set; }

            public string Version { get; set; }

            public string WebUrl { get; set; }
        }
    }
}
