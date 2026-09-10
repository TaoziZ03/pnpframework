using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public static class ClassicWikiRuntimeBindingFactory
    {
        public static NativePageRuntimeBinding CreateNative(
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            NativePageRuntimeTargetIdentity target,
            RuntimeVerificationReceipt runtimeReceipt,
            RuntimeVerificationManifest requirementsManifest,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore,
            ProducerBuildProvenanceReceipt provenanceReceipt = null,
            ProducerBuildProvenanceManifest provenanceManifest = null)
        {
            var importBinding = NativePageImportReceiptAggregateValidator.ValidateForCompare(
                importAggregate,
                admittedPlan,
                admittedPlanDigestSha256);
            var result = new NativePageRuntimeBinding
            {
                RuntimeEvidenceAuthority = NativePageRuntimeContract.NativeAuthority,
                RuntimeOperationId = admittedPlan.Operations.RuntimeOperationId,
                SourceIdentityDigestSha256 = admittedPlan.SourceVersion.IdentityDigestSha256,
                SourceVersionDigestSha256 = admittedPlan.SourceVersion.VersionDigestSha256,
                AdmittedPlanDigestSha256 = admittedPlanDigestSha256,
                ImportReceiptDigestSha256 = importBinding.ReceiptDigestSha256,
                Target = NativePageRuntimeBindingValidator.CopyTarget(target),
                RequestedUrl = FirstRequestedUrl(runtimeReceipt),
                FinalUrl = FirstFinalUrl(runtimeReceipt),
                RuntimeReceiptSchemaVersion = runtimeReceipt?.SchemaVersion,
                RuntimeEvidenceDigestSha256 = runtimeReceipt == null
                    ? null
                    : MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(runtimeReceipt)),
                RuntimeVerificationStatus = runtimeReceipt?.Status ?? RuntimeVerificationStatus.Pending,
                ProducerBuildProvenanceReceiptDigestSha256 = ProvenanceDigest(provenanceReceipt, provenanceManifest)
            };
            NativePageRuntimeBindingValidator.ValidateCoreAndComputeDigest(
                result,
                admittedPlan,
                admittedPlanDigestSha256,
                importAggregate,
                target);
            NativePageRuntimeBindingValidator.ValidateNativeEvidence(
                result,
                runtimeReceipt,
                requirementsManifest,
                expectedImplementationRef,
                artifactStore,
                admittedPlan);
            return result;
        }

        public static NativePageRuntimeBinding CreatePending(
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            NativePageRuntimeTargetIdentity target,
            ExternalPageRuntimeEvidence externalEvidence = null,
            ProducerBuildProvenanceReceipt provenanceReceipt = null,
            ProducerBuildProvenanceManifest provenanceManifest = null)
        {
            var importBinding = NativePageImportReceiptAggregateValidator.ValidateForCompare(
                importAggregate,
                admittedPlan,
                admittedPlanDigestSha256);
            var result = new NativePageRuntimeBinding
            {
                RuntimeEvidenceAuthority = externalEvidence == null
                    ? NativePageRuntimeContract.NoAuthority
                    : NativePageRuntimeContract.ExternalAuthority,
                RuntimeOperationId = admittedPlan.Operations.RuntimeOperationId,
                SourceIdentityDigestSha256 = admittedPlan.SourceVersion.IdentityDigestSha256,
                SourceVersionDigestSha256 = admittedPlan.SourceVersion.VersionDigestSha256,
                AdmittedPlanDigestSha256 = admittedPlanDigestSha256,
                ImportReceiptDigestSha256 = importBinding.ReceiptDigestSha256,
                Target = NativePageRuntimeBindingValidator.CopyTarget(target),
                RequestedUrl = externalEvidence?.RequestedUrl,
                FinalUrl = externalEvidence?.FinalUrl,
                RuntimeReceiptSchemaVersion = externalEvidence?.SchemaVersion,
                RuntimeEvidenceDigestSha256 = externalEvidence?.EvidenceDigestSha256,
                RuntimeVerificationStatus = RuntimeVerificationStatus.Pending,
                ExternalEvidenceDigestSha256 = externalEvidence?.ReportDigestSha256,
                ProducerBuildProvenanceReceiptDigestSha256 = ProvenanceDigest(provenanceReceipt, provenanceManifest)
            };
            NativePageRuntimeBindingValidator.ValidateCoreAndComputeDigest(
                result,
                admittedPlan,
                admittedPlanDigestSha256,
                importAggregate,
                target);
            if (externalEvidence != null)
            {
                NativePageRuntimeBindingValidator.ValidateExternalEvidence(externalEvidence, result);
            }
            return result;
        }

        private static string FirstRequestedUrl(RuntimeVerificationReceipt receipt)
        {
            return receipt?.Results?.FirstOrDefault()?.Http?.RequestedUrl;
        }

        private static string FirstFinalUrl(RuntimeVerificationReceipt receipt)
        {
            return receipt?.Results?.FirstOrDefault()?.Http?.FinalUrl;
        }

        private static string ProvenanceDigest(
            ProducerBuildProvenanceReceipt receipt,
            ProducerBuildProvenanceManifest manifest)
        {
            return receipt == null || manifest == null
                ? null
                : ProducerBuildProvenanceContract.ValidateReceiptAndComputeDigest(receipt, manifest);
        }
    }
}
