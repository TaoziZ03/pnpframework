using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Layouts
{
    public sealed class PublishingPageNativeLayoutProfile
    {
        public string FileName { get; set; }

        public string Title { get; set; }

        public string AssociatedContentTypeName { get; set; }

        public string AssociatedContentTypeId { get; set; }
    }

    public static class PublishingPageNativeLayoutCatalog
    {
        private static readonly IReadOnlyDictionary<string, PublishingPageNativeLayoutProfile> Profiles =
            new Dictionary<string, PublishingPageNativeLayoutProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["EnterpriseWiki.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "EnterpriseWiki.aspx",
                    Title = "Basic Page",
                    AssociatedContentTypeName = "Enterprise Wiki Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.EnterpriseWikiPage
                },
                ["BlankWebPartPage.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "BlankWebPartPage.aspx",
                    Title = "Blank Web Part page",
                    AssociatedContentTypeName = "Welcome Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.WelcomePage
                },
                ["ArticleLeft.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "ArticleLeft.aspx",
                    Title = "Article Page with left image",
                    AssociatedContentTypeName = "Article Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.ArticlePage
                },
                ["ArticleRight.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "ArticleRight.aspx",
                    Title = "Article Page with right image",
                    AssociatedContentTypeName = "Article Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.ArticlePage
                },
                ["ArticleLinks.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "ArticleLinks.aspx",
                    Title = "Article Page with summary links",
                    AssociatedContentTypeName = "Article Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.ArticlePage
                },
                ["PageFromDocPack.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "PageFromDocPack.aspx",
                    Title = "Page from document pack",
                    AssociatedContentTypeName = "Article Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.ArticlePage
                },
                ["WelcomeSplash.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "WelcomeSplash.aspx",
                    Title = "Welcome Splash page",
                    AssociatedContentTypeName = "Welcome Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.WelcomePage
                },
                ["WelcomeLinks.aspx"] = new PublishingPageNativeLayoutProfile
                {
                    FileName = "WelcomeLinks.aspx",
                    Title = "Welcome Links page",
                    AssociatedContentTypeName = "Welcome Page",
                    AssociatedContentTypeId = BuiltInContentTypeId.WelcomePage
                }
            };

        public static bool TryGetUnavailableSourceSubstitution(
            PublishingPageLayoutSnapshot layout,
            string fileName,
            out PublishingPageNativeLayoutProfile profile)
        {
            profile = null;
            if (layout == null
                || layout.EvidenceState == PublishingPageLayoutEvidenceState.Readable
                || layout.Availability != EvidenceAvailability.Unavailable
                || string.IsNullOrWhiteSpace(fileName)
                || !Profiles.TryGetValue(fileName, out var candidate))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(layout.Description)
                && !string.Equals(layout.Description.Trim(), candidate.Title, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(layout.Description.Trim(), candidate.AssociatedContentTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            profile = candidate;
            return true;
        }

        public static bool TryGetProfile(string fileName, out PublishingPageNativeLayoutProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }
            return Profiles.TryGetValue(fileName, out profile);
        }

        public static IReadOnlyCollection<PublishingPageNativeLayoutProfile> AllProfiles => new List<PublishingPageNativeLayoutProfile>(Profiles.Values);
    }
}

