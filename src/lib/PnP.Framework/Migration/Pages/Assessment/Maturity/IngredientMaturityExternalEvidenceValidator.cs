using PnP.Framework.Migration.Pages.Assessment.Maturity.External;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal static partial class IngredientMaturityEvidenceValidator
    {
        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM3(
            IngredientMaturityEvaluationContext context, IngredientPlanEvidence evidence)
        {
            if (evidence?.External == null) return ValidateM3(evidence);
            var validated = new Lazy<ValidatedExternalIngredientPlan>(() =>
            {
                Require(evidence.Plan == null, "Typed Publishing and external plans are a strict one-of.");
                var plan = Batch1SealedPlanEvidenceAdapter.Validate(context, evidence.External);
                Require(evidence.ExpectedPlanDigest == plan.Binding.PlanDigest
                    && evidence.ExpectedSourceSnapshotDigest == plan.Binding.Source.SourceSnapshotDigest
                    && evidence.IngredientId == plan.Binding.Identity.IngredientId,
                    "External plan evidence differs from the independent context and admitted annex.");
                return plan;
            });
            var references = ExternalReferences(evidence.External);
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.SnapshotPlanBinding, references, () => { _ = validated.Value; }),
                Validate(IngredientMaturityGateCatalog.ActionDisposition, references, () => { _ = validated.Value; }),
                Validate(IngredientMaturityGateCatalog.DependencyPolicy, references, () => { _ = validated.Value; })
            };
        }

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM4(
            IngredientMaturityEvaluationContext context, IngredientOperationalEvidence evidence)
        {
            if (evidence?.External == null) return ValidateM4(evidence);
            var validated = new Lazy<SharedTargetLifecycleEvidenceAdapter>(() =>
            {
                Require(evidence.Plan == null && evidence.ImportReceipt == null
                    && evidence.JournalState == null && evidence.VerificationReceipt == null
                    && evidence.ActionSignature == null && evidence.RuntimeReceipt == null,
                    "Typed Publishing and original external lifecycle evidence are a strict one-of.");
                var result = SharedTargetLifecycleEvidenceAdapter.Read(context, evidence.External);
                Require(evidence.AdmittedPlanDigest == result.Binding.PlanDigest
                    && evidence.IngredientId == result.Binding.Identity.IngredientId,
                    "Operational evidence differs from the independently admitted ingredient and plan.");
                // Legacy AdmissionPassed/CleanupPassed/RuntimeRequired/RetryPassed
                // booleans have no authority on this path. Original artifacts do.
                return result;
            });
            var references = new List<string>(ExternalReferences(evidence.External.PlanEvidence));
            if (evidence.External.ReceiptSetArtifact != null)
                references.Add("sha256:" + evidence.External.ReceiptSetArtifact.Sha256);
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.AdmittedExactPlan, references, () => validated.Value.ValidateAdmission()),
                Validate(IngredientMaturityGateCatalog.OperationActionBinding, references, () => validated.Value.ValidateOperations()),
                Validate(IngredientMaturityGateCatalog.MutationJournalVerification, references, () => validated.Value.ValidateOperations()),
                Validate(IngredientMaturityGateCatalog.OwnershipProvenance, references, () => validated.Value.ValidateOwnership()),
                Validate(IngredientMaturityGateCatalog.FreshStorage, references, () => validated.Value.ValidateFreshStorage()),
                Validate(IngredientMaturityGateCatalog.RuntimeCleanupRetry, references, () => validated.Value.ValidateRuntimeCleanup())
            };
        }

        private static IEnumerable<string> ExternalReferences(IngredientExternalPlanEvidence evidence)
        {
            if (evidence?.PlanArtifact != null) yield return "sha256:" + evidence.PlanArtifact.Sha256;
            if (evidence?.BindingArtifact != null) yield return "sha256:" + evidence.BindingArtifact.Sha256;
        }
    }
}
