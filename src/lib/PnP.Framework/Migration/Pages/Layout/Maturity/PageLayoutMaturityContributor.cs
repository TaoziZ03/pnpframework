using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.PageLayout
{
    internal sealed class PageLayoutMaturityContributor : IIngredientMaturityContributor
    {
        private readonly PageLayoutMaturityEvidence evidence;

        public PageLayoutMaturityContributor(PageLayoutMaturityEvidence evidence)
        {
            this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        public string ContributorId => PageLayoutWikiEvidenceNormalizer.ContributorId;

        public string Lane => PageLayoutWikiEvidenceNormalizer.Lane;

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var normalized = PageLayoutWikiEvidenceNormalizer.Normalize(context, evidence.WikiSource);
            var receipts = new List<IngredientMaturityGateReceipt>();
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                normalized.Node,
                null,
                CreateOwnerRegistry(normalized.SourcePredicateMatched),
                evidence.WikiSource?.EvidenceReferences));

            var liveInput = PageLayoutWikiEvidenceNormalizer.ProjectTargetReadback(
                context,
                evidence.WikiSource,
                evidence.Live,
                evidence.WikiTargetReadback);
            var live = PageLayoutWikiEvidenceNormalizer.ProjectLiveEvidence(liveInput, normalized);
            if (live != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(context, live));
            }

            if (evidence.WikiSource != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(
                    new IngredientContentIntegrityEvidence
                    {
                        Artifact = evidence.WikiSource.RawArtifact,
                        ContentBase64 = evidence.WikiSource.RawArtifactBase64,
                        SemanticValueCanonicalJson = normalized.SemanticCanonicalJson,
                        SemanticDigest = evidence.WikiSource.SemanticDigest,
                        EvidenceReferences = evidence.WikiSource.EvidenceReferences?.ToList()
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
                        "page.layout.wiki.v1",
                        PageIngredientKind.Layout,
                        PageLayoutWikiEvidenceNormalizer.Subtype,
                        PageLayoutWikiEvidenceNormalizer.SemanticRole,
                        PageLayoutWikiEvidenceNormalizer.SourcePredicateId,
                        PageLayoutWikiEvidenceNormalizer.Lane)
                },
                new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(StringComparer.Ordinal)
                {
                    [PageLayoutWikiEvidenceNormalizer.SourcePredicateId] = value =>
                        sourcePredicateMatched && value.HasBoundSourceIdentity
                });
        }
    }
}
