using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Resume;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    /// <summary>
    /// Shared execution boundary for Publishing ingredient materializers. It
    /// writes the existing intent/apply/failure records through the signature
    /// overloads, then emits the existing fresh-verification receipt format.
    /// </summary>
    internal sealed class PublishingPageIngredientExecutionSession
    {
        private readonly MigrationExecutionRecorder recorder;
        private readonly IReadOnlyDictionary<string, PublishingPageIngredientExecutionBinding> bindings;

        public PublishingPageIngredientExecutionSession(
            PublishingPageMigrationPackage package,
            MigrationExecutionRecorder recorder)
        {
            this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            if (recorder.OperationId == Guid.Empty
                || package == null
                || !string.Equals(recorder.PlanDigest, package.PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Publishing ingredient execution requires one nonempty operation identity bound to the exact package plan digest.");
            }
            bindings = PublishingPageIngredientActionSignatureFactory.Create(package);
        }

        public PublishingPageIngredientExecutionBinding GetBinding(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) || !bindings.TryGetValue(actionId, out var binding))
            {
                throw new InvalidDataException(
                    "The requested Publishing ingredient action is not executable in the sealed package frontier.");
            }
            return binding;
        }

        public T Execute<T>(
            string actionId,
            string description,
            Func<T> mutation,
            Func<T, MigrationFreshProbeResult> freshProbe,
            Func<T, MutationOutcome> outcome = null,
            Func<T, string> message = null)
        {
            if (freshProbe == null)
            {
                throw new ArgumentNullException(nameof(freshProbe));
            }
            var binding = GetBinding(actionId);
            var result = recorder.Execute(
                binding.ActionSignature,
                description,
                mutation,
                outcome,
                message);
            RecordVerification(binding, freshProbe(result));
            return result;
        }

        public void RecordAlreadySatisfiedFromResume(
            string actionId,
            MigrationResumeDecision decision)
        {
            var binding = GetBinding(actionId);
            if (decision == null
                || decision.Disposition != MigrationResumeDisposition.AlreadySatisfied
                || !decision.FreshProbePerformed
                || !decision.PriorSealedEvidenceFound
                || !string.Equals(
                    decision.ActionSignature,
                    binding.ActionSignature.Signature,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Only an exact current-signature decision backed by a fresh target probe may satisfy a Publishing ingredient action.");
            }
            recorder.RecordAlreadySatisfied(
                binding.ActionSignature,
                decision.Diagnostic ?? "Fresh target inspection proved the sealed Publishing ingredient action already satisfied.");
            RecordVerification(binding, decision.Probe);
        }

        public static MigrationResumeDecision EvaluateResume(
            PublishingPageMigrationPackage package,
            string actionId,
            string journalPath,
            Func<MigrationFreshProbeResult> freshProbe)
        {
            var binding = GetPackageBinding(package, actionId);
            return MigrationResumeCoordinator.Evaluate(
                journalPath,
                Request(binding),
                freshProbe);
        }

        public static MigrationResumeDecision EvaluateResume(
            PublishingPageMigrationPackage package,
            string actionId,
            Stream journalStream,
            Func<MigrationFreshProbeResult> freshProbe)
        {
            var binding = GetPackageBinding(package, actionId);
            return MigrationResumeCoordinator.Evaluate(
                journalStream,
                Request(binding),
                freshProbe);
        }

        private static PublishingPageIngredientExecutionBinding GetPackageBinding(
            PublishingPageMigrationPackage package,
            string actionId)
        {
            var bindings = PublishingPageIngredientActionSignatureFactory.Create(package);
            if (string.IsNullOrWhiteSpace(actionId) || !bindings.TryGetValue(actionId, out var binding))
            {
                throw new InvalidDataException(
                    "The requested Publishing ingredient action is not executable in the sealed package frontier.");
            }
            return binding;
        }

        private static MigrationResumeRequest Request(PublishingPageIngredientExecutionBinding binding)
        {
            return new MigrationResumeRequest
            {
                Action = binding.ActionSignature,
                ExpectedOwnership = binding.ExpectedOwnership
            };
        }

        private void RecordVerification(
            PublishingPageIngredientExecutionBinding binding,
            MigrationFreshProbeResult probe)
        {
            if (probe == null)
            {
                throw new InvalidOperationException("The Publishing ingredient fresh target probe returned no result.");
            }
            var signature = binding.ActionSignature;
            var exact = probe.State == MigrationFreshProbeState.Exact
                && probe.Ownership == binding.ExpectedOwnership
                && string.Equals(probe.ObservedStateDigest, signature.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(probe.TargetIdentityDigest, signature.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase)
                && (binding.ExpectedOwnership == MigrationTargetOwnership.External || probe.ProvenanceMatched);
            var verification = new MigrationMutationVerificationReceipt
            {
                OperationId = recorder.OperationId,
                PlanDigest = recorder.PlanDigest,
                ActionId = signature.ActionId,
                ActionSignature = signature.Signature,
                VerifiedAtUtc = DateTimeOffset.UtcNow,
                FreshReadbackPassed = exact,
                ObservedStateDigest = ValidDigestOrProbeDigest(probe.ObservedStateDigest, probe, "state"),
                Ownership = Enum.IsDefined(typeof(MigrationTargetOwnership), probe.Ownership)
                    ? probe.Ownership
                    : binding.ExpectedOwnership,
                TargetIdentityDigest = ValidDigestOrProbeDigest(probe.TargetIdentityDigest, probe, "target"),
                ProvenanceMatched = probe.ProvenanceMatched,
                Message = probe.Diagnostic ?? (exact
                    ? "Fresh target inspection matched the sealed Publishing ingredient action."
                    : "Fresh target inspection did not match the sealed Publishing ingredient action.")
            };
            recorder.RecordVerification(verification);
            if (!exact)
            {
                throw new InvalidOperationException(verification.Message);
            }
        }

        private static string ValidDigestOrProbeDigest(
            string value,
            MigrationFreshProbeResult probe,
            string subject)
        {
            if (MigrationActionSignature.IsSha256(value))
            {
                return value.ToLowerInvariant();
            }
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-publishing-ingredient-probe-gap/v1",
                subject,
                probe.State,
                probe.Ownership,
                probe.ProvenanceMatched,
                probe.Diagnostic
            }));
        }
    }
}
