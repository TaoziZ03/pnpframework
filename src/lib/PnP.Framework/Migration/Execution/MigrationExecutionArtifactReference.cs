using System;

namespace PnP.Framework.Migration.Execution
{
    public enum MigrationExecutionArtifactKind
    {
        MaterializationReceipt = 1,
        MappingCatalog = 2,
        VerificationEvidence = 3,
        ComparisonEvidence = 4
    }

    /// <summary>
    /// Content-addressed reference only. Durable journals never embed arbitrary
    /// artifact JSON or binary payloads.
    /// </summary>
    public sealed class MigrationExecutionArtifactReference
    {
        public const string CurrentSchemaVersion = "pnp-migration-execution-artifact-reference/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Guid OperationId { get; set; }

        public string PlanDigest { get; set; }

        public string ActionId { get; set; }

        public string ActionSignature { get; set; }

        public DateTimeOffset WrittenAtUtc { get; set; }

        public MigrationExecutionArtifactKind ArtifactKind { get; set; }

        public string ArtifactSchemaVersion { get; set; }

        public string Sha256 { get; set; }

        public long Length { get; set; }

        public string MediaType { get; set; }
    }
}
