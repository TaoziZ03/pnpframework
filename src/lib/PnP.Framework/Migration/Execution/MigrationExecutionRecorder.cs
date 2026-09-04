using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Execution
{
    internal sealed class MigrationExecutionRecorder
    {
        private readonly IMigrationExecutionJournal journal;
        private int sequence;

        public MigrationExecutionRecorder(
            Guid operationId,
            string planDigest,
            IMigrationExecutionJournal journal)
        {
            OperationId = operationId;
            PlanDigest = planDigest;
            this.journal = journal ?? NullMigrationExecutionJournal.Instance;
        }

        public Guid OperationId { get; }

        public string PlanDigest { get; }

        public IList<MigrationMutationReceipt> Steps { get; } = new List<MigrationMutationReceipt>();

        public void RecordState(MigrationExecutionStatus status, string message)
        {
            journal.WriteExecutionState(new MigrationExecutionStateReceipt
            {
                OperationId = OperationId,
                PlanDigest = PlanDigest,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Status = status,
                Message = message
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
            MigrationActionSignature signature,
            string description,
            Func<T> action,
            Func<T, MutationOutcome> outcome,
            Func<T, string> message)
        {
            if (signature == null)
            {
                throw new ArgumentNullException(nameof(signature));
            }
            return Execute(signature, signature.ActionId, description, action, outcome, message);
        }

        private T Execute<T>(
            MigrationActionSignature signature,
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
            ValidateSignature(signature, actionId);

            var currentSequence = sequence++;
            journal.WriteIntent(new MigrationMutationIntent
            {
                OperationId = OperationId,
                PlanDigest = PlanDigest,
                ActionId = actionId,
                Sequence = currentSequence,
                WrittenAtUtc = DateTimeOffset.UtcNow,
                Description = description,
                ActionSignature = signature?.Signature
            });
            try
            {
                var result = action();
                RecordReceipt(
                    actionId,
                    currentSequence,
                    outcome == null ? MutationOutcome.Applied : outcome(result),
                    message == null ? description : message(result),
                    signature);
                return result;
            }
            catch (Exception exception)
            {
                RecordReceipt(
                    actionId,
                    currentSequence,
                    MutationOutcome.Failed,
                    exception.Message,
                    signature);
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
            var currentSequence = sequence++;
            RecordReceipt(actionId, currentSequence, MutationOutcome.AlreadySatisfied, message, null);
        }

        public void RecordAlreadySatisfied(MigrationActionSignature signature, string message)
        {
            if (signature == null)
            {
                throw new ArgumentNullException(nameof(signature));
            }
            ValidateSignature(signature, signature.ActionId);
            var currentSequence = sequence++;
            RecordReceipt(signature.ActionId, currentSequence, MutationOutcome.AlreadySatisfied, message, signature);
        }

        public void RecordVerification(MigrationMutationVerificationReceipt verification)
        {
            if (verification == null)
            {
                throw new ArgumentNullException(nameof(verification));
            }
            if (verification.OperationId != OperationId
                || !string.Equals(verification.PlanDigest, PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The verification does not belong to this execution operation and plan.", nameof(verification));
            }
            if (journal is IMigrationExecutionCheckpointJournal checkpointJournal)
            {
                checkpointJournal.WriteVerification(verification);
            }
        }

        public void RecordArtifactReference(MigrationExecutionArtifactReference artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            if (artifact.OperationId != OperationId
                || !string.Equals(artifact.PlanDigest, PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The artifact reference does not belong to this execution operation and plan.", nameof(artifact));
            }
            if (journal is IMigrationExecutionCheckpointJournal checkpointJournal)
            {
                checkpointJournal.WriteArtifactReference(artifact);
            }
        }

        private void RecordReceipt(
            string actionId,
            int currentSequence,
            MutationOutcome outcome,
            string message,
            MigrationActionSignature signature)
        {
            var receipt = new MigrationMutationReceipt
            {
                OperationId = OperationId,
                PlanDigest = PlanDigest,
                ActionId = actionId,
                Sequence = currentSequence,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Outcome = outcome,
                Message = message,
                ActionSignature = signature?.Signature
            };
            Steps.Add(receipt);
            journal.WriteReceipt(receipt);
        }

        private static void ValidateSignature(MigrationActionSignature signature, string actionId)
        {
            if (signature == null)
            {
                return;
            }
            MigrationActionSignature.Validate(signature);
            if (!string.Equals(signature.ActionId, actionId, StringComparison.Ordinal))
            {
                throw new ArgumentException("The action signature names a different action.", nameof(signature));
            }
        }
    }
}
