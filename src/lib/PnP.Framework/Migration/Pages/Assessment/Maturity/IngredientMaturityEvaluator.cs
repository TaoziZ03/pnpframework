using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal static class IngredientMaturityEvaluator
    {
        private const string MissingReceiptReason = "The required gate receipt is missing.";

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
            // Do not retain aliases to mutable contributor/context bindings.
            return MigrationContractSerializer.Deserialize<IngredientMaturityAssessment>(
                MigrationContractSerializer.SerializeCanonical(assessment));
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

        // Intrinsic consistency only; use the context/contributor overload for admission.
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
            ValidateContext(new IngredientMaturityEvaluationContext
            {
                Identity = assessment.Identity,
                Source = assessment.Source,
                Target = assessment.Target,
                Producer = assessment.Producer,
                TargetMaturity = assessment.TargetMaturity,
                TechnicalOutcome = assessment.TechnicalOutcome
            });

            var expectedLevels = (IngredientMaturityLevel[])Enum.GetValues(typeof(IngredientMaturityLevel));
            Require(assessment.Levels != null && assessment.Levels.Count == expectedLevels.Length,
                "The assessment must contain every maturity level exactly once in catalog order.");
            IngredientMaturityLevel? attained = null;
            var continuous = true;
            for (var index = 0; index < expectedLevels.Length; index++)
            {
                var level = assessment.Levels[index];
                Require(level != null && level.Level == expectedLevels[index],
                    "The assessment maturity levels are missing, duplicated, unknown, or out of order.");
                var definitions = IngredientMaturityGateCatalog.ForLevel(level.Level);
                Require(level.Gates != null && level.Gates.Count == definitions.Count,
                    "The assessment must contain every required gate exactly once.");
                for (var gateIndex = 0; gateIndex < definitions.Count; gateIndex++)
                {
                    ValidateGateResult(level.Gates[gateIndex], definitions[gateIndex]);
                }
                continuous = continuous && level.Gates.All(value => value.Status == IngredientMaturityGateStatus.Passed);
                Require(level.Passed == continuous,
                    "The level result does not match continuous gate evaluation.");
                if (continuous)
                {
                    attained = level.Level;
                }
            }
            Require(assessment.AttainedMaturity == attained
                && assessment.Confidence == Confidence(attained)
                && string.Equals(assessment.MissingPromotionGate,
                    MissingPromotionGate(assessment.Levels, attained, assessment.TargetMaturity), StringComparison.Ordinal),
                "The attained maturity, confidence, or missing promotion gate does not match the gate evidence.");
        }

        // A digest is not an authenticity proof. Admission consumers must also supply
        // independently obtained bindings and contributors that revalidate their evidence.
        public static void ValidateAssessment(
            IngredientMaturityAssessment assessment,
            IngredientMaturityEvaluationContext expectedContext,
            IngredientMaturityContributorCatalog contributors)
        {
            ValidateAssessment(assessment);
            var expected = Evaluate(expectedContext, contributors);
            Require(string.Equals(assessment.AssessmentDigest, expected.AssessmentDigest, StringComparison.Ordinal),
                "The assessment does not match the independently bound context and re-evaluated evidence.");
        }

        private static void ValidateGateResult(
            IngredientMaturityGateResult gate,
            IngredientMaturityGateDefinition definition)
        {
            Require(gate != null
                && string.Equals(gate.GateId, definition.GateId, StringComparison.Ordinal)
                && gate.Category == definition.Category
                && Enum.IsDefined(typeof(IngredientMaturityGateStatus), gate.Status),
                "The gate identity, order, category, or status is not in the frozen gate catalog.");
            Require(gate.EvidenceReferences != null
                && CanonicalReferences(gate.EvidenceReferences).Count == gate.EvidenceReferences.Count,
                "Gate evidence references must be nonempty strings, unique, and in canonical order.");
            if (gate.Status == IngredientMaturityGateStatus.Passed)
            {
                Require(HasValidatorIdentity(gate.ValidatorId, gate.ValidatorVersion)
                    && gate.EvidenceReferences.Count > 0 && gate.FailureReason == null,
                    "A passed gate requires the supported validator, evidence references, and no failure reason.");
            }
            else
            {
                Require(!string.IsNullOrWhiteSpace(gate.FailureReason),
                    "A failed or missing gate requires an explicit failure reason.");
                if (gate.Status == IngredientMaturityGateStatus.Missing)
                {
                    Require(gate.ValidatorId == null && gate.ValidatorVersion == null
                        && gate.EvidenceReferences.Count == 0
                        && string.Equals(gate.FailureReason, MissingReceiptReason, StringComparison.Ordinal),
                        "A missing gate cannot contain a validator or supplied evidence.");
                }
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
                    FailureReason = MissingReceiptReason
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
                && HasValidatorIdentity(receipt.ValidatorId, receipt.ValidatorVersion)
                && canonicalReferences.Count > 0;
            var passed = receipt.Passed && metadataValid && string.IsNullOrWhiteSpace(receipt.FailureReason);
            return new IngredientMaturityGateResult
            {
                GateId = definition.GateId,
                Category = definition.Category,
                Status = passed
                    ? IngredientMaturityGateStatus.Passed
                    : IngredientMaturityGateStatus.Failed,
                ValidatorId = receipt.ValidatorId,
                ValidatorVersion = receipt.ValidatorVersion,
                FailureReason = passed ? null : !metadataValid
                    ? "The gate receipt metadata or evidence references are incomplete, unsupported, or non-canonical."
                    : !string.IsNullOrWhiteSpace(receipt.FailureReason)
                        ? receipt.FailureReason
                        : "The gate validator did not pass.",
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

        internal static void ValidateContext(IngredientMaturityEvaluationContext context)
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
                || string.IsNullOrWhiteSpace(context.Identity.SourcePredicateId)
                || !IngredientMaturityContributorCatalog.IsKnownLane(context.Identity.Lane)
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
            Require(Enum.IsDefined(typeof(IngredientMaturityLevel), context.TargetMaturity)
                && Enum.IsDefined(typeof(IngredientTechnicalStatus), context.TechnicalOutcome.Status)
                && Enum.IsDefined(typeof(PageMigrationOutcome), context.TechnicalOutcome.PnPMigrationOutcome)
                && !string.IsNullOrWhiteSpace(context.TechnicalOutcome.ReasonCode),
                "The target maturity or independent technical outcome is missing or unknown.");
            var runtimeWork = string.Equals(
                context.Identity.WorkItemType,
                IngredientMaturityContract.RuntimeVerificationWorkItem,
                StringComparison.Ordinal);
            if ((!runtimeWork && (!string.Equals(context.Identity.WorkItemType, IngredientMaturityContract.CanonicalIngredientWorkItem, StringComparison.Ordinal)
                    || !context.Identity.Kind.HasValue
                    || !Enum.IsDefined(typeof(PageIngredientKind), context.Identity.Kind.Value)
                    || string.Equals(context.Identity.Lane, "behavior.interaction", StringComparison.Ordinal)))
                || (runtimeWork && (context.Identity.Kind.HasValue
                    || !string.Equals(context.Identity.Lane, "behavior.interaction", StringComparison.Ordinal))))
            {
                throw new InvalidDataException("Only behavior.interaction runtime-verification work may omit PageIngredientKind.");
            }
        }

        private static bool HasValidatorIdentity(string validatorId, string validatorVersion)
        {
            return string.Equals(validatorId, IngredientMaturityEvidenceValidator.ValidatorId, StringComparison.Ordinal)
                && string.Equals(validatorVersion, IngredientMaturityEvidenceValidator.ValidatorVersion, StringComparison.Ordinal);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
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
            var receipts = contribution.GateReceipts ?? new List<IngredientMaturityGateReceipt>();
            Require(receipts.All(value => value != null), "The maturity contribution contains a null gate receipt.");
            var unknown = receipts
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
