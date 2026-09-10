using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.BehaviorInteraction
{
    internal sealed class BehaviorInteractionEndpointEvidence
    {
        public string CanonicalOwnerInstanceId { get; set; }

        public string LogicalRole { get; set; }

        public string SourceObservationLocator { get; set; }

        public bool LocatorIsCanonicalIdentity { get; set; }
    }

    internal sealed class BehaviorInteractionRuntimeBoundaryEvidence
    {
        public IList<string> Allowed { get; set; } = new List<string>();

        public IList<string> Forbidden { get; set; } = new List<string>();
    }

    internal sealed class BehaviorInteractionTypedVerdictPolicy
    {
        public string Pass { get; set; }

        public string Conditional { get; set; }

        public string Unsupported { get; set; }

        public string Unknown { get; set; }

        public string Fail { get; set; }
    }

    internal sealed class BehaviorInteractionSearchConfiguration
    {
        public bool AllowEmptySearch { get; set; }

        public bool MaintainQueryState { get; set; }

        public IList<string> QueryGroupNames { get; set; } = new List<string>();

        public string ResultsPageAddress { get; set; }

        public bool TryInplaceQuery { get; set; }

        public bool UpdatePageTitle { get; set; }

        public int MsBeforeShowingProgress { get; set; }
    }

    internal enum BehaviorInteractionProviderAvailability
    {
        Unknown = 0,
        Available = 1,
        Missing = 2,
        Cleaned = 3
    }

    internal sealed class BehaviorInteractionResultScriptProviderEvidence
    {
        public string CanonicalProviderInstanceId { get; set; }

        public string CanonicalDynamicRegionId { get; set; }

        public string PageIdentity { get; set; }

        public string PageVersion { get; set; }

        public string ProviderType { get; set; }

        public string QueryGroupName { get; set; }

        public bool UpdateAjaxNavigate { get; set; }

        public string ConfigurationDigest { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public BehaviorInteractionProviderAvailability Availability { get; set; }

        public DateTimeOffset? CleanupObservedAtUtc { get; set; }

        public string OperationReference { get; set; }

        public string MarkerReference { get; set; }
    }

    internal sealed class BehaviorInteractionSearchBoxTargetEvidence
    {
        public string CanonicalSearchBoxInstanceId { get; set; }

        public string PageIdentity { get; set; }

        public string PageVersion { get; set; }

        public BehaviorInteractionSearchConfiguration Configuration { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public string OperationReference { get; set; }

        public string MarkerReference { get; set; }
    }

    internal sealed class BehaviorInteractionResultScriptTargetMappingEvidence
    {
        public string SourceSearchBoxInstanceId { get; set; }

        public string TargetSearchBoxInstanceId { get; set; }

        public string SourceProviderInstanceId { get; set; }

        public string TargetProviderInstanceId { get; set; }

        public string SourceDynamicRegionId { get; set; }

        public string TargetDynamicRegionId { get; set; }

        public string SourcePageIdentity { get; set; }

        public string SourcePageVersion { get; set; }

        public string TargetPageIdentity { get; set; }

        public string TargetPageVersion { get; set; }

        public string TargetIdentity { get; set; }

        public string SearchBoxActionId { get; set; }

        public string ProviderActionId { get; set; }

        public string AdmittedPlanDigest { get; set; }

        public string EvidenceReference { get; set; }
    }

    internal sealed class BehaviorInteractionResultScriptLeaseEvidence
    {
        public string LeaseId { get; set; }

        public string Status { get; set; }

        public DateTimeOffset ActiveFromUtc { get; set; }

        public DateTimeOffset RetainThroughUtc { get; set; }

        public DateTimeOffset? ReleasedAtUtc { get; set; }

        public string ClaimId { get; set; }

        public string SourceVersion { get; set; }

        public string TargetIdentity { get; set; }

        public string SearchBoxInstanceId { get; set; }

        public string ProviderInstanceId { get; set; }

        public string OperationReference { get; set; }

        public string MarkerReference { get; set; }

        public string PlanDigest { get; set; }

        public string EvidenceReference { get; set; }
    }

    internal sealed class BehaviorInteractionResultScriptConsumerTopologyEvidence
    {
        public BehaviorInteractionResultScriptProviderEvidence SourceProvider { get; set; }

        public BehaviorInteractionResultScriptProviderEvidence TargetProvider { get; set; }

        public BehaviorInteractionSearchBoxTargetEvidence TargetSearchBox { get; set; }

        public BehaviorInteractionResultScriptTargetMappingEvidence TargetMapping { get; set; }

        public BehaviorInteractionResultScriptLeaseEvidence Lease { get; set; }

        public string SearchBoxQueryGroupName { get; set; }

        public string AdmittedReviewedConfigurationDigest { get; set; }

        public string AdmittedTargetConfigurationDigest { get; set; }

        public string AdmittedPlanDigest { get; set; }

        public DateTimeOffset TargetReadbackNotBeforeUtc { get; set; }

        public DateTimeOffset RuntimeFinalEvidenceAtUtc { get; set; }

        public string ProviderInventoryEvidenceReference { get; set; }
    }

    internal sealed class BehaviorInteractionSearchSubmitSourceEvidence
    {
        public RuntimeVerificationAssertion Assertion { get; set; }

        public CanonicalPageIngredientGraph IngredientGraph { get; set; }

        public IList<PageIngredientAction> IngredientActions { get; set; } =
            new List<PageIngredientAction>();

        public BehaviorInteractionEndpointEvidence Trigger { get; set; }

        public BehaviorInteractionEndpointEvidence Target { get; set; }

        public int MaximumAttempts { get; set; }

        public BehaviorInteractionRuntimeBoundaryEvidence RuntimeBoundary { get; set; }

        public BehaviorInteractionTypedVerdictPolicy TypedVerdictPolicy { get; set; }

        public BehaviorInteractionSearchConfiguration SearchConfiguration { get; set; }

        public BehaviorInteractionResultScriptConsumerTopologyEvidence ResultScriptTopology { get; set; }

        public ArtifactReference RawArtifact { get; set; }

        public string RawArtifactBase64 { get; set; }

        public string SemanticDigest { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class BehaviorInteractionMaturityEvidence
    {
        public BehaviorInteractionSearchSubmitSourceEvidence Source { get; set; }

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }
}
