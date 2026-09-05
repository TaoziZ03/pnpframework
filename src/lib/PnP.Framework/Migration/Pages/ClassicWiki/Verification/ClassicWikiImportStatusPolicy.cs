using PnP.Framework.Migration.Verification;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    internal static class ClassicWikiImportStatusPolicy
    {
        public static RuntimeVerificationStatus RuntimeStatus => RuntimeVerificationStatus.Pending;

        public static MigrationAcceptanceStatus Acceptance(
            bool storagePassed,
            RuntimeVerificationStatus runtimeStatus,
            bool hasExplicitExclusions)
        {
            if (!storagePassed || runtimeStatus == RuntimeVerificationStatus.Failed)
            {
                return MigrationAcceptanceStatus.Rejected;
            }

            if (runtimeStatus == RuntimeVerificationStatus.Pending
                || runtimeStatus == RuntimeVerificationStatus.NotRun)
            {
                return MigrationAcceptanceStatus.Pending;
            }

            return hasExplicitExclusions
                ? MigrationAcceptanceStatus.PartiallyAccepted
                : MigrationAcceptanceStatus.Accepted;
        }
    }
}
