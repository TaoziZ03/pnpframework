using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public sealed class NativePageRuntimeCaptureProducer
    {
        public string AdapterId { get; set; }
        public string Version { get; set; }
        public string ImplementationRef { get; set; }
        public string ToolVersion { get; set; }
        public string ProtocolVersion { get; set; }
    }

    public sealed class NativePageRuntimeAttempt
    {
        public int Sequence { get; set; }
        public int MaximumAttempts { get; set; }
        public DateTimeOffset ObservedAtUtc { get; set; }
        public Guid RunId { get; set; }
        public Guid RuntimeOperationId { get; set; }
        public string RequestedUrl { get; set; }
        public string FinalUrl { get; set; }
        public int? HttpStatusCode { get; set; }
        public bool TransportUnavailable { get; set; }
        public string RequestId { get; set; }
        public string RequestIdAvailability { get; set; }
        public string RequestIdUnavailableReason { get; set; }
        public string ProviderId { get; set; }
        public string ProviderVersion { get; set; }
        public string SemanticDetectorId { get; set; }
        public string SemanticDetectorVersion { get; set; }
        public string SemanticDetectorDigestSha256 { get; set; }
        public string SemanticResult { get; set; }
        public string BrowserContextId { get; set; }
        public IList<NativePageRuntimeArtifactReference> RawEvidence { get; set; } = new List<NativePageRuntimeArtifactReference>();
    }

    public sealed class NativePageRuntimeTerminalObservation
    {
        public string Kind { get; set; }
        public int? HttpStatusCode { get; set; }
        public string SemanticDetectorResult { get; set; }
        public string ReasonCode { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Binding-bound runtime observation. It carries either an existing
    /// canonical receipt or a terminal negative observation, never an
    /// acceptance decision.
    /// </summary>
    public sealed class ExternalPageRuntimeEvidence
    {
        public string SchemaVersion { get; set; } = NativePageRuntimeContract.ExternalEvidenceSchemaVersion;
        public string ContentSha256 { get; set; }
        public Guid RunId { get; set; }
        public string ClaimId { get; set; }
        public string BindingDigestSha256 { get; set; }
        public NativePageRuntimeCaptureProducer CaptureProducer { get; set; }
        public string ProfileClass { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset CompletedAtUtc { get; set; }
        public string ResultKind { get; set; }
        public NativePageRuntimeTargetIdentity ObservedTargetIdentity { get; set; }
        public NativePageRuntimeTargetIdentityEvidence PreCaptureTargetReadback { get; set; }
        public NativePageRuntimeTargetIdentityEvidence PostCaptureTargetReadback { get; set; }
        public IList<NativePageRuntimeAttempt> Attempts { get; set; } = new List<NativePageRuntimeAttempt>();
        public MigrationArtifactManifest ArtifactManifest { get; set; }
        public RuntimeVerificationReceipt RuntimeReceipt { get; set; }
        public string RuntimeReceiptDigestSha256 { get; set; }
        public NativePageRuntimeTerminalObservation TerminalObservation { get; set; }
        public IDictionary<string, string> Extensions { get; set; } = new Dictionary<string, string>();
    }
}
