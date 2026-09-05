using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public enum SharedTopologyActionExecutionOutcome
    {
        Applied = 1,
        AlreadySatisfied = 2,
        RecoveredInterruptedCreate = 3,
        ReusedExternal = 4,
        OutcomeUnknownButConverged = 5,
        AuthorizationBlocked = 6,
        SkippedByDependency = 7
    }

    public sealed class PathDerivedTargetWebObservation
    {
        public string LogicalActionKey { get; set; }

        public int? HttpStatusCode { get; set; }

        public BoundLiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public bool InspectionFailed { get; set; }

        public bool IdentityConflict { get; set; }

        public bool Exists { get; set; }

        public Guid? TargetSiteId { get; set; }

        public Guid? TargetWebId { get; set; }

        public Guid? TargetParentWebId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string ExistingTitle { get; set; }

        public string ExistingTemplate { get; set; }

        public int? ExistingConfiguration { get; set; }

        public int? ExistingLanguage { get; set; }

        public bool? ExistingHasUniqueRoleAssignments { get; set; }

        public string ExistingDescription { get; set; }

        public string ExistingOriginalIdentifier { get; set; }

        public string ExistingMappingDigest { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class PathDerivedTargetWebProbe
    {
        public string TargetSlotKey { get; set; }

        public string LogicalActionKey { get; set; }

        public string ParentLogicalActionKey { get; set; }

        public TargetWebContainerState State { get; set; }

        public SharedTopologyOwnership? Ownership { get; set; }

        public Guid? TargetSiteId { get; set; }

        public Guid? TargetWebId { get; set; }

        public Guid? TargetParentWebId { get; set; }

        public string ObservedOriginalIdentifier { get; set; }

        public string ObservedMappingDigest { get; set; }

        public string ObservedTitle { get; set; }

        public string ObservedDescription { get; set; }

        public string ObservedTemplate { get; set; }

        public int? ObservedConfiguration { get; set; }

        public int? ObservedLanguage { get; set; }

        public bool? ObservedHasUniqueRoleAssignments { get; set; }

        public string ObservedStateDigest { get; set; }

        public BoundLiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public IList<string> CauseLogicalActionKeys { get; set; } = new List<string>();

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public bool IsExecutable => State == TargetWebContainerState.CreateMissing
            || State == TargetWebContainerState.ReuseOwned
            || State == TargetWebContainerState.ReuseExplicitApprovedHost
            || State == TargetWebContainerState.RecoverInterruptedCreate;
    }

    public sealed class SharedTopologyGlobalTargetAnalysis
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-global-target-analysis/v4";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string GlobalActionDagDigest { get; set; }

        public IList<PathDerivedTargetWebProbe> Probes { get; set; } = new List<PathDerivedTargetWebProbe>();

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public string AnalysisDigest { get; set; }

        public bool IsExecutable => Probes.All(value => value.IsExecutable
                || value.State == TargetWebContainerState.AuthorizationBlocked
                || value.State == TargetWebContainerState.SkippedByDependency)
            && Issues.All(value => value.Severity != MigrationIssueSeverity.Error
                && (value.Severity != MigrationIssueSeverity.Blocker
                    || string.Equals(value.Code, "PathDerivedTargetAuthorizationBlocked", StringComparison.Ordinal)
                    || string.Equals(value.Code, "PathDerivedTargetParentBlocked", StringComparison.Ordinal)));
    }

    public sealed class SharedTopologyGlobalAction
    {
        public string TargetSlotKey { get; set; }

        public string LogicalActionKey { get; set; }

        public string ParentLogicalActionKey { get; set; }

        public MigrationActionSignature ExecutionGrant { get; set; }

        public SharedTopologyActionKind SelectedAction { get; set; }

        public TargetWebContainerState ReviewedState { get; set; }

        public SharedTopologyOwnership ExpectedOwnership { get; set; }

        public Guid? ApprovedExistingTargetWebId { get; set; }

        public string Reason { get; set; }
    }

    public sealed class SharedTopologyGlobalActionPlan
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-global-action-plan/v4";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string GlobalActionDagDigest { get; set; }

        public string TargetAnalysisDigest { get; set; }

        public IList<SharedTopologyGlobalAction> Actions { get; set; } = new List<SharedTopologyGlobalAction>();

        public string ActionPlanDigest { get; set; }

        public bool IsExecutable => Actions.All(value => value.SelectedAction != SharedTopologyActionKind.Block);
    }

    public sealed class SharedTopologyGlobalActionReceipt
    {
        public string TargetSlotKey { get; set; }

        public string LogicalActionKey { get; set; }

        public string ExecutionGrantSignature { get; set; }

        public SharedTopologyActionKind SelectedAction { get; set; }

        public TargetWebContainerState FinalState { get; set; }

        public SharedTopologyOwnership Ownership { get; set; }

        public Guid TargetSiteId { get; set; }

        public Guid TargetWebId { get; set; }

        public Guid TargetParentWebId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string ObservedOriginalIdentifier { get; set; }

        public string ObservedMappingDigest { get; set; }

        public string ObservedTitle { get; set; }

        public string ObservedDescription { get; set; }

        public string ObservedTemplate { get; set; }

        public int ObservedConfiguration { get; set; }

        public int ObservedLanguage { get; set; }

        public bool ObservedHasUniqueRoleAssignments { get; set; }

        public string ObservedStateDigest { get; set; }

        public bool MutationAttempted { get; set; }

        public SharedTopologyActionExecutionOutcome ExecutionOutcome { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public MigrationMutationVerificationReceipt VerificationCheckpoint { get; set; }

        public string Diagnostic { get; set; }

        public string ReceiptDigest { get; set; }
    }

    public sealed class SharedTopologyGlobalTerminalActionReceipt
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-global-terminal-action-receipt/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string TargetSlotKey { get; set; }

        public string LogicalActionKey { get; set; }

        public string ExecutionGrantSignature { get; set; }

        public SharedTopologyActionKind SelectedAction { get; set; }

        public TargetWebContainerState FinalState { get; set; }

        public SharedTopologyActionExecutionOutcome ExecutionOutcome { get; set; }

        public BoundLiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public IList<string> CauseLogicalActionKeys { get; set; } = new List<string>();

        public string Diagnostic { get; set; }

        public string ReceiptDigest { get; set; }
    }

    public sealed class SharedTopologyGlobalMaterializationReceipt
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-global-receipt/v4";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Guid OperationId { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public IList<string> SourcePlanDigests { get; set; } = new List<string>();

        public string GlobalActionDagDigest { get; set; }

        public string ActionPlanDigest { get; set; }

        public IList<string> ExecutionGroupDigests { get; set; } = new List<string>();

        public IList<string> SupportCohortDigests { get; set; } = new List<string>();

        public IList<SharedTopologyGlobalActionReceipt> Actions { get; set; } = new List<SharedTopologyGlobalActionReceipt>();

        public IList<SharedTopologyGlobalTerminalActionReceipt> TerminalActions { get; set; } = new List<SharedTopologyGlobalTerminalActionReceipt>();

        public IList<SharedTopologySourceWebMaterializationReceipt> SourceWebMappings { get; set; } = new List<SharedTopologySourceWebMaterializationReceipt>();

        public bool SourceFidelityAuthorizationLimited { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public IList<string> Diagnostics { get; set; } = new List<string>();

        public string ReceiptDigest { get; set; }
    }

    public sealed class SharedTopologySourceWebMaterializationReceipt
    {
        public string SourceOwnerKey { get; set; }

        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public string SourceWebUrl { get; set; }

        public string SourceServerRelativeUrl { get; set; }

        public string TargetLogicalActionKey { get; set; }

        public Guid TargetSiteId { get; set; }

        public Guid TargetWebId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public SharedTopologyOwnership Ownership { get; set; }

        public string ReceiptDigest { get; set; }
    }

    public sealed class SharedTopologyRequiredActionReference
    {
        public string TargetSlotKey { get; set; }

        public string LogicalActionKey { get; set; }

        public MigrationActionSignature ExecutionGrant { get; set; }

        public string OriginalIdentifier { get; set; }

        public SharedTopologyOwnership ExpectedOwnership { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }
    }

    public sealed class SharedTopologySourceFidelityReference
    {
        public string IngredientId { get; set; }

        public string SourceOwnerKey { get; set; }

        public Guid SourceWebId { get; set; }

        public string SourceWebUrl { get; set; }

        public string SourceServerRelativeUrl { get; set; }

        public SourceWebFidelityState State { get; set; }

        public BoundLiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public string AuthorizationOperation { get; set; }

        public string AuthorizationRequestUri { get; set; }

        public string EvidenceDigest { get; set; }
    }

    public sealed class SharedTopologyPageReference
    {
        public string SchemaVersion { get; set; } = "pnp-shared-topology-page-reference/v4";

        public string SharedPlanDigest { get; set; }

        public string GlobalActionDagDigest { get; set; }

        public string ActionPlanDigest { get; set; }

        public string ExecutionGroupDigest { get; set; }

        public string SupportCohortDigest { get; set; }

        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public IList<SharedTopologySourceFidelityReference> SourceFidelity { get; set; } = new List<SharedTopologySourceFidelityReference>();

        public string TargetLeafContainerIngredientId { get; set; }

        public string TargetLeafLogicalActionKey { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public IList<SharedTopologyRequiredActionReference> RequiredActions { get; set; } = new List<SharedTopologyRequiredActionReference>();
    }

    public sealed class SharedTopologyExecutionProof
    {
        public IList<SharedTopologyPlan> SourcePlans { get; set; } = new List<SharedTopologyPlan>();

        public SharedTopologyGlobalActionDag GlobalActionDag { get; set; }

        public SharedTopologyGlobalActionPlan ActionPlan { get; set; }

        public SharedTopologyGlobalMaterializationReceipt Receipt { get; set; }
    }
}
