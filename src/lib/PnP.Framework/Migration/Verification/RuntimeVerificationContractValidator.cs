using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Verification
{
    /// <summary>
    /// Validates both the legacy requirement/result contract and the v2
    /// assertion/state-transition contract used by the shared browser verifier.
    /// </summary>
    public static class RuntimeVerificationContractValidator
    {
        public const string ManifestSchemaV1 = "pnp-migration-runtime-verification/v1";
        public const string ManifestSchemaV2 = "pnp-migration-runtime-verification/v2";
        public const string ReceiptSchemaV1 = "pnp-migration-runtime-verification-receipt/v1";
        public const string ReceiptSchemaV2 = "pnp-migration-runtime-verification-receipt/v2";

        public static void ValidateManifest(
            RuntimeVerificationManifest manifest,
            CanonicalPageIngredientGraph ingredientGraph,
            IEnumerable<PageIngredientAction> ingredientActions)
        {
            if (manifest == null || manifest.Requirements == null)
            {
                throw new InvalidDataException("The runtime verification manifest is missing.");
            }
            ValidateRequirements(manifest.Requirements);

            if (string.Equals(manifest.SchemaVersion, ManifestSchemaV1, StringComparison.Ordinal))
            {
                if (manifest.Assertions != null && manifest.Assertions.Count != 0)
                {
                    throw new InvalidDataException("A v1 runtime verification manifest cannot contain v2 assertions.");
                }
                return;
            }
            if (!string.Equals(manifest.SchemaVersion, ManifestSchemaV2, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported runtime verification manifest schema '{manifest.SchemaVersion}'.");
            }
            if (manifest.Assertions == null || manifest.Assertions.Count == 0)
            {
                throw new InvalidDataException("A v2 runtime verification manifest requires at least one assertion.");
            }
            if (ingredientGraph?.Nodes == null || ingredientActions == null)
            {
                throw new InvalidDataException("Runtime assertions require the sealed canonical ingredient graph and actions.");
            }

            var actions = ingredientActions.ToArray();
            var duplicateAssertion = manifest.Assertions
                .GroupBy(value => value?.AssertionId ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            if (duplicateAssertion != null)
            {
                throw new InvalidDataException("Every runtime assertion must have exactly one assertion owner binding.");
            }
            var duplicateOwnerIdentity = manifest.Assertions
                .GroupBy(OwnerIdentity, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            if (duplicateOwnerIdentity != null)
            {
                throw new InvalidDataException("A runtime assertion identity is claimed by more than one assertion owner.");
            }

            foreach (var assertion in manifest.Assertions.OrderBy(value => value.AssertionId, StringComparer.Ordinal))
            {
                ValidateAssertion(assertion, ingredientGraph.Nodes, actions);
            }
        }

        public static void ValidateReceipt(
            RuntimeVerificationManifest manifest,
            RuntimeVerificationReceipt receipt,
            string expectedPlanDigest,
            string expectedTargetIdentity,
            DateTimeOffset executionStartedAtUtc)
        {
            if (manifest == null || receipt == null)
            {
                throw new InvalidDataException("Runtime verification requires both a manifest and a receipt.");
            }
            BindExact(receipt.PlanDigest, expectedPlanDigest, "runtime receipt plan digest");
            BindExact(receipt.TargetIdentity, expectedTargetIdentity, "runtime receipt target identity");
            if (receipt.CompletedAtUtc < executionStartedAtUtc)
            {
                throw new InvalidDataException("The runtime receipt predates the current execution.");
            }

            if (string.Equals(manifest.SchemaVersion, ManifestSchemaV1, StringComparison.Ordinal))
            {
                if (!string.Equals(receipt.SchemaVersion, ReceiptSchemaV1, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A v1 runtime manifest requires a v1 runtime receipt.");
                }
                return;
            }
            if (!string.Equals(manifest.SchemaVersion, ManifestSchemaV2, StringComparison.Ordinal)
                || !string.Equals(receipt.SchemaVersion, ReceiptSchemaV2, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A v2 runtime manifest requires a v2 runtime receipt.");
            }
            if (receipt.AssertionResults == null)
            {
                throw new InvalidDataException("A v2 runtime receipt is missing assertion results.");
            }

            var assertions = manifest.Assertions ?? Array.Empty<RuntimeVerificationAssertion>();
            var results = receipt.AssertionResults;
            var foreignOrDuplicate = results
                .GroupBy(value => value?.AssertionId ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key)
                    || group.Count() != 1
                    || assertions.Count(assertion => string.Equals(assertion.AssertionId, group.Key, StringComparison.Ordinal)) != 1);
            if (foreignOrDuplicate != null || results.Count != assertions.Count)
            {
                throw new InvalidDataException("The runtime receipt must contain exactly one result for each sealed assertion.");
            }

            foreach (var assertion in assertions)
            {
                var result = results.Single(value => string.Equals(value.AssertionId, assertion.AssertionId, StringComparison.Ordinal));
                ValidateAssertionResult(
                    assertion,
                    result,
                    expectedPlanDigest,
                    expectedTargetIdentity,
                    executionStartedAtUtc);
            }
            ValidateAggregateStatus(manifest, receipt);
        }

        /// <summary>
        /// Validates the single aggregate status shared by legacy required
        /// results and v2 assertion results. Optional legacy requirements do
        /// not affect the aggregate outcome.
        /// </summary>
        public static void ValidateAggregateStatus(
            RuntimeVerificationManifest manifest,
            RuntimeVerificationReceipt receipt)
        {
            if (manifest == null || receipt == null)
            {
                throw new InvalidDataException("Runtime verification requires both a manifest and a receipt.");
            }

            var requirements = (manifest.Requirements ?? Array.Empty<RuntimeVerificationRequirement>()).ToArray();
            var required = requirements.Where(value => value != null && value.Required).ToArray();
            var results = (receipt.Results ?? Array.Empty<RuntimeVerificationResult>()).ToArray();
            var duplicateResult = results
                .GroupBy(value => value?.RequirementId ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            var knownRequirementIds = new HashSet<string>(
                requirements.Where(value => value != null).Select(value => value.Id),
                StringComparer.Ordinal);
            if (duplicateResult != null
                || results.Any(value => value == null || !knownRequirementIds.Contains(value.RequirementId)))
            {
                throw new InvalidDataException("Runtime results contain a missing, duplicate, or unknown requirement ID.");
            }
            if (required.Any(requirement => results.Count(result => string.Equals(
                    result.RequirementId,
                    requirement.Id,
                    StringComparison.Ordinal)) != 1))
            {
                throw new InvalidDataException("The runtime receipt does not cover every required requirement exactly once.");
            }

            var assertions = string.Equals(manifest.SchemaVersion, ManifestSchemaV2, StringComparison.Ordinal)
                ? (manifest.Assertions ?? Array.Empty<RuntimeVerificationAssertion>()).ToArray()
                : Array.Empty<RuntimeVerificationAssertion>();
            var assertionResults = receipt.AssertionResults ?? Array.Empty<RuntimeVerificationAssertionResult>();
            if (assertions.Length > 0
                && (assertionResults.Count != assertions.Length
                    || assertions.Any(assertion => assertionResults.Count(result => result != null
                        && string.Equals(result.AssertionId, assertion.AssertionId, StringComparison.Ordinal)) != 1)))
            {
                throw new InvalidDataException("The runtime receipt must contain exactly one result for each sealed assertion.");
            }

            var hasRequiredEvidence = required.Length > 0 || assertions.Length > 0;
            var allRequiredPassed = required.All(requirement => results.Single(result => string.Equals(
                result.RequirementId,
                requirement.Id,
                StringComparison.Ordinal)).Passed);
            var allAssertionsPassed = assertions.All(assertion => assertionResults.Single(result => string.Equals(
                result.AssertionId,
                assertion.AssertionId,
                StringComparison.Ordinal)).Passed);
            var aggregatePassed = allRequiredPassed && allAssertionsPassed;
            var validStatus = hasRequiredEvidence
                ? receipt.Status == (aggregatePassed
                    ? RuntimeVerificationStatus.Passed
                    : RuntimeVerificationStatus.Failed)
                : receipt.Status == RuntimeVerificationStatus.NotRequired
                    || (string.Equals(manifest.SchemaVersion, ManifestSchemaV1, StringComparison.Ordinal)
                        && receipt.Status == RuntimeVerificationStatus.Passed);
            if (!validStatus)
            {
                throw new InvalidDataException("The runtime receipt status does not agree with required results and assertion results.");
            }
        }

        private static void ValidateRequirements(IEnumerable<RuntimeVerificationRequirement> requirements)
        {
            var values = requirements.ToArray();
            var duplicate = values
                .GroupBy(value => value?.Id ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            if (duplicate != null)
            {
                throw new InvalidDataException("The runtime verification manifest contains a missing or duplicate requirement ID.");
            }
        }

        private static void ValidateAssertion(
            RuntimeVerificationAssertion assertion,
            IEnumerable<PageIngredientNode> ingredientNodes,
            IEnumerable<PageIngredientAction> ingredientActions)
        {
            if (assertion == null
                || string.IsNullOrWhiteSpace(assertion.AssertionOwnerLane)
                || string.IsNullOrWhiteSpace(assertion.Subtype)
                || string.IsNullOrWhiteSpace(assertion.SemanticRole)
                || string.IsNullOrWhiteSpace(assertion.SourcePredicateId)
                || string.IsNullOrWhiteSpace(assertion.SourcePredicateVersion)
                || string.IsNullOrWhiteSpace(assertion.SourcePageOrListItemIdentity)
                || string.IsNullOrWhiteSpace(assertion.SourceVersionIdentity)
                || !IsDigest(assertion.SourceEvidenceDigestSha256)
                || string.IsNullOrWhiteSpace(assertion.StableAssertionKey)
                || string.IsNullOrWhiteSpace(assertion.TargetProfileId)
                || string.IsNullOrWhiteSpace(assertion.FixtureContractVersion))
            {
                throw new InvalidDataException("A runtime assertion is missing its canonical identity or source evidence binding.");
            }
            if (assertion.AttachedActionReferences == null || assertion.AttachedActionReferences.Count == 0)
            {
                throw new InvalidDataException("A runtime assertion requires at least one attached canonical action.");
            }

            var references = assertion.AttachedActionReferences.ToArray();
            var canonical = references
                .OrderBy(value => value?.IngredientId, StringComparer.Ordinal)
                .ThenBy(value => value?.ActionId, StringComparer.Ordinal)
                .ToArray();
            if (!references.SequenceEqual(canonical)
                || references.Any(value => value == null
                    || string.IsNullOrWhiteSpace(value.IngredientId)
                    || string.IsNullOrWhiteSpace(value.ActionId)
                    || string.IsNullOrWhiteSpace(value.DependencyRole))
                || references.GroupBy(value => value.ActionId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                throw new InvalidDataException("Runtime assertion action references must be non-empty, unique, and canonically ordered.");
            }

            foreach (var reference in references)
            {
                if (!string.Equals(reference.ActionId, "action:" + reference.IngredientId, StringComparison.Ordinal)
                    || ingredientNodes.Count(value => value != null
                        && string.Equals(value.Id, reference.IngredientId, StringComparison.Ordinal)) != 1
                    || ingredientActions.Count(value => value != null
                        && string.Equals(value.IngredientId, reference.IngredientId, StringComparison.Ordinal)
                        && string.Equals(value.ActionId, reference.ActionId, StringComparison.Ordinal)) != 1)
                {
                    throw new InvalidDataException("A runtime assertion references a missing, mismatched, or foreign sealed action.");
                }
            }

            if (assertion.Intent?.InitialState == null
                || assertion.Intent.Action == null
                || assertion.Intent.ExpectedFinalState == null
                || !ValidState(assertion.Intent.InitialState)
                || !ValidState(assertion.Intent.ExpectedFinalState)
                || string.IsNullOrWhiteSpace(assertion.Intent.Action.Kind)
                || string.IsNullOrWhiteSpace(assertion.Intent.Action.Selector)
                || assertion.Intent.Action.TimeoutMilliseconds <= 0
                || !references.Any(value => string.Equals(
                    value.ActionId,
                    assertion.Intent.Action.ActionId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidDataException("A runtime assertion requires bound initial, action, and expected-final intent.");
            }
        }

        private static void ValidateAssertionResult(
            RuntimeVerificationAssertion assertion,
            RuntimeVerificationAssertionResult result,
            string expectedPlanDigest,
            string expectedTargetIdentity,
            DateTimeOffset executionStartedAtUtc)
        {
            BindExact(result.PlanDigest, expectedPlanDigest, "assertion result plan digest");
            BindExact(result.TargetIdentity, expectedTargetIdentity, "assertion result target identity");
            if (result.ObservedAtUtc < executionStartedAtUtc)
            {
                throw new InvalidDataException("A runtime assertion result predates the current execution.");
            }
            if (!result.Passed && string.IsNullOrWhiteSpace(result.FailureReasonCode))
            {
                throw new InvalidDataException("A failed runtime assertion requires a machine-readable failure reason.");
            }

            var actionId = assertion.Intent.Action.ActionId;
            ValidateStateEvidence(result.InitialStateEvidence, "initial", assertion.AssertionId, actionId,
                expectedPlanDigest, expectedTargetIdentity, executionStartedAtUtc);
            ValidateStateEvidence(result.ActionEvidence, "action", assertion.AssertionId, actionId,
                expectedPlanDigest, expectedTargetIdentity, result.InitialStateEvidence.ObservedAtUtc);
            ValidateStateEvidence(result.FinalStateEvidence, "final", assertion.AssertionId, actionId,
                expectedPlanDigest, expectedTargetIdentity, result.ActionEvidence.ObservedAtUtc);
            if (result.ObservedAtUtc < result.FinalStateEvidence.ObservedAtUtc)
            {
                throw new InvalidDataException("The assertion result time predates its final-state evidence.");
            }
        }

        private static void ValidateStateEvidence(
            RuntimeVerificationStateEvidence evidence,
            string stage,
            string assertionId,
            string actionId,
            string planDigest,
            string targetIdentity,
            DateTimeOffset notBeforeUtc)
        {
            if (evidence == null)
            {
                throw new InvalidDataException($"The runtime assertion is missing {stage}-state evidence.");
            }
            BindExact(evidence.Stage, stage, stage + " evidence stage");
            BindExact(evidence.AssertionId, assertionId, stage + " evidence assertion");
            BindExact(evidence.ActionId, actionId, stage + " evidence action");
            BindExact(evidence.PlanDigest, planDigest, stage + " evidence plan digest");
            BindExact(evidence.TargetIdentity, targetIdentity, stage + " evidence target identity");
            if (evidence.ObservedAtUtc < notBeforeUtc
                || !IsDigest(evidence.ObservedStateDigestSha256)
                || !IsDigest(evidence.EvidenceArtifactSha256))
            {
                throw new InvalidDataException($"The runtime assertion has stale or incomplete {stage}-state evidence.");
            }
        }

        private static bool ValidState(RuntimeVerificationStateExpectation state)
        {
            return !string.IsNullOrWhiteSpace(state.StateId)
                && !string.IsNullOrWhiteSpace(state.Predicate)
                && !string.IsNullOrWhiteSpace(state.EvidenceKind);
        }

        private static string OwnerIdentity(RuntimeVerificationAssertion assertion)
        {
            return assertion == null
                ? string.Empty
                : string.Join("\u001f", new[]
                {
                    assertion.Subtype ?? string.Empty,
                    assertion.SemanticRole ?? string.Empty,
                    assertion.SourcePredicateId ?? string.Empty,
                    assertion.SourcePredicateVersion ?? string.Empty,
                    assertion.SourcePageOrListItemIdentity ?? string.Empty,
                    assertion.SourceVersionIdentity ?? string.Empty,
                    assertion.StableAssertionKey ?? string.Empty
                });
        }

        private static void BindExact(string actual, string expected, string name)
        {
            if (string.IsNullOrWhiteSpace(expected)
                || !string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The {name} is missing or bound to a different plan execution.");
            }
        }

        private static bool IsDigest(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(character => (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }
    }
}
