using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Execution.Journaling
{
    public static class MigrationExecutionJournalReader
    {
        private const int MaximumRecordBytes = 64 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly ISet<string> JournalRecordProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion",
            "recordKind",
            "journalSequence",
            "recordedAtUtc",
            "operationId",
            "planDigest",
            "actionId",
            "executionState",
            "mutationIntent",
            "mutationReceipt",
            "mutationVerification",
            "artifact",
            "payloadDigest",
            "recordDigest"
        };

        public static MigrationExecutionJournalReadResult Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A migration journal path is required.", nameof(path));
            }
            if (!File.Exists(path))
            {
                return Empty();
            }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return Read(stream);
            }
        }

        public static MigrationExecutionJournalReadResult Read(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (!stream.CanRead)
            {
                throw new ArgumentException("The migration journal stream must be readable.", nameof(stream));
            }

            var result = new MigrationExecutionJournalReadResult();
            var expectedSequence = 0L;
            var buffer = new byte[8192];
            var absoluteOffset = stream.CanSeek ? stream.Position : 0L;
            var lineOffset = absoluteOffset;
            using (var line = new MemoryStream())
            {
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (var index = 0; index < bytesRead; index++, absoluteOffset++)
                    {
                        var current = buffer[index];
                        if (current == (byte)'\n')
                        {
                            var recordBytes = line.ToArray();
                            if (recordBytes.Length > 0 && recordBytes[recordBytes.Length - 1] == (byte)'\r')
                            {
                                Array.Resize(ref recordBytes, recordBytes.Length - 1);
                            }
                            if (recordBytes.Length == 0)
                            {
                                throw Corruption(expectedSequence, "Blank records are not valid in a migration execution journal.");
                            }
                            var record = ParseRecord(recordBytes, expectedSequence);
                            result.Records.Add(record);
                            expectedSequence++;
                            line.SetLength(0);
                            lineOffset = absoluteOffset + 1;
                            continue;
                        }
                        if (line.Length >= MaximumRecordBytes)
                        {
                            throw Corruption(expectedSequence, "The journal record exceeds the 64 MiB safety limit.");
                        }
                        line.WriteByte(current);
                    }
                }
                if (line.Length > 0)
                {
                    var tail = line.ToArray();
                    result.InterruptedTail = new MigrationInterruptedJournalTail
                    {
                        Offset = lineOffset,
                        ByteCount = tail.Length,
                        Sha256 = MigrationDigest.ComputeSha256(tail),
                        Diagnostic = "The journal ends with an unterminated JSON Lines record. Completed records were validated, but the tail is explicit interrupted-write evidence and is not trusted."
                    };
                }
            }

            ValidateRelationships(result.Records);
            result.JournalDigest = MigrationDigest.ComputeSha256(string.Join("\n", result.Records.Select(value => value.RecordDigest)));
            return result;
        }

        private static MigrationExecutionJournalRecord ParseRecord(byte[] bytes, long expectedSequence)
        {
            string json;
            try
            {
                json = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw Corruption(expectedSequence, "The journal record is not valid UTF-8.", exception);
            }

            MigrationExecutionJournalRecord record;
            try
            {
                ValidateRootSchema(json, expectedSequence);
                record = MigrationContractSerializer.Deserialize<MigrationExecutionJournalRecord>(json);
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException || exception is NotSupportedException)
            {
                throw Corruption(expectedSequence, "The journal record is not valid schema JSON.", exception);
            }
            ValidateRecord(record, expectedSequence);
            return record;
        }

        private static void ValidateRootSchema(string json, long expectedSequence)
        {
            using (var document = System.Text.Json.JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    throw Corruption(expectedSequence, "The journal record root must be a JSON object.");
                }
                var observed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!JournalRecordProperties.Contains(property.Name) || !observed.Add(property.Name))
                    {
                        throw Corruption(expectedSequence, "The journal record contains an unknown or duplicate root property '" + property.Name + "'.");
                    }
                }
                if (!observed.SetEquals(JournalRecordProperties))
                {
                    throw Corruption(expectedSequence, "The journal record omits one or more required root properties.");
                }
            }
        }

        internal static void ValidateRecord(MigrationExecutionJournalRecord record, long expectedSequence)
        {
            if (record == null)
            {
                throw Corruption(expectedSequence, "The journal record deserialized as null.");
            }
            if (!string.Equals(record.SchemaVersion, MigrationExecutionJournalRecord.CurrentSchemaVersion, StringComparison.Ordinal))
            {
                throw Corruption(expectedSequence, "Unsupported migration execution journal record schema '" + record.SchemaVersion + "'.");
            }
            if (record.JournalSequence != expectedSequence)
            {
                throw Corruption(expectedSequence, "The journal sequence is duplicate, missing, or out of order; observed " + record.JournalSequence + ".");
            }
            if (record.OperationId == Guid.Empty || !IsSha256(record.PlanDigest) || record.RecordedAtUtc == default(DateTimeOffset))
            {
                throw Corruption(expectedSequence, "The journal record lacks its operation, plan, or timestamp boundary.");
            }

            var payloadCount = new object[]
            {
                record.ExecutionState,
                record.MutationIntent,
                record.MutationReceipt,
                record.MutationVerification,
                record.Artifact
            }.Count(value => value != null);
            if (payloadCount != 1 || !PayloadMatchesKind(record))
            {
                throw Corruption(expectedSequence, "The journal record must contain exactly one payload matching its record kind.");
            }
            ValidatePayloadBoundary(record, expectedSequence);
            var expectedPayloadDigest = MigrationExecutionJournalRecord.ComputePayloadDigest(record);
            if (!IsSha256(record.PayloadDigest)
                || !string.Equals(record.PayloadDigest, expectedPayloadDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(expectedSequence, "The journal payload digest is absent or invalid.");
            }
            var expectedRecordDigest = MigrationExecutionJournalRecord.ComputeRecordDigest(record);
            if (!IsSha256(record.RecordDigest)
                || !string.Equals(record.RecordDigest, expectedRecordDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(expectedSequence, "The journal record digest is absent or invalid.");
            }
        }

        private static void ValidatePayloadBoundary(MigrationExecutionJournalRecord record, long sequence)
        {
            Guid operationId;
            string planDigest;
            string actionId;
            switch (record.RecordKind)
            {
                case MigrationExecutionJournalRecordKind.ExecutionState:
                    operationId = record.ExecutionState.OperationId;
                    planDigest = record.ExecutionState.PlanDigest;
                    actionId = null;
                    if (record.ExecutionState.RecordedAtUtc != record.RecordedAtUtc)
                    {
                        throw Corruption(sequence, "The execution-state timestamp differs from its envelope.");
                    }
                    break;
                case MigrationExecutionJournalRecordKind.MutationIntent:
                    operationId = record.MutationIntent.OperationId;
                    planDigest = record.MutationIntent.PlanDigest;
                    actionId = record.MutationIntent.ActionId;
                    if (record.MutationIntent.WrittenAtUtc != record.RecordedAtUtc
                        || record.MutationIntent.Sequence < 0
                        || string.IsNullOrWhiteSpace(actionId))
                    {
                        throw Corruption(sequence, "The mutation intent timestamp, sequence, or action identity is invalid.");
                    }
                    ValidateMutationIdentity(record.MutationIntent, sequence);
                    break;
                case MigrationExecutionJournalRecordKind.MutationReceipt:
                    operationId = record.MutationReceipt.OperationId;
                    planDigest = record.MutationReceipt.PlanDigest;
                    actionId = record.MutationReceipt.ActionId;
                    if (record.MutationReceipt.CompletedAtUtc != record.RecordedAtUtc
                        || record.MutationReceipt.Sequence < 0
                        || string.IsNullOrWhiteSpace(actionId)
                        || !Enum.IsDefined(typeof(MutationOutcome), record.MutationReceipt.Outcome))
                    {
                        throw Corruption(sequence, "The mutation receipt timestamp, sequence, outcome, or action identity is invalid.");
                    }
                    ValidateMutationIdentity(record.MutationReceipt, sequence);
                    break;
                case MigrationExecutionJournalRecordKind.MutationVerification:
                    operationId = record.MutationVerification.OperationId;
                    planDigest = record.MutationVerification.PlanDigest;
                    actionId = record.MutationVerification.ActionId;
                    if (record.MutationVerification.VerifiedAtUtc != record.RecordedAtUtc
                        || string.IsNullOrWhiteSpace(actionId))
                    {
                        throw Corruption(sequence, "The mutation verification timestamp or action identity is invalid.");
                    }
                    ValidateMutationIdentity(record.MutationVerification, sequence);
                    break;
                case MigrationExecutionJournalRecordKind.Artifact:
                    operationId = record.Artifact.OperationId;
                    planDigest = record.Artifact.PlanDigest;
                    actionId = null;
                    if (record.Artifact.WrittenAtUtc != record.RecordedAtUtc
                        || string.IsNullOrWhiteSpace(record.Artifact.ArtifactKind)
                        || string.IsNullOrWhiteSpace(record.Artifact.ArtifactSchemaVersion)
                        || !IsSha256(record.Artifact.ArtifactDigest)
                        || string.IsNullOrWhiteSpace(record.Artifact.PayloadJson)
                        || !IsSha256(record.Artifact.PayloadSha256)
                        || !string.Equals(
                            record.Artifact.PayloadSha256,
                            MigrationDigest.ComputeSha256(record.Artifact.PayloadJson),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw Corruption(sequence, "The journal artifact payload is incomplete.");
                    }
                    try
                    {
                        using (System.Text.Json.JsonDocument.Parse(record.Artifact.PayloadJson))
                        {
                        }
                    }
                    catch (System.Text.Json.JsonException exception)
                    {
                        throw Corruption(sequence, "The journal artifact payload is not valid JSON.", exception);
                    }
                    break;
                default:
                    throw Corruption(sequence, "Unsupported journal record kind.");
            }
            if (operationId != record.OperationId
                || !string.Equals(planDigest, record.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(actionId, record.ActionId, StringComparison.Ordinal))
            {
                throw Corruption(sequence, "The journal envelope and payload operation, plan, or action identity differ.");
            }
        }

        private static void ValidateMutationIdentity(MigrationMutationIntent value, long sequence)
        {
            ValidateMutationIdentityCore(
                value.SourceSnapshotDigest,
                value.PlanDigest,
                value.ApprovalDigest,
                value.TargetBoundaryDigest,
                value.IngredientId,
                value.ActionId,
                value.SelectedDisposition,
                value.SemanticDigest,
                value.IdempotencyKey,
                sequence);
        }

        private static void ValidateMutationIdentity(MigrationMutationReceipt value, long sequence)
        {
            ValidateMutationIdentityCore(
                value.SourceSnapshotDigest,
                value.PlanDigest,
                value.ApprovalDigest,
                value.TargetBoundaryDigest,
                value.IngredientId,
                value.ActionId,
                value.SelectedDisposition,
                value.SemanticDigest,
                value.IdempotencyKey,
                sequence);
        }

        private static void ValidateMutationIdentity(MigrationMutationVerificationReceipt value, long sequence)
        {
            if (!string.Equals(value.SchemaVersion, "pnp-migration-mutation-verification/v1", StringComparison.Ordinal)
                || value.VerifiedAtUtc == default(DateTimeOffset)
                || !IsSha256(value.CurrentStateDigest))
            {
                throw Corruption(sequence, "The mutation verification payload is incomplete or uses an unsupported schema.");
            }
            ValidateMutationIdentityCore(
                value.SourceSnapshotDigest,
                value.PlanDigest,
                value.ApprovalDigest,
                value.TargetBoundaryDigest,
                value.IngredientId,
                value.ActionId,
                value.SelectedDisposition,
                value.SemanticDigest,
                value.IdempotencyKey,
                sequence);
        }

        private static void ValidateMutationIdentityCore(
            string sourceSnapshotDigest,
            string planDigest,
            string approvalDigest,
            string targetBoundaryDigest,
            string ingredientId,
            string actionId,
            string selectedDisposition,
            string semanticDigest,
            string idempotencyKey,
            long sequence)
        {
            var hasAnyStableIdentity = !string.IsNullOrWhiteSpace(sourceSnapshotDigest)
                || !string.IsNullOrWhiteSpace(approvalDigest)
                || !string.IsNullOrWhiteSpace(targetBoundaryDigest)
                || !string.IsNullOrWhiteSpace(ingredientId)
                || !string.IsNullOrWhiteSpace(selectedDisposition)
                || !string.IsNullOrWhiteSpace(semanticDigest)
                || !string.IsNullOrWhiteSpace(idempotencyKey);
            if (!hasAnyStableIdentity)
            {
                return;
            }
            if (!IsOptionalSha256(sourceSnapshotDigest)
                || !IsSha256(planDigest)
                || !IsOptionalSha256(approvalDigest)
                || !IsSha256(targetBoundaryDigest)
                || string.IsNullOrWhiteSpace(ingredientId)
                || string.IsNullOrWhiteSpace(actionId)
                || string.IsNullOrWhiteSpace(selectedDisposition)
                || !IsSha256(semanticDigest)
                || !IsSha256(idempotencyKey))
            {
                throw Corruption(sequence, "The durable mutation identity is partial or invalid.");
            }
            var boundary = new MigrationExecutionBoundary
            {
                SourceSnapshotDigest = sourceSnapshotDigest,
                PlanDigest = planDigest,
                ApprovalDigest = approvalDigest,
                TargetBoundaryDigest = targetBoundaryDigest
            };
            var identity = new MigrationMutationIdentity
            {
                IngredientId = ingredientId,
                ActionId = actionId,
                SelectedDisposition = selectedDisposition,
                SemanticDigest = semanticDigest
            };
            var expected = MigrationMutationIdentity.ComputeIdempotencyKey(boundary, identity);
            if (!string.Equals(idempotencyKey, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(sequence, "The durable mutation idempotency key is invalid.");
            }
        }

        internal static void ValidateRelationships(IList<MigrationExecutionJournalRecord> records)
        {
            var state = new MigrationExecutionJournalRelationshipState();
            foreach (var record in records)
            {
                state.Add(record);
            }
        }

        private static bool PayloadMatchesKind(MigrationExecutionJournalRecord value)
        {
            return value.RecordKind == MigrationExecutionJournalRecordKind.ExecutionState && value.ExecutionState != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent && value.MutationIntent != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt && value.MutationReceipt != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.MutationVerification && value.MutationVerification != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.Artifact && value.Artifact != null;
        }

        private static MigrationExecutionJournalReadResult Empty()
        {
            return new MigrationExecutionJournalReadResult
            {
                JournalDigest = MigrationDigest.ComputeSha256(string.Empty)
            };
        }

        private static bool IsOptionalSha256(string value)
        {
            return string.IsNullOrWhiteSpace(value) || IsSha256(value);
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 64
                && value.All(character => character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F');
        }

        private static InvalidDataException Corruption(long sequence, string message, Exception inner = null)
        {
            var diagnostic = "Migration execution journal corruption at record " + sequence + ": " + message;
            return inner == null
                ? new InvalidDataException(diagnostic)
                : new InvalidDataException(diagnostic, inner);
        }
    }
}
