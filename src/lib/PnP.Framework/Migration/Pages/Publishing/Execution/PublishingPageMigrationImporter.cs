using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Topology.Ingredients;
using System;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    public sealed class PublishingPageMigrationImporter
    {
        public PublishingPageImportReceipt Import(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            PublishingPageWorkflowPolicy policy = null,
            IMigrationExecutionJournal journal = null,
            IMigrationArtifactStore artifactStore = null)
        {
            return ImportCore(targetContext, package, approvedPlanDigest, policy, journal, artifactStore, null);
        }

        public PublishingPageImportReceipt ImportWithSharedTopology(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            SharedTopologyExecutionProof sharedTopologyProof,
            PublishingPageWorkflowPolicy policy = null,
            IMigrationExecutionJournal journal = null,
            IMigrationArtifactStore artifactStore = null)
        {
            return ImportCore(
                targetContext,
                package,
                approvedPlanDigest,
                policy,
                journal,
                artifactStore,
                sharedTopologyProof);
        }

        private static PublishingPageImportReceipt ImportCore(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            PublishingPageWorkflowPolicy policy,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore,
            SharedTopologyExecutionProof sharedTopologyProof)
        {
            if (targetContext == null)
            {
                throw new ArgumentNullException(nameof(targetContext));
            }

            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            PublishingPagePackageValidator.ValidateMigration(package, artifactStore);
            var executionScope = PublishingPageExecutionScope.Create(package);
            var workflowPolicy = policy ?? PublishingPageProfileRegistry.ResolvePolicy(
                package.Selection?.WorkflowId,
                package.Snapshot?.Source?.ContentTypeId);

            PublishingPageImportPlanValidator.Validate(package, workflowPolicy, executionScope);
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
