using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Ingredients.WebPartInstance
{
    internal sealed class WebPartInstanceMaturityContributor : IIngredientMaturityContributor
    {
        private readonly WebPartInstanceMaturityEvidence evidence;

        public WebPartInstanceMaturityContributor(WebPartInstanceMaturityEvidence evidence)
        {
            this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        public string ContributorId => WebPartInstanceEvidenceNormalizer.ContributorId;

        public string Lane => WebPartInstanceEvidenceNormalizer.Lane;

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var normalized = WebPartInstanceEvidenceNormalizer.Normalize(
                context,
                evidence.Source,
                evidence.TargetExpectation);
            var receipts = new List<IngredientMaturityGateReceipt>();
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                normalized.Node,
                null,
                CreateOwnerRegistry(normalized.SourcePredicateMatched),
                evidence.Source?.EvidenceReferences));

            var live = WebPartInstanceEvidenceNormalizer.ProjectLiveEvidence(
                evidence.Live,
                normalized,
                evidence.TargetExpectation);
            if (live != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(live));
            }
            if (evidence.Source != null)
            {
                var rawBytes = Encoding.UTF8.GetBytes(normalized.RawPayloadCanonicalJson);
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(
                    new IngredientContentIntegrityEvidence
                    {
                        Artifact = new ArtifactReference
                        {
                            Sha256 = evidence.Source.NormalizedRestPayloadSha256,
                            Length = rawBytes.LongLength,
                            MediaType = "application/json",
                            ContentEncoding = "utf-8",
                            OriginalName = "normalized-webpart-instance.json"
                        },
                        ContentBase64 = Convert.ToBase64String(rawBytes),
                        SemanticValueCanonicalJson = normalized.SemanticCanonicalJson,
                        SemanticDigest = normalized.SemanticSha256,
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
            bool sourcePredicateMatched)
        {
            return new PublishingPageIngredientPrimaryOwnerRegistry(
                new[]
                {
                    new PageIngredientPrimaryOwnerDescriptor(
                        "webpart.instance.persisted-classic.v1",
                        PageIngredientKind.WebPart,
                        WebPartInstanceEvidenceNormalizer.Subtype,
                        WebPartInstanceEvidenceNormalizer.SemanticRole,
                        WebPartInstanceEvidenceNormalizer.SourcePredicateId,
                        WebPartInstanceEvidenceNormalizer.Lane)
                },
                new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(StringComparer.Ordinal)
                {
                    [WebPartInstanceEvidenceNormalizer.SourcePredicateId] = value =>
                        sourcePredicateMatched && value.HasBoundSourceIdentity
                });
        }
    }
}
