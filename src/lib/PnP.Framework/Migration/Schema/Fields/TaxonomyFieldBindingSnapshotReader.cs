using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace PnP.Framework.Migration.Schema.Fields
{
    internal sealed class TaxonomyFieldBindingCaptureResult
    {
        public TaxonomyFieldBindingSnapshot Binding { get; set; }

        public bool IsComplete { get; set; }

        public bool UsedSchemaXmlFallback { get; set; }

        public IList<EvidenceSource> Sources { get; set; } = new List<EvidenceSource>();

        public IList<string> Diagnostics { get; set; } = new List<string>();
    }

    internal static class TaxonomyFieldBindingSnapshotReader
    {
        private static readonly string[] RequiredProperties =
        {
            "SspId",
            "TermSetId",
            "AnchorId",
            "TextField",
            "Open"
        };

        public static TaxonomyFieldBindingCaptureResult Read(
            Guid fieldId,
            string fieldInternalName,
            string schemaXml,
            Func<TaxonomyFieldBindingSnapshot> typedReader)
        {
            var result = new TaxonomyFieldBindingCaptureResult();
            try
            {
                var typed = typedReader == null ? null : typedReader();
                return FromTypedOrSchemaXml(fieldId, fieldInternalName, schemaXml, typed, null);
            }
            catch (Exception exception)
            {
                result.Diagnostics.Add(
                    "TaxonomyBindingTypedReadFailed: field="
                    + FieldLabel(fieldId, fieldInternalName) + "; exceptionType="
                    + exception.GetType().FullName + ".");
                result.Diagnostics.Add(
                    "TaxonomyBindingAuthorizationUnclassified: no literal HTTP 401/403 wire evidence was captured.");
            }
            AddSchemaXmlFallback(result, fieldId, schemaXml);
            return result;
        }

        internal static IDictionary<Guid, TaxonomyFieldBindingCaptureResult> ReadAll<T>(
            IEnumerable<T> fields,
            Func<T, Guid> fieldId,
            Func<T, string> fieldInternalName,
            Func<T, string> schemaXml,
            Func<IEnumerable<T>, IDictionary<Guid, TaxonomyFieldBindingSnapshot>> batchReader,
            Func<T, TaxonomyFieldBindingSnapshot> isolatedReader)
        {
            var values = (fields ?? Enumerable.Empty<T>()).ToArray();
            var results = new Dictionary<Guid, TaxonomyFieldBindingCaptureResult>();
            try
            {
                var typed = batchReader(values) ?? new Dictionary<Guid, TaxonomyFieldBindingSnapshot>();
                foreach (var field in values)
                {
                    var id = fieldId(field);
                    typed.TryGetValue(id, out var binding);
                    results[id] = FromTypedOrSchemaXml(
                        id,
                        fieldInternalName(field),
                        schemaXml(field),
                        binding,
                        binding == null ? "TaxonomyBindingBatchResultMissing" : null);
                }
                return results;
            }
            catch (Exception exception)
            {
                foreach (var field in values)
                {
                    var id = fieldId(field);
                    var isolated = Read(
                        id,
                        fieldInternalName(field),
                        schemaXml(field),
                        () => isolatedReader(field));
                    isolated.Diagnostics.Insert(
                        0,
                        "TaxonomyBindingBatchReadFailed: exceptionType="
                        + exception.GetType().FullName + "; isolatedRetry=true.");
                    results[id] = isolated;
                }
            }
            return results;
        }

        internal static bool IsComplete(TaxonomyFieldBindingSnapshot binding)
        {
            string ignored;
            return TryValidate(binding, out ignored);
        }

        internal static bool IsTaxonomyFieldType(string typeAsString)
        {
            return string.Equals(typeAsString, "TaxonomyFieldType", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeAsString, "TaxonomyFieldTypeMulti", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryValidateHiddenTextCompanion<T>(
            Guid taxonomyFieldId,
            TaxonomyFieldBindingSnapshot binding,
            IEnumerable<T> closure,
            Func<T, Guid> fieldId,
            Func<T, string> fieldType,
            Func<T, bool> fieldHidden,
            out string diagnostic)
        {
            diagnostic = null;
            if (!IsComplete(binding))
            {
                diagnostic = "reason=incomplete-binding";
                return false;
            }
            if (binding.HiddenTextFieldId == taxonomyFieldId)
            {
                diagnostic = "reason=self-reference; hiddenTextFieldId=" + binding.HiddenTextFieldId.ToString("D");
                return false;
            }

            var matches = (closure ?? Enumerable.Empty<T>())
                .Where(value => value != null && fieldId(value) == binding.HiddenTextFieldId)
                .ToArray();
            if (matches.Length != 1)
            {
                diagnostic = "reason=" + (matches.Length == 0 ? "missing" : "duplicate-identity")
                    + "; hiddenTextFieldId=" + binding.HiddenTextFieldId.ToString("D");
                return false;
            }
            if (!fieldHidden(matches[0]))
            {
                diagnostic = "reason=not-hidden; hiddenTextFieldId=" + binding.HiddenTextFieldId.ToString("D");
                return false;
            }
            if (!string.Equals(fieldType(matches[0]), "Note", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "reason=wrong-type; hiddenTextFieldId=" + binding.HiddenTextFieldId.ToString("D");
                return false;
            }
            return true;
        }

        internal static bool TryReadSchemaXml(
            string schemaXml,
            out TaxonomyFieldBindingSnapshot binding,
            out string diagnostic)
        {
            binding = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(schemaXml))
            {
                diagnostic = "SchemaXml is empty.";
                return false;
            }

            XElement root;
            try
            {
                root = XDocument.Parse(schemaXml, LoadOptions.None).Root;
            }
            catch (XmlException)
            {
                diagnostic = "SchemaXml is malformed.";
                return false;
            }
            if (root == null || !string.Equals(root.Name.LocalName, "Field", StringComparison.Ordinal))
            {
                diagnostic = "SchemaXml has no Field root element.";
                return false;
            }
            var fieldType = ((string)root.Attribute("Type") ?? string.Empty).Trim();
            if (!IsTaxonomyFieldType(fieldType))
            {
                diagnostic = "SchemaXml Field Type is not a taxonomy field type.";
                return false;
            }

            var customization = SingleChild(root, "Customization");
            var propertyArray = customization == null ? null : SingleChild(customization, "ArrayOfProperty");
            if (propertyArray == null)
            {
                diagnostic = "SchemaXml has no unique Customization/ArrayOfProperty element.";
                return false;
            }

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in propertyArray.Elements().Where(value => value.Name.LocalName == "Property"))
            {
                var names = property.Elements().Where(value => value.Name.LocalName == "Name").ToArray();
                var values = property.Elements().Where(value => value.Name.LocalName == "Value").ToArray();
                if (names.Length != 1)
                {
                    diagnostic = "SchemaXml contains a Property with an ambiguous Name element count.";
                    return false;
                }
                var name = names[0].Value.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    diagnostic = "SchemaXml contains a Property with an empty Name.";
                    return false;
                }
                if (!RequiredProperties.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (values.Length != 1)
                {
                    diagnostic = "SchemaXml taxonomy property '" + name + "' does not contain exactly one Value element.";
                    return false;
                }
                if (properties.ContainsKey(name))
                {
                    diagnostic = "SchemaXml contains duplicate taxonomy property '" + name + "'.";
                    return false;
                }
                properties.Add(name, values[0].Value.Trim());
            }

            var missing = RequiredProperties.Where(name => !properties.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
            {
                diagnostic = "SchemaXml is missing required taxonomy properties: " + string.Join(", ", missing) + ".";
                return false;
            }

            Guid termStoreId;
            Guid termSetId;
            Guid anchorId;
            Guid textFieldId;
            bool open;
            if (!TryParseRequiredGuid(properties["SspId"], false, out termStoreId))
            {
                diagnostic = "SspId is not a non-empty GUID.";
                return false;
            }
            if (!TryParseRequiredGuid(properties["TermSetId"], false, out termSetId))
            {
                diagnostic = "TermSetId is not a non-empty GUID.";
                return false;
            }
            if (!TryParseRequiredGuid(properties["AnchorId"], true, out anchorId))
            {
                diagnostic = "AnchorId is not a GUID.";
                return false;
            }
            if (!TryParseRequiredGuid(properties["TextField"], false, out textFieldId))
            {
                diagnostic = "TextField is not a non-empty GUID.";
                return false;
            }
            if (!bool.TryParse(properties["Open"], out open))
            {
                diagnostic = "Open is not a Boolean value.";
                return false;
            }

            binding = new TaxonomyFieldBindingSnapshot
            {
                SourceTermStoreId = termStoreId,
                SourceTermSetId = termSetId,
                AnchorTermId = anchorId,
                HiddenTextFieldId = textFieldId,
                Open = open
            };
            return true;
        }

        private static TaxonomyFieldBindingCaptureResult FromTypedOrSchemaXml(
            Guid fieldId,
            string fieldInternalName,
            string schemaXml,
            TaxonomyFieldBindingSnapshot typed,
            string typedFailureCode)
        {
            var result = new TaxonomyFieldBindingCaptureResult();
            string typedDiagnostic;
            if (TryValidate(typed, out typedDiagnostic))
            {
                result.Binding = typed;
                result.IsComplete = true;
                return result;
            }

            result.Diagnostics.Add(
                (typedFailureCode ?? "TaxonomyBindingTypedResultIncomplete")
                + ": field=" + FieldLabel(fieldId, fieldInternalName)
                + "; reason=" + typedDiagnostic);
            AddSchemaXmlFallback(result, fieldId, schemaXml);
            return result;
        }

        private static void AddSchemaXmlFallback(
            TaxonomyFieldBindingCaptureResult result,
            Guid fieldId,
            string schemaXml)
        {
            if (!string.IsNullOrWhiteSpace(schemaXml))
            {
                result.Sources.Add(new EvidenceSource
                {
                    ExchangeId = "field-schema:" + fieldId.ToString("D"),
                    PayloadSha256 = MigrationDigest.ComputeSha256(schemaXml),
                    Selector = "Field.SchemaXml/Customization/ArrayOfProperty"
                });
            }

            TaxonomyFieldBindingSnapshot fallback;
            string fallbackDiagnostic;
            if (TryReadSchemaXml(schemaXml, out fallback, out fallbackDiagnostic))
            {
                result.Binding = fallback;
                result.IsComplete = true;
                result.UsedSchemaXmlFallback = true;
                result.Diagnostics.Add("TaxonomyBindingSchemaXmlFallbackUsed: complete binding recovered.");
                return;
            }

            result.Diagnostics.Add(
                "TaxonomyBindingSchemaXmlFallbackIncomplete: " + fallbackDiagnostic);
        }

        private static bool TryValidate(TaxonomyFieldBindingSnapshot binding, out string diagnostic)
        {
            if (binding == null)
            {
                diagnostic = "the typed result was null.";
                return false;
            }
            if (binding.SourceTermStoreId == Guid.Empty)
            {
                diagnostic = "SspId was empty.";
                return false;
            }
            if (binding.SourceTermSetId == Guid.Empty)
            {
                diagnostic = "TermSetId was empty.";
                return false;
            }
            if (binding.HiddenTextFieldId == Guid.Empty)
            {
                diagnostic = "TextField was empty.";
                return false;
            }
            diagnostic = null;
            return true;
        }

        private static XElement SingleChild(XElement parent, string localName)
        {
            var matches = parent.Elements().Where(value => value.Name.LocalName == localName).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static bool TryParseRequiredGuid(string value, bool allowEmpty, out Guid result)
        {
            return Guid.TryParse((value ?? string.Empty).Trim().Trim('{', '}'), out result)
                && (allowEmpty || result != Guid.Empty);
        }

        private static string FieldLabel(Guid fieldId, string fieldInternalName)
        {
            return "'" + (string.IsNullOrWhiteSpace(fieldInternalName) ? "(unnamed)" : fieldInternalName)
                + "' (" + fieldId.ToString("D") + ")";
        }
    }
}
