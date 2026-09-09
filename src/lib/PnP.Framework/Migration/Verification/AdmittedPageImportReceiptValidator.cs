using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Verification
{
    /// <summary>
    /// Family-neutral admission checks for native page import receipts.
    /// </summary>
    public static class AdmittedPageImportReceiptValidator
    {
        public static void ValidateSuccessfulExecution(
            IAdmittedPageImportReceipt receipt,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            ValidateExecutionEvidence(receipt, admittedPlan, admittedPlanDigestSha256);

            Require(receipt.ExecutionStatus == MigrationExecutionStatus.Succeeded
                && !receipt.PartialExecution,
                "A partial or incomplete import receipt cannot enter admitted compare.");
            Require(receipt.Steps.All(value => value.Outcome != MutationOutcome.Failed),
                "A successful import receipt cannot contain a failed native execution step.");
            Require(receipt.StorageVerificationStatus != StorageVerificationStatus.Passed
                || receipt.FreshReadbackPassed,
                "Storage cannot pass without a successful fresh readback.");
        }

        /// <summary>
        /// Validates the immutable lineage and native step ledger without asserting
        /// successful Compare admission. Versioned terminal contracts use this for
        /// mutation-started failed or partial executions that must remain evidence,
        /// while <see cref="ValidateSuccessfulExecution"/> remains the success gate.
        /// </summary>
        internal static void ValidateExecutionEvidence(
            IAdmittedPageImportReceipt receipt,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            if (admittedPlan == null)
            {
                throw new ArgumentNullException(nameof(admittedPlan));
            }
            if (string.IsNullOrWhiteSpace(admittedPlanDigestSha256))
            {
                throw new InvalidDataException("The admitted import receipt digest is required.");
            }

            Require(receipt.OperationId != Guid.Empty
                && receipt.OperationId == admittedPlan.Operations.MutationOperationId,
                "The import receipt mutation operation ID is missing or foreign.");
            Require(string.Equals(
                    receipt.AdmittedPlanDigestSha256,
                    admittedPlanDigestSha256,
                    StringComparison.OrdinalIgnoreCase),
                "The import receipt is not bound to the admitted plan digest.");
            Require(string.Equals(
                    receipt.ApprovedPlanDigest,
                    admittedPlan.PlanDigest,
                    StringComparison.OrdinalIgnoreCase),
                "The import receipt is not bound to the approved plan digest.");
            Require(AdmittedReproExecutionPlanValidator.SameOperations(
                    receipt.Operations,
                    admittedPlan.Operations),
                "The import receipt operation set is missing, foreign, or stale.");
            Require(AdmittedReproExecutionPlanValidator.SameSourceVersion(
                    receipt.SourceVersion,
                    admittedPlan.SourceVersion),
                "The import receipt source-version binding is missing, foreign, or stale.");
            Require(receipt.StartedAtUtc != default
                && receipt.CompletedAtUtc != default
                && receipt.StartedAtUtc <= receipt.CompletedAtUtc,
                "The import receipt execution timeline is missing or invalid.");
            Require(receipt.MutationStarted,
                "A successful admitted import receipt must come from the native execution path.");

            var steps = receipt.Steps ?? new List<MigrationMutationReceipt>();
            Require(steps.Count > 0,
                "A successful admitted import receipt requires native execution steps.");
            Require(steps.All(value => value != null),
                "The import receipt contains a missing execution step.");
            Require(steps.Select(value => value.Sequence).Distinct().Count() == steps.Count,
                "The import receipt contains duplicate execution-step sequence numbers.");
            Require(steps.Select(value => value.ActionId).Distinct(StringComparer.Ordinal).Count() == steps.Count,
                "The import receipt contains duplicate execution-step action IDs.");

            var ordered = steps.OrderBy(value => value.Sequence).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                var step = ordered[index];
                Require(step.Sequence == index,
                    "The import receipt execution-step sequence is partial or orphaned.");
                Require(step.OperationId == receipt.OperationId,
                    "The import receipt contains a step from a foreign operation.");
                Require(string.Equals(
                        step.PlanDigest,
                        receipt.ApprovedPlanDigest,
                        StringComparison.OrdinalIgnoreCase),
                    "The import receipt contains a step from a foreign or stale plan.");
                Require(!string.IsNullOrWhiteSpace(step.ActionId),
                    "The import receipt contains a step without an action ID.");
                Require(step.CompletedAtUtc != default
                    && step.CompletedAtUtc >= receipt.StartedAtUtc
                    && step.CompletedAtUtc <= receipt.CompletedAtUtc,
                    "The import receipt contains a step outside its execution timeline.");
            }
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
