using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using System;
using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal sealed class ScaleRunJournalRecorder
    {
        private readonly ScaleRunManifest manifest;
        private readonly JsonLinesMigrationExecutionJournal mutationJournal;
        private readonly JsonLinesScaleStageExecutionJournal stageJournal;
        private readonly IScaleRunClock clock;

        public ScaleRunJournalRecorder(
            ScaleRunManifest manifest,
            JsonLinesMigrationExecutionJournal mutationJournal,
            JsonLinesScaleStageExecutionJournal stageJournal,
            IScaleRunClock clock)
        {
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this.mutationJournal = mutationJournal ?? throw new ArgumentNullException(nameof(mutationJournal));
            this.stageJournal = stageJournal ?? throw new ArgumentNullException(nameof(stageJournal));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public Guid Start(
            ScaleRunPage page,
            ScaleRunStage stage,
            int attempt,
            MigrationActionSignature action,
            bool mutationAudit,
            bool writeMutationIntent,
            string description)
        {
            var operationId = Guid.NewGuid();
            stageJournal.Write(new ScaleStageExecutionJournalRecord
            {
                RecordKind = ScaleStageExecutionJournalRecordKind.AttemptStarted,
                RecordedAtUtc = clock.UtcNow,
                OperationId = operationId,
                ManifestDigest = manifest.ManifestDigest,
                PageKey = page.PageKey,
                Stage = stage,
                Attempt = attempt,
                ActionId = action.ActionId,
                ActionSignature = action.Signature,
                DiagnosticCode = attempt == 0 ? "RecoveryStarted" : "AttemptStarted"
            });
            if (mutationAudit)
            {
                WriteMutationState(operationId, MigrationExecutionStatus.Running, description);
                if (writeMutationIntent)
                {
                    mutationJournal.WriteIntent(new MigrationMutationIntent
                    {
                        OperationId = operationId,
                        PlanDigest = manifest.ManifestDigest,
                        ActionId = action.ActionId,
                        ActionSignature = action.Signature,
                        Sequence = 0,
                        WrittenAtUtc = clock.UtcNow,
                        Description = description
                    });
                }
            }
            return operationId;
        }

        public void Complete(
            Guid operationId,
            ScaleRunPage page,
            ScaleRunStage stage,
            int attempt,
            MigrationActionSignature action,
            ScaleStageExecutionResult result,
            bool mutationAudit)
        {
            if (mutationAudit)
            {
                CompleteMutation(operationId, action, result);
            }

            CompleteStage(operationId, page, stage, attempt, action, result);
        }

        public void CompleteMutation(
            Guid operationId,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            mutationJournal.WriteReceipt(new MigrationMutationReceipt
            {
                OperationId = operationId,
                PlanDigest = manifest.ManifestDigest,
                ActionId = action.ActionId,
                ActionSignature = action.Signature,
                Sequence = 0,
                CompletedAtUtc = clock.UtcNow,
                Outcome = ToMutationOutcome(result.Outcome),
                Message = string.IsNullOrWhiteSpace(result.DiagnosticCode)
                    ? result.Outcome.ToString()
                    : result.DiagnosticCode
            });
            foreach (var artifact in result.Artifacts)
            {
                mutationJournal.WriteArtifactReference(new MigrationExecutionArtifactReference
                {
                    OperationId = operationId,
                    PlanDigest = manifest.ManifestDigest,
                    ActionId = action.ActionId,
                    ActionSignature = action.Signature,
                    WrittenAtUtc = clock.UtcNow,
                    ArtifactKind = ToArtifactKind(artifact.Kind),
                    ArtifactSchemaVersion = artifact.SchemaVersion,
                    Sha256 = artifact.Sha256,
                    Length = artifact.Length,
                    MediaType = artifact.MediaType
                });
            }
            if (ScaleStageOutcomeRules.IsSuccessful(result.Outcome))
            {
                mutationJournal.WriteVerification(new MigrationMutationVerificationReceipt
                {
                    OperationId = operationId,
                    PlanDigest = manifest.ManifestDigest,
                    ActionId = action.ActionId,
                    ActionSignature = action.Signature,
                    VerifiedAtUtc = clock.UtcNow,
                    FreshReadbackPassed = result.Verified,
                    ObservedStateDigest = result.ObservedStateDigest,
                    Ownership = MigrationTargetOwnership.MigrationOwned,
                    TargetIdentityDigest = result.TargetIdentityDigest,
                    ProvenanceMatched = result.ProvenanceMatched,
                    Message = "Scale target state and retained evidence were freshly verified."
                });
            }
            WriteMutationState(
                operationId,
                ScaleStageOutcomeRules.IsSuccessful(result.Outcome)
                    ? MigrationExecutionStatus.Succeeded
                    : MigrationExecutionStatus.FailedUnexpectedly,
                ScaleStageOutcomeRules.IsSuccessful(result.Outcome)
                    ? result.Ingredients.Any(value => value.Outcome == ScaleIngredientOutcome.AuthorizationBlocked)
                        ? "Scale mutation action converged; literal-authorization ingredient terminals remain recorded for page-level acceptance."
                        : "Scale mutation action converged and was verified."
                    : "Scale mutation action did not converge; evidence was retained.");
        }

        public void CompleteStage(
            Guid operationId,
            ScaleRunPage page,
            ScaleRunStage stage,
            int attempt,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            stageJournal.Write(new ScaleStageExecutionJournalRecord
            {
                RecordKind = ScaleStageExecutionJournalRecordKind.AttemptCompleted,
                RecordedAtUtc = clock.UtcNow,
                OperationId = operationId,
                ManifestDigest = manifest.ManifestDigest,
                PageKey = page.PageKey,
                Stage = stage,
                Attempt = attempt,
                ActionId = action.ActionId,
                ActionSignature = action.Signature,
                Outcome = result.Outcome,
                Verified = result.Verified,
                MutationAttempted = result.MutationAttempted,
                ProvenanceMatched = result.ProvenanceMatched,
                ObservedStateDigest = result.ObservedStateDigest,
                TargetIdentityDigest = result.TargetIdentityDigest,
                ArtifactSetDigest = ScaleRunStorage.ComputeArtifactReferenceSetDigest(result.Artifacts),
                Artifacts = new System.Collections.Generic.List<ScaleStageArtifact>(result.Artifacts),
                Requests = new System.Collections.Generic.List<ScaleRequestMetric>(result.Requests),
                Ingredients = new System.Collections.Generic.List<ScaleIngredientRunResult>(result.Ingredients),
                DiagnosticCode = result.DiagnosticCode,
                DiscoveredProfile = result.DiscoveredProfile
            });
        }

        private void WriteMutationState(
            Guid operationId,
            MigrationExecutionStatus status,
            string message)
        {
            mutationJournal.WriteExecutionState(new MigrationExecutionStateReceipt
            {
                OperationId = operationId,
                PlanDigest = manifest.ManifestDigest,
                RecordedAtUtc = clock.UtcNow,
                Status = status,
                Message = message
            });
        }

        private static MutationOutcome ToMutationOutcome(ScaleStageOutcome outcome)
        {
            if (outcome == ScaleStageOutcome.AlreadySatisfied)
            {
                return MutationOutcome.AlreadySatisfied;
            }
            if (outcome == ScaleStageOutcome.OutcomeUnknownButConverged)
            {
                return MutationOutcome.OutcomeUnknownButConverged;
            }
            return outcome == ScaleStageOutcome.Succeeded
                ? MutationOutcome.Applied
                : MutationOutcome.Failed;
        }

        private static MigrationExecutionArtifactKind ToArtifactKind(ScaleStageArtifactKind kind)
        {
            return kind == ScaleStageArtifactKind.Output
                ? MigrationExecutionArtifactKind.MaterializationReceipt
                : MigrationExecutionArtifactKind.VerificationEvidence;
        }
    }
}
