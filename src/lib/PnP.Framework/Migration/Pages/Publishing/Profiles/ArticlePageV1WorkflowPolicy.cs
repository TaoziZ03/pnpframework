using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class ArticlePageV1WorkflowPolicy
    {
        public static readonly PublishingPageWorkflowPolicy Instance = new PublishingPageWorkflowPolicy
        {
            WorkflowId = ArticlePageV1CohortPolicy.CohortId,
            PreferredTargetPageLayoutFileName = "ArticleLeft.aspx",
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
                "ArticleByLine",
                "ArticleStartDate",
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
                "Comments"
            },
            AssessValidationCohort = ArticlePageV1CohortPolicy.Assess
        };
    }
}

