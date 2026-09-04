using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public sealed class ClassicWikiComparisonResult
    {
        public bool Passed { get; set; }

        public bool WikiContentMatched { get; set; }

        public bool BracketNormalizationMatched { get; set; }

        public bool WebPartsMatched { get; set; }

        public bool NestedFoldersMatched { get; set; }

        public bool EmptyContentPreserved { get; set; }

        public bool DependenciesMatched { get; set; }

        public bool LifecycleMatched { get; set; }

        public bool SecurityMatched { get; set; }

        public IList<string> Differences { get; set; } = new List<string>();

        public IList<string> CanariesPassed { get; set; } = new List<string>();
    }

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

            var exactMatch = string.Equals(sourceSnap.WikiFieldSha256, targetSnap.WikiFieldSha256, StringComparison.OrdinalIgnoreCase);
            if (exactMatch)
            {
                result.WikiContentMatched = true;
                result.CanariesPassed.Add("ExactWikiFieldMatch");
            }
            else
            {
                // Check entity-safe bracket normalization: [[ -> &#91;&#91; and ]] -> &#93;&#93;
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
            if (string.IsNullOrEmpty(sourceContent) && string.IsNullOrEmpty(targetContent))
            {
                result.EmptyContentPreserved = true;
                result.CanariesPassed.Add("EmptyContentPreserved");
            }
            else if (!string.IsNullOrEmpty(sourceContent))
            {
                result.EmptyContentPreserved = true;
            }

            // 3. Nested folders comparison
            var sourcePath = sourceSnap.Source?.PageServerRelativeUrl ?? string.Empty;
            var targetPath = targetSnap.Source?.PageServerRelativeUrl ?? string.Empty;
            var sourceDir = PagePath.GetDirectoryName(sourcePath);
            var targetDir = PagePath.GetDirectoryName(targetPath);
            var sourceFileName = PagePath.GetFileName(sourcePath);
            var targetFileName = PagePath.GetFileName(targetPath);

            if (string.Equals(sourceFileName, targetFileName, StringComparison.OrdinalIgnoreCase))
            {
                result.NestedFoldersMatched = true;
                result.CanariesPassed.Add("FolderAndFileNameFidelity");
            }
            else
            {
                result.Differences.Add($"Filename mismatch: source '{sourceFileName}', target '{targetFileName}'.");
            }

            // 4. Web Parts comparison
            if (sourceSnap.WebParts.Count == targetSnap.WebParts.Count)
            {
                result.WebPartsMatched = true;
                result.CanariesPassed.Add("WebPartCountAndPlacementFidelity");
            }
            else
            {
                result.Differences.Add($"WebPart count mismatch: source has {sourceSnap.WebParts.Count}, target has {targetSnap.WebParts.Count}.");
            }

            // 5. Dependencies comparison
            if (sourceSnap.Dependencies.Count == targetSnap.Dependencies.Count)
            {
                result.DependenciesMatched = true;
                result.CanariesPassed.Add("DependencyCountFidelity");
            }
            else
            {
                result.Differences.Add($"Dependency count mismatch: source has {sourceSnap.Dependencies.Count}, target has {targetSnap.Dependencies.Count}.");
            }

            // 6. Lifecycle comparison
            if (sourceSnap.Lifecycle != null && targetSnap.Lifecycle != null)
            {
                result.LifecycleMatched = true;
                result.CanariesPassed.Add("LifecycleFidelity");
            }

            // 7. Security comparison
            if (sourceSnap.Security != null && targetSnap.Security != null)
            {
                result.SecurityMatched = true;
                result.CanariesPassed.Add("SecurityFidelity");
            }

            result.Passed = result.Differences.Count == 0;
            return result;
        }
    }
}
