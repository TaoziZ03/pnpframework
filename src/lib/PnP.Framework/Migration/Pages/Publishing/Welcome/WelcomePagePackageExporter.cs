using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Profiles;

namespace PnP.Framework.Migration.Pages.Publishing.Welcome
{
    public sealed class WelcomePagePackageExporter
    {
        private readonly PublishingPagePackageExporter exporter = new PublishingPagePackageExporter();

        public PublishingPageExportPackage Export(ClientContext sourceContext, PageCaptureOptions options)
        {
            return exporter.Export(sourceContext, options, WelcomePageV1WorkflowPolicy.Instance);
        }

        public PublishingPageExportPackage Export(
            ClientContext sourceContext,
            PageCaptureOptions options,
            IMigrationArtifactStore artifactStore)
        {
            return exporter.Export(sourceContext, options, WelcomePageV1WorkflowPolicy.Instance, artifactStore);
        }
    }
}
