using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace PnP.Framework.Migration.Execution.Journaling
{
    /// <summary>
    /// Append-only, flush-on-record JSON Lines execution journal. The writer
    /// holds an exclusive write handle while allowing concurrent readers.
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
                stream = new FileStream(
                    Path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    4096,
                    FileOptions.WriteThrough);
                try
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    var existing = MigrationExecutionJournalReader.Read(stream);
                    if (existing.HasInterruptedTail)
                    {
                        throw new InvalidDataException(
                            "Cannot append to a migration execution journal with an interrupted tail. "
                            + existing.InterruptedTail.Diagnostic);
                    }
                    nextSequence = existing.Records.Count;
                    foreach (var record in existing.Records)
                    {
                        relationships.Add(record);
                    }
                    stream.Seek(0, SeekOrigin.End);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
        }

        public string Path { get; }

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
                MutationVerification = verification
            });
        }

        public void WriteArtifact(MigrationExecutionArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            Append(new MigrationExecutionJournalRecord
            {
                RecordKind = MigrationExecutionJournalRecordKind.Artifact,
                RecordedAtUtc = artifact.WrittenAtUtc,
                OperationId = artifact.OperationId,
                PlanDigest = artifact.PlanDigest,
                Artifact = artifact
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
                record.PayloadDigest = MigrationExecutionJournalRecord.ComputePayloadDigest(record);
                record.RecordDigest = MigrationExecutionJournalRecord.ComputeRecordDigest(record);
                MigrationExecutionJournalReader.ValidateRecord(record, nextSequence);
                relationships.Add(record);
                var bytes = Utf8.GetBytes(MigrationContractSerializer.SerializeCanonical(record) + "\n");
                try
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                    nextSequence++;
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
