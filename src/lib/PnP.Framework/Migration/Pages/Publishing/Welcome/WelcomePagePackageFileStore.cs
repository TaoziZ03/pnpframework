using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using System;

namespace PnP.Framework.Migration.Pages.Publishing.Welcome
{
    public static class WelcomePagePackageFileStore
    {
        public const string DefaultExportFileName = "welcome-page-export.json";

        public const string DefaultPackageFileName = "welcome-page-package.json";

        public const string DefaultAssessmentFileName = "welcome-page-assessment.json";

        public const string DefaultReportFileName = "welcome-page-report.md";

        public const string DefaultReceiptFileName = "welcome-page-import-receipt.json";

        public static string SaveExport(string path, PublishingPageExportPackage package, bool overwrite = false)
            => PublishingPagePackageFileStore.SaveExport(path, package, null, overwrite, DefaultExportFileName);

        public static string SaveExport(
            string path,
            PublishingPageExportPackage package,
            IMigrationArtifactStore artifactStore,
            bool overwrite = false)
            => PublishingPagePackageFileStore.SaveExport(path, package, artifactStore, overwrite, DefaultExportFileName);

        public static PublishingPageExportPackage LoadExport(string path)
            => PublishingPagePackageFileStore.LoadExport(path, null, DefaultExportFileName, "Welcome Page export");

        public static PublishingPageExportPackage LoadExport(string path, IMigrationArtifactStore artifactStore)
            => PublishingPagePackageFileStore.LoadExport(path, artifactStore, DefaultExportFileName, "Welcome Page export");

        public static string SaveAssessment(
            string path,
            PublishingPageMigrationAssessment assessment,
            bool overwrite = false)
            => PublishingPagePackageFileStore.SaveAssessment(path, assessment, overwrite, DefaultAssessmentFileName);

        public static PublishingPageMigrationAssessment LoadAssessment(string path)
            => PublishingPagePackageFileStore.LoadAssessment(path, DefaultAssessmentFileName, "Welcome Page assessment");

        public static string SaveMigration(string path, PublishingPageMigrationPackage package, bool overwrite = false)
            => PublishingPagePackageFileStore.SaveMigration(path, package, null, overwrite, DefaultPackageFileName, DefaultReportFileName);

        public static string SaveMigration(
            string path,
            PublishingPageMigrationPackage package,
            IMigrationArtifactStore artifactStore,
            bool overwrite = false)
            => PublishingPagePackageFileStore.SaveMigration(path, package, artifactStore, overwrite, DefaultPackageFileName, DefaultReportFileName);

        public static PublishingPageMigrationPackage LoadMigration(string path)
            => PublishingPagePackageFileStore.LoadMigration(path, null, DefaultPackageFileName, "Welcome Page migration package");

        public static PublishingPageMigrationPackage LoadMigration(string path, IMigrationArtifactStore artifactStore)
            => PublishingPagePackageFileStore.LoadMigration(path, artifactStore, DefaultPackageFileName, "Welcome Page migration package");

        public static string SaveReceipt(string path, PublishingPageImportReceipt receipt, bool overwrite = false)
            => PublishingPagePackageFileStore.SaveReceipt(path, receipt, overwrite, DefaultReceiptFileName);
    }
}
