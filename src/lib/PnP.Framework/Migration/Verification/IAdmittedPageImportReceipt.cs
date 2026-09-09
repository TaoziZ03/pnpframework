using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Verification
{
    /// <summary>
    /// Common execution lineage carried by a native page-family import receipt.
    /// Family-specific receipt payloads remain authoritative for persisted results.
    /// </summary>
    public interface IAdmittedPageImportReceipt
    {
        string SchemaVersion { get; }

        DateTimeOffset StartedAtUtc { get; }

        DateTimeOffset CompletedAtUtc { get; }

        Guid OperationId { get; }

        string AdmittedPlanDigestSha256 { get; }

        CurrentSourceVersionIdentity SourceVersion { get; }

        ReproOperationIds Operations { get; }

        MigrationExecutionStatus ExecutionStatus { get; }

        bool PartialExecution { get; }

        bool MutationStarted { get; }

        IList<MigrationMutationReceipt> Steps { get; }

        string ApprovedPlanDigest { get; }

        string TargetWebUrl { get; }

        string TargetPageServerRelativeUrl { get; }

        Guid TargetFileUniqueId { get; }

        int TargetListItemId { get; }

        string TargetVersionLabel { get; }

        bool FreshReadbackPassed { get; }

        StorageVerificationStatus StorageVerificationStatus { get; }

        RuntimeVerificationStatus RuntimeVerificationStatus { get; }
    }
}
