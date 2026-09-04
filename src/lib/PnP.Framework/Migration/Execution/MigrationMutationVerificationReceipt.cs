using System;

namespace PnP.Framework.Migration.Execution
{
    public sealed class MigrationMutationVerificationReceipt
    {
        public string SchemaVersion { get; set; } = "pnp-migration-mutation-verification/v1";

        public Guid OperationId { get; set; }

        public string PlanDigest { get; set; }

        public string ActionId { get; set; }

        public DateTimeOffset VerifiedAtUtc { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public string CurrentStateDigest { get; set; }

        public string Ownership { get; set; }

        public string TargetIdentity { get; set; }

        public string SourceSnapshotDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public string IngredientId { get; set; }

        public string SelectedDisposition { get; set; }

        public string TargetBoundaryDigest { get; set; }

        public string SemanticDigest { get; set; }

        public string IdempotencyKey { get; set; }

        public string Message { get; set; }
    }
}
