using PnP.Framework.Migration.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class SharedTopologyTargetSiteObservation
    {
        public int? HttpStatusCode { get; set; }

        public bool InspectionFailed { get; set; }

        public bool Exists { get; set; }

        public Guid? TargetSiteId { get; set; }

        public Guid? TargetRootWebId { get; set; }

        public string TargetSiteCollectionUrl { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class TargetWebContainerObservation
    {
        public string IngredientId { get; set; }

        public int? HttpStatusCode { get; set; }

        public bool InspectionFailed { get; set; }

        public bool Exists { get; set; }

        public Guid? TargetSiteId { get; set; }

        public Guid? TargetWebId { get; set; }

        public Guid? TargetParentWebId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string ExistingTitle { get; set; }

        public string ExistingTemplate { get; set; }

        public int? ExistingConfiguration { get; set; }

        public string ExistingIngredientId { get; set; }

        public string ExistingPlanDigest { get; set; }

        public string Diagnostic { get; set; }
    }

    public sealed class TargetWebContainerProbe
    {
        public string IngredientId { get; set; }

        public string ParentIngredientId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public TargetWebContainerState State { get; set; }

        public int? HttpStatusCode { get; set; }

        public bool Exists { get; set; }

        public Guid? TargetSiteId { get; set; }

        public Guid? TargetWebId { get; set; }

        public Guid? TargetParentWebId { get; set; }

        public bool IsMigrationOwned { get; set; }

        public IList<string> CauseIngredientIds { get; set; } = new List<string>();

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public bool IsActionable => State == TargetWebContainerState.Reuse || State == TargetWebContainerState.CreateMissing;
    }

    public sealed class SharedTopologyTargetAnalysis
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-target-analysis/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string SharedTopologyPlanDigest { get; set; }

        public SharedTopologyTargetSiteObservation TargetSite { get; set; }

        public IList<TargetWebContainerProbe> TargetWebContainers { get; set; } = new List<TargetWebContainerProbe>();

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public string AnalysisDigest { get; set; }

        public bool IsActionable => Issues.All(value => value.Severity != MigrationIssueSeverity.Blocker
                && value.Severity != MigrationIssueSeverity.Error)
            && TargetWebContainers.All(value => value.IsActionable);
    }

    public sealed class SharedTopologyIngredientAction
    {
        public string IngredientId { get; set; }

        public SharedTopologyActionKind Action { get; set; }

        public TargetWebContainerState SourceState { get; set; }

        public string ParentIngredientId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string Reason { get; set; }

        public IList<string> CauseIngredientIds { get; set; } = new List<string>();
    }

    public sealed class SharedTopologyActionPlan
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-action-plan/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string SharedTopologyPlanDigest { get; set; }

        public string TargetAnalysisDigest { get; set; }

        public IList<SharedTopologyIngredientAction> Actions { get; set; } = new List<SharedTopologyIngredientAction>();

        public string ActionPlanDigest { get; set; }

        public bool IsExecutable => Actions.All(value => value.Action == SharedTopologyActionKind.Reuse
            || value.Action == SharedTopologyActionKind.CreateMissing);
    }

    public sealed class SharedTopologyWebReceipt
    {
        public string IngredientId { get; set; }

        public Guid TargetSiteId { get; set; }

        public Guid TargetWebId { get; set; }

        public Guid TargetParentWebId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public SharedTopologyReceiptDisposition Disposition { get; set; }

        public string IngredientDigest { get; set; }
    }

    public sealed class SharedTopologyMaterializationReceipt
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-receipt/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string SharedTopologyPlanDigest { get; set; }

        public string ActionPlanDigest { get; set; }

        public IList<SharedTopologyWebReceipt> Webs { get; set; } = new List<SharedTopologyWebReceipt>();

        public bool FreshReadbackPassed { get; set; }

        public IList<string> Diagnostics { get; set; } = new List<string>();

        public string ReceiptDigest { get; set; }
    }

    public sealed class SharedTopologyVerificationResult
    {
        public string SharedTopologyPlanDigest { get; set; }

        public string ReceiptDigest { get; set; }

        public bool Passed { get; set; }

        public IList<string> Mismatches { get; set; } = new List<string>();
    }

    public sealed class SharedTopologyPageReference
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-page-reference/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string SharedTopologyPlanDigest { get; set; }

        public string TargetAnalysisDigest { get; set; }

        public string ActionPlanDigest { get; set; }

        public string SourceWebFidelityIngredientId { get; set; }

        public string TargetLeafContainerIngredientId { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public IList<string> RequiredTargetContainerIngredientIds { get; set; } = new List<string>();
    }
}
