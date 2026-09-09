using PnP.Framework.Migration.Pages.Assessment;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientAssessmentContext
    {
        private readonly PublishingPageAssessmentAccumulator accumulator;

        internal PublishingPageIngredientAssessmentContext(
            PublishingPageCaptureBundle snapshot,
            CanonicalPageIngredientGraph graph,
            PublishingPageAssessmentAccumulator accumulator)
        {
            Snapshot = snapshot;
            IngredientGraph = graph;
            this.accumulator = accumulator;
        }

        public PublishingPageCaptureBundle Snapshot { get; }

        public CanonicalPageIngredientGraph IngredientGraph { get; }

        public void AddAssessment(
            string ingredientId,
            PageIngredientAssessmentState state,
            IngredientCapability capability,
            IngredientDisposition proposedDisposition,
            string proposedRealization,
            string policyId,
            string reason,
            string targetIdentity = null,
            string mitigationCode = null,
            params string[] verificationAssertions)
        {
            accumulator.Add(
                ingredientId,
                state,
                capability,
                proposedDisposition,
                proposedRealization,
                policyId,
                reason,
                targetIdentity,
                mitigationCode,
                verificationAssertions);
        }
    }
}
