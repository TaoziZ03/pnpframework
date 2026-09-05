using PnP.Framework.Migration.Evidence;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class WelcomePageV1WorkflowPolicy
    {
        public static PublishingPageWorkflowPolicy Instance { get; } = new PublishingPageWorkflowPolicy(
            WelcomePageV1CohortPolicy.CohortId,
            "Welcome Page",
            BuiltInContentTypeId.WelcomePage,
            "BlankWebPartPage.aspx",
            new[]
            {
                "BlankWebPartPage.aspx",
                "WelcomeSplash.aspx",
                "WelcomeLinks.aspx"
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
                "SummaryLinks",
                "SummaryLinks2",
                "PublishingContact",
                "PublishingContactName",
                "PublishingContactEmail",
                "PublishingContactPicture",
                "PublishingPageDescription",
                "PublishingPageImage",
                "PublishingRollupImage",
                "PublishingStartDate",
                "PublishingExpirationDate",
                "SeoBrowserTitle",
                "SeoKeywords",
                "SeoMetaDescription",
                "SeoRobotsNoIndex",
                "HeaderStyle",
                "HidePhysicalUrlsFromSearch"
            },
            WelcomePageV1CohortPolicy.Assess);
    }
}
