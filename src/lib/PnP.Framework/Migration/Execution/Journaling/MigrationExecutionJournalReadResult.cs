using System.Collections.Generic;

namespace PnP.Framework.Migration.Execution.Journaling
{
    public sealed class MigrationInterruptedJournalTail
    {
        public long Offset { get; set; }

        public int ByteCount { get; set; }

        public string Sha256 { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class MigrationExecutionJournalReadResult
    {
        public string SchemaVersion { get; set; } = "pnp-migration-execution-journal-read-result/v1";

        public IList<MigrationExecutionJournalRecord> Records { get; set; } = new List<MigrationExecutionJournalRecord>();

        public string JournalDigest { get; set; }

        public MigrationInterruptedJournalTail InterruptedTail { get; set; }

        public bool HasInterruptedTail => InterruptedTail != null;
    }
}
