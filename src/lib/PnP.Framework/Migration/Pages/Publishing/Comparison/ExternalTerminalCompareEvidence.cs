using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity.External;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Comparison
{
    internal static class ExternalTerminalCompareContract
    {
        public const string IntakeSchema = "pnp-page-compare-external-terminal-evidence/v1";
        public const string AdmissionSchema = "pnp-page-compare-external-terminal-admission/v1";
        public const string ManifestSchema = "ccd.shared-target-lifecycle-manifest/v1";
        public const string BrowserSchema = "ccd.browser-runtime-review/v1";
        public const string CleanupUnavailableSchema = "ccd.lifecycle-cleanup-transport-observation/v1";
        public const string Scope = "single-ingredient-terminal";
    }

    // References to original bytes, never relabeled PnP receipt objects.
    internal sealed class ExternalTerminalCompareEvidence
    {
        public string SchemaVersion { get; set; } = ExternalTerminalCompareContract.IntakeSchema;
        public ArtifactReference Manifest { get; set; }
        public ArtifactReference FreshReadback { get; set; }
        public ArtifactReference BrowserReview { get; set; }
        public ArtifactReference CleanupUnavailable { get; set; }
        public ArtifactReference PostCleanup { get; set; }
    }

    // The trusted consumer obtains these pins independently of the artifacts.
    // Reuse CCD-559 identity types; this is NOT a plan admission or maturity gate.
    internal sealed class ExternalTerminalCompareAdmission
    {
        public string SchemaVersion { get; set; } = ExternalTerminalCompareContract.AdmissionSchema;
        public IngredientExternalPlanBinding Binding { get; set; }
        public IngredientExternalTargetIdentity Target { get; set; }
        public string ConsumerIssue { get; set; }
        public string ImplementationBranch { get; set; }
        public string ImplementationTree { get; set; }
        public string BrowserReviewIssue { get; set; }
        public string BrowserReviewRunId { get; set; }
        public DateTimeOffset ExecutionWindowStartUtc { get; set; }
        public DateTimeOffset ExecutionWindowEndUtc { get; set; }
        public string ManifestSha256 { get; set; }
        public string FreshReadbackSha256 { get; set; }
        public string BrowserReviewSha256 { get; set; }
        public string CleanupUnavailableSha256 { get; set; }
        public string PostCleanupSha256 { get; set; }
        public ExternalCompareBrowserPolicy BrowserPolicy { get; set; }
        public int ReadbackMaximumAttempts { get; set; }
        public int ReadbackTimeoutMilliseconds { get; set; }
    }

    // Schema-specific browser requirements, not domain normalization. The
    // independent caller supplies the required surface; the lane cannot replace
    // this policy by editing the receipt's gates or its self-authored verdict.
    internal sealed class ExternalCompareBrowserPolicy
    {
        public int MinimumBodyTextLength { get; set; }
        public bool RequireAspNetForm { get; set; }
        public bool RequireClassicSurface { get; set; }
        public bool RequireWikiRuntimeSignal { get; set; }
    }

    public sealed class CompareExternalEvidenceSummary
    {
        public string Scope { get; set; }
        public string AdmissionDigestSha256 { get; set; }
        public string ClaimId { get; set; }
        public string Subtype { get; set; }
        public string SemanticRole { get; set; }
        public string PrimaryOwnerLane { get; set; }
        public string SourcePageUrl { get; set; }
        public string SourceListId { get; set; }
        public int SourceItemId { get; set; }
        public string SourceFileUniqueId { get; set; }
        public string SourceETag { get; set; }
        public string TargetUrl { get; set; }
        public string TargetListId { get; set; }
        public string TargetETag { get; set; }
        public string ImplementationCommit { get; set; }
        public string ImplementationTree { get; set; }
        public string LifecycleRunId { get; set; }
        public string LifecycleDigestSha256 { get; set; }
        public string TargetMappingDigestSha256 { get; set; }
        public string NativeOperationId { get; set; }
        public DateTimeOffset ExecutionWindowStartUtc { get; set; }
        public DateTimeOffset ExecutionWindowEndUtc { get; set; }
        public DateTimeOffset ReadbackAtUtc { get; set; }
        public DateTimeOffset BrowserObservedAtUtc { get; set; }
        public string BrowserReviewIssue { get; set; }
        public string BrowserReviewRunId { get; set; }
        public string BrowserVerdict { get; set; }
        public string BrowserSurfaceClassification { get; set; }
        public IList<string> BrowserFailedGates { get; set; }
        public string StorageIdentityStatus { get; set; }
        public string PlanEvidenceStatus { get; set; }
        public string PlanSchemaVersion { get; set; }
        public string DomainComparisonStatus { get; set; }
        public string NativeCleanupReceiptStatus { get; set; }
        public string NativeCleanupReceiptDigestSha256 { get; set; }
        public string PostCleanupStatus { get; set; }
        public IList<CompareExternalArtifactBinding> Artifacts { get; set; }
    }

    public sealed class CompareExternalArtifactBinding
    {
        public string Role { get; set; }
        public string SchemaVersion { get; set; }
        public string RawDigestSha256 { get; set; }
        public long Length { get; set; }
    }
}
