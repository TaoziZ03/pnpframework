using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Test.ClassicWiki
{
    internal static class ClassicWikiTestFactory
    {
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
                LibraryServerRelativeUrl = "/sites/demo/SitePages",
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
    }
}
