using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts.Bindings;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Profiles;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    internal static class ClassicWikiFreshTargetReader
    {
        public static ClassicWikiFreshTargetEvidence Read(
            ClientContext freshContext,
            ClassicWikiMigrationPackage package,
            IMigrationArtifactStore artifactStore,
            ICollection<string> warnings,
            ICollection<string> diagnostics)
        {
            if (freshContext == null) throw new ArgumentNullException(nameof(freshContext));
            if (package?.Plan?.TargetLocation == null) throw new ArgumentNullException(nameof(package));

            var options = new PageCaptureOptions
            {
                SourcePageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                IncludeWebParts = true,
                MaximumDependencyBytes = package.Snapshot?.CapturePolicy?.MaximumDependencyBytes ?? 10 * 1024 * 1024,
                ProtectedAssets = package.Snapshot?.CapturePolicy?.ProtectedAssets
            };
            var blockers = new List<string>();
            var captureWarnings = new List<string>();
            var capture = ClassicWikiCaptureReader.Read(
                freshContext,
                package.Plan.TargetPageServerRelativeUrl,
                options,
                artifactStore,
                blockers,
                captureWarnings);
            var runtime = PageRuntimeResolver.Resolve(
                capture.PageArtifact,
                null,
                capture.Identity.ContentTypeId);
            var references = PageReferenceSnapshotReader.Read(
                freshContext,
                capture.Identity,
                null,
                capture.WikiField,
                capture.WebParts,
                options,
                captureWarnings);

            var file = freshContext.Web.GetFileByServerRelativePath(
                ResourcePath.FromDecodedUrl(package.Plan.TargetPageServerRelativeUrl));
            freshContext.Load(file, value => value.Properties);
            freshContext.ExecuteQueryRetry();

            foreach (var blocker in blockers)
            {
                diagnostics.Add("Fresh capture blocker: " + blocker);
            }
            foreach (var warning in captureWarnings)
            {
                warnings.Add("Fresh capture: " + warning);
            }

            var snapshot = new ClassicWikiCaptureBundle
            {
                CapturePolicy = options,
                Source = capture.Identity,
                PageArtifact = capture.PageArtifact,
                Runtime = runtime,
                ProfileSignals = new List<PageProfileSignal>(),
                WikiField = capture.WikiField,
                WikiFieldSha256 = ClassicWikiDigest.ComputeSha256(capture.WikiField ?? string.Empty),
                LibraryBaseTemplate = capture.LibraryBaseTemplate,
                LibraryTitle = capture.LibraryTitle,
                LibraryServerRelativeUrl = capture.LibraryServerRelativeUrl,
                LibraryEnableVersioning = capture.LibraryEnableVersioning,
                LibraryEnableMinorVersions = capture.LibraryEnableMinorVersions,
                LibraryEnableModeration = capture.LibraryEnableModeration,
                LibraryForceCheckout = capture.LibraryForceCheckout,
                Fields = capture.Fields,
                WebParts = capture.WebParts,
                ListWebPartBindings = new List<ClassicListWebPartBindingSnapshot>(),
                Dependencies = references,
                Security = capture.Security,
                Lifecycle = capture.Lifecycle,
                SourceFence = capture.SourceFence,
                Blockers = blockers,
                Warnings = captureWarnings
            };
            var recapture = new ClassicWikiExportPackage
            {
                SchemaVersion = ClassicWikiPackageContract.ExportSchemaVersion,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = package.Selection,
                SelectionDigest = package.SelectionDigest,
                Snapshot = snapshot,
                SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(snapshot)
            };

            return new ClassicWikiFreshTargetEvidence
            {
                Recapture = recapture,
                FileProperties = file.Properties.FieldValues
                    .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase),
                IndependentContext = true
            };
        }
    }
}
