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
                string typedDiagnostic;
                if (TryValidate(typed, out typedDiagnostic))
                {
                    result.Binding = typed;
                    result.IsComplete = true;
                    result.Sources.Add(new EvidenceSource
                    {
                        ExchangeId = "csom-taxonomy-field:" + fieldId.ToString("D"),
                        Selector = "TaxonomyField.{SspId,TermSetId,AnchorId,TextField,Open}"
                    });
                    return result;
                }

                result.Diagnostics.Add(
                    "Typed CSOM taxonomy binding evidence was incomplete for field "
                    + FieldLabel(fieldId, fieldInternalName) + ": " + typedDiagnostic);
            }
            catch (Exception exception)
            {
                result.Diagnostics.Add(
                    "Typed CSOM taxonomy binding read failed for field "
                    + FieldLabel(fieldId, fieldInternalName) + " ("
                    + exception.GetType().Name + "): " + exception.Message);
                result.Diagnostics.Add(
                    "No literal HTTP 401/403 wire evidence was captured for this field failure; no authorization classification was produced.");
            }

            TaxonomyFieldBindingSnapshot fallback;
            string fallbackDiagnostic;
            if (TryReadSchemaXml(schemaXml, out fallback, out fallbackDiagnostic))
            {
                result.Binding = fallback;
                result.IsComplete = true;
                result.UsedSchemaXmlFallback = true;
                result.Sources.Add(new EvidenceSource
                {
                    ExchangeId = "field-schema:" + fieldId.ToString("D"),
                    PayloadSha256 = MigrationDigest.ComputeSha256(schemaXml),
                    Selector = "Field.SchemaXml/Customization/ArrayOfProperty"
                });
                result.Diagnostics.Add(
                    "Complete taxonomy binding evidence was recovered from the captured Field.SchemaXml.");
                return result;
            }

            result.Diagnostics.Add(
                "Captured Field.SchemaXml did not provide a complete taxonomy binding: " + fallbackDiagnostic);
            return result;
        }

        internal static IDictionary<Guid, TaxonomyFieldBindingCaptureResult> ReadAll<T>(
            IEnumerable<T> fields,
            Func<T, Guid> fieldId,
            Func<T, string> fieldInternalName,
            Func<T, string> schemaXml,
            Func<T, TaxonomyFieldBindingSnapshot> typedReader)
        {
            var results = new Dictionary<Guid, TaxonomyFieldBindingCaptureResult>();
            foreach (var field in fields ?? Enumerable.Empty<T>())
            {
                var id = fieldId(field);
                results[id] = Read(
                    id,
                    fieldInternalName(field),
                    schemaXml(field),
                    () => typedReader(field));
            }
            return results;
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
            catch (XmlException exception)
            {
                diagnostic = "SchemaXml is malformed: " + exception.Message;
                return false;
            }
            if (root == null || !string.Equals(root.Name.LocalName, "Field", StringComparison.Ordinal))
            {
                diagnostic = "SchemaXml has no Field root element.";
                return false;
            }
            var fieldType = ((string)root.Attribute("Type") ?? string.Empty).Trim();
            if (!fieldType.StartsWith("TaxonomyFieldType", StringComparison.OrdinalIgnoreCase))
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
                if (names.Length != 1 || string.IsNullOrWhiteSpace(names[0].Value))
                {
                    continue;
                }
                var name = names[0].Value.Trim();
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
