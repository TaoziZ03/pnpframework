using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Execution.Journaling
{
    public static class MigrationReproStatusProjector
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static MigrationReproStatus Project(MigrationExecutionJournalReadResult journal)
        {
            if (journal == null)
            {
                throw new ArgumentNullException(nameof(journal));
            }

            var projected = new Dictionary<string, MigrationReproActionStatus>(StringComparer.Ordinal);
            foreach (var record in journal.Records)
            {
                if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent)
                {
                    var value = record.MutationIntent;
                    var status = Get(projected, value.IdempotencyKey, record.OperationId, value.ActionId);
                    Populate(status, value, record.RecordedAtUtc);
                    status.State = "OutcomeUnknown";
                }
                else if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt)
                {
                    var value = record.MutationReceipt;
                    var status = Get(projected, value.IdempotencyKey, record.OperationId, value.ActionId);
                    Populate(status, value, record.RecordedAtUtc);
                    status.State = value.Outcome == MutationOutcome.Applied
                        ? "AppliedAwaitingVerification"
                        : value.Outcome == MutationOutcome.AlreadySatisfied
                            ? "AlreadySatisfiedAwaitingVerification"
                            : "Failed";
                }
                else if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationVerification)
                {
                    var value = record.MutationVerification;
                    var status = Get(projected, value.IdempotencyKey, record.OperationId, value.ActionId);
                    Populate(status, value, record.RecordedAtUtc);
                    status.FreshReadbackPassed = value.FreshReadbackPassed;
                    status.CurrentStateDigest = value.CurrentStateDigest;
                    status.Ownership = value.Ownership;
                    status.TargetIdentity = value.TargetIdentity;
                    status.State = value.FreshReadbackPassed ? "Verified" : "VerificationFailed";
                }
            }

            return new MigrationReproStatus
            {
                JournalDigest = journal.JournalDigest,
                ProjectedThroughUtc = journal.Records.Count == 0
                    ? default(DateTimeOffset)
                    : journal.Records.Max(value => value.RecordedAtUtc),
                InterruptedTail = journal.InterruptedTail,
                Ingredients = projected.Values
                    .OrderBy(value => value.IngredientId, StringComparer.Ordinal)
                    .ThenBy(value => value.ActionId, StringComparer.Ordinal)
                    .ThenBy(value => value.IdempotencyKey, StringComparer.Ordinal)
                    .ToList()
            };
        }

        public static void WriteAtomic(string path, MigrationExecutionJournalReadResult journal)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A repro status path is required.", nameof(path));
            }
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    Write(stream, journal);
                    stream.Flush(true);
                }
                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, null);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public static void Write(Stream output, MigrationExecutionJournalReadResult journal)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            if (!output.CanWrite)
            {
                throw new ArgumentException("The repro status stream must be writable.", nameof(output));
            }
            var bytes = Utf8.GetBytes(MigrationContractSerializer.SerializeIndented(Project(journal)) + "\n");
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }

        private static MigrationReproActionStatus Get(
            IDictionary<string, MigrationReproActionStatus> projected,
            string idempotencyKey,
            Guid operationId,
            string actionId)
        {
            var key = string.IsNullOrWhiteSpace(idempotencyKey)
                ? "legacy/" + operationId.ToString("N") + "/" + actionId
                : idempotencyKey;
            if (!projected.TryGetValue(key, out var status))
            {
                status = new MigrationReproActionStatus
                {
                    IdempotencyKey = idempotencyKey
                };
                projected[key] = status;
            }
            return status;
        }

        private static void Populate(
            MigrationReproActionStatus target,
            MigrationMutationIntent source,
            DateTimeOffset timestamp)
        {
            target.IdempotencyKey = source.IdempotencyKey;
            target.SourceSnapshotDigest = source.SourceSnapshotDigest;
            target.PlanDigest = source.PlanDigest;
            target.ApprovalDigest = source.ApprovalDigest;
            target.TargetBoundaryDigest = source.TargetBoundaryDigest;
            target.IngredientId = source.IngredientId;
            target.ActionId = source.ActionId;
            target.SelectedDisposition = source.SelectedDisposition;
            target.SemanticDigest = source.SemanticDigest;
            target.LastOperationId = source.OperationId;
            target.LastRecordedAtUtc = timestamp;
        }

        private static void Populate(
            MigrationReproActionStatus target,
            MigrationMutationReceipt source,
            DateTimeOffset timestamp)
        {
            target.IdempotencyKey = source.IdempotencyKey;
            target.SourceSnapshotDigest = source.SourceSnapshotDigest;
            target.PlanDigest = source.PlanDigest;
            target.ApprovalDigest = source.ApprovalDigest;
            target.TargetBoundaryDigest = source.TargetBoundaryDigest;
            target.IngredientId = source.IngredientId;
            target.ActionId = source.ActionId;
            target.SelectedDisposition = source.SelectedDisposition;
            target.SemanticDigest = source.SemanticDigest;
            target.LastOperationId = source.OperationId;
            target.LastRecordedAtUtc = timestamp;
        }

        private static void Populate(
            MigrationReproActionStatus target,
            MigrationMutationVerificationReceipt source,
            DateTimeOffset timestamp)
        {
            target.IdempotencyKey = source.IdempotencyKey;
            target.SourceSnapshotDigest = source.SourceSnapshotDigest;
            target.PlanDigest = source.PlanDigest;
            target.ApprovalDigest = source.ApprovalDigest;
            target.TargetBoundaryDigest = source.TargetBoundaryDigest;
            target.IngredientId = source.IngredientId;
            target.ActionId = source.ActionId;
            target.SelectedDisposition = source.SelectedDisposition;
            target.SemanticDigest = source.SemanticDigest;
            target.LastOperationId = source.OperationId;
            target.LastRecordedAtUtc = timestamp;
        }
    }
}
