using System;

namespace PnP.Framework.Migration.Verification
{
    public sealed class RuntimeVerificationAssertionResult
    {
        public string AssertionId { get; set; }

        public string PlanDigest { get; set; }

        public string TargetIdentity { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public RuntimeVerificationStateEvidence InitialStateEvidence { get; set; }

        public RuntimeVerificationStateEvidence ActionEvidence { get; set; }

        public RuntimeVerificationStateEvidence FinalStateEvidence { get; set; }

        public bool Passed { get; set; }

        public string FailureReasonCode { get; set; }

        public string Message { get; set; }
    }

    public sealed class RuntimeVerificationStateEvidence
    {
        public string Stage { get; set; }

        public string AssertionId { get; set; }

        public string ActionId { get; set; }

        public string PlanDigest { get; set; }

        public string TargetIdentity { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public string ObservedStateDigestSha256 { get; set; }

        public string EvidenceArtifactSha256 { get; set; }
    }
}
