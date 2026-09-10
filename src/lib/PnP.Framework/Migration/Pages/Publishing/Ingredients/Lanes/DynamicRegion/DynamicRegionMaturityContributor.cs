using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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
            var receipts = new List<IngredientMaturityGateReceipt>();
            DynamicRegionNormalizedEvidence normalized;
            try
            {
                normalized = DynamicRegionEvidenceNormalizer.Normalize(context, evidence.Source);
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is InvalidDataException
                || exception is InvalidOperationException
                || exception is JsonException)
            {
                receipts.AddRange(FailedLevel(
                    IngredientMaturityLevel.M0,
                    evidence.Source?.EvidenceReferences,
                    "The dynamic region instance is unavailable or malformed: " + exception.Message));
                return CreateContribution(context, receipts);
            }
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(
                context,
                normalized.Node,
                null,
                CreateOwnerRegistry(normalized.SourcePredicateMatched),
                evidence.Source?.EvidenceReferences));

            var live = DynamicRegionEvidenceNormalizer.ProjectLiveEvidence(
                evidence.Live,
                normalized,
                context,
                evidence.Source,
                evidence.Target);
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
                if (DynamicRegionEvidenceNormalizer.IsPlanBound(
                    context,
                    normalized,
                    evidence.Source,
                    evidence.Target,
                    evidence.Plan,
                    out var planFailure))
                {
                    receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM3(evidence.Plan));
                }
                else
                {
                    receipts.AddRange(FailedLevel(
                        IngredientMaturityLevel.M3,
                        References(
                            evidence.Plan.SnapshotPlanEvidenceReferences,
                            evidence.Plan.ActionEvidenceReferences,
                            evidence.Plan.DependencyPolicyEvidenceReferences),
                        planFailure));
                }
            }
            if (evidence.Operational != null)
            {
                if (DynamicRegionEvidenceNormalizer.IsOperationalBound(
                    context,
                    normalized,
                    evidence.Source,
                    evidence.Target,
                    evidence.Plan,
                    evidence.Operational,
                    out var operationFailure))
                {
                    receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM4(evidence.Operational));
                }
                else
                {
                    receipts.AddRange(FailedLevel(
                        IngredientMaturityLevel.M4,
                        References(
                            evidence.Operational.PlanAdmissionEvidenceReferences,
                            evidence.Operational.ActionEvidenceReferences,
                            evidence.Operational.ReceiptEvidenceReferences,
                            evidence.Operational.FreshReadbackEvidenceReferences,
                            evidence.Operational.RuntimeCleanupRetryEvidenceReferences),
                        operationFailure));
                }
            }
            if (evidence.Productization != null)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM5(
                    evidence.Productization,
                    context?.Producer,
                    evidence.Plan?.ExpectedPlanDigest));
            }

            return CreateContribution(context, receipts);
        }

        private IngredientMaturityContribution CreateContribution(
            IngredientMaturityEvaluationContext context,
            IList<IngredientMaturityGateReceipt> receipts)
        {
            return new IngredientMaturityContribution
            {
                ContributorId = ContributorId,
                ClaimId = context?.Identity?.ClaimId,
                Lane = Lane,
                IngredientId = context?.Identity?.IngredientId,
                GateReceipts = receipts
            };
        }

        private static IEnumerable<IngredientMaturityGateReceipt> FailedLevel(
            IngredientMaturityLevel level,
            IEnumerable<string> evidenceReferences,
            string failureReason)
        {
            var references = (evidenceReferences ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            if (references.Count == 0)
            {
                references.Add("dynamic.region/unavailable-instance");
            }
            return IngredientMaturityGateCatalog.ForLevel(level).Select(definition =>
                new IngredientMaturityGateReceipt
                {
                    Level = definition.Level,
                    GateId = definition.GateId,
                    Category = definition.Category,
                    ValidatorId = DynamicRegionEvidenceNormalizer.ContributorId,
                    ValidatorVersion = "v1",
                    Passed = false,
                    FailureReason = failureReason,
                    EvidenceReferences = references.ToList()
                });
        }

        private static IEnumerable<string> References(params IEnumerable<string>[] values)
        {
            return (values ?? Array.Empty<IEnumerable<string>>())
                .Where(value => value != null)
                .SelectMany(value => value);
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
