using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Ingredients.ResourceScript
{
    internal sealed class ResourceScriptMaturityContributor : IIngredientMaturityContributor
    {
        private readonly ResourceScriptMaturityEvidence evidence;

        public ResourceScriptMaturityContributor(ResourceScriptMaturityEvidence evidence)
        {
            this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        public string ContributorId => ResourceScriptEvidenceNormalizer.ContributorId;

        public string Lane => ResourceScriptEvidenceNormalizer.Lane;

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var normalized = ResourceScriptEvidenceNormalizer.Normalize(context, evidence);
            var receipts = new List<IngredientMaturityGateReceipt>();
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                normalized.Node,
                normalized.Snapshot,
                ResourceScriptIngredientCatalog.Create().PrimaryOwnerRegistry,
                evidence.EvidenceReferences));

            var live = ResourceScriptEvidenceNormalizer.ProjectLiveEvidence(evidence.Live, normalized);
            if (live != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(live));
            }
            if (evidence.Source?.Reference != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(
                    new IngredientContentIntegrityEvidence
                    {
                        Artifact = new ArtifactReference
                        {
                            Sha256 = evidence.Source.Reference.ContentSha256,
                            Length = evidence.Source.Reference.ContentLength,
                            MediaType = "application/javascript",
                            ContentEncoding = "utf-8"
                        },
                        ContentBase64 = evidence.ScriptContentBase64,
                        SemanticValueCanonicalJson = normalized.SemanticCanonicalJson,
                        SemanticDigest = normalized.SemanticDigest,
                        EvidenceReferences = (evidence.EvidenceReferences ?? Array.Empty<string>()).ToList()
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
    }
}
