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
            if (exportPackage == null) throw new ArgumentNullException(nameof(exportPackage));
            if (options == null) throw new ArgumentNullException(nameof(options));

            ClassicWikiPackageValidator.ValidateExport(exportPackage, artifactStore);

            var targetWeb = targetContext.Web;
            targetContext.Load(targetWeb, w => w.Url, w => w.ServerRelativeUrl, w => w.Title);
            targetContext.ExecuteQueryRetry();

            var snapshot = exportPackage.Snapshot;
            var targetPageUrl = options.TargetPageServerRelativeUrl;
            if (string.IsNullOrWhiteSpace(targetPageUrl))
            {
                var libraryName = string.IsNullOrWhiteSpace(snapshot.LibraryTitle) ? "SitePages" : snapshot.LibraryTitle;
                var fileName = PagePath.GetFileName(snapshot.Source.PageServerRelativeUrl);
                targetPageUrl = PagePath.Normalize(targetWeb.ServerRelativeUrl, fileName, libraryName);
            }
            else
            {
                targetPageUrl = PagePath.Normalize(targetWeb.ServerRelativeUrl, targetPageUrl, "SitePages");
            }

            var warnings = new List<string>(snapshot.Warnings);
            var blockers = new List<string>(snapshot.Blockers);

            var targetLibraryDir = PagePath.GetDirectoryName(targetPageUrl);
            var fileNameOnly = PagePath.GetFileName(targetPageUrl);

            var targetTemplate = snapshot.LibraryBaseTemplate == 101 ? 101 : 119;
            var targetLocation = new ClassicWikiTargetLocationPlan
            {
                TargetWebUrl = targetWeb.Url.TrimEnd('/'),
                TargetLibraryServerRelativeUrl = targetLibraryDir,
                TargetLibraryTitle = snapshot.LibraryTitle ?? "Site Pages",
                TargetLibraryTemplate = targetTemplate,
                TargetFolderServerRelativeUrl = targetLibraryDir,
                FileName = fileNameOnly
            };

            var rewrittenWikiContent = RewriteWikiContent(
                snapshot.WikiField ?? string.Empty,
                snapshot.Source.WebServerRelativeUrl,
                targetWeb.ServerRelativeUrl);

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
                    Xml = wp.ExportXml
                });
            }

            var dependencyPlans = new List<ClassicWikiDependencyPlan>();
            foreach (var dep in snapshot.Dependencies)
            {
                dependencyPlans.Add(new ClassicWikiDependencyPlan
                {
                    SourceOriginalUrl = dep.SourceServerRelativeUrl ?? dep.SourceAbsoluteUrl ?? dep.OriginalValue,
                    TargetServerRelativeUrl = dep.SourceServerRelativeUrl != null && targetWeb.ServerRelativeUrl != null
                        ? RewriteUrl(dep.SourceServerRelativeUrl, snapshot.Source.WebServerRelativeUrl, targetWeb.ServerRelativeUrl)
                        : dep.SourceServerRelativeUrl,
                    Disposition = "Rewrite"
                });
            }

            var originalIdentifier = "urn:pnp:spo-wiki-page:v1:" + snapshot.Source.SiteId.ToString("D")
                + ":" + snapshot.Source.WebId.ToString("D") + ":" + snapshot.Source.FileUniqueId.ToString("D");

            var migrationPlan = new ClassicWikiMigrationPlan
            {
                OriginalIdentifier = originalIdentifier,
                SourceSnapshotDigest = exportPackage.SnapshotDigest,
                TargetPageServerRelativeUrl = targetPageUrl,
                TargetLocation = targetLocation,
                WikiFieldPlan = wikiFieldPlan,
                WebParts = webPartPlans,
                Dependencies = dependencyPlans,
                LifecyclePolicy = "Publish",
                Warnings = warnings,
                Blockers = blockers
            };

            var planDigest = ClassicWikiDigest.ComputePlanDigest(migrationPlan);

            var report = new ClassicWikiMigrationReport
            {
                Status = blockers.Count > 0 ? "Blocked" : "Ready",
                Dispositions = new List<string> { "ClassicWikiPage: " + targetPageUrl },
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
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(sourceWebUrl) || string.IsNullOrEmpty(targetWebUrl))
            {
                return content;
            }

            var src = sourceWebUrl.TrimEnd('/');
            var tgt = targetWebUrl.TrimEnd('/');
            if (string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase))
            {
                return content;
            }

            return content.Replace(src + "/", tgt + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string RewriteUrl(string url, string sourceWebUrl, string targetWebUrl)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(sourceWebUrl) || string.IsNullOrEmpty(targetWebUrl))
            {
                return url;
            }

            var src = sourceWebUrl.TrimEnd('/');
            var tgt = targetWebUrl.TrimEnd('/');
            if (url.StartsWith(src + "/", StringComparison.OrdinalIgnoreCase))
            {
                return tgt + "/" + url.Substring(src.Length + 1);
            }

            return url;
        }
    }
}
