namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public sealed class NativePageRuntimePolicyDecision
    {
        public RuntimeVerificationStatus RuntimeVerificationStatus { get; set; }

        public MigrationAcceptanceStatus AcceptanceStatus { get; set; }

        public string DecisionCode { get; set; }
    }

    public interface INativePageRuntimeEvidencePolicy
    {
        string ProfileId { get; }

        NativePageRuntimePolicyDecision Decide(
            NativePageRuntimeBinding binding,
            bool hasExplicitExclusions);
    }
}
