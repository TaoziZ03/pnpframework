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
        public static IList<ListFieldSnapshot> Read(
            ClientContext context,
            string sourceWebUrl,
            Guid sourceListId,
            FieldCollection fields)
        {
            var taxonomyFields = TaxonomyFieldBindingSnapshotReader.ReadAll(
                fields.Where(value => value.TypeAsString.StartsWith("TaxonomyFieldType", StringComparison.OrdinalIgnoreCase)),
                field => field.Id,
                field => field.InternalName,
                field => field.SchemaXml,
                values => ReadTypedTaxonomyBindings(context, values),
                field => ReadTypedTaxonomyBinding(context, sourceWebUrl, sourceListId, field.Id));

            var snapshots = fields.Select(field => Create(field, taxonomyFields))
                .OrderBy(value => value.InternalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ValidateTaxonomyCompanionClosure(snapshots);
            return snapshots;
        }

        internal static bool ValidateTaxonomyCompanionClosure(IEnumerable<ListFieldSnapshot> fields)
        {
            var closure = (fields ?? Enumerable.Empty<ListFieldSnapshot>())
                .Where(value => value != null)
                .ToArray();
            var complete = true;
            foreach (var taxonomyField in closure.Where(value => value.Taxonomy != null))
            {
                string companionDiagnostic;
                if (TaxonomyFieldBindingSnapshotReader.TryValidateHiddenTextCompanion(
                    taxonomyField.Id,
                    taxonomyField.Taxonomy,
                    closure,
                    value => value.Id,
                    value => value.TypeAsString,
                    value => value.Hidden,
                    out companionDiagnostic))
                {
                    continue;
                }
                complete = false;
                taxonomyField.Availability = PnP.Framework.Migration.Evidence.EvidenceAvailability.Partial;
                taxonomyField.Diagnostics.Add(
                    "TaxonomyBindingHiddenTextCompanionInvalid: " + companionDiagnostic + ".");
            }
            return complete;
        }

        private static TaxonomyFieldBindingSnapshot ReadTypedTaxonomyBinding(
            ClientContext context,
            string sourceWebUrl,
            Guid sourceListId,
            Guid fieldId)
        {
            using (var isolatedContext = context.Clone(sourceWebUrl))
            {
                var field = isolatedContext.Web.Lists.GetById(sourceListId).Fields.GetById(fieldId);
                var taxonomy = isolatedContext.CastTo<Microsoft.SharePoint.Client.Taxonomy.TaxonomyField>(field);
                isolatedContext.Load(
                    taxonomy,
                    value => value.SspId,
                    value => value.TermSetId,
                    value => value.AnchorId,
                    value => value.TextField,
                    value => value.Open);
                isolatedContext.ExecuteQueryRetry();
                return new TaxonomyFieldBindingSnapshot
                {
                    SourceTermStoreId = taxonomy.SspId,
                    SourceTermSetId = taxonomy.TermSetId,
                    AnchorTermId = taxonomy.AnchorId,
                    HiddenTextFieldId = taxonomy.TextField,
                    Open = taxonomy.Open
                };
            }
        }

        private static IDictionary<Guid, TaxonomyFieldBindingSnapshot> ReadTypedTaxonomyBindings(
            ClientContext context,
            IEnumerable<Field> fields)
        {
            var typed = new Dictionary<Guid, Microsoft.SharePoint.Client.Taxonomy.TaxonomyField>();
            foreach (var field in fields)
            {
                var taxonomy = context.CastTo<Microsoft.SharePoint.Client.Taxonomy.TaxonomyField>(field);
                context.Load(
                    taxonomy,
                    value => value.SspId,
                    value => value.TermSetId,
                    value => value.AnchorId,
                    value => value.TextField,
                    value => value.Open);
                typed.Add(field.Id, taxonomy);
            }
            if (typed.Count > 0)
            {
                context.ExecuteQueryRetry();
            }
            return typed.ToDictionary(
                value => value.Key,
                value => new TaxonomyFieldBindingSnapshot
                {
                    SourceTermStoreId = value.Value.SspId,
                    SourceTermSetId = value.Value.TermSetId,
                    AnchorTermId = value.Value.AnchorId,
                    HiddenTextFieldId = value.Value.TextField,
                    Open = value.Value.Open
                });
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
                snapshot.Diagnostics.Add(
                    "FieldSchemaCanonicalizationFailed: exceptionType=" + exception.GetType().FullName + ".");
            }
            if (taxonomy != null)
            {
                snapshot.Sources = taxonomy.Sources.Count == 0 ? null : taxonomy.Sources.ToList();
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
