using PnP.Framework.Migration.Pages.Assessment;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using System.IO;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientAssessmentContext
    {
        private readonly PublishingPageAssessmentAccumulator accumulator;
        private readonly PageIngredientHandlerDescriptor descriptor;

        internal PublishingPageIngredientAssessmentContext(
            PublishingPageCaptureBundle snapshot,
            CanonicalPageIngredientGraph graph,
            PublishingPageAssessmentAccumulator accumulator,
            PageIngredientHandlerDescriptor descriptor)
        {
            Snapshot = PublishingPageIngredientReadOnlyView.Clone(snapshot);
            IngredientGraph = PublishingPageIngredientReadOnlyView.Clone(graph);
            PageFamily = PublishingPageIngredientPageFamily.Resolve(snapshot);
            HandlerId = descriptor?.HandlerId;
            LaneId = descriptor?.Lane?.LaneId;
            this.accumulator = accumulator;
            this.descriptor = descriptor;
        }

        public PublishingPageCaptureBundle Snapshot { get; }

        public CanonicalPageIngredientGraph IngredientGraph { get; }

        public string PageFamily { get; }

        public string HandlerId { get; }

        public string LaneId { get; }

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
            if (!descriptor.Owns(ingredientId))
            {
                throw new InvalidDataException(
                    $"Handler '{descriptor.HandlerId}' does not own assessment ingredient '{ingredientId}'.");
            }
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
