using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.ClassicWebParts
{
    internal static class ClassicWebPartPropertyEvidenceValidator
    {
        internal const string RestExpandedFormat = "rest-expanded-webpart-json";
        internal const string NativeExportFormat = "native-webpart-export-xml";

        internal static void Validate(ClassicWebPartSnapshot host)
        {
            Require(host != null && host.Id != Guid.Empty
                && string.Equals(host.PropertyEvidenceFormat, RestExpandedFormat, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(host.PropertyEvidenceJson)
                && host.PropertyEvidenceArtifact?.Availability == EvidenceAvailability.Captured,
                "The persisted Web Part property evidence is missing or has an unknown acquisition format.");
            // These are the original captured normalized-property artifact bytes,
            // not a second canonicalization algorithm or synthetic native export.
            MigrationArtifactContractValidator.Validate(host.PropertyEvidenceArtifact,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(host.PropertyEvidenceJson)), null, "Web Part persisted properties");
            using (var document = JsonDocument.Parse(host.PropertyEvidenceJson))
            {
                var root = document.RootElement;
                Require(root.ValueKind == JsonValueKind.Object && HasUniqueNames(root)
                    && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && Guid.TryParse(id.GetString(), out var instanceId) && instanceId == host.Id
                    && root.TryGetProperty("zoneIndex", out var order) && order.ValueKind == JsonValueKind.Number
                    && order.TryGetInt32(out var zoneIndex) && zoneIndex == host.ZoneIndex
                    && root.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object && HasUniqueNames(properties),
                    "The property artifact does not bind this exact Web Part instance, ZoneIndex and unambiguous property bag.");
            }
        }

        internal static string ReadDirectProperty(ClassicWebPartSnapshot host, string propertyName)
        {
            Validate(host);
            using (var document = JsonDocument.Parse(host.PropertyEvidenceJson))
            {
                var properties = document.RootElement.GetProperty("properties").EnumerateObject()
                    .Where(value => string.Equals(value.Name, propertyName, StringComparison.OrdinalIgnoreCase)).ToArray();
                Require(properties.Length == 1 && properties[0].Value.ValueKind == JsonValueKind.String,
                    "The direct persisted Web Part property is missing, ambiguous or is not a string.");
                return properties[0].Value.GetString();
            }
        }

        private static bool HasUniqueNames(JsonElement value) => value.EnumerateObject()
            .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).All(group => group.Count() == 1);

        private static void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidDataException(message); }
        }
    }
}
