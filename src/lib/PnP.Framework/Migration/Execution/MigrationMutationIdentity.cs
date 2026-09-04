using PnP.Framework.Migration.Packaging;
using System;

namespace PnP.Framework.Migration.Execution
{
    /// <summary>
    /// Stable identity for one independently resumable ingredient action.
    /// </summary>
    public sealed class MigrationMutationIdentity
    {
        public string SchemaVersion { get; set; } = "pnp-migration-mutation-identity/v1";

        public string IngredientId { get; set; }

        public string ActionId { get; set; }

        public string SelectedDisposition { get; set; }

        public string SemanticDigest { get; set; }

        public string IdempotencyKey { get; set; }

        public static MigrationMutationIdentity Create(
            MigrationExecutionBoundary boundary,
            string ingredientId,
            string actionId,
            string selectedDisposition,
            string semanticDigest)
        {
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            if (string.IsNullOrWhiteSpace(ingredientId))
            {
                throw new ArgumentException("An ingredient ID is required.", nameof(ingredientId));
            }
            if (string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("An action ID is required.", nameof(actionId));
            }
            if (string.IsNullOrWhiteSpace(selectedDisposition))
            {
                throw new ArgumentException("A selected disposition is required.", nameof(selectedDisposition));
            }
            if (!IsSha256(semanticDigest))
            {
                throw new ArgumentException("A SHA-256 semantic digest is required.", nameof(semanticDigest));
            }

            var result = new MigrationMutationIdentity
            {
                IngredientId = ingredientId.Trim(),
                ActionId = actionId.Trim(),
                SelectedDisposition = selectedDisposition.Trim(),
                SemanticDigest = semanticDigest.Trim()
            };
            result.IdempotencyKey = ComputeIdempotencyKey(boundary, result);
            return result;
        }

        public static string ComputeIdempotencyKey(
            MigrationExecutionBoundary boundary,
            MigrationMutationIdentity identity)
        {
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-migration-idempotency-key/v1",
                sourceSnapshotDigest = boundary.SourceSnapshotDigest,
                planDigest = boundary.PlanDigest,
                approvalDigest = boundary.ApprovalDigest,
                targetBoundaryDigest = boundary.TargetBoundaryDigest,
                ingredientId = identity.IngredientId,
                actionId = identity.ActionId,
                selectedDisposition = identity.SelectedDisposition,
                semanticDigest = identity.SemanticDigest
            }));
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }
            foreach (var character in value)
            {
                if (!(character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
