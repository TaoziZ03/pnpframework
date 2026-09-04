using PnP.Framework.Migration.Packaging;
using System;

namespace PnP.Framework.Migration.Execution.Journaling
{
    public enum MigrationExecutionJournalRecordKind
    {
        ExecutionState = 1,
        MutationIntent = 2,
        MutationReceipt = 3,
        MutationVerification = 4,
        ArtifactReference = 5
    }

    /// <summary>
    /// One digest-sealed JSON Lines record. Exactly one typed payload is present.
    /// PreviousRecordDigest chains records across recovered journal segments.
    /// </summary>
    public sealed class MigrationExecutionJournalRecord
    {
        public const string CurrentSchemaVersion = "pnp-migration-execution-journal-record/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public MigrationExecutionJournalRecordKind RecordKind { get; set; }

        public long JournalSequence { get; set; }

        public string PreviousRecordDigest { get; set; }

        public DateTimeOffset RecordedAtUtc { get; set; }

        public Guid OperationId { get; set; }

        public string PlanDigest { get; set; }

        public string ActionId { get; set; }

        public string ActionSignature { get; set; }

        public MigrationExecutionStateReceipt ExecutionState { get; set; }

        public MigrationMutationIntent MutationIntent { get; set; }

        public MigrationMutationReceipt MutationReceipt { get; set; }

        public MigrationMutationVerificationReceipt MutationVerification { get; set; }

        public MigrationExecutionArtifactReference ArtifactReference { get; set; }

        public string PayloadDigest { get; set; }

        public string RecordDigest { get; set; }

        internal static string ComputePayloadDigest(MigrationExecutionJournalRecord value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return value.RecordKind switch
            {
                MigrationExecutionJournalRecordKind.ExecutionState => Digest(value.ExecutionState),
                MigrationExecutionJournalRecordKind.MutationIntent => Digest(value.MutationIntent),
                MigrationExecutionJournalRecordKind.MutationReceipt => Digest(value.MutationReceipt),
                MigrationExecutionJournalRecordKind.MutationVerification => Digest(value.MutationVerification),
                MigrationExecutionJournalRecordKind.ArtifactReference => Digest(value.ArtifactReference),
                _ => throw new InvalidOperationException("Unsupported migration journal record kind: " + value.RecordKind + ".")
            };
        }

        internal static string ComputeRecordDigest(MigrationExecutionJournalRecord value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    value,
                    nameof(RecordDigest)));
        }

        private static string Digest<T>(T value)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value));
        }
    }
}
