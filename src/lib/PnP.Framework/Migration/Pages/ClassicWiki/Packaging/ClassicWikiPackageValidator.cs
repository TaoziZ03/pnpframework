using PnP.Framework.Migration.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Packaging
{
    public static class ClassicWikiPackageValidator
    {
        public static void ValidateExport(ClassicWikiExportPackage package, IMigrationArtifactStore artifactStore = null)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            if (!string.Equals(package.SchemaVersion, ClassicWikiPackageContract.ExportSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported export schema version '{package.SchemaVersion}'. Expected '{ClassicWikiPackageContract.ExportSchemaVersion}'.");
            }
            if (package.Snapshot == null)
            {
                throw new InvalidDataException("Export package snapshot is required.");
            }
            if (string.IsNullOrWhiteSpace(package.SnapshotDigest))
            {
                throw new InvalidDataException("Export package snapshot digest is required.");
            }
        }

        public static void ValidateMigration(ClassicWikiMigrationPackage package, IMigrationArtifactStore artifactStore = null)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            if (!string.Equals(package.SchemaVersion, ClassicWikiPackageContract.MigrationSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported migration schema version '{package.SchemaVersion}'. Expected '{ClassicWikiPackageContract.MigrationSchemaVersion}'.");
            }
            ValidateExport(new ClassicWikiExportPackage
            {
                SchemaVersion = package.ExportSchemaVersion,
                ExportedAtUtc = package.ExportedAtUtc,
                Selection = package.Selection,
                SelectionDigest = package.SelectionDigest,
                Snapshot = package.Snapshot,
                SnapshotDigest = package.SnapshotDigest
            }, artifactStore);

            if (package.Plan == null)
            {
                throw new InvalidDataException("Migration package plan is required.");
            }
            if (string.IsNullOrWhiteSpace(package.PlanDigest))
            {
                throw new InvalidDataException("Migration package plan digest is required.");
            }
        }
    }
}
