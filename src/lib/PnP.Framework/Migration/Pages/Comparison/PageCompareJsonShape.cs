using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Comparison
{
    /// <summary>
    /// Compare-contract-specific guard against System.Text.Json silently
    /// discarding unknown or omitted fields during a native DTO round-trip.
    /// </summary>
    internal static class PageCompareJsonShape
    {
        public static bool HasSameJsonShape<T>(string json, T value)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Page compare contract JSON is required.", nameof(json));
            }
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var source = JsonDocument.Parse(json))
            using (var roundTrip = JsonDocument.Parse(MigrationContractSerializer.SerializeCanonical(value)))
            {
                return HasSameShape(source.RootElement, roundTrip.RootElement);
            }
        }

        public static bool HasNoDuplicateProperties(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Page compare contract JSON is required.", nameof(json));
            }

            using (var document = JsonDocument.Parse(json))
            {
                return HasNoDuplicateProperties(document.RootElement);
            }
        }

        private static bool HasSameShape(JsonElement source, JsonElement roundTrip)
        {
            if (source.ValueKind != roundTrip.ValueKind)
            {
                return false;
            }
            if (source.ValueKind == JsonValueKind.Object)
            {
                var sourceProperties = Properties(source);
                var roundTripProperties = Properties(roundTrip);
                if (sourceProperties == null
                    || roundTripProperties == null
                    || sourceProperties.Count != roundTripProperties.Count)
                {
                    return false;
                }
                foreach (var property in sourceProperties)
                {
                    if (!roundTripProperties.TryGetValue(property.Key, out var roundTripValue)
                        || !HasSameShape(property.Value, roundTripValue))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (source.ValueKind == JsonValueKind.Array)
            {
                var sourceItems = source.EnumerateArray();
                var roundTripItems = roundTrip.EnumerateArray();
                while (true)
                {
                    var hasSource = sourceItems.MoveNext();
                    var hasRoundTrip = roundTripItems.MoveNext();
                    if (hasSource != hasRoundTrip)
                    {
                        return false;
                    }
                    if (!hasSource)
                    {
                        return true;
                    }
                    if (!HasSameShape(sourceItems.Current, roundTripItems.Current))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static Dictionary<string, JsonElement> Properties(JsonElement value)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (result.ContainsKey(property.Name))
                {
                    return null;
                }
                result.Add(property.Name, property.Value);
            }
            return result;
        }

        private static bool HasNoDuplicateProperties(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name) || !HasNoDuplicateProperties(property.Value))
                    {
                        return false;
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (!HasNoDuplicateProperties(item))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
