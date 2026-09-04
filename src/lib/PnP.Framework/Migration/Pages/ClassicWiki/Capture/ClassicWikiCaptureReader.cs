using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Capture
{
    internal static class ClassicWikiCaptureReader
    {
        public static CapturedClassicWikiPage Read(
            ClientContext context,
            string pagePath,
            PageCaptureOptions options,
            IMigrationArtifactStore artifactStore,
            ICollection<string> blockers,
            ICollection<string> warnings)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(pagePath)) throw new ArgumentException("Page path is required.", nameof(pagePath));

            var web = context.Web;
            var site = context.Site;
            var file = web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(pagePath));
            var item = file.ListItemAllFields;
            var parentList = item.ParentList;
            var contentType = item.ContentType;

            context.Load(site, value => value.Id);
            context.Load(web, value => value.Id, value => value.Url, value => value.ServerRelativeUrl);
            context.Load(file,
                value => value.Exists,
                value => value.Name,
                value => value.UniqueId,
                value => value.ServerRelativeUrl,
                value => value.UIVersionLabel,
                value => value.Length,
                value => value.TimeLastModified,
                value => value.CheckOutType,
                value => value.Level,
                value => value.TimeCreated);
            context.Load(item);
            context.Load(contentType, value => value.Id, value => value.Name);
            context.Load(parentList,
                value => value.Id,
                value => value.Title,
                value => value.BaseTemplate,
                value => value.RootFolder.ServerRelativeUrl);

            try
            {
                context.ExecuteQueryRetry();
            }
            catch (ServerException ex) when (ex.ServerErrorTypeName == "System.IO.FileNotFoundException" || ex.ServerErrorCode == -2147024894)
            {
                throw new FileNotFoundException("The source classic wiki page was not found.", pagePath);
            }

            if (!file.Exists)
            {
                throw new FileNotFoundException("The source classic wiki page was not found.", pagePath);
            }

            context.Load(item, value => value.Id, value => value.HasUniqueRoleAssignments);
            context.ExecuteQueryRetry();

            var wikiField = GetFieldString(item, "WikiField") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(wikiField))
            {
                warnings.Add("WikiField content is empty.");
            }

            var pageArtifact = PageArtifactSnapshotReader.Read(context, file, artifactStore, blockers);

            var identity = new PageIdentity
            {
                SiteId = site.Id,
                WebId = web.Id,
                WebUrl = web.Url.TrimEnd('/'),
                WebServerRelativeUrl = web.ServerRelativeUrl,
                PageServerRelativeUrl = file.ServerRelativeUrl,
                ListItemId = item.Id,
                FileUniqueId = file.UniqueId,
                ContentTypeId = contentType?.Id?.StringValue ?? "0x010108",
                ContentTypeName = contentType?.Name ?? "Wiki Page",
                VersionLabel = file.UIVersionLabel,
                Length = file.Length,
                ModifiedUtc = file.TimeLastModified.ToUniversalTime(),
                Title = GetFieldString(item, "Title") ?? PagePath.GetFileName(pagePath)
            };

            return new CapturedClassicWikiPage
            {
                Identity = identity,
                PageArtifact = pageArtifact,
                WikiField = wikiField,
                LibraryBaseTemplate = parentList.BaseTemplate,
                LibraryTitle = parentList.Title,
                LibraryServerRelativeUrl = parentList.RootFolder.ServerRelativeUrl,
                Fields = PageFieldSnapshotReader.Read(context, item, warnings),
                WebParts = options.IncludeWebParts
                    ? ClassicWebPartSnapshotReader.Read(web, pagePath, blockers)
                    : new List<ClassicWebPartSnapshot>(),
                Security = PageSecuritySnapshotReader.Read(context, item, warnings),
                Lifecycle = new PageLifecycleSnapshot
                {
                    CheckOutType = file.CheckOutType.ToString(),
                    Level = file.Level.ToString(),
                    ModerationStatus = TryGetInt32(item, "_ModerationStatus"),
                    CreatedUtc = file.TimeCreated.ToUniversalTime(),
                    ModifiedUtc = file.TimeLastModified.ToUniversalTime()
                },
                SourceFence = SourcePageFenceReader.FromFile(file)
            };
        }

        internal static string GetFieldString(ListItem item, string internalName)
        {
            return item.FieldValues.TryGetValue(internalName, out var value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;
        }

        internal static int? TryGetInt32(ListItem item, string internalName)
        {
            if (!item.FieldValues.TryGetValue(internalName, out var value) || value == null)
            {
                return null;
            }

            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : (int?)null;
        }
    }
}
