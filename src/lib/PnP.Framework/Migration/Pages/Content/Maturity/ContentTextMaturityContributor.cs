using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Content.Maturity
{
    internal sealed class ContentTextMaturityContributor : IIngredientMaturityContributor
    {
        private readonly ContentTextMaturityEvidence evidence;

        public ContentTextMaturityContributor(ContentTextMaturityEvidence evidence)
        {
            this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        public string ContributorId => ContentTextEvidenceNormalizer.ContributorId;

        public string Lane => ContentTextEvidenceNormalizer.Lane;

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var normalized = ContentTextEvidenceNormalizer.Normalize(context, evidence.Source);
            var receipts = new List<IngredientMaturityGateReceipt>();
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                normalized.Node,
                null,
                CreateOwnerRegistry(normalized),
                evidence.Source?.EvidenceReferences));

            var live = ContentTextEvidenceNormalizer.ProjectLiveEvidence(evidence.Live, normalized);
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
                        SemanticDigest = normalized.SemanticProjectionSha256,
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

        private static PublishingPageIngredientPrimaryOwnerRegistry CreateOwnerRegistry(
            ContentTextNormalization normalized)
        {
            var node = normalized.Node;
            return new PublishingPageIngredientPrimaryOwnerRegistry(
                new[]
                {
                    new PageIngredientPrimaryOwnerDescriptor(
                        "content.text.body-field-value.v1",
                        PageIngredientKind.Content,
                        node.Subtype,
                        node.SemanticRole,
                        node.SourcePredicateId,
                        ContentTextEvidenceNormalizer.Lane)
                },
                new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(StringComparer.Ordinal)
                {
                    [node.SourcePredicateId] = value =>
                        normalized.SourcePredicateMatched && value.HasBoundSourceIdentity
                });
        }
    }
}
