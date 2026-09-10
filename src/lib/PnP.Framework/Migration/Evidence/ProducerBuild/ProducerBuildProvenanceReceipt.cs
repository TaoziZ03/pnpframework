using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Evidence.ProducerBuild
{
    public sealed class ProducerBuildProvenanceReceipt
    {
        public string SchemaVersion { get; set; } = ProducerBuildProvenanceContract.ReceiptSchemaVersion;
        public string ContentSha256 { get; set; }
        public string ManifestDigestSha256 { get; set; }
        public string VerifierId { get; set; }
        public string VerifierImplementationRef { get; set; }
        public DateTimeOffset VerifiedAtUtc { get; set; }
        public string SourceBindingStatus { get; set; } = ProducerBuildProvenanceContract.Unverified;
        public string ArtifactHashStatus { get; set; } = ProducerBuildProvenanceContract.Unverified;
        public string RebuildStatus { get; set; } = ProducerBuildProvenanceContract.Unverified;
        public string HistoricalBinaryMatchStatus { get; set; } = ProducerBuildProvenanceContract.Unverified;
        public string VerificationStatus { get; set; } = ProducerBuildProvenanceContract.Unverified;
        public IList<ProducerBuildArtifact> ActualArtifacts { get; set; } = new List<ProducerBuildArtifact>();
        public IList<NativePageRuntimeArtifactReference> OpenedBytes { get; set; } = new List<NativePageRuntimeArtifactReference>();
        public IList<NativePageRuntimeArtifactReference> RebuildEvidence { get; set; } = new List<NativePageRuntimeArtifactReference>();
        public IList<string> ReasonCodes { get; set; } = new List<string>();
    }
}
