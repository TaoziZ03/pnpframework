using System;

namespace PnP.Framework.Migration.Execution.Resume
{
    public enum MigrationFreshProbeState
    {
        Absent = 1,
        Exact = 2,
        Drifted = 3,
        ForeignCollision = 4,
        Unavailable = 5
    }

    public enum MigrationResumeDisposition
    {
        Pending = 1,
        AlreadySatisfied = 2,
        ReplanAndReapprove = 3,
        TargetProbeUnavailable = 4
    }

    public sealed class MigrationResumeRequest
    {
        public MigrationExecutionBoundary Boundary { get; set; }

        public MigrationMutationIdentity Mutation { get; set; }

        public string ExpectedOwnership { get; set; }
    }

    public sealed class MigrationFreshProbeResult
    {
        public MigrationFreshProbeState State { get; set; }

        public bool ProvenanceMatched { get; set; }

        public string Ownership { get; set; }

        public string CurrentStateDigest { get; set; }

        public string TargetIdentity { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class MigrationResumeDecision
    {
        public MigrationResumeDisposition Disposition { get; set; }

        public bool FreshProbePerformed { get; set; }

        public string IdempotencyKey { get; set; }

        public string Diagnostic { get; set; }

        public MigrationFreshProbeResult Probe { get; set; }
    }
}
