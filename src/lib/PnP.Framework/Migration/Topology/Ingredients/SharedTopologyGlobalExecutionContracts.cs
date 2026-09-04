using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class PathDerivedTargetWebObservation
    {
        public string GlobalActionKey { get; set; }

        public int? HttpStatusCode { get; set; }

        public LiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

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

        public bool? ExistingHasUniqueRoleAssignments { get; set; }

        public string ExistingDescription { get; set; }

        public string ExistingOriginalIdentifier { get; set; }

        public string ExistingMappingDigest { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class PathDerivedTargetWebProbe
    {
        public string TargetSlotKey { get; set; }

        public string GlobalActionKey { get; set; }

        public string ParentGlobalActionKey { get; set; }

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

        public bool? ObservedHasUniqueRoleAssignments { get; set; }

        public LiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public IList<string> CauseGlobalActionKeys { get; set; } = new List<string>();

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public bool IsExecutable => State == TargetWebContainerState.CreateMissing
            || State == TargetWebContainerState.ReuseOwned
            || State == TargetWebContainerState.ReuseExplicitApprovedHost
            || State == TargetWebContainerState.RecoverInterruptedCreate;
    }

    public sealed class SharedTopologyGlobalTargetAnalysis
    {
        public string SchemaVersion { get; set; } = "pnp-shared-topology-global-target-analysis/v1";

        public string GlobalActionDagDigest { get; set; }

        public IList<PathDerivedTargetWebProbe> Probes { get; set; } = new List<PathDerivedTargetWebProbe>();

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public string AnalysisDigest { get; set; }

        public bool IsExecutable => Probes.All(value => value.IsExecutable)
            && Issues.All(value => value.Severity != MigrationIssueSeverity.Blocker
                && value.Severity != MigrationIssueSeverity.Error);
    }

    public sealed class SharedTopologyGlobalAction
    {
        public string TargetSlotKey { get; set; }

        public string GlobalActionKey { get; set; }

        public string ParentGlobalActionKey { get; set; }

        public string ActionSignatureDigest { get; set; }

        public SharedTopologyActionKind SelectedAction { get; set; }

        public TargetWebContainerState ReviewedState { get; set; }

        public Guid? ApprovedExistingTargetWebId { get; set; }

        public string Reason { get; set; }
    }

    public sealed class SharedTopologyGlobalActionPlan
    {
        public string SchemaVersion { get; set; } = "pnp-shared-topology-global-action-plan/v1";

        public string GlobalActionDagDigest { get; set; }

        public string TargetAnalysisDigest { get; set; }

        public IList<SharedTopologyGlobalAction> Actions { get; set; } = new List<SharedTopologyGlobalAction>();

        public string ActionPlanDigest { get; set; }

        public bool IsExecutable => Actions.All(value => value.SelectedAction != SharedTopologyActionKind.Block
            && value.SelectedAction != SharedTopologyActionKind.SkipByDependency);
    }

    public sealed class SharedTopologyGlobalActionReceipt
    {
        public string TargetSlotKey { get; set; }

        public string GlobalActionKey { get; set; }

        public string ActionSignatureDigest { get; set; }

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

        public bool ObservedHasUniqueRoleAssignments { get; set; }

        public bool ChangedTarget { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class SharedTopologyGlobalMaterializationReceipt
    {
        public string SchemaVersion { get; set; } = "pnp-shared-topology-global-receipt/v1";

        public Guid OperationId { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public string GlobalActionDagDigest { get; set; }

        public string ActionPlanDigest { get; set; }

        public IList<SharedTopologyGlobalActionReceipt> Actions { get; set; } = new List<SharedTopologyGlobalActionReceipt>();

        public IList<SharedTopologySourceWebMaterializationReceipt> SourceWebMappings { get; set; } = new List<SharedTopologySourceWebMaterializationReceipt>();

        public bool SourceFidelityAuthorizationLimited { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public IList<string> Diagnostics { get; set; } = new List<string>();

        public string ReceiptDigest { get; set; }
    }

    public sealed class SharedTopologySourceWebMaterializationReceipt
    {
        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public string TargetGlobalActionKey { get; set; }

        public Guid TargetSiteId { get; set; }

        public Guid TargetWebId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public SharedTopologyOwnership Ownership { get; set; }
    }

    public sealed class SharedTopologyPageReference
    {
        public string SchemaVersion { get; set; } = "pnp-shared-topology-page-reference/v2";

        public string SupportCohortSignature { get; set; }

        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public string SourceWebFidelityIngredientId { get; set; }

        public SourceWebFidelityState SourceFidelityState { get; set; }

        public LiteralHttpAuthorizationEvidence SourceAuthorizationEvidence { get; set; }

        public string TargetLeafContainerIngredientId { get; set; }

        public string TargetLeafGlobalActionKey { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public IList<string> RequiredGlobalActionKeys { get; set; } = new List<string>();
    }
}
