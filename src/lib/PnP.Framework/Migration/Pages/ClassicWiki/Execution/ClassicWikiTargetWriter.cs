using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.WebParts;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal static class ClassicWikiTargetWriter
    {
        public static ClassicWikiWriteResult Write(
            ClientContext targetContext,
            ClassicWikiMigrationPackage package,
            MigrationExecutionRecorder recorder,
            ICollection<string> warnings)
        {
            if (targetContext == null) throw new ArgumentNullException(nameof(targetContext));
            if (package == null) throw new ArgumentNullException(nameof(package));

            var targetWeb = targetContext.Web;
            var targetLocation = ClassicWikiTargetLocationMaterializer.Ensure(
                targetContext,
                package,
                recorder);

            var targetFile = targetWeb.GetFileByServerRelativePath(
                ResourcePath.FromDecodedUrl(package.Plan.TargetPageServerRelativeUrl));
            targetContext.Load(targetFile, f => f.Exists, f => f.Properties, f => f.CheckOutType);

            var resumeOwnedPage = false;
            try
            {
                targetContext.ExecuteQueryRetry();
                resumeOwnedPage = targetFile.Exists;
            }
            catch (ServerException ex) when (IsMissing(ex))
            {
                resumeOwnedPage = false;
            }

            if (resumeOwnedPage)
            {
                if (!ClassicWikiTargetOwnership.MatchesApprovedPlan(
                    targetFile.Properties.FieldValues,
                    package.Plan.OriginalIdentifier,
                    package.SnapshotDigest,
                    package.PlanDigest))
                {
                    throw new InvalidOperationException(
                        $"The approved exact page path is occupied by a target that is not owned by this sealed plan: '{package.Plan.TargetPageServerRelativeUrl}'.");
                }
                recorder.RecordAlreadySatisfied(
                    "page.create",
                    $"Resume the exact migration-owned classic wiki page '{package.Plan.TargetPageServerRelativeUrl}' under the same sealed plan.");
            }
            else
            {
                recorder.Execute<Microsoft.SharePoint.Client.File>(
                    "page.create",
                    $"Create classic wiki page '{package.Plan.TargetPageServerRelativeUrl}'.",
                    () =>
                    {
                        var newFile = targetLocation.TargetFolder.Files.AddTemplateFile(
                            targetLocation.FileName,
                            TemplateFileType.WikiPage);
                        targetContext.Load(newFile, f => f.Exists, f => f.ServerRelativeUrl);
                        targetContext.ExecuteQueryRetry();
                        return newFile;
                    },
                    value => MutationOutcome.Applied,
                    value => $"Created classic wiki page '{package.Plan.TargetPageServerRelativeUrl}'.");
            }

            targetFile = targetWeb.GetFileByServerRelativePath(
                ResourcePath.FromDecodedUrl(package.Plan.TargetPageServerRelativeUrl));
            var targetItem = targetFile.ListItemAllFields;
            targetContext.Load(targetFile, f => f.Exists, f => f.CheckOutType, f => f.Properties);
            targetContext.Load(targetItem, i => i.Id);
            targetContext.ExecuteQueryRetry();

            if (!targetFile.Exists)
            {
                throw new InvalidOperationException(
                    $"SharePoint did not create the classic wiki page at '{package.Plan.TargetPageServerRelativeUrl}'.");
            }

            if (resumeOwnedPage)
            {
                targetContext.Load(targetItem);
                targetContext.ExecuteQueryRetry();
                var resumedContent = targetItem.FieldValues.TryGetValue("WikiField", out var val) ? val as string ?? string.Empty : string.Empty;
                var resumedSha = PageDigest.ComputeSha256(resumedContent);

                var webPartCount = 0;
                try
                {
                    var webParts = targetWeb.GetWebParts(package.Plan.TargetPageServerRelativeUrl);
                    webPartCount = webParts != null ? webParts.Count() : 0;
                }
                catch
                {
                    webPartCount = 0;
                }

                recorder.RecordAlreadySatisfied("wiki-field.write", "Stored WikiField verified on resumed page.");
                recorder.RecordAlreadySatisfied("page.webparts", "Web parts verified on resumed page.");
                recorder.RecordAlreadySatisfied("page.ownership", "Target page carries matching provenance properties.");
                return new ClassicWikiWriteResult
                {
                    TargetLibrary = targetLocation.TargetLibrary,
                    TargetFile = targetFile,
                    TargetItem = targetItem,
                    ResumedExistingOwnedPage = true,
                    PersistedWikiFieldSha256 = resumedSha,
                    ImportedWebPartCount = webPartCount
                };
            }

            // Ensure checkout if library requires checkout
            if (targetLocation.TargetLibrary.ForceCheckout && targetFile.CheckOutType == CheckOutType.None)
            {
                recorder.Execute<bool>(
                    "page.checkout",
                    $"Check out page '{package.Plan.TargetPageServerRelativeUrl}'.",
                    () =>
                    {
                        targetFile.CheckOut();
                        return true;
                    },
                    value => MutationOutcome.Applied,
                    value => "Checked out page.");
                targetContext.ExecuteQueryRetry();
            }

            // Write WikiField: exact first, fallback to entity-safe if SharePoint rewrote brackets
            var wikiPlan = package.Plan.WikiFieldPlan;
            var titleToSet = package.Plan.FieldPlan?.Title ?? package.Snapshot.Source.Title;
            var persistedSha = ClassicWikiFieldWriter.WriteFields(
                targetContext,
                targetItem,
                wikiPlan,
                titleToSet,
                recorder,
                warnings);

            // Place Web Parts
            var importedWebParts = 0;
            if (package.Plan.WebParts != null && package.Plan.WebParts.Count > 0)
            {
                importedWebParts = ClassicWikiWebPartWriter.WriteWebParts(targetContext, targetFile, package.Plan.WebParts, recorder, warnings);
            }

            // Write ownership provenance properties
            ClassicWikiProvenanceWriter.WriteOwnership(targetContext, targetFile, package, recorder);

            // Lifecycle: Check-in / Publish if needed
            if (targetLocation.TargetLibrary.EnableVersioning || targetLocation.TargetLibrary.ForceCheckout)
            {
                try
                {
                    recorder.Execute<bool>(
                        "page.checkin",
                        $"Check in wiki page '{package.Plan.TargetPageServerRelativeUrl}'.",
                        () =>
                        {
                            targetFile.CheckIn("Migration checkin", CheckinType.MajorCheckIn);
                            return true;
                        },
                        value => MutationOutcome.Applied,
                        value => "Checked in wiki page.");
                    targetContext.ExecuteQueryRetry();
                }
                catch (Exception ex)
                {
                    warnings.Add("Check-in warning: " + ex.Message);
                }
            }

            return new ClassicWikiWriteResult
            {
                TargetLibrary = targetLocation.TargetLibrary,
                TargetFile = targetFile,
                TargetItem = targetItem,
                ResumedExistingOwnedPage = false,
                PersistedWikiFieldSha256 = persistedSha,
                ImportedWebPartCount = importedWebParts
            };
        }

        private static bool IsMissing(ServerException ex)
        {
            return string.Equals(ex.ServerErrorTypeName, "System.IO.FileNotFoundException", StringComparison.Ordinal)
                || ex.ServerErrorCode == -2147024894;
        }
    }
}
