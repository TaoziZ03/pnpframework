using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using System;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Discovery
{
    public static class ClassicWikiPageDiscovery
    {
        public static bool IsClassicWikiContentType(string contentTypeId)
        {
            if (string.IsNullOrWhiteSpace(contentTypeId)) return false;
            return contentTypeId.StartsWith(ClassicWikiPackageContract.DefaultContentTypeId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsClassicWikiPage(string inherits, string contentTypeId)
        {
            if (!string.IsNullOrWhiteSpace(inherits)
                && inherits.IndexOf("WikiEditPage", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return IsClassicWikiContentType(contentTypeId);
        }

        public static bool IsClassicWikiLibrary(int baseTemplate)
        {
            return baseTemplate == ClassicWikiPackageContract.DefaultLibraryTemplate; // 119
        }
    }
}
