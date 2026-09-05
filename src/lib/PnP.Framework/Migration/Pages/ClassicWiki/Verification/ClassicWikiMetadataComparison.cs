using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.Packaging;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    internal static class ClassicWikiMetadataComparison
    {
        public static void CompareFolderHierarchy(ClassicWikiCaptureBundle sourceSnap, ClassicWikiCaptureBundle targetSnap, ClassicWikiComparisonResult result)
        {
            var sourcePath = sourceSnap.Source?.PageServerRelativeUrl ?? string.Empty;
            var targetPath = targetSnap.Source?.PageServerRelativeUrl ?? string.Empty;
            var sourceFileName = PagePath.GetFileName(sourcePath);
            var targetFileName = PagePath.GetFileName(targetPath);
            var fileNameMatch = string.Equals(sourceFileName, targetFileName, StringComparison.OrdinalIgnoreCase);
            var sourceRelFolder = GetRelativeFolder(sourcePath, sourceSnap.LibraryServerRelativeUrl);
            var targetRelFolder = GetRelativeFolder(targetPath, targetSnap.LibraryServerRelativeUrl);
            var folderMatch = string.Equals(sourceRelFolder, targetRelFolder, StringComparison.OrdinalIgnoreCase);

            if (fileNameMatch && folderMatch)
            {
                result.NestedFoldersMatched = true;
                result.CanariesPassed.Add("FolderAndFileNameFidelity");
            }
            else
            {
                if (!fileNameMatch) result.Differences.Add($"Filename mismatch: source '{sourceFileName}', target '{targetFileName}'.");
                if (!folderMatch) result.Differences.Add($"Nested folder hierarchy mismatch: source '{sourceRelFolder}', target '{targetRelFolder}'.");
            }
        }

        public static void CompareLifecycle(ClassicWikiCaptureBundle sourceSnap, ClassicWikiCaptureBundle targetSnap, ClassicWikiComparisonResult result)
        {
            if (sourceSnap.Lifecycle != null && targetSnap.Lifecycle != null)
            {
                var levelMatch = string.Equals(sourceSnap.Lifecycle.Level, targetSnap.Lifecycle.Level, StringComparison.OrdinalIgnoreCase);
                var checkOutMatch = string.Equals(sourceSnap.Lifecycle.CheckOutType, targetSnap.Lifecycle.CheckOutType, StringComparison.OrdinalIgnoreCase);
                var modMatch = sourceSnap.Lifecycle.ModerationStatus == targetSnap.Lifecycle.ModerationStatus;

                if (levelMatch && checkOutMatch && modMatch)
                {
                    result.LifecycleMatched = true;
                    result.CanariesPassed.Add("LifecycleFidelity");
                }
                else
                {
                    result.Differences.Add($"Lifecycle mismatch: source (Level={sourceSnap.Lifecycle.Level}, CheckOut={sourceSnap.Lifecycle.CheckOutType}, Moderation={sourceSnap.Lifecycle.ModerationStatus}) vs target (Level={targetSnap.Lifecycle.Level}, CheckOut={targetSnap.Lifecycle.CheckOutType}, Moderation={targetSnap.Lifecycle.ModerationStatus}).");
                }
            }
            else if (sourceSnap.Lifecycle == null && targetSnap.Lifecycle == null)
            {
                result.LifecycleMatched = true;
            }
            else
            {
                result.Differences.Add("Lifecycle presence mismatch between source and target.");
            }
        }

        public static void CompareSecurity(PageSecuritySnapshot sourceSecurity, PageSecuritySnapshot targetSecurity, ClassicWikiComparisonResult result)
        {
            if (sourceSecurity != null && targetSecurity != null)
            {
                var uniqueMatch = sourceSecurity.HasUniqueRoleAssignments == targetSecurity.HasUniqueRoleAssignments;
                var sRoles = sourceSecurity.RoleAssignments ?? (IList<PageRoleAssignmentSnapshot>)Array.Empty<PageRoleAssignmentSnapshot>();
                var tRoles = targetSecurity.RoleAssignments ?? (IList<PageRoleAssignmentSnapshot>)Array.Empty<PageRoleAssignmentSnapshot>();
                var countMatch = sRoles.Count == tRoles.Count;
                var rolesMatch = true;

                if (uniqueMatch && countMatch)
                {
                    foreach (var sRole in sRoles)
                    {
                        var tRole = tRoles.FirstOrDefault(r => string.Equals(r.PrincipalLoginName, sRole.PrincipalLoginName, StringComparison.OrdinalIgnoreCase));
                        if (tRole == null) { rolesMatch = false; break; }
                        var sDefs = (sRole.RoleDefinitionNames ?? (IList<string>)Array.Empty<string>()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                        var tDefs = (tRole.RoleDefinitionNames ?? (IList<string>)Array.Empty<string>()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                        if (!sDefs.SequenceEqual(tDefs, StringComparer.OrdinalIgnoreCase)) { rolesMatch = false; break; }
                    }
                }
                else { rolesMatch = false; }

                if (uniqueMatch && countMatch && rolesMatch)
                {
                    result.SecurityMatched = true;
                    result.CanariesPassed.Add("SecurityFidelity");
                }
                else
                {
                    result.Differences.Add($"Security mismatch: source unique={sourceSecurity.HasUniqueRoleAssignments} ({sRoles.Count} roles) vs target unique={targetSecurity.HasUniqueRoleAssignments} ({tRoles.Count} roles).");
                }
            }
            else if (sourceSecurity == null && targetSecurity == null)
            {
                result.SecurityMatched = true;
            }
            else
            {
                result.Differences.Add("Security presence mismatch between source and target.");
            }
        }

        private static string GetRelativeFolder(string pageUrl, string libraryUrl)
        {
            var dir = PagePath.GetDirectoryName(pageUrl ?? string.Empty).TrimEnd('/');
            if (!string.IsNullOrEmpty(libraryUrl))
            {
                var lib = libraryUrl.TrimEnd('/');
                if (dir.StartsWith(lib, StringComparison.OrdinalIgnoreCase))
                {
                    return dir.Substring(lib.Length).TrimStart('/');
                }
            }

            var idx = dir.IndexOf("/SitePages/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return dir.Substring(idx + "/SitePages/".Length).TrimStart('/');
            idx = dir.IndexOf("/SitePages", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && dir.Length == idx + "/SitePages".Length) return string.Empty;

            return dir;
        }
    }
}
