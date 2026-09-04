using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public enum IngredientSelectableAction
    {
        Reproduce = 1,
        Transform = 2,
        Reference = 3,
        EvidenceOnly = 4,
        Exclude = 5,
        Defer = 6
    }

    public enum IngredientActionScope
    {
        Self = 1,
        PayloadOnly = 2,
        Subtree = 3
    }

    public enum IngredientDependencyEffect
    {
        Retained = 1,
        SatisfiedByPolicy = 2,
        RequiresAlternative = 3,
        Deferred = 4
    }

    public enum IngredientComparisonRule
    {
        Exact = 1,
        Transformed = 2,
        Referenced = 3,
        EvidenceOnly = 4,
        ExpectedAbsent = 5,
        NotEvaluated = 6
    }

    public enum IngredientTerminalStatus
    {
        Executable = 1,
        SatisfiedByPolicy = 2,
        DecisionRequired = 3,
        AuthorizationBlocked = 4
    }

    public sealed class PageIngredientActionCandidate
    {
        public string CandidateActionId { get; set; }

        public IngredientSelectableAction Action { get; set; }

        public IngredientActionScope Scope { get; set; }

        public IngredientCapability Capability { get; set; }

        public string Realization { get; set; }

        public string PolicyId { get; set; }

        public string PolicyVersion { get; set; }

        public string ReasonCode { get; set; }

        public string Reason { get; set; }

        public IngredientDependencyEffect DependencyEffect { get; set; }

        public IngredientComparisonRule ComparisonRule { get; set; }

        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// Reviewable JSON input. It names one policy-filtered candidate and binds
    /// that choice to an immutable source snapshot digest.
    /// </summary>
    public sealed class PageIngredientActionSelectionRequest
    {
        public string IngredientId { get; set; }

        public string CandidateActionId { get; set; }

        public string SnapshotDigest { get; set; }

        public string SelectedBy { get; set; }

        public DateTimeOffset? SelectedAtUtc { get; set; }

        public string ApprovalReference { get; set; }
    }

    public sealed class PageIngredientSelectionAudit
    {
        public string SelectedBy { get; set; }

        public DateTimeOffset? SelectedAtUtc { get; set; }

        public string ApprovalReference { get; set; }
    }

    public sealed class PageIngredientSelectedAction
    {
        public string IngredientId { get; set; }

        public string CandidateActionId { get; set; }

        public IngredientSelectableAction Action { get; set; }

        public IngredientActionScope Scope { get; set; }

        public string SnapshotDigest { get; set; }

        public string SelectedBy { get; set; }

        public DateTimeOffset? SelectedAtUtc { get; set; }

        public string ApprovalReference { get; set; }
    }

    public sealed class PageIngredientActionSelectionReceipt
    {
        public const string ContractVersion = "pnp-ingredient-action-selection-receipt/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public string IngredientId { get; set; }

        public string SnapshotDigest { get; set; }

        public string CandidateSetDigest { get; set; }

        public string CandidateActionId { get; set; }

        public IngredientSelectableAction Action { get; set; }

        public string PolicyId { get; set; }

        public string PolicyVersion { get; set; }

        public string ReasonCode { get; set; }

        public IngredientActionScope Scope { get; set; }

        public IngredientDependencyEffect DependencyEffect { get; set; }

        public IngredientComparisonRule ComparisonRule { get; set; }

        public string SelectedBy { get; set; }

        public DateTimeOffset? SelectedAtUtc { get; set; }

        public string ApprovalReference { get; set; }

        public string ReceiptDigest { get; set; }
    }

    public sealed class ProtectedAssetActionPlan
    {
        public const string ContractVersion = "pnp-protected-asset-action-plan/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public string SourceSnapshotDigest { get; set; }

        public IList<PageIngredientAction> Actions { get; set; } = new List<PageIngredientAction>();
    }
}
