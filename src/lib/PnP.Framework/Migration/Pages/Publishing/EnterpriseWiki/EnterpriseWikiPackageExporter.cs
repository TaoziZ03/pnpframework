using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.EnterpriseWiki
{
    public sealed class EnterpriseWikiPackageExporter
    {
        private readonly PublishingPagePackageExporter exporter;

        public EnterpriseWikiPackageExporter()
            : this(PublishingPageIngredientHandlerCatalog.Default)
        {
        }

        public EnterpriseWikiPackageExporter(PublishingPageIngredientHandlerCatalog handlerCatalog)
        {
            exporter = new PublishingPagePackageExporter(handlerCatalog);
        }

        public PublishingPageExportPackage Export(ClientContext sourceContext, PageCaptureOptions options)
        {
            return exporter.Export(sourceContext, options, EnterpriseWikiV1WorkflowPolicy.Instance);
        }

        public PublishingPageExportPackage Export(
            ClientContext sourceContext,
            PageCaptureOptions options,
            IMigrationArtifactStore artifactStore)
        {
            return exporter.Export(sourceContext, options, EnterpriseWikiV1WorkflowPolicy.Instance, artifactStore);
        }

        public PublishingPageExportPackage ExportWithIngredientEvidence(
            ClientContext sourceContext,
            PageCaptureOptions options,
            IEnumerable<PublishingPageIngredientEvidenceEnvelope> ingredientEvidence,
            IMigrationArtifactStore artifactStore = null)
        {
            return exporter.ExportWithIngredientEvidence(
                sourceContext,
                options,
                EnterpriseWikiV1WorkflowPolicy.Instance,
                ingredientEvidence,
                artifactStore);
        }
    }
}
