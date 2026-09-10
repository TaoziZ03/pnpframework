using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
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
