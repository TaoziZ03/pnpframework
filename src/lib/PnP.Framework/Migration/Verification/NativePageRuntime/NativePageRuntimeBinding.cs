using System;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public sealed class NativePageRuntimeTargetIdentity
    {
        public Guid SiteId { get; set; }

        public Guid WebId { get; set; }

        public string WebUrl { get; set; }

        public string PageServerRelativeUrl { get; set; }

        public Guid FileUniqueId { get; set; }

        public int ListItemId { get; set; }

        public string ListItemVersion { get; set; }

        public string ListItemETag { get; set; }
    }

    public sealed class NativePageRuntimeBinding
    {
        public string SchemaVersion { get; set; } = NativePageRuntimeContract.BindingSchemaVersion;

        public string ProfileId { get; set; } = NativePageRuntimeContract.ClassicWikiProfile;

        public string PageFamily { get; set; } = NativePageRuntimeContract.ClassicWikiFamily;

        public string RuntimeEvidenceAuthority { get; set; } = NativePageRuntimeContract.NoAuthority;

        public Guid RuntimeOperationId { get; set; }

        public string SourceIdentityDigestSha256 { get; set; }

        public string SourceVersionDigestSha256 { get; set; }

        public string AdmittedPlanDigestSha256 { get; set; }

        public string ImportReceiptDigestSha256 { get; set; }

        public NativePageRuntimeTargetIdentity Target { get; set; }

        public string RequestedUrl { get; set; }

        public string FinalUrl { get; set; }

        public string RuntimeReceiptSchemaVersion { get; set; }

        public string RuntimeEvidenceDigestSha256 { get; set; }

        public RuntimeVerificationStatus RuntimeVerificationStatus { get; set; } = RuntimeVerificationStatus.Pending;

        public string ExternalEvidenceDigestSha256 { get; set; }

        public string ProducerBuildProvenanceReceiptDigestSha256 { get; set; }
    }
}
