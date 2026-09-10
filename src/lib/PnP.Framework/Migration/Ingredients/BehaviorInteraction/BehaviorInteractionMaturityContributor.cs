using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.BehaviorInteraction
{
    internal sealed class BehaviorInteractionMaturityContributor : IIngredientMaturityContributor
    {
        private readonly BehaviorInteractionMaturityEvidence evidence;

        public BehaviorInteractionMaturityContributor(BehaviorInteractionMaturityEvidence evidence)
        {
            this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        public string ContributorId => BehaviorInteractionSearchSubmitEvidenceNormalizer.ContributorId;

        public string Lane => BehaviorInteractionSearchSubmitEvidenceNormalizer.Lane;

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var normalized = BehaviorInteractionSearchSubmitEvidenceNormalizer.Normalize(context, evidence.Source);
            var receipts = IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                null,
                null,
                null,
                evidence.Source?.EvidenceReferences).ToList();
            FailClosed(receipts, IngredientMaturityGateCatalog.CanonicalIdentity,
                normalized.RuntimeContractValid && normalized.SourcePredicateMatched && normalized.IdentityBindingMatched,
                normalized.FailureReason ?? "The runtime assertion identity or safe search-submit source predicate does not match.");
            FailClosed(receipts, IngredientMaturityGateCatalog.SourceBinding,
                normalized.SourceBindingMatched,
                "The runtime assertion source identity, version, or evidence digest does not match the evaluation context.");

            var live = BehaviorInteractionSearchSubmitEvidenceNormalizer.ProjectLiveEvidence(evidence.Live, normalized);
            if (live != null)
            {
                var liveReceipts = IngredientMaturityEvidenceValidator.ValidateM1(live).ToList();
                FailClosed(
                    liveReceipts,
                    IngredientMaturityGateCatalog.CupCollectFreshReadback,
                    normalized.TargetRuntimePreconditionPassed,
                    normalized.TargetRuntimePreconditionReasonCode + ": " + normalized.TargetRuntimePreconditionFailure);
                receipts.AddRange(liveReceipts);
            }
            if (evidence.Source != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(
                    new IngredientContentIntegrityEvidence
                    {
                        Artifact = evidence.Source.RawArtifact,
                        ContentBase64 = evidence.Source.RawArtifactBase64,
                        SemanticValueCanonicalJson = normalized.SemanticCanonicalJson,
                        SemanticDigest = evidence.Source.SemanticDigest,
                        EvidenceReferences = evidence.Source.EvidenceReferences?.ToList()
                            ?? new List<string>()
                    }));
            }
            if (evidence.Plan != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM3(evidence.Plan));
            }
            if (evidence.Operational != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM4(evidence.Operational));
            }
            if (evidence.Productization != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM5(
                    evidence.Productization,
                    context?.Producer,
                    evidence.Plan?.ExpectedPlanDigest));
            }

            return new IngredientMaturityContribution
            {
                ContributorId = ContributorId,
                ClaimId = context?.Identity?.ClaimId,
                Lane = Lane,
                IngredientId = context?.Identity?.IngredientId,
                GateReceipts = receipts
            };
        }

        private static void FailClosed(
            IEnumerable<IngredientMaturityGateReceipt> receipts,
            string gateId,
            bool passed,
            string reason)
        {
            if (passed)
            {
                return;
            }
            var receipt = receipts.Single(value => string.Equals(value.GateId, gateId, StringComparison.Ordinal));
            receipt.Passed = false;
            receipt.FailureReason = reason;
        }
    }
}
