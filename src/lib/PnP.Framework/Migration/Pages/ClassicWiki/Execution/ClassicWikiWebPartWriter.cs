using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.WebParts;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal static class ClassicWikiWebPartWriter
    {
        public static int WriteWebParts(
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
    }
}
