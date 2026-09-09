using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Topology.Ingredients;
using PnP.Framework.Migration.Verification;
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
            return ImportCore(targetContext, package, approvedPlanDigest, policy, journal, artifactStore, null, null);
        }

        public PublishingPageImportReceipt ImportAdmitted(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            AdmittedReproExecutionPlan admittedPlan,
            PublishingPageWorkflowPolicy policy = null,
            IMigrationExecutionJournal journal = null,
            IMigrationArtifactStore artifactStore = null)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            var targetIdentity = PublishingPageImportReceiptBinder.CanonicalTargetIdentity(
                package.Plan?.TargetWebUrl,
                package.Plan?.TargetPageServerRelativeUrl);
            var admittedPlanDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                targetIdentity);
            return ImportCore(
                targetContext,
                package,
                approvedPlanDigest,
                policy,
                journal,
                artifactStore,
                null,
                admittedPlan,
                admittedPlanDigest);
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
                sharedTopologyProof,
                null);
        }

        internal PublishingPageImportReceipt ImportWithExecutionSeam(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            PublishingPageImportExecutionSeam executionSeam,
            PublishingPageWorkflowPolicy policy = null,
            IMigrationArtifactStore artifactStore = null)
        {
            return ImportCore(
                targetContext,
                package,
                approvedPlanDigest,
                policy,
                null,
                artifactStore,
                null,
                null,
                null,
                executionSeam ?? throw new ArgumentNullException(nameof(executionSeam)));
        }

        private static PublishingPageImportReceipt ImportCore(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            PublishingPageWorkflowPolicy policy,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore,
            SharedTopologyExecutionProof sharedTopologyProof,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256 = null,
            PublishingPageImportExecutionSeam executionSeam = null)
        {
            if (targetContext == null)
            {
                throw new ArgumentNullException(nameof(targetContext));
            }

            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var executionScope = Prepare(package, policy, artifactStore);
            var operationId = admittedPlan?.Operations?.MutationOperationId ?? Guid.NewGuid();
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
                sharedTopologyProof,
                executionSeam);
            if (admissionFailure != null)
            {
                return PublishingPageImportReceiptBinder.Bind(
                    admissionFailure,
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }

            recorder.RecordState(MigrationExecutionStatus.Running, "Target admission passed. Mutation execution is starting.");
            try
            {
                var receipt = PublishingPageMutationExecutor.Execute(
                    targetContext,
                    package,
                    executionScope,
                    approvedPlanDigest,
                    operationId,
                    startedAt,
                    recorder,
                    artifactStore,
                    package.Plan.TargetProbe?.PageContentTypeId,
                    sharedTopologyProof,
                    executionSeam);
                return PublishingPageImportReceiptBinder.Bind(
                    receipt,
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }
            catch (Exception exception)
            {
                recorder.RecordState(MigrationExecutionStatus.FailedUnexpectedly, exception.Message);
                var receipt = PublishingPageImportReceiptFactory.UnexpectedFailure(
                    package,
                    operationId,
                    startedAt,
                    exception,
                    recorder);
                return PublishingPageImportReceiptBinder.Bind(
                    receipt,
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }
        }

        private static PublishingPageExecutionScope Prepare(
            PublishingPageMigrationPackage package,
            PublishingPageWorkflowPolicy policy,
            IMigrationArtifactStore artifactStore)
        {
            PublishingPagePackageValidator.ValidateMigration(package, artifactStore);
            var executionScope = PublishingPageExecutionScope.Create(package);
            var workflowPolicy = policy ?? PublishingPageProfileRegistry.ResolvePolicy(
                workflowId: package.Selection?.WorkflowId,
                contentTypeId: package.Snapshot?.Source?.ContentTypeId);
            PublishingPageImportPlanValidator.Validate(package, workflowPolicy, executionScope);
            return executionScope;
        }
    }
}
