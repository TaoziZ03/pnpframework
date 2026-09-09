using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Verification;
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
            return ImportCore(
                targetContext,
                package,
                approvedPlanDigest,
                journal,
                artifactStore,
                null,
                null);
        }

        public ClassicWikiImportReceipt ImportAdmitted(
            ClientContext targetContext,
            ClassicWikiMigrationPackage package,
            string approvedPlanDigest,
            AdmittedReproExecutionPlan admittedPlan,
            IMigrationExecutionJournal journal = null,
            IMigrationArtifactStore artifactStore = null)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            var targetIdentity = ClassicWikiImportReceiptBinder.CanonicalTargetIdentity(
                package.Plan?.TargetLocation?.TargetWebUrl,
                package.Plan?.TargetPageServerRelativeUrl);
            var admittedPlanDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                targetIdentity);
            return ImportCore(
                targetContext,
                package,
                approvedPlanDigest,
                journal,
                artifactStore,
                admittedPlan,
                admittedPlanDigest);
        }

        private static ClassicWikiImportReceipt ImportCore(
            ClientContext targetContext,
            ClassicWikiMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            if (targetContext == null) throw new ArgumentNullException(nameof(targetContext));
            if (package == null) throw new ArgumentNullException(nameof(package));

            var operationId = admittedPlan?.Operations?.MutationOperationId ?? Guid.NewGuid();
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
                return ClassicWikiImportReceiptBinder.Bind(
                    ClassicWikiImportReceiptFactory.AdmissionFailure(
                        package,
                        operationId,
                        startedAt,
                        failure,
                        recorder),
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }

            if (!string.Equals(package.PlanDigest, approvedPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                var failure = new ExecutionAdmissionFailure
                {
                    Code = "PlanDigestNotApproved",
                    Subject = package.Plan.TargetPageServerRelativeUrl,
                    Message = $"Plan digest mismatch: package has '{package.PlanDigest}', approved is '{approvedPlanDigest}'."
                };
                return ClassicWikiImportReceiptBinder.Bind(
                    ClassicWikiImportReceiptFactory.AdmissionFailure(
                        package,
                        operationId,
                        startedAt,
                        failure,
                        recorder),
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }

            try
            {
                return ClassicWikiImportReceiptBinder.Bind(
                    ClassicWikiMutationExecutor.Execute(
                        targetContext,
                        package,
                        approvedPlanDigest,
                        operationId,
                        startedAt,
                        recorder,
                        artifactStore),
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }
            catch (Exception ex)
            {
                recorder.RecordState(MigrationExecutionStatus.FailedUnexpectedly, ex.Message);
                return ClassicWikiImportReceiptBinder.Bind(
                    ClassicWikiImportReceiptFactory.UnexpectedFailure(
                        package,
                        operationId,
                        startedAt,
                        ex,
                        recorder),
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }
        }
    }
}
