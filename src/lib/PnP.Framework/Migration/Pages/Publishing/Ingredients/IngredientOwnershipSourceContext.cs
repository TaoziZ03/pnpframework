using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class IngredientOwnershipSourceContext
    {
        internal IngredientOwnershipSourceContext(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            Snapshot = snapshot;
            Node = node;
        }

        public PublishingPageCaptureBundle Snapshot { get; }

        public PageIngredientNode Node { get; }

        public bool HasBoundSourceIdentity =>
            PublishingPageIngredientSourceBinding.HasExactSourceBinding(Snapshot, Node);

        public bool HasBoundEvidence =>
            PublishingPageIngredientSourceBinding.HasBoundEvidence(Snapshot, Node);
    }
}
