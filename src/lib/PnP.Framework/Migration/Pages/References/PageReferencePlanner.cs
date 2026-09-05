using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Content;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.References
{
    internal static class PageReferencePlanner
    {
        public static List<PageReferenceAction> BuildActions(
            PageIdentity source,
            IEnumerable<PageReferenceSnapshot> dependencies,
            string targetWebUrl,
            string targetWebServerRelativeUrl,
            SiteCollectionMappingPlan siteMapping,
            PagePlanningOptions options,
            ICollection<string> blockers)
        {
            var sourceWebUri = new Uri(UrlUtility.EnsureTrailingSlash(source.WebUrl));
            var targetWebUri = new Uri(UrlUtility.EnsureTrailingSlash(targetWebUrl));
            var sourceWebPath = Uri.UnescapeDataString(sourceWebUri.AbsolutePath).TrimEnd('/');
            var targetWebPath = targetWebServerRelativeUrl.TrimEnd('/');
            var sourceSitePath = siteMapping == null
                ? null
                : Uri.UnescapeDataString(new Uri(siteMapping.SourceSiteCollectionUrl).AbsolutePath).TrimEnd('/');
            var targetSitePath = siteMapping == null
                ? null
                : Uri.UnescapeDataString(new Uri(siteMapping.TargetSiteCollectionUrl).AbsolutePath).TrimEnd('/');
            var dependencyArray = (dependencies ?? Array.Empty<PageReferenceSnapshot>()).ToArray();
            var result = new List<PageReferenceAction>();
            foreach (var reference in dependencyArray)
            {
                var action = new PageReferenceAction
                {
                    SnapshotDependencyId = reference.Id,
                    Disposition = PageReferenceDisposition.PreserveExternal
                };
                result.Add(action);
                if (!Uri.TryCreate(reference.SourceAbsoluteUrl, UriKind.Absolute, out var sourceUri))
                {
                    action.Disposition = PageReferenceDisposition.Block;
                    action.Diagnostics.Add("The captured dependency URL is not an absolute HTTP(S) URL.");
                    blockers.Add($"Dependency '{reference.OriginalValue}' has an invalid captured URL.");
                    continue;
                }

                action.TargetAbsoluteUrl = sourceUri.AbsoluteUri;

                if (!string.Equals(sourceWebUri.Host, sourceUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    if (reference.IsRenderableResource && !options.AllowExternalResourceReferences)
                    {
                        action.Disposition = PageReferenceDisposition.Block;
                        action.Diagnostics.Add("External renderable resources are blocked by planning policy.");
                        blockers.Add($"External resource '{sourceUri}' is blocked by policy.");
                    }

                    continue;
                }

                var sourcePath = reference.SourceServerRelativeUrl ?? Uri.UnescapeDataString(sourceUri.AbsolutePath);
                var insideSourceWeb = PagePath.IsWithin(sourcePath, sourceWebPath);
                var insideSourceSite = !string.IsNullOrWhiteSpace(sourceSitePath)
                    && PagePath.IsWithin(sourcePath, sourceSitePath);
                if (PageReferenceSnapshotReader.IsSharePointRuntimePath(sourcePath)
                    && reference.Kind != PageReferenceKind.IFrame)
                {
                    action.TargetServerRelativeUrl = insideSourceWeb
                        ? targetWebPath + sourcePath.Substring(sourceWebPath.Length)
                        : insideSourceSite
                            ? targetSitePath + sourcePath.Substring(sourceSitePath.Length)
                            : sourcePath;
                    action.TargetAbsoluteUrl = targetWebUri.GetLeftPart(UriPartial.Authority)
                        + PagePath.Encode(action.TargetServerRelativeUrl)
                        + sourceUri.Query
                        + sourceUri.Fragment;
                    action.Disposition = PageReferenceDisposition.RewriteToTarget;
                    action.Diagnostics.Add(
                        "Rewrite the recognized SharePoint runtime path only after a fresh target HTTP probe verifies the exact resource.");
                    continue;
                }

                if (!insideSourceWeb && !insideSourceSite)
                {
                    action.Diagnostics.Add(
                        "The same-tenant reference is outside the reviewed source Site Collection mapping and is preserved unchanged.");
                    continue;
                }

                var targetPath = insideSourceWeb
                    ? targetWebPath + sourcePath.Substring(sourceWebPath.Length)
                    : targetSitePath + sourcePath.Substring(sourceSitePath.Length);
                action.TargetServerRelativeUrl = targetPath;
                action.TargetAbsoluteUrl = targetWebUri.GetLeftPart(UriPartial.Authority)
                    + PagePath.Encode(targetPath)
                    + sourceUri.Query
                    + sourceUri.Fragment;
                action.Disposition = PageReferenceDisposition.RewriteToTarget;
                if (!reference.IsRenderableResource)
                {
                    continue;
                }

                if (reference.Kind == PageReferenceKind.IFrame)
                {
                    action.Disposition = PageReferenceDisposition.Delegate;
                    action.Diagnostics.Add(
                        "Retain the exact iframe relationship for a separately reviewed page/application profile.");
                    continue;
                }

                if (reference.CaptureStatus == PageCaptureStatus.Failed
                    || reference.ContentBase64 == null
                    || string.IsNullOrWhiteSpace(reference.ContentSha256))
                {
                    if (options.AllowExternalResourceReferences)
                    {
                        action.Disposition = PageReferenceDisposition.PreserveExternal;
                        action.TargetServerRelativeUrl = null;
                        action.TargetAbsoluteUrl = sourceUri.AbsoluteUri;
                        action.Diagnostics.Add(
                            "The source payload is unavailable; retain the exact source resource identity as an external reference without claiming or copying bytes.");
                        continue;
                    }

                    action.Disposition = PageReferenceDisposition.Block;
                    action.Diagnostics.Add("The source payload was not captured successfully.");
                    blockers.Add($"Resource '{sourceUri}' has no restorable payload in the source snapshot.");
                    continue;
                }

                action.Disposition = PageReferenceDisposition.MaterializeAtTarget;
            }

            var dependencyById = dependencyArray
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var group in result
                         .Where(value => value.Disposition == PageReferenceDisposition.MaterializeAtTarget)
                         .GroupBy(value => value.TargetServerRelativeUrl, StringComparer.OrdinalIgnoreCase))
            {
                var payloads = group
                    .Select(value => dependencyById[value.SnapshotDependencyId])
                    .Select(value => $"{value.ContentLength}:{value.ContentSha256}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (payloads.Length <= 1)
                {
                    continue;
                }

                foreach (var action in group)
                {
                    action.Disposition = PageReferenceDisposition.Block;
                    action.Diagnostics.Add(
                        "Multiple captured payloads map to the same target dependency path with different length or SHA-256 evidence.");
                }
                blockers.Add(
                    $"Conflicting dependency payloads map to target path '{group.Key}'.");
            }

            return result.OrderBy(action => action.SnapshotDependencyId, StringComparer.Ordinal).ToList();
        }

        public static IList<PageTextReplacement> BuildTextReplacements(
            PageIdentity source,
            string targetWebUrl,
            string targetWebServerRelativeUrl,
            IEnumerable<PageReferenceSnapshot> dependencies = null,
            IEnumerable<PageReferenceAction> actions = null)
        {
            var sourceWebUri = new Uri(source.WebUrl);
            var targetWebUri = new Uri(targetWebUrl);
            var candidates = new[]
            {
                new PageTextReplacement
                {
                    Source = source.WebUrl.TrimEnd('/'),
                    Target = targetWebUrl.TrimEnd('/'),
                    Reason = "Map authored absolute URLs from the source web to the target web."
                },
                new PageTextReplacement
                {
                    Source = Uri.UnescapeDataString(sourceWebUri.AbsolutePath).TrimEnd('/'),
                    Target = targetWebServerRelativeUrl.TrimEnd('/'),
                    Reason = "Map authored server-relative URLs from the source web to the target web."
                },
                new PageTextReplacement
                {
                    Source = sourceWebUri.AbsolutePath.TrimEnd('/'),
                    Target = targetWebUri.AbsolutePath.TrimEnd('/'),
                    Reason = "Map URL-encoded source web paths to the target web."
                }
            }.ToList();
            var dependencyById = (dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .Where(value => value != null)
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var action in actions ?? Array.Empty<PageReferenceAction>())
            {
                if (action == null
                    || action.Disposition != PageReferenceDisposition.PreserveExternal
                        && action.Disposition != PageReferenceDisposition.RewriteToTarget
                        && action.Disposition != PageReferenceDisposition.MaterializeAtTarget
                    || !dependencyById.TryGetValue(action.SnapshotDependencyId, out var reference)
                    || string.IsNullOrWhiteSpace(reference.OriginalValue))
                {
                    continue;
                }

                var target = ReferenceTarget(reference, action);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    candidates.Add(new PageTextReplacement
                    {
                        Source = reference.OriginalValue,
                        Target = target,
                        Reason = "Map the exact captured dependency reference to its reviewed target path."
                    });
                }
            }

            return candidates
                .Where(item => !string.IsNullOrEmpty(item.Source)
                    && !string.Equals(item.Source, item.Target, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => item.Source.Length)
                .ToList();
        }

        private static string ReferenceTarget(
            PageReferenceSnapshot reference,
            PageReferenceAction action)
        {
            if (action.Disposition == PageReferenceDisposition.PreserveExternal)
            {
                return action.TargetAbsoluteUrl ?? reference.SourceAbsoluteUrl;
            }

            if (Uri.TryCreate(reference.OriginalValue, UriKind.Absolute, out _))
            {
                return action.TargetAbsoluteUrl;
            }
            if (reference.OriginalValue.StartsWith("/", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(reference.SourceAbsoluteUrl, UriKind.Absolute, out var sourceUri))
                {
                    return action.TargetServerRelativeUrl;
                }
                return action.TargetServerRelativeUrl + sourceUri.Query + sourceUri.Fragment;
            }
            return action.TargetAbsoluteUrl;
        }
    }
}
