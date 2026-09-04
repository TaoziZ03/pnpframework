namespace PnP.Framework.Migration.Execution
{
    /// <summary>
    /// Optional durable-journal extension. Existing journal implementations
    /// remain valid and simply omit verification and CAS-reference records.
    /// </summary>
    public interface IMigrationExecutionCheckpointJournal : IMigrationExecutionJournal
    {
        void WriteVerification(MigrationMutationVerificationReceipt verification);

        void WriteArtifactReference(MigrationExecutionArtifactReference artifact);
    }
}
