using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Execution.Journaling
{
    public static class MigrationExecutionJournalReader
    {
        private const int MaximumRecordBytes = 4 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly ISet<string> JournalRecordProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion",
            "recordKind",
            "journalSequence",
            "previousRecordDigest",
            "recordedAtUtc",
            "operationId",
            "planDigest",
            "actionId",
            "actionSignature",
            "executionState",
            "mutationIntent",
            "mutationReceipt",
            "mutationVerification",
            "artifactReference",
            "payloadDigest",
            "recordDigest"
        };

        public static MigrationExecutionJournalReadResult Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A migration journal path is required.", nameof(path));
            }
            var fullPath = Path.GetFullPath(path);
            var segments = GetSegments(fullPath);
            if (segments.Count == 0)
            {
                return Empty();
            }
            if (segments[0].Index != 0)
            {
                throw new InvalidDataException("A segmented migration journal is missing its base segment.");
            }
            for (var index = 0; index < segments.Count; index++)
            {
                if (segments[index].Index != index)
                {
                    throw new InvalidDataException("Migration journal segment indexes are missing or out of order.");
                }
            }

            var result = new MigrationExecutionJournalReadResult();
            var expectedSequence = 0L;
            string previousRecordDigest = null;
            foreach (var segment in segments)
            {
                using (var stream = new FileStream(
                    segment.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    ReadSegment(
                        stream,
                        segment.Index,
                        segment.Path,
                        result,
                        ref expectedSequence,
                        ref previousRecordDigest);
                }
            }
            FinalizeResult(result);
            return result;
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
            string previousRecordDigest = null;
            ReadSegment(stream, 0, null, result, ref expectedSequence, ref previousRecordDigest);
            FinalizeResult(result);
            return result;
        }

        internal static IReadOnlyList<JournalSegment> GetSegments(string basePath)
        {
            var fullPath = Path.GetFullPath(basePath);
            var result = new List<JournalSegment>();
            if (File.Exists(fullPath))
            {
                result.Add(new JournalSegment(0, fullPath));
            }
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return result;
            }
            var stem = Path.GetFileNameWithoutExtension(fullPath);
            var extension = Path.GetExtension(fullPath);
            var segmentPrefix = (string.IsNullOrEmpty(extension)
                ? Path.GetFileName(fullPath)
                : stem) + ".segment-";
            foreach (var candidate in Directory.GetFiles(directory, segmentPrefix + "*" + extension))
            {
                var fileName = Path.GetFileName(candidate);
                var numericLength = fileName.Length - segmentPrefix.Length - extension.Length;
                if (!fileName.StartsWith(segmentPrefix, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(extension)
                        && !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    || numericLength <= 0
                    || !int.TryParse(
                        fileName.Substring(segmentPrefix.Length, numericLength),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var index)
                    || index <= 0)
                {
                    continue;
                }
                result.Add(new JournalSegment(index, Path.GetFullPath(candidate)));
            }
            return result.OrderBy(value => value.Index).ToArray();
        }

        internal static string SegmentPath(string basePath, int segmentIndex)
        {
            if (segmentIndex == 0)
            {
                return Path.GetFullPath(basePath);
            }
            var fullPath = Path.GetFullPath(basePath);
            var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(fullPath);
            var extension = Path.GetExtension(fullPath);
            return Path.Combine(
                directory,
                stem + ".segment-" + segmentIndex.ToString("D6", CultureInfo.InvariantCulture) + extension);
        }

        private static void ReadSegment(
            Stream stream,
            int segmentIndex,
            string segmentPath,
            MigrationExecutionJournalReadResult result,
            ref long expectedSequence,
            ref string previousRecordDigest)
        {
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
                            var record = ParseRecord(recordBytes, expectedSequence, previousRecordDigest);
                            result.Records.Add(record);
                            previousRecordDigest = record.RecordDigest;
                            expectedSequence++;
                            line.SetLength(0);
                            lineOffset = absoluteOffset + 1;
                            continue;
                        }
                        if (line.Length >= MaximumRecordBytes)
                        {
                            throw Corruption(expectedSequence, "The journal record exceeds the 4 MiB safety limit.");
                        }
                        line.WriteByte(current);
                    }
                }
                if (line.Length > 0)
                {
                    var tail = line.ToArray();
                    result.InterruptedTails.Add(new MigrationInterruptedJournalTail
                    {
                        SegmentIndex = segmentIndex,
                        SegmentPath = segmentPath,
                        Offset = lineOffset,
                        ByteCount = tail.Length,
                        Sha256 = MigrationDigest.ComputeSha256(tail),
                        Diagnostic = "The journal segment ends with an unterminated JSON Lines record. Completed records remain trusted; a later segment may continue from their last sealed digest."
                    });
                }
            }
        }

        private static MigrationExecutionJournalRecord ParseRecord(
            byte[] bytes,
            long expectedSequence,
            string previousRecordDigest)
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
                if (!string.Equals(
                    json,
                    MigrationContractSerializer.SerializeCanonical(record),
                    StringComparison.Ordinal))
                {
                    throw Corruption(
                        expectedSequence,
                        "The journal record is not canonical typed JSON or contains an unknown nested property.");
                }
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException || exception is NotSupportedException)
            {
                throw Corruption(expectedSequence, "The journal record is not valid schema JSON.", exception);
            }
            ValidateRecord(record, expectedSequence, previousRecordDigest);
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

        internal static void ValidateRecord(
            MigrationExecutionJournalRecord record,
            long expectedSequence,
            string previousRecordDigest)
        {
            if (record == null
                || !string.Equals(record.SchemaVersion, MigrationExecutionJournalRecord.CurrentSchemaVersion, StringComparison.Ordinal)
                || record.JournalSequence != expectedSequence
                || record.OperationId == Guid.Empty
                || !MigrationActionSignature.IsSha256(record.PlanDigest)
                || record.RecordedAtUtc == default(DateTimeOffset)
                || (expectedSequence == 0 && !string.IsNullOrWhiteSpace(record.PreviousRecordDigest))
                || (expectedSequence > 0
                    && !string.Equals(record.PreviousRecordDigest, previousRecordDigest, StringComparison.OrdinalIgnoreCase)))
            {
                throw Corruption(expectedSequence, "The journal record has an invalid schema, sequence, chain, operation, plan, or timestamp boundary.");
            }

            var payloadCount = new object[]
            {
                record.ExecutionState,
                record.MutationIntent,
                record.MutationReceipt,
                record.MutationVerification,
                record.ArtifactReference
            }.Count(value => value != null);
            if (payloadCount != 1 || !PayloadMatchesKind(record))
            {
                throw Corruption(expectedSequence, "The journal record must contain exactly one payload matching its record kind.");
            }
            ValidatePayloadBoundary(record, expectedSequence);
            if (!string.Equals(record.PayloadDigest, MigrationExecutionJournalRecord.ComputePayloadDigest(record), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(record.RecordDigest, MigrationExecutionJournalRecord.ComputeRecordDigest(record), StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(expectedSequence, "The journal payload or record digest is absent or invalid.");
            }
        }

        private static void ValidatePayloadBoundary(MigrationExecutionJournalRecord record, long sequence)
        {
            Guid operationId;
            string planDigest;
            string actionId;
            string actionSignature;
            switch (record.RecordKind)
            {
                case MigrationExecutionJournalRecordKind.ExecutionState:
                    operationId = record.ExecutionState.OperationId;
                    planDigest = record.ExecutionState.PlanDigest;
                    actionId = null;
                    actionSignature = null;
                    if (record.ExecutionState.RecordedAtUtc != record.RecordedAtUtc
                        || !Enum.IsDefined(typeof(MigrationExecutionStatus), record.ExecutionState.Status))
                    {
                        throw Corruption(sequence, "The execution-state payload is invalid.");
                    }
                    break;
                case MigrationExecutionJournalRecordKind.MutationIntent:
                    operationId = record.MutationIntent.OperationId;
                    planDigest = record.MutationIntent.PlanDigest;
                    actionId = record.MutationIntent.ActionId;
                    actionSignature = record.MutationIntent.ActionSignature;
                    if (record.MutationIntent.WrittenAtUtc != record.RecordedAtUtc
                        || record.MutationIntent.Sequence < 0
                        || string.IsNullOrWhiteSpace(actionId)
                        || !MigrationActionSignature.IsOptionalSha256(actionSignature))
                    {
                        throw Corruption(sequence, "The mutation intent timestamp, sequence, or action signature is invalid.");
                    }
                    break;
                case MigrationExecutionJournalRecordKind.MutationReceipt:
                    operationId = record.MutationReceipt.OperationId;
                    planDigest = record.MutationReceipt.PlanDigest;
                    actionId = record.MutationReceipt.ActionId;
                    actionSignature = record.MutationReceipt.ActionSignature;
                    if (record.MutationReceipt.CompletedAtUtc != record.RecordedAtUtc
                        || record.MutationReceipt.Sequence < 0
                        || string.IsNullOrWhiteSpace(actionId)
                        || !Enum.IsDefined(typeof(MutationOutcome), record.MutationReceipt.Outcome)
                        || !MigrationActionSignature.IsOptionalSha256(actionSignature))
                    {
                        throw Corruption(sequence, "The mutation receipt timestamp, sequence, outcome, or action signature is invalid.");
                    }
                    break;
                case MigrationExecutionJournalRecordKind.MutationVerification:
                    var verification = record.MutationVerification;
                    operationId = verification.OperationId;
                    planDigest = verification.PlanDigest;
                    actionId = verification.ActionId;
                    actionSignature = verification.ActionSignature;
                    if (!string.Equals(verification.SchemaVersion, MigrationMutationVerificationReceipt.CurrentSchemaVersion, StringComparison.Ordinal)
                        || verification.VerifiedAtUtc != record.RecordedAtUtc
                        || string.IsNullOrWhiteSpace(actionId)
                        || !MigrationActionSignature.IsSha256(actionSignature)
                        || !MigrationActionSignature.IsSha256(verification.ObservedStateDigest)
                        || !MigrationActionSignature.IsSha256(verification.TargetIdentityDigest)
                        || !Enum.IsDefined(typeof(MigrationTargetOwnership), verification.Ownership))
                    {
                        throw Corruption(sequence, "The mutation verification payload is incomplete or invalid.");
                    }
                    break;
                case MigrationExecutionJournalRecordKind.ArtifactReference:
                    var artifact = record.ArtifactReference;
                    operationId = artifact.OperationId;
                    planDigest = artifact.PlanDigest;
                    actionId = artifact.ActionId;
                    actionSignature = artifact.ActionSignature;
                    if (!string.Equals(artifact.SchemaVersion, MigrationExecutionArtifactReference.CurrentSchemaVersion, StringComparison.Ordinal)
                        || artifact.WrittenAtUtc != record.RecordedAtUtc
                        || !Enum.IsDefined(typeof(MigrationExecutionArtifactKind), artifact.ArtifactKind)
                        || string.IsNullOrWhiteSpace(artifact.ArtifactSchemaVersion)
                        || !MigrationActionSignature.IsSha256(artifact.Sha256)
                        || artifact.Length < 0
                        || string.IsNullOrWhiteSpace(artifact.MediaType)
                        || !MigrationActionSignature.IsOptionalSha256(actionSignature)
                        || string.IsNullOrWhiteSpace(actionId) != string.IsNullOrWhiteSpace(actionSignature))
                    {
                        throw Corruption(sequence, "The content-addressed artifact reference is incomplete or invalid.");
                    }
                    break;
                default:
                    throw Corruption(sequence, "Unsupported journal record kind.");
            }
            if (operationId != record.OperationId
                || !string.Equals(planDigest, record.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(actionId, record.ActionId, StringComparison.Ordinal)
                || !string.Equals(actionSignature, record.ActionSignature, StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(sequence, "The journal envelope and payload identities differ.");
            }
        }

        private static bool PayloadMatchesKind(MigrationExecutionJournalRecord value)
        {
            return value.RecordKind == MigrationExecutionJournalRecordKind.ExecutionState && value.ExecutionState != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent && value.MutationIntent != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt && value.MutationReceipt != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.MutationVerification && value.MutationVerification != null
                || value.RecordKind == MigrationExecutionJournalRecordKind.ArtifactReference && value.ArtifactReference != null;
        }

        private static void FinalizeResult(MigrationExecutionJournalReadResult result)
        {
            var relationships = new MigrationExecutionJournalRelationshipState();
            foreach (var record in result.Records)
            {
                relationships.Add(record);
            }
            var canonical = string.Join("\n", result.Records.Select(value => value.RecordDigest))
                + "\n--interrupted-tails--\n"
                + string.Join("\n", result.InterruptedTails
                    .OrderBy(value => value.SegmentIndex)
                    .Select(value => value.SegmentIndex + ":" + value.Offset + ":" + value.Sha256));
            result.JournalDigest = MigrationDigest.ComputeSha256(canonical);
        }

        private static MigrationExecutionJournalReadResult Empty()
        {
            var result = new MigrationExecutionJournalReadResult();
            FinalizeResult(result);
            return result;
        }

        private static InvalidDataException Corruption(long sequence, string message, Exception inner = null)
        {
            var diagnostic = "Migration execution journal corruption at record " + sequence + ": " + message;
            return inner == null ? new InvalidDataException(diagnostic) : new InvalidDataException(diagnostic, inner);
        }

        internal sealed class JournalSegment
        {
            public JournalSegment(int index, string path)
            {
                Index = index;
                Path = path;
            }

            public int Index { get; }

            public string Path { get; }
        }
    }
}
