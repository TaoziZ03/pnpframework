using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal static class SharedTopologyPath
    {
        public static string NormalizeServerRelativePath(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A server-relative path is required.", parameterName);
            }
            var trimmed = value.Trim();
            if (trimmed.IndexOf('?') >= 0
                || trimmed.IndexOf('#') >= 0
                || trimmed.IndexOf('\\') >= 0
                || trimmed.IndexOf("%2f", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf("%5c", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new ArgumentException("A server-relative path cannot contain a query, fragment, or backslash.", parameterName);
            }
            var normalized = Uri.UnescapeDataString(trimmed);
            if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException("A server-relative path is required.", parameterName);
            }
            if (normalized.Length > 1)
            {
                normalized = normalized.TrimEnd('/');
            }
            var segments = normalized.Split('/');
            if (segments.Skip(1).Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment.Length > 128
                || segment == "."
                || segment == ".."
                || segment.EndsWith(".", StringComparison.Ordinal)
                || segment.EndsWith(" ", StringComparison.Ordinal)))
            {
                throw new ArgumentException("The path contains an empty, traversing, or ambiguous segment.", parameterName);
            }
            return normalized;
        }

        public static string NormalizeAbsoluteUrl(string value, string parameterName)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException("An HTTPS URL without query or fragment is required.", parameterName);
            }
            return uri.AbsoluteUri.TrimEnd('/');
        }

        public static string[] RelativeSegments(string ancestorPath, string descendantPath)
        {
            var ancestor = NormalizeServerRelativePath(ancestorPath, nameof(ancestorPath)).TrimEnd('/');
            var descendant = NormalizeServerRelativePath(descendantPath, nameof(descendantPath)).TrimEnd('/');
            if (string.Equals(ancestor, descendant, StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }
            if (!descendant.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The descendant path is outside the declared ancestor.", nameof(descendantPath));
            }
            var relative = descendant.Substring(ancestor.Length + 1);
            var segments = relative.Split('/');
            foreach (var segment in segments)
            {
                ValidateSegment(segment, nameof(descendantPath));
            }
            return segments;
        }

        public static string Combine(string parent, string segment)
        {
            var normalizedParent = NormalizeServerRelativePath(parent, nameof(parent)).TrimEnd('/');
            ValidateSegment(segment, nameof(segment));
            return (normalizedParent.Length == 0 ? string.Empty : normalizedParent) + "/" + segment;
        }

        public static string AbsoluteUrl(string siteUrl, string serverRelativePath)
        {
            var absoluteSite = new Uri(NormalizeAbsoluteUrl(siteUrl, nameof(siteUrl)));
            var path = NormalizeServerRelativePath(serverRelativePath, nameof(serverRelativePath));
            return new Uri(absoluteSite.GetLeftPart(UriPartial.Authority) + path).AbsoluteUri.TrimEnd('/');
        }

        public static string Leaf(string path)
        {
            var normalized = NormalizeServerRelativePath(path, nameof(path));
            return normalized.Substring(normalized.LastIndexOf('/') + 1);
        }

        public static int Depth(string path)
        {
            return NormalizeServerRelativePath(path, nameof(path))
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        public static bool EqualsPath(string left, string right)
        {
            return string.Equals(
                NormalizeServerRelativePath(left, nameof(left)),
                NormalizeServerRelativePath(right, nameof(right)),
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool EqualsUrl(string left, string right)
        {
            return string.Equals(
                NormalizeAbsoluteUrl(left, nameof(left)),
                NormalizeAbsoluteUrl(right, nameof(right)),
                StringComparison.OrdinalIgnoreCase);
        }

        public static string AllocateCollisionSuffix(string preferredPath, string stableIdentity, IEnumerable<string> occupiedPaths)
        {
            var preferred = NormalizeServerRelativePath(preferredPath, nameof(preferredPath));
            var occupied = new HashSet<string>(
                (occupiedPaths ?? Enumerable.Empty<string>()).Select(value => NormalizeServerRelativePath(value, nameof(occupiedPaths))),
                StringComparer.OrdinalIgnoreCase);
            if (!occupied.Contains(preferred))
            {
                return preferred;
            }
            var parent = preferred.Substring(0, preferred.LastIndexOf('/'));
            var leaf = Leaf(preferred);
            var digest = SharedTopologyIdentity.StableDigest(stableIdentity);
            for (var length = 8; length <= digest.Length; length += 4)
            {
                var suffix = "-pnp-" + digest.Substring(0, length);
                var maximumStemLength = 128 - suffix.Length;
                var stem = leaf.Length <= maximumStemLength ? leaf : leaf.Substring(0, maximumStemLength).TrimEnd(' ', '.', '-');
                if (stem.Length == 0)
                {
                    stem = "web";
                }
                var candidate = Combine(parent.Length == 0 ? "/" : parent, stem + suffix);
                if (!occupied.Contains(candidate))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("No deterministic target segment remained after exhausting the stable collision digest.");
        }

        public static void ValidateUrlMatchesPath(string absoluteUrl, string serverRelativePath, string parameterName)
        {
            var uri = new Uri(NormalizeAbsoluteUrl(absoluteUrl, parameterName));
            var path = NormalizeServerRelativePath(serverRelativePath, parameterName);
            if (!string.Equals(Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The absolute URL and server-relative path do not identify the same object.", parameterName);
            }
        }

        private static void ValidateSegment(string segment, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(segment)
                || segment.Length > 128
                || segment == "."
                || segment == ".."
                || segment.IndexOf('/') >= 0
                || segment.IndexOf('\\') >= 0
                || segment.EndsWith(".", StringComparison.Ordinal)
                || segment.EndsWith(" ", StringComparison.Ordinal))
            {
                throw new ArgumentException("A safe, unambiguous URL segment is required.", parameterName);
            }
        }
    }
}
