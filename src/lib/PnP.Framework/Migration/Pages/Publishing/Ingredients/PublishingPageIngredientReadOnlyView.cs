using PnP.Framework.Migration.Packaging;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageIngredientReadOnlyView
    {
        public static T Clone<T>(T value)
            where T : class
        {
            return value == null
                ? null
                : MigrationContractSerializer.Deserialize<T>(
                    MigrationContractSerializer.SerializeCanonical(value));
        }
    }
}
