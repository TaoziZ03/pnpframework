using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public static class ClassicWikiNativeRuntimeAcceptance
    {
        public static NativePageRuntimeAcceptanceReceipt Decide(
            NativePageRuntimeBinding binding,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            NativePageRuntimeTargetIdentity expectedTarget,
            INativePageRuntimeEvidencePolicy policy,
            bool hasExplicitExclusions,
            RuntimeVerificationReceipt runtimeReceipt = null,
            RuntimeVerificationManifest requirementsManifest = null,
            string expectedImplementationRef = null,
            IMigrationArtifactStore artifactStore = null,
            ExternalPageRuntimeEvidence externalEvidence = null,
            ProducerBuildProvenanceReceipt provenanceReceipt = null,
            ProducerBuildProvenanceManifest provenanceManifest = null,
            DateTimeOffset? decidedAtUtc = null)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (!string.Equals(policy.ProfileId, NativePageRuntimeContract.ClassicWikiProfile, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The native runtime evidence policy is foreign to Classic Wiki.");
            }

            var bindingDigest = NativePageRuntimeBindingValidator.ValidateCoreAndComputeDigest(
                binding,
                admittedPlan,
                admittedPlanDigestSha256,
                importAggregate,
                expectedTarget);
            if (string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.NativeAuthority, StringComparison.Ordinal))
            {
                NativePageRuntimeBindingValidator.ValidateNativeEvidence(
                    binding,
                    runtimeReceipt,
                    requirementsManifest,
                    expectedImplementationRef,
                    artifactStore,
                    admittedPlan);
            }
            else if (string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.ExternalAuthority, StringComparison.Ordinal))
            {
                NativePageRuntimeBindingValidator.ValidateExternalEvidence(externalEvidence, binding);
            }

            var decision = policy.Decide(binding, hasExplicitExclusions);
            var provenanceStatus = ProducerBuildProvenanceContract.Unverified;
            if (provenanceReceipt != null || provenanceManifest != null)
            {
                if (provenanceReceipt == null || provenanceManifest == null)
                {
                    throw new InvalidDataException("Producer provenance manifest and receipt must be supplied together.");
                }
                var provenanceDigest = ProducerBuildProvenanceContract.ValidateReceiptAndComputeDigest(
                    provenanceReceipt,
                    provenanceManifest);
                if (!string.Equals(
                    provenanceDigest,
                    binding.ProducerBuildProvenanceReceiptDigestSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The runtime binding producer provenance receipt is foreign or stale.");
                }
                provenanceStatus = provenanceReceipt.VerificationStatus;
            }

            var receipt = new NativePageRuntimeAcceptanceReceipt
            {
                DecidedAtUtc = decidedAtUtc ?? DateTimeOffset.UtcNow,
                Binding = binding,
                BindingDigestSha256 = bindingDigest,
                RuntimeVerificationStatus = decision.RuntimeVerificationStatus,
                AcceptanceStatus = decision.AcceptanceStatus,
                DecisionCode = decision.DecisionCode,
                ProducerBuildProvenanceStatus = provenanceStatus
            };
            if (!string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.NativeAuthority, StringComparison.Ordinal))
            {
                receipt.Diagnostics.Add("Only canonical native runtime evidence can change Pending acceptance.");
            }
            return receipt;
        }
    }
}
