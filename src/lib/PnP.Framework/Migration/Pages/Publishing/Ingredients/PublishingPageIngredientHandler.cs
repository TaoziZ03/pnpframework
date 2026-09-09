using PnP.Framework.Migration.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public abstract class PublishingPageIngredientHandler
    {
        public abstract PageIngredientHandlerDescriptor Descriptor { get; }

        internal abstract void ValidateAndProject(
            PublishingPageIngredientGraphProjectionContext context,
            PublishingPageIngredientEvidenceEnvelope envelope);

        internal abstract void Validate(PublishingPageIngredientEvidenceEnvelope envelope);
    }

    public abstract class PublishingPageIngredientHandler<TEvidence> : PublishingPageIngredientHandler
        where TEvidence : class
    {
        protected virtual void ValidateEvidence(TEvidence evidence)
        {
        }

        protected abstract void ProjectGraph(
            PublishingPageIngredientGraphProjectionContext context,
            PublishingPageIngredientEvidenceEnvelope envelope,
            TEvidence evidence);

        internal override void ValidateAndProject(
            PublishingPageIngredientGraphProjectionContext context,
            PublishingPageIngredientEvidenceEnvelope envelope)
        {
            var evidence = DeserializeAndValidate(envelope);
            ProjectGraph(context, envelope, evidence);
        }

        internal override void Validate(PublishingPageIngredientEvidenceEnvelope envelope)
        {
            DeserializeAndValidate(envelope);
        }

        private TEvidence DeserializeAndValidate(PublishingPageIngredientEvidenceEnvelope envelope)
        {
            if (envelope.CanonicalPayload.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new InvalidDataException($"Ingredient evidence '{envelope.IngredientKey}' must contain a canonical JSON object payload.");
            }

            var evidence = MigrationContractSerializer.Deserialize<TEvidence>(envelope.CanonicalPayload.GetRawText());
            if (evidence == null)
            {
                throw new InvalidDataException($"Ingredient evidence '{envelope.IngredientKey}' did not contain a typed payload.");
            }
            var typedCanonical = MigrationContractSerializer.SerializeCanonical(evidence);
            var payloadCanonical = MigrationContractSerializer.SerializeCanonical(envelope.CanonicalPayload);
            if (!string.Equals(typedCanonical, payloadCanonical, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Ingredient evidence '{envelope.IngredientKey}' is not the canonical payload for handler '{Descriptor.HandlerId}'.");
            }

            ValidateEvidence(evidence);
            return evidence;
        }
    }
}
