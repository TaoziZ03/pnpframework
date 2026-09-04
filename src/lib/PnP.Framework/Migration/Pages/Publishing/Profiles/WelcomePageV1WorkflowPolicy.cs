using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class WelcomePageV1WorkflowPolicy
    {
        public static readonly PublishingPageWorkflowPolicy Instance = new PublishingPageWorkflowPolicy
        {
            WorkflowId = WelcomePageV1CohortPolicy.CohortId,
            PreferredTargetPageLayoutFileName = "BlankWebPartPage.aspx",
            FieldsHandledByPageWriter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ContentTypeId",
                "FileLeafRef",
                "PublishingPageContent",
                "PublishingPageLayout",
                "Title"
            },
            RecognizedPageFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            AssessValidationCohort = WelcomePageV1CohortPolicy.Assess
        };
    }
}

