using System;

namespace PnP.Framework.Migration.Pages.Publishing.Layouts
{
    internal static class PublishingPageLayoutServerFailure
    {
        public static bool IsMissing(int serverErrorCode, string serverErrorTypeName)
        {
            return serverErrorCode == -2147024894
                || string.Equals(
                    serverErrorTypeName,
                    "System.IO.FileNotFoundException",
                    StringComparison.Ordinal);
        }

        public static bool IsAccessDenied(int serverErrorCode, string serverErrorTypeName)
        {
            return !IsMissing(serverErrorCode, serverErrorTypeName)
                && serverErrorCode == -2147024891;
        }
    }
}
