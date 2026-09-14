using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;

namespace Ccd263.RuntimeBrowser;

public sealed class BrowserRuntimeAdapterRequest
{
    public string SchemaVersion { get; set; }
    public string BindingPath { get; set; }
    public string ArtifactStorePath { get; set; }
    public string OutputPath { get; set; }
    public BrowserRuntimeExpectedIdentity Expected { get; set; }
    public NativePageRuntimeCaptureProducer CaptureProducer { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public NativePageRuntimeTargetIdentity ObservedTargetIdentity { get; set; }
    public NativePageRuntimeTargetIdentityEvidence PreCaptureTargetReadback { get; set; }
    public NativePageRuntimeTargetIdentityEvidence PostCaptureTargetReadback { get; set; }
    public IList<NativePageRuntimeAttempt> Attempts { get; set; } = new List<NativePageRuntimeAttempt>();
    public RuntimeBrowserContextIdentity BrowserContext { get; set; }
    public IList<RuntimeVerificationResult> Results { get; set; } = new List<RuntimeVerificationResult>();
    public NativePageRuntimeTerminalObservation TerminalObservation { get; set; }
    public IDictionary<string, string> Extensions { get; set; } = new Dictionary<string, string>();
}

public sealed class BrowserRuntimeExpectedIdentity
{
    public string BindingDigestSha256 { get; set; }
    public string SourceVersionDigestSha256 { get; set; }
    public string AdmittedPlanDigestSha256 { get; set; }
    public string ImportReceiptDigestSha256 { get; set; }
    public Guid RuntimeOperationId { get; set; }
    public Guid TargetFileUniqueId { get; set; }
    public int TargetListItemId { get; set; }
    public string TargetListItemVersion { get; set; }
    public string TargetCanonicalUrl { get; set; }
}

public sealed class BrowserRuntimeAdapterResult
{
    public string SchemaVersion { get; set; } = BrowserRuntimeEvidenceAdapter.ResultSchemaVersion;
    public string ExternalEvidenceSchemaVersion { get; set; }
    public string ExternalEvidenceDigestSha256 { get; set; }
    public string BindingDigestSha256 { get; set; }
    public string ResultKind { get; set; }
    public string OutputPath { get; set; }
}
