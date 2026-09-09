using System;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public static class PageIngredientKindIdentity
    {
        public static string FromLegacyKind(PageIngredientKind kind)
        {
            switch (kind)
            {
                case PageIngredientKind.Runtime: return "pnp.runtime";
                case PageIngredientKind.PageArtifact: return "pnp.page-artifact";
                case PageIngredientKind.Layout: return "pnp.layout";
                case PageIngredientKind.ContentType: return "pnp.content-type";
                case PageIngredientKind.Content: return "pnp.content";
                case PageIngredientKind.Field: return "pnp.field";
                case PageIngredientKind.WebPart: return "pnp.webpart";
                case PageIngredientKind.List: return "pnp.list";
                case PageIngredientKind.View: return "pnp.view";
                case PageIngredientKind.Asset: return "pnp.asset";
                case PageIngredientKind.Taxonomy: return "pnp.taxonomy";
                case PageIngredientKind.Reference: return "pnp.reference";
                case PageIngredientKind.Topology: return "pnp.topology";
                case PageIngredientKind.Security: return "pnp.security";
                case PageIngredientKind.Lifecycle: return "pnp.lifecycle";
                case PageIngredientKind.Service: return "pnp.service";
                case PageIngredientKind.Web: return "pnp.web";
                case PageIngredientKind.ListItem: return "pnp.list-item";
                case PageIngredientKind.Document: return "pnp.document";
                case PageIngredientKind.Attachment: return "pnp.attachment";
                case PageIngredientKind.PlatformFeature: return "pnp.platform-feature";
                case PageIngredientKind.Policy: return "pnp.policy";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "The legacy ingredient kind has no stable kind identity.");
            }
        }
    }
}
