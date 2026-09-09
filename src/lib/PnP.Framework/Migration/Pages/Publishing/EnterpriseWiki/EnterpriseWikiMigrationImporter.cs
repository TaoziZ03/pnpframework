using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Topology.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using System;

namespace PnP.Framework.Migration.Pages.Publishing.EnterpriseWiki
{
    public sealed class EnterpriseWikiMigrationImporter
    {
        private readonly PublishingPageMigrationImporter importer;

        public EnterpriseWikiMigrationImporter()
            : this(PublishingPageIngredientHandlerCatalog.Default)
        {
        }

        public EnterpriseWikiMigrationImporter(PublishingPageIngredientHandlerCatalog handlerCatalog)
        {
            importer = new PublishingPageMigrationImporter(handlerCatalog);
        }

        public PublishingPageImportReceipt Import(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest)
        {
            return importer.Import(targetContext, package, approvedPlanDigest, EnterpriseWikiV1WorkflowPolicy.Instance);
        }

        public PublishingPageImportReceipt Import(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal)
        {
            return importer.Import(targetContext, package, approvedPlanDigest, EnterpriseWikiV1WorkflowPolicy.Instance, journal);
        }

        public PublishingPageImportReceipt Import(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            IMigrationExecutionJournal journal,
            IMigrationArtifactStore artifactStore)
        {
            return importer.Import(targetContext, package, approvedPlanDigest, EnterpriseWikiV1WorkflowPolicy.Instance, journal, artifactStore);
        }

        public PublishingPageImportReceipt ImportWithSharedTopology(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            string approvedPlanDigest,
            SharedTopologyExecutionProof sharedTopologyProof,
            IMigrationExecutionJournal journal = null,
            IMigrationArtifactStore artifactStore = null)
        {
            return importer.ImportWithSharedTopology(
                targetContext,
                package,
                approvedPlanDigest,
                sharedTopologyProof,
                EnterpriseWikiV1WorkflowPolicy.Instance,
                journal,
                artifactStore);
        }
    }
}
