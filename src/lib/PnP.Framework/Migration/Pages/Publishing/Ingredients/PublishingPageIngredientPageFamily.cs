using PnP.Framework.Migration.Pages.Publishing.Capture;
using System;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageIngredientPageFamily
    {
        public const string EnterpriseWikiV1 = "enterprise-wiki/v1";
        public const string ProjectPageV1 = "project-page/v1";
        public const string ArticlePageV1 = "article-page/v1";
        public const string WelcomePageV1 = "welcome-page/v1";

        public static string Resolve(PublishingPageCaptureBundle snapshot)
        {
            var contentTypeId = snapshot?.Source?.ContentTypeId;
            if (string.IsNullOrWhiteSpace(contentTypeId))
            {
                return null;
            }
            if (contentTypeId.StartsWith(BuiltInContentTypeId.ProjectPage, StringComparison.OrdinalIgnoreCase))
            {
                return ProjectPageV1;
            }
            if (contentTypeId.StartsWith(BuiltInContentTypeId.EnterpriseWikiPage, StringComparison.OrdinalIgnoreCase))
            {
                return EnterpriseWikiV1;
            }
            if (contentTypeId.StartsWith(BuiltInContentTypeId.ArticlePage, StringComparison.OrdinalIgnoreCase))
            {
                return ArticlePageV1;
            }
            if (contentTypeId.StartsWith(BuiltInContentTypeId.WelcomePage, StringComparison.OrdinalIgnoreCase))
            {
                return WelcomePageV1;
            }
            return null;
        }
    }
}
