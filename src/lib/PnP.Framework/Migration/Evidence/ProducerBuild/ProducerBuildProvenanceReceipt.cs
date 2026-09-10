using System;

namespace PnP.Framework.Migration.Evidence.ProducerBuild
{
    public sealed class ProducerBuildProvenanceReceipt
    {
        public string SchemaVersion { get; set; } = ProducerBuildProvenanceContract.ReceiptSchemaVersion;

        public string ManifestDigestSha256 { get; set; }

        public string BinarySha256 { get; set; }

        public string VerificationStatus { get; set; } = ProducerBuildProvenanceContract.Unverified;

        public string VerifierId { get; set; }

        public DateTimeOffset VerifiedAtUtc { get; set; }

        public string Reason { get; set; }
    }
}
