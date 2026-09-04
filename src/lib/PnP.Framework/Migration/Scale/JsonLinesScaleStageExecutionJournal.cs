using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Scale
{
    internal sealed class JsonLinesScaleStageExecutionJournal : IDisposable
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly object gate = new object();
        private readonly FileStream stream;
        private readonly Dictionary<Guid, ScaleStageExecutionJournalRecord> started =
            new Dictionary<Guid, ScaleStageExecutionJournalRecord>();
        private long nextSequence;
        private string previousDigest;
        private bool disposed;

        public JsonLinesScaleStageExecutionJournal(string path)
        {
            var fullPath = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);
            var read = ScaleStageExecutionJournalReader.Read(fullPath);
            nextSequence = read.Records.Count;
            previousDigest = read.Records.LastOrDefault()?.RecordDigest;
            foreach (var record in read.Records)
            {
                ScaleStageExecutionJournalReader.ValidateRelationship(record, started);
            }
            var segments = MigrationExecutionJournalReader.GetSegments(fullPath);
            var segmentIndex = segments.Count == 0 ? 0 : segments[segments.Count - 1].Index;
            if (read.InterruptedTails.Any(value => value.SegmentIndex == segmentIndex))
            {
                segmentIndex++;
            }
            var segmentPath = MigrationExecutionJournalReader.SegmentPath(fullPath, segmentIndex);
            stream = new FileStream(segmentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        }

        public void Write(ScaleStageExecutionJournalRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            lock (gate)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(JsonLinesScaleStageExecutionJournal));
                }
                record.JournalSequence = nextSequence;
                record.PreviousRecordDigest = previousDigest;
                record.RecordDigest = ScaleStageExecutionJournalRecord.ComputeRecordDigest(record);
                ScaleStageExecutionJournalReader.Validate(record, nextSequence, previousDigest);
                var nextStarted = new Dictionary<Guid, ScaleStageExecutionJournalRecord>(started);
                ScaleStageExecutionJournalReader.ValidateRelationship(record, nextStarted);
                var bytes = Utf8.GetBytes(MigrationContractSerializer.SerializeCanonical(record) + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
                started.Clear();
                foreach (var value in nextStarted)
                {
                    started.Add(value.Key, value.Value);
                }
                previousDigest = record.RecordDigest;
                nextSequence++;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                stream.Dispose();
            }
        }
    }
}
