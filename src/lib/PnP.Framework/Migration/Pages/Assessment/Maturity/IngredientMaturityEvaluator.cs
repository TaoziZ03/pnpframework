using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal static class IngredientMaturityEvaluator
    {
        public static IngredientMaturityAssessment Evaluate(
            IngredientMaturityEvaluationContext context,
            IngredientMaturityContributorCatalog contributors)
        {
            ValidateContext(context);
            if (contributors == null)
            {
                throw new ArgumentNullException(nameof(contributors));
            }

            var contributor = contributors.Resolve(context.Identity.Lane);
            var contribution = contributor.Contribute(context)
                ?? throw new InvalidDataException("The maturity contributor returned no evidence.");
            ValidateContribution(context, contributor, contribution);
            var receipts = contribution.GateReceipts ?? new List<IngredientMaturityGateReceipt>();
            var levelResults = new List<IngredientMaturityLevelResult>();
            IngredientMaturityLevel? attained = null;
            var continuous = true;

            foreach (IngredientMaturityLevel level in Enum.GetValues(typeof(IngredientMaturityLevel)))
            {
                var gates = IngredientMaturityGateCatalog.ForLevel(level)
                    .Select(definition => EvaluateGate(definition, receipts))
                    .ToList();
                var passed = continuous && gates.All(value => value.Status == IngredientMaturityGateStatus.Passed);
                if (passed)
                {
                    attained = level;
                }
                else
                {
                    continuous = false;
                }
                levelResults.Add(new IngredientMaturityLevelResult
                {
                    Level = level,
                    Passed = passed,
                    Gates = gates
                });
            }

            var assessment = new IngredientMaturityAssessment
            {
                Identity = context.Identity,
                Source = context.Source,
                Target = context.Target,
                Producer = context.Producer,
                Levels = levelResults,
                TargetMaturity = context.TargetMaturity,
                AttainedMaturity = attained,
                Confidence = Confidence(attained),
                MissingPromotionGate = MissingPromotionGate(levelResults, attained, context.TargetMaturity),
                TechnicalOutcome = context.TechnicalOutcome
            };
            assessment.AssessmentDigest = ComputeDigest(assessment);
            return assessment;
        }

        public static string ComputeDigest(IngredientMaturityAssessment assessment)
        {
            if (assessment == null)
            {
                throw new ArgumentNullException(nameof(assessment));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    assessment,
                    nameof(IngredientMaturityAssessment.AssessmentDigest)));
        }

        public static void ValidateAssessment(IngredientMaturityAssessment assessment)
        {
            if (assessment == null
                || !string.Equals(assessment.SchemaVersion, IngredientMaturityContract.SchemaVersion, StringComparison.Ordinal)
                || !string.Equals(assessment.EvaluatorVersion, IngredientMaturityContract.EvaluatorVersion, StringComparison.Ordinal)
                || !IsSha256(assessment.AssessmentDigest)
                || !string.Equals(assessment.AssessmentDigest, ComputeDigest(assessment), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The ingredient maturity assessment schema, evaluator version, or canonical digest is invalid.");
            }
        }

        private static IngredientMaturityGateResult EvaluateGate(
            IngredientMaturityGateDefinition definition,
            IEnumerable<IngredientMaturityGateReceipt> receipts)
        {
            var matches = receipts
                .Where(value => value != null && string.Equals(value.GateId, definition.GateId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                return new IngredientMaturityGateResult
                {
                    GateId = definition.GateId,
                    Category = definition.Category,
                    Status = IngredientMaturityGateStatus.Missing,
                    FailureReason = "The required gate receipt is missing."
                };
            }
            if (matches.Length != 1)
            {
                return new IngredientMaturityGateResult
                {
                    GateId = definition.GateId,
                    Category = definition.Category,
                    Status = IngredientMaturityGateStatus.Failed,
                    FailureReason = "The contributor supplied duplicate gate receipts."
                };
            }

            var receipt = matches[0];
            var canonicalReferences = CanonicalReferences(receipt.EvidenceReferences);
            var metadataValid = receipt.Level == definition.Level
                && receipt.Category == definition.Category
                && !string.IsNullOrWhiteSpace(receipt.ValidatorId)
                && !string.IsNullOrWhiteSpace(receipt.ValidatorVersion)
                && canonicalReferences.Count > 0;
            return new IngredientMaturityGateResult
            {
                GateId = definition.GateId,
                Category = definition.Category,
                Status = receipt.Passed && metadataValid
                    ? IngredientMaturityGateStatus.Passed
                    : IngredientMaturityGateStatus.Failed,
                ValidatorId = receipt.ValidatorId,
                ValidatorVersion = receipt.ValidatorVersion,
                FailureReason = metadataValid
                    ? receipt.FailureReason
                    : "The gate receipt metadata or evidence references are incomplete or non-canonical.",
                EvidenceReferences = canonicalReferences
            };
        }

        private static IList<string> CanonicalReferences(IEnumerable<string> values)
        {
            var supplied = (values ?? Array.Empty<string>()).ToArray();
            if (supplied.Any(string.IsNullOrWhiteSpace)
                || supplied.Count() != supplied.Distinct(StringComparer.Ordinal).Count()
                || !supplied.SequenceEqual(supplied.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                return new List<string>();
            }
            return new ReadOnlyCollection<string>(supplied);
        }

        private static void ValidateContext(IngredientMaturityEvaluationContext context)
        {
            if (context?.Identity == null || context.Source == null || context.Target == null
                || context.Producer == null || context.TechnicalOutcome == null)
            {
                throw new InvalidDataException("Maturity evaluation requires identity, source, target, producer, and technical-outcome bindings.");
            }
            if (!IsSha256(context.Identity.ClaimId)
                || string.IsNullOrWhiteSpace(context.Identity.WorkItemType)
                || string.IsNullOrWhiteSpace(context.Identity.Lane)
                || string.IsNullOrWhiteSpace(context.Identity.IngredientId)
                || string.IsNullOrWhiteSpace(context.Identity.Subtype)
                || string.IsNullOrWhiteSpace(context.Identity.SemanticRole)
                || string.IsNullOrWhiteSpace(context.Source.PageOrListItemIdentity)
                || string.IsNullOrWhiteSpace(context.Source.SourceVersion)
                || !IsSha256(context.Source.SourceArtifactDigest)
                || !IsSha256(context.Source.SourceSnapshotDigest)
                || string.IsNullOrWhiteSpace(context.Target.TargetProfile)
                || string.IsNullOrWhiteSpace(context.Target.TargetIdentity)
                || string.IsNullOrWhiteSpace(context.Producer.ProducerId)
                || string.IsNullOrWhiteSpace(context.Producer.ProducerVersion)
                || !IsCommit(context.Producer.ImplementationCommit)
                || !IsOptionalSha256(context.Producer.BinaryDigest))
            {
                throw new InvalidDataException("Maturity evaluation bindings are incomplete or malformed.");
            }
            var runtimeWork = string.Equals(
                context.Identity.WorkItemType,
                IngredientMaturityContract.RuntimeVerificationWorkItem,
                StringComparison.Ordinal);
            if ((!runtimeWork && !context.Identity.Kind.HasValue)
                || (runtimeWork && (context.Identity.Kind.HasValue
                    || !string.Equals(context.Identity.Lane, "behavior.interaction", StringComparison.Ordinal))))
            {
                throw new InvalidDataException("Only behavior.interaction runtime-verification work may omit PageIngredientKind.");
            }
        }

        private static void ValidateContribution(
            IngredientMaturityEvaluationContext context,
            IIngredientMaturityContributor contributor,
            IngredientMaturityContribution contribution)
        {
            if (!string.Equals(contribution.ContributorId, contributor.ContributorId, StringComparison.Ordinal)
                || !string.Equals(contribution.ClaimId, context.Identity.ClaimId, StringComparison.Ordinal)
                || !string.Equals(contribution.Lane, context.Identity.Lane, StringComparison.Ordinal)
                || !string.Equals(contribution.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The maturity contribution does not bind the requested claim, lane, ingredient, and contributor identity.");
            }
            var unknown = (contribution.GateReceipts ?? new List<IngredientMaturityGateReceipt>())
                .FirstOrDefault(value => value != null
                    && !IngredientMaturityGateCatalog.Definitions.Any(definition =>
                        string.Equals(definition.GateId, value.GateId, StringComparison.Ordinal)));
            if (unknown != null)
            {
                throw new InvalidDataException($"Unknown maturity gate '{unknown.GateId}'.");
            }
        }

        private static string MissingPromotionGate(
            IEnumerable<IngredientMaturityLevelResult> levels,
            IngredientMaturityLevel? attained,
            IngredientMaturityLevel target)
        {
            if (attained.HasValue && attained.Value >= target)
            {
                return null;
            }
            var next = attained.HasValue ? (IngredientMaturityLevel)((int)attained.Value + 1) : IngredientMaturityLevel.M0;
            return levels.Single(value => value.Level == next).Gates
                .First(value => value.Status != IngredientMaturityGateStatus.Passed)
                .GateId;
        }

        private static IngredientMaturityConfidence Confidence(IngredientMaturityLevel? attained)
        {
            if (!attained.HasValue)
            {
                return IngredientMaturityConfidence.None;
            }
            if (attained.Value >= IngredientMaturityLevel.M5)
            {
                return IngredientMaturityConfidence.High;
            }
            return attained.Value >= IngredientMaturityLevel.M3
                ? IngredientMaturityConfidence.Medium
                : IngredientMaturityConfidence.Low;
        }

        internal static bool IsSha256(string value)
        {
            return value != null && value.Length == 64
                && value.All(character => character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f');
        }

        internal static bool IsCommit(string value)
        {
            return value != null && value.Length == 40
                && value.All(character => character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f');
        }

        private static bool IsOptionalSha256(string value)
        {
            return value == null || IsSha256(value);
        }
    }
}
