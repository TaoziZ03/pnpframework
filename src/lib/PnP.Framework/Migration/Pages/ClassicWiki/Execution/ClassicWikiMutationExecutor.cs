using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
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

            ClassicWikiFreshTargetEvidence freshEvidence;
            using (var freshContext = targetContext.Clone(package.Plan.TargetLocation.TargetWebUrl))
            {
                freshEvidence = ClassicWikiFreshTargetReader.Read(
                    freshContext,
                    package,
                    artifactStore,
                    warnings,
                    diagnostics);
                freshEvidence.IndependentContext = !ReferenceEquals(freshContext, targetContext);
            }

            var comparison = ClassicWikiFreshVerification.Evaluate(package, freshEvidence);
            diagnostics.AddRange(comparison.Differences);
            var targetSnapshot = freshEvidence.Recapture.Snapshot;
            var targetIdentity = targetSnapshot.Source;
            var readbackPassed = comparison.Passed;
            var hasExclusions = ClassicWikiFreshVerification.HasExplicitExclusions(package);
            warnings.Add("Authenticated browser/runtime verification was not run by the storage importer.");
            if (hasExclusions)
            {
                warnings.Add("The sealed plan contains explicit deferred fidelity exclusions; acceptance is partial even when storage verification passes.");
            }

            var receipt = new ClassicWikiImportReceipt
            {
                SchemaVersion = ClassicWikiPackageContract.ReceiptSchemaVersion,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                OperationId = operationId,
                ExecutionStatus = readbackPassed ? MigrationExecutionStatus.Succeeded : MigrationExecutionStatus.FailedUnexpectedly,
                MutationStarted = true,
                Steps = new List<MigrationMutationReceipt>(recorder.Steps),
                ApprovedPlanDigest = approvedPlanDigest,
                TargetWebUrl = targetIdentity.WebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                TargetFileUniqueId = targetIdentity.FileUniqueId,
                TargetListItemId = targetIdentity.ListItemId,
                TargetContentTypeId = targetIdentity.ContentTypeId,
                TargetVersionLabel = targetIdentity.VersionLabel,
                StoredWikiFieldSha256 = targetSnapshot.WikiFieldSha256,
                StorageContentEqual = comparison.WikiContentMatched,
                ResumedExistingOwnedPage = writeResult.ResumedExistingOwnedPage,
                ImportedWebPartCount = targetSnapshot.WebParts?.Count ?? 0,
                WebPartsMatched = comparison.WebPartsMatched,
                FieldsMatched = comparison.FieldsMatched,
                ContentTypeMatched = comparison.ContentTypeMatched,
                LibraryMatched = comparison.LibraryMatched,
                RuntimeIdentityMatched = comparison.RuntimeMatched,
                OwnershipMatched = comparison.OwnershipMatched,
                DependenciesMatched = comparison.DependenciesMatched,
                LifecycleMatched = comparison.LifecycleMatched,
                SecurityMatched = comparison.SecurityMatched,
                TargetIdentityMatched = comparison.TargetIdentityMatched,
                TargetLibraryTemplate = targetSnapshot.LibraryBaseTemplate,
                FreshReadbackPassed = readbackPassed,
                StorageVerificationStatus = readbackPassed ? StorageVerificationStatus.Passed : StorageVerificationStatus.Failed,
                RuntimeVerificationStatus = ClassicWikiImportStatusPolicy.RuntimeStatus,
                AcceptanceStatus = ClassicWikiImportStatusPolicy.Acceptance(readbackPassed, hasExclusions),
                Warnings = warnings,
                Diagnostics = diagnostics
            };

            return receipt;
        }
    }
}
