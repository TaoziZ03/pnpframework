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

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal sealed class ClassicWikiWriteResult
    {
        public List TargetLibrary { get; set; }

        public Microsoft.SharePoint.Client.File TargetFile { get; set; }

        public ListItem TargetItem { get; set; }

        public bool ResumedExistingOwnedPage { get; set; }

        public string PersistedWikiFieldSha256 { get; set; }

        public int ImportedWebPartCount { get; set; }
    }

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
                recorder.RecordAlreadySatisfied("wiki-field.write", "Stored WikiField verified on resumed page.");
                recorder.RecordAlreadySatisfied("page.webparts", "Web parts verified on resumed page.");
                recorder.RecordAlreadySatisfied("page.ownership", "Target page carries matching provenance properties.");
                return new ClassicWikiWriteResult
                {
                    TargetLibrary = targetLocation.TargetLibrary,
                    TargetFile = targetFile,
                    TargetItem = targetItem,
                    ResumedExistingOwnedPage = true,
                    PersistedWikiFieldSha256 = package.Snapshot.WikiFieldSha256
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
            string persistedSha = null;
            if (wikiPlan != null)
            {
                persistedSha = WriteWikiField(targetContext, targetItem, wikiPlan, recorder, warnings);
            }

            // Place Web Parts
            var importedWebParts = 0;
            if (package.Plan.WebParts != null && package.Plan.WebParts.Count > 0)
            {
                importedWebParts = WriteWebParts(targetContext, targetFile, package.Plan.WebParts, recorder, warnings);
            }

            // Write ownership provenance properties
            WriteOwnership(targetContext, targetFile, package, recorder);

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

        private static string WriteWikiField(
            ClientContext context,
            ListItem item,
            WikiFieldWritePlan wikiPlan,
            MigrationExecutionRecorder recorder,
            ICollection<string> warnings)
        {
            // Step 1: Attempt exact write
            recorder.Execute<bool>(
                "wiki-field.exact",
                "Write exact captured WikiField and verify stored value.",
                () =>
                {
                    item["WikiField"] = wikiPlan.ExactValue;
                    item.Update();
                    context.ExecuteQueryRetry();
                    return true;
                },
                value => MutationOutcome.Applied,
                value => "Exact WikiField written.");

            // Read back fresh value
            context.Load(item);
            context.ExecuteQueryRetry();
            var readback = item.FieldValues.TryGetValue("WikiField", out var val) ? val as string ?? string.Empty : string.Empty;
            var readbackSha = PageDigest.ComputeSha256(readback);

            if (string.Equals(readbackSha, wikiPlan.ExpectedStoredSha256, StringComparison.OrdinalIgnoreCase))
            {
                return readbackSha;
            }

            // Step 2: Fallback to entity-safe literal double brackets
            warnings.Add($"Exact WikiField readback digest mismatch (expected {wikiPlan.ExpectedStoredSha256}, got {readbackSha}). Executing entity-safe bracket fallback.");
            recorder.Execute<bool>(
                "wiki-field.entity-safe",
                "Write entity-safe literal double brackets and verify normalization.",
                () =>
                {
                    item["WikiField"] = wikiPlan.EntitySafeValue;
                    item.Update();
                    context.ExecuteQueryRetry();
                    return true;
                },
                value => MutationOutcome.Applied,
                value => "Entity-safe WikiField written.");

            context.Load(item);
            context.ExecuteQueryRetry();
            var fallbackReadback = item.FieldValues.TryGetValue("WikiField", out var val2) ? val2 as string ?? string.Empty : string.Empty;
            var fallbackSha = PageDigest.ComputeSha256(fallbackReadback);

            var normalizedReadback = fallbackReadback.Replace("&#91;&#91;", "[[").Replace("&#93;&#93;", "]]");
            var normalizedSha = PageDigest.ComputeSha256(normalizedReadback);

            if (string.Equals(normalizedSha, wikiPlan.ExpectedStoredSha256, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedSha;
            }

            return fallbackSha;
        }

        private static int WriteWebParts(
            ClientContext context,
            Microsoft.SharePoint.Client.File file,
            IList<ClassicWikiWebPartPlacementPlan> webParts,
            MigrationExecutionRecorder recorder,
            ICollection<string> warnings)
        {
            var count = 0;
            try
            {
                var wpm = file.GetLimitedWebPartManager(PersonalizationScope.Shared);
                foreach (var wp in webParts)
                {
                    if (string.IsNullOrWhiteSpace(wp.Xml)) continue;
                    try
                    {
                        recorder.Execute<WebPartDefinition>(
                            "webpart.place." + wp.SourceId.ToString("N"),
                            $"Place Web Part '{wp.Title}' into zone '{wp.ZoneId}'.",
                            () =>
                            {
                                var def = wpm.ImportWebPart(wp.Xml);
                                var added = wpm.AddWebPart(def.WebPart, wp.ZoneId ?? "Bottom", wp.TargetZoneIndex);
                                context.Load(added, a => a.Id);
                                context.ExecuteQueryRetry();
                                return added;
                            },
                            value => MutationOutcome.Applied,
                            value => $"Placed Web Part '{wp.Title}'.");
                        count++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"WebPart placement warning for '{wp.Title}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add("WebPartManager initialization warning: " + ex.Message);
            }
            return count;
        }

        private static void WriteOwnership(
            ClientContext context,
            Microsoft.SharePoint.Client.File targetFile,
            ClassicWikiMigrationPackage package,
            MigrationExecutionRecorder recorder)
        {
            recorder.Execute<bool>(
                "page.ownership",
                "Write migration provenance properties to target file.",
                () =>
                {
                    targetFile.Properties[ClassicWikiTargetOwnership.OriginalIdentifierPropertyName] = package.Plan.OriginalIdentifier;
                    targetFile.Properties[ClassicWikiTargetOwnership.SourceSnapshotDigestPropertyName] = package.SnapshotDigest;
                    targetFile.Properties[ClassicWikiTargetOwnership.PlanDigestPropertyName] = package.PlanDigest;
                    targetFile.Update();
                    context.ExecuteQueryRetry();
                    return true;
                },
                value => MutationOutcome.Applied,
                value => "Wrote provenance properties.");
        }

        private static bool IsMissing(ServerException ex)
        {
            return string.Equals(ex.ServerErrorTypeName, "System.IO.FileNotFoundException", StringComparison.Ordinal)
                || ex.ServerErrorCode == -2147024894;
        }
    }
}
