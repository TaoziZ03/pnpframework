using PnP.Framework.Migration.Verification;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    internal static class ClassicWikiImportStatusPolicy
    {
        public static RuntimeVerificationStatus RuntimeStatus => RuntimeVerificationStatus.Pending;

        public static MigrationAcceptanceStatus Acceptance(bool storagePassed, bool hasExplicitExclusions)
        {
            if (!storagePassed)
            {
                return MigrationAcceptanceStatus.Rejected;
            }

            return hasExplicitExclusions
                ? MigrationAcceptanceStatus.PartiallyAccepted
                : MigrationAcceptanceStatus.Pending;
        }
    }
}
