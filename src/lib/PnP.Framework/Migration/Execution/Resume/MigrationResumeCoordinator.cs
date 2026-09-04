using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Packaging;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Execution.Resume
{
    /// <summary>
    /// Interprets a validated journal only as evidence of prior attempts. It
    /// always requires a fresh target probe before treating a prior action as
    /// already satisfied and never authorizes a mutation.
    /// </summary>
    public static class MigrationResumeCoordinator
    {
        public static MigrationResumeDecision Evaluate(
            MigrationExecutionJournalReadResult journal,
            MigrationResumeRequest request,
            Func<MigrationFreshProbeResult> freshProbe)
        {
            if (journal == null)
            {
                throw new ArgumentNullException(nameof(journal));
            }
            ValidateRequest(request);
            var idempotencyKey = MigrationMutationIdentity.ComputeIdempotencyKey(
                request.Boundary,
                request.Mutation);
            if (!string.Equals(request.Mutation.IdempotencyKey, idempotencyKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The resume mutation idempotency key is absent or invalid.");
            }

            var sameActionRecords = journal.Records.Where(record =>
                (record.MutationIntent != null
                    && string.Equals(record.MutationIntent.IngredientId, request.Mutation.IngredientId, StringComparison.Ordinal)
                    && string.Equals(record.MutationIntent.ActionId, request.Mutation.ActionId, StringComparison.Ordinal))
                || (record.MutationReceipt != null
                    && string.Equals(record.MutationReceipt.IngredientId, request.Mutation.IngredientId, StringComparison.Ordinal)
                    && string.Equals(record.MutationReceipt.ActionId, request.Mutation.ActionId, StringComparison.Ordinal))
                || (record.MutationVerification != null
                    && string.Equals(record.MutationVerification.IngredientId, request.Mutation.IngredientId, StringComparison.Ordinal)
                    && string.Equals(record.MutationVerification.ActionId, request.Mutation.ActionId, StringComparison.Ordinal)))
                .ToArray();
            foreach (var record in sameActionRecords)
            {
                var observed = record.MutationIntent?.IdempotencyKey
                    ?? record.MutationReceipt?.IdempotencyKey
                    ?? record.MutationVerification?.IdempotencyKey;
                if (!string.Equals(observed, idempotencyKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The journal contains a stale snapshot, plan, approval, disposition, semantic mapping, or target boundary for ingredient '"
                        + request.Mutation.IngredientId + "'. Replan and reapprove before continuing.");
                }
            }

            var matching = sameActionRecords.Where(record =>
                string.Equals(
                    record.MutationIntent?.IdempotencyKey
                        ?? record.MutationReceipt?.IdempotencyKey
                        ?? record.MutationVerification?.IdempotencyKey,
                    idempotencyKey,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matching.Length == 0 && !journal.HasInterruptedTail)
            {
                return new MigrationResumeDecision
                {
                    Disposition = MigrationResumeDisposition.Pending,
                    FreshProbePerformed = false,
                    IdempotencyKey = idempotencyKey,
                    Diagnostic = "No prior intent exists for this stable action identity. Normal admission is still required before mutation."
                };
            }
            if (freshProbe == null)
            {
                throw new ArgumentNullException(nameof(freshProbe), "A fresh target probe is required for every resumed action.");
            }

            var probe = freshProbe();
            if (probe == null)
            {
                throw new InvalidOperationException("The fresh target probe returned no result.");
            }
            if (probe.State == MigrationFreshProbeState.Unavailable)
            {
                return Decision(
                    MigrationResumeDisposition.TargetProbeUnavailable,
                    idempotencyKey,
                    probe,
                    "The journal was not trusted as target truth because fresh inspection is unavailable.");
            }
            if (probe.State == MigrationFreshProbeState.Drifted
                || probe.State == MigrationFreshProbeState.ForeignCollision)
            {
                return Decision(
                    MigrationResumeDisposition.ReplanAndReapprove,
                    idempotencyKey,
                    probe,
                    "Fresh target state conflicts with the sealed action identity; no overwrite or implicit adoption is allowed.");
            }
            if (probe.State == MigrationFreshProbeState.Exact)
            {
                var ownershipMatches = string.Equals(
                    request.ExpectedOwnership,
                    probe.Ownership,
                    StringComparison.Ordinal);
                var semanticMatches = string.Equals(
                    request.Mutation.SemanticDigest,
                    probe.CurrentStateDigest,
                    StringComparison.OrdinalIgnoreCase);
                var externalExpected = string.Equals(request.ExpectedOwnership, "External", StringComparison.Ordinal);
                if (!ownershipMatches
                    || !semanticMatches
                    || !externalExpected && !probe.ProvenanceMatched)
                {
                    return Decision(
                        MigrationResumeDisposition.ReplanAndReapprove,
                        idempotencyKey,
                        probe,
                        "Fresh target identity, ownership, provenance, or semantic digest differs from the sealed action.");
                }
                return Decision(
                    MigrationResumeDisposition.AlreadySatisfied,
                    idempotencyKey,
                    probe,
                    "Fresh target inspection proves the interrupted or completed prior action is already satisfied.");
            }

            return Decision(
                MigrationResumeDisposition.Pending,
                idempotencyKey,
                probe,
                journal.HasInterruptedTail
                    ? "The interrupted journal tail was not trusted. Fresh target inspection found no matching object; normal admission and approval remain required."
                    : "Fresh target inspection found no matching object. The journal does not authorize replay; normal admission and approval remain required.");
        }

        private static MigrationResumeDecision Decision(
            MigrationResumeDisposition disposition,
            string idempotencyKey,
            MigrationFreshProbeResult probe,
            string diagnostic)
        {
            return new MigrationResumeDecision
            {
                Disposition = disposition,
                FreshProbePerformed = true,
                IdempotencyKey = idempotencyKey,
                Probe = probe,
                Diagnostic = diagnostic
            };
        }

        private static void ValidateRequest(MigrationResumeRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.Boundary == null
                || request.Mutation == null
                || string.IsNullOrWhiteSpace(request.Boundary.PlanDigest)
                || string.IsNullOrWhiteSpace(request.Boundary.TargetBoundary)
                || string.IsNullOrWhiteSpace(request.Boundary.TargetBoundaryDigest)
                || string.IsNullOrWhiteSpace(request.Mutation.IngredientId)
                || string.IsNullOrWhiteSpace(request.Mutation.ActionId)
                || string.IsNullOrWhiteSpace(request.Mutation.SelectedDisposition)
                || string.IsNullOrWhiteSpace(request.Mutation.SemanticDigest)
                || string.IsNullOrWhiteSpace(request.ExpectedOwnership))
            {
                throw new InvalidDataException("A complete stable execution boundary, mutation identity, and expected ownership are required for resume.");
            }
            if (!string.Equals(
                request.Boundary.TargetBoundaryDigest,
                MigrationDigest.ComputeSha256(request.Boundary.TargetBoundary.Trim()),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The resume target-boundary digest is invalid.");
            }
        }
    }
}
