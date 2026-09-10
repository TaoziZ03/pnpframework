using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;

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
}
