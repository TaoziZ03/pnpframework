using PnP.Framework.Migration.Pages.Assessment.Maturity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.EmbedIframe
{
    internal sealed class EmbedIframeMaturityContributor : IIngredientMaturityContributor
    {
        private readonly EmbedIframeMaturityEvidence evidence;

        public EmbedIframeMaturityContributor(EmbedIframeMaturityEvidence evidence)
        {
            this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        public string ContributorId => EmbedIframeEvidenceNormalizer.ContributorId;

        public string Lane => EmbedIframeEvidenceNormalizer.Lane;

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var normalized = EmbedIframeEvidenceNormalizer.Normalize(context, evidence.Source);
            var receipts = new List<IngredientMaturityGateReceipt>();
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                normalized.Node,
                normalized.Snapshot,
                normalized.SourcePredicateMatched
                    ? PublishingPageIngredientPrimaryOwnerRegistry.Default
                    : CreateClosedOwnerRegistry(),
                evidence.Source?.EvidenceReferences));

            var live = EmbedIframeEvidenceNormalizer.ProjectLiveEvidence(evidence.Live, normalized);
            if (live != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(live));
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

        private static PublishingPageIngredientPrimaryOwnerRegistry CreateClosedOwnerRegistry()
        {
            return new PublishingPageIngredientPrimaryOwnerRegistry(
                new[]
                {
                    new PageIngredientPrimaryOwnerDescriptor(
                        "reference.embed.iframe.closed.v1",
                        Pages.Ingredients.PageIngredientKind.Reference,
                        EmbedIframeEvidenceNormalizer.Subtype,
                        EmbedIframeEvidenceNormalizer.SemanticRole,
                        EmbedIframeEvidenceNormalizer.SourcePredicateId,
                        EmbedIframeEvidenceNormalizer.Lane)
                },
                new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(StringComparer.Ordinal)
                {
                    [EmbedIframeEvidenceNormalizer.SourcePredicateId] = _ => false
                });
        }
    }
}
