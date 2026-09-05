using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal sealed class ClassicWikiTargetLocation
    {
        public List TargetLibrary { get; set; }

        public Folder TargetFolder { get; set; }

        public string FileName { get; set; }

        public int LibraryTemplate { get; set; }
    }

    internal static class ClassicWikiTargetLocationMaterializer
    {
        public static ClassicWikiTargetLocation Ensure(
            ClientContext context,
            ClassicWikiMigrationPackage package,
            MigrationExecutionRecorder recorder)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (package?.Plan?.TargetLocation == null) throw new InvalidDataException("Target location plan is required.");

            var planLoc = package.Plan.TargetLocation;
            var web = context.Web;
            context.Load(web, w => w.Id, w => w.Url, w => w.ServerRelativeUrl);
            context.ExecuteQueryRetry();

            if (planLoc.TargetWebId == Guid.Empty
                || web.Id != planLoc.TargetWebId
                || !SameAbsoluteUrl(web.Url, planLoc.TargetWebUrl))
            {
                throw new InvalidOperationException(
                    $"The supplied target context is not the sealed target Web. Expected '{planLoc.TargetWebUrl}' ({planLoc.TargetWebId:D}); observed '{web.Url}' ({web.Id:D}).");
            }

            var libraryPath = planLoc.TargetLibraryServerRelativeUrl.TrimEnd('/');
            List list = null;
            try
            {
                list = web.GetList(libraryPath);
                context.Load(list, l => l.Id, l => l.Title, l => l.BaseTemplate, l => l.RootFolder.ServerRelativeUrl);
                context.ExecuteQueryRetry();
            }
            catch (ServerException exception) when (IsMissingLibrary(exception))
            {
                // Library does not exist, create it based on modeled template
                var templateType = planLoc.TargetLibraryTemplate == 101 ? ListTemplateType.DocumentLibrary : ListTemplateType.WebPageLibrary;
                list = recorder.Execute(
                    "library.ensure",
                    $"Create target wiki library '{planLoc.TargetLibraryTitle}' with template {planLoc.TargetLibraryTemplate}.",
                    () => web.CreateList(templateType, planLoc.TargetLibraryTitle, false),
                    value => MutationOutcome.Applied,
                    value => $"Created target wiki library '{planLoc.TargetLibraryTitle}'.");
                context.Load(list, l => l.Id, l => l.Title, l => l.BaseTemplate, l => l.RootFolder.ServerRelativeUrl);
                context.ExecuteQueryRetry();
            }

            if (list.BaseTemplate != planLoc.TargetLibraryTemplate
                || !string.Equals(
                    list.RootFolder.ServerRelativeUrl?.TrimEnd('/'),
                    planLoc.TargetLibraryServerRelativeUrl?.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Target library identity differs from the sealed plan. Expected template/path '{planLoc.TargetLibraryTemplate}:{planLoc.TargetLibraryServerRelativeUrl}'; observed '{list.BaseTemplate}:{list.RootFolder.ServerRelativeUrl}'.");
            }

            var rootFolderUrl = list.RootFolder.ServerRelativeUrl.TrimEnd('/');
            var targetFolderUrl = (planLoc.TargetFolderServerRelativeUrl ?? rootFolderUrl).TrimEnd('/');

            Folder targetFolder = null;
            if (string.Equals(targetFolderUrl, rootFolderUrl, StringComparison.OrdinalIgnoreCase))
            {
                targetFolder = list.RootFolder;
                recorder?.RecordAlreadySatisfied("folder.ensure", "Target page is in library root folder.");
            }
            else
            {
                // Ensure nested subfolders under library
                var webRelDir = targetFolderUrl.StartsWith(web.ServerRelativeUrl, StringComparison.OrdinalIgnoreCase)
                    ? targetFolderUrl.Substring(web.ServerRelativeUrl.Length).TrimStart('/')
                    : targetFolderUrl.TrimStart('/');

                targetFolder = recorder.Execute(
                    "folder.ensure",
                    $"Ensure nested folder path '{targetFolderUrl}'.",
                    () => web.EnsureFolderPath(webRelDir, f => f.ServerRelativeUrl),
                    value => MutationOutcome.Applied,
                    value => $"Ensured nested folder path '{targetFolderUrl}'.");
            }

            return new ClassicWikiTargetLocation
            {
                TargetLibrary = list,
                TargetFolder = targetFolder,
                FileName = planLoc.FileName,
                LibraryTemplate = list.BaseTemplate
            };
        }


        private static bool SameAbsoluteUrl(string left, string right)
        {
            return Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
                && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
                && string.Equals(
                    leftUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
                    rightUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMissingLibrary(ServerException exception)
        {
            return exception != null
                && (exception.ServerErrorCode == -2147024809
                    && string.Equals(exception.ServerErrorTypeName, "System.ArgumentException", StringComparison.Ordinal)
                    || exception.ServerErrorCode == -2147024894
                    && string.Equals(exception.ServerErrorTypeName, "System.IO.FileNotFoundException", StringComparison.Ordinal));
        }
    }
}
