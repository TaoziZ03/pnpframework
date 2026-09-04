using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Scale
{
    internal enum ScaleStageExecutionJournalRecordKind
    {
        AttemptStarted = 1,
        AttemptCompleted = 2
    }

    internal sealed class ScaleStageExecutionJournalRecord
    {
        public const string CurrentSchemaVersion = "pnp-scale-stage-journal-record/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public ScaleStageExecutionJournalRecordKind RecordKind { get; set; }

        public long JournalSequence { get; set; }

        public string PreviousRecordDigest { get; set; }

        public DateTimeOffset RecordedAtUtc { get; set; }

        public Guid OperationId { get; set; }

        public string ManifestDigest { get; set; }

        public string PageKey { get; set; }

        public ScaleRunStage Stage { get; set; }

        public int Attempt { get; set; }

        public string ActionId { get; set; }

        public string ActionSignature { get; set; }

        public ScaleStageOutcome Outcome { get; set; }

        public bool Verified { get; set; }

        public bool MutationAttempted { get; set; }

        public bool ProvenanceMatched { get; set; }

        public string ObservedStateDigest { get; set; }

        public string TargetIdentityDigest { get; set; }

        public string ArtifactSetDigest { get; set; }

        public IList<ScaleStageArtifact> Artifacts { get; set; } = new List<ScaleStageArtifact>();

        public IList<ScaleRequestMetric> Requests { get; set; } = new List<ScaleRequestMetric>();

        public string DiagnosticCode { get; set; }

        public string RecordDigest { get; set; }

        public static string ComputeRecordDigest(ScaleStageExecutionJournalRecord value)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    value ?? throw new ArgumentNullException(nameof(value)),
                    nameof(RecordDigest)));
        }
    }

    internal sealed class ScaleStageExecutionJournalReadResult
    {
        public IList<ScaleStageExecutionJournalRecord> Records { get; } =
            new List<ScaleStageExecutionJournalRecord>();

        public IList<ScaleInterruptedStageJournalTail> InterruptedTails { get; } =
            new List<ScaleInterruptedStageJournalTail>();

        public bool HasInterruptedTail => InterruptedTails.Count > 0;

        public string JournalDigest { get; internal set; }
    }

    internal sealed class ScaleInterruptedStageJournalTail
    {
        public int SegmentIndex { get; set; }

        public string SegmentPath { get; set; }

        public long Offset { get; set; }

        public long ByteCount { get; set; }

        public string Sha256 { get; set; }
    }
}
