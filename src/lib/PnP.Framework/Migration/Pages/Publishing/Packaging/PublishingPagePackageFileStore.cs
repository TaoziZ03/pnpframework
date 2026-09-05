using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Reporting;
using System;
using System.IO;
using System.Text;

namespace PnP.Framework.Migration.Pages.Publishing.Packaging
{
    public static class PublishingPagePackageFileStore
    {
        public const string DefaultExportFileName = "publishing-page-export.json";

        public const string DefaultPackageFileName = "publishing-page-package.json";

        public const string DefaultAssessmentFileName = "publishing-page-assessment.json";

        public const string DefaultReportFileName = "publishing-page-report.md";

        public const string DefaultReceiptFileName = "publishing-page-import-receipt.json";

        public static string SaveExport(
            string path,
            PublishingPageExportPackage package,
            IMigrationArtifactStore artifactStore = null,
            bool overwrite = false,
            string defaultFileName = DefaultExportFileName)
        {
            PublishingPagePackageValidator.ValidateExport(package, artifactStore);
            var exportPath = ResolvePath(path, defaultFileName);
            SaveText(exportPath, PublishingPagePackageSerializer.Serialize(package), overwrite);
            return exportPath;
        }

        public static PublishingPageExportPackage LoadExport(
            string path,
            IMigrationArtifactStore artifactStore = null,
            string defaultFileName = DefaultExportFileName,
            string description = "Publishing Page export")
        {
            var exportPath = ResolveExistingPath(path, defaultFileName, description);
            using var stream = OpenPackageReadStream(exportPath);
            var package = PublishingPagePackageSerializer.Deserialize<PublishingPageExportPackage>(stream);
            PublishingPagePackageValidator.ValidateExport(package, artifactStore);
            return package;
        }

        public static string SaveAssessment(
            string path,
            PublishingPageMigrationAssessment assessment,
            bool overwrite = false,
            string defaultFileName = DefaultAssessmentFileName)
        {
            PublishingPageMigrationAssessmentValidator.Validate(assessment);
            var assessmentPath = ResolvePath(path, defaultFileName);
            SaveText(assessmentPath, PublishingPagePackageSerializer.Serialize(assessment), overwrite);
            return assessmentPath;
        }

        public static PublishingPageMigrationAssessment LoadAssessment(
            string path,
            string defaultFileName = DefaultAssessmentFileName,
            string description = "Publishing Page assessment")
        {
            var assessmentPath = ResolveExistingPath(path, defaultFileName, description);
            using var stream = OpenPackageReadStream(assessmentPath);
            var assessment = PublishingPagePackageSerializer.Deserialize<PublishingPageMigrationAssessment>(stream);
            PublishingPageMigrationAssessmentValidator.Validate(assessment);
            return assessment;
        }

        public static string SaveMigration(
            string path,
            PublishingPageMigrationPackage package,
            IMigrationArtifactStore artifactStore = null,
            bool overwrite = false,
            string defaultPackageFileName = DefaultPackageFileName,
            string defaultReportFileName = DefaultReportFileName)
        {
            PublishingPagePackageValidator.ValidateMigration(package, artifactStore);
            var packagePath = ResolvePath(path, defaultPackageFileName);
            var reportPath = Path.Combine(Path.GetDirectoryName(packagePath) ?? string.Empty, defaultReportFileName);
            EnsureWritable(packagePath, overwrite);
            EnsureWritable(reportPath, overwrite);
            SaveText(packagePath, PublishingPagePackageSerializer.Serialize(package), true);
            SaveText(reportPath, PublishingPageMigrationReportBuilder.Build(package, artifactStore), true);
            return packagePath;
        }

        public static PublishingPageMigrationPackage LoadMigration(
            string path,
            IMigrationArtifactStore artifactStore = null,
            string defaultPackageFileName = DefaultPackageFileName,
            string description = "Publishing Page migration package")
        {
            var packagePath = ResolveExistingPath(path, defaultPackageFileName, description);
            using var stream = OpenPackageReadStream(packagePath);
            var package = PublishingPagePackageSerializer.Deserialize<PublishingPageMigrationPackage>(stream);
            PublishingPagePackageValidator.ValidateMigration(package, artifactStore);
            return package;
        }

        public static string SaveReceipt(
            string path,
            PublishingPageImportReceipt receipt,
            bool overwrite = false,
            string defaultReceiptFileName = DefaultReceiptFileName)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            var receiptPath = ResolvePath(path, defaultReceiptFileName);
            SaveText(receiptPath, PublishingPagePackageSerializer.Serialize(receipt), overwrite);
            return receiptPath;
        }

        public static string ResolveExistingPath(string path, string defaultFileName, string description)
        {
            var resolved = ResolvePath(path, defaultFileName);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"{description} not found.", resolved);
            }

            return resolved;
        }

        public static string ResolvePath(string path, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A file path or directory is required.", nameof(path));
            }

            var fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) || string.IsNullOrEmpty(Path.GetExtension(fullPath))
                ? Path.Combine(fullPath, defaultFileName)
                : fullPath;
        }

        public static void SaveText(string path, string value, bool overwrite)
        {
            EnsureWritable(path, overwrite);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, value, new UTF8Encoding(false));
        }

        public static FileStream OpenPackageReadStream(string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.SequentialScan);
        }

        public static void EnsureWritable(string path, bool overwrite)
        {
            if (File.Exists(path) && !overwrite)
            {
                throw new IOException($"The file already exists: {path}");
            }
        }
    }
}
