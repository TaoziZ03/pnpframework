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
            ScaleStageJournalRecordValidator.Validate(record, expectedSequence, previousDigest);
        }

        internal static void ValidateRelationship(
            ScaleStageExecutionJournalRecord record,
            IDictionary<Guid, ScaleStageExecutionJournalRecord> started)
        {
            ScaleStageJournalRecordValidator.ValidateRelationship(record, started);
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
                            ScaleStageJournalRecordValidator.ValidateRelationship(record, started);
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
            ScaleStageJournalRecordValidator.Validate(record, expectedSequence, previousDigest);
            return record;
        }

        internal static InvalidDataException Corruption(long sequence, string message, Exception inner = null)
        {
            var diagnostic = "Scale stage journal corruption at record " + sequence + ": " + message;
            return inner == null
                ? new InvalidDataException(diagnostic)
                : new InvalidDataException(diagnostic, inner);
        }
    }
}
