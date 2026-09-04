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

        public void Add(MigrationExecutionJournalRecord record)
        {
            if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent)
            {
                var key = AttemptKey(record.OperationId, record.MutationIntent.Sequence);
                if (intents.ContainsKey(key))
                {
                    throw Corruption(record.JournalSequence, "The journal contains a duplicate mutation intent sequence for one operation.");
                }
                intents.Add(key, record.MutationIntent);
                return;
            }
            if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt)
            {
                var key = AttemptKey(record.OperationId, record.MutationReceipt.Sequence);
                var actionKey = ActionKey(record.OperationId, record.ActionId);
                if (receipts.ContainsKey(key))
                {
                    throw Corruption(record.JournalSequence, "The journal contains a duplicate mutation receipt sequence for one operation.");
                }
                if (receiptsByAction.ContainsKey(actionKey))
                {
                    throw Corruption(record.JournalSequence, "The journal contains duplicate mutation receipts for one operation action.");
                }
                if (intents.TryGetValue(key, out var intent))
                {
                    if (!string.Equals(intent.ActionId, record.MutationReceipt.ActionId, StringComparison.Ordinal)
                        || !string.Equals(intent.IdempotencyKey, record.MutationReceipt.IdempotencyKey, StringComparison.OrdinalIgnoreCase))
                    {
                        throw Corruption(record.JournalSequence, "A mutation receipt does not match its intent action or idempotency identity.");
                    }
                }
                else if (record.MutationReceipt.Outcome != MutationOutcome.AlreadySatisfied)
                {
                    throw Corruption(record.JournalSequence, "An applied or failed mutation receipt has no matching intent.");
                }
                receipts.Add(key, record.MutationReceipt);
                receiptsByAction.Add(actionKey, record.MutationReceipt);
                return;
            }
            if (record.RecordKind == MigrationExecutionJournalRecordKind.MutationVerification)
            {
                var key = ActionKey(record.OperationId, record.ActionId);
                if (verifications.Contains(key))
                {
                    throw Corruption(record.JournalSequence, "The journal contains duplicate verification for one operation action.");
                }
                if (!receiptsByAction.TryGetValue(key, out var matchingReceipt)
                    || !string.Equals(matchingReceipt.IdempotencyKey, record.MutationVerification.IdempotencyKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw Corruption(record.JournalSequence, "A mutation verification has no matching receipt identity.");
                }
                verifications.Add(key);
            }
        }

        private static string AttemptKey(Guid operationId, int sequence)
        {
            return operationId.ToString("N") + "/" + sequence;
        }

        private static string ActionKey(Guid operationId, string actionId)
        {
            return operationId.ToString("N") + "/" + actionId;
        }

        private static InvalidDataException Corruption(long sequence, string message)
        {
            return new InvalidDataException(
                "Migration execution journal corruption at record " + sequence + ": " + message);
        }
    }
}
