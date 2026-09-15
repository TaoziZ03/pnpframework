using System;

namespace PnP.Framework.Modernization.Publishing
{
    internal enum PublishingPageTransformationTarget
    {
        CrossSiteCollection,
        SameWeb,
        SameSiteCollectionNotAllowed,
        InPlaceDifferentSiteCollection,
        SameSiteCollectionDifferentWeb,
        SameWebRequiresWritableSitePages,
    }

    /// <summary>
    /// Pure validation rules for choosing the publishing page transformation target mode.
    /// </summary>
    internal static class PublishingPageTransformationValidator
    {
        internal static PublishingPageTransformationTarget ValidateTarget(
            bool inPlacePublishingPage,
            Guid sourceSiteId,
            Guid targetSiteId,
            Guid sourceWebId,
            Guid targetWebId,
            bool hasWritableSitePages)
        {
            if (!inPlacePublishingPage)
            {
                return sourceSiteId != targetSiteId
                    ? PublishingPageTransformationTarget.CrossSiteCollection
                    : PublishingPageTransformationTarget.SameSiteCollectionNotAllowed;
            }

            if (sourceSiteId != targetSiteId)
            {
                return PublishingPageTransformationTarget.InPlaceDifferentSiteCollection;
            }

            if (sourceWebId != targetWebId)
            {
                return PublishingPageTransformationTarget.SameSiteCollectionDifferentWeb;
            }

            if (!hasWritableSitePages)
            {
                return PublishingPageTransformationTarget.SameWebRequiresWritableSitePages;
            }

            return PublishingPageTransformationTarget.SameWeb;
        }

        internal static bool CanOverwriteTarget(PublishingPageTransformationTarget target, bool overwriteRequested)
        {
            return target == PublishingPageTransformationTarget.CrossSiteCollection && overwriteRequested;
        }

        internal static bool UsesCrossSitePermissionSemantics(PublishingPageTransformationTarget target)
        {
            return target == PublishingPageTransformationTarget.CrossSiteCollection;
        }
    }
}
