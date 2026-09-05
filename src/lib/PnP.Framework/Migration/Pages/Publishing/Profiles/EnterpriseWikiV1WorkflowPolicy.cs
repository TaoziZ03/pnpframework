using PnP.Framework.Migration.Evidence;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class EnterpriseWikiV1WorkflowPolicy
    {
        public static PublishingPageWorkflowPolicy Instance { get; } = new PublishingPageWorkflowPolicy(
            EnterpriseWikiV1CohortPolicy.CohortId,
            "Enterprise Wiki Page",
            BuiltInContentTypeId.EnterpriseWikiPage,
            "EnterpriseWiki.aspx",
            new[]
            {
                "EnterpriseWiki.aspx"
            },
            new[]
            {
                "ContentTypeId",
                "FileLeafRef",
                "PublishingPageContent",
                "PublishingPageLayout",
                "Title"
            },
            new[]
            {
                "OOCLReference",
                "PublishingContact",
                "PublishingContactName",
                "PublishingContactEmail",
                "PublishingContactPicture",
                "PublishingPageDescription",
                "PublishingPageImage",
                "PublishingRollupImage",
                "PublishingStartDate",
                "PublishingExpirationDate",
                "Wiki_x0020_Page_x0020_Categories",
                "SeoBrowserTitle",
                "SeoKeywords",
                "SeoMetaDescription",
                "SeoRobotsNoIndex"
            },
            EnterpriseWikiV1CohortPolicy.Assess);
    }
}
