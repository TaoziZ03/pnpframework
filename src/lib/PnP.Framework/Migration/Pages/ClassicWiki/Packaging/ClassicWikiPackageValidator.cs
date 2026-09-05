using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using System;
using System.IO;
using System.Linq;

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
            var actualSnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(package.Snapshot);
            if (!string.Equals(package.SnapshotDigest, actualSnapshotDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Export package snapshot digest does not match the captured snapshot.");
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
            if (!string.Equals(package.Plan.SourceSnapshotDigest, package.SnapshotDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Migration plan is not bound to the sealed source snapshot digest.");
            }
            var actualPlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            if (!string.Equals(package.PlanDigest, actualPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Migration package plan digest does not match the sealed plan.");
            }
            if (package.Plan.TargetLocation == null
                || package.Plan.TargetLocation.TargetWebId == Guid.Empty
                || string.IsNullOrWhiteSpace(package.Plan.TargetLocation.TargetWebUrl)
                || string.IsNullOrWhiteSpace(package.Plan.TargetLocation.TargetLibraryServerRelativeUrl)
                || string.IsNullOrWhiteSpace(package.Plan.TargetLocation.TargetLibraryTitle)
                || string.IsNullOrWhiteSpace(package.Plan.TargetPageServerRelativeUrl))
            {
                throw new InvalidDataException("Migration package requires a sealed target Web URL and identity.");
            }
            if (!Uri.TryCreate(package.Plan.TargetLocation.TargetWebUrl, UriKind.Absolute, out var targetWebUri)
                || !PagePath.IsWithin(
                    package.Plan.TargetLocation.TargetLibraryServerRelativeUrl,
                    Uri.UnescapeDataString(targetWebUri.AbsolutePath))
                || !PagePath.IsWithin(
                    package.Plan.TargetPageServerRelativeUrl,
                    package.Plan.TargetLocation.TargetLibraryServerRelativeUrl)
                || !PagePath.IsWithin(
                    package.Plan.TargetLocation.TargetFolderServerRelativeUrl,
                    package.Plan.TargetLocation.TargetLibraryServerRelativeUrl))
            {
                throw new InvalidDataException("Target library and page paths must remain within the sealed target Web and library.");
            }
            if (package.Plan.TargetLocation.TargetLibraryTemplate != 101
                && package.Plan.TargetLocation.TargetLibraryTemplate != 119)
            {
                throw new InvalidDataException("Classic wiki target library template must be 101 or 119.");
            }
            if (package.Plan.WikiFieldPlan == null
                || string.IsNullOrWhiteSpace(package.Plan.WikiFieldPlan.ExpectedStoredSha256)
                || !string.Equals(
                    package.Plan.WikiFieldPlan.ExpectedStoredSha256,
                    ClassicWikiDigest.ComputeSha256(package.Plan.WikiFieldPlan.ExactValue ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Classic wiki plan requires a digest-valid WikiField value.");
            }
            if (package.Plan.FieldPlan == null || string.IsNullOrWhiteSpace(package.Plan.FieldPlan.Title))
            {
                throw new InvalidDataException("Classic wiki plan requires an explicit Title value.");
            }
            if (package.Plan.SecurityPlan == null || string.IsNullOrWhiteSpace(package.Plan.SecurityPlan.Disposition))
            {
                throw new InvalidDataException("Classic wiki plan requires an explicit target security disposition.");
            }
            if ((package.Plan.WebParts ?? Array.Empty<ClassicWikiWebPartPlacementPlan>()).Any(value =>
                value == null
                || string.IsNullOrWhiteSpace(value.TypeName)
                || string.IsNullOrWhiteSpace(value.ZoneId)
                || string.IsNullOrWhiteSpace(value.Xml)))
            {
                throw new InvalidDataException("Every planned Web Part requires type, zone, and export evidence.");
            }
            if ((package.Plan.Dependencies ?? Array.Empty<ClassicWikiDependencyPlan>()).Any(value =>
                value == null
                || string.IsNullOrWhiteSpace(value.Consumer)
                || string.IsNullOrWhiteSpace(value.TargetOriginalValue)
                || string.IsNullOrWhiteSpace(value.TargetAbsoluteUrl)))
            {
                throw new InvalidDataException("Every planned dependency requires exact consumer, original-value, and absolute-URL evidence.");
            }
        }
    }
}
