using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientEvidenceEnvelope
    {
        public string HandlerId { get; set; }

        public string LaneId { get; set; }

        public string EvidenceSchemaVersion { get; set; }

        public string IngredientKey { get; set; }

        public JsonElement CanonicalPayload { get; set; }

        public string EvidenceDigest { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();

        public static PublishingPageIngredientEvidenceEnvelope Create<TEvidence>(
            PublishingPageIngredientHandler<TEvidence> handler,
            string evidenceSchemaVersion,
            string ingredientKey,
            TEvidence evidence,
            IEnumerable<string> evidenceReferences = null)
            where TEvidence : class
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }
            if (string.IsNullOrWhiteSpace(ingredientKey))
            {
                throw new ArgumentException("A stable ingredient evidence key is required.", nameof(ingredientKey));
            }
            if (!handler.Descriptor.EvidenceSchemaVersions.Contains(evidenceSchemaVersion, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Handler '{handler.Descriptor.HandlerId}' does not declare evidence schema '{evidenceSchemaVersion}'.",
                    nameof(evidenceSchemaVersion));
            }

            var payloadJson = MigrationContractSerializer.SerializeCanonical(evidence);
            using (var document = JsonDocument.Parse(payloadJson))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("Ingredient evidence must serialize as a JSON object.", nameof(evidence));
                }

                var envelope = new PublishingPageIngredientEvidenceEnvelope
                {
                    HandlerId = handler.Descriptor.HandlerId,
                    LaneId = handler.Descriptor.Lane.LaneId,
                    EvidenceSchemaVersion = evidenceSchemaVersion,
                    IngredientKey = ingredientKey,
                    CanonicalPayload = document.RootElement.Clone(),
                    EvidenceReferences = (evidenceReferences ?? Array.Empty<string>())
                        .Select(value => value?.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToList()
                };
                envelope.EvidenceDigest = ComputeDigest(envelope);
                return envelope;
            }
        }

        internal static string ComputeDigest(PublishingPageIngredientEvidenceEnvelope envelope)
        {
            var seal = new PublishingPageIngredientEvidenceSeal
            {
                HandlerId = envelope.HandlerId,
                LaneId = envelope.LaneId,
                EvidenceSchemaVersion = envelope.EvidenceSchemaVersion,
                IngredientKey = envelope.IngredientKey,
                CanonicalPayload = envelope.CanonicalPayload,
                EvidenceReferences = envelope.EvidenceReferences
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(seal));
        }

        private sealed class PublishingPageIngredientEvidenceSeal
        {
            public string HandlerId { get; set; }
            public string LaneId { get; set; }
            public string EvidenceSchemaVersion { get; set; }
            public string IngredientKey { get; set; }
            public JsonElement CanonicalPayload { get; set; }
            public IList<string> EvidenceReferences { get; set; }
        }
    }
}
