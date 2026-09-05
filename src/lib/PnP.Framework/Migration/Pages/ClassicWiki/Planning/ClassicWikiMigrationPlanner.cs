using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Topology;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Planning
{
    public sealed class ClassicWikiMigrationPlanner
    {
        public ClassicWikiMigrationPackage Plan(
            ClientContext targetContext,
            ClassicWikiExportPackage exportPackage,
            PagePlanningOptions options)
        {
            return Plan(targetContext, exportPackage, options, null);
        }

        public ClassicWikiMigrationPackage Plan(
            ClientContext targetContext,
            ClassicWikiExportPackage exportPackage,
            PagePlanningOptions options,
            IMigrationArtifactStore artifactStore)
        {
            if (targetContext == null) throw new ArgumentNullException(nameof(targetContext));
            var targetWeb = targetContext.Web;
            targetContext.Load(targetWeb, w => w.Id, w => w.Url, w => w.ServerRelativeUrl, w => w.Title);
            targetContext.ExecuteQueryRetry();
            return PlanCore(targetWeb.Id, targetWeb.Url, targetWeb.ServerRelativeUrl, exportPackage, options, artifactStore);
        }

        internal static ClassicWikiMigrationPackage PlanCore(
            Guid targetWebId,
            string targetWebUrl,
            string targetWebServerRelativeUrl,
            ClassicWikiExportPackage exportPackage,
            PagePlanningOptions options,
            IMigrationArtifactStore artifactStore = null)
        {
            if (string.IsNullOrWhiteSpace(targetWebUrl)) throw new ArgumentException("Target web URL is required.", nameof(targetWebUrl));
            if (exportPackage == null) throw new ArgumentNullException(nameof(exportPackage));
            if (options == null) throw new ArgumentNullException(nameof(options));

            ClassicWikiPackageValidator.ValidateExport(exportPackage, artifactStore);

            var webServerRelativeUrl = targetWebServerRelativeUrl ?? "/";
            var snapshot = exportPackage.Snapshot;
            var targetPageUrl = options.TargetPageServerRelativeUrl;
            var sourceLibraryPath = snapshot.LibraryServerRelativeUrl?.TrimEnd('/');
            var sourceLibraryLeaf = !string.IsNullOrWhiteSpace(sourceLibraryPath)
                ? PagePath.GetFileName(sourceLibraryPath)
                : null;
            var targetLibraryLeaf = string.IsNullOrWhiteSpace(sourceLibraryLeaf)
                ? (string.IsNullOrWhiteSpace(snapshot.LibraryTitle) ? "SitePages" : snapshot.LibraryTitle.Replace(" ", string.Empty))
                : sourceLibraryLeaf;
            var sourceRelativeFolder = GetRelativeFolder(
                PagePath.GetDirectoryName(snapshot.Source.PageServerRelativeUrl),
                sourceLibraryPath);
            string targetLibraryDir;
            string targetFolderDir;
            if (string.IsNullOrWhiteSpace(targetPageUrl))
            {
                var fileName = PagePath.GetFileName(snapshot.Source.PageServerRelativeUrl);
                targetLibraryDir = CombineServerRelative(webServerRelativeUrl, targetLibraryLeaf);
                targetFolderDir = CombineServerRelative(targetLibraryDir, sourceRelativeFolder);
                targetPageUrl = CombineServerRelative(targetFolderDir, fileName);
            }
            else
            {
                targetPageUrl = PagePath.Normalize(webServerRelativeUrl, targetPageUrl, "SitePages");
                targetFolderDir = PagePath.GetDirectoryName(targetPageUrl);
                targetLibraryDir = RemoveRelativeFolder(targetFolderDir, sourceRelativeFolder);
            }

            var warnings = new List<string>(snapshot.Warnings);
            var blockers = new List<string>(snapshot.Blockers);

            var fileNameOnly = PagePath.GetFileName(targetPageUrl);

            var targetTemplate = snapshot.LibraryBaseTemplate == 101 ? 101 : 119;
            var targetLocation = new ClassicWikiTargetLocationPlan
            {
                TargetWebId = targetWebId,
                TargetWebUrl = targetWebUrl.TrimEnd('/'),
                TargetLibraryServerRelativeUrl = targetLibraryDir,
                TargetLibraryTitle = snapshot.LibraryTitle ?? "Site Pages",
                TargetLibraryTemplate = targetTemplate,
                TargetFolderServerRelativeUrl = targetFolderDir,
                FileName = fileNameOnly
            };

            var rewrittenWikiContent = RewriteWikiContent(
                snapshot.WikiField ?? string.Empty,
                snapshot.Source.WebServerRelativeUrl,
                webServerRelativeUrl);

            var wikiFieldPlan = WikiFieldWritePolicy.Build(rewrittenWikiContent);

            var webPartPlans = new List<ClassicWikiWebPartPlacementPlan>();
            foreach (var wp in snapshot.WebParts)
            {
                webPartPlans.Add(new ClassicWikiWebPartPlacementPlan
                {
                    SourceId = wp.Id,
                    Title = wp.Title ?? wp.TypeName,
                    TypeName = wp.TypeName,
                    ZoneId = wp.ZoneId ?? "Bottom",
                    SourceZoneIndex = wp.ZoneIndex,
                    TargetZoneIndex = wp.ZoneIndex,
                    Hidden = wp.Hidden,
                    Xml = wp.ExportXml
                });
            }

            var dependencyPlans = new List<ClassicWikiDependencyPlan>();
            foreach (var dep in snapshot.Dependencies)
            {
                var targetOriginalValue = RewriteReferenceValue(
                    dep.OriginalValue,
                    snapshot.Source.WebUrl,
                    snapshot.Source.WebServerRelativeUrl,
                    targetWebUrl,
                    webServerRelativeUrl);
                var targetAbsoluteUrl = RewriteReferenceValue(
                    dep.SourceAbsoluteUrl,
                    snapshot.Source.WebUrl,
                    snapshot.Source.WebServerRelativeUrl,
                    targetWebUrl,
                    webServerRelativeUrl);
                var targetServerRelativeUrl = RewriteReferenceValue(
                    dep.SourceServerRelativeUrl,
                    snapshot.Source.WebUrl,
                    snapshot.Source.WebServerRelativeUrl,
                    targetWebUrl,
                    webServerRelativeUrl);
                dependencyPlans.Add(new ClassicWikiDependencyPlan
                {
                    SourceId = dep.Id,
                    Consumer = dep.Consumer,
                    Kind = dep.Kind,
                    SourceOriginalValue = dep.OriginalValue,
                    SourceOriginalUrl = dep.SourceServerRelativeUrl ?? dep.SourceAbsoluteUrl ?? dep.OriginalValue,
                    TargetOriginalValue = targetOriginalValue,
                    TargetAbsoluteUrl = targetAbsoluteUrl,
                    TargetServerRelativeUrl = targetServerRelativeUrl,
                    Disposition = "Rewrite"
                });
            }

            var originalIdentifier = "urn:pnp:spo-wiki-page:v1:" + snapshot.Source.SiteId.ToString("D")
                + ":" + snapshot.Source.WebId.ToString("D") + ":" + snapshot.Source.FileUniqueId.ToString("D");

            var fieldPlan = new ClassicWikiFieldPlan
            {
                Title = snapshot.Source.Title
            };

            if (snapshot.Fields != null)
            {
                foreach (var field in snapshot.Fields)
                {
                    if (string.Equals(field.InternalName, "Title", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(field.Value))
                        {
                            fieldPlan.Title = field.Value;
                        }
                    }
                    else if (!string.Equals(field.InternalName, "WikiField", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(field.InternalName, "FileLeafRef", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(field.InternalName, "ContentTypeId", StringComparison.OrdinalIgnoreCase))
                    {
                        fieldPlan.DeferredFieldNames.Add(field.InternalName);
                        warnings.Add($"Field '{field.InternalName}' is captured but metadata replay is deferred in this classic wiki vertical slice.");
                    }
                }
            }

            var hasUniqueSecurity = snapshot.Security?.HasUniqueRoleAssignments == true;
            var securityPlan = new ClassicWikiSecurityPlan
            {
                HasUniqueRoleAssignments = hasUniqueSecurity,
                Disposition = hasUniqueSecurity ? "Deferred" : "Inherit",
                Reason = hasUniqueSecurity
                    ? "Unique role assignments on classic wiki pages are deferred in this vertical slice; target inherits library permissions."
                    : "Target page inherits permissions from parent library."
            };

            if (hasUniqueSecurity)
            {
                warnings.Add($"Unique permissions on '{snapshot.Source.PageServerRelativeUrl}' are deferred; target page inherits library permissions.");
            }

            var migrationPlan = new ClassicWikiMigrationPlan
            {
                OriginalIdentifier = originalIdentifier,
                SourceSnapshotDigest = exportPackage.SnapshotDigest,
                TargetPageServerRelativeUrl = targetPageUrl,
                TargetLocation = targetLocation,
                WikiFieldPlan = wikiFieldPlan,
                FieldPlan = fieldPlan,
                SecurityPlan = securityPlan,
                WebParts = webPartPlans,
                Dependencies = dependencyPlans,
                LifecyclePolicy = ClassicWikiLifecyclePolicy.Publish,
                Warnings = warnings,
                Blockers = blockers
            };

            var planDigest = ClassicWikiDigest.ComputePlanDigest(migrationPlan);

            var dispositions = new List<string> { "ClassicWikiPage: " + targetPageUrl };
            dispositions.Add("Fields: " + (fieldPlan.DeferredFieldNames.Count > 0 ? "Title materialized; metadata fields deferred" : "Title materialized"));
            dispositions.Add("Security: " + (hasUniqueSecurity ? "Unique permissions deferred (target inherits)" : "Inherited"));

            var report = new ClassicWikiMigrationReport
            {
                Status = blockers.Count > 0 ? "Blocked" : "Ready",
                Dispositions = dispositions,
                Warnings = warnings,
                Blockers = blockers
            };

            var package = new ClassicWikiMigrationPackage
            {
                SchemaVersion = ClassicWikiPackageContract.MigrationSchemaVersion,
                PlannedAtUtc = DateTimeOffset.UtcNow,
                ExportSchemaVersion = exportPackage.SchemaVersion,
                ExportedAtUtc = exportPackage.ExportedAtUtc,
                State = blockers.Count > 0 ? ClassicWikiPackageState.Quarantined : ClassicWikiPackageState.Planned,
                Selection = exportPackage.Selection,
                SelectionDigest = exportPackage.SelectionDigest,
                Snapshot = snapshot,
                Plan = migrationPlan,
                SnapshotDigest = exportPackage.SnapshotDigest,
                PlanDigest = planDigest,
                Report = report
            };

            ClassicWikiPackageValidator.ValidateMigration(package, artifactStore);
            return package;
        }

        private static string RewriteWikiContent(string content, string sourceWebUrl, string targetWebUrl)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(sourceWebUrl) || string.IsNullOrEmpty(targetWebUrl)) return content;
            var src = sourceWebUrl.TrimEnd('/');
            var tgt = targetWebUrl.TrimEnd('/');
            return string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase) ? content : ReplaceCaseInsensitive(content, src + "/", tgt + "/");
        }

        private static string GetRelativeFolder(string sourceFolder, string sourceLibraryPath)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder)
                || string.IsNullOrWhiteSpace(sourceLibraryPath)
                || !PagePath.IsWithin(sourceFolder, sourceLibraryPath))
            {
                return string.Empty;
            }

            return sourceFolder.Substring(sourceLibraryPath.TrimEnd('/').Length).Trim('/');
        }

        private static string RemoveRelativeFolder(string targetFolder, string relativeFolder)
        {
            if (string.IsNullOrWhiteSpace(relativeFolder))
            {
                return targetFolder;
            }

            var suffix = "/" + relativeFolder.Trim('/');
            return targetFolder.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? targetFolder.Substring(0, targetFolder.Length - suffix.Length)
                : targetFolder;
        }

        private static string CombineServerRelative(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(right))
            {
                return string.IsNullOrWhiteSpace(left) ? "/" : left.TrimEnd('/');
            }

            var prefix = string.IsNullOrWhiteSpace(left) || left == "/"
                ? string.Empty
                : left.TrimEnd('/');
            return prefix + "/" + right.Trim('/');
        }

        private static string RewriteUrl(string url, string sourceWebUrl, string targetWebUrl)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(sourceWebUrl) || string.IsNullOrEmpty(targetWebUrl)) return url;
            var src = sourceWebUrl.TrimEnd('/');
            var tgt = targetWebUrl.TrimEnd('/');
            return url.StartsWith(src + "/", StringComparison.OrdinalIgnoreCase) ? tgt + "/" + url.Substring(src.Length + 1) : url;
        }

        private static string RewriteReferenceValue(
            string value,
            string sourceWebUrl,
            string sourceWebServerRelativeUrl,
            string targetWebUrl,
            string targetWebServerRelativeUrl)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var rewritten = RewriteUrl(value, sourceWebServerRelativeUrl, targetWebServerRelativeUrl);
            if (!string.Equals(rewritten, value, StringComparison.Ordinal))
            {
                return rewritten;
            }

            return RewriteUrl(value, sourceWebUrl, targetWebUrl);
        }

        private static string ReplaceCaseInsensitive(string input, string pattern, string replacement)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern)) return input;
            return System.Text.RegularExpressions.Regex.Replace(
                input,
                System.Text.RegularExpressions.Regex.Escape(pattern),
                (replacement ?? string.Empty).Replace("$", "$$"),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
