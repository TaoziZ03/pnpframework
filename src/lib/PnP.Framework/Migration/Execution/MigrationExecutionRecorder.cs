using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Execution
{
    internal sealed class MigrationExecutionRecorder
    {
        private readonly IMigrationExecutionJournal journal;
        private readonly MigrationExecutionBoundary boundary;
        private int sequence;

        public MigrationExecutionRecorder(
            Guid operationId,
            string planDigest,
            IMigrationExecutionJournal journal)
            : this(
                operationId,
                new MigrationExecutionBoundary { PlanDigest = planDigest },
                journal)
        {
        }

        public MigrationExecutionRecorder(
            Guid operationId,
            MigrationExecutionBoundary boundary,
            IMigrationExecutionJournal journal)
        {
            if (operationId == Guid.Empty)
            {
                throw new ArgumentException("A migration operation ID is required.", nameof(operationId));
            }
            if (boundary == null || string.IsNullOrWhiteSpace(boundary.PlanDigest))
            {
                throw new ArgumentException("A migration execution boundary with a plan digest is required.", nameof(boundary));
            }
            OperationId = operationId;
            PlanDigest = boundary.PlanDigest;
            this.boundary = boundary;
            this.journal = journal ?? NullMigrationExecutionJournal.Instance;
        }

        public Guid OperationId { get; }

        public string PlanDigest { get; }

        public MigrationExecutionBoundary Boundary => boundary;

        public IList<MigrationMutationReceipt> Steps { get; } = new List<MigrationMutationReceipt>();

        public void RecordState(MigrationExecutionStatus status, string message)
        {
            journal.WriteExecutionState(new MigrationExecutionStateReceipt
            {
                OperationId = OperationId,
                PlanDigest = PlanDigest,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Status = status,
                Message = message,
                SourceSnapshotDigest = boundary.SourceSnapshotDigest,
                ApprovalDigest = boundary.ApprovalDigest,
                TargetBoundaryDigest = boundary.TargetBoundaryDigest
            });
        }

        public T Execute<T>(
            string actionId,
            string description,
            Func<T> action,
            Func<T, MutationOutcome> outcome,
            Func<T, string> message)
        {
            return Execute(null, actionId, description, action, outcome, message);
        }

        public T Execute<T>(
            MigrationMutationIdentity identity,
            string actionId,
            string description,
            Func<T> action,
            Func<T, MutationOutcome> outcome,
            Func<T, string> message)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("An action ID is required.", nameof(actionId));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            ValidateIdentity(identity, actionId);

            var currentSequence = sequence++;
            var intent = new MigrationMutationIntent
            {
                OperationId = OperationId,
                PlanDigest = PlanDigest,
                ActionId = actionId,
                Sequence = currentSequence,
                WrittenAtUtc = DateTimeOffset.UtcNow,
                Description = description
            };
            PopulateIdentity(intent, identity);
            journal.WriteIntent(intent);
            try
            {
                var result = action();
                RecordReceipt(
                    actionId,
                    currentSequence,
                    outcome == null ? MutationOutcome.Applied : outcome(result),
                    message == null ? description : message(result),
                    identity);
                return result;
            }
            catch (Exception exception)
            {
                RecordReceipt(
                    actionId,
                    currentSequence,
                    MutationOutcome.Failed,
                    exception.Message,
                    identity);
                throw;
            }
        }

        public void Execute(string actionId, string description, Action action)
        {
            Execute(
                actionId,
                description,
                () =>
                {
                    action();
                    return true;
                },
                value => MutationOutcome.Applied,
                value => description);
        }

        public void RecordAlreadySatisfied(string actionId, string message)
        {
            RecordAlreadySatisfied(null, actionId, message);
        }

        public void RecordAlreadySatisfied(
            MigrationMutationIdentity identity,
            string actionId,
            string message)
        {
            ValidateIdentity(identity, actionId);
            var currentSequence = sequence++;
            RecordReceipt(actionId, currentSequence, MutationOutcome.AlreadySatisfied, message, identity);
        }

        public void RecordVerification(MigrationMutationVerificationReceipt verification)
        {
            if (verification == null)
            {
                throw new ArgumentNullException(nameof(verification));
            }
            if (journal is IMigrationExecutionCheckpointJournal checkpointJournal)
            {
                checkpointJournal.WriteVerification(verification);
            }
        }

        public void RecordArtifact(MigrationExecutionArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            if (journal is IMigrationExecutionCheckpointJournal checkpointJournal)
            {
                checkpointJournal.WriteArtifact(artifact);
            }
        }

        private void RecordReceipt(
            string actionId,
            int currentSequence,
            MutationOutcome outcome,
            string message,
            MigrationMutationIdentity identity)
        {
            var receipt = new MigrationMutationReceipt
            {
                OperationId = OperationId,
                PlanDigest = PlanDigest,
                ActionId = actionId,
                Sequence = currentSequence,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Outcome = outcome,
                Message = message
            };
            PopulateIdentity(receipt, identity);
            Steps.Add(receipt);
            journal.WriteReceipt(receipt);
        }

        private void ValidateIdentity(MigrationMutationIdentity identity, string actionId)
        {
            if (identity == null)
            {
                return;
            }
            if (!string.Equals(identity.ActionId, actionId, StringComparison.Ordinal))
            {
                throw new ArgumentException("The mutation identity action ID differs from the executed action.", nameof(identity));
            }
            var expected = MigrationMutationIdentity.ComputeIdempotencyKey(boundary, identity);
            if (!string.Equals(identity.IdempotencyKey, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The mutation identity idempotency key is absent or invalid.", nameof(identity));
            }
        }

        private void PopulateIdentity(MigrationMutationIntent target, MigrationMutationIdentity identity)
        {
            target.SourceSnapshotDigest = boundary.SourceSnapshotDigest;
            target.ApprovalDigest = boundary.ApprovalDigest;
            target.TargetBoundaryDigest = boundary.TargetBoundaryDigest;
            if (identity == null)
            {
                return;
            }
            target.IngredientId = identity.IngredientId;
            target.SelectedDisposition = identity.SelectedDisposition;
            target.SemanticDigest = identity.SemanticDigest;
            target.IdempotencyKey = identity.IdempotencyKey;
        }

        private void PopulateIdentity(MigrationMutationReceipt target, MigrationMutationIdentity identity)
        {
            target.SourceSnapshotDigest = boundary.SourceSnapshotDigest;
            target.ApprovalDigest = boundary.ApprovalDigest;
            target.TargetBoundaryDigest = boundary.TargetBoundaryDigest;
            if (identity == null)
            {
                return;
            }
            target.IngredientId = identity.IngredientId;
            target.SelectedDisposition = identity.SelectedDisposition;
            target.SemanticDigest = identity.SemanticDigest;
            target.IdempotencyKey = identity.IdempotencyKey;
        }
    }
}
