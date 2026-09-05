using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleCheckpointValidator
    {
        public static void ValidateCheckpoint(
            string outputRoot,
            ScaleRunPage page,
            ScaleRunStage stage,
            MigrationActionSignature action,
            ScaleStageExecutionJournalReadResult journal,
            ScaleStageCheckpoint checkpoint)
        {
            if (checkpoint == null
                || !string.Equals(checkpoint.SchemaVersion, ScaleStageCheckpoint.CurrentSchemaVersion, StringComparison.Ordinal)
                || !string.Equals(checkpoint.PageKey, page.PageKey, StringComparison.Ordinal)
                || checkpoint.Stage != stage
                || !string.Equals(checkpoint.ActionSignature, action.Signature, StringComparison.OrdinalIgnoreCase)
                || !MigrationActionSignature.IsSha256(checkpoint.ArtifactSetDigest)
                || !MigrationActionSignature.IsSha256(checkpoint.CheckpointDigest)
                || !string.Equals(checkpoint.CheckpointDigest, ScaleRunStorage.ComputeCheckpointDigest(checkpoint), StringComparison.OrdinalIgnoreCase)
                || !checkpoint.Verified
                || checkpoint.Artifacts == null
                || checkpoint.Artifacts.Count == 0
                || checkpoint.Requests == null
                || !ScaleIngredientResultValidator.HasValidShape(checkpoint.Ingredients)
                || !string.Equals(
                    checkpoint.ArtifactSetDigest,
                    ScaleRunStorage.ComputeArtifactReferenceSetDigest(checkpoint.Artifacts),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "A scale stage checkpoint is incomplete, stale, or not verified."
                    + " schema=" + string.Equals(checkpoint?.SchemaVersion, ScaleStageCheckpoint.CurrentSchemaVersion, StringComparison.Ordinal)
                    + "; page=" + string.Equals(checkpoint?.PageKey, page.PageKey, StringComparison.Ordinal)
                    + "; stage=" + (checkpoint != null && checkpoint.Stage == stage)
                    + "; action=" + string.Equals(checkpoint?.ActionSignature, action.Signature, StringComparison.OrdinalIgnoreCase)
                    + "; checkpointDigest=" + (checkpoint != null && MigrationActionSignature.IsSha256(checkpoint.CheckpointDigest)
                        && string.Equals(checkpoint.CheckpointDigest, ScaleRunStorage.ComputeCheckpointDigest(checkpoint), StringComparison.OrdinalIgnoreCase))
                    + "; verified=" + (checkpoint?.Verified == true)
                    + "; artifacts=" + (checkpoint?.Artifacts?.Count ?? -1)
                    + "; requests=" + (checkpoint?.Requests?.Count ?? -1)
                    + "; ingredients=" + (checkpoint == null ? false : ScaleIngredientResultValidator.HasValidShape(checkpoint.Ingredients))
                    + "; artifactSet=" + (checkpoint != null && MigrationActionSignature.IsSha256(checkpoint.ArtifactSetDigest)
                        && checkpoint.Artifacts != null
                        && string.Equals(checkpoint.ArtifactSetDigest, ScaleRunStorage.ComputeArtifactReferenceSetDigest(checkpoint.Artifacts), StringComparison.OrdinalIgnoreCase)) + ".");
            }
            foreach (var artifact in checkpoint.Artifacts)
            {
                ValidateArtifact(outputRoot, artifact);
            }
            ScaleIngredientResultValidator.Validate(
                outputRoot,
                action,
                checkpoint.Artifacts,
                checkpoint.Requests,
                checkpoint.Ingredients);
            if (checkpoint.DiscoveredProfile != null)
            {
                ScalePageProfile.Validate(checkpoint.DiscoveredProfile);
                ScalePageProfile.ValidateCompatibility(page, checkpoint.DiscoveredProfile);
            }
            var receipts = journal.Records.Where(value =>
                    value.RecordKind == ScaleStageExecutionJournalRecordKind.AttemptCompleted
                    && string.Equals(value.ActionId, action.ActionId, StringComparison.Ordinal)
                    && string.Equals(value.ActionSignature, action.Signature, StringComparison.OrdinalIgnoreCase)
                    && (value.Outcome == ScaleStageOutcome.Succeeded
                        || value.Outcome == ScaleStageOutcome.AlreadySatisfied
                        || value.Outcome == ScaleStageOutcome.OutcomeUnknownButConverged)
                    && value.Verified
                    && value.ProvenanceMatched
                    && value.Outcome == checkpoint.Outcome
                    && value.MutationAttempted == checkpoint.MutationAttempted
                    && string.Equals(value.DiagnosticCode, checkpoint.DiagnosticCode, StringComparison.Ordinal)
                    && string.Equals(value.ArtifactSetDigest, checkpoint.ArtifactSetDigest, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        ComputeRequestMetricsDigest(value.Requests),
                        ComputeRequestMetricsDigest(checkpoint.Requests),
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        ComputeIngredientResultsDigest(value.Ingredients),
                        ComputeIngredientResultsDigest(checkpoint.Ingredients),
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value.ObservedStateDigest, checkpoint.ObservedStateDigest, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value.TargetIdentityDigest, checkpoint.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase)
                    && (checkpoint.DiscoveredProfile == null && value.DiscoveredProfile == null
                        || checkpoint.DiscoveredProfile != null
                            && value.DiscoveredProfile != null
                            && string.Equals(value.DiscoveredProfile.ProfileDigest, checkpoint.DiscoveredProfile.ProfileDigest, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (receipts.Length == 0)
            {
                throw new InvalidDataException("A scale stage checkpoint has no matching digest-sealed stage completion record.");
            }
        }

        public static void ValidateArtifact(string outputRoot, ScaleStageArtifact artifact)
        {
            if (artifact == null
                || !Enum.IsDefined(typeof(ScaleStageArtifactKind), artifact.Kind)
                || !MigrationActionSignature.IsSha256(artifact.Sha256)
                || artifact.Length < 0
                || string.IsNullOrWhiteSpace(artifact.MediaType)
                || string.IsNullOrWhiteSpace(artifact.SchemaVersion))
            {
                throw new InvalidDataException("A scale stage artifact reference is incomplete.");
            }
            var path = ScaleRunStorage.ResolveArtifactPath(outputRoot, artifact.RelativePath);
            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length != artifact.Length
                || !string.Equals(ScaleRunStorage.ComputeFileSha256(path), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A scale stage artifact is missing or differs from its content-addressed checkpoint.");
            }
        }

        public static string ComputeRequestMetricsDigest(IEnumerable<ScaleRequestMetric> requests)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                (requests ?? Enumerable.Empty<ScaleRequestMetric>()).ToArray()));
        }

        public static string ComputeIngredientResultsDigest(IEnumerable<ScaleIngredientRunResult> ingredients)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                (ingredients ?? Enumerable.Empty<ScaleIngredientRunResult>()).ToArray()));
        }
    }
}
