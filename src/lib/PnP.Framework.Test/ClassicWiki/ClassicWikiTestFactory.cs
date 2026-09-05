using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Test.ClassicWiki
{
    internal static class ClassicWikiTestFactory
    {
        public static readonly Guid TargetWebId = new Guid("11111111-2222-3333-4444-555555555555");

        public static ClassicWikiExportPackage CreatePackage(string content, int libraryTemplate, string pageUrl = "/sites/demo/SitePages/Welcome.aspx")
        {
            var bundle = CreateSampleBundle(content, libraryTemplate, pageUrl);
            return new ClassicWikiExportPackage
            {
                SchemaVersion = ClassicWikiPackageContract.ExportSchemaVersion,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = new ClassicWikiWorkflowSelection(),
                SelectionDigest = "sel_digest",
                Snapshot = bundle,
                SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(bundle)
            };
        }

        public static ClassicWikiCaptureBundle CreateSampleBundle(string content, int libraryTemplate, string pageUrl = "/sites/demo/SitePages/Welcome.aspx")
        {
            if (libraryTemplate == 101
                && string.Equals(pageUrl, "/sites/demo/SitePages/Welcome.aspx", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = "/sites/demo/Documents/Welcome.aspx";
            }

            return new ClassicWikiCaptureBundle
            {
                Source = new PageIdentity
                {
                    SiteId = Guid.NewGuid(),
                    WebId = Guid.NewGuid(),
                    WebUrl = "https://contoso.sharepoint.com/sites/demo",
                    WebServerRelativeUrl = "/sites/demo",
                    PageServerRelativeUrl = pageUrl,
                    ListItemId = 1,
                    FileUniqueId = Guid.NewGuid(),
                    ContentTypeId = "0x010108",
                    ContentTypeName = "Wiki Page",
                    VersionLabel = "1.0",
                    Title = PagePath.GetFileName(pageUrl)
                },
                PageArtifact = new PageArtifactSnapshot
                {
                    PageDirective = new PageDirectiveSnapshot
                    {
                        Inherits = "Microsoft.SharePoint.WebPartPages.WikiEditPage, Microsoft.SharePoint"
                    }
                },
                Runtime = new PageRuntimeSnapshot
                {
                    AdapterId = PageRuntimeAdapterIds.Wiki,
                    ResolutionState = PageRuntimeResolutionState.Resolved
                },
                WikiField = content,
                WikiFieldSha256 = ClassicWikiDigest.ComputeSha256(content ?? string.Empty),
                LibraryBaseTemplate = libraryTemplate,
                LibraryTitle = libraryTemplate == 119 ? "Site Pages" : "Documents",
                LibraryServerRelativeUrl = libraryTemplate == 119 ? "/sites/demo/SitePages" : "/sites/demo/Documents",
                Fields = new List<PageFieldValueSnapshot>
                {
                    new PageFieldValueSnapshot { InternalName = "Title", Value = "Test Page" },
                    new PageFieldValueSnapshot { InternalName = "FileLeafRef", Value = PagePath.GetFileName(pageUrl) }
                },
                Lifecycle = new PageLifecycleSnapshot
                {
                    CheckOutType = "None",
                    Level = "Published",
                    CreatedUtc = DateTime.UtcNow.AddDays(-10),
                    ModifiedUtc = DateTime.UtcNow
                },
                Security = new PageSecuritySnapshot
                {
                    HasUniqueRoleAssignments = false
                }
            };
        }

        public static ClassicWikiMigrationPackage CreateMigrationPackage(
            int libraryTemplate = 119,
            bool deferredField = false,
            bool deferredSecurity = false)
        {
            var source = CreatePackage("approved content", libraryTemplate);
            if (deferredField)
            {
                source.Snapshot.Fields.Add(new PageFieldValueSnapshot
                {
                    InternalName = "DeferredMetadata",
                    Value = "source-only"
                });
            }
            if (deferredSecurity)
            {
                source.Snapshot.Security = new PageSecuritySnapshot { HasUniqueRoleAssignments = true };
            }
            source.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(source.Snapshot);

            var libraryTitle = libraryTemplate == 119 ? "Site Pages" : "Documents";
            var libraryPath = libraryTemplate == 119 ? "/sites/target/SitePages" : "/sites/target/Documents";
            var plan = new ClassicWikiMigrationPlan
            {
                OriginalIdentifier = "urn:pnp:spo-wiki-page:v1:source",
                SourceSnapshotDigest = source.SnapshotDigest,
                TargetPageServerRelativeUrl = libraryPath + "/Welcome.aspx",
                TargetLocation = new ClassicWikiTargetLocationPlan
                {
                    TargetWebId = TargetWebId,
                    TargetWebUrl = "https://contoso.sharepoint.com/sites/target",
                    TargetLibraryServerRelativeUrl = libraryPath,
                    TargetLibraryTitle = libraryTitle,
                    TargetLibraryTemplate = libraryTemplate,
                    TargetFolderServerRelativeUrl = libraryPath,
                    FileName = "Welcome.aspx"
                },
                WikiFieldPlan = WikiFieldWritePolicy.Build("approved content"),
                FieldPlan = new ClassicWikiFieldPlan
                {
                    Title = "Test Page",
                    DeferredFieldNames = deferredField
                        ? new List<string> { "DeferredMetadata" }
                        : new List<string>()
                },
                SecurityPlan = new ClassicWikiSecurityPlan
                {
                    HasUniqueRoleAssignments = deferredSecurity,
                    Disposition = deferredSecurity ? "Deferred" : "Inherit"
                },
                LifecyclePolicy = "Publish"
            };
            var package = new ClassicWikiMigrationPackage
            {
                SchemaVersion = ClassicWikiPackageContract.MigrationSchemaVersion,
                ExportSchemaVersion = source.SchemaVersion,
                ExportedAtUtc = source.ExportedAtUtc,
                PlannedAtUtc = DateTimeOffset.UtcNow,
                Selection = source.Selection,
                SelectionDigest = source.SelectionDigest,
                Snapshot = source.Snapshot,
                SnapshotDigest = source.SnapshotDigest,
                Plan = plan,
                Report = new ClassicWikiMigrationReport { Status = "Ready" }
            };
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(plan);
            return package;
        }

        public static ClassicWikiFreshTargetEvidence CreateFreshEvidence(ClassicWikiMigrationPackage package)
        {
            var location = package.Plan.TargetLocation;
            var target = CreatePackage(
                package.Plan.WikiFieldPlan.ExactValue,
                location.TargetLibraryTemplate,
                package.Plan.TargetPageServerRelativeUrl);
            target.Snapshot.Source.WebId = location.TargetWebId;
            target.Snapshot.Source.WebUrl = location.TargetWebUrl;
            target.Snapshot.Source.WebServerRelativeUrl = "/sites/target";
            target.Snapshot.Source.ContentTypeId = ClassicWikiPackageContract.DefaultContentTypeId;
            target.Snapshot.Source.ContentTypeName = ClassicWikiPackageContract.DefaultContentTypeName;
            target.Snapshot.Source.Title = package.Plan.FieldPlan.Title;
            target.Snapshot.LibraryBaseTemplate = location.TargetLibraryTemplate;
            target.Snapshot.LibraryTitle = location.TargetLibraryTitle;
            target.Snapshot.LibraryServerRelativeUrl = location.TargetLibraryServerRelativeUrl;
            var title = target.Snapshot.Fields.First(value => value.InternalName == "Title");
            title.Value = package.Plan.FieldPlan.Title;
            target.Snapshot.Lifecycle = new PageLifecycleSnapshot
            {
                CheckOutType = "None",
                Level = "Published",
                ModerationStatus = package.Snapshot.Lifecycle.ModerationStatus
            };
            target.Snapshot.Security = new PageSecuritySnapshot { HasUniqueRoleAssignments = false };
            target.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(target.Snapshot);

            return new ClassicWikiFreshTargetEvidence
            {
                Recapture = target,
                IndependentContext = true,
                FileProperties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [ClassicWikiTargetOwnership.OriginalIdentifierPropertyName] = package.Plan.OriginalIdentifier,
                    [ClassicWikiTargetOwnership.SourceSnapshotDigestPropertyName] = package.SnapshotDigest,
                    [ClassicWikiTargetOwnership.PlanDigestPropertyName] = package.PlanDigest
                }
            };
        }
    }
}
