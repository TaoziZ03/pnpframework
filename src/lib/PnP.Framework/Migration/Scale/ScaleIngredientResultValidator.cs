using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleIngredientResultValidator
    {
        public static void Validate(
            string outputRoot,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            Validate(outputRoot, action, result.Artifacts, result.Requests, result.Ingredients);
        }

        public static void Validate(
            string outputRoot,
            MigrationActionSignature action,
            IList<ScaleStageArtifact> artifacts,
            IList<ScaleRequestMetric> requests,
            IList<ScaleIngredientRunResult> ingredients)
        {
            if (artifacts == null || requests == null)
            {
                throw new InvalidDataException("Scale ingredient outcomes require stage artifacts and request telemetry.");
            }
            ingredients = ingredients ?? throw new InvalidDataException("Scale ingredient outcomes are missing.");
            if (!HasValidShape(ingredients))
            {
                throw new InvalidDataException("Scale ingredient outcomes have an invalid identity, dependency set, disposition, or diagnostic code.");
            }
            foreach (var ingredient in ingredients)
            {
                if (ingredient.Outcome == ScaleIngredientOutcome.AuthorizationBlocked)
                {
                    ValidateAuthorizationEvidence(outputRoot, action, artifacts, requests, ingredient);
                }
                else if (!string.IsNullOrWhiteSpace(ingredient.AuthorizationEvidenceArtifactSha256))
                {
                    throw new InvalidDataException("Only an authorization-blocked ingredient may reference HTTP authorization evidence.");
                }
            }
        }

        public static bool HasValidShape(IList<ScaleIngredientRunResult> ingredients)
        {
            if (ingredients == null
                || ingredients.Any(value => value == null)
                || ingredients.Select(value => value.IngredientId).Distinct(StringComparer.Ordinal).Count() != ingredients.Count)
            {
                return false;
            }
            var structurallyValid = ingredients.All(ingredient =>
            {
                var dependencies = ingredient.DependencyIngredientIds;
                return Enum.IsDefined(typeof(ScaleIngredientOutcome), ingredient.Outcome)
                    && IsSafeIdentifier(ingredient.IngredientId)
                    && dependencies != null
                    && dependencies.All(IsSafeIdentifier)
                    && dependencies.Distinct(StringComparer.Ordinal).Count() == dependencies.Count
                    && !dependencies.Contains(ingredient.IngredientId, StringComparer.Ordinal)
                    && (ingredient.Outcome != ScaleIngredientOutcome.SkippedByDependency || dependencies.Count > 0)
                    && ScaleStageResultValidator.IsSafeDiagnosticCode(ingredient.DiagnosticCode)
                    && (ingredient.Outcome == ScaleIngredientOutcome.AuthorizationBlocked
                        || string.IsNullOrWhiteSpace(ingredient.AuthorizationEvidenceArtifactSha256));
            });
            if (!structurallyValid)
            {
                return false;
            }
            var byId = ingredients.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            return ingredients.Where(value => value.Outcome == ScaleIngredientOutcome.SkippedByDependency)
                .All(value => AllCausesReachAuthorization(value, byId, new HashSet<string>(StringComparer.Ordinal)));
        }

        private static bool AllCausesReachAuthorization(
            ScaleIngredientRunResult ingredient,
            IReadOnlyDictionary<string, ScaleIngredientRunResult> byId,
            ISet<string> visiting)
        {
            if (!visiting.Add(ingredient.IngredientId))
            {
                return false;
            }
            try
            {
                return ingredient.DependencyIngredientIds.All(dependencyId =>
                    byId.TryGetValue(dependencyId, out var dependency)
                    && (dependency.Outcome == ScaleIngredientOutcome.AuthorizationBlocked
                        || dependency.Outcome == ScaleIngredientOutcome.SkippedByDependency
                            && AllCausesReachAuthorization(dependency, byId, visiting)));
            }
            finally
            {
                visiting.Remove(ingredient.IngredientId);
            }
        }

        private static void ValidateAuthorizationEvidence(
            string outputRoot,
            MigrationActionSignature action,
            IList<ScaleStageArtifact> artifacts,
            IList<ScaleRequestMetric> requests,
            ScaleIngredientRunResult ingredient)
        {
            if (!MigrationActionSignature.IsSha256(ingredient.AuthorizationEvidenceArtifactSha256))
            {
                throw new InvalidDataException("An authorization-blocked ingredient requires content-addressed HTTP 401/403 evidence.");
            }
            var matches = artifacts.Where(value =>
                    value.Kind == ScaleStageArtifactKind.HttpAuthorizationEvidence
                    && string.Equals(value.Sha256, ingredient.AuthorizationEvidenceArtifactSha256, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException("An authorization-blocked ingredient must reference exactly one retained evidence artifact.");
            }
            var artifact = matches[0];
            var path = ScaleRunStorage.ResolveArtifactPath(outputRoot, artifact.RelativePath);
            var raw = File.ReadAllText(path, Encoding.UTF8).TrimEnd('\r', '\n');
            var evidence = MigrationContractSerializer.Deserialize<ScaleHttpAuthorizationEvidence>(raw);
            if (evidence == null
                || !string.Equals(evidence.SchemaVersion, ScaleHttpAuthorizationEvidence.CurrentSchemaVersion, StringComparison.Ordinal)
                || !string.Equals(raw, MigrationContractSerializer.SerializeCanonical(evidence), StringComparison.Ordinal)
                || !string.Equals(evidence.IngredientId, ingredient.IngredientId, StringComparison.Ordinal)
                || !string.Equals(evidence.ActionSignature, action.Signature, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(evidence.TargetIdentityDigest, action.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase)
                || evidence.HttpStatusCode != 401 && evidence.HttpStatusCode != 403
                || evidence.CapturedAtUtc == default(DateTimeOffset)
                || !requests.Any(value => value.HttpStatusCode == evidence.HttpStatusCode
                    && string.Equals(value.Operation, evidence.Operation, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("Ingredient authorization evidence does not match its ingredient, action, or literal request telemetry.");
            }
        }

        private static bool IsSafeIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= 512
                && value.All(character => !char.IsControl(character) && !char.IsWhiteSpace(character));
        }
    }
}
