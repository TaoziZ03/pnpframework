using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Layouts
{
    public sealed class PublishingPageNativeLayoutProfile
    {
        internal PublishingPageNativeLayoutProfile(
            string fileName,
            string title,
            string associatedContentTypeName,
            string associatedContentTypeId)
        {
            FileName = fileName;
            Title = title;
            AssociatedContentTypeName = associatedContentTypeName;
            AssociatedContentTypeId = associatedContentTypeId;
        }

        public string FileName { get; }

        public string Title { get; }

        public string AssociatedContentTypeName { get; }

        public string AssociatedContentTypeId { get; }
    }

    public static class PublishingPageNativeLayoutCatalog
    {
        private static readonly IReadOnlyDictionary<string, PublishingPageNativeLayoutProfile> Profiles =
            new Dictionary<string, PublishingPageNativeLayoutProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["EnterpriseWiki.aspx"] = new PublishingPageNativeLayoutProfile(
                    "EnterpriseWiki.aspx", "Basic Page", "Enterprise Wiki Page", BuiltInContentTypeId.EnterpriseWikiPage),
                ["BlankWebPartPage.aspx"] = new PublishingPageNativeLayoutProfile(
                    "BlankWebPartPage.aspx", "Blank Web Part page", "Welcome Page", BuiltInContentTypeId.WelcomePage),
                ["ArticleLeft.aspx"] = new PublishingPageNativeLayoutProfile(
                    "ArticleLeft.aspx", "Image on left", "Article Page", BuiltInContentTypeId.ArticlePage),
                ["ArticleRight.aspx"] = new PublishingPageNativeLayoutProfile(
                    "ArticleRight.aspx", "Image on right", "Article Page", BuiltInContentTypeId.ArticlePage),
                ["ArticleLinks.aspx"] = new PublishingPageNativeLayoutProfile(
                    "ArticleLinks.aspx", "Summary links", "Article Page", BuiltInContentTypeId.ArticlePage),
                ["PageFromDocLayout.aspx"] = new PublishingPageNativeLayoutProfile(
                    "PageFromDocLayout.aspx", "Body only", "Article Page", BuiltInContentTypeId.ArticlePage),
                ["WelcomeSplash.aspx"] = new PublishingPageNativeLayoutProfile(
                    "WelcomeSplash.aspx", "Splash", "Welcome Page", BuiltInContentTypeId.WelcomePage),
                ["WelcomeLinks.aspx"] = new PublishingPageNativeLayoutProfile(
                    "WelcomeLinks.aspx", "Summary links", "Welcome Page", BuiltInContentTypeId.WelcomePage)
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

            if (string.IsNullOrWhiteSpace(layout.Description)
                || !string.Equals(layout.Description.Trim(), candidate.Title, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(layout.AssociatedContentTypeName)
                && !string.Equals(layout.AssociatedContentTypeName, candidate.AssociatedContentTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(layout.AssociatedContentTypeId)
                && !string.Equals(
                    layout.AssociatedContentTypeId,
                    candidate.AssociatedContentTypeId,
                    StringComparison.OrdinalIgnoreCase))
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

        public static IReadOnlyCollection<PublishingPageNativeLayoutProfile> AllProfiles => Profiles.Values
            .OrderBy(value => value.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }
}
