using PnP.Framework.Migration.Packaging;

namespace PnP.Framework.Migration.Scale
{
    public static class ScaleRunContractSerializer
    {
        public static string SerializeCanonical<T>(T value)
        {
            return MigrationContractSerializer.SerializeCanonical(value);
        }

        public static T Deserialize<T>(string value)
        {
            return MigrationContractSerializer.Deserialize<T>(value);
        }
    }
}
