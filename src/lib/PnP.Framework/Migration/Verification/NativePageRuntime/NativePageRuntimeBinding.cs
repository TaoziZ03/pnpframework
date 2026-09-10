using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public sealed class NativePageRuntimeSubject
    {
        public string IngredientId { get; set; }

        public string PageIngredientKind { get; set; }

        public string Subtype { get; set; }

        public string SemanticRole { get; set; }

        public string PrimaryOwnerLane { get; set; }

        public IList<string> DependencyIds { get; set; } = new List<string>();
    }

    public sealed class NativePageRuntimeSourceIdentity
    {
        public Guid SiteId { get; set; }

        public Guid WebId { get; set; }

        public Guid ListId { get; set; }

        public int ListItemId { get; set; }

        public Guid FileUniqueId { get; set; }

        public string PageServerRelativeUrl { get; set; }
    }

    public sealed class NativePageRuntimeSourceIdentityEvidence
    {
        public string ObservationId { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public Guid OperationId { get; set; }

        public string AcquisitionMethod { get; set; }

        public string ProviderId { get; set; }

        public string ProviderVersion { get; set; }

        public string SourceVersionDigestSha256 { get; set; }

        public NativePageRuntimeSourceIdentity Identity { get; set; }

        public NativePageRuntimeArtifactReference Artifact { get; set; }
    }

    public sealed class NativePageRuntimeTargetIdentity
    {
        public Guid SiteId { get; set; }

        public Guid WebId { get; set; }

        public Guid ListId { get; set; }

        public string WebUrl { get; set; }

        public string PageServerRelativeUrl { get; set; }

        public Guid FileUniqueId { get; set; }

        public int ListItemId { get; set; }

        public string ListItemVersion { get; set; }

        public string ListItemETag { get; set; }

        public string CanonicalUrl { get; set; }
    }

    public sealed class NativePageRuntimeArtifactReference
    {
        public string Sha256 { get; set; }

        public long Length { get; set; }

        public string MediaType { get; set; }

        public string Locator { get; set; }
    }

    public sealed class NativePageRuntimeTargetIdentityEvidence
    {
        public string ObservationId { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public Guid OperationId { get; set; }

        public string ProviderId { get; set; }

        public string ProviderVersion { get; set; }

        public string SourceArtifactSha256 { get; set; }

        public NativePageRuntimeTargetIdentity Identity { get; set; }

        public NativePageRuntimeArtifactReference Artifact { get; set; }
    }

    public sealed class NativePageRuntimeBinding
    {
        public string SchemaVersion { get; set; } = NativePageRuntimeContract.BindingSchemaVersion;

        public string ContentSha256 { get; set; }

        public Guid RunId { get; set; }

        public string ClaimId { get; set; } = NativePageRuntimeContract.ClaimId;

        public NativePageRuntimeSubject Subject { get; set; }

        public string ProfileId { get; set; } = NativePageRuntimeContract.ClassicWikiProfile;

        public string PolicyVersion { get; set; } = NativePageRuntimeContract.ClassicWikiPolicyVersion;

        public NativePageRuntimeSourceIdentity SourceIdentity { get; set; }

        public NativePageRuntimeSourceIdentityEvidence SourceIdentityEvidence { get; set; }

        public CurrentSourceVersionIdentity SourceVersion { get; set; }

        public string SnapshotDigestSha256 { get; set; }

        public string PlanDigest { get; set; }

        public string AdmittedPlanDigestSha256 { get; set; }

        public string ImportReceiptDigestSha256 { get; set; }

        public ReproOperationIds Operations { get; set; }

        public NativePageRuntimeTargetIdentity TargetStorageIdentity { get; set; }

        public NativePageRuntimeTargetIdentityEvidence TargetIdentityEvidence { get; set; }

        public string IdentityEvidenceVerifierId { get; set; }

        public string IdentityEvidenceVerifierImplementationRef { get; set; }

        public NativePageRuntimeArtifactReference NativeImportEvidence { get; set; }

        public NativePageRuntimeArtifactReference PackageEvidence { get; set; }

        public NativePageRuntimeArtifactReference PolicyArtifact { get; set; }

        public RuntimeVerificationManifest RequirementsManifest { get; set; }

        public string RequirementsManifestDigestSha256 { get; set; }

        public string ExpectedAuthoredContentSha256 { get; set; }

        public DateTimeOffset IssuedAtUtc { get; set; }

        public DateTimeOffset CaptureNotBeforeUtc { get; set; }

        public DateTimeOffset CaptureExpiresAtUtc { get; set; }

        public string ImportProducerRef { get; set; }

        public string ContractProducerRef { get; set; }

        public string ContractProducerProvenanceManifestDigestSha256 { get; set; }

        public string ContractProducerProvenanceStatus { get; set; }

        public IDictionary<string, string> Extensions { get; set; } = new Dictionary<string, string>();
    }
}
