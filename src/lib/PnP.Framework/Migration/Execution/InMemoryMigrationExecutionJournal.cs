using System.Collections.Generic;

namespace PnP.Framework.Migration.Execution
{
    public sealed class InMemoryMigrationExecutionJournal : IMigrationExecutionCheckpointJournal
    {
        public IList<MigrationExecutionStateReceipt> ExecutionStates { get; } = new List<MigrationExecutionStateReceipt>();

        public IList<MigrationMutationIntent> Intents { get; } = new List<MigrationMutationIntent>();

        public IList<MigrationMutationReceipt> Receipts { get; } = new List<MigrationMutationReceipt>();

        public IList<MigrationMutationVerificationReceipt> Verifications { get; } = new List<MigrationMutationVerificationReceipt>();

        public IList<MigrationExecutionArtifactReference> ArtifactReferences { get; } = new List<MigrationExecutionArtifactReference>();

        public void WriteExecutionState(MigrationExecutionStateReceipt state)
        {
            ExecutionStates.Add(state);
        }

        public void WriteIntent(MigrationMutationIntent intent)
        {
            Intents.Add(intent);
        }

        public void WriteReceipt(MigrationMutationReceipt receipt)
        {
            Receipts.Add(receipt);
        }

        public void WriteVerification(MigrationMutationVerificationReceipt verification)
        {
            Verifications.Add(verification);
        }

        public void WriteArtifactReference(MigrationExecutionArtifactReference artifact)
        {
            ArtifactReferences.Add(artifact);
        }
    }
}
