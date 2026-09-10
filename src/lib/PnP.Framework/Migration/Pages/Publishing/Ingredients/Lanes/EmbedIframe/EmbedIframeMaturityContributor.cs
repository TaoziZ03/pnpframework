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

            var live = EmbedIframeEvidenceNormalizer.ProjectLiveEvidence(
                evidence.Live,
                normalized,
                evidence.ExpectedSourceValueDigests,
                evidence.ExpectedTargetValueDigests);
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
                var levelReceipts = IngredientMaturityEvidenceValidator.ValidateM3(evidence.Plan).ToList();
                ApplyBinding(levelReceipts, ValidatePlanBinding(context, normalized));
                receipts.AddRange(levelReceipts);
            }
            if (evidence.Operational != null)
            {
                var levelReceipts = IngredientMaturityEvidenceValidator.ValidateM4(evidence.Operational).ToList();
                ApplyBinding(levelReceipts, ValidateOperationalBinding(context));
                receipts.AddRange(levelReceipts);
            }
            if (evidence.Productization != null)
            {
                var levelReceipts = IngredientMaturityEvidenceValidator.ValidateM5(
                    evidence.Productization,
                    context?.Producer,
                    evidence.Plan?.ExpectedPlanDigest).ToList();
                ApplyBinding(levelReceipts, ValidateProductizationBinding(context));
                receipts.AddRange(levelReceipts);
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

        private string ValidatePlanBinding(
            IngredientMaturityEvaluationContext context,
            EmbedIframeNormalization normalized)
        {
            var bindingFailure = ValidateCommonBinding(context);
            if (bindingFailure != null)
            {
                return bindingFailure;
            }
            var plan = evidence.Plan?.Plan;
            var nodes = plan?.IngredientGraph?.Nodes?
                .Where(value => value != null
                    && string.Equals(value.Id, context.Identity.IngredientId, StringComparison.Ordinal))
                .ToArray() ?? Array.Empty<Pages.Ingredients.PageIngredientNode>();
            var actions = plan?.IngredientActions?
                .Where(value => value != null
                    && string.Equals(value.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal))
                .ToArray() ?? Array.Empty<Pages.Ingredients.PageIngredientAction>();
            if (!string.Equals(evidence.Plan.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                || !string.Equals(evidence.Plan.ExpectedSourceSnapshotDigest, context.Source.SourceSnapshotDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(evidence.Plan.ExpectedPlanDigest, evidence.Binding.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || nodes.Length != 1
                || !string.Equals(nodes[0].SourceVersionIdentity, context.Source.SourceVersion, StringComparison.Ordinal)
                || !string.Equals(nodes[0].EvidenceDigest, context.Source.SourceArtifactDigest, StringComparison.OrdinalIgnoreCase)
                || actions.Length != 1
                || !string.Equals(actions[0].ActionId, evidence.Binding.ActionId, StringComparison.Ordinal)
                || !string.Equals(actions[0].TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(normalized?.Node?.Id, context.Identity.IngredientId, StringComparison.Ordinal))
            {
                return "The iframe plan, action, source version, ingredient, or target does not bind the evaluation context.";
            }
            return null;
        }

        private string ValidateOperationalBinding(IngredientMaturityEvaluationContext context)
        {
            var bindingFailure = ValidateCommonBinding(context);
            if (bindingFailure != null)
            {
                return bindingFailure;
            }
            var operational = evidence.Operational;
            var actions = operational?.Plan?.IngredientActions?
                .Where(value => value != null
                    && string.Equals(value.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal))
                .ToArray() ?? Array.Empty<Pages.Ingredients.PageIngredientAction>();
            var action = actions.Length == 1 ? actions[0] : null;
            var operationId = operational?.ImportReceipt?.OperationId ?? Guid.Empty;
            if (!string.Equals(operational?.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                || !string.Equals(operational?.AdmittedPlanDigest, evidence.Binding.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || action == null
                || !string.Equals(action.ActionId, evidence.Binding.ActionId, StringComparison.Ordinal)
                || !string.Equals(action.TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(operational.ActionSignature?.ActionId, evidence.Binding.ActionId, StringComparison.Ordinal)
                || !string.Equals(operational.ActionSignature?.TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                || operationId == Guid.Empty
                || operationId != evidence.Binding.OperationId
                || operational.JournalState?.OperationId != operationId
                || operational.VerificationReceipt?.OperationId != operationId
                || operational.ImportReceipt?.VerifiedIngredientIds?.Contains(context.Identity.IngredientId, StringComparer.Ordinal) != true
                || !HasValues(operational.PlanAdmissionEvidenceReferences)
                || !HasValues(operational.ActionEvidenceReferences)
                || !HasValues(operational.ReceiptEvidenceReferences)
                || !HasValues(operational.FreshReadbackEvidenceReferences)
                || !HasValues(operational.RuntimeCleanupRetryEvidenceReferences)
                || operational.RuntimeRequired && (!string.Equals(operational.RuntimeReceipt?.TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                    || !Contains(operational.RuntimeCleanupRetryEvidenceReferences, evidence.Binding.RuntimeEvidenceReference))
                || operational.CleanupRequired && !Contains(operational.RuntimeCleanupRetryEvidenceReferences, evidence.Binding.CleanupEvidenceReference))
            {
                return "The iframe operation, action, receipt, runtime, cleanup, ingredient, or target binding does not match the evaluation context.";
            }
            return null;
        }

        private string ValidateProductizationBinding(IngredientMaturityEvaluationContext context)
        {
            var bindingFailure = ValidateCommonBinding(context);
            if (bindingFailure != null)
            {
                return bindingFailure;
            }
            var product = evidence.Productization;
            var compare = product?.CompareReport;
            var results = compare?.Ingredients?
                .Where(value => value != null
                    && string.Equals(value.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal))
                .ToArray() ?? Array.Empty<Pages.Publishing.Comparison.IngredientCompareResult>();
            var result = results.Length == 1 ? results[0] : null;
            if (!string.Equals(product?.ImplementationCommit, evidence.Binding.ImplementationCommit, StringComparison.Ordinal)
                || !string.Equals(product?.EndToEndCommit, evidence.Binding.ImplementationCommit, StringComparison.Ordinal)
                || !string.Equals(product?.EndToEndPlanDigest, evidence.Binding.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(product?.EndToEndEvidenceReference, evidence.Binding.RuntimeEvidenceReference, StringComparison.Ordinal)
                || !string.Equals(product?.CompareEvidenceReference, evidence.Binding.CompareEvidenceReference, StringComparison.Ordinal)
                || !string.Equals(compare?.Bindings?.PlanDigestSha256, evidence.Binding.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(compare?.TargetIdentity?.CanonicalIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                || result == null
                || !string.Equals(result.TargetEvidenceState, Pages.Publishing.Comparison.IngredientTargetEvidenceStates.Fresh, StringComparison.Ordinal)
                || !string.Equals(result.Lineage?.SourceIngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                || !string.Equals(result.Lineage?.ActionId, evidence.Binding.ActionId, StringComparison.Ordinal)
                || !string.Equals(result.Lineage?.TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(result.Lineage?.PlanDigestSha256, evidence.Binding.PlanDigest, StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.ResultClass, Pages.Publishing.Comparison.PublishingPageCompareContract.ResultClasses.AuthorizationBlocked, StringComparison.Ordinal)
                || string.Equals(result.TargetEvidenceState, Pages.Publishing.Comparison.IngredientTargetEvidenceStates.Denied, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(result.Expected?.CanonicalDigestSha256)
                || !string.Equals(result.Expected.CanonicalDigestSha256, result.Actual?.CanonicalDigestSha256, StringComparison.OrdinalIgnoreCase))
            {
                return "The iframe productization, same-commit E2E, Compare ingredient, action, target, or availability binding does not match the evaluation context.";
            }
            return null;
        }

        private string ValidateCommonBinding(IngredientMaturityEvaluationContext context)
        {
            var binding = evidence.Binding;
            if (context?.Identity == null || context.Source == null || context.Target == null || binding == null
                || !string.Equals(binding.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                || !string.Equals(binding.SourceVersion, context.Source.SourceVersion, StringComparison.Ordinal)
                || !string.Equals(binding.TargetProfile, context.Target.TargetProfile, StringComparison.Ordinal)
                || !string.Equals(binding.TargetIdentity, context.Target.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(binding.SourceHostWebPartId, evidence.Source?.Host?.WebPartId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(binding.TargetHostWebPartId)
                || string.Equals(binding.SourceHostWebPartId, binding.TargetHostWebPartId, StringComparison.OrdinalIgnoreCase)
                || !context.Target.TargetIdentity.EndsWith("#webpart:" + binding.TargetHostWebPartId, StringComparison.OrdinalIgnoreCase)
                || !MatchesDigest(evidence.ExpectedSourceValueDigests, "binding.hostWebPartId", binding.SourceHostWebPartId)
                || !MatchesDigest(evidence.ExpectedSourceValueDigests, "binding.consumer", "webpart:" + binding.SourceHostWebPartId)
                || !MatchesDigest(evidence.ExpectedTargetValueDigests, "binding.hostWebPartId", binding.TargetHostWebPartId)
                || !MatchesDigest(evidence.ExpectedTargetValueDigests, "binding.consumer", "webpart:" + binding.TargetHostWebPartId))
            {
                return "The iframe evidence envelope does not bind the exact ingredient, source version, host mapping, target profile, and target identity.";
            }
            return null;
        }

        private static bool Contains(IEnumerable<string> values, string expected)
        {
            return !string.IsNullOrWhiteSpace(expected)
                && (values?.Contains(expected, StringComparer.Ordinal) == true);
        }

        private static bool HasValues(IEnumerable<string> values)
        {
            return values?.Any(value => !string.IsNullOrWhiteSpace(value)) == true;
        }

        private static bool MatchesDigest(
            IReadOnlyDictionary<string, string> values,
            string path,
            string expectedValue)
        {
            return values?.TryGetValue(path, out var digest) == true
                && string.Equals(
                    digest,
                    EmbedIframeEvidenceNormalizer.ComputeScalarDigest(expectedValue),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyBinding(
            IEnumerable<IngredientMaturityGateReceipt> receipts,
            string failureReason)
        {
            if (failureReason == null)
            {
                return;
            }
            foreach (var receipt in receipts)
            {
                receipt.Passed = false;
                receipt.FailureReason = failureReason;
            }
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
