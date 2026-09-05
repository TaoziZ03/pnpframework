using PnP.Framework.Migration.Pages.Fields;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.Packaging;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PnP.Framework.Migration.Pages.References
{
    internal static class PageReferenceSnapshotReader
    {
        private static readonly Regex CssUrlPattern = new Regex(
            @"url\(\s*(?:['""](?<url>.*?)['""]|(?<url>[^)]*?))\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static List<PageReferenceSnapshot> Read(
            ClientContext sourceContext,
            PageIdentity source,
            SourceSiteCollectionSnapshot sourceTopology,
            string pageContent,
            IEnumerable<ClassicWebPartSnapshot> webParts,
            PageCaptureOptions options,
            ICollection<string> warnings,
            IEnumerable<PageFieldValueSnapshot> fields = null,
            Func<Web, ClientContext, string, long, byte[]> payloadReader = null)
        {
            var candidates = ExtractHtmlReferences(pageContent);
            foreach (var webPart in webParts ?? Array.Empty<ClassicWebPartSnapshot>())
            {
                var consumer = $"webpart:{webPart.Id}";
                candidates.AddRange(ExtractClassicScriptEditorReferences(webPart.ExportXml, consumer));
                candidates.AddRange(ExtractTextReferences(webPart.ExportXml, consumer));
            }

            if (fields != null)
            {
                foreach (var field in fields)
                {
                    if (field == null
                        || (field.Kind != PageFieldValueKind.String
                            && field.Kind != PageFieldValueKind.Url))
                    {
                        continue;
                    }

                    var fieldValue = field.Kind == PageFieldValueKind.Url
                        ? field.UrlValue?.Url
                        : field.Kind == PageFieldValueKind.String
                            ? field.Value
                            : null;
                    if (string.IsNullOrWhiteSpace(fieldValue))
                    {
                        continue;
                    }

                    if (field.Kind == PageFieldValueKind.Url)
                    {
                        candidates.Add(new ReferenceCandidate
                        {
                            Consumer = $"field:{field.InternalName}",
                            Kind = IsImageField(field.InternalName) ? PageReferenceKind.Image : PageReferenceKind.Anchor,
                            Value = fieldValue.Trim(),
                            IsRenderableResource = IsImageField(field.InternalName)
                        });
                        continue;
                    }

                    var fieldConsumer = $"field:{field.InternalName}";
                    var htmlRefs = ExtractHtmlReferences(fieldValue, fieldConsumer).ToList();
                    candidates.AddRange(htmlRefs);
                    if (htmlRefs.Count == 0 && IsImageField(field.InternalName) && LooksLikeReference(fieldValue))
                    {
                        candidates.Add(new ReferenceCandidate
                        {
                            Consumer = $"field:{field.InternalName}",
                            Kind = PageReferenceKind.Image,
                            Value = fieldValue.Trim(),
                            IsRenderableResource = true
                        });
                    }
                    var htmlValues = new HashSet<string>(htmlRefs.Select(r => r.Value), StringComparer.OrdinalIgnoreCase);
                    candidates.AddRange(ExtractTextReferences(fieldValue, fieldConsumer).Where(r => !htmlValues.Contains(r.Value)));
                }
            }

            var sourceWebUri = new Uri(UrlUtility.EnsureTrailingSlash(source.WebUrl));
            var sourcePageUri = new Uri(sourceWebUri.GetLeftPart(UriPartial.Authority) + PagePath.Encode(source.PageServerRelativeUrl));
            var result = new List<PageReferenceSnapshot>();
            foreach (var candidate in candidates
                         .GroupBy(item => $"{item.Consumer}\n{item.Value}", StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
            {
                if (!TryResolveUri(
                    sourcePageUri,
                    sourceWebUri,
                    sourceTopology?.SiteCollectionUrl,
                    candidate.Value,
                    out var absoluteUri))
                {
                    continue;
                }

                result.Add(Capture(
                    sourceContext,
                    source,
                    sourceTopology,
                    sourceWebUri,
                    candidate,
                    absoluteUri,
                    options,
                    warnings,
                    payloadReader));
            }

            return result;
        }

        public static bool IsSharePointRuntimePath(string serverRelativeUrl)
        {
            if (string.IsNullOrWhiteSpace(serverRelativeUrl))
            {
                return false;
            }

            return serverRelativeUrl.IndexOf("/_layouts/", StringComparison.OrdinalIgnoreCase) >= 0
                || serverRelativeUrl.IndexOf("/_controltemplates/", StringComparison.OrdinalIgnoreCase) >= 0
                || serverRelativeUrl.IndexOf("/_vti_bin/", StringComparison.OrdinalIgnoreCase) >= 0
                || serverRelativeUrl.IndexOf("/_api/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static PageReferenceSnapshot Capture(
            ClientContext sourceContext,
            PageIdentity source,
            SourceSiteCollectionSnapshot sourceTopology,
            Uri sourceWebUri,
            ReferenceCandidate candidate,
            Uri absoluteUri,
            PageCaptureOptions options,
            ICollection<string> warnings,
            Func<Web, ClientContext, string, long, byte[]> payloadReader)
        {
            var reference = new PageReferenceSnapshot
            {
                Id = PageDigest.ComputeSha256($"{candidate.Consumer}\n{absoluteUri.AbsoluteUri}"),
                OriginalValue = candidate.Value,
                SourceAbsoluteUrl = absoluteUri.AbsoluteUri,
                Consumer = candidate.Consumer,
                Kind = candidate.Kind,
                IsRenderableResource = candidate.IsRenderableResource,
                CaptureStatus = PageCaptureStatus.Captured
            };
            if (!string.Equals(sourceWebUri.Host, absoluteUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                MarkMissingRenderablePayload(
                    reference,
                    "The external renderable resource is retained by URL only; no source payload was captured.");
                return reference;
            }

            var sourcePath = Uri.UnescapeDataString(absoluteUri.AbsolutePath);
            var sourceWebPath = Uri.UnescapeDataString(sourceWebUri.AbsolutePath).TrimEnd('/');
            reference.SourceServerRelativeUrl = sourcePath;
            if (!candidate.IsRenderableResource)
            {
                return reference;
            }

            if (IsSharePointRuntimePath(sourcePath))
            {
                MarkMissingRenderablePayload(
                    reference,
                    "The reference is supplied by the SharePoint runtime; no source payload was captured.");
                return reference;
            }

            if (candidate.Kind == PageReferenceKind.IFrame)
            {
                reference.CaptureStatus = PageCaptureStatus.CapturedWithLimitations;
                reference.Diagnostics.Add("Same-tenant iframe dependencies require a separately reviewed page/application profile during planning.");
                return reference;
            }

            var owner = (sourceTopology?.Webs ?? Array.Empty<SourceWebSnapshot>())
                .Where(web => web != null
                    && !string.IsNullOrWhiteSpace(web.ServerRelativeUrl)
                    && PagePath.IsWithin(sourcePath, web.ServerRelativeUrl))
                .OrderByDescending(web => web.ServerRelativeUrl.Length)
                .FirstOrDefault();
            if (owner == null && PagePath.IsWithin(sourcePath, sourceWebPath))
            {
                owner = new SourceWebSnapshot
                {
                    WebId = source.WebId,
                    ServerRelativeUrl = source.WebServerRelativeUrl,
                    WebUrl = source.WebUrl
                };
            }
            if (owner == null)
            {
                reference.CaptureStatus = PageCaptureStatus.CapturedWithLimitations;
                reference.Diagnostics.Add(
                    "The resource owner is outside the captured source Site/Web topology closure.");
                return reference;
            }

            if (sourceContext != null)
            {
                var requestUri = PageReferenceAuthorizationEvidence.CsomRequestUri(sourceContext.Url);
                try
                {
                    var ownerWeb = owner.WebId == source.WebId
                        ? sourceContext.Web
                        : sourceContext.Site.OpenWebById(owner.WebId);
                    var payload = (payloadReader ?? ReadFile)(
                        ownerWeb,
                        sourceContext,
                        sourcePath,
                        options.MaximumDependencyBytes);
                    if (payload == null)
                    {
                        throw new IOException("The source dependency reader returned no payload bytes.");
                    }
                    reference.ContentBase64 = Convert.ToBase64String(payload);
                    reference.ContentLength = payload.LongLength;
                    reference.ContentSha256 = PageDigest.ComputeSha256(payload);
                }
                catch (Exception exception) when (PageReferenceAuthorizationEvidence.IsExpectedReadFailure(exception))
                {
                    reference.CaptureStatus = PageCaptureStatus.Failed;
                    if (PageReferenceAuthorizationEvidence.TryCreate(
                            exception,
                            PageReferenceAuthorizationEvidence.SourceCaptureOperation,
                            requestUri,
                            out var authorizationEvidence))
                    {
                        reference.AuthorizationEvidence = authorizationEvidence;
                    }
                    reference.Diagnostics.Add(exception.Message);
                    warnings?.Add($"Resource '{absoluteUri}' could not be captured and may block a later plan: {exception.Message}");
                }
            }
            else
            {
                reference.CaptureStatus = PageCaptureStatus.CapturedWithLimitations;
                reference.Diagnostics.Add("The renderable resource candidate was extracted without a source ClientContext, so no payload was captured.");
                warnings?.Add($"Resource '{absoluteUri}' was discovered without a source ClientContext; no restorable payload is claimed.");
            }

            return reference;
        }

        private static bool LooksLikeReference(string value)
        {
            var trimmed = value?.Trim();
            return !string.IsNullOrWhiteSpace(trimmed)
                && (trimmed.StartsWith("/", StringComparison.Ordinal)
                    || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsImageField(string internalName)
        {
            return string.Equals(internalName, "PublishingPageImage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(internalName, "PublishingRollupImage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(internalName, "PublishingContactPicture", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] ReadFile(
            Web web,
            ClientContext context,
            string serverRelativeUrl,
            long maximumBytes)
        {
            var file = web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(serverRelativeUrl));
            context.Load(file, value => value.Exists, value => value.Length);
            var stream = file.OpenBinaryStream();
            context.ExecuteQueryRetry();
            if (!file.Exists || stream.Value == null)
            {
                throw new FileNotFoundException("The referenced SharePoint file was not found.", serverRelativeUrl);
            }

            if (file.Length > maximumBytes)
            {
                throw new InvalidOperationException($"The dependency is {file.Length} bytes, above the configured {maximumBytes}-byte limit.");
            }

            using (stream.Value)
            using (var output = new MemoryStream())
            {
                stream.Value.CopyTo(output);
                if (output.Length > maximumBytes)
                {
                    throw new InvalidOperationException($"The dependency is above the configured {maximumBytes}-byte limit.");
                }

                return output.ToArray();
            }
        }

        private static List<ReferenceCandidate> ExtractHtmlReferences(string html)
        {
            return ExtractHtmlReferences(html, null);
        }

        private static List<ReferenceCandidate> ExtractHtmlReferences(string html, string consumer)
        {
            var result = new List<ReferenceCandidate>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return result;
            }

            var document = new HtmlParser().ParseDocument(html);
            foreach (var element in document.All)
            {
                AddAttributeReference(result, element, "href", GetKind(element, "href"), consumer);
                AddAttributeReference(result, element, "src", GetKind(element, "src"), consumer);
                AddAttributeReference(result, element, "poster", PageReferenceKind.Media, consumer);
                AddAttributeReference(result, element, "data", PageReferenceKind.Object, consumer);
                var style = element.GetAttribute("style");
                if (!string.IsNullOrWhiteSpace(style))
                {
                    foreach (Match match in CssUrlPattern.Matches(style))
                    {
                        result.Add(new ReferenceCandidate
                        {
                            Consumer = consumer ?? $"{element.LocalName}[style]",
                            Kind = PageReferenceKind.Image,
                            Value = match.Groups["url"].Value.Trim(),
                            IsRenderableResource = true
                        });
                    }
                }
            }

            return result;
        }

        private static IEnumerable<ReferenceCandidate> ExtractClassicScriptEditorReferences(
            string exportXml,
            string consumer)
        {
            if (string.IsNullOrWhiteSpace(exportXml))
            {
                return Array.Empty<ReferenceCandidate>();
            }

            try
            {
                var document = XDocument.Parse(exportXml, LoadOptions.PreserveWhitespace);
                return document
                    .Descendants()
                    .Where(element => string.Equals(element.Name.LocalName, "webPart", StringComparison.OrdinalIgnoreCase))
                    .Where(IsScriptEditorWebPart)
                    .SelectMany(element => element
                        .Descendants()
                        .Where(property => string.Equals(property.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(AttributeValue(property, "name"), "Content", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(property =>
                        {
                            var content = WebUtility.HtmlDecode(property.Value ?? string.Empty);
                            return ExtractHtmlReferences(content, consumer)
                                .Concat(ExtractTextReferences(content, consumer, inferSafeRenderableKind: true));
                        }))
                    .GroupBy(candidate => candidate.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray();
            }
            catch (System.Xml.XmlException)
            {
                return Array.Empty<ReferenceCandidate>();
            }
        }

        private static bool IsScriptEditorWebPart(XElement webPart)
        {
            return webPart.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "type", StringComparison.OrdinalIgnoreCase))
                .Select(element => AttributeValue(element, "name"))
                .Any(value => !string.IsNullOrWhiteSpace(value)
                    && string.Equals(
                        value.Split(',')[0].Trim(),
                        "Microsoft.SharePoint.WebPartPages.ScriptEditorWebPart",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string AttributeValue(XElement element, string localName)
        {
            return element?.Attributes()
                .FirstOrDefault(attribute => string.Equals(
                    attribute.Name.LocalName,
                    localName,
                    StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private static IEnumerable<ReferenceCandidate> ExtractTextReferences(
            string text,
            string consumer,
            bool inferSafeRenderableKind = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<ReferenceCandidate>();
            }

            return Regex.Matches(
                    text,
                    @"(?<quote>['""])(?<path>(?:https?://|/|~site(?:collection)?/|~/)[^'""<>\r\n]+)\k<quote>|https?://[^\s'""<>]+",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(match => (match.Groups["path"].Success ? match.Groups["path"].Value : match.Value)
                    .Trim()
                    .TrimEnd('.', ',', ';', ')'))
                .Select(value =>
                {
                    var kind = inferSafeRenderableKind
                        ? InferSafeRenderableKind(value)
                        : PageReferenceKind.Unknown;
                    return new ReferenceCandidate
                    {
                        Consumer = consumer,
                        Kind = kind,
                        Value = value,
                        IsRenderableResource = kind == PageReferenceKind.Script
                            || kind == PageReferenceKind.StyleSheet
                    };
                })
                .ToArray();
        }

        private static PageReferenceKind InferSafeRenderableKind(string value)
        {
            var path = value ?? string.Empty;
            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            {
                path = absolute.AbsolutePath;
            }
            else
            {
                var delimiter = path.IndexOfAny(new[] { '?', '#' });
                if (delimiter >= 0)
                {
                    path = path.Substring(0, delimiter);
                }
            }

            if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                return PageReferenceKind.Script;
            }
            if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                return PageReferenceKind.StyleSheet;
            }
            return PageReferenceKind.Unknown;
        }

        private static void AddAttributeReference(
            ICollection<ReferenceCandidate> result,
            IElement element,
            string attributeName,
            PageReferenceKind kind,
            string consumer)
        {
            var value = element.GetAttribute(attributeName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            result.Add(new ReferenceCandidate
            {
                Consumer = consumer ?? $"{element.LocalName}[{attributeName}]",
                Kind = kind,
                Value = value.Trim(),
                IsRenderableResource = kind != PageReferenceKind.Anchor
                    && kind != PageReferenceKind.Unknown
            });
        }

        private static void MarkMissingRenderablePayload(
            PageReferenceSnapshot reference,
            string diagnostic)
        {
            if (reference == null || !reference.IsRenderableResource)
            {
                return;
            }

            reference.CaptureStatus = PageCaptureStatus.CapturedWithLimitations;
            reference.Diagnostics.Add(diagnostic);
        }

        private static PageReferenceKind GetKind(IElement element, string attributeName)
        {
            switch (element.LocalName.ToLowerInvariant())
            {
                case "a":
                case "area":
                    return PageReferenceKind.Anchor;
                case "img":
                    return PageReferenceKind.Image;
                case "script":
                    return PageReferenceKind.Script;
                case "link":
                    return PageReferenceKind.StyleSheet;
                case "iframe":
                    return PageReferenceKind.IFrame;
                case "object":
                    return PageReferenceKind.Object;
                case "audio":
                case "source":
                case "video":
                    return PageReferenceKind.Media;
                default:
                    return attributeName == "href"
                        ? PageReferenceKind.Anchor
                        : PageReferenceKind.Unknown;
            }
        }

        private static bool TryResolveUri(
            Uri sourcePageUri,
            Uri sourceWebUri,
            string sourceSiteCollectionUrl,
            string value,
            out Uri result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(value)
                || value.StartsWith("#", StringComparison.Ordinal)
                || value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var normalized = value.Trim().Replace('\\', '/');
            if (normalized.StartsWith("~sitecollection/", StringComparison.OrdinalIgnoreCase))
            {
                var owner = Uri.TryCreate(sourceSiteCollectionUrl, UriKind.Absolute, out var siteCollection)
                    ? siteCollection
                    : sourceWebUri;
                normalized = new Uri(
                    new Uri(UrlUtility.EnsureTrailingSlash(owner.AbsoluteUri)),
                    normalized.Substring("~sitecollection/".Length)).AbsoluteUri;
            }
            else if (normalized.StartsWith("~site/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = new Uri(
                    new Uri(UrlUtility.EnsureTrailingSlash(sourceWebUri.AbsoluteUri)),
                    normalized.Substring("~site/".Length)).AbsoluteUri;
            }
            else if (normalized.StartsWith("~/", StringComparison.Ordinal))
            {
                normalized = new Uri(
                    new Uri(UrlUtility.EnsureTrailingSlash(sourceWebUri.AbsoluteUri)),
                    normalized.Substring(2)).AbsoluteUri;
            }

            return Uri.TryCreate(sourcePageUri, normalized, out result)
                && (result.Scheme == Uri.UriSchemeHttps || result.Scheme == Uri.UriSchemeHttp);
        }

        private sealed class ReferenceCandidate
        {
            public string Value { get; set; }

            public string Consumer { get; set; }

            public PageReferenceKind Kind { get; set; }

            public bool IsRenderableResource { get; set; }
        }
    }
}
