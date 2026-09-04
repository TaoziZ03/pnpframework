using PnP.Framework.Migration.Packaging;
using System;

namespace PnP.Framework.Migration.Execution
{
    /// <summary>
    /// Stable execution boundary shared by every retry attempt for one reviewed
    /// migration plan. OperationId is deliberately excluded because it changes
    /// between attempts.
    /// </summary>
    public sealed class MigrationExecutionBoundary
    {
        public string SchemaVersion { get; set; } = "pnp-migration-execution-boundary/v1";

        public string SourceSnapshotDigest { get; set; }

        public string PlanDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public string TargetBoundary { get; set; }

        public string TargetBoundaryDigest { get; set; }

        public static MigrationExecutionBoundary Create(
            string sourceSnapshotDigest,
            string planDigest,
            string approvalDigest,
            string targetBoundary)
        {
            if (!IsSha256(planDigest))
            {
                throw new ArgumentException("A SHA-256 plan digest is required.", nameof(planDigest));
            }
            if (!string.IsNullOrWhiteSpace(sourceSnapshotDigest) && !IsSha256(sourceSnapshotDigest))
            {
                throw new ArgumentException("The optional source snapshot digest must be SHA-256.", nameof(sourceSnapshotDigest));
            }
            if (!string.IsNullOrWhiteSpace(approvalDigest) && !IsSha256(approvalDigest))
            {
                throw new ArgumentException("The optional approval digest must be SHA-256.", nameof(approvalDigest));
            }
            if (string.IsNullOrWhiteSpace(targetBoundary))
            {
                throw new ArgumentException("A target boundary is required.", nameof(targetBoundary));
            }

            return new MigrationExecutionBoundary
            {
                SourceSnapshotDigest = sourceSnapshotDigest,
                PlanDigest = planDigest,
                ApprovalDigest = approvalDigest,
                TargetBoundary = targetBoundary.Trim(),
                TargetBoundaryDigest = MigrationDigest.ComputeSha256(targetBoundary.Trim())
            };
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }
            foreach (var character in value)
            {
                if (!(character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
