using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;

namespace PnP.Framework.Migration.Pages.Publishing.Packaging
{
    public static class PublishingPagePackageValidator
    {
        public static void ValidateExport(PublishingPageExportPackage package)
        {
            PublishingPageExportPackageValidator.Validate(package, null, PublishingPageIngredientHandlerCatalog.Default);
        }

        public static void ValidateExport(
            PublishingPageExportPackage package,
            IMigrationArtifactStore artifactStore)
        {
            PublishingPageExportPackageValidator.Validate(package, artifactStore, PublishingPageIngredientHandlerCatalog.Default);
        }

        public static void ValidateExport(
            PublishingPageExportPackage package,
            IMigrationArtifactStore artifactStore,
            PublishingPageIngredientHandlerCatalog handlerCatalog)
        {
            PublishingPageExportPackageValidator.Validate(package, artifactStore, handlerCatalog);
        }

        public static void ValidateMigration(PublishingPageMigrationPackage package)
        {
            PublishingPageMigrationPackageValidator.Validate(package, null, PublishingPageIngredientHandlerCatalog.Default);
        }

        public static void ValidateMigration(
            PublishingPageMigrationPackage package,
            IMigrationArtifactStore artifactStore)
        {
            PublishingPageMigrationPackageValidator.Validate(package, artifactStore, PublishingPageIngredientHandlerCatalog.Default);
        }

        public static void ValidateMigration(
            PublishingPageMigrationPackage package,
            IMigrationArtifactStore artifactStore,
            PublishingPageIngredientHandlerCatalog handlerCatalog)
        {
            PublishingPageMigrationPackageValidator.Validate(package, artifactStore, handlerCatalog);
        }
    }
}
