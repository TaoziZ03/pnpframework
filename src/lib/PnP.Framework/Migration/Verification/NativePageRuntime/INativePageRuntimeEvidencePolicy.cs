using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Verification;
using System;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public interface INativePageRuntimeEvidencePolicy
    {
        string ProfileId { get; }

        string PolicyVersion { get; }

        void ValidateBinding(
            NativePageRuntimeBinding binding,
            IMigrationArtifactStore artifactStore);

        bool VerifyResult(
            NativePageRuntimeBinding binding,
            RuntimeVerificationResult result,
            IMigrationArtifactStore artifactStore);
    }

    /// <summary>
    /// Independent trust boundary for source and target identity observations.
    /// Reopenable caller-authored DTO bytes are evidence inputs, not authority.
    /// </summary>
    public interface INativePageRuntimeIdentityEvidenceVerifier
    {
        string VerifierId { get; }

        string ImplementationRef { get; }

        void Verify(
            NativePageRuntimeSourceIdentityEvidence sourceEvidence,
            NativePageRuntimeTargetIdentityEvidence targetEvidence,
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan,
            NativePageImportReceiptAggregate importAggregate);
    }

    public sealed class UnverifiedNativePageRuntimeIdentityEvidenceVerifier : INativePageRuntimeIdentityEvidenceVerifier
    {
        public UnverifiedNativePageRuntimeIdentityEvidenceVerifier(string verifierId, string implementationRef)
        {
            VerifierId = verifierId;
            ImplementationRef = implementationRef;
        }

        public string VerifierId { get; }

        public string ImplementationRef { get; }

        public void Verify(
            NativePageRuntimeSourceIdentityEvidence sourceEvidence,
            NativePageRuntimeTargetIdentityEvidence targetEvidence,
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan,
            NativePageImportReceiptAggregate importAggregate)
        {
            throw new NativePageRuntimeIdentityEvidenceUnverifiedException(
                "Source and target identity evidence has not been independently verified.");
        }
    }

    public sealed class NativePageRuntimeIdentityEvidenceUnverifiedException : Exception
    {
        public NativePageRuntimeIdentityEvidenceUnverifiedException(string message) : base(message)
        {
        }
    }
}
