using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Packaging;
using System;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Topology.Ingredients;

namespace PnP.Framework.Migration.Pages.Publishing.EnterpriseWiki
{
    public sealed class EnterpriseWikiMigrationImporter
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
            SharedTopologyGlobalMaterializationReceipt sharedTopologyReceipt,
            IMigrationExecutionJournal journal = null,
            IMigrationArtifactStore artifactStore = null)
        {
            return ImportCore(
                targetContext,
                package,
                approvedPlanDigest,
                journal,
                artifactStore,
                sharedTopologyReceipt);
        }

        private static PublishingPageImportReceipt ImportCore(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore,
            SharedTopologyGlobalMaterializationReceipt sharedTopologyReceipt)
        {
            if (targetContext == null)
            {
                throw new ArgumentNullException(nameof(targetContext));
            }

            PublishingPagePackageValidator.ValidateMigration(package, artifactStore);
            var executionScope = PublishingPageExecutionScope.Create(package);
            PublishingPageImportPlanValidator.Validate(package, EnterpriseWikiV1WorkflowPolicy.Instance, executionScope);
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
                sharedTopologyReceipt);
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
                    sharedTopologyReceipt);
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
