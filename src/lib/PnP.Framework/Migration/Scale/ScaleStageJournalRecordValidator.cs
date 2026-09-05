using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleStageJournalRecordValidator
    {
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
                || !string.Equals(record.RecordDigest, ScaleStageExecutionJournalRecord.ComputeRecordDigest(record), StringComparison.OrdinalIgnoreCase)
                || (record.DiscoveredProfile != null
                    && (!string.Equals(record.DiscoveredProfile.SchemaVersion, ScalePageProfile.CurrentSchemaVersion, StringComparison.Ordinal)
                        || !MigrationActionSignature.IsSha256(record.DiscoveredProfile.ProfileDigest)
                        || !string.Equals(record.DiscoveredProfile.ProfileDigest, ScalePageProfile.ComputeProfileDigest(record.DiscoveredProfile), StringComparison.OrdinalIgnoreCase))))
            {
                throw ScaleStageExecutionJournalReader.Corruption(expectedSequence, "A record has an invalid schema, chain, operation, action, or digest boundary.");
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
                    || record.Requests.Count != 0
                    || record.Ingredients == null
                    || record.Ingredients.Count != 0)
                {
                    throw ScaleStageExecutionJournalReader.Corruption(expectedSequence, "A started record contains completion evidence.");
                }
                return;
            }
            if (!Enum.IsDefined(typeof(ScaleStageOutcome), record.Outcome)
                || !MigrationActionSignature.IsSha256(record.ArtifactSetDigest)
                || record.Artifacts == null
                || record.Artifacts.Count == 0
                || record.Requests == null
                || !ScaleIngredientResultValidator.HasValidShape(record.Ingredients)
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
                throw ScaleStageExecutionJournalReader.Corruption(expectedSequence, "A completed record has invalid outcome or evidence digests.");
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
                    throw ScaleStageExecutionJournalReader.Corruption(record.JournalSequence, "An operation has duplicate start records.");
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
                throw ScaleStageExecutionJournalReader.Corruption(record.JournalSequence, "A completion record has no exact matching start record.");
            }
            started.Remove(record.OperationId);
        }

    }
}
