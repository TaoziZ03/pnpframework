using PnP.Framework.Migration.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Packaging
{
    public static class ClassicWikiPackageSerializer
    {
        public static string Serialize<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return MigrationContractSerializer.SerializeIndented(value) + Environment.NewLine;
        }

        public static string SerializeCanonical<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return MigrationContractSerializer.SerializeCanonical(value);
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Package JSON is required.", nameof(json));
            var value = MigrationContractSerializer.Deserialize<T>(json);
            if (value == null)
            {
                throw new InvalidDataException($"The JSON payload did not contain a {typeof(T).Name} value.");
            }
            return value;
        }
    }
}
