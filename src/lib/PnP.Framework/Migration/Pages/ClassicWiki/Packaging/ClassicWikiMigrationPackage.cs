using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using System;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Packaging
{
    public sealed class ClassicWikiMigrationPackage
    {
        public string SchemaVersion { get; set; } = ClassicWikiPackageContract.MigrationSchemaVersion;

        public DateTimeOffset PlannedAtUtc { get; set; }

        public string ExportSchemaVersion { get; set; } = ClassicWikiPackageContract.ExportSchemaVersion;

        public DateTimeOffset ExportedAtUtc { get; set; }

        public ClassicWikiPackageState State { get; set; }

        public ClassicWikiWorkflowSelection Selection { get; set; } = new ClassicWikiWorkflowSelection();

        public string SelectionDigest { get; set; }

        public ClassicWikiCaptureBundle Snapshot { get; set; }

        public ClassicWikiMigrationPlan Plan { get; set; }

        public string SnapshotDigest { get; set; }

        public string PlanDigest { get; set; }

        public ClassicWikiMigrationReport Report { get; set; }
    }
}
