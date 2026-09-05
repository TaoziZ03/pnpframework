using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Schema.Fields;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PnP.Framework.Migration.Lists.Fields
{
    internal static class ListFieldSnapshotReader
    {
        public static IList<ListFieldSnapshot> Read(ClientContext context, FieldCollection fields)
        {
            var taxonomyFields = TaxonomyFieldBindingSnapshotReader.ReadAll(
                fields.Where(value => value.TypeAsString.StartsWith("TaxonomyFieldType", StringComparison.OrdinalIgnoreCase)),
                field => field.Id,
                field => field.InternalName,
                field => field.SchemaXml,
                field => ReadTypedTaxonomyBinding(context, field));

            return fields.Select(field => Create(field, taxonomyFields)).OrderBy(value => value.InternalName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static TaxonomyFieldBindingSnapshot ReadTypedTaxonomyBinding(ClientContext context, Field field)
        {
            var taxonomy = context.CastTo<Microsoft.SharePoint.Client.Taxonomy.TaxonomyField>(field);
            context.Load(
                taxonomy,
                value => value.SspId,
                value => value.TermSetId,
                value => value.AnchorId,
                value => value.TextField,
                value => value.Open);
            context.ExecuteQueryRetry();
            return new TaxonomyFieldBindingSnapshot
            {
                SourceTermStoreId = taxonomy.SspId,
                SourceTermSetId = taxonomy.TermSetId,
                AnchorTermId = taxonomy.AnchorId,
                HiddenTextFieldId = taxonomy.TextField,
                Open = taxonomy.Open
            };
        }

        private static ListFieldSnapshot Create(Field field, IDictionary<Guid, TaxonomyFieldBindingCaptureResult> taxonomyFields)
        {
            Guid? lookupWebId = null;
            Guid? lookupListId = null;
            string lookupField = null;
            try
            {
                var root = XDocument.Parse(field.SchemaXml).Root;
                lookupWebId = ParseGuid(root == null ? null : (string)root.Attribute("WebId"));
                lookupListId = ParseGuid(root == null ? null : (string)root.Attribute("List"));
                lookupField = root == null ? null : (string)root.Attribute("ShowField");
            }
            catch (System.Xml.XmlException)
            {
            }

            TaxonomyFieldBindingCaptureResult taxonomy;
            var snapshot = new ListFieldSnapshot
            {
                Id = field.Id,
                InternalName = field.InternalName,
                Title = field.Title,
                TypeAsString = field.TypeAsString,
                Group = field.Group,
                SchemaXml = field.SchemaXml,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(field.SchemaXml ?? string.Empty),
                Hidden = field.Hidden,
                ReadOnly = field.ReadOnlyField,
                Required = field.Required,
                FromBaseType = field.FromBaseType,
                Sealed = field.Sealed,
                SourceLookupWebId = lookupWebId,
                SourceLookupListId = lookupListId,
                LookupField = lookupField,
                Taxonomy = taxonomyFields.TryGetValue(field.Id, out taxonomy) ? taxonomy.Binding : null
            };
            try
            {
                snapshot.PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(field.SchemaXml);
            }
            catch (Exception exception)
            {
                snapshot.Availability = PnP.Framework.Migration.Evidence.EvidenceAvailability.Partial;
                snapshot.Diagnostics.Add("Field.SchemaXml could not be canonicalized: " + exception.Message);
            }
            if (taxonomy != null)
            {
                snapshot.Sources = taxonomy.Sources.ToList();
                snapshot.Diagnostics = snapshot.Diagnostics.Concat(taxonomy.Diagnostics).ToList();
                if (!taxonomy.IsComplete)
                {
                    snapshot.Availability = PnP.Framework.Migration.Evidence.EvidenceAvailability.Partial;
                }
            }
            return snapshot;
        }

        private static Guid? ParseGuid(string value)
        {
            Guid result;
            return Guid.TryParse((value ?? string.Empty).Trim().Trim('{', '}'), out result) && result != Guid.Empty ? result : (Guid?)null;
        }
    }
}
