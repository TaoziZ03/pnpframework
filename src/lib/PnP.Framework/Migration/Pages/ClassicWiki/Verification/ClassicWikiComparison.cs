using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public static class ClassicWikiComparison
    {
        public static ClassicWikiComparisonResult Compare(
            ClassicWikiExportPackage sourcePackage,
            ClassicWikiExportPackage targetRecapturePackage)
        {
            if (sourcePackage == null) throw new ArgumentNullException(nameof(sourcePackage));
            if (targetRecapturePackage == null) throw new ArgumentNullException(nameof(targetRecapturePackage));

            var result = new ClassicWikiComparisonResult();
            var sourceSnap = sourcePackage.Snapshot;
            var targetSnap = targetRecapturePackage.Snapshot;

            // 1. Content comparison
            var sourceContent = sourceSnap.WikiField ?? string.Empty;
            var targetContent = targetSnap.WikiField ?? string.Empty;

            if (RequiredEquals(sourceSnap.WikiFieldSha256, targetSnap.WikiFieldSha256))
            {
                result.WikiContentMatched = true;
                result.CanariesPassed.Add("ExactWikiFieldMatch");
            }
            else
            {
                var sourceNorm = sourceContent.Replace("&#91;&#91;", "[[").Replace("&#93;&#93;", "]]");
                var targetNorm = targetContent.Replace("&#91;&#91;", "[[").Replace("&#93;&#93;", "]]");
                if (string.Equals(sourceNorm, targetNorm, StringComparison.Ordinal))
                {
                    result.WikiContentMatched = true;
                    result.BracketNormalizationMatched = true;
                    result.CanariesPassed.Add("BracketNormalizationMatch");
                }
                else
                {
                    result.Differences.Add($"Wiki content mismatch: source SHA '{sourceSnap.WikiFieldSha256}', target SHA '{targetSnap.WikiFieldSha256}'.");
                }
            }

            // 2. Empty content canary
            if (string.IsNullOrEmpty(sourceContent))
            {
                if (string.IsNullOrEmpty(targetContent))
                {
                    result.EmptyContentPreserved = true;
                    result.CanariesPassed.Add("EmptyContentPreserved");
                }
                else
                {
                    result.Differences.Add("Empty content canary failed: source was empty but target has content.");
                }
            }

            // 3. Nested folders comparison
            ClassicWikiMetadataComparison.CompareFolderHierarchy(sourceSnap, targetSnap, result);

            // 4. Fields, Content Type, library, and runtime identity
            ClassicWikiMetadataComparison.CompareFields(sourceSnap.Fields, targetSnap.Fields, result);
            ClassicWikiMetadataComparison.CompareContentType(sourceSnap, targetSnap, result);
            ClassicWikiMetadataComparison.CompareLibrary(sourceSnap, targetSnap, result);
            ClassicWikiMetadataComparison.CompareRuntime(sourceSnap.Runtime, targetSnap.Runtime, result);

            // 5. Web Parts value-level comparison
            CompareWebParts(sourceSnap.WebParts, targetSnap.WebParts, result);

            // 6. Dependencies value-level comparison
            CompareDependencies(sourceSnap.Dependencies, targetSnap.Dependencies, result);

            // 7. Lifecycle value-level comparison
            ClassicWikiMetadataComparison.CompareLifecycle(sourceSnap, targetSnap, result);

            // 8. Security value-level comparison
            ClassicWikiMetadataComparison.CompareSecurity(sourceSnap.Security, targetSnap.Security, result);

            result.Passed = result.Differences.Count == 0;
            return result;
        }

        private static void CompareWebParts(IList<ClassicWebPartSnapshot> sourceWps, IList<ClassicWebPartSnapshot> targetWps, ClassicWikiComparisonResult result)
        {
            var expectedWps = sourceWps ?? (IList<ClassicWebPartSnapshot>)Array.Empty<ClassicWebPartSnapshot>();
            var actualWps = targetWps ?? (IList<ClassicWebPartSnapshot>)Array.Empty<ClassicWebPartSnapshot>();
            if (expectedWps.Count != actualWps.Count)
            {
                result.Differences.Add($"WebPart count mismatch: source has {expectedWps.Count}, target has {actualWps.Count}.");
                return;
            }

            var allMatched = true;
            var unusedTargetWps = actualWps.ToList();
            foreach (var exp in expectedWps)
            {
                var match = unusedTargetWps.FirstOrDefault(act =>
                    RequiredEquals(act.ZoneId, exp.ZoneId)
                    && act.ZoneIndex == exp.ZoneIndex
                    && act.Hidden == exp.Hidden
                    && RequiredEquals(act.TypeName, exp.TypeName)
                    && RequiredEquals(act.ExportSha256, exp.ExportSha256));

                if (match != null) unusedTargetWps.Remove(match);
                else
                {
                    allMatched = false;
                    result.Differences.Add($"WebPart placement/digest mismatch: Zone='{exp.ZoneId}', Index={exp.ZoneIndex}, Type='{exp.TypeName}', Title='{exp.Title}'.");
                }
            }

            if (allMatched)
            {
                result.WebPartsMatched = true;
                result.CanariesPassed.Add("WebPartCountAndPlacementFidelity");
            }
        }

        private static void CompareDependencies(IList<PageReferenceSnapshot> sourceDeps, IList<PageReferenceSnapshot> targetDeps, ClassicWikiComparisonResult result)
        {
            var expectedDeps = sourceDeps ?? (IList<PageReferenceSnapshot>)Array.Empty<PageReferenceSnapshot>();
            var actualDeps = targetDeps ?? (IList<PageReferenceSnapshot>)Array.Empty<PageReferenceSnapshot>();
            if (expectedDeps.Count != actualDeps.Count)
            {
                result.Differences.Add($"Dependency count mismatch: source has {expectedDeps.Count}, target has {actualDeps.Count}.");
                return;
            }

            var allMatched = true;
            var unusedDeps = actualDeps.ToList();
            foreach (var exp in expectedDeps)
            {
                var match = unusedDeps.FirstOrDefault(act =>
                    act.Kind == exp.Kind
                    && RequiredEquals(act.Id, exp.Id)
                    && RequiredEquals(act.Consumer, exp.Consumer)
                    && RequiredEquals(act.OriginalValue, exp.OriginalValue)
                    && OptionalEquals(act.SourceAbsoluteUrl, exp.SourceAbsoluteUrl)
                    && OptionalEquals(act.SourceServerRelativeUrl, exp.SourceServerRelativeUrl));

                if (match != null) unusedDeps.Remove(match);
                else
                {
                    allMatched = false;
                    result.Differences.Add($"Dependency mismatch: Kind={exp.Kind}, Id='{exp.Id}', Url='{exp.SourceServerRelativeUrl}'.");
                }
            }

            if (allMatched)
            {
                result.DependenciesMatched = true;
                result.CanariesPassed.Add("DependencyCountFidelity");
            }
        }

        internal static bool RequiredEquals(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool OptionalEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                return true;
            }

            return RequiredEquals(left, right);
        }
    }
}
