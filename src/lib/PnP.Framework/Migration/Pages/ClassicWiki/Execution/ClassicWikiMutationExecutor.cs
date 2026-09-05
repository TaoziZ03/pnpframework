using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal static class ClassicWikiMutationExecutor
    {
        public static ClassicWikiImportReceipt Execute(
            ClientContext targetContext,
            ClassicWikiMigrationPackage package,
            string approvedPlanDigest,
            Guid operationId,
            DateTimeOffset startedAt,
            MigrationExecutionRecorder recorder,
            IMigrationArtifactStore artifactStore)
        {
            var warnings = new List<string>();
            var diagnostics = new List<string>();

            recorder.RecordState(MigrationExecutionStatus.Running, "Starting classic wiki page mutation.");

            var writeResult = ClassicWikiTargetWriter.Write(
                targetContext,
                package,
                recorder,
                warnings);

            // Fresh readback verification
            var targetWeb = targetContext.Web;
            var targetFile = targetWeb.GetFileByServerRelativePath(
                ResourcePath.FromDecodedUrl(package.Plan.TargetPageServerRelativeUrl));
            var targetItem = targetFile.ListItemAllFields;
            targetContext.Load(targetFile, f => f.Exists, f => f.UniqueId, f => f.UIVersionLabel);
            targetContext.Load(targetItem, i => i.Id);
            targetContext.ExecuteQueryRetry();

            var readbackPassed = targetFile.Exists && targetItem.Id > 0;
            var expectedSha = package.Plan.WikiFieldPlan?.ExpectedStoredSha256;
            var contentEqual = !string.IsNullOrEmpty(writeResult.PersistedWikiFieldSha256)
                ? string.Equals(writeResult.PersistedWikiFieldSha256, expectedSha, StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrEmpty(expectedSha);

            var expectedWebPartCount = package.Plan.WebParts?.Count ?? 0;
            var webPartsMatched = writeResult.ImportedWebPartCount == expectedWebPartCount;
            var runtimePassed = readbackPassed && contentEqual && webPartsMatched;

            var receipt = new ClassicWikiImportReceipt
            {
                SchemaVersion = ClassicWikiPackageContract.ReceiptSchemaVersion,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                OperationId = operationId,
                ExecutionStatus = runtimePassed ? MigrationExecutionStatus.Succeeded : MigrationExecutionStatus.FailedUnexpectedly,
                MutationStarted = true,
                Steps = new List<MigrationMutationReceipt>(recorder.Steps),
                ApprovedPlanDigest = approvedPlanDigest,
                TargetWebUrl = targetWeb.Url,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                TargetFileUniqueId = targetFile.UniqueId,
                TargetListItemId = targetItem.Id,
                TargetContentTypeId = ClassicWikiPackageContract.DefaultContentTypeId,
                TargetVersionLabel = targetFile.UIVersionLabel,
                StoredWikiFieldSha256 = writeResult.PersistedWikiFieldSha256,
                StorageContentEqual = contentEqual,
                ResumedExistingOwnedPage = writeResult.ResumedExistingOwnedPage,
                ImportedWebPartCount = writeResult.ImportedWebPartCount,
                WebPartsMatched = webPartsMatched,
                FreshReadbackPassed = readbackPassed,
                StorageVerificationStatus = contentEqual ? StorageVerificationStatus.Passed : StorageVerificationStatus.Failed,
                RuntimeVerificationStatus = runtimePassed ? RuntimeVerificationStatus.Passed : RuntimeVerificationStatus.Failed,
                AcceptanceStatus = runtimePassed ? MigrationAcceptanceStatus.Accepted : MigrationAcceptanceStatus.Rejected,
                Warnings = warnings,
                Diagnostics = diagnostics
            };

            return receipt;
        }
    }
}
