using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.WebParts;
using PnP.Framework.Migration.Pages.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    public sealed class CsomClassicPageMutationPort : IClassicPageMutationPort
    {
        private readonly ClientContext context;

        public CsomClassicPageMutationPort(ClientContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task<ClassicWikiTargetPageState> ReadPageAsync(
            ClassicWikiTargetPageSpec target,
            CancellationToken cancellationToken = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var web = context.Web;
            var file = web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(target.TargetPageServerRelativeUrl));
            context.Load(file, f => f.Exists, f => f.UniqueId);

            var exists = false;
            try
            {
                context.ExecuteQueryRetry();
                exists = file.Exists;
            }
            catch (ServerException ex) when (IsMissing(ex))
            {
                exists = false;
            }

            if (!exists)
            {
                return Task.FromResult(new ClassicWikiTargetPageState(false, null, null, Array.Empty<ClassicWikiTargetWebPartState>()));
            }

            var item = file.ListItemAllFields;
            context.Load(item);
            context.ExecuteQueryRetry();

            var wikiField = item.FieldValues.TryGetValue("WikiField", out var val) ? val as string : null;
            var webParts = new List<ClassicWikiTargetWebPartState>();

            try
            {
                var wpm = file.GetLimitedWebPartManager(PersonalizationScope.Shared);
                var query = context.LoadQuery(wpm.WebParts.Include(wp => wp.Id, wp => wp.ZoneId, wp => wp.WebPart.Title, wp => wp.WebPart.ZoneIndex));
                context.ExecuteQueryRetry();

                foreach (var def in query)
                {
                    webParts.Add(new ClassicWikiTargetWebPartState(
                        def.Id,
                        def.WebPart?.Title ?? "WebPart",
                        def.ZoneId,
                        def.WebPart?.ZoneIndex ?? 0,
                        string.Empty));
                }
            }
            catch
            {
                // Web parts query optional
            }

            return Task.FromResult(new ClassicWikiTargetPageState(true, file.UniqueId, wikiField, webParts));
        }

        public Task<(ClassicWikiTargetPageState State, IReadOnlyList<string> ExchangeIds)> EnsurePageAsync(
            ClassicWikiTargetPageSpec target,
            CancellationToken cancellationToken = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var web = context.Web;
            var targetDir = PagePath.GetDirectoryName(target.TargetPageServerRelativeUrl);
            var fileName = PagePath.GetFileName(target.TargetPageServerRelativeUrl);

            var folder = web.GetFolderByServerRelativePath(ResourcePath.FromDecodedUrl(targetDir));
            var newFile = folder.Files.AddTemplateFile(target.TargetPageServerRelativeUrl, TemplateFileType.WikiPage);
            context.Load(newFile, f => f.Exists, f => f.UniqueId, f => f.ServerRelativeUrl);
            context.ExecuteQueryRetry();

            var exchangeId = Guid.NewGuid().ToString("N");
            var state = new ClassicWikiTargetPageState(newFile.Exists, newFile.UniqueId, string.Empty, Array.Empty<ClassicWikiTargetWebPartState>());
            return Task.FromResult((state, (IReadOnlyList<string>)new[] { exchangeId }));
        }

        public Task<(ClassicWikiTargetPageState State, IReadOnlyList<string> ExchangeIds)> WriteWikiFieldAsync(
            ClassicWikiTargetPageSpec target,
            string value,
            CancellationToken cancellationToken = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var web = context.Web;
            var file = web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(target.TargetPageServerRelativeUrl));
            var item = file.ListItemAllFields;
            context.Load(file, f => f.Exists, f => f.UniqueId);
            context.Load(item);
            context.ExecuteQueryRetry();

            item["WikiField"] = value;
            item.Update();
            context.ExecuteQueryRetry();

            // Readback
            context.Load(item);
            context.ExecuteQueryRetry();
            var readback = item.FieldValues.TryGetValue("WikiField", out var val) ? val as string : null;

            var exchangeId = Guid.NewGuid().ToString("N");
            var state = new ClassicWikiTargetPageState(file.Exists, file.UniqueId, readback, Array.Empty<ClassicWikiTargetWebPartState>());
            return Task.FromResult((state, (IReadOnlyList<string>)new[] { exchangeId }));
        }

        public Task<(ClassicWikiTargetWebPartState State, IReadOnlyList<string> ExchangeIds)> EnsureWebPartAsync(
            ClassicWikiTargetPageSpec target,
            string webPartXml,
            string zoneId,
            int zoneIndex,
            CancellationToken cancellationToken = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var web = context.Web;
            var file = web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(target.TargetPageServerRelativeUrl));
            var wpm = file.GetLimitedWebPartManager(PersonalizationScope.Shared);
            var def = wpm.ImportWebPart(webPartXml);
            var added = wpm.AddWebPart(def.WebPart, zoneId, zoneIndex);
            context.Load(added, a => a.Id, a => a.ZoneId);
            context.ExecuteQueryRetry();

            var exchangeId = Guid.NewGuid().ToString("N");
            var state = new ClassicWikiTargetWebPartState(added.Id, "WebPart", added.ZoneId, zoneIndex, PageDigest.ComputeSha256(webPartXml));
            return Task.FromResult((state, (IReadOnlyList<string>)new[] { exchangeId }));
        }

        public Task<(ClassicWikiTargetWebPartState State, IReadOnlyList<string> ExchangeIds)> MoveAndSaveWebPartAsync(
            ClassicWikiTargetPageSpec target,
            Guid targetWebPartId,
            string zoneId,
            int zoneIndex,
            CancellationToken cancellationToken = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var web = context.Web;
            var file = web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(target.TargetPageServerRelativeUrl));
            var wpm = file.GetLimitedWebPartManager(PersonalizationScope.Shared);
            var def = wpm.WebParts.GetById(targetWebPartId);
            context.Load(def);
            context.ExecuteQueryRetry();
            def.SaveWebPartChanges();
            context.ExecuteQueryRetry();

            var exchangeId = Guid.NewGuid().ToString("N");
            var state = new ClassicWikiTargetWebPartState(targetWebPartId, "WebPart", zoneId, zoneIndex, string.Empty);
            return Task.FromResult((state, (IReadOnlyList<string>)new[] { exchangeId }));
        }

        private static bool IsMissing(ServerException ex)
        {
            return string.Equals(ex.ServerErrorTypeName, "System.IO.FileNotFoundException", StringComparison.Ordinal)
                || ex.ServerErrorCode == -2147024894;
        }
    }
}
