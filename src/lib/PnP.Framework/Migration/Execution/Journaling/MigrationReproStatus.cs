using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Execution.Journaling
{
    public sealed class MigrationReproActionStatus
    {
        public string IdempotencyKey { get; set; }

        public string SourceSnapshotDigest { get; set; }

        public string PlanDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public string TargetBoundaryDigest { get; set; }

        public string IngredientId { get; set; }

        public string ActionId { get; set; }

        public string SelectedDisposition { get; set; }

        public string SemanticDigest { get; set; }

        public string State { get; set; }

        public string Ownership { get; set; }

        public string TargetIdentity { get; set; }

        public string CurrentStateDigest { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public Guid LastOperationId { get; set; }

        public DateTimeOffset LastRecordedAtUtc { get; set; }
    }

    public sealed class MigrationReproStatus
    {
        public string SchemaVersion { get; set; } = "pnp-migration-repro-status/v1";

        public string JournalDigest { get; set; }

        public DateTimeOffset ProjectedThroughUtc { get; set; }

        public MigrationInterruptedJournalTail InterruptedTail { get; set; }

        public IList<MigrationReproActionStatus> Ingredients { get; set; } = new List<MigrationReproActionStatus>();
    }
}
