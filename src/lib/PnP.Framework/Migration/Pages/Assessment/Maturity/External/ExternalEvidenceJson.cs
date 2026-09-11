using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.External
{
    internal static class ExternalEvidenceJson
    {
        public static JsonElement Read(ArtifactReference artifact, IMigrationArtifactStore store)
        {
            Require(artifact != null && IngredientMaturityEvaluator.IsSha256(artifact.Sha256)
                && artifact.Length > 0 && artifact.Availability == EvidenceAvailability.Captured
                && store != null, "External evidence requires captured bytes in an artifact store.");
            // This existing helper requires a store and checks actual length even
            // for non-seekable streams. Metadata-only validation is insufficient.
            byte[] bytes;
            try
            {
                bytes = MigrationArtifact.ReadAllBytes(artifact, null, store);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new InvalidDataException("External evidence bytes cannot be reopened.", exception);
            }
            using (var document = JsonDocument.Parse(bytes))
            {
                Require(document.RootElement.ValueKind == JsonValueKind.Object,
                    "An external evidence artifact must be a JSON object.");
                RejectDuplicateProperties(document.RootElement);
                return document.RootElement.Clone();
            }
        }

        public static T ReadCanonical<T>(ArtifactReference artifact, IMigrationArtifactStore store)
        {
            var root = Read(artifact, store);
            var result = MigrationContractSerializer.Deserialize<T>(root.GetRawText());
            Require(result != null && string.Equals(
                MigrationContractSerializer.SerializeCanonical(result),
                MigrationContractSerializer.SerializeCanonical(root), StringComparison.Ordinal),
                "The shared annex/manifest must use the exact canonical schema, fields, enums and ordering.");
            return result;
        }

        // Schema-specific wire adaptation: CCD-109 seals recursively key-sorted
        // JSON with the root planDigest omitted, unlike PnP's null-root seal.
        // Use the existing PnP serializer/hash; never change its semantics.
        public static string BatchPlanDigest(JsonElement root)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                SortedValue(root, true)));
        }

        private static object SortedValue(JsonElement value, bool omitPlanDigest = false)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    return value.EnumerateObject()
                        .Where(property => !omitPlanDigest || property.Name != "planDigest")
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                        .ToDictionary(property => property.Name,
                            property => SortedValue(property.Value), StringComparer.Ordinal);
                case JsonValueKind.Array:
                    return value.EnumerateArray().Select(item => SortedValue(item)).ToArray();
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                default:
                    // Batch-plan/v1 currently has no floating-point values.
                    // Do not guess ECMAScript numeric canonicalization.
                    Require(value.TryGetInt64(out var integer)
                        && integer >= -9007199254740991L && integer <= 9007199254740991L,
                        "Unsupported external plan numeric representation.");
                    return integer;
            }
        }

        private static void RejectDuplicateProperties(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    Require(names.Add(property.Name), "Duplicate external evidence JSON property.");
                    RejectDuplicateProperties(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
            }
        }

        public static JsonElement Property(JsonElement root, string name)
        {
            Require(root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out _),
                "Missing external evidence field: " + name);
            return root.GetProperty(name);
        }

        public static string Text(JsonElement root, string name)
        {
            var value = Property(root, name);
            Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
                "Invalid external evidence string: " + name);
            return value.GetString();
        }

        public static int Number(JsonElement root, string name)
        {
            var value = Property(root, name);
            Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
                "Invalid external evidence integer: " + name);
            return value.GetInt32();
        }

        public static bool Flag(JsonElement root, string name)
        {
            var value = Property(root, name);
            Require(value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False,
                "Invalid external evidence boolean: " + name);
            return value.GetBoolean();
        }

        public static DateTimeOffset Time(JsonElement root, string name)
        {
            var text = Text(root, name);
            Require((text.EndsWith("Z", StringComparison.Ordinal) || text.EndsWith("+00:00", StringComparison.Ordinal))
                && DateTimeOffset.TryParse(text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var value)
                && value != default && value.Offset == TimeSpan.Zero,
                "External evidence requires an explicit UTC timestamp: " + name);
            return DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static JsonElement[] Array(JsonElement root, string name)
        {
            var value = Property(root, name);
            Require(value.ValueKind == JsonValueKind.Array, "Invalid external evidence array: " + name);
            return value.EnumerateArray().ToArray();
        }

        public static void Equal(string actual, string expected, string field)
        {
            Require(!string.IsNullOrWhiteSpace(expected)
                && string.Equals(actual, expected, StringComparison.Ordinal),
                "External evidence binding mismatch: " + field);
        }

        public static void EqualObject<T>(T actual, T expected, string field)
        {
            Require(actual != null && expected != null, "Missing external binding: " + field);
            Equal(MigrationContractSerializer.SerializeCanonical(actual),
                MigrationContractSerializer.SerializeCanonical(expected), field);
        }

        public static void GuidIdentity(string value, string field)
        {
            Require(Guid.TryParse(value, out var parsed) && parsed != Guid.Empty,
                "Invalid storage identity: " + field);
        }

        public static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
