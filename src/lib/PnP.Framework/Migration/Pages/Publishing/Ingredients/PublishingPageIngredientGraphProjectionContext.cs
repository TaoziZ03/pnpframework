using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientGraphProjectionContext
    {
        private readonly CanonicalPageIngredientGraph graph;
        private readonly PageIngredientHandlerDescriptor descriptor;
        private readonly PublishingPageCaptureBundle snapshot;
        private readonly PublishingPageIngredientPrimaryOwnerRegistry primaryOwnerRegistry;

        internal PublishingPageIngredientGraphProjectionContext(
            PublishingPageCaptureBundle snapshot,
            CanonicalPageIngredientGraph graph,
            PageIngredientHandlerDescriptor descriptor,
            PublishingPageIngredientPrimaryOwnerRegistry primaryOwnerRegistry)
        {
            SourceContentTypeId = snapshot?.Source?.ContentTypeId;
            RuntimeAdapterId = snapshot?.Runtime?.AdapterId;
            ProjectionVersion = graph?.ProjectionVersion;
            this.graph = graph;
            this.descriptor = descriptor;
            this.snapshot = snapshot;
            this.primaryOwnerRegistry = primaryOwnerRegistry;
        }

        public string SourceContentTypeId { get; }

        public string RuntimeAdapterId { get; }

        public string ProjectionVersion { get; }

        /// <summary>
        /// Projects captured direct-file bytes selected by an existing source
        /// reference, without introducing a lane-private node or wire contract.
        /// Returns the canonical asset ID, or null when payload evidence is not
        /// sufficient. This does not plan a copy or award maturity.
        /// </summary>
        public string AddPageReferencedAsset(string referenceId)
        {
            var node = PublishingPagePageReferenceAssetGraphProjector.CreateSourceNode(snapshot, referenceId);
            if (node == null)
            {
                return null;
            }
            PublishingPageIngredientPrimaryOwnerRegistry.BindBuiltInNode(snapshot, node);
            node.PrimaryOwnerLane = primaryOwnerRegistry.Resolve(snapshot, node).PrimaryOwnerLane;
            if (!descriptor.Owns(node.Id)
                || !string.Equals(descriptor.Lane.LaneId, node.PrimaryOwnerLane, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Handler '{descriptor.HandlerId}' does not own page-reference asset '{node.Id}'.");
            }
            if (node.EvidenceReferences.Any(id => !graph.Nodes.Any(value => value.Id == id
                && value.Kind == PageIngredientKind.Reference && value.PrimaryOwnerLane == node.PrimaryOwnerLane)))
            {
                throw new InvalidDataException("A page-reference asset requires its canonical source Reference nodes.");
            }

            var existing = graph.Nodes.SingleOrDefault(value => string.Equals(value.Id, node.Id, StringComparison.Ordinal));
            if (existing == null)
            {
                AddNode(node);
            }
            else if (!string.Equals(MigrationContractSerializer.SerializeCanonical(existing),
                MigrationContractSerializer.SerializeCanonical(node), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Conflicting page-reference asset projection for '{node.Id}'.");
            }

            // Reference provenance is distinct from bytes. Only the reference
            // write branches depend on this payload; page/body work stays independent.
            foreach (var id in node.EvidenceReferences)
            {
                AddCanonicalAssetDependency(id, node.Id, PageIngredientRelationship.BindsTo);
            }
            var ownerWeb = PublishingPageIngredientOwnerWebResolver.ExactOrContaining(snapshot, node.Label);
            if (ownerWeb != null)
            {
                AddCanonicalAssetDependency(node.Id, ownerWeb, PageIngredientRelationship.DependsOn);
            }
            return node.Id;
        }

        private void AddCanonicalAssetDependency(string from, string to, PageIngredientRelationship relationship)
        {
            if (!graph.Edges.Any(value => value.FromIngredientId == from && value.ToIngredientId == to
                && value.Relationship == relationship && value.Requirement == PageIngredientRequirement.Required
                && value.Condition == null))
            {
                graph.Edges.Add(PublishingPageIngredientGraphFactory.Edge(from, to, relationship, PageIngredientRequirement.Required));
            }
        }

        public void AddNode(PageIngredientNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Id))
            {
                throw new InvalidDataException($"Handler '{descriptor.HandlerId}' produced a null node or missing ingredient ID.");
            }
            if (!descriptor.Owns(node.Id))
            {
                throw new InvalidDataException($"Handler '{descriptor.HandlerId}' does not own ingredient ID '{node.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(node.KindId))
            {
                throw new InvalidDataException($"Extension ingredient '{node.Id}' must declare a stable kindId.");
            }
            if (node.Kind != 0
                && !string.Equals(node.KindId, PageIngredientKindIdentity.FromLegacyKind(node.Kind), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Ingredient '{node.Id}' has contradictory legacy and extensible kind identities.");
            }
            if (node.EvidenceReferences == null)
            {
                throw new InvalidDataException($"Ingredient '{node.Id}' has a null evidence-reference collection.");
            }
            if (graph.Nodes.Any(value => string.Equals(value?.Id, node.Id, StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"Ingredient handler projection produced duplicate node ID '{node.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(node.Subtype)
                || string.IsNullOrWhiteSpace(node.SemanticRole)
                || string.IsNullOrWhiteSpace(node.SourcePredicateId)
                || string.IsNullOrWhiteSpace(node.SourcePageOrListItemIdentity)
                || string.IsNullOrWhiteSpace(node.SourceVersionIdentity)
                || string.IsNullOrWhiteSpace(node.PrimaryOwnerLane))
            {
                throw new InvalidDataException(
                    $"Extension ingredient '{node.Id}' has an incomplete primary-owner source envelope.");
            }
            var owner = primaryOwnerRegistry.Resolve(snapshot, node);
            if (!string.Equals(owner.PrimaryOwnerLane, descriptor.Lane.LaneId, StringComparison.Ordinal)
                || !string.Equals(owner.PrimaryOwnerLane, node.PrimaryOwnerLane, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Ingredient '{node.Id}' resolves to primary owner '{owner.PrimaryOwnerLane}', not handler lane '{descriptor.Lane.LaneId}'.");
            }
            graph.Nodes.Add(node);
        }

        public void AddEdge(PageIngredientEdge edge)
        {
            if (edge == null
                || string.IsNullOrWhiteSpace(edge.FromIngredientId)
                || string.IsNullOrWhiteSpace(edge.ToIngredientId))
            {
                throw new InvalidDataException($"Handler '{descriptor.HandlerId}' produced a null or unidentified dependency edge.");
            }
            if (!descriptor.Owns(edge.FromIngredientId))
            {
                throw new InvalidDataException(
                    $"Handler '{descriptor.HandlerId}' may add dependencies only from an ingredient ID that it owns ('{edge.FromIngredientId}').");
            }
            var duplicate = graph.Edges.Any(value => value != null
                && string.Equals(value.FromIngredientId, edge.FromIngredientId, StringComparison.Ordinal)
                && string.Equals(value.ToIngredientId, edge.ToIngredientId, StringComparison.Ordinal)
                && value.Relationship == edge.Relationship
                && value.Requirement == edge.Requirement
                && string.Equals(value.Condition, edge.Condition, StringComparison.Ordinal));
            if (duplicate)
            {
                throw new InvalidDataException(
                    $"Ingredient handler projection produced a duplicate dependency edge '{edge.FromIngredientId}' -> '{edge.ToIngredientId}'.");
            }
            graph.Edges.Add(edge);
        }
    }
}
