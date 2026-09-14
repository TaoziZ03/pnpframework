using System;

namespace PnP.Framework.Modernization.Publishing
{
    internal enum PublishingPageTransformationTarget
    {
        CrossSiteCollection,
        SameWeb,
        SameSiteCollectionNotAllowed,
        SameSiteCollectionDifferentWeb,
        SameWebRequiresEnterpriseWiki,
        SameWebRequiresWritableSitePages,
    }

    /// <summary>
    /// Pure validation rules for choosing the publishing page transformation target mode.
    /// </summary>
    internal static class PublishingPageTransformationValidator
    {
        private const string EnterpriseWikiWebTemplate = "ENTERWIKI";

        internal static PublishingPageTransformationTarget ValidateTarget(
            bool inPlacePublishingPage,
            Guid sourceSiteId,
            Guid targetSiteId,
            Guid sourceWebId,
            Guid targetWebId,
            string targetWebTemplate,
            bool hasWritableSitePages)
        {
            if (sourceSiteId != targetSiteId)
            {
                return PublishingPageTransformationTarget.CrossSiteCollection;
            }

            if (sourceWebId != targetWebId)
            {
                return PublishingPageTransformationTarget.SameSiteCollectionDifferentWeb;
            }

            if (!inPlacePublishingPage)
            {
                return PublishingPageTransformationTarget.SameSiteCollectionNotAllowed;
            }

            if (!string.Equals(targetWebTemplate, EnterpriseWikiWebTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return PublishingPageTransformationTarget.SameWebRequiresEnterpriseWiki;
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
