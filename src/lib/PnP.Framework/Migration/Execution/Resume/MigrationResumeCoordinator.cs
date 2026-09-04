using PnP.Framework.Migration.Execution.Journaling;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Execution.Resume
{
    /// <summary>
    /// Uses a validated local journal only as prior-attempt evidence. Every call
    /// performs a fresh target probe. It never invokes mutation and never treats
    /// a journal record as target truth.
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
            if (request?.Action == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            MigrationActionSignature.Validate(request.Action);
            if (!Enum.IsDefined(typeof(MigrationTargetOwnership), request.ExpectedOwnership))
            {
                throw new InvalidDataException("A valid expected target ownership is required for resume.");
            }
            if (freshProbe == null)
            {
                throw new ArgumentNullException(nameof(freshProbe));
            }

            var signature = request.Action.Signature;
            var prior = journal.Records.Where(record =>
                    (record.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent
                        || record.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt
                        || record.RecordKind == MigrationExecutionJournalRecordKind.MutationVerification)
                    && string.Equals(record.ActionId, request.Action.ActionId, StringComparison.Ordinal)
                    && string.Equals(record.ActionSignature, signature, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Records for the same action ID with older signatures are history,
            // not corruption and not authority for the current action node.
            var probe = freshProbe();
            if (probe == null)
            {
                throw new InvalidOperationException("The fresh target probe returned no result.");
            }
            if (probe.State == MigrationFreshProbeState.Unavailable)
            {
                return Decision(MigrationResumeDisposition.TargetProbeUnavailable, prior.Length > 0, signature, probe,
                    "Fresh target inspection is unavailable; neither the journal nor prior receipts authorize replay.");
            }
            if (probe.State == MigrationFreshProbeState.Drifted
                || probe.State == MigrationFreshProbeState.ForeignCollision)
            {
                return Decision(MigrationResumeDisposition.ReplanAndReapprove, prior.Length > 0, signature, probe,
                    "Fresh target state conflicts with the sealed action signature; no overwrite or implicit adoption is allowed.");
            }
            if (probe.State == MigrationFreshProbeState.Exact)
            {
                var ownershipMatches = probe.Ownership == request.ExpectedOwnership;
                var targetMatches = string.Equals(
                    probe.TargetIdentityDigest,
                    request.Action.TargetIdentityDigest,
                    StringComparison.OrdinalIgnoreCase);
                var semanticMatches = string.Equals(
                    probe.ObservedStateDigest,
                    request.Action.SemanticDigest,
                    StringComparison.OrdinalIgnoreCase);
                var provenanceMatches = request.ExpectedOwnership == MigrationTargetOwnership.External
                    || probe.ProvenanceMatched;
                if (!ownershipMatches || !targetMatches || !semanticMatches || !provenanceMatches)
                {
                    return Decision(MigrationResumeDisposition.ReplanAndReapprove, prior.Length > 0, signature, probe,
                        "Fresh target identity, ownership, provenance, or observed semantic state differs from the sealed action.");
                }
                if (prior.Length > 0)
                {
                    return Decision(MigrationResumeDisposition.AlreadySatisfied, true, signature, probe,
                        "Prior signed attempt evidence plus fresh target inspection prove the action is already satisfied.");
                }
                return Decision(MigrationResumeDisposition.Pending, false, signature, probe,
                    "The target is exact, but no prior record carries this action signature; normal admission must decide reuse.");
            }

            return Decision(MigrationResumeDisposition.Pending, prior.Length > 0, signature, probe,
                journal.HasInterruptedTail
                    ? "Interrupted segment evidence was not trusted. Fresh inspection found the target absent; normal admission remains required."
                    : "Fresh inspection found the target absent; normal admission remains required.");
        }

        private static MigrationResumeDecision Decision(
            MigrationResumeDisposition disposition,
            bool prior,
            string signature,
            MigrationFreshProbeResult probe,
            string diagnostic)
        {
            return new MigrationResumeDecision
            {
                Disposition = disposition,
                FreshProbePerformed = true,
                PriorSignedEvidenceFound = prior,
                ActionSignature = signature,
                Probe = probe,
                Diagnostic = diagnostic
            };
        }
    }
}
