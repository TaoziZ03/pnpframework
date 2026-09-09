using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientPrimaryOwnerRegistry
    {
        public const string SchemaVersion = "pnp-page-ingredient-primary-owner-registry/v1";

        private readonly IReadOnlyDictionary<string, Func<IngredientOwnershipSourceContext, bool>> predicates;

        public static PublishingPageIngredientPrimaryOwnerRegistry Default { get; } = CreateDefault();

        public PublishingPageIngredientPrimaryOwnerRegistry(
            IEnumerable<PageIngredientPrimaryOwnerDescriptor> entries,
            IReadOnlyDictionary<string, Func<IngredientOwnershipSourceContext, bool>> sourcePredicates)
        {
            var frozenEntries = (entries ?? throw new ArgumentNullException(nameof(entries)))
                .Select(value => value ?? throw new ArgumentException("The primary-owner registry cannot contain null entries.", nameof(entries)))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
            var duplicate = frozenEntries.GroupBy(value => value.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException($"Duplicate primary-owner registry entry ID '{duplicate.Key}'.", nameof(entries));
            }

            var suppliedPredicates = sourcePredicates
                ?? throw new ArgumentNullException(nameof(sourcePredicates));
            var missingPredicate = frozenEntries.FirstOrDefault(value =>
                !suppliedPredicates.TryGetValue(value.SourcePredicateId, out var predicate) || predicate == null);
            if (missingPredicate != null)
            {
                throw new ArgumentException(
                    $"Primary-owner entry '{missingPredicate.Id}' has unbound source predicate '{missingPredicate.SourcePredicateId}'.",
                    nameof(sourcePredicates));
            }

            Entries = new ReadOnlyCollection<PageIngredientPrimaryOwnerDescriptor>(frozenEntries);
            predicates = new ReadOnlyDictionary<string, Func<IngredientOwnershipSourceContext, bool>>(
                suppliedPredicates.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
        }

        public IReadOnlyList<PageIngredientPrimaryOwnerDescriptor> Entries { get; }

        public PageIngredientPrimaryOwnerDescriptor Resolve(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            if (node == null)
            {
                throw new InvalidDataException("A primary-owner claim requires an ingredient node.");
            }

            var context = new IngredientOwnershipSourceContext(snapshot, node);
            var matches = Entries.Where(value => value.MatchesTuple(node)
                    && predicates[value.SourcePredicateId](context))
                .ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidDataException(
                    $"Ingredient '{node.Id}' has no primary owner for tuple "
                    + $"({node.Kind}, {node.Subtype}, {node.SemanticRole}, {node.SourcePredicateId}).");
            }
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Ingredient '{node.Id}' has overlapping primary-owner predicates: "
                    + string.Join(", ", matches.Select(value => value.Id)) + ".");
            }
            return matches[0];
        }

        internal void BindBuiltInNodes(
            PublishingPageCaptureBundle snapshot,
            CanonicalPageIngredientGraph graph)
        {
            foreach (var node in graph?.Nodes ?? Array.Empty<PageIngredientNode>())
            {
                BindBuiltInNode(snapshot, node);
                var owner = Resolve(snapshot, node);
                node.PrimaryOwnerLane = owner.PrimaryOwnerLane;
            }
        }

        private static void BindBuiltInNode(PublishingPageCaptureBundle snapshot, PageIngredientNode node)
        {
            var tuple = Classify(snapshot, node);
            node.Subtype = tuple.Subtype;
            node.SemanticRole = tuple.SemanticRole;
            node.SourcePredicateId = tuple.PredicateId;
            node.SourcePageOrListItemIdentity = PublishingPageIngredientSourceBinding.SourceIdentity(snapshot, node.Id);
            node.SourceVersionIdentity = PublishingPageIngredientSourceBinding.SourceVersionIdentity(snapshot);
            node.EvidenceDigest = PublishingPageIngredientSourceBinding.BindBuiltInEvidence(snapshot, node);
            if (!PublishingPageIngredientSourceBinding.IsSha256(node.EvidenceDigest))
            {
                throw new InvalidDataException(
                    $"Ingredient '{node.Id}' cannot bind primary ownership without typed source evidence.");
            }
        }

        private static (string Subtype, string SemanticRole, string PredicateId) Classify(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            switch (node.Kind)
            {
                case PageIngredientKind.Runtime:
                    return ("runtime.page", "page-runtime", "runtime.page.capture");
                case PageIngredientKind.PageArtifact:
                    return ("page-artifact.source-file", "page-container-file", "page-artifact.captured-spfile");
                case PageIngredientKind.Layout:
                    return ("layout.page", "layout-container-or-binding", "layout.typed-snapshot");
                case PageIngredientKind.ContentType:
                    return string.Equals(node.Id, PublishingPageIngredientIds.ContentType, StringComparison.Ordinal)
                        ? ("content-type.page-layout-binding", "page-layout-content-type-binding", "content-type.page-binding")
                        : ("content-type.generic-schema", "schema-closure", "content-type.generic-schema");
                case PageIngredientKind.Content:
                    return ("content.publishing-page-content", "body-content", "content.publishing-page-field");
                case PageIngredientKind.Field:
                    if (string.Equals(node.Id, "field:WikiField", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(node.Id, "field:PublishingPageContent", StringComparison.OrdinalIgnoreCase))
                    {
                        return ("field.body-value", "body-field-value", "field.body-only");
                    }
                    return node.Id?.StartsWith("page-content-type-field:", StringComparison.Ordinal) == true
                        ? ("field.page-content-type-schema", "page-content-type-field-schema", "field.page-content-type-closure")
                        : ("field.generic-value-or-schema", "non-body-field", "field.non-body");
                case PageIngredientKind.WebPart:
                    return ("classic.webpart.instance", "persisted-instance", "webpart.classic-export");
                case PageIngredientKind.List:
                    return ("list.generic", "dependency-provider", "list.captured-closure");
                case PageIngredientKind.View:
                    return ("view.generic", "dependency-provider", "view.captured-list-view");
                case PageIngredientKind.Asset:
                    return AssetTuple(snapshot, node);
                case PageIngredientKind.Taxonomy:
                    return ("taxonomy.generic", "taxonomy-binding-or-term-relationship", "taxonomy.typed-relationship");
                case PageIngredientKind.Reference:
                    return ReferenceTuple(snapshot, node);
                case PageIngredientKind.Topology:
                    return ("topology.generic", "target-topology", "topology.typed-plan");
                case PageIngredientKind.Security:
                    return ("security.generic", "security-contract", "security.typed-snapshot");
                case PageIngredientKind.Lifecycle:
                    return ("lifecycle.generic", "page-or-item-lifecycle", "lifecycle.typed-state");
                case PageIngredientKind.Service:
                    return ("service.generic", "service-contract", "service.typed-contract");
                case PageIngredientKind.Web:
                    return ("web.generic", "materialization-owner-web", "web.topology-member");
                case PageIngredientKind.ListItem:
                    return ("list-item.generic", "dependency-provider-item", "list-item.captured-current-state");
                case PageIngredientKind.Document:
                    return node.Id?.StartsWith("document:page-reference:", StringComparison.Ordinal) == true
                        ? ("document.page-referenced-file", "direct-page-dependency", "document.direct-page-reference")
                        : ("document.list-closure", "generic-list-document", "document.list-item-member");
                case PageIngredientKind.Attachment:
                    return node.Id?.StartsWith("attachment:page-reference:", StringComparison.Ordinal) == true
                        ? ("attachment.page-referenced-file", "direct-page-dependency", "attachment.direct-page-reference")
                        : ("attachment.list-closure", "generic-list-attachment", "attachment.list-item-member");
                case PageIngredientKind.PlatformFeature:
                    return ("platform-feature.generic", "platform-capability-contract", "platform-feature.typed-requirement");
                case PageIngredientKind.Policy:
                    return ("policy.generic", "policy-contract", "policy.typed-contract");
                default:
                    throw new InvalidDataException($"Ingredient '{node.Id}' has unsupported legacy kind '{node.Kind}'.");
            }
        }

        private static (string Subtype, string SemanticRole, string PredicateId) ReferenceTuple(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            var referenceId = node.Id?.StartsWith("reference:", StringComparison.Ordinal) == true
                ? node.Id.Substring("reference:".Length)
                : null;
            var reference = (snapshot?.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .FirstOrDefault(value => string.Equals(value?.Id, referenceId, StringComparison.Ordinal));
            switch (reference?.Kind ?? PageReferenceKind.Unknown)
            {
                case PageReferenceKind.Image:
                    return ("reference.image", "typed-image-reference", "reference.kind-image");
                case PageReferenceKind.Script:
                    return ("reference.script", "typed-script-reference", "reference.kind-script");
                case PageReferenceKind.IFrame:
                    return ("reference.embed.iframe", "embedded-frame-reference", "reference.kind-iframe");
                case PageReferenceKind.Anchor when reference.IsRenderableResource && !string.IsNullOrWhiteSpace(reference.ContentSha256):
                    return ("reference.file", "typed-file-reference", "reference.direct-file-binding");
                default:
                    return ("reference.other", "non-lane-reference", "reference.non-image-file-script-iframe");
            }
        }

        private static (string Subtype, string SemanticRole, string PredicateId) AssetTuple(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            if (node.Id?.StartsWith("view-rendering-resource:", StringComparison.Ordinal) == true)
            {
                var resource = (snapshot?.ListDependencies ?? Array.Empty<Lists.Capture.ListDependencySnapshot>())
                    .Where(value => value != null)
                    .SelectMany(value => (value.ViewRenderingResources ?? Array.Empty<Lists.Views.ListViewRenderingResourceSnapshot>())
                        .Select(resourceValue => new { List = value, Resource = resourceValue }))
                    .FirstOrDefault(value => value.Resource != null
                        && string.Equals(
                            PublishingPageIngredientIds.ViewRenderingResource(value.List.SourceSiteId, value.Resource.Id),
                            node.Id,
                            StringComparison.Ordinal));
                if (resource?.Resource.Kind == Lists.Views.ListViewRenderingResourceKind.JavaScript)
                {
                    return ("asset.script", "script-bytes-or-inline-binding", "asset.script-source");
                }
            }
            if (node.Id?.StartsWith("layout-resource:", StringComparison.Ordinal) == true)
            {
                var value = node.Label ?? node.Id;
                if (value.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    return ("asset.script", "script-bytes-or-inline-binding", "asset.script-source");
                }
                if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".ico" }
                    .Any(value.EndsWith))
                {
                    return ("asset.image", "rendered-image-bytes", "asset.typed-image");
                }
                return ("asset.page-referenced-file", "page-referenced-file-bytes", "asset.direct-page-file");
            }
            return ("asset.other", "unassigned-typed-asset", "asset.non-image-file-script");
        }

        private static PublishingPageIngredientPrimaryOwnerRegistry CreateDefault()
        {
            var entries = new[]
            {
                Entry("runtime.page", PageIngredientKind.Runtime, "runtime.page", "page-runtime", "runtime.page.capture", "page.layout"),
                Entry("runtime.dynamic-region", PageIngredientKind.Runtime, "runtime.dynamic-region", "provider-derived-runtime-region", "runtime.dynamic-region.typed-provider-binding", "dynamic.region"),
                Entry("page-artifact.source-file", PageIngredientKind.PageArtifact, "page-artifact.source-file", "page-container-file", "page-artifact.captured-spfile", "shared.cross-site-repro-integration"),
                Entry("layout.any", PageIngredientKind.Layout, "layout.*", "layout-container-or-binding", "layout.typed-snapshot", "page.layout"),
                Entry("content-type.page-layout-binding", PageIngredientKind.ContentType, "content-type.page-layout-binding", "page-layout-content-type-binding", "content-type.page-binding", "page.layout"),
                Entry("content-type.generic-schema", PageIngredientKind.ContentType, "content-type.generic-schema", "schema-closure", "content-type.generic-schema", "shared.pnp-framework"),
                Entry("content.wiki-field", PageIngredientKind.Content, "content.wiki-field", "body-content", "content.classic-wiki-field", "content.text"),
                Entry("content.publishing-page-content", PageIngredientKind.Content, "content.publishing-page-content", "body-content", "content.publishing-page-field", "content.text"),
                Entry("field.body-value", PageIngredientKind.Field, "field.body-value", "body-field-value", "field.body-only", "content.text"),
                Entry("field.page-content-type-schema", PageIngredientKind.Field, "field.page-content-type-schema", "page-content-type-field-schema", "field.page-content-type-closure", "shared.pnp-framework"),
                Entry("field.generic", PageIngredientKind.Field, "field.generic-value-or-schema", "non-body-field", "field.non-body", "shared.pnp-framework"),
                Entry("webpart.classic-instance", PageIngredientKind.WebPart, "classic.webpart.instance", "persisted-instance", "webpart.classic-export", "webpart.instance"),
                Entry("list.generic", PageIngredientKind.List, "list.generic", "dependency-provider", "list.captured-closure", "shared.cross-site-repro-integration"),
                Entry("view.generic", PageIngredientKind.View, "view.generic", "dependency-provider", "view.captured-list-view", "shared.cross-site-repro-integration"),
                Entry("asset.image", PageIngredientKind.Asset, "asset.image", "rendered-image-bytes", "asset.typed-image", "resource.image"),
                Entry("asset.page-referenced-file", PageIngredientKind.Asset, "asset.page-referenced-file", "page-referenced-file-bytes", "asset.direct-page-file", "resource.image"),
                Entry("asset.script", PageIngredientKind.Asset, "asset.script", "script-bytes-or-inline-binding", "asset.script-source", "resource.script"),
                Entry("asset.other", PageIngredientKind.Asset, "asset.other", "unassigned-typed-asset", "asset.non-image-file-script", "shared.pnp-framework"),
                Entry("taxonomy.generic", PageIngredientKind.Taxonomy, "taxonomy.generic", "taxonomy-binding-or-term-relationship", "taxonomy.typed-relationship", "shared.pnp-framework"),
                Entry("reference.image", PageIngredientKind.Reference, "reference.image", "typed-image-reference", "reference.kind-image", "resource.image"),
                Entry("reference.file", PageIngredientKind.Reference, "reference.file", "typed-file-reference", "reference.direct-file-binding", "resource.image"),
                Entry("reference.script", PageIngredientKind.Reference, "reference.script", "typed-script-reference", "reference.kind-script", "resource.script"),
                Entry("reference.embed.iframe", PageIngredientKind.Reference, "reference.embed.iframe", "embedded-frame-reference", "reference.kind-iframe", "embed.iframe"),
                Entry("reference.other", PageIngredientKind.Reference, "reference.other", "non-lane-reference", "reference.non-image-file-script-iframe", "shared.pnp-framework"),
                Entry("topology.generic", PageIngredientKind.Topology, "topology.generic", "target-topology", "topology.typed-plan", "shared.cross-site-repro-integration"),
                Entry("security.generic", PageIngredientKind.Security, "security.generic", "security-contract", "security.typed-snapshot", "shared.pnp-framework"),
                Entry("lifecycle.generic", PageIngredientKind.Lifecycle, "lifecycle.generic", "page-or-item-lifecycle", "lifecycle.typed-state", "shared.cross-site-repro-integration"),
                Entry("service.generic", PageIngredientKind.Service, "service.generic", "service-contract", "service.typed-contract", "shared.pnp-framework"),
                Entry("web.generic", PageIngredientKind.Web, "web.generic", "materialization-owner-web", "web.topology-member", "shared.cross-site-repro-integration"),
                Entry("list-item.generic", PageIngredientKind.ListItem, "list-item.generic", "dependency-provider-item", "list-item.captured-current-state", "shared.cross-site-repro-integration"),
                Entry("document.page-referenced", PageIngredientKind.Document, "document.page-referenced-file", "direct-page-dependency", "document.direct-page-reference", "resource.image"),
                Entry("document.list-closure", PageIngredientKind.Document, "document.list-closure", "generic-list-document", "document.list-item-member", "shared.cross-site-repro-integration"),
                Entry("attachment.page-referenced", PageIngredientKind.Attachment, "attachment.page-referenced-file", "direct-page-dependency", "attachment.direct-page-reference", "resource.image"),
                Entry("attachment.list-closure", PageIngredientKind.Attachment, "attachment.list-closure", "generic-list-attachment", "attachment.list-item-member", "shared.cross-site-repro-integration"),
                Entry("platform-feature.generic", PageIngredientKind.PlatformFeature, "platform-feature.generic", "platform-capability-contract", "platform-feature.typed-requirement", "shared.pnp-framework"),
                Entry("policy.generic", PageIngredientKind.Policy, "policy.generic", "policy-contract", "policy.typed-contract", "shared.pnp-framework")
            };
            var predicateIds = entries.Select(value => value.SourcePredicateId).Distinct(StringComparer.Ordinal);
            return new PublishingPageIngredientPrimaryOwnerRegistry(
                entries,
                predicateIds.ToDictionary(
                    value => value,
                    value => (Func<IngredientOwnershipSourceContext, bool>)(context =>
                        MatchesDefaultPredicate(context, value)),
                    StringComparer.Ordinal));
        }

        private static bool MatchesDefaultPredicate(
            IngredientOwnershipSourceContext context,
            string predicateId)
        {
            if (!context.HasBoundSourceIdentity
                || !context.HasBoundEvidence
                || !string.Equals(context.Node.SourcePredicateId, predicateId, StringComparison.Ordinal))
            {
                return false;
            }

            var snapshot = context.Snapshot;
            var node = context.Node;
            switch (predicateId)
            {
                case "runtime.page.capture":
                    return !string.IsNullOrWhiteSpace(snapshot?.Runtime?.AdapterId);
                case "runtime.dynamic-region.typed-provider-binding":
                    return HasDynamicRegionIdentity(node)
                        && PublishingPageIngredientSourceBinding.MatchesExtensionEnvelope(snapshot, node);
                case "page-artifact.captured-spfile":
                    return snapshot?.PageArtifact != null
                        && snapshot.PageArtifact.FileUniqueId == snapshot.Source.FileUniqueId;
                case "layout.typed-snapshot":
                    return snapshot?.Layout != null;
                case "content-type.page-binding":
                    return !string.IsNullOrWhiteSpace(snapshot?.Source?.ContentTypeId)
                        && string.Equals(node.Id, PublishingPageIngredientIds.ContentType, StringComparison.Ordinal);
                case "content-type.generic-schema":
                    return node.Id?.StartsWith("site-content-type:", StringComparison.Ordinal) == true
                        || node.Id?.StartsWith("list-content-type:", StringComparison.Ordinal) == true;
                case "content.publishing-page-field":
                    return snapshot?.PublishingPageContent != null
                        && string.Equals(node.Id, PublishingPageIngredientIds.PublishingContent, StringComparison.Ordinal);
                case "field.body-only":
                    return string.Equals(node.Id, "field:WikiField", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(node.Id, "field:PublishingPageContent", StringComparison.OrdinalIgnoreCase);
                case "field.page-content-type-closure":
                    return node.Id?.StartsWith("page-content-type-field:", StringComparison.Ordinal) == true;
                case "field.non-body":
                    return !string.Equals(node.Id, "field:WikiField", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(node.Id, "field:PublishingPageContent", StringComparison.OrdinalIgnoreCase)
                        && (node.Id?.StartsWith("field:", StringComparison.Ordinal) == true
                            || node.Id?.StartsWith("site-field:", StringComparison.Ordinal) == true
                            || node.Id?.StartsWith("list-field:", StringComparison.Ordinal) == true);
                case "webpart.classic-export":
                    return node.Id?.StartsWith("webpart:", StringComparison.Ordinal) == true;
                case "list.captured-closure":
                    return node.Id?.StartsWith("list:", StringComparison.Ordinal) == true;
                case "view.captured-list-view":
                    return node.Id?.StartsWith("view:", StringComparison.Ordinal) == true;
                case "asset.non-image-file-script":
                    return node.Id?.StartsWith("layout-resource:", StringComparison.Ordinal) == true
                        || node.Id?.StartsWith("view-rendering-resource:", StringComparison.Ordinal) == true;
                case "asset.typed-image":
                    return node.Id?.StartsWith("layout-resource:", StringComparison.Ordinal) == true
                        || PublishingPageIngredientSourceBinding.MatchesExtensionEnvelope(snapshot, node);
                case "asset.direct-page-file":
                    return node.Id?.StartsWith("layout-resource:", StringComparison.Ordinal) == true
                        || PublishingPageIngredientSourceBinding.MatchesExtensionEnvelope(snapshot, node);
                case "asset.script-source":
                    return node.Id?.StartsWith("layout-resource:", StringComparison.Ordinal) == true
                        || node.Id?.StartsWith("view-rendering-resource:", StringComparison.Ordinal) == true
                        || PublishingPageIngredientSourceBinding.MatchesExtensionEnvelope(snapshot, node);
                case "content.classic-wiki-field":
                case "service.typed-contract":
                    return PublishingPageIngredientSourceBinding.MatchesExtensionEnvelope(snapshot, node);
                case "taxonomy.typed-relationship":
                    return node.Id?.StartsWith("taxonomy-relationship:", StringComparison.Ordinal) == true;
                case "reference.kind-image":
                    return ReferenceKind(snapshot, node, PageReferenceKind.Image);
                case "reference.kind-script":
                    return ReferenceKind(snapshot, node, PageReferenceKind.Script);
                case "reference.kind-iframe":
                    return ReferenceKind(snapshot, node, PageReferenceKind.IFrame);
                case "reference.direct-file-binding":
                    return ReferenceKind(snapshot, node, PageReferenceKind.Anchor, requireCapturedContent: true);
                case "reference.non-image-file-script-iframe":
                    return IsOtherReference(snapshot, node);
                case "topology.typed-plan":
                    return snapshot?.SourceTopology != null || snapshot?.PathDerivedTopologyEvidence != null;
                case "security.typed-snapshot":
                    return snapshot?.Security != null;
                case "lifecycle.typed-state":
                    return snapshot?.Lifecycle != null;
                case "web.topology-member":
                    return node.Id?.StartsWith("web:", StringComparison.Ordinal) == true;
                case "list-item.captured-current-state":
                    return node.Id?.StartsWith("list-item:", StringComparison.Ordinal) == true;
                case "document.direct-page-reference":
                    return node.Id?.StartsWith("document:page-reference:", StringComparison.Ordinal) == true
                        && PublishingPageIngredientSourceBinding.FindDirectReference(snapshot, node) != null
                        && PublishingPageIngredientSourceBinding.MatchesExtensionEnvelope(snapshot, node);
                case "document.list-item-member":
                    return node.Id?.StartsWith("list-document:", StringComparison.Ordinal) == true;
                case "attachment.direct-page-reference":
                    return node.Id?.StartsWith("attachment:page-reference:", StringComparison.Ordinal) == true
                        && PublishingPageIngredientSourceBinding.FindDirectReference(snapshot, node) != null
                        && PublishingPageIngredientSourceBinding.MatchesExtensionEnvelope(snapshot, node);
                case "attachment.list-item-member":
                    return node.Id?.StartsWith("list-attachment:", StringComparison.Ordinal) == true;
                case "platform-feature.typed-requirement":
                    return node.Id?.StartsWith("platform-feature:", StringComparison.Ordinal) == true;
                case "policy.typed-contract":
                    return node.Id?.StartsWith("policy:", StringComparison.Ordinal) == true
                        || node.Id?.StartsWith("list-document-information-protection:", StringComparison.Ordinal) == true;
                default:
                    return false;
            }
        }

        private static bool HasDynamicRegionIdentity(PageIngredientNode node)
        {
            if (node?.Id?.StartsWith("dynamic-region:", StringComparison.Ordinal) != true)
            {
                return false;
            }
            var parts = node.Id.Substring("dynamic-region:".Length)
                .Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 1;
        }

        private static bool ReferenceKind(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node,
            PageReferenceKind kind,
            bool requireCapturedContent = false)
        {
            var reference = FindReference(snapshot, node);
            return reference?.Kind == kind
                && (!requireCapturedContent || !string.IsNullOrWhiteSpace(reference.ContentSha256));
        }

        private static bool IsOtherReference(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            var reference = FindReference(snapshot, node);
            return reference != null
                && reference.Kind != PageReferenceKind.Image
                && reference.Kind != PageReferenceKind.Script
                && reference.Kind != PageReferenceKind.IFrame
                && !(reference.Kind == PageReferenceKind.Anchor
                    && reference.IsRenderableResource
                    && !string.IsNullOrWhiteSpace(reference.ContentSha256));
        }

        private static PageReferenceSnapshot FindReference(
            PublishingPageCaptureBundle snapshot,
            PageIngredientNode node)
        {
            var referenceId = node?.Id?.StartsWith("reference:", StringComparison.Ordinal) == true
                ? node.Id.Substring("reference:".Length)
                : null;
            return (snapshot?.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .FirstOrDefault(value => string.Equals(value?.Id, referenceId, StringComparison.Ordinal));
        }

        private static PageIngredientPrimaryOwnerDescriptor Entry(
            string id,
            PageIngredientKind kind,
            string subtype,
            string semanticRole,
            string predicateId,
            string lane)
        {
            return new PageIngredientPrimaryOwnerDescriptor(id, kind, subtype, semanticRole, predicateId, lane);
        }
    }
}
