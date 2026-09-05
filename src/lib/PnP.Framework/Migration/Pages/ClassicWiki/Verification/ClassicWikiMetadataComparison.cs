using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Packaging;
using PnP.Framework.Migration.Pages.Runtime;
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
            else
            {
                result.Differences.Add("Lifecycle evidence is missing from the source or target capture.");
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
            else
            {
                result.Differences.Add("Security evidence is missing from the source or target capture.");
            }
        }

        public static void CompareFields(IList<PageFieldValueSnapshot> sourceFields, IList<PageFieldValueSnapshot> targetFields, ClassicWikiComparisonResult result)
        {
            var expected = (sourceFields ?? Array.Empty<PageFieldValueSnapshot>())
                .Where(IsPortableValueField)
                .ToList();
            var actual = (targetFields ?? Array.Empty<PageFieldValueSnapshot>())
                .Where(IsPortableValueField)
                .ToList();
            if (expected.Count == 0 || actual.Count == 0)
            {
                result.Differences.Add("Portable field evidence is missing from the source or target capture.");
                return;
            }

            foreach (var sourceField in expected)
            {
                var targetField = actual.FirstOrDefault(value =>
                    ClassicWikiComparison.RequiredEquals(value.InternalName, sourceField.InternalName));
                if (targetField == null
                    || !string.Equals(sourceField.Value ?? string.Empty, targetField.Value ?? string.Empty, StringComparison.Ordinal))
                {
                    result.Differences.Add($"Field value mismatch for '{sourceField.InternalName}'.");
                    return;
                }
            }

            result.FieldsMatched = true;
            result.CanariesPassed.Add("FieldValueFidelity");
        }

        public static void CompareContentType(ClassicWikiCaptureBundle sourceSnap, ClassicWikiCaptureBundle targetSnap, ClassicWikiComparisonResult result)
        {
            var sourceId = sourceSnap.Source?.ContentTypeId;
            var targetId = targetSnap.Source?.ContentTypeId;
            if (ClassicWikiComparison.RequiredEquals(sourceId, targetId)
                && ClassicWikiComparison.RequiredEquals(sourceSnap.Source?.ContentTypeName, targetSnap.Source?.ContentTypeName))
            {
                result.ContentTypeMatched = true;
                result.CanariesPassed.Add("ContentTypeFidelity");
            }
            else
            {
                result.Differences.Add($"Content Type mismatch: source '{sourceId}'/'{sourceSnap.Source?.ContentTypeName}', target '{targetId}'/'{targetSnap.Source?.ContentTypeName}'.");
            }
        }

        public static void CompareLibrary(ClassicWikiCaptureBundle sourceSnap, ClassicWikiCaptureBundle targetSnap, ClassicWikiComparisonResult result)
        {
            if ((sourceSnap.LibraryBaseTemplate == 101 || sourceSnap.LibraryBaseTemplate == 119)
                && sourceSnap.LibraryBaseTemplate == targetSnap.LibraryBaseTemplate
                && ClassicWikiComparison.RequiredEquals(sourceSnap.LibraryTitle, targetSnap.LibraryTitle)
                && !string.IsNullOrWhiteSpace(sourceSnap.LibraryServerRelativeUrl)
                && !string.IsNullOrWhiteSpace(targetSnap.LibraryServerRelativeUrl))
            {
                result.LibraryMatched = true;
                result.CanariesPassed.Add("LibraryIdentityFidelity");
            }
            else
            {
                result.Differences.Add($"Library mismatch: source template/title '{sourceSnap.LibraryBaseTemplate}:{sourceSnap.LibraryTitle}', target '{targetSnap.LibraryBaseTemplate}:{targetSnap.LibraryTitle}'.");
            }
        }

        public static void CompareRuntime(PageRuntimeSnapshot sourceRuntime, PageRuntimeSnapshot targetRuntime, ClassicWikiComparisonResult result)
        {
            if (sourceRuntime != null
                && targetRuntime != null
                && sourceRuntime.ResolutionState == PageRuntimeResolutionState.Resolved
                && targetRuntime.ResolutionState == PageRuntimeResolutionState.Resolved
                && ClassicWikiComparison.RequiredEquals(sourceRuntime.AdapterId, targetRuntime.AdapterId))
            {
                result.RuntimeMatched = true;
                result.CanariesPassed.Add("RuntimeIdentityFidelity");
            }
            else
            {
                result.Differences.Add("Resolved page runtime identity is missing or differs between source and target.");
            }
        }

        private static bool IsPortableValueField(PageFieldValueSnapshot field)
        {
            return field != null
                && (string.Equals(field.InternalName, "Title", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field.InternalName, "FileLeafRef", StringComparison.OrdinalIgnoreCase));
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
