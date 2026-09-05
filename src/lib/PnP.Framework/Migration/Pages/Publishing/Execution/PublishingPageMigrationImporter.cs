using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Content;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Lifecycle;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Pages.Publishing.Verification;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Topology.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    public sealed class PublishingPageMigrationImporter
    {
        internal sealed class ContractExecutionObservation
        {
            public ListItem TargetItem { get; set; }

            public bool ResumedExistingOwnedPage { get; set; }

            public PublishingPageTargetLifecycle ObservedLifecycle { get; set; }
        }

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

        internal PublishingPageImportReceipt ImportWithExecutionContractSeam(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            Func<PublishingPageExecutionScope, ContractExecutionObservation> execute,
            PublishingPageWorkflowPolicy policy = null,
            IMigrationArtifactStore artifactStore = null)
        {
            if (targetContext == null)
            {
                throw new ArgumentNullException(nameof(targetContext));
            }
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }
            if (!string.Equals(approvedPlanDigest, package.PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The execution-contract seam requires the exact approved plan digest.");
            }

            var executionScope = Prepare(package, policy, artifactStore);
            var observation = execute(executionScope)
                ?? throw new InvalidOperationException("The execution-contract seam returned no target observation.");
            if (observation.TargetItem == null)
            {
                throw new InvalidOperationException("The execution-contract seam returned no fresh target ListItem observation.");
            }

            var replacements = PublishingPageExecutionReplacementProjector.Project(package, executionScope);
            var fieldActions = executionScope.PageFieldActions(package)
                .Where(value => value.WillApply)
                .ToArray();
            var fieldResults = PublishingPageFieldFreshReadbackVerifier.Verify(
                observation.TargetItem,
                package.Snapshot.Fields,
                fieldActions,
                replacements,
                Array.Empty<PageFieldImportResult>());
            var fieldsMatched = fieldResults.Count == fieldActions.Length
                && fieldResults.All(value => value.Attempted && value.Succeeded);
            var layoutMatched = PublishingPageFieldFreshReadbackVerifier.LayoutMatches(
                observation.TargetItem,
                package.Plan.LayoutMaterialization?.TargetServerRelativeUrl);
            var expectedContent = PageTextTransformer.Rewrite(
                package.Snapshot.PublishingPageContent,
                replacements);
            var actualContent = PublishingPageCaptureReader.GetFieldString(
                observation.TargetItem,
                "PublishingPageContent") ?? string.Empty;
            var contentMatched = !executionScope.PublishingContent
                || PublishingPageContentStorageCanonicalizer.AreEquivalent(expectedContent, actualContent);
            var actualContentTypeId = PublishingPageCaptureReader.GetFieldString(
                observation.TargetItem,
                "ContentTypeId");
            var contentTypeMatched = !executionScope.ContentType
                || PublishingPageContentTypeIdentity.MatchesSiteContentType(
                    actualContentTypeId,
                    package.Plan.TargetProbe?.PageContentTypeId);
            var lifecycleMatched = !executionScope.Lifecycle
                || observation.ObservedLifecycle == package.Plan.TargetLifecycle;
            var passed = observation.ResumedExistingOwnedPage
                && fieldsMatched
                && layoutMatched
                && contentMatched
                && contentTypeMatched
                && lifecycleMatched;
            var operationId = Guid.NewGuid();
            var completed = DateTimeOffset.UtcNow;
            return new PublishingPageImportReceipt
            {
                StartedAtUtc = completed,
                CompletedAtUtc = completed,
                OperationId = operationId,
                ExecutionStatus = passed ? MigrationExecutionStatus.Succeeded : MigrationExecutionStatus.FailedUnexpectedly,
                MutationStarted = true,
                ApprovedPlanDigest = approvedPlanDigest,
                TargetWebUrl = package.Plan.TargetWebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                ApprovedLifecycle = package.Plan.TargetLifecycle,
                ExpectedLifecycle = package.Plan.TargetLifecycle,
                LifecycleMatched = lifecycleMatched,
                PageArtifactMatched = observation.ResumedExistingOwnedPage,
                OwnershipMatched = observation.ResumedExistingOwnedPage,
                LayoutMatched = layoutMatched,
                ContentTypeMatched = contentTypeMatched,
                PageFieldsMatched = fieldsMatched,
                StorageContentEqual = contentMatched,
                FieldResults = fieldResults,
                FreshReadbackPassed = passed,
                StorageVerificationStatus = passed ? StorageVerificationStatus.Passed : StorageVerificationStatus.Failed,
                RuntimeVerificationStatus = RuntimeVerificationStatus.NotRequired,
                AcceptanceStatus = passed ? MigrationAcceptanceStatus.Accepted : MigrationAcceptanceStatus.Rejected,
                Steps = new List<MigrationMutationReceipt>
                {
                    new MigrationMutationReceipt
                    {
                        OperationId = operationId,
                        PlanDigest = package.PlanDigest,
                        ActionId = "page.resume.contract",
                        Sequence = 1,
                        CompletedAtUtc = completed,
                        Outcome = observation.ResumedExistingOwnedPage
                            ? MutationOutcome.AlreadySatisfied
                            : MutationOutcome.Failed,
                        Message = observation.ResumedExistingOwnedPage
                            ? "The exact migration-owned page was resumed and verified by fresh field and layout readback."
                            : "The contract observation did not prove resume ownership."
                    }
                },
                Warnings = passed
                    ? new List<string>()
                    : new List<string> { "The execution-contract seam did not reproduce fresh resumed field and layout evidence." }
            };
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

            var executionScope = Prepare(package, policy, artifactStore);
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
