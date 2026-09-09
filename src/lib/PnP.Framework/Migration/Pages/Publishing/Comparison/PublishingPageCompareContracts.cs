using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Publishing.Comparison
{
    public static class PublishingPageCompareContract
    {
        public const string SchemaVersion = "pnp-page-compare-report/v1";
        public const string RuntimeReceiptSchemaVersion = "pnp-migration-runtime-verification-receipt/v1";
        public const string RuntimeManifestSchemaVersion = "pnp-migration-runtime-verification/v1";
        public const string AssessmentHandoffSchemaVersion = "ccd-aspx-capture-handoff/1.0.0";
        public const string AssessmentProducerRevisionId = "fa6aa144-2abe-41dc-b25d-740e2230070e";
        public const string AssessmentConformanceRevisionId = "f8366f5a-8012-4b4c-93d0-25150849dc8e";
        public const string UnavailableByProducerContract = "unavailable_by_producer_contract";

        public static class ResultClasses
        {
            public const string Exact = "exact";
            public const string CanonicalEquivalent = "canonical-equivalent";
            public const string TransformedAsPlanned = "transformed-as-planned";
            public const string SourceVersionChanged = "source-version-changed";
            public const string Missing = "missing";
            public const string UnexpectedExtra = "unexpected-extra";
            public const string Mismatch = "mismatch";
            public const string Deferred = "deferred";
            public const string AuthorizationBlocked = "authorization-blocked";
            public const string RuntimePending = "runtime-pending";
            public const string AuthExpired = "auth-expired";
            public const string Unknown = "unknown";
            public const string NotApplicable = "not-applicable";
        }

        public static class ReasonCodes
        {
            public const string ExactRawDigest = "compare.raw.exact";
            public const string CanonicalRulesEquivalent = "compare.canonical.equivalent";
            public const string ApprovedTransformMatched = "compare.transform.approved";
            public const string SourceVersionDrift = "source.version.changed";
            public const string TargetMissing = "target.material.missing";
            public const string TargetUnexpectedExtra = "target.material.unexpected-extra";
            public const string TargetMismatch = "target.material.mismatch";
            public const string FrontierDeferred = "frontier.deferred";
            public const string FrontierAuthorizationBlocked = "frontier.authorization-blocked";
            public const string RuntimeReceiptPending = "runtime.receipt.pending";
            public const string SourceAuthExpired = "source.auth.expired";
            public const string SourcePermissionDenied = "source.permission.denied";
            public const string SourcePermissionUnknown = "source.permission.unknown";
            public const string EvidenceIncomplete = "evidence.incomplete";
            public const string AssertionNotApplicable = "assertion.not-applicable";
            public const string StorageFailed = "acceptance.storage.failed";
            public const string StorageNotRun = "acceptance.storage.not-run";
            public const string RuntimeFailed = "acceptance.runtime.failed";
            public const string MaterialGap = "acceptance.material-gap";
            public const string MaterialUnknown = "acceptance.material-unknown";
        }
    }

    public sealed class PublishingPageCompareReport
    {
        public string SchemaVersion { get; set; } = PublishingPageCompareContract.SchemaVersion;
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public CompareProducer Producer { get; set; }
        public AssessmentHandoffProjection AssessmentHandoff { get; set; }
        public CompareBindings Bindings { get; set; }
        public SourceVersionComparison SourceVersionComparison { get; set; }
        public CompareTargetIdentity TargetIdentity { get; set; }
        public CompareStorageSummary Storage { get; set; }
        public CompareRuntimeSummary Runtime { get; set; }
        public IList<IngredientCompareResult> Ingredients { get; set; } = new List<IngredientCompareResult>();
        public CompareAcceptance Acceptance { get; set; }
        public string ReportDigestSha256 { get; set; }
    }

    public sealed class CompareProducer
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string ImplementationRef { get; set; }
    }

    public sealed class AssessmentHandoffProjection
    {
        public string SchemaVersion { get; set; }
        public string ProducerRevisionId { get; set; }
        public string ConformanceRevisionId { get; set; }
        public string HandoffDigestSha256 { get; set; }
        public string ManifestRevisionId { get; set; }
        public string AllowlistRevisionId { get; set; }
        public string SelectionId { get; set; }
        public string CanonicalLocatorHash { get; set; }
        public string ResourceIdentityHash { get; set; }
        public string ApprovedHostHash { get; set; }
        public string PermissionSignal { get; set; }
        public string DiscoveryObservationStatus { get; set; }
        public string ProducerAttestationStatus { get; set; }
        public string CoverageStatus { get; set; }
    }

    public sealed class CompareBindings
    {
        public string ManifestDigestSha256 { get; set; }
        public string SourceCaptureReceiptDigestSha256 { get; set; }
        public string ExportSchemaVersion { get; set; }
        public string SnapshotDigestSha256 { get; set; }
        public string IngredientGraphSchemaVersion { get; set; }
        public string IngredientProjectionVersion { get; set; }
        public string MigrationPackageSchemaVersion { get; set; }
        public string PlanDigestSha256 { get; set; }
        public string AdmittedPlanDigestSha256 { get; set; }
        public string SourceVersionDigestSha256 { get; set; }
        public ReproOperationIds Operations { get; set; }
        public string ImportReceiptSchemaVersion { get; set; }
        public string ImportReceiptDigestSha256 { get; set; }
        public string RuntimeReceiptSchemaVersion { get; set; }
        public string RuntimeReceiptDigestSha256 { get; set; }
    }

    public sealed class SourceVersionComparison
    {
        public string IdentityDigestSha256 { get; set; }
        public string ExpectedCompositeDigestSha256 { get; set; }
        public string ObservedCompositeDigestSha256 { get; set; }
        public string ObservedETag { get; set; }
        public DateTimeOffset? ObservedLastModifiedUtc { get; set; }
        public string ObservedVersionLabel { get; set; }
        public DateTimeOffset ObservedAtUtc { get; set; }
        public string Status { get; set; }
    }

    public sealed class CompareTargetIdentity
    {
        public string WebUrlHashSha256 { get; set; }
        public string PageServerRelativeUrlHashSha256 { get; set; }
        public Guid? FileUniqueId { get; set; }
        public int ListItemId { get; set; }
        public string VersionLabel { get; set; }

        [JsonIgnore]
        public string CanonicalIdentity { get; set; }
    }

    public sealed class CompareStorageSummary
    {
        public string Status { get; set; }
        public bool FreshReadback { get; set; }
    }

    public sealed class CompareRuntimeSummary
    {
        public string Status { get; set; }
    }

    public sealed class IngredientCompareResult
    {
        public string IngredientId { get; set; }
        public string Kind { get; set; }
        public bool Material { get; set; }
        public IngredientCompareLineage Lineage { get; set; }
        public CompareDigestPair Expected { get; set; }
        public CompareDigestPair Actual { get; set; }
        public string ResultClass { get; set; }
        public string ReasonCode { get; set; }
        public string Message { get; set; }
    }

    public sealed class IngredientCompareLineage
    {
        public string RequestedUrlHashSha256 { get; set; }
        public string SourceArtifactDigestSha256 { get; set; }
        public string SourceIngredientId { get; set; }
        public string ActionId { get; set; }
        public string TargetIdentity { get; set; }
        public IList<string> EvidenceRefs { get; set; } = new List<string>();
        public IList<string> CauseIngredientIds { get; set; } = new List<string>();
    }

    public sealed class CompareDigestPair
    {
        public string RawDigestSha256 { get; set; }
        public string CanonicalDigestSha256 { get; set; }
    }

    public sealed class CompareAcceptance
    {
        public string Verdict { get; set; }
        public IList<string> ReasonCodes { get; set; } = new List<string>();
        public string StorageStatus { get; set; }
        public string RuntimeStatus { get; set; }
    }

    public sealed class PublishingPageCompareRequest
    {
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public CompareProducer Producer { get; set; }
        public AssessmentCaptureHandoff AssessmentHandoff { get; set; }
        public string AssessmentProducerRevisionId { get; set; }
        public string AssessmentHandoffDigestSha256 { get; set; }
        public PublishingPageMigrationPackage Package { get; set; }
        public AdmittedReproExecutionPlan AdmittedPlan { get; set; }
        public string AdmittedPlanDigestSha256 { get; set; }
        public PublishingPageImportReceipt ImportReceipt { get; set; }
        public string ImportReceiptDigestSha256 { get; set; }
        public RuntimeVerificationReceipt RuntimeReceipt { get; set; }
        public string RuntimeReceiptDigestSha256 { get; set; }
        public CompareBindings Bindings { get; set; }
        public SourceVersionComparison SourceVersionComparison { get; set; }
        public string SourceAuthState { get; set; } = "valid";
        public CompareTargetIdentity PlannedTargetIdentity { get; set; }
        public IList<IngredientCompareObservation> Ingredients { get; set; } = new List<IngredientCompareObservation>();
    }

    public sealed class AssessmentCaptureHandoff
    {
        [JsonPropertyName("schema")]
        public string Schema { get; set; }

        [JsonPropertyName("manifest_revision_id")]
        public string ManifestRevisionId { get; set; }

        [JsonPropertyName("allowlist_revision_id")]
        public string AllowlistRevisionId { get; set; }

        [JsonPropertyName("sample_id")]
        public string SampleId { get; set; }

        [JsonPropertyName("stage_membership")]
        public IList<string> StageMembership { get; set; } = new List<string>();

        [JsonPropertyName("canonical_locator_ref")]
        public string CanonicalLocatorRef { get; set; }

        [JsonPropertyName("canonical_locator_hash")]
        public string CanonicalLocatorHash { get; set; }

        [JsonPropertyName("resource_identity_hash")]
        public string ResourceIdentityHash { get; set; }

        [JsonPropertyName("approved_host_hash")]
        public string ApprovedHostHash { get; set; }

        [JsonPropertyName("stratum_id")]
        public string StratumId { get; set; }

        [JsonPropertyName("expected_profile")]
        public string ExpectedProfile { get; set; }

        [JsonPropertyName("permission_signal")]
        public string PermissionSignal { get; set; }

        [JsonPropertyName("redirect_signal")]
        public string RedirectSignal { get; set; }

        [JsonPropertyName("unknown_signals")]
        public IList<string> UnknownSignals { get; set; } = new List<string>();

        [JsonPropertyName("request_policy")]
        public AssessmentCaptureRequestPolicy RequestPolicy { get; set; }
    }

    public sealed class AssessmentCaptureRequestPolicy
    {
        [JsonPropertyName("methods")]
        public IList<string> Methods { get; set; } = new List<string>();

        [JsonPropertyName("auto_discovery")]
        public bool AutoDiscovery { get; set; }

        [JsonPropertyName("follow_redirects")]
        public string FollowRedirects { get; set; }

        [JsonPropertyName("max_redirects")]
        public int MaxRedirects { get; set; }

        [JsonPropertyName("mutation_allowed")]
        public bool MutationAllowed { get; set; }
    }

    public sealed class IngredientCompareObservation
    {
        public string IngredientId { get; set; }
        public string Kind { get; set; }
        public bool Material { get; set; } = true;
        public bool AssertionApplicable { get; set; } = true;
        public bool SourceVersionSensitive { get; set; } = true;
        public bool TargetEvidenceComplete { get; set; } = true;
        public bool TargetPresent { get; set; } = true;
        public bool UnexpectedExtra { get; set; }
        public string RuntimeRequirementId { get; set; }
        public string UnknownReasonCode { get; set; }
        public IngredientCompareLineage Lineage { get; set; }
        public CompareDigestPair Expected { get; set; } = new CompareDigestPair();
        public CompareDigestPair Actual { get; set; } = new CompareDigestPair();
        public string ApprovedTransformedCanonicalDigestSha256 { get; set; }
        public string Message { get; set; }
    }
}
