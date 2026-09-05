using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.Packaging;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal static class ClassicWikiFieldWriter
    {
        public static string WriteFields(
            ClientContext context,
            ListItem item,
            WikiFieldWritePlan wikiPlan,
            string titleToSet,
            MigrationExecutionRecorder recorder,
            ICollection<string> warnings)
        {
            if (wikiPlan == null)
            {
                if (!string.IsNullOrWhiteSpace(titleToSet))
                {
                    item["Title"] = titleToSet;
                    item.Update();
                    context.ExecuteQueryRetry();
                }
                return null;
            }

            // Step 1: Attempt exact write
            recorder.Execute<bool>(
                "wiki-field.exact",
                "Write exact captured WikiField and verify stored value.",
                () =>
                {
                    item["WikiField"] = wikiPlan.ExactValue;
                    if (!string.IsNullOrWhiteSpace(titleToSet))
                    {
                        item["Title"] = titleToSet;
                    }
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
                    if (!string.IsNullOrWhiteSpace(titleToSet))
                    {
                        item["Title"] = titleToSet;
                    }
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
    }
}
