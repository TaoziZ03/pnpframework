using System;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    /// <summary>
    /// Supplemental report supplied by a non-native producer. It can be bound
    /// into a receipt for audit, but it is never acceptance authority.
    /// </summary>
    public sealed class ExternalPageRuntimeEvidence
    {
        public string SchemaVersion { get; set; } = NativePageRuntimeContract.ExternalEvidenceSchemaVersion;

        public string ProfileId { get; set; } = NativePageRuntimeContract.ClassicWikiProfile;

        public string ReportId { get; set; }

        public Guid RuntimeOperationId { get; set; }

        public string SourceVersionDigestSha256 { get; set; }

        public string AdmittedPlanDigestSha256 { get; set; }

        public string ImportReceiptDigestSha256 { get; set; }

        public NativePageRuntimeTargetIdentity Target { get; set; }

        public string RequestedUrl { get; set; }

        public string FinalUrl { get; set; }

        public string EvidenceDigestSha256 { get; set; }

        public RuntimeVerificationStatus ClaimedStatus { get; set; } = RuntimeVerificationStatus.Pending;

        public DateTimeOffset ObservedAtUtc { get; set; }

        public string ReportDigestSha256 { get; set; }
    }
}
