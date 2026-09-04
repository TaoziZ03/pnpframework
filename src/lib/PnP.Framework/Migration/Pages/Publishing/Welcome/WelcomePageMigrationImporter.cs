using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Packaging;
using System;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Topology.Ingredients;

namespace PnP.Framework.Migration.Pages.Publishing.Welcome
{
    public sealed class WelcomePageMigrationImporter
    {
        public PublishingPageImportReceipt Import(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest)
        {
            return ImportCore(targetContext, package, approvedPlanDigest, null, null, null);
        }

        public PublishingPageImportReceipt Import(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal)
        {
            return ImportCore(targetContext, package, approvedPlanDigest, journal, null, null);
        }

        public PublishingPageImportReceipt Import(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore)
        {
            return ImportCore(targetContext, package, approvedPlanDigest, journal, artifactStore, null);
        }

        public PublishingPageImportReceipt ImportWithSharedTopology(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            SharedTopologyExecutionProof sharedTopologyProof,
            IMigrationExecutionJournal journal = null,
            IMigrationArtifactStore artifactStore = null)
        {
            return ImportCore(
                targetContext,
                package,
                approvedPlanDigest,
                journal,
                artifactStore,
                sharedTopologyProof);
        }

        private static PublishingPageImportReceipt ImportCore(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore,
            SharedTopologyExecutionProof sharedTopologyProof)
        {
            if (targetContext == null)
            {
                throw new ArgumentNullException(nameof(targetContext));
            }

            PublishingPagePackageValidator.ValidateMigration(package, artifactStore);
            var executionScope = PublishingPageExecutionScope.Create(package);
            PublishingPageImportPlanValidator.Validate(package, WelcomePageV1WorkflowPolicy.Instance, executionScope);
            var operationId = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;
            var recorder = new MigrationExecutionRecorder(operationId, package.PlanDigest, journal);
            var admissionFailure = PublishingPageImportAdmission.TryAdmit(
                targetContext,
                package,
                executionScope,
                approvedPlanDigest,
                operationId,
                startedAt,
                recorder,
                sharedTopologyProof);
            if (admissionFailure != null)
            {
                return admissionFailure;
            }

            recorder.RecordState(MigrationExecutionStatus.Running, "Target admission passed. Mutation execution is starting.");
            try
            {
                return PublishingPageMutationExecutor.Execute(
                    targetContext,
                    package,
                    executionScope,
                    approvedPlanDigest,
                    operationId,
                    startedAt,
                    recorder,
                    artifactStore,
                    package.Plan.TargetProbe?.PageContentTypeId,
                    sharedTopologyProof);
            }
            catch (Exception exception)
            {
                recorder.RecordState(MigrationExecutionStatus.FailedUnexpectedly, exception.Message);
                return PublishingPageImportReceiptFactory.UnexpectedFailure(
                    package,
                    operationId,
                    startedAt,
                    exception,
                    recorder);
            }
        }
    }
}

