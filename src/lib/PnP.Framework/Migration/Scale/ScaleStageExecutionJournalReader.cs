using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleStageExecutionJournalReader
    {
        private const int MaximumRecordBytes = 4 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static ScaleStageExecutionJournalReadResult Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A scale stage journal path is required.", nameof(path));
            }
            var segments = MigrationExecutionJournalReader.GetSegments(Path.GetFullPath(path));
            if (segments.Count > 0 && segments[0].Index != 0)
            {
                throw new InvalidDataException("The scale stage journal is missing its base segment.");
            }
            for (var index = 0; index < segments.Count; index++)
            {
                if (segments[index].Index != index)
                {
                    throw new InvalidDataException("Scale stage journal segment indexes are missing or out of order.");
                }
            }
            var result = new ScaleStageExecutionJournalReadResult();
            var expectedSequence = 0L;
            string previousDigest = null;
            var started = new Dictionary<Guid, ScaleStageExecutionJournalRecord>();
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
                        started,
                        ref expectedSequence,
                        ref previousDigest);
                }
            }
            result.JournalDigest = MigrationDigest.ComputeSha256(string.Join(
                "\n",
                result.Records.Select(value => value.RecordDigest))
                + "\n--interrupted-tails--\n"
                + string.Join("\n", result.InterruptedTails.Select(value =>
                    value.SegmentIndex + ":" + value.Offset + ":" + value.Sha256)));
            return result;
        }

        internal static void Validate(
            ScaleStageExecutionJournalRecord record,
            long expectedSequence,
            string previousDigest)
        {
            if (record == null
                || !string.Equals(record.SchemaVersion, ScaleStageExecutionJournalRecord.CurrentSchemaVersion, StringComparison.Ordinal)
                || !Enum.IsDefined(typeof(ScaleStageExecutionJournalRecordKind), record.RecordKind)
                || record.JournalSequence != expectedSequence
                || record.RecordedAtUtc == default(DateTimeOffset)
                || record.OperationId == Guid.Empty
                || !MigrationActionSignature.IsSha256(record.ManifestDigest)
                || string.IsNullOrWhiteSpace(record.PageKey)
                || !Enum.IsDefined(typeof(ScaleRunStage), record.Stage)
                || record.Attempt < 0
                || string.IsNullOrWhiteSpace(record.ActionId)
                || !MigrationActionSignature.IsSha256(record.ActionSignature)
                || string.IsNullOrWhiteSpace(record.DiagnosticCode)
                || record.DiagnosticCode.Length > 256
                || record.DiagnosticCode.Any(character => !(char.IsLetterOrDigit(character)
                    || character == '-'
                    || character == '_'
                    || character == '.'
                    || character == '/'))
                || (expectedSequence == 0 && !string.IsNullOrWhiteSpace(record.PreviousRecordDigest))
                || (expectedSequence > 0
                    && !string.Equals(record.PreviousRecordDigest, previousDigest, StringComparison.OrdinalIgnoreCase))
                || !MigrationActionSignature.IsSha256(record.RecordDigest)
                || !string.Equals(record.RecordDigest, ScaleStageExecutionJournalRecord.ComputeRecordDigest(record), StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(expectedSequence, "A record has an invalid schema, chain, operation, action, or digest boundary.");
            }
            if (record.RecordKind == ScaleStageExecutionJournalRecordKind.AttemptStarted)
            {
                if (record.Outcome != 0
                    || record.Verified
                    || record.MutationAttempted
                    || record.ProvenanceMatched
                    || record.ObservedStateDigest != null
                    || record.TargetIdentityDigest != null
                    || record.ArtifactSetDigest != null
                    || record.Artifacts == null
                    || record.Artifacts.Count != 0
                    || record.Requests == null
                    || record.Requests.Count != 0)
                {
                    throw Corruption(expectedSequence, "A started record contains completion evidence.");
                }
                return;
            }
            if (!Enum.IsDefined(typeof(ScaleStageOutcome), record.Outcome)
                || !MigrationActionSignature.IsSha256(record.ArtifactSetDigest)
                || record.Artifacts == null
                || record.Artifacts.Count == 0
                || record.Requests == null
                || !string.Equals(
                    record.ArtifactSetDigest,
                    ScaleRunStorage.ComputeArtifactReferenceSetDigest(record.Artifacts),
                    StringComparison.OrdinalIgnoreCase)
                || record.ObservedStateDigest != null && !MigrationActionSignature.IsSha256(record.ObservedStateDigest)
                || record.TargetIdentityDigest != null && !MigrationActionSignature.IsSha256(record.TargetIdentityDigest)
                || record.Artifacts.Any(value => value == null
                    || !Enum.IsDefined(typeof(ScaleStageArtifactKind), value.Kind)
                    || string.IsNullOrWhiteSpace(value.RelativePath)
                    || Path.IsPathRooted(value.RelativePath)
                    || value.RelativePath.IndexOf(':') >= 0
                    || value.RelativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(segment => segment == "." || segment == "..")
                    || !MigrationActionSignature.IsSha256(value.Sha256)
                    || value.Length < 0
                    || string.IsNullOrWhiteSpace(value.MediaType)
                    || string.IsNullOrWhiteSpace(value.SchemaVersion))
                || record.Requests.Any(value => value == null
                    || string.IsNullOrWhiteSpace(value.Operation)
                    || value.Operation.Length > 256
                    || value.Operation.Any(character => !(char.IsLetterOrDigit(character)
                        || character == '-'
                        || character == '_'
                        || character == '.'
                        || character == '/'))
                    || value.DurationMilliseconds < 0
                    || value.ResponseBytes < 0
                    || value.RetryAfterWaitMilliseconds < 0)
                || ScaleStageOutcomeRules.IsSuccessful(record.Outcome)
                    && (!record.Verified
                        || !record.ProvenanceMatched
                        || !MigrationActionSignature.IsSha256(record.ObservedStateDigest)
                        || !MigrationActionSignature.IsSha256(record.TargetIdentityDigest)))
            {
                throw Corruption(expectedSequence, "A completed record has invalid outcome or evidence digests.");
            }
        }

        internal static void ValidateRelationship(
            ScaleStageExecutionJournalRecord record,
            IDictionary<Guid, ScaleStageExecutionJournalRecord> started)
        {
            if (record.RecordKind == ScaleStageExecutionJournalRecordKind.AttemptStarted)
            {
                if (started.ContainsKey(record.OperationId))
                {
                    throw Corruption(record.JournalSequence, "An operation has duplicate start records.");
                }
                started.Add(record.OperationId, record);
                return;
            }
            if (!started.TryGetValue(record.OperationId, out var start)
                || !string.Equals(start.ManifestDigest, record.ManifestDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(start.PageKey, record.PageKey, StringComparison.Ordinal)
                || start.Stage != record.Stage
                || start.Attempt != record.Attempt
                || !string.Equals(start.ActionId, record.ActionId, StringComparison.Ordinal)
                || !string.Equals(start.ActionSignature, record.ActionSignature, StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(record.JournalSequence, "A completion record has no exact matching start record.");
            }
            started.Remove(record.OperationId);
        }

        private static void ReadSegment(
            Stream stream,
            int segmentIndex,
            string segmentPath,
            ScaleStageExecutionJournalReadResult result,
            IDictionary<Guid, ScaleStageExecutionJournalRecord> started,
            ref long expectedSequence,
            ref string previousDigest)
        {
            using (var line = new MemoryStream())
            {
                var buffer = new byte[8192];
                var absoluteOffset = stream.CanSeek ? stream.Position : 0L;
                var lineOffset = absoluteOffset;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (var index = 0; index < read; index++, absoluteOffset++)
                    {
                        if (buffer[index] == (byte)'\n')
                        {
                            var bytes = line.ToArray();
                            if (bytes.Length > 0 && bytes[bytes.Length - 1] == (byte)'\r')
                            {
                                Array.Resize(ref bytes, bytes.Length - 1);
                            }
                            if (bytes.Length == 0)
                            {
                                throw Corruption(expectedSequence, "Blank records are not valid.");
                            }
                            var record = Parse(bytes, expectedSequence, previousDigest);
                            ValidateRelationship(record, started);
                            result.Records.Add(record);
                            previousDigest = record.RecordDigest;
                            expectedSequence++;
                            line.SetLength(0);
                            lineOffset = absoluteOffset + 1;
                            continue;
                        }
                        if (line.Length >= MaximumRecordBytes)
                        {
                            throw Corruption(expectedSequence, "A record exceeds the 4 MiB safety limit.");
                        }
                        line.WriteByte(buffer[index]);
                    }
                }
                if (line.Length > 0)
                {
                    var tail = line.ToArray();
                    result.InterruptedTails.Add(new ScaleInterruptedStageJournalTail
                    {
                        SegmentIndex = segmentIndex,
                        SegmentPath = segmentPath,
                        Offset = lineOffset,
                        ByteCount = tail.Length,
                        Sha256 = MigrationDigest.ComputeSha256(tail)
                    });
                }
            }
        }

        private static ScaleStageExecutionJournalRecord Parse(
            byte[] bytes,
            long expectedSequence,
            string previousDigest)
        {
            string json;
            try
            {
                json = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw Corruption(expectedSequence, "A record is not valid UTF-8.", exception);
            }
            ScaleStageExecutionJournalRecord record;
            try
            {
                record = MigrationContractSerializer.Deserialize<ScaleStageExecutionJournalRecord>(json);
                if (!string.Equals(
                    json,
                    MigrationContractSerializer.SerializeCanonical(record),
                    StringComparison.Ordinal))
                {
                    throw Corruption(expectedSequence, "A record is not canonical typed JSON.");
                }
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException || exception is NotSupportedException)
            {
                throw Corruption(expectedSequence, "A record is not valid schema JSON.", exception);
            }
            Validate(record, expectedSequence, previousDigest);
            return record;
        }

        private static InvalidDataException Corruption(long sequence, string message, Exception inner = null)
        {
            var diagnostic = "Scale stage journal corruption at record " + sequence + ": " + message;
            return inner == null
                ? new InvalidDataException(diagnostic)
                : new InvalidDataException(diagnostic, inner);
        }
    }
}
