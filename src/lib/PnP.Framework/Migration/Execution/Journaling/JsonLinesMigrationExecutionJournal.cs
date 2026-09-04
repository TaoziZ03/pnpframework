using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Execution.Journaling
{
    /// <summary>
    /// Single-machine, single-writer, append-only JSON Lines journal. A partial
    /// final record is retained as interrupted-write evidence and the next writer
    /// continues in a chained segment without truncating the damaged bytes.
    /// </summary>
    public sealed class JsonLinesMigrationExecutionJournal : IMigrationExecutionCheckpointJournal, IDisposable
    {
        private static readonly ConcurrentDictionary<string, object> PathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private readonly object gate;
        private readonly FileStream stream;
        private readonly MigrationExecutionJournalRelationshipState relationships =
            new MigrationExecutionJournalRelationshipState();
        private long nextSequence;
        private string previousRecordDigest;
        private bool disposed;

        public JsonLinesMigrationExecutionJournal(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A migration execution journal path is required.", nameof(path));
            }
            Path = System.IO.Path.GetFullPath(path);
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            gate = PathLocks.GetOrAdd(Path, _ => new object());
            lock (gate)
            {
                var existing = MigrationExecutionJournalReader.Read(Path);
                nextSequence = existing.Records.Count;
                previousRecordDigest = existing.Records.LastOrDefault()?.RecordDigest;
                foreach (var record in existing.Records)
                {
                    relationships.Add(record);
                }

                var segments = MigrationExecutionJournalReader.GetSegments(Path);
                var latestIndex = segments.Count == 0 ? 0 : segments.Max(value => value.Index);
                var latestInterrupted = existing.InterruptedTails.Any(value => value.SegmentIndex == latestIndex);
                ActiveSegmentIndex = latestInterrupted ? latestIndex + 1 : latestIndex;
                ActiveSegmentPath = MigrationExecutionJournalReader.SegmentPath(Path, ActiveSegmentIndex);
                stream = new FileStream(
                    ActiveSegmentPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    4096,
                    FileOptions.WriteThrough);
                stream.Seek(0, SeekOrigin.End);
            }
        }

        public string Path { get; }

        public int ActiveSegmentIndex { get; }

        public string ActiveSegmentPath { get; }

        public void WriteExecutionState(MigrationExecutionStateReceipt state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            Append(new MigrationExecutionJournalRecord
            {
                RecordKind = MigrationExecutionJournalRecordKind.ExecutionState,
                RecordedAtUtc = state.RecordedAtUtc,
                OperationId = state.OperationId,
                PlanDigest = state.PlanDigest,
                ExecutionState = state
            });
        }

        public void WriteIntent(MigrationMutationIntent intent)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }
            Append(new MigrationExecutionJournalRecord
            {
                RecordKind = MigrationExecutionJournalRecordKind.MutationIntent,
                RecordedAtUtc = intent.WrittenAtUtc,
                OperationId = intent.OperationId,
                PlanDigest = intent.PlanDigest,
                ActionId = intent.ActionId,
                ActionSignature = intent.ActionSignature,
                MutationIntent = intent
            });
        }

        public void WriteReceipt(MigrationMutationReceipt receipt)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            Append(new MigrationExecutionJournalRecord
            {
                RecordKind = MigrationExecutionJournalRecordKind.MutationReceipt,
                RecordedAtUtc = receipt.CompletedAtUtc,
                OperationId = receipt.OperationId,
                PlanDigest = receipt.PlanDigest,
                ActionId = receipt.ActionId,
                ActionSignature = receipt.ActionSignature,
                MutationReceipt = receipt
            });
        }

        public void WriteVerification(MigrationMutationVerificationReceipt verification)
        {
            if (verification == null)
            {
                throw new ArgumentNullException(nameof(verification));
            }
            Append(new MigrationExecutionJournalRecord
            {
                RecordKind = MigrationExecutionJournalRecordKind.MutationVerification,
                RecordedAtUtc = verification.VerifiedAtUtc,
                OperationId = verification.OperationId,
                PlanDigest = verification.PlanDigest,
                ActionId = verification.ActionId,
                ActionSignature = verification.ActionSignature,
                MutationVerification = verification
            });
        }

        public void WriteArtifactReference(MigrationExecutionArtifactReference artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            Append(new MigrationExecutionJournalRecord
            {
                RecordKind = MigrationExecutionJournalRecordKind.ArtifactReference,
                RecordedAtUtc = artifact.WrittenAtUtc,
                OperationId = artifact.OperationId,
                PlanDigest = artifact.PlanDigest,
                ActionId = artifact.ActionId,
                ActionSignature = artifact.ActionSignature,
                ArtifactReference = artifact
            });
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
                stream.Flush(true);
                stream.Dispose();
                disposed = true;
            }
        }

        private void Append(MigrationExecutionJournalRecord record)
        {
            lock (gate)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(JsonLinesMigrationExecutionJournal));
                }
                record.JournalSequence = nextSequence;
                record.PreviousRecordDigest = previousRecordDigest;
                record.PayloadDigest = MigrationExecutionJournalRecord.ComputePayloadDigest(record);
                record.RecordDigest = MigrationExecutionJournalRecord.ComputeRecordDigest(record);
                MigrationExecutionJournalReader.ValidateRecord(record, nextSequence, previousRecordDigest);
                relationships.Add(record);
                var bytes = Utf8.GetBytes(MigrationContractSerializer.SerializeCanonical(record) + "\n");
                try
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                    nextSequence++;
                    previousRecordDigest = record.RecordDigest;
                }
                catch
                {
                    disposed = true;
                    stream.Dispose();
                    throw;
                }
            }
        }
    }
}
