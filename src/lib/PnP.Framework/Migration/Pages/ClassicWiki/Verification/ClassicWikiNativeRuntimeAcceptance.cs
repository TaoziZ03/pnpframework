using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public static class ClassicWikiNativeRuntimeAcceptance
    {
        public static NativePageRuntimeAcceptanceReceipt Evaluate(
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            NativePageRuntimeBinding binding,
            ExternalPageRuntimeEvidence externalEvidence,
            IMigrationArtifactStore artifactStore,
            INativePageRuntimeEvidencePolicy policy,
            ProducerBuildProvenanceManifest provenanceManifest,
            IProducerBuildProvenanceVerifier provenanceVerifier,
            INativePageRuntimeIdentityEvidenceVerifier identityEvidenceVerifier,
            string evaluatorId,
            string evaluatorImplementationRef,
            DateTimeOffset evaluatedAtUtc)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            var receipt = CreateBaseReceipt(
                package,
                admittedPlanDigestSha256,
                importAggregate,
                binding,
                evaluatorId,
                evaluatorImplementationRef,
                evaluatedAtUtc);
            try
            {
                if (string.IsNullOrWhiteSpace(evaluatorId)
                    || evaluatorImplementationRef == null
                    || evaluatorImplementationRef.Length != 40
                    || !string.Equals(evaluatorImplementationRef, binding?.ContractProducerRef, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The native runtime evaluator identity/ref is missing or foreign.");
                }
                var bindingDigest = NativePageRuntimeBindingValidator.ValidateBindingAndComputeDigest(
                    binding,
                    package,
                    admittedPlan,
                    admittedPlanDigestSha256,
                    importAggregate,
                    provenanceManifest,
                    artifactStore,
                    identityEvidenceVerifier);
                if (!string.Equals(bindingDigest, receipt.BindingDigestSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The evaluated runtime binding digest is stale or foreign.");
                }
                if (!string.Equals(policy.ProfileId, binding.ProfileId, StringComparison.Ordinal)
                    || !string.Equals(policy.PolicyVersion, binding.PolicyVersion, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The native runtime policy registration is missing or foreign.");
                }
                policy.ValidateBinding(binding, artifactStore);
                receipt.BindingValidationStatus = NativePageRuntimeContract.BindingValid;
            }
            catch (NativePageRuntimeIdentityEvidenceUnverifiedException exception)
            {
                receipt.BindingValidationStatus = NativePageRuntimeContract.BindingIncomplete;
                receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Pending;
                receipt.AcceptanceStatus = MigrationAcceptanceStatus.Pending;
                receipt.ProvenanceStatus = ProducerBuildProvenanceContract.Unverified;
                receipt.ReasonCodes.Add("IDENTITY_EVIDENCE_NOT_INDEPENDENTLY_VERIFIED:" + exception.Message);
                NativePageRuntimeBindingValidator.SealAcceptanceReceipt(receipt);
                return receipt;
            }
            catch (InvalidDataException exception)
            {
                receipt.BindingValidationStatus = NativePageRuntimeContract.BindingInvalid;
                receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Pending;
                receipt.AcceptanceStatus = MigrationAcceptanceStatus.Pending;
                receipt.ProvenanceStatus = ProducerBuildProvenanceContract.Unverified;
                receipt.ReasonCodes.Add("RUNTIME_BINDING_INVALID:" + exception.Message);
                NativePageRuntimeBindingValidator.SealAcceptanceReceipt(receipt);
                return receipt;
            }

            var provenanceReceipt = VerifyProvenance(
                provenanceManifest,
                provenanceVerifier ?? new UnverifiedProducerBuildProvenanceVerifier(),
                receipt);
            receipt.ProvenanceStatus = provenanceReceipt.VerificationStatus;
            receipt.ProducerBuildProvenanceReceiptDigestSha256 = provenanceReceipt.ContentSha256;

            if (externalEvidence == null)
            {
                receipt.BindingValidationStatus = NativePageRuntimeContract.BindingIncomplete;
                receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Pending;
                receipt.AcceptanceStatus = MigrationAcceptanceStatus.Pending;
                receipt.ReasonCodes.Add("NATIVE_RUNTIME_EXTERNAL_EVIDENCE_MISSING");
                NativePageRuntimeBindingValidator.SealAcceptanceReceipt(receipt);
                return receipt;
            }

            RuntimeVerificationStatus semanticStatus;
            try
            {
                receipt.ExternalEvidenceDigestSha256 = NativePageRuntimeBindingValidator.ValidateExternalEvidenceAndComputeDigest(
                    externalEvidence,
                    binding,
                    policy,
                    artifactStore,
                    out semanticStatus);
            }
            catch (InvalidDataException exception)
            {
                receipt.BindingValidationStatus = NativePageRuntimeContract.BindingInvalid;
                receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Pending;
                receipt.AcceptanceStatus = MigrationAcceptanceStatus.Pending;
                receipt.ReasonCodes.Add("RUNTIME_EVIDENCE_INVALID:" + exception.Message);
                NativePageRuntimeBindingValidator.SealAcceptanceReceipt(receipt);
                return receipt;
            }

            if (semanticStatus == RuntimeVerificationStatus.Failed)
            {
                receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Failed;
                receipt.AcceptanceStatus = ClassicWikiImportStatusPolicy.Acceptance(
                    storagePassed: true,
                    runtimeStatus: RuntimeVerificationStatus.Failed,
                    hasExplicitExclusions: receipt.ExplicitExclusions);
                receipt.ReasonCodes.Add("NATIVE_RUNTIME_TERMINAL_NEGATIVE");
            }
            else if (!string.Equals(provenanceReceipt.VerificationStatus, ProducerBuildProvenanceContract.Verified, StringComparison.Ordinal))
            {
                receipt.BindingValidationStatus = NativePageRuntimeContract.BindingIncomplete;
                receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Pending;
                receipt.AcceptanceStatus = MigrationAcceptanceStatus.Pending;
                receipt.ReasonCodes.Add("PRODUCER_BUILD_PROVENANCE_NOT_VERIFIED");
            }
            else
            {
                receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Passed;
                receipt.AcceptanceStatus = ClassicWikiImportStatusPolicy.Acceptance(
                    storagePassed: true,
                    runtimeStatus: RuntimeVerificationStatus.Passed,
                    hasExplicitExclusions: receipt.ExplicitExclusions);
                receipt.ReasonCodes.Add(receipt.ExplicitExclusions
                    ? "NATIVE_RUNTIME_PASSED_WITH_EXCLUSIONS"
                    : "NATIVE_RUNTIME_PASSED");
            }

            AddEvidence(receipt, binding.PackageEvidence);
            AddEvidence(receipt, binding.NativeImportEvidence);
            AddEvidence(receipt, binding.PolicyArtifact);
            AddEvidence(receipt, binding.SourceIdentityEvidence?.Artifact);
            AddEvidence(receipt, binding.TargetIdentityEvidence?.Artifact);
            AddEvidence(receipt, externalEvidence.PreCaptureTargetReadback?.Artifact);
            AddEvidence(receipt, externalEvidence.PostCaptureTargetReadback?.Artifact);
            NativePageRuntimeBindingValidator.SealAcceptanceReceipt(receipt);
            return receipt;
        }

        private static ProducerBuildProvenanceReceipt VerifyProvenance(
            ProducerBuildProvenanceManifest manifest,
            IProducerBuildProvenanceVerifier verifier,
            NativePageRuntimeAcceptanceReceipt acceptance)
        {
            try
            {
                var result = verifier.Verify(manifest);
                ProducerBuildProvenanceContract.ValidateReceiptAndComputeDigest(result, manifest);
                return result;
            }
            catch (InvalidDataException exception)
            {
                acceptance.ReasonCodes.Add("PROVENANCE_RECEIPT_INVALID:" + exception.Message);
                return ProducerBuildProvenanceContract.CreateUnverified(
                    manifest,
                    "PROVENANCE_RECEIPT_INVALID");
            }
        }

        private static NativePageRuntimeAcceptanceReceipt CreateBaseReceipt(
            ClassicWikiMigrationPackage package,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            NativePageRuntimeBinding binding,
            string evaluatorId,
            string evaluatorImplementationRef,
            DateTimeOffset evaluatedAtUtc)
        {
            return new NativePageRuntimeAcceptanceReceipt
            {
                EvaluatedAtUtc = evaluatedAtUtc,
                EvaluatorId = evaluatorId,
                EvaluatorImplementationRef = evaluatorImplementationRef,
                RunId = binding?.RunId ?? Guid.Empty,
                ClaimId = binding?.ClaimId,
                Subject = binding?.Subject,
                PackageDigestSha256 = binding?.PackageEvidence?.Sha256,
                SnapshotDigestSha256 = package?.SnapshotDigest,
                AdmittedPlanDigestSha256 = admittedPlanDigestSha256,
                ImportReceiptDigestSha256 = importAggregate?.ReceiptDigestSha256,
                BindingDigestSha256 = binding?.ContentSha256,
                StorageVerificationStatus = importAggregate?.ClassicWikiReceipt?.StorageVerificationStatus
                    ?? StorageVerificationStatus.Failed,
                RuntimeVerificationStatus = RuntimeVerificationStatus.Pending,
                AcceptanceStatus = MigrationAcceptanceStatus.Pending,
                ProvenanceStatus = ProducerBuildProvenanceContract.Unverified,
                ExplicitExclusions = ClassicWikiFreshVerification.HasExplicitExclusions(package)
            };
        }

        private static void AddEvidence(
            NativePageRuntimeAcceptanceReceipt receipt,
            NativePageRuntimeArtifactReference artifact)
        {
            if (artifact != null)
            {
                receipt.EvidenceReferences.Add(artifact);
            }
        }
    }
}
