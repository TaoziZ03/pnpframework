using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Schema.ContentTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageIngredientSourceBinding
    {
        public static string SourceIdentity(PublishingPageCaptureBundle snapshot, string ingredientId)
        {
            var source = snapshot?.Source;
            if (source == null
                || source.SiteId == Guid.Empty
                || source.WebId == Guid.Empty
                || source.FileUniqueId == Guid.Empty
                || string.IsNullOrWhiteSpace(source.PageServerRelativeUrl)
                || string.IsNullOrWhiteSpace(ingredientId))
            {
                return null;
            }
            return string.Join("/", new[]
            {
                source.SiteId.ToString("D"),
                source.WebId.ToString("D"),
                source.ListItemId.ToString(CultureInfo.InvariantCulture),
                source.FileUniqueId.ToString("D"),
                source.PageServerRelativeUrl,
                ingredientId
            });
        }

        public static string SourceVersionIdentity(PublishingPageCaptureBundle snapshot)
        {
            var fence = snapshot?.SourceFence;
            var source = snapshot?.Source;
            var versionLabel = fence?.VersionLabel ?? source?.VersionLabel;
            var modifiedUtc = fence?.ModifiedUtc ?? source?.ModifiedUtc ?? DateTime.MinValue;
            var length = fence?.Length ?? source?.Length ?? -1;
            if (string.IsNullOrWhiteSpace(versionLabel)
                || modifiedUtc == DateTime.MinValue
                || length < 0)
            {
                return null;
            }
            return "version=" + versionLabel
                + ";modified=" + modifiedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                + ";length=" + length.ToString(CultureInfo.InvariantCulture);
        }

        public static bool HasExactSourceBinding(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            var expectedIdentity = SourceIdentity(snapshot, node?.Id);
            var expectedVersion = SourceVersionIdentity(snapshot);
            return expectedIdentity != null
                && expectedVersion != null
                && string.Equals(node.SourcePageOrListItemIdentity, expectedIdentity, StringComparison.Ordinal)
                && string.Equals(node.SourceVersionIdentity, expectedVersion, StringComparison.Ordinal);
        }

        public static string BindBuiltInEvidence(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            var evidence = FindNativeEvidence(snapshot, node);
            if (evidence == null)
            {
                return null;
            }
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(evidence));
        }

        public static bool HasBoundEvidence(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            if (!IsSha256(node?.EvidenceDigest))
            {
                return false;
            }
            var nativeDigest = BindBuiltInEvidence(snapshot, node);
            if (nativeDigest != null)
            {
                return string.Equals(nativeDigest, node.EvidenceDigest, StringComparison.OrdinalIgnoreCase);
            }
            return MatchesExtensionEnvelope(snapshot, node);
        }

        public static bool MatchesExtensionEnvelope(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            if (node == null || !IsSha256(node.EvidenceDigest))
            {
                return false;
            }
            var nodeReferences = node.EvidenceReferences ?? Array.Empty<string>();
            return (snapshot?.IngredientEvidence ?? Array.Empty<PublishingPageIngredientEvidenceEnvelope>())
                .Any(value => value != null
                    && string.Equals(value.LaneId, node.PrimaryOwnerLane, StringComparison.Ordinal)
                    && string.Equals(value.EvidenceDigest, node.EvidenceDigest, StringComparison.OrdinalIgnoreCase)
                    && nodeReferences.SequenceEqual(
                        value.EvidenceReferences ?? Array.Empty<string>(),
                        StringComparer.Ordinal));
        }

        public static bool IsSha256(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(character => (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }

        public static PageReferenceSnapshot FindDirectReference(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            var references = new HashSet<string>(
                node?.EvidenceReferences ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            return (snapshot?.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .FirstOrDefault(value => value != null
                    && (references.Contains(value.Id)
                        || references.Contains(PublishingPageIngredientIds.Reference(value.Id))));
        }

        private static object FindNativeEvidence(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            if (snapshot == null || node == null)
            {
                return null;
            }
            switch (node.SourcePredicateId)
            {
                case "runtime.page.capture":
                    return string.Equals(node.Id, PublishingPageIngredientIds.Runtime, StringComparison.Ordinal)
                        ? snapshot.Runtime : null;
                case "page-artifact.captured-spfile":
                    return string.Equals(node.Id, PublishingPageIngredientIds.PageArtifact, StringComparison.Ordinal)
                        && snapshot.PageArtifact?.FileUniqueId == snapshot.Source?.FileUniqueId
                        ? snapshot.PageArtifact : null;
                case "layout.typed-snapshot":
                    return string.Equals(node.Id, PublishingPageIngredientIds.Layout, StringComparison.Ordinal)
                        ? snapshot.Layout : null;
                case "content-type.page-binding":
                    return string.Equals(node.Id, PublishingPageIngredientIds.ContentType, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(snapshot.Source?.ContentTypeId)
                        ? new SourceContentTypeEvidence(snapshot.Source.ContentTypeId, snapshot.Source.ContentTypeName) : null;
                case "content.publishing-page-field":
                    return string.Equals(node.Id, PublishingPageIngredientIds.PublishingContent, StringComparison.Ordinal)
                        && snapshot.PublishingPageContent != null
                        ? new BodyEvidence("PublishingPageContent", snapshot.PublishingPageContent, snapshot.PublishingPageContentSha256) : null;
                case "field.body-only":
                    return FindBodyField(snapshot, node.Id);
                case "field.page-content-type-closure":
                    return (snapshot.Layout?.AssociatedContentTypeSchema?.RequiredFieldClosure ?? Array.Empty<Schema.Fields.FieldSchemaSnapshot>())
                        .FirstOrDefault(value => value != null
                            && string.Equals(PublishingPageIngredientIds.PageContentTypeField(value.Id), node.Id, StringComparison.Ordinal));
                case "field.non-body":
                    return FindNonBodyField(snapshot, node.Id);
                case "content-type.generic-schema":
                    return FindGenericContentType(snapshot, node.Id);
                case "webpart.classic-export":
                    return (snapshot.WebParts ?? Array.Empty<Pages.ClassicWebParts.ClassicWebPartSnapshot>())
                        .FirstOrDefault(value => value != null
                            && string.Equals(PublishingPageIngredientIds.WebPart(value.Id), node.Id, StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(value.ExportSha256));
                case "list.captured-closure":
                    return (snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
                        .FirstOrDefault(value => value != null
                            && string.Equals(PublishingPageIngredientIds.List(value.SourceWebId, value.SourceListId), node.Id, StringComparison.Ordinal));
                case "view.captured-list-view":
                    return snapshot.ListDependencies?.Where(value => value != null)
                        .SelectMany(value => value.Views ?? Array.Empty<Lists.Views.ListViewSnapshot>())
                        .FirstOrDefault(value => value != null
                            && snapshot.ListDependencies.Any(list => list != null
                                && (list.Views ?? Array.Empty<Lists.Views.ListViewSnapshot>()).Contains(value)
                                && string.Equals(PublishingPageIngredientIds.View(list.SourceWebId, list.SourceListId, value.Id), node.Id, StringComparison.Ordinal)));
                case "asset.typed-image":
                case "asset.direct-page-file":
                case "asset.script-source":
                case "asset.non-image-file-script":
                    return FindAsset(snapshot, node);
                case "taxonomy.typed-relationship":
                    return FindTaxonomyRelationship(snapshot, node.Id);
                case "reference.kind-image":
                case "reference.kind-script":
                case "reference.kind-iframe":
                case "reference.direct-file-binding":
                case "reference.non-image-file-script-iframe":
                    return FindReference(snapshot, node.Id);
                case "topology.typed-plan":
                    return (object)snapshot.SourceTopology ?? snapshot.PathDerivedTopologyEvidence;
                case "security.typed-snapshot":
                    return snapshot.Security;
                case "lifecycle.typed-state":
                    return snapshot.Lifecycle;
                case "web.topology-member":
                    return FindWeb(snapshot, node.Id);
                case "list-item.captured-current-state":
                    return FindListItem(snapshot, node.Id);
                case "document.list-item-member":
                    return FindListDocument(snapshot, node.Id);
                case "attachment.list-item-member":
                    return FindListAttachment(snapshot, node.Id);
                case "platform-feature.typed-requirement":
                    return FindPlatformFeature(snapshot, node.Id);
                case "policy.typed-contract":
                    return FindPolicy(snapshot, node.Id);
                default:
                    return null;
            }
        }

        private static object FindBodyField(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            var internalName = FieldInternalName(nodeId);
            if (string.Equals(internalName, "PublishingPageContent", StringComparison.OrdinalIgnoreCase))
            {
                return snapshot.PublishingPageContent == null
                    ? null
                    : new BodyEvidence("PublishingPageContent", snapshot.PublishingPageContent, snapshot.PublishingPageContentSha256);
            }
            return (snapshot.Fields ?? Array.Empty<Pages.Fields.PageFieldValueSnapshot>())
                .FirstOrDefault(value => value != null
                    && string.Equals(value.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
        }

        private static object FindNonBodyField(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            var internalName = FieldInternalName(nodeId);
            if (!string.IsNullOrWhiteSpace(internalName)
                && !string.Equals(internalName, "WikiField", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(internalName, "PublishingPageContent", StringComparison.OrdinalIgnoreCase))
            {
                var pageField = (snapshot.Fields ?? Array.Empty<Pages.Fields.PageFieldValueSnapshot>())
                    .FirstOrDefault(value => value != null
                        && string.Equals(value.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
                if (pageField != null)
                {
                    return pageField;
                }
            }
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                var listField = (list?.Fields ?? Array.Empty<Lists.Fields.ListFieldSnapshot>())
                    .FirstOrDefault(value => value != null
                        && string.Equals(PublishingPageIngredientIds.ListField(list.SourceWebId, list.SourceListId, value.Id), nodeId, StringComparison.Ordinal));
                if (listField != null)
                {
                    return listField;
                }
                var siteField = (list?.SiteContentTypes ?? Array.Empty<Schema.ContentTypes.ContentTypeSchemaSnapshot>())
                    .SelectMany(value => value?.RequiredFieldClosure ?? Array.Empty<Schema.Fields.FieldSchemaSnapshot>())
                    .FirstOrDefault(value => value != null
                        && (list.SiteContentTypes ?? Array.Empty<Schema.ContentTypes.ContentTypeSchemaSnapshot>())
                            .Any(schema => schema != null
                                && (schema.RequiredFieldClosure ?? Array.Empty<Schema.Fields.FieldSchemaSnapshot>()).Contains(value)
                                && string.Equals(
                                    PublishingPageIngredientIds.SiteField(
                                        PublishingPageListSchemaIngredientGraphProjector.SchemaScope(schema),
                                        value.Id),
                                    nodeId,
                                    StringComparison.Ordinal)));
                if (siteField != null)
                {
                    return siteField;
                }
            }
            return null;
        }

        private static object FindGenericContentType(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                var listType = (list?.ContentTypes ?? Array.Empty<Lists.ContentTypes.ListContentTypeSnapshot>())
                    .FirstOrDefault(value => value != null
                        && string.Equals(PublishingPageIngredientIds.ListContentType(list.SourceWebId, list.SourceListId, value.Id), nodeId, StringComparison.Ordinal));
                if (listType != null)
                {
                    return listType;
                }
                var siteType = (list?.SiteContentTypes ?? Array.Empty<Schema.ContentTypes.ContentTypeSchemaSnapshot>())
                    .FirstOrDefault(value => value != null
                        && string.Equals(
                            PublishingPageIngredientIds.SiteContentType(
                                PublishingPageListSchemaIngredientGraphProjector.SchemaScope(value),
                                value.ContentTypeId),
                            nodeId,
                            StringComparison.Ordinal));
                if (siteType != null)
                {
                    return siteType;
                }
            }
            return null;
        }

        private static object FindAsset(PublishingPageCaptureBundle snapshot, PageIngredientNode node)
        {
            var layoutResource = (snapshot.Layout?.ResourceArtifacts ?? Array.Empty<Layouts.PublishingPageLayoutResourceSnapshot>())
                .FirstOrDefault(value => value != null
                    && string.Equals(
                        PublishingPageIngredientIds.LayoutResource(value.Reference?.Value ?? value.ResolvedSourceUrl ?? string.Empty),
                        node.Id,
                        StringComparison.Ordinal));
            if (layoutResource != null)
            {
                return layoutResource;
            }
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                var resource = (list?.ViewRenderingResources ?? Array.Empty<Lists.Views.ListViewRenderingResourceSnapshot>())
                    .FirstOrDefault(value => value != null
                        && string.Equals(PublishingPageIngredientIds.ViewRenderingResource(list.SourceSiteId, value.Id), node.Id, StringComparison.Ordinal));
                if (resource != null)
                {
                    return resource;
                }
            }
            return null;
        }

        private static object FindTaxonomyRelationship(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            return (snapshot.Fields ?? Array.Empty<Pages.Fields.PageFieldValueSnapshot>())
                .Where(value => value != null)
                .SelectMany(field => (field.TaxonomyValues ?? Array.Empty<Pages.Fields.PageTaxonomyValueSnapshot>())
                    .Where(value => value != null)
                    .Select(value => new { Field = field, Value = value }))
                .FirstOrDefault(value => Guid.TryParse(value.Value.TermGuid, out var termId)
                    && string.Equals(PublishingPageIngredientIds.TaxonomyRelationship(value.Field.Id, termId, value.Value.WssId), nodeId, StringComparison.Ordinal));
        }

        private static PageReferenceSnapshot FindReference(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            var referenceId = nodeId?.StartsWith("reference:", StringComparison.Ordinal) == true
                ? nodeId.Substring("reference:".Length)
                : null;
            return (snapshot.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .FirstOrDefault(value => string.Equals(value?.Id, referenceId, StringComparison.Ordinal));
        }

        private static object FindWeb(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            if (snapshot.Source != null
                && string.Equals(
                    PublishingPageIngredientIds.Web(snapshot.Source.SiteId, snapshot.Source.WebId),
                    nodeId,
                    StringComparison.Ordinal))
            {
                return snapshot.Source;
            }
            var complete = snapshot.SourceTopology?.Webs?.FirstOrDefault(value => value != null
                && string.Equals(PublishingPageIngredientIds.Web(value.SiteId, value.WebId), nodeId, StringComparison.Ordinal));
            if (complete != null)
            {
                return complete;
            }
            return (snapshot.PathDerivedTopologyEvidence?.CapturedWebs ?? Array.Empty<Topology.SourceWebSnapshot>())
                .FirstOrDefault(value => value != null
                    && string.Equals(PublishingPageIngredientIds.Web(value.SiteId, value.WebId), nodeId, StringComparison.Ordinal));
        }

        private static object FindListItem(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                var item = (list?.Items ?? Array.Empty<Lists.Items.ListItemSnapshot>())
                    .FirstOrDefault(value => value != null
                        && string.Equals(PublishingPageIngredientIds.ListItem(list.SourceWebId, list.SourceListId, value.SourceItemId), nodeId, StringComparison.Ordinal));
                if (item != null)
                {
                    return item;
                }
            }
            return null;
        }

        private static object FindListDocument(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                var item = (list?.Items ?? Array.Empty<Lists.Items.ListItemSnapshot>())
                    .FirstOrDefault(value => value?.Document != null
                        && string.Equals(PublishingPageIngredientIds.ListDocument(list.SourceWebId, list.SourceListId, value.SourceItemId), nodeId, StringComparison.Ordinal));
                if (item != null)
                {
                    return item.Document;
                }
            }
            return null;
        }

        private static object FindListAttachment(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                foreach (var item in list?.Items ?? Array.Empty<Lists.Items.ListItemSnapshot>())
                {
                    var attachment = (item?.Attachments ?? Array.Empty<Lists.Items.ListAttachmentSnapshot>())
                        .FirstOrDefault(value => value != null
                            && string.Equals(PublishingPageIngredientIds.ListAttachment(list.SourceWebId, list.SourceListId, item.SourceItemId, value.FileName), nodeId, StringComparison.Ordinal));
                    if (attachment != null)
                    {
                        return attachment;
                    }
                }
            }
            return null;
        }

        private static object FindPlatformFeature(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                var feature = ContentTypeRuntimeCatalog.CreateFeatureRequirements(
                        (list.ContentTypes ?? Array.Empty<Lists.ContentTypes.ListContentTypeSnapshot>()).Select(value => value?.ParentId),
                        list.SiteContentTypes,
                        list.SourceWebUrl)
                    .FirstOrDefault(value => value != null
                        && string.Equals(PublishingPageIngredientIds.PlatformFeature(list.SourceSiteId, value.FeatureId), nodeId, StringComparison.Ordinal));
                if (feature != null)
                {
                    return feature;
                }
            }
            return null;
        }

        private static object FindPolicy(PublishingPageCaptureBundle snapshot, string nodeId)
        {
            foreach (var list in snapshot.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
            {
                foreach (var item in list?.Items ?? Array.Empty<Lists.Items.ListItemSnapshot>())
                {
                    if (item?.Document?.InformationProtection != null
                        && string.Equals(PublishingPageIngredientIds.ListDocumentInformationProtection(
                            list.SourceWebId,
                            list.SourceListId,
                            item.SourceItemId), nodeId, StringComparison.Ordinal))
                    {
                        return item.Document.InformationProtection;
                    }
                }
            }
            return null;
        }

        private static string FieldInternalName(string nodeId)
        {
            return nodeId?.StartsWith("field:", StringComparison.Ordinal) == true
                ? nodeId.Substring("field:".Length)
                : null;
        }

        private sealed class SourceContentTypeEvidence
        {
            public SourceContentTypeEvidence(string id, string name)
            {
                Id = id;
                Name = name;
            }

            public string Id { get; }
            public string Name { get; }
        }

        private sealed class BodyEvidence
        {
            public BodyEvidence(string internalName, string value, string digest)
            {
                InternalName = internalName;
                Value = value;
                Digest = digest;
            }

            public string InternalName { get; }
            public string Value { get; }
            public string Digest { get; }
        }
    }
}
