using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Capture
{
    /// <summary>
    /// Reconstructs the authored reference inventory from an already captured
    /// Classic Wiki snapshot without requiring another source request.
    /// </summary>
    public static class ClassicWikiReferenceInventory
    {
        public static IList<PageReferenceSnapshot> CaptureReferenceOnly(
            ClassicWikiCaptureBundle snapshot,
            ICollection<string> warnings = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Source == null) throw new InvalidDataException("Classic Wiki source identity is required for reference capture.");
            if (snapshot.CapturePolicy == null) throw new InvalidDataException("Classic Wiki capture policy is required for reference capture.");

            var actualWikiFieldDigest = Packaging.ClassicWikiDigest.ComputeSha256(snapshot.WikiField ?? string.Empty);
            if (string.IsNullOrWhiteSpace(snapshot.WikiFieldSha256)
                || !string.Equals(snapshot.WikiFieldSha256, actualWikiFieldDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Classic Wiki WikiField digest is missing or stale; dependency inventory cannot be reconstructed safely.");
            }

            var inventoryWarnings = warnings ?? new List<string>();
            return PageReferenceSnapshotReader.Read(
                sourceContext: null,
                source: snapshot.Source,
                sourceTopology: null,
                pageContent: snapshot.WikiField,
                webParts: snapshot.WebParts,
                options: snapshot.CapturePolicy,
                warnings: inventoryWarnings);
        }
    }
}
