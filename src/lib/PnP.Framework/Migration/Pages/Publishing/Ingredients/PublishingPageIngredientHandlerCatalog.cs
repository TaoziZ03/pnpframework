using PnP.Framework.Migration.Pages.Publishing.Capture;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientHandlerCatalog
    {
        private readonly IReadOnlyDictionary<string, PublishingPageIngredientHandler> handlersById;

        public static PublishingPageIngredientHandlerCatalog Default { get; } =
            new PublishingPageIngredientHandlerCatalog(
                Array.Empty<PublishingPageIngredientHandler>(),
                PublishingPageIngredientPrimaryOwnerRegistry.Default);

        public static PublishingPageIngredientHandlerCatalog Empty => Default;

        public PublishingPageIngredientHandlerCatalog(IEnumerable<PublishingPageIngredientHandler> handlers)
            : this(handlers, PublishingPageIngredientPrimaryOwnerRegistry.Default)
        {
        }

        public PublishingPageIngredientHandlerCatalog(
            IEnumerable<PublishingPageIngredientHandler> handlers,
            PublishingPageIngredientPrimaryOwnerRegistry primaryOwnerRegistry)
        {
            PrimaryOwnerRegistry = primaryOwnerRegistry
                ?? throw new ArgumentNullException(nameof(primaryOwnerRegistry));
            var ordered = (handlers ?? throw new ArgumentNullException(nameof(handlers)))
                .Select(value => value ?? throw new ArgumentException("The ingredient handler catalog cannot contain null handlers.", nameof(handlers)))
                .OrderBy(value => value.Descriptor.OrderGroup)
                .ThenBy(value => value.Descriptor.HandlerId, StringComparer.Ordinal)
                .ToArray();

            var duplicate = ordered
                .GroupBy(value => value.Descriptor.HandlerId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException($"Duplicate ingredient handler ID '{duplicate.Key}'.", nameof(handlers));
            }

            for (var leftIndex = 0; leftIndex < ordered.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < ordered.Length; rightIndex++)
                {
                    var left = ordered[leftIndex].Descriptor;
                    var right = ordered[rightIndex].Descriptor;
                    var overlap = left.OwnedIngredientIds.FirstOrDefault(leftOwnership =>
                        right.OwnedIngredientIds.Any(leftOwnership.Overlaps));
                    if (overlap != null)
                    {
                        throw new ArgumentException(
                            $"Ingredient handlers '{left.HandlerId}' and '{right.HandlerId}' have overlapping ingredient ownership at '{overlap.Value}'.",
                            nameof(handlers));
                    }
                }
            }

            Handlers = new ReadOnlyCollection<PublishingPageIngredientHandler>(ordered);
            handlersById = new ReadOnlyDictionary<string, PublishingPageIngredientHandler>(
                ordered.ToDictionary(value => value.Descriptor.HandlerId, StringComparer.Ordinal));
        }

        public IReadOnlyList<PublishingPageIngredientHandler> Handlers { get; }

        public PublishingPageIngredientPrimaryOwnerRegistry PrimaryOwnerRegistry { get; }

        public void ValidateEvidence(IEnumerable<PublishingPageIngredientEvidenceEnvelope> evidence)
        {
            ValidateAndOrderEvidence(evidence);
        }

        public IReadOnlyList<PublishingPageIngredientEvidenceEnvelope> OrderEvidence(
            IEnumerable<PublishingPageIngredientEvidenceEnvelope> evidence)
        {
            var ordered = (evidence ?? throw new ArgumentNullException(nameof(evidence)))
                .Select(value => new { Envelope = value, Handler = Resolve(value) })
                .OrderBy(value => value.Handler.Descriptor.OrderGroup)
                .ThenBy(value => value.Handler.Descriptor.HandlerId, StringComparer.Ordinal)
                .ThenBy(value => value.Envelope.IngredientKey, StringComparer.Ordinal)
                .Select(value => value.Envelope)
                .ToArray();
            return ValidateAndOrderEvidence(ordered);
        }

        internal IReadOnlyList<PublishingPageIngredientEvidenceEnvelope> ValidateAndOrderEvidence(
            IEnumerable<PublishingPageIngredientEvidenceEnvelope> evidence)
        {
            var values = (evidence ?? throw new InvalidDataException("The ingredient evidence collection is null.")).ToArray();
            var duplicate = values
                .GroupBy(value => (value?.HandlerId ?? string.Empty) + "\u001f" + (value?.IngredientKey ?? string.Empty), StringComparer.Ordinal)
                .FirstOrDefault(group => group.Any(value => value == null) || group.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidDataException("The ingredient evidence collection contains a null or duplicate handler/key identity.");
            }

            var ordered = values
                .Select(value => new
                {
                    Envelope = value,
                    Handler = Resolve(value)
                })
                .OrderBy(value => value.Handler.Descriptor.OrderGroup)
                .ThenBy(value => value.Handler.Descriptor.HandlerId, StringComparer.Ordinal)
                .ThenBy(value => value.Envelope.IngredientKey, StringComparer.Ordinal)
                .ToArray();
            if (!values.SequenceEqual(ordered.Select(value => value.Envelope)))
            {
                throw new InvalidDataException("Ingredient evidence must use deterministic catalog and ingredient-key order.");
            }

            foreach (var item in ordered)
            {
                ValidateEnvelope(item.Handler, item.Envelope);
                item.Handler.Validate(item.Envelope);
            }
            return new ReadOnlyCollection<PublishingPageIngredientEvidenceEnvelope>(values);
        }

        internal void Project(
            PublishingPageCaptureBundle snapshot,
            PnP.Framework.Migration.Pages.Ingredients.CanonicalPageIngredientGraph graph)
        {
            var evidence = ValidateAndOrderEvidence(snapshot.IngredientEvidence);
            foreach (var envelope in evidence)
            {
                var handler = handlersById[envelope.HandlerId];
                var context = new PublishingPageIngredientGraphProjectionContext(
                    snapshot,
                    graph,
                    handler.Descriptor,
                    PrimaryOwnerRegistry);
                handler.ValidateAndProject(context, envelope);
            }
        }

        internal void ProjectActions(
            PublishingPageCaptureBundle snapshot,
            PnP.Framework.Migration.Pages.Publishing.Planning.PublishingPageMigrationPlan plan,
            PnP.Framework.Migration.Pages.Ingredients.CanonicalPageIngredientGraph graph,
            IDictionary<string, PnP.Framework.Migration.Pages.Ingredients.PageIngredientAction> actions)
        {
            var evidence = ValidateAndOrderEvidence(snapshot.IngredientEvidence);
            foreach (var envelope in evidence)
            {
                var handler = handlersById[envelope.HandlerId];
                handler.ValidateAndProjectActions(
                    new PublishingPageIngredientActionProjectionContext(snapshot, plan, graph, actions, handler.Descriptor),
                    envelope);
            }
        }

        internal void ContributeAssessment(
            PublishingPageCaptureBundle snapshot,
            PnP.Framework.Migration.Pages.Ingredients.CanonicalPageIngredientGraph graph,
            PnP.Framework.Migration.Pages.Publishing.Assessment.PublishingPageAssessmentAccumulator accumulator)
        {
            var evidence = ValidateAndOrderEvidence(snapshot.IngredientEvidence);
            var context = new PublishingPageIngredientAssessmentContext(snapshot, graph, accumulator);
            foreach (var envelope in evidence)
            {
                handlersById[envelope.HandlerId].ValidateAndContributeAssessment(context, envelope);
            }
        }

        private PublishingPageIngredientHandler Resolve(PublishingPageIngredientEvidenceEnvelope envelope)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.HandlerId))
            {
                throw new InvalidDataException("Every ingredient evidence envelope must identify its handler.");
            }
            if (!handlersById.TryGetValue(envelope.HandlerId, out var handler))
            {
                throw new InvalidDataException($"Unknown ingredient evidence handler '{envelope.HandlerId}'.");
            }
            return handler;
        }

        private static void ValidateEnvelope(
            PublishingPageIngredientHandler handler,
            PublishingPageIngredientEvidenceEnvelope envelope)
        {
            var descriptor = handler.Descriptor;
            if (!string.Equals(
                descriptor.IntroducedProjectionVersion,
                PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Ingredient handler '{envelope.HandlerId}' is not part of projection '{PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion}'.");
            }
            if (!string.Equals(envelope.LaneId, descriptor.Lane.LaneId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Ingredient evidence handler '{envelope.HandlerId}' does not own lane '{envelope.LaneId}'.");
            }
            if (!descriptor.EvidenceSchemaVersions.Contains(envelope.EvidenceSchemaVersion, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unknown ingredient evidence schema '{envelope.EvidenceSchemaVersion}' for handler '{envelope.HandlerId}'.");
            }
            if (string.IsNullOrWhiteSpace(envelope.IngredientKey)
                || envelope.EvidenceReferences == null
                || !envelope.EvidenceReferences.SequenceEqual(
                    envelope.EvidenceReferences.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Ingredient evidence '{envelope.IngredientKey}' has a missing key or non-canonical evidence references.");
            }
            var expectedDigest = PublishingPageIngredientEvidenceEnvelope.ComputeDigest(envelope);
            if (!string.Equals(expectedDigest, envelope.EvidenceDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Ingredient evidence digest mismatch for '{envelope.HandlerId}:{envelope.IngredientKey}'.");
            }
        }
    }
}
