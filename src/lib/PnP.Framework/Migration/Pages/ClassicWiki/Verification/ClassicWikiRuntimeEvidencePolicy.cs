using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public sealed class ClassicWikiRuntimeEvidencePolicy : INativePageRuntimeEvidencePolicy
    {
        public string ProfileId => NativePageRuntimeContract.ClassicWikiProfile;

        public NativePageRuntimePolicyDecision Decide(
            NativePageRuntimeBinding binding,
            bool hasExplicitExclusions)
        {
            if (binding == null)
            {
                throw new InvalidDataException("A Classic Wiki native runtime binding is required.");
            }
            if (!string.Equals(binding.ProfileId, ProfileId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The runtime binding profile is foreign to the Classic Wiki policy.");
            }
            if (!string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.NativeAuthority, StringComparison.Ordinal))
            {
                return new NativePageRuntimePolicyDecision
                {
                    RuntimeVerificationStatus = RuntimeVerificationStatus.Pending,
                    AcceptanceStatus = MigrationAcceptanceStatus.Pending,
                    DecisionCode = "NATIVE_RUNTIME_EVIDENCE_PENDING"
                };
            }

            switch (binding.RuntimeVerificationStatus)
            {
                case RuntimeVerificationStatus.Passed:
                case RuntimeVerificationStatus.NotRequired:
                    return new NativePageRuntimePolicyDecision
                    {
                        RuntimeVerificationStatus = binding.RuntimeVerificationStatus,
                        AcceptanceStatus = hasExplicitExclusions
                            ? MigrationAcceptanceStatus.PartiallyAccepted
                            : MigrationAcceptanceStatus.Accepted,
                        DecisionCode = hasExplicitExclusions
                            ? "NATIVE_RUNTIME_PASSED_WITH_EXCLUSIONS"
                            : "NATIVE_RUNTIME_PASSED"
                    };
                case RuntimeVerificationStatus.Failed:
                    return new NativePageRuntimePolicyDecision
                    {
                        RuntimeVerificationStatus = RuntimeVerificationStatus.Failed,
                        AcceptanceStatus = MigrationAcceptanceStatus.Rejected,
                        DecisionCode = "NATIVE_RUNTIME_FAILED"
                    };
                case RuntimeVerificationStatus.Pending:
                case RuntimeVerificationStatus.NotRun:
                    return new NativePageRuntimePolicyDecision
                    {
                        RuntimeVerificationStatus = RuntimeVerificationStatus.Pending,
                        AcceptanceStatus = MigrationAcceptanceStatus.Pending,
                        DecisionCode = "NATIVE_RUNTIME_EVIDENCE_PENDING"
                    };
                default:
                    throw new InvalidDataException("The runtime verification status is unsupported.");
            }
        }
    }
}
