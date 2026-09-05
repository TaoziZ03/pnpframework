using PnP.Framework.Migration.Pages.Cohorts;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class WelcomePageV1CohortPolicy
    {
        public const string CohortId = "welcome-page-v1";

        public const string PolicyVersion = "1";

        public static ValidationCohortAssessment Assess(string contentTypeId)
        {
            if (string.IsNullOrWhiteSpace(contentTypeId))
            {
                return Result(
                    ValidationCohortDisposition.Unknown,
                    "The source ContentTypeId is unavailable, so Welcome-v1 cohort membership cannot be established.");
            }

            if (contentTypeId.StartsWith(BuiltInContentTypeId.EnterpriseWikiPage, StringComparison.OrdinalIgnoreCase))
            {
                return Result(
                    ValidationCohortDisposition.Excluded,
                    "Enterprise Wiki Content Type lineage is handled by EW-v1 and excluded from the Welcome-v1 validation cohort.");
            }

            if (contentTypeId.StartsWith(BuiltInContentTypeId.ArticlePage, StringComparison.OrdinalIgnoreCase))
            {
                return Result(
                    ValidationCohortDisposition.Excluded,
                    "Article Page Content Type lineage is handled by Article-v1 and excluded from the Welcome-v1 validation cohort.");
            }

            if (contentTypeId.StartsWith(BuiltInContentTypeId.ProjectPage, StringComparison.OrdinalIgnoreCase))
            {
                return Result(
                    ValidationCohortDisposition.Excluded,
                    "Project Page Content Type lineage is intentionally outside the Welcome-v1 validation cohort; migration capability is assessed independently.");
            }

            if (contentTypeId.StartsWith(BuiltInContentTypeId.WelcomePage, StringComparison.OrdinalIgnoreCase))
            {
                return Result(
                    ValidationCohortDisposition.Included,
                    "Welcome Page Content Type lineage is included by the Welcome-v1 validation policy.");
            }

            return Result(
                ValidationCohortDisposition.Excluded,
                "The source Content Type is outside the Welcome-v1 validation cohort.");
        }

        public static bool IsIncludedContentType(string contentTypeId)
        {
            return Assess(contentTypeId).Disposition == ValidationCohortDisposition.Included;
        }

        private static ValidationCohortAssessment Result(ValidationCohortDisposition disposition, string reason)
        {
            return new ValidationCohortAssessment
            {
                CohortId = CohortId,
                PolicyVersion = PolicyVersion,
                Disposition = disposition,
                Reasons = new List<string> { reason }
            };
        }
    }
}
