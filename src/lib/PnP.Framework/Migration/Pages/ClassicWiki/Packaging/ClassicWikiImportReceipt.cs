using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Packaging
{
    public sealed class ClassicWikiImportReceipt
    {
        public string SchemaVersion { get; set; } = ClassicWikiPackageContract.ReceiptSchemaVersion;

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public Guid OperationId { get; set; }

        public MigrationExecutionStatus ExecutionStatus { get; set; }

        public ExecutionAdmissionFailure AdmissionFailure { get; set; }

        public bool MutationStarted { get; set; }

        public IList<MigrationMutationReceipt> Steps { get; set; } = new List<MigrationMutationReceipt>();

        public string ApprovedPlanDigest { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetPageServerRelativeUrl { get; set; }

        public Guid TargetFileUniqueId { get; set; }

        public int TargetListItemId { get; set; }

        public string TargetContentTypeId { get; set; }

        public string TargetVersionLabel { get; set; }

        public string StoredWikiFieldSha256 { get; set; }

        public bool StorageContentEqual { get; set; }

        public bool ResumedExistingOwnedPage { get; set; }

        public int ImportedWebPartCount { get; set; }

        public bool WebPartsMatched { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public StorageVerificationStatus StorageVerificationStatus { get; set; }

        public RuntimeVerificationStatus RuntimeVerificationStatus { get; set; }

        public MigrationAcceptanceStatus AcceptanceStatus { get; set; }

        public IList<string> Warnings { get; set; } = new List<string>();

        public IList<string> Diagnostics { get; set; } = new List<string>();
    }
}
