using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public enum SharedTopologyIdentityBasis
    {
        CapturedSourceWeb = 1,
        ExactRelativePath = 2,
        TargetSiteRoot = 3
    }

    public enum SourceWebFidelityState
    {
        Captured = 1,
        AuthorizationBlocked = 2,
        Unavailable = 3
    }

    public enum TargetWebProvisioningValueSource
    {
        ExplicitTargetPolicy = 1,
        DerivedFromTargetPathSegment = 2
    }

    public enum TargetWebTitlePolicy
    {
        DeriveFromPathSegment = 1,
        RequireExplicitOverride = 2
    }

    public enum TargetWebCollisionPolicy
    {
        Block = 1,
        StableSuffix = 2
    }

    public enum TargetWebContainerState
    {
        TargetInspectionRequired = 1,
        ReuseOwned = 2,
        CreateMissing = 3,
        ReuseExplicitApprovedHost = 4,
        RecoverInterruptedCreate = 5,
        AuthorizationBlocked = 6,
        RetryRequired = 7,
        CollisionBlocked = 8,
        SkippedByDependency = 9
    }

    public enum SharedTopologyActionKind
    {
        ReuseOwned = 1,
        CreateMissing = 2,
        ReuseExplicitApprovedHost = 3,
        RecoverInterruptedCreate = 4,
        SkipByDependency = 5,
        Block = 6
    }

    public enum SharedTopologyOwnership
    {
        MigrationOwned = 1,
        ExternalApprovedHost = 2
    }

    /// <summary>
    /// Retains the source facts that remain trustworthy when ancestor-Web capture
    /// fails. It deliberately contains no inferred parent identity or Web metadata.
    /// </summary>
    public sealed class PathDerivedSourceTopologyEvidence
    {
        public string SchemaVersion { get; set; } = "pnp-path-derived-source-topology-evidence/v1";

        public Guid SourceSiteId { get; set; }

        public string SourceSiteCollectionUrl { get; set; }

        public string SourceSiteServerRelativeUrl { get; set; }

        public Guid SourceLeafWebId { get; set; }

        public string SourceLeafWebUrl { get; set; }

        public string SourceLeafWebServerRelativeUrl { get; set; }

        public SourceWebFidelityState FidelityState { get; set; }

        public LiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public IList<string> Diagnostics { get; set; } = new List<string>();

        public string EvidenceSha256 { get; set; }
    }

    public sealed class TargetWebProvisioningOverride
    {
        public string SourceRelativePath { get; set; }

        public string TargetTitle { get; set; }

        public string TargetTemplate { get; set; }

        public int? TargetConfiguration { get; set; }

        public bool? UseSamePermissionsAsParentWeb { get; set; }
    }

    public sealed class TargetWebApprovedHost
    {
        public string SourceRelativePath { get; set; }

        public Guid ExpectedTargetWebId { get; set; }
    }

    /// <summary>
    /// Explicit target-only creation policy. None of these values are represented
    /// as captured source metadata.
    /// </summary>
    public sealed class PathDerivedTargetWebProvisioningPolicy
    {
        public string SchemaVersion { get; set; } = "pnp-path-derived-target-web-policy/v1";

        public TargetWebTitlePolicy TitlePolicy { get; set; } = TargetWebTitlePolicy.DeriveFromPathSegment;

        public string DefaultTargetTemplate { get; set; }

        public int DefaultTargetConfiguration { get; set; }

        public int DefaultTargetLanguage { get; set; } = 1033;

        public bool DefaultUseSamePermissionsAsParentWeb { get; set; } = true;

        public TargetWebCollisionPolicy CollisionPolicy { get; set; } = TargetWebCollisionPolicy.Block;

        public IList<TargetWebProvisioningOverride> Overrides { get; set; } = new List<TargetWebProvisioningOverride>();

        public IList<TargetWebApprovedHost> ApprovedExistingWebs { get; set; } = new List<TargetWebApprovedHost>();
    }

    public sealed class PathDerivedTopologyPlanningRequest
    {
        public PathDerivedSourceTopologyEvidence Source { get; set; }

        public string TargetSiteCollectionUrl { get; set; }

        public string TargetSiteServerRelativeUrl { get; set; }

        public Guid? ExpectedTargetSiteId { get; set; }

        public PathDerivedTargetWebProvisioningPolicy ProvisioningPolicy { get; set; }

        /// <summary>
        /// Fresh target inventory used only to resolve a reviewed collision policy
        /// before the plan is sealed. Empty inventory never adds a suffix.
        /// </summary>
        public IList<string> ConfirmedForeignCollisionServerRelativeUrls { get; set; } = new List<string>();
    }

    public sealed class SourceWebFidelityIngredientPlan
    {
        public string IngredientId { get; set; }

        public SharedTopologyIdentityBasis IdentityBasis { get; set; } = SharedTopologyIdentityBasis.CapturedSourceWeb;

        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public string SourceWebUrl { get; set; }

        public string SourceServerRelativeUrl { get; set; }

        public SourceWebFidelityState State { get; set; }

        public LiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public string EvidenceSha256 { get; set; }
    }

    public sealed class TargetSiteCollectionIngredientPlan
    {
        public string IngredientId { get; set; }

        public SharedTopologyIdentityBasis IdentityBasis { get; set; } = SharedTopologyIdentityBasis.TargetSiteRoot;

        public string TargetSiteCollectionUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public Guid? ExpectedTargetSiteId { get; set; }
    }

    public sealed class TargetWebContainerProvisioningValues
    {
        public string Title { get; set; }

        public TargetWebProvisioningValueSource TitleSource { get; set; }

        public string Template { get; set; }

        public TargetWebProvisioningValueSource TemplateSource { get; set; } = TargetWebProvisioningValueSource.ExplicitTargetPolicy;

        public int Configuration { get; set; }

        public TargetWebProvisioningValueSource ConfigurationSource { get; set; } = TargetWebProvisioningValueSource.ExplicitTargetPolicy;

        public int Language { get; set; }

        public TargetWebProvisioningValueSource LanguageSource { get; set; } = TargetWebProvisioningValueSource.ExplicitTargetPolicy;

        public bool UseSamePermissionsAsParentWeb { get; set; }

        public TargetWebProvisioningValueSource PermissionsSource { get; set; } = TargetWebProvisioningValueSource.ExplicitTargetPolicy;

        public IList<string> ExpectedMetadataDifferences { get; set; } = new List<string>();
    }

    public sealed class TargetWebContainerIngredientPlan
    {
        public string IngredientId { get; set; }

        public string TargetSlotKey { get; set; }

        public string ActionSignatureDigest { get; set; }

        public string GlobalActionKey { get; set; }

        public string OriginalIdentifier { get; set; }

        public SharedTopologyIdentityBasis IdentityBasis { get; set; } = SharedTopologyIdentityBasis.ExactRelativePath;

        public string ParentIngredientId { get; set; }

        public string ParentGlobalActionKey { get; set; }

        public string SourceRelativePath { get; set; }

        public string SourcePathSegment { get; set; }

        public string PreferredTargetWebUrl { get; set; }

        public string PreferredTargetServerRelativeUrl { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string TargetParentWebUrl { get; set; }

        public Guid? ExpectedTargetSiteId { get; set; }

        public bool CollisionResolved { get; set; }

        public string CollisionResolutionReason { get; set; }

        public Guid? ApprovedExistingTargetWebId { get; set; }

        public TargetWebContainerProvisioningValues Provisioning { get; set; }

        public string IngredientDigest { get; set; }
    }

    public sealed class SourceWebTargetContainerBinding
    {
        public Guid SourceSiteId { get; set; }

        public Guid SourceWebId { get; set; }

        public string SourceWebUrl { get; set; }

        public string SourceServerRelativeUrl { get; set; }

        public string TargetContainerIngredientId { get; set; }

        public string TargetGlobalActionKey { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }
    }

    public sealed class SharedTopologyPlan
    {
        public const string CurrentSchemaVersion = "pnp-shared-topology-plan/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public TargetSiteCollectionIngredientPlan TargetSite { get; set; }

        public IList<SourceWebFidelityIngredientPlan> SourceWebFidelityIngredients { get; set; } = new List<SourceWebFidelityIngredientPlan>();

        public IList<TargetWebContainerIngredientPlan> TargetWebContainers { get; set; } = new List<TargetWebContainerIngredientPlan>();

        public IList<SourceWebTargetContainerBinding> SourceWebBindings { get; set; } = new List<SourceWebTargetContainerBinding>();

        public string SupportCohortSignature { get; set; }

        public string PlanDigest { get; set; }
    }

    public sealed class SharedTopologyPlanBuildResult
    {
        public SharedTopologyPlan Plan { get; set; }

        public IList<MigrationIssue> Issues { get; set; } = new List<MigrationIssue>();

        public bool IsExecutable => Plan != null && Issues.All(value => value.Severity != MigrationIssueSeverity.Blocker
            && value.Severity != MigrationIssueSeverity.Error);
    }
}
