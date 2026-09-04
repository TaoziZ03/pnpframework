namespace PnP.Framework.Migration.Execution
{
    /// <summary>
    /// Optional extension implemented by durable journals. Existing callers and
    /// third-party journal implementations remain valid IMigrationExecutionJournal
    /// implementations and simply do not persist verification/artifact records.
    /// </summary>
    public interface IMigrationExecutionCheckpointJournal : IMigrationExecutionJournal
    {
        void WriteVerification(MigrationMutationVerificationReceipt verification);

        void WriteArtifact(MigrationExecutionArtifact artifact);
    }
}
