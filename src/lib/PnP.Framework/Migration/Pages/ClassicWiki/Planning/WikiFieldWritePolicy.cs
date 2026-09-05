using PnP.Framework.Migration.Pages.Packaging;
using System;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Planning
{
    public static class WikiFieldWritePolicy
    {
        public static WikiFieldWritePlan Build(string sourceValue)
        {
            var value = sourceValue ?? string.Empty;
            return new WikiFieldWritePlan
            {
                ExactValue = value,
                EntitySafeValue = ToEntitySafeLiteralBrackets(value),
                ExpectedStoredSha256 = PageDigest.ComputeSha256(value)
            };
        }

        public static string ToEntitySafeLiteralBrackets(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("[[", "&#91;&#91;")
                .Replace("]]", "&#93;&#93;");
        }
    }
}
