using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using System;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Packaging
{
    public sealed class ClassicWikiExportPackage
    {
        public string SchemaVersion { get; set; } = ClassicWikiPackageContract.ExportSchemaVersion;

        public DateTimeOffset ExportedAtUtc { get; set; }

        public ClassicWikiWorkflowSelection Selection { get; set; } = new ClassicWikiWorkflowSelection();

        public string SelectionDigest { get; set; }

        public ClassicWikiCaptureBundle Snapshot { get; set; }

        public string SnapshotDigest { get; set; }
    }
}
