using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;

namespace PnP.Framework.Migration.Ingredients.ResourceScript
{
    internal sealed class ResourceScriptIdentityHandler
        : PublishingPageIngredientHandler<PublishingPageJsLinkReferenceEvidence>
    {
        public const string HandlerId = "pnp.resource-script.identity/v1";

        public override PageIngredientHandlerDescriptor Descriptor { get; } = new PageIngredientHandlerDescriptor(
            HandlerId,
            new PageIngredientLaneDescriptor(ResourceScriptEvidenceNormalizer.Lane, new[] { "classic-wiki", "publishing" }),
            new[] { PublishingPageJsLinkReferenceEvidence.SchemaVersion },
            PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
            700,
            new[]
            {
                new PageIngredientIdOwnership(
                    PageIngredientIdOwnershipKind.Prefix,
                    PublishingPageJsLinkReferenceEvidence.IngredientIdPrefix)
            });

        protected override void ProjectGraph(
            PublishingPageIngredientGraphProjectionContext context,
            PublishingPageIngredientEvidenceEnvelope envelope,
            PublishingPageJsLinkReferenceEvidence evidence)
        {
            context.AddPersistedJsLinkReference(envelope, evidence);
        }
    }

    internal static class ResourceScriptIngredientCatalog
    {
        public static PublishingPageIngredientHandlerCatalog Create()
        {
            return new PublishingPageIngredientHandlerCatalog(new PublishingPageIngredientHandler[]
            {
                new ResourceScriptIdentityHandler()
            });
        }
    }
}
