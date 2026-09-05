using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
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
                LoadLibrary(context, list);
                context.ExecuteQueryRetry();
            }
            catch (ServerException exception) when (IsMissingLibrary(exception))
            {
                list = recorder.Execute(
                    "library.ensure",
                    $"Create target wiki library '{planLoc.TargetLibraryTitle}' with template {planLoc.TargetLibraryTemplate}.",
                    () =>
                    {
                        var created = web.Lists.Add(BuildCreationInformation(planLoc, web.ServerRelativeUrl));
                        if (package.Plan.LifecyclePolicy == ClassicWikiLifecyclePolicy.Publish)
                        {
                            created.EnableVersioning = true;
                        }
                        created.Update();
                        context.ExecuteQueryRetry();
                        return created;
                    },
                    value => MutationOutcome.Applied,
                    value => $"Created target wiki library '{planLoc.TargetLibraryTitle}'.");
                LoadLibrary(context, list);
                context.ExecuteQueryRetry();
            }

            ValidateLoadedLibrary(
                planLoc,
                package.Plan.LifecyclePolicy,
                list.BaseTemplate,
                list.RootFolder.ServerRelativeUrl,
                list.EnableVersioning);

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

        internal static ListCreationInformation BuildCreationInformation(
            ClassicWikiTargetLocationPlan plan,
            string targetWebServerRelativeUrl)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var webPath = (targetWebServerRelativeUrl ?? string.Empty).TrimEnd('/');
            var libraryPath = plan.TargetLibraryServerRelativeUrl.TrimEnd('/');
            if (!PagePath.IsWithin(libraryPath, webPath))
            {
                throw new InvalidDataException("The sealed library path is outside the target Web.");
            }
            var relativeUrl = libraryPath.Substring(webPath.Length).TrimStart('/');
            if (string.IsNullOrWhiteSpace(relativeUrl))
            {
                throw new InvalidDataException("A sealed library URL below the target Web is required.");
            }

            return new ListCreationInformation
            {
                Title = plan.TargetLibraryTitle,
                Url = relativeUrl,
                TemplateType = plan.TargetLibraryTemplate
            };
        }

        private static void LoadLibrary(ClientContext context, List list)
        {
            context.Load(
                list,
                value => value.Id,
                value => value.Title,
                value => value.BaseTemplate,
                value => value.ForceCheckout,
                value => value.EnableVersioning,
                value => value.EnableMinorVersions,
                value => value.EnableModeration,
                value => value.RootFolder.ServerRelativeUrl);
        }

        internal static void ValidateLoadedLibrary(
            ClassicWikiTargetLocationPlan plan,
            ClassicWikiLifecyclePolicy lifecyclePolicy,
            int actualTemplate,
            string actualServerRelativeUrl,
            bool enableVersioning)
        {
            if (actualTemplate != plan.TargetLibraryTemplate
                || !string.Equals(
                    actualServerRelativeUrl?.TrimEnd('/'),
                    plan.TargetLibraryServerRelativeUrl?.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Target library identity differs from the sealed plan. Expected template/path '{plan.TargetLibraryTemplate}:{plan.TargetLibraryServerRelativeUrl}'; observed '{actualTemplate}:{actualServerRelativeUrl}'.");
            }
            if (lifecyclePolicy == ClassicWikiLifecyclePolicy.Publish && !enableVersioning)
            {
                throw new InvalidOperationException(
                    $"Target library '{actualServerRelativeUrl}' does not support the sealed Publish lifecycle because versioning is disabled.");
            }
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
