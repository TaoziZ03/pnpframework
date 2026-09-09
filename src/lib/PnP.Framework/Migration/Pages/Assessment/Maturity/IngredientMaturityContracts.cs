using PnP.Framework.Migration.Pages.Ingredients;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal enum IngredientMaturityLevel
    {
        M0 = 0,
        M1 = 1,
        M2 = 2,
        M3 = 3,
        M4 = 4,
        M5 = 5
    }

    internal enum IngredientMaturityGateCategory
    {
        PnPDomain = 1,
        DeliveryProcess = 2
    }

    internal enum IngredientMaturityGateStatus
    {
        Passed = 1,
        Failed = 2,
        Missing = 3
    }

    internal enum IngredientMaturityConfidence
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    internal enum IngredientTechnicalStatus
    {
        Pass = 1,
        Conditional = 2,
        Fail = 3,
        Unverified = 4
    }

    internal static class IngredientMaturityContract
    {
        public const string SchemaVersion = "pnp-ingredient-maturity-assessment/v1";
        public const string EvaluatorVersion = "pnp-ingredient-maturity-evaluator/v1";
        public const string CanonicalIngredientWorkItem = "canonical-ingredient";
        public const string RuntimeVerificationWorkItem = "runtime-verification";
    }

    internal sealed class IngredientMaturityIdentity
    {
        public string ClaimId { get; set; }

        public string WorkItemType { get; set; } = IngredientMaturityContract.CanonicalIngredientWorkItem;

        public string Lane { get; set; }

        public string IngredientId { get; set; }

        public PageIngredientKind? Kind { get; set; }

        public string Subtype { get; set; }

        public string SemanticRole { get; set; }

        public string SourcePredicateId { get; set; }
    }

    internal sealed class IngredientMaturitySourceBinding
    {
        public string PageOrListItemIdentity { get; set; }

        public string SourceVersion { get; set; }

        public string SourceArtifactDigest { get; set; }

        public string SourceSnapshotDigest { get; set; }
    }

    internal sealed class IngredientMaturityTargetBinding
    {
        public string TargetProfile { get; set; }

        public string TargetIdentity { get; set; }
    }

    internal sealed class IngredientMaturityProducerBinding
    {
        public string ProducerId { get; set; }

        public string ProducerVersion { get; set; }

        public string ImplementationCommit { get; set; }

        public string BinaryDigest { get; set; }
    }

    internal sealed class IngredientTechnicalOutcome
    {
        public PageMigrationOutcome PnPMigrationOutcome { get; set; }

        public IngredientTechnicalStatus Status { get; set; }

        public string ReasonCode { get; set; }
    }

    internal sealed class IngredientMaturityGateReceipt
    {
        public IngredientMaturityLevel Level { get; set; }

        public string GateId { get; set; }

        public IngredientMaturityGateCategory Category { get; set; }

        public string ValidatorId { get; set; }

        public string ValidatorVersion { get; set; }

        public bool Passed { get; set; }

        public string FailureReason { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class IngredientMaturityContribution
    {
        public string ContributorId { get; set; }

        public string ClaimId { get; set; }

        public string Lane { get; set; }

        public string IngredientId { get; set; }

        public IList<IngredientMaturityGateReceipt> GateReceipts { get; set; } =
            new List<IngredientMaturityGateReceipt>();
    }

    internal interface IIngredientMaturityContributor
    {
        string ContributorId { get; }

        string Lane { get; }

        IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context);
    }

    internal sealed class IngredientMaturityEvaluationContext
    {
        public IngredientMaturityIdentity Identity { get; set; }

        public IngredientMaturitySourceBinding Source { get; set; }

        public IngredientMaturityTargetBinding Target { get; set; }

        public IngredientMaturityProducerBinding Producer { get; set; }

        public IngredientMaturityLevel TargetMaturity { get; set; }

        public IngredientTechnicalOutcome TechnicalOutcome { get; set; }
    }

    internal sealed class IngredientMaturityGateResult
    {
        public string GateId { get; set; }

        public IngredientMaturityGateCategory Category { get; set; }

        public IngredientMaturityGateStatus Status { get; set; }

        public string ValidatorId { get; set; }

        public string ValidatorVersion { get; set; }

        public string FailureReason { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class IngredientMaturityLevelResult
    {
        public IngredientMaturityLevel Level { get; set; }

        public bool Passed { get; set; }

        public IList<IngredientMaturityGateResult> Gates { get; set; } =
            new List<IngredientMaturityGateResult>();
    }

    internal sealed class IngredientMaturityAssessment
    {
        public string SchemaVersion { get; set; } = IngredientMaturityContract.SchemaVersion;

        public IngredientMaturityIdentity Identity { get; set; }

        public IngredientMaturitySourceBinding Source { get; set; }

        public IngredientMaturityTargetBinding Target { get; set; }

        public IngredientMaturityProducerBinding Producer { get; set; }

        public IList<IngredientMaturityLevelResult> Levels { get; set; } =
            new List<IngredientMaturityLevelResult>();

        public IngredientMaturityLevel TargetMaturity { get; set; }

        public IngredientMaturityLevel? AttainedMaturity { get; set; }

        public IngredientMaturityConfidence Confidence { get; set; }

        public string MissingPromotionGate { get; set; }

        public IngredientTechnicalOutcome TechnicalOutcome { get; set; }

        public string EvaluatorVersion { get; set; } = IngredientMaturityContract.EvaluatorVersion;

        public string AssessmentDigest { get; set; }
    }
}
