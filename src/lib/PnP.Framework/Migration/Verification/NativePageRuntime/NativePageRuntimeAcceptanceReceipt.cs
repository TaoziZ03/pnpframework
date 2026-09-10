using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public sealed class NativePageRuntimeAcceptanceReceipt
    {
        public string SchemaVersion { get; set; } = NativePageRuntimeContract.AcceptanceReceiptSchemaVersion;
        public string ContentSha256 { get; set; }
        public DateTimeOffset EvaluatedAtUtc { get; set; }
        public string EvaluatorId { get; set; }
        public string EvaluatorImplementationRef { get; set; }
        public Guid RunId { get; set; }
        public string ClaimId { get; set; }
        public NativePageRuntimeSubject Subject { get; set; }
        public string PackageDigestSha256 { get; set; }
        public string SnapshotDigestSha256 { get; set; }
        public string AdmittedPlanDigestSha256 { get; set; }
        public string ImportReceiptDigestSha256 { get; set; }
        public string BindingDigestSha256 { get; set; }
        public string ExternalEvidenceDigestSha256 { get; set; }
        public string ProducerBuildProvenanceReceiptDigestSha256 { get; set; }
        public string BindingValidationStatus { get; set; }
        public StorageVerificationStatus StorageVerificationStatus { get; set; }
        public RuntimeVerificationStatus RuntimeVerificationStatus { get; set; } = RuntimeVerificationStatus.Pending;
        public MigrationAcceptanceStatus AcceptanceStatus { get; set; } = MigrationAcceptanceStatus.Pending;
        public string ProvenanceStatus { get; set; }
        public bool ExplicitExclusions { get; set; }
        public IList<string> ReasonCodes { get; set; } = new List<string>();
        public IList<NativePageRuntimeArtifactReference> EvidenceReferences { get; set; } = new List<NativePageRuntimeArtifactReference>();
        public IDictionary<string, string> Extensions { get; set; } = new Dictionary<string, string>();
    }
}
