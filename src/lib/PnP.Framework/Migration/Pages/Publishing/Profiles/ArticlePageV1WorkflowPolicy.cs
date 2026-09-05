using PnP.Framework.Migration.Evidence;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class ArticlePageV1WorkflowPolicy
    {
        public static PublishingPageWorkflowPolicy Instance { get; } = new PublishingPageWorkflowPolicy(
            ArticlePageV1CohortPolicy.CohortId,
            "Article Page",
            BuiltInContentTypeId.ArticlePage,
            "ArticleLeft.aspx",
            new[]
            {
                "ArticleLeft.aspx",
                "ArticleRight.aspx",
                "ArticleLinks.aspx",
                "PageFromDocLayout.aspx"
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
                "ArticleByLine",
                "ArticleStartDate",
                "PublishingContact",
                "PublishingContactName",
                "PublishingContactEmail",
                "PublishingContactPicture",
                "PublishingPageDescription",
                "PublishingPageImage",
                "PublishingRollupImage",
                "SummaryLinks",
                "PublishingStartDate",
                "PublishingExpirationDate",
                "SeoBrowserTitle",
                "SeoKeywords",
                "SeoMetaDescription",
                "SeoRobotsNoIndex",
                "Comments"
            },
            ArticlePageV1CohortPolicy.Assess);
    }
}
