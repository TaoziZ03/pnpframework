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
        Artifact = 5
    }

    /// <summary>
    /// One digest-sealed JSON Lines record. Exactly one typed payload property is
    /// populated according to RecordKind.
    /// </summary>
    public sealed class MigrationExecutionJournalRecord
    {
        public const string CurrentSchemaVersion = "pnp-migration-execution-journal-record/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public MigrationExecutionJournalRecordKind RecordKind { get; set; }

        public long JournalSequence { get; set; }

        public DateTimeOffset RecordedAtUtc { get; set; }

        public Guid OperationId { get; set; }

        public string PlanDigest { get; set; }

        public string ActionId { get; set; }

        public MigrationExecutionStateReceipt ExecutionState { get; set; }

        public MigrationMutationIntent MutationIntent { get; set; }

        public MigrationMutationReceipt MutationReceipt { get; set; }

        public MigrationMutationVerificationReceipt MutationVerification { get; set; }

        public MigrationExecutionArtifact Artifact { get; set; }

        public string PayloadDigest { get; set; }

        public string RecordDigest { get; set; }

        internal static string ComputePayloadDigest(MigrationExecutionJournalRecord value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            switch (value.RecordKind)
            {
                case MigrationExecutionJournalRecordKind.ExecutionState:
                    return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value.ExecutionState));
                case MigrationExecutionJournalRecordKind.MutationIntent:
                    return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value.MutationIntent));
                case MigrationExecutionJournalRecordKind.MutationReceipt:
                    return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value.MutationReceipt));
                case MigrationExecutionJournalRecordKind.MutationVerification:
                    return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value.MutationVerification));
                case MigrationExecutionJournalRecordKind.Artifact:
                    return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value.Artifact));
                default:
                    throw new InvalidOperationException("Unsupported migration journal record kind: " + value.RecordKind + ".");
            }
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
    }
}
