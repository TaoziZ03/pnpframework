using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    public sealed class ClassicWikiMigrationImporter
    {
        public ClassicWikiImportReceipt Import(
            ClientContext targetContext,
            ClassicWikiMigrationPackage package,
            string approvedPlanDigest)
        {
            return Import(targetContext, package, approvedPlanDigest, null, null);
        }

        public ClassicWikiImportReceipt Import(
            ClientContext targetContext,
            ClassicWikiMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal)
        {
            return Import(targetContext, package, approvedPlanDigest, journal, null);
        }

        public ClassicWikiImportReceipt Import(
            ClientContext targetContext,
            ClassicWikiMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore)
        {
            if (targetContext == null) throw new ArgumentNullException(nameof(targetContext));
            if (package == null) throw new ArgumentNullException(nameof(package));

            var operationId = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;
            var recorder = new MigrationExecutionRecorder(operationId, package.PlanDigest, journal);

            try
            {
                ClassicWikiPackageValidator.ValidateMigration(package, artifactStore);
            }
            catch (InvalidDataException exception)
            {
                var failure = new ExecutionAdmissionFailure
                {
                    Code = "PackageNotAdmissible",
                    Subject = package.Plan?.TargetPageServerRelativeUrl,
                    Message = exception.Message
                };
                return ClassicWikiImportReceiptFactory.AdmissionFailure(
                    package,
                    operationId,
                    startedAt,
                    failure,
                    recorder);
            }

            if (!string.Equals(package.PlanDigest, approvedPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                var failure = new ExecutionAdmissionFailure
                {
                    Code = "PlanDigestNotApproved",
                    Subject = package.Plan.TargetPageServerRelativeUrl,
                    Message = $"Plan digest mismatch: package has '{package.PlanDigest}', approved is '{approvedPlanDigest}'."
                };
                return ClassicWikiImportReceiptFactory.AdmissionFailure(
                    package,
                    operationId,
                    startedAt,
                    failure,
                    recorder);
            }

            try
            {
                return ClassicWikiMutationExecutor.Execute(
                    targetContext,
                    package,
                    approvedPlanDigest,
                    operationId,
                    startedAt,
                    recorder,
                    artifactStore);
            }
            catch (Exception ex)
            {
                recorder.RecordState(MigrationExecutionStatus.FailedUnexpectedly, ex.Message);
                return ClassicWikiImportReceiptFactory.UnexpectedFailure(
                    package,
                    operationId,
                    startedAt,
                    ex,
                    recorder);
            }
        }
    }
}
