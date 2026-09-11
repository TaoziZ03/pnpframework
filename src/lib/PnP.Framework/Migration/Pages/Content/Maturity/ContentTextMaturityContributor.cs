using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
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
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(context, live));
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
            var planBound = IsPlanBound(context, normalized, evidence.Plan);
            if (planBound)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM3(evidence.Plan));
            }
            var operationalBound = planBound && IsOperationalBound(context, normalized, evidence.Plan, evidence.Operational);
            if (operationalBound)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM4(evidence.Operational));
            }
            if (operationalBound && IsProductizationBound(
                context,
                evidence.Plan,
                evidence.Operational,
                evidence.Productization))
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

        private static bool IsPlanBound(
            IngredientMaturityEvaluationContext context,
            ContentTextNormalization normalized,
            IngredientPlanEvidence planEvidence)
        {
            var plan = planEvidence?.Plan;
            var planDigestMatches = TryComputePlanDigest(plan, out var planDigest)
                && string.Equals(planDigest, planEvidence?.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase);
            if (context?.Identity == null || context.Source == null || context.Target == null
                || normalized?.Node == null || plan?.IngredientGraph?.Nodes == null
                || plan.IngredientActions == null
                || !string.Equals(planEvidence.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                || !string.Equals(planEvidence.ExpectedSourceSnapshotDigest, context.Source.SourceSnapshotDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plan.SourceSnapshotDigest, context.Source.SourceSnapshotDigest, StringComparison.OrdinalIgnoreCase)
                || !planDigestMatches)
            {
                return false;
            }

            var nodes = plan.IngredientGraph.Nodes.Where(value =>
                value != null && string.Equals(value.Id, context.Identity.IngredientId, StringComparison.Ordinal)).ToArray();
            var actions = plan.IngredientActions.Where(value =>
                value != null && string.Equals(value.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)).ToArray();
            if (nodes.Length != 1 || actions.Length != 1)
            {
                return false;
            }

            var node = nodes[0];
            return node.Kind == normalized.Node.Kind
                && string.Equals(node.Subtype, normalized.Node.Subtype, StringComparison.Ordinal)
                && string.Equals(node.SemanticRole, normalized.Node.SemanticRole, StringComparison.Ordinal)
                && string.Equals(node.SourcePredicateId, normalized.Node.SourcePredicateId, StringComparison.Ordinal)
                && string.Equals(node.SourcePageOrListItemIdentity, context.Source.PageOrListItemIdentity, StringComparison.Ordinal)
                && string.Equals(node.SourceVersionIdentity, context.Source.SourceVersion, StringComparison.Ordinal)
                && string.Equals(node.EvidenceDigest, context.Source.SourceArtifactDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(actions[0].TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal);
        }

        private static bool IsOperationalBound(
            IngredientMaturityEvaluationContext context,
            ContentTextNormalization normalized,
            IngredientPlanEvidence planEvidence,
            IngredientOperationalEvidence operational)
        {
            var operationalPlanDigestMatches = TryComputePlanDigest(operational?.Plan, out var operationalPlanDigest)
                && string.Equals(operationalPlanDigest, planEvidence?.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase);
            if (operational?.Plan == null || operational.ActionSignature == null
                || !string.Equals(operational.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                || !operationalPlanDigestMatches
                || !string.Equals(operational.AdmittedPlanDigest, planEvidence.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(operational.ActionSignature.TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(operational.ActionSignature.SourceEvidenceDigest, context.Source.SourceArtifactDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(operational.ActionSignature.SemanticDigest, normalized.SemanticProjectionSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    operational.VerificationReceipt?.TargetIdentityDigest,
                    operational.ActionSignature.TargetIdentityDigest,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    operational.VerificationReceipt?.ObservedStateDigest,
                    normalized.SemanticProjectionSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var action = planEvidence.Plan.IngredientActions.SingleOrDefault(value =>
                value != null && string.Equals(value.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal));
            return action != null
                && string.Equals(action.ActionId, operational.ActionSignature.ActionId, StringComparison.Ordinal)
                && string.Equals(action.TargetIdentity, operational.ActionSignature.TargetIdentity, StringComparison.Ordinal);
        }

        private static bool IsProductizationBound(
            IngredientMaturityEvaluationContext context,
            IngredientPlanEvidence planEvidence,
            IngredientOperationalEvidence operational,
            IngredientProductizationEvidence productization)
        {
            var report = productization?.CompareReport;
            var bindings = report?.Bindings;
            var action = operational?.ActionSignature;
            var verification = operational?.VerificationReceipt;
            var rows = (report?.Ingredients ?? Array.Empty<IngredientCompareResult>())
                .Where(value => value != null
                    && string.Equals(value.IngredientId, context?.Identity?.IngredientId, StringComparison.Ordinal))
                .ToArray();
            if (rows.Length != 1)
            {
                return false;
            }

            var row = rows[0];
            var importReceiptDigest = operational?.ImportReceipt == null
                ? null
                : MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(operational.ImportReceipt));

            return productization != null
                && context?.Identity != null
                && context.Source != null
                && context.Target != null
                && context.Producer != null
                && action != null
                && verification != null
                && string.Equals(productization.ImplementationCommit, context.Producer.ImplementationCommit, StringComparison.Ordinal)
                && string.Equals(productization.BuildCommit, context.Producer.ImplementationCommit, StringComparison.Ordinal)
                && string.Equals(productization.EndToEndCommit, context.Producer.ImplementationCommit, StringComparison.Ordinal)
                && string.Equals(productization.PrReadyCommit, context.Producer.ImplementationCommit, StringComparison.Ordinal)
                && string.Equals(productization.EndToEndPlanDigest, planEvidence.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(bindings?.PlanDigestSha256, planEvidence.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(bindings?.SnapshotDigestSha256, context.Source.SourceSnapshotDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(bindings?.ImportReceiptDigestSha256, importReceiptDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(report.Producer?.Id, context.Producer.ProducerId, StringComparison.Ordinal)
                && string.Equals(report.Producer?.Version, context.Producer.ProducerVersion, StringComparison.Ordinal)
                && string.Equals(report.Producer?.ImplementationRef, context.Producer.ImplementationCommit, StringComparison.Ordinal)
                && string.Equals(row.Kind, "Content", StringComparison.Ordinal)
                && row.Material
                && string.Equals(row.Lineage?.SourceIngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                && string.Equals(row.Lineage?.SourceArtifactDigestSha256, context.Source.SourceArtifactDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.Lineage?.ActionId, action.ActionId, StringComparison.Ordinal)
                && string.Equals(row.Lineage?.TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                && string.Equals(row.Lineage?.PlanDigestSha256, planEvidence.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase)
                && row.Lineage.EvidenceRefs != null
                && row.Lineage.EvidenceRefs.Count > 0
                && row.Lineage.EvidenceRefs.All(value => !string.IsNullOrWhiteSpace(value))
                && string.Equals(row.TargetEvidenceState, IngredientTargetEvidenceStates.Fresh, StringComparison.Ordinal)
                && row.ObservedAtUtc.HasValue
                && row.ObservedAtUtc.Value != default
                && row.ObservedAtUtc.Value >= verification.VerifiedAtUtc
                && HasAdmissibleCompareResult(row, context.Source.SourceArtifactDigest);
        }

        private static bool HasAdmissibleCompareResult(
            IngredientCompareResult row,
            string sourceArtifactDigest)
        {
            if (string.Equals(row.ResultClass, PublishingPageCompareContract.ResultClasses.Exact, StringComparison.Ordinal))
            {
                return string.Equals(row.Expected?.RawDigestSha256, sourceArtifactDigest, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.Actual?.RawDigestSha256, sourceArtifactDigest, StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(row.ResultClass, PublishingPageCompareContract.ResultClasses.CanonicalEquivalent, StringComparison.Ordinal))
            {
                return IngredientMaturityEvaluator.IsSha256(row.Expected?.CanonicalDigestSha256)
                    && string.Equals(
                        row.Expected.CanonicalDigestSha256,
                        row.Actual?.CanonicalDigestSha256,
                        StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                    row.ResultClass,
                    PublishingPageCompareContract.ResultClasses.TransformedAsPlanned,
                    StringComparison.Ordinal)
                && IngredientMaturityEvaluator.IsSha256(row.Actual?.CanonicalDigestSha256);
        }

        private static bool TryComputePlanDigest(
            PublishingPageMigrationPlan plan,
            out string digest)
        {
            try
            {
                digest = plan == null ? null : PublishingPageDigest.ComputePlanDigest(plan);
                return !string.IsNullOrWhiteSpace(digest);
            }
            catch (Exception)
            {
                digest = null;
                return false;
            }
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
