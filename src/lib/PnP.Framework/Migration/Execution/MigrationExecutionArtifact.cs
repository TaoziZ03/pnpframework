using System;

namespace PnP.Framework.Migration.Execution
{
    public sealed class MigrationExecutionArtifact
    {
        public string SchemaVersion { get; set; } = "pnp-migration-execution-artifact/v1";

        public Guid OperationId { get; set; }

        public string PlanDigest { get; set; }

        public DateTimeOffset WrittenAtUtc { get; set; }

        public string ArtifactKind { get; set; }

        public string ArtifactSchemaVersion { get; set; }

        public string ArtifactDigest { get; set; }

        public string PayloadJson { get; set; }

        public string PayloadSha256 { get; set; }
    }
}
