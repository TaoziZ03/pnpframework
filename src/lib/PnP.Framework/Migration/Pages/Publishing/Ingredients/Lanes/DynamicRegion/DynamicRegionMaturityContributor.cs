using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.DynamicRegion
{
    internal sealed class DynamicRegionMaturityContributor : IIngredientMaturityContributor
    {
        private readonly DynamicRegionMaturityEvidence evidence;

        public DynamicRegionMaturityContributor(DynamicRegionMaturityEvidence evidence)
        {
            this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        public string ContributorId => DynamicRegionEvidenceNormalizer.ContributorId;

        public string Lane => DynamicRegionEvidenceNormalizer.Lane;

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var normalized = DynamicRegionEvidenceNormalizer.Normalize(context, evidence.Source);
            var receipts = new List<IngredientMaturityGateReceipt>();
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                normalized.Node,
                null,
                CreateOwnerRegistry(normalized.SourcePredicateMatched),
                evidence.Source?.EvidenceReferences));

            var live = DynamicRegionEvidenceNormalizer.ProjectLiveEvidence(evidence.Live, normalized);
            if (live != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(live));
            }

            if (evidence.Source != null)
            {
                var rawBytes = Encoding.UTF8.GetBytes(normalized.SemanticCanonicalJson);
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(
                    new IngredientContentIntegrityEvidence
                    {
                        Artifact = new ArtifactReference
                        {
                            Sha256 = evidence.Source.RawArtifactSha256,
                            Length = evidence.Source.RawArtifactLength
                        },
                        ContentBase64 = Convert.ToBase64String(rawBytes),
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

        private static PublishingPageIngredientPrimaryOwnerRegistry CreateOwnerRegistry(bool sourcePredicateMatched)
        {
            return new PublishingPageIngredientPrimaryOwnerRegistry(
                new[]
                {
                    new PageIngredientPrimaryOwnerDescriptor(
                        "dynamic.region.result-script-webpart.v1",
                        PageIngredientKind.Runtime,
                        DynamicRegionEvidenceNormalizer.Subtype,
                        DynamicRegionEvidenceNormalizer.SemanticRole,
                        DynamicRegionEvidenceNormalizer.SourcePredicateId,
                        DynamicRegionEvidenceNormalizer.Lane)
                },
                new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(StringComparer.Ordinal)
                {
                    [DynamicRegionEvidenceNormalizer.SourcePredicateId] = value =>
                        sourcePredicateMatched && value.HasBoundSourceIdentity
                });
        }
    }
}
