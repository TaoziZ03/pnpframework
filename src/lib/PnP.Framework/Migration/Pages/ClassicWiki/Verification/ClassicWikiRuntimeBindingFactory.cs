using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public static class ClassicWikiRuntimeBindingFactory
    {
        /// <summary>
        /// Seals the native expectation before browser/runtime capture begins.
        /// No runtime result or caller-selected acceptance status is accepted here.
        /// </summary>
        public static NativePageRuntimeBinding CreatePreCapture(
            Guid runId,
            ClassicWikiMigrationPackage package,
            Guid sourceListId,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            NativePageRuntimeTargetIdentityEvidence freshTargetIdentity,
            string importProducerRef,
            string contractProducerRef,
            ProducerBuildProvenanceManifest contractProvenanceManifest,
            IMigrationArtifactStore artifactStore,
            DateTimeOffset issuedAtUtc,
            DateTimeOffset captureExpiresAtUtc)
        {
            if (package?.Snapshot?.Source == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            if (freshTargetIdentity == null)
            {
                throw new ArgumentNullException(nameof(freshTargetIdentity));
            }

            var requirements = NativePageRuntimeBindingValidator.CreateClassicWikiManifest();
            var packageEvidence = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                package,
                artifactStore,
                "application/vnd.pnp.classic-wiki-package+json");
            var importEvidence = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                importAggregate,
                artifactStore,
                "application/vnd.pnp.native-import-receipt+json");
            var policyEvidence = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                requirements,
                artifactStore,
                "application/vnd.pnp.runtime-policy+json");
            if (freshTargetIdentity.Artifact == null)
            {
                freshTargetIdentity.Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
                    freshTargetIdentity.Identity,
                    artifactStore,
                    "application/vnd.pnp.target-identity+json");
            }
            var provenanceDigest = ProducerBuildProvenanceContract.ValidateManifestAndComputeDigest(
                contractProvenanceManifest);
            var importBinding = NativePageImportReceiptAggregateValidator.ValidateForCompare(
                importAggregate,
                admittedPlan,
                admittedPlanDigestSha256);
            var captureNotBefore = issuedAtUtc;
            if (captureNotBefore < importAggregate.ClassicWikiReceipt.CompletedAtUtc)
            {
                captureNotBefore = importAggregate.ClassicWikiReceipt.CompletedAtUtc;
            }
            if (captureNotBefore < admittedPlan.SourceVersion.ObservedAtUtc)
            {
                captureNotBefore = admittedPlan.SourceVersion.ObservedAtUtc;
            }

            var result = new NativePageRuntimeBinding
            {
                RunId = runId,
                ClaimId = NativePageRuntimeContract.ClaimId,
                Subject = new NativePageRuntimeSubject
                {
                    IngredientId = "node:runtime",
                    PageIngredientKind = "Runtime",
                    Subtype = "runtime.page",
                    SemanticRole = "runtime.wiki acceptance binding",
                    PrimaryOwnerLane = "shared integration",
                    DependencyIds = new List<string>()
                },
                SourceIdentity = new NativePageRuntimeSourceIdentity
                {
                    SiteId = package.Snapshot.Source.SiteId,
                    WebId = package.Snapshot.Source.WebId,
                    ListId = sourceListId,
                    ListItemId = package.Snapshot.Source.ListItemId,
                    FileUniqueId = package.Snapshot.Source.FileUniqueId,
                    PageServerRelativeUrl = package.Snapshot.Source.PageServerRelativeUrl
                },
                SourceVersion = Copy(admittedPlan.SourceVersion),
                SnapshotDigestSha256 = package.SnapshotDigest,
                PlanDigest = package.PlanDigest,
                AdmittedPlanDigestSha256 = admittedPlanDigestSha256,
                ImportReceiptDigestSha256 = importBinding.ReceiptDigestSha256,
                Operations = Copy(admittedPlan.Operations),
                TargetStorageIdentity = NativePageRuntimeBindingValidator.CopyTarget(freshTargetIdentity.Identity),
                TargetIdentityEvidence = freshTargetIdentity,
                NativeImportEvidence = importEvidence,
                PackageEvidence = packageEvidence,
                PolicyArtifact = policyEvidence,
                RequirementsManifest = requirements,
                RequirementsManifestDigestSha256 = MigrationDigest.ComputeSha256(
                    MigrationContractSerializer.SerializeCanonical(requirements)),
                ExpectedAuthoredContentSha256 = package.Plan.WikiFieldPlan.ExpectedStoredSha256,
                IssuedAtUtc = issuedAtUtc,
                CaptureNotBeforeUtc = captureNotBefore,
                CaptureExpiresAtUtc = captureExpiresAtUtc,
                ImportProducerRef = importProducerRef,
                ContractProducerRef = contractProducerRef,
                ContractProducerProvenanceManifestDigestSha256 = provenanceDigest,
                ContractProducerProvenanceStatus = ProducerBuildProvenanceContract.Unverified
            };
            NativePageRuntimeBindingValidator.SealBinding(result);
            NativePageRuntimeBindingValidator.ValidateBindingAndComputeDigest(
                result,
                package,
                admittedPlan,
                admittedPlanDigestSha256,
                importAggregate,
                contractProvenanceManifest,
                artifactStore);
            return result;
        }

        private static CurrentSourceVersionIdentity Copy(CurrentSourceVersionIdentity value)
        {
            return value == null ? null : new CurrentSourceVersionIdentity
            {
                IdentityDigestSha256 = value.IdentityDigestSha256,
                VersionDigestSha256 = value.VersionDigestSha256,
                ETag = value.ETag,
                LastModifiedUtc = value.LastModifiedUtc,
                VersionLabel = value.VersionLabel,
                ObservedAtUtc = value.ObservedAtUtc
            };
        }

        private static ReproOperationIds Copy(ReproOperationIds value)
        {
            return value == null ? null : new ReproOperationIds
            {
                MutationOperationId = value.MutationOperationId,
                ReadbackOperationId = value.ReadbackOperationId,
                RuntimeOperationId = value.RuntimeOperationId,
                CleanupOperationId = value.CleanupOperationId
            };
        }
    }
}
