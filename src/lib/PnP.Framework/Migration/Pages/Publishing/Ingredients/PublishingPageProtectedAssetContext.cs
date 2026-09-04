using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal sealed class PublishingPageProtectedAssetContext
    {
        public Guid SourceWebId { get; set; }

        public Guid SourceListId { get; set; }

        public int SourceItemId { get; set; }

        public ListDocumentSnapshot Document { get; set; }

        public ProtectedAssetCapturePolicy Policy { get; set; }

        public IEnumerable<PageIngredientKind> Kinds()
        {
            yield return PageIngredientKind.ProtectedAsset;
            yield return PageIngredientKind.DocumentIdentity;
            yield return PageIngredientKind.BinaryPayload;
            if (Document.InformationProtection?.State == ProtectedAssetProtectionState.Protected)
            {
                yield return PageIngredientKind.InformationProtectionRelationship;
            }
        }

        public string IngredientId(PageIngredientKind kind)
        {
            switch (kind)
            {
                case PageIngredientKind.ProtectedAsset:
                    return PublishingPageIngredientIds.ProtectedAsset(SourceWebId, SourceListId, SourceItemId);
                case PageIngredientKind.DocumentIdentity:
                    return PublishingPageIngredientIds.DocumentIdentity(SourceWebId, SourceListId, SourceItemId);
                case PageIngredientKind.BinaryPayload:
                    return PublishingPageIngredientIds.BinaryPayload(SourceWebId, SourceListId, SourceItemId);
                case PageIngredientKind.InformationProtectionRelationship:
                    return PublishingPageIngredientIds.InformationProtectionRelationship(SourceWebId, SourceListId, SourceItemId);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static IEnumerable<PublishingPageProtectedAssetContext> Create(PublishingPageCaptureBundle snapshot)
        {
            var policy = snapshot.CapturePolicy?.ProtectedAssets ?? ProtectedAssetCapturePolicy.MicrosoftTenant();
            return snapshot.ListDependencies
                .Where(value => value != null)
                .SelectMany(list => list.Items.Where(item => item?.Document != null
                        && ProtectedAssetCaptureGate.IsControlledAsset(item.Document))
                    .Select(item => new PublishingPageProtectedAssetContext
                    {
                        SourceWebId = list.SourceWebId,
                        SourceListId = list.SourceListId,
                        SourceItemId = item.SourceItemId,
                        Document = item.Document,
                        Policy = policy
                    }));
        }
    }
}
