using System;

namespace PnP.Framework.Migration.Execution
{
    public enum MigrationTargetOwnership
    {
        MigrationOwned = 1,
        External = 2
    }

    public sealed class MigrationMutationVerificationReceipt
    {
        public const string CurrentSchemaVersion = "pnp-migration-mutation-verification/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Guid OperationId { get; set; }

        public string PlanDigest { get; set; }

        public string ActionId { get; set; }

        public string ActionSignature { get; set; }

        public DateTimeOffset VerifiedAtUtc { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public string ObservedStateDigest { get; set; }

        public MigrationTargetOwnership Ownership { get; set; }

        public string TargetIdentityDigest { get; set; }

        public bool ProvenanceMatched { get; set; }

        public string Message { get; set; }
    }
}
