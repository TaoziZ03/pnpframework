using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Packaging
{
    internal static class ClassicWikiImportReceiptFactory
    {
        public static ClassicWikiImportReceipt AdmissionFailure(
            ClassicWikiMigrationPackage package,
            Guid operationId,
            DateTimeOffset startedAt,
            ExecutionAdmissionFailure failure,
            MigrationExecutionRecorder recorder)
        {
            return new ClassicWikiImportReceipt
            {
                OperationId = operationId,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ExecutionStatus = MigrationExecutionStatus.FailedUnexpectedly,
                AdmissionFailure = failure,
                MutationStarted = false,
                ApprovedPlanDigest = package?.PlanDigest,
                TargetPageServerRelativeUrl = package?.Plan?.TargetPageServerRelativeUrl,
                Steps = recorder?.Steps != null ? new List<MigrationMutationReceipt>(recorder.Steps) : new List<MigrationMutationReceipt>(),
                StorageVerificationStatus = StorageVerificationStatus.NotRun,
                RuntimeVerificationStatus = RuntimeVerificationStatus.NotRun,
                AcceptanceStatus = MigrationAcceptanceStatus.Rejected
            };
        }

        public static ClassicWikiImportReceipt UnexpectedFailure(
            ClassicWikiMigrationPackage package,
            Guid operationId,
            DateTimeOffset startedAt,
            Exception exception,
            MigrationExecutionRecorder recorder)
        {
            return new ClassicWikiImportReceipt
            {
                OperationId = operationId,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ExecutionStatus = MigrationExecutionStatus.FailedUnexpectedly,
                MutationStarted = recorder?.Steps != null && recorder.Steps.Count > 0,
                ApprovedPlanDigest = package?.PlanDigest,
                TargetPageServerRelativeUrl = package?.Plan?.TargetPageServerRelativeUrl,
                Steps = recorder?.Steps != null ? new List<MigrationMutationReceipt>(recorder.Steps) : new List<MigrationMutationReceipt>(),
                StorageVerificationStatus = StorageVerificationStatus.NotRun,
                RuntimeVerificationStatus = RuntimeVerificationStatus.NotRun,
                AcceptanceStatus = MigrationAcceptanceStatus.Rejected,
                Diagnostics = new List<string> { exception?.ToString() ?? "Unknown failure" }
            };
        }
    }
}
