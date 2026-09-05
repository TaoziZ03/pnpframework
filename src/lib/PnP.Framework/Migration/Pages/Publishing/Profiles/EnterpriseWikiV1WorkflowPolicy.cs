using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class EnterpriseWikiV1WorkflowPolicy
    {
        public static readonly PublishingPageWorkflowPolicy Instance = new PublishingPageWorkflowPolicy
        {
            WorkflowId = EnterpriseWikiV1CohortPolicy.CohortId,
            PreferredTargetPageLayoutFileName = "EnterpriseWiki.aspx",
            StockPageLayoutFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "EnterpriseWiki.aspx"
            },
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
                "WikiCategories",
                "SeoBrowserTitle",
                "SeoKeywords",
                "SeoMetaDescription",
                "SeoRobotsNoIndex"
            },
            AssessValidationCohort = EnterpriseWikiV1CohortPolicy.Assess
        };
    }
}
