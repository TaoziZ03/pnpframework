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

        internal PublishingPageIngredientGraphProjectionContext(
            PublishingPageCaptureBundle snapshot,
            CanonicalPageIngredientGraph graph,
            PageIngredientHandlerDescriptor descriptor)
        {
            SourceContentTypeId = snapshot?.Source?.ContentTypeId;
            RuntimeAdapterId = snapshot?.Runtime?.AdapterId;
            ProjectionVersion = graph?.ProjectionVersion;
            this.graph = graph;
            this.descriptor = descriptor;
        }

        public string SourceContentTypeId { get; }

        public string RuntimeAdapterId { get; }

        public string ProjectionVersion { get; }

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
