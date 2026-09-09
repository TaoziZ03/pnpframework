using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using System;
using System.Collections.Generic;
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
        private readonly PublishingPageIngredientEvidenceEnvelope envelope;

        internal PublishingPageIngredientGraphProjectionContext(
            PublishingPageCaptureBundle snapshot,
            CanonicalPageIngredientGraph graph,
            PageIngredientHandlerDescriptor descriptor,
            PublishingPageIngredientPrimaryOwnerRegistry primaryOwnerRegistry,
            PublishingPageIngredientEvidenceEnvelope envelope)
        {
            SourceContentTypeId = snapshot?.Source?.ContentTypeId;
            RuntimeAdapterId = snapshot?.Runtime?.AdapterId;
            ProjectionVersion = graph?.ProjectionVersion;
            PageFamily = PublishingPageIngredientPageFamily.Resolve(snapshot);
            SourceVersionIdentity = PublishingPageIngredientSourceBinding.SourceVersionIdentity(snapshot);
            EvidenceDigest = envelope?.EvidenceDigest;
            EvidenceReferences = (envelope?.EvidenceReferences ?? Array.Empty<string>()).ToArray();
            this.graph = graph;
            this.descriptor = descriptor;
            this.snapshot = snapshot;
            this.primaryOwnerRegistry = primaryOwnerRegistry;
            this.envelope = envelope;
        }

        public string SourceContentTypeId { get; }

        public string RuntimeAdapterId { get; }

        public string ProjectionVersion { get; }

        public string PageFamily { get; }

        public string SourceVersionIdentity { get; }

        public string EvidenceDigest { get; }

        public IReadOnlyList<string> EvidenceReferences { get; }

        public string SourceIdentity(string ingredientId)
        {
            return PublishingPageIngredientSourceBinding.SourceIdentity(snapshot, ingredientId);
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
            if (!string.Equals(node.SourcePageOrListItemIdentity, SourceIdentity(node.Id), StringComparison.Ordinal)
                || !string.Equals(node.SourceVersionIdentity, SourceVersionIdentity, StringComparison.Ordinal)
                || !string.Equals(node.EvidenceDigest, envelope.EvidenceDigest, StringComparison.OrdinalIgnoreCase)
                || !(node.EvidenceReferences ?? Array.Empty<string>()).SequenceEqual(EvidenceReferences, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Extension ingredient '{node.Id}' does not bind the current handler evidence and source fence.");
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
