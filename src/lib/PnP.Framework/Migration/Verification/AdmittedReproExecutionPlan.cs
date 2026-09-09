using PnP.Framework.Migration.Packaging;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Verification
{
    /// <summary>
    /// Immutable execution envelope admitted immediately before target mutation.
    /// Its digest binds the sealed page plan, current source version, target identity,
    /// and every operation that may affect or verify the run.
    /// </summary>
    public sealed class AdmittedReproExecutionPlan
    {
        public string SchemaVersion { get; set; } = "pnp-admitted-page-repro-plan/v1";

        public string PlanDigest { get; set; }

        public string TargetIdentity { get; set; }

        public CurrentSourceVersionIdentity SourceVersion { get; set; }

        public ReproOperationIds Operations { get; set; }
    }

    public sealed class CurrentSourceVersionIdentity
    {
        public string IdentityDigestSha256 { get; set; }

        public string VersionDigestSha256 { get; set; }

        public string ETag { get; set; }

        public DateTimeOffset? LastModifiedUtc { get; set; }

        public string VersionLabel { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }
    }

    public sealed class ReproOperationIds
    {
        public Guid MutationOperationId { get; set; }

        public Guid ReadbackOperationId { get; set; }

        public Guid RuntimeOperationId { get; set; }

        public Guid CleanupOperationId { get; set; }
    }

    public static class AdmittedReproExecutionPlanValidator
    {
        public const string SchemaVersion = "pnp-admitted-page-repro-plan/v1";

        public static string ValidateAndComputeDigest(
            AdmittedReproExecutionPlan admittedPlan,
            string expectedPlanDigest,
            string expectedTargetIdentity)
        {
            if (admittedPlan == null)
            {
                throw new InvalidDataException("A target-admitted repro plan is required.");
            }

            Require(string.Equals(admittedPlan.SchemaVersion, SchemaVersion, StringComparison.Ordinal),
                "The target-admitted repro plan schema is unsupported.");
            ValidateDigest(admittedPlan.PlanDigest, "admitted page plan digest");
            Require(string.Equals(admittedPlan.PlanDigest, expectedPlanDigest, StringComparison.OrdinalIgnoreCase),
                "The target-admitted repro plan is foreign or stale.");
            Require(!string.IsNullOrWhiteSpace(admittedPlan.TargetIdentity)
                && string.Equals(admittedPlan.TargetIdentity, expectedTargetIdentity, StringComparison.Ordinal),
                "The target-admitted repro plan target identity is foreign or stale.");

            ValidateSourceVersion(admittedPlan.SourceVersion);
            ValidateOperations(admittedPlan.Operations);
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(admittedPlan));
        }

        public static void ValidateSourceVersion(CurrentSourceVersionIdentity sourceVersion)
        {
            Require(sourceVersion != null, "A current source-version identity is required.");
            ValidateDigest(sourceVersion.IdentityDigestSha256, "source-version identity digest");
            ValidateDigest(sourceVersion.VersionDigestSha256, "source-version digest");
            Require(sourceVersion.ObservedAtUtc != default,
                "The current source-version observation time is required.");
            Require(!string.IsNullOrWhiteSpace(sourceVersion.ETag)
                || sourceVersion.LastModifiedUtc.HasValue
                || !string.IsNullOrWhiteSpace(sourceVersion.VersionLabel),
                "The current source-version identity requires an ETag, Last-Modified value, or version label.");
        }

        public static void ValidateOperations(ReproOperationIds operations)
        {
            Require(operations != null, "Admitted repro operation IDs are required.");
            var values = new[]
            {
                operations.MutationOperationId,
                operations.ReadbackOperationId,
                operations.RuntimeOperationId,
                operations.CleanupOperationId
            };
            Require(values.All(value => value != Guid.Empty),
                "Every admitted mutation/readback/runtime/cleanup operation ID is required.");
            Require(values.Distinct().Count() == values.Length,
                "Admitted repro operation IDs must be unique.");
        }

        public static bool SameSourceVersion(
            CurrentSourceVersionIdentity left,
            CurrentSourceVersionIdentity right)
        {
            return left != null
                && right != null
                && string.Equals(left.IdentityDigestSha256, right.IdentityDigestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.VersionDigestSha256, right.VersionDigestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.ETag, right.ETag, StringComparison.Ordinal)
                && Nullable.Equals(left.LastModifiedUtc, right.LastModifiedUtc)
                && string.Equals(left.VersionLabel, right.VersionLabel, StringComparison.Ordinal)
                && left.ObservedAtUtc.Equals(right.ObservedAtUtc);
        }

        public static bool SameOperations(ReproOperationIds left, ReproOperationIds right)
        {
            return left != null
                && right != null
                && left.MutationOperationId == right.MutationOperationId
                && left.ReadbackOperationId == right.ReadbackOperationId
                && left.RuntimeOperationId == right.RuntimeOperationId
                && left.CleanupOperationId == right.CleanupOperationId;
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex),
                "The " + name + " must be a SHA-256 digest.");
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
