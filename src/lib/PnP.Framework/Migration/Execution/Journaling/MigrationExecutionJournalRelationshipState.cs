using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Migration.Execution.Journaling
{
    internal sealed class MigrationExecutionJournalRelationshipState
    {
        private readonly IDictionary<string, MigrationMutationIntent> intents =
            new Dictionary<string, MigrationMutationIntent>(StringComparer.Ordinal);
        private readonly IDictionary<string, MigrationMutationReceipt> receipts =
            new Dictionary<string, MigrationMutationReceipt>(StringComparer.Ordinal);
        private readonly IDictionary<string, MigrationMutationReceipt> receiptsByAction =
            new Dictionary<string, MigrationMutationReceipt>(StringComparer.Ordinal);
        private readonly ISet<string> verifications = new HashSet<string>(StringComparer.Ordinal);
        private readonly IDictionary<Guid, string> operationPlans = new Dictionary<Guid, string>();

        public void Add(MigrationExecutionJournalRecord record)
        {
            if (operationPlans.TryGetValue(record.OperationId, out var existingPlan)
                && !string.Equals(existingPlan, record.PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw Corruption(record.JournalSequence, "One operation identity is bound to multiple plan digests.");
            }
            if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent)
            {
                var key = AttemptKey(record.OperationId, record.MutationIntent.Sequence);
                if (intents.ContainsKey(key))
                {
                    throw Corruption(record.JournalSequence, "The journal contains a duplicate mutation intent sequence for one operation.");
                }
                intents.Add(key, record.MutationIntent);
                BindOperationPlan(record);
                return;
            }
            if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt)
            {
                var key = AttemptKey(record.OperationId, record.MutationReceipt.Sequence);
                var actionKey = ActionKey(record.OperationId, record.ActionId, record.ActionSignature);
                if (receipts.ContainsKey(key) || receiptsByAction.ContainsKey(actionKey))
                {
                    throw Corruption(record.JournalSequence, "The journal contains a duplicate mutation receipt for one operation action.");
                }
                if (intents.TryGetValue(key, out var intent))
                {
                    if (!string.Equals(intent.ActionId, record.MutationReceipt.ActionId, StringComparison.Ordinal)
                        || !string.Equals(intent.ActionSignature, record.MutationReceipt.ActionSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        throw Corruption(record.JournalSequence, "A mutation receipt does not match its intent action signature.");
                    }
                }
                else if (record.MutationReceipt.Outcome != MutationOutcome.AlreadySatisfied)
                {
                    throw Corruption(record.JournalSequence, "An applied or failed mutation receipt has no matching intent.");
                }
                receipts.Add(key, record.MutationReceipt);
                receiptsByAction.Add(actionKey, record.MutationReceipt);
                BindOperationPlan(record);
                return;
            }
            if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationVerification)
            {
                var key = ActionKey(record.OperationId, record.ActionId, record.ActionSignature);
                if (verifications.Contains(key))
                {
                    throw Corruption(record.JournalSequence, "The journal contains duplicate verification for one operation action signature.");
                }
                if (!receiptsByAction.TryGetValue(key, out var matchingReceipt)
                    || !string.Equals(matchingReceipt.ActionSignature, record.MutationVerification.ActionSignature, StringComparison.OrdinalIgnoreCase))
                {
                    throw Corruption(record.JournalSequence, "A mutation verification has no matching receipt action signature.");
                }
                verifications.Add(key);
                BindOperationPlan(record);
                return;
            }
            if (record.RecordKind == MigrationExecutionJournalRecordKind.ArtifactReference
                && !string.IsNullOrWhiteSpace(record.ActionSignature)
                && !receiptsByAction.ContainsKey(ActionKey(record.OperationId, record.ActionId, record.ActionSignature)))
            {
                throw Corruption(record.JournalSequence, "An action-scoped artifact reference has no matching receipt action signature.");
            }
            BindOperationPlan(record);
        }

        private void BindOperationPlan(MigrationExecutionJournalRecord record)
        {
            if (!operationPlans.ContainsKey(record.OperationId))
            {
                operationPlans.Add(record.OperationId, record.PlanDigest);
            }
        }

        private static string AttemptKey(Guid operationId, int sequence)
        {
            return operationId.ToString("N") + "/" + sequence;
        }

        private static string ActionKey(Guid operationId, string actionId, string signature)
        {
            return operationId.ToString("N") + "/" + actionId + "/" + (signature ?? "legacy");
        }

        private static InvalidDataException Corruption(long sequence, string message)
        {
            return new InvalidDataException("Migration execution journal corruption at record " + sequence + ": " + message);
        }
    }
}
