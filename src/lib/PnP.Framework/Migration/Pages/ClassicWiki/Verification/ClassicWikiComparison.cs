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

            if (string.Equals(sourceSnap.WikiFieldSha256, targetSnap.WikiFieldSha256, StringComparison.OrdinalIgnoreCase))
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

            // 4. Web Parts value-level comparison
            CompareWebParts(sourceSnap.WebParts, targetSnap.WebParts, result);

            // 5. Dependencies value-level comparison
            CompareDependencies(sourceSnap.Dependencies, targetSnap.Dependencies, result);

            // 6. Lifecycle value-level comparison
            ClassicWikiMetadataComparison.CompareLifecycle(sourceSnap, targetSnap, result);

            // 7. Security value-level comparison
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
                    string.Equals(act.ZoneId, exp.ZoneId, StringComparison.OrdinalIgnoreCase)
                    && act.ZoneIndex == exp.ZoneIndex
                    && (string.IsNullOrEmpty(exp.TypeName) || string.IsNullOrEmpty(act.TypeName) || string.Equals(act.TypeName, exp.TypeName, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrEmpty(exp.ExportSha256) || string.IsNullOrEmpty(act.ExportSha256) || string.Equals(act.ExportSha256, exp.ExportSha256, StringComparison.OrdinalIgnoreCase)));

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
                    && (string.Equals(act.Id, exp.Id, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(act.SourceServerRelativeUrl, exp.SourceServerRelativeUrl, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(act.OriginalValue, exp.OriginalValue, StringComparison.OrdinalIgnoreCase)));

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
    }
}
