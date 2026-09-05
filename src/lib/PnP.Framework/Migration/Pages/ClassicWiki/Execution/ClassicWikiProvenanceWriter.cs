using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using System;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal static class ClassicWikiProvenanceWriter
    {
        public static void WriteOwnership(
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
    }
}
