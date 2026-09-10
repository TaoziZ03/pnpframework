using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.BehaviorInteraction
{
    internal sealed class BehaviorInteractionSearchSubmitNormalization
    {
        public string SemanticCanonicalJson { get; set; }

        public bool RuntimeContractValid { get; set; }

        public bool SourcePredicateMatched { get; set; }

        public bool IdentityBindingMatched { get; set; }

        public bool SourceBindingMatched { get; set; }

        public string FailureReason { get; set; }

        public IReadOnlyDictionary<string, string> ValueDigests { get; set; }
    }

    internal static class BehaviorInteractionSearchSubmitEvidenceNormalizer
    {
        public const string ContributorId = "pnp-behavior-interaction-maturity/v1";
        public const string Lane = "behavior.interaction";
        public const string Subtype = "interaction.search-submit";
        public const string SemanticRole = "interaction-state-transition";
        public const string SourcePredicateId = "interaction.search-box.try-in-place";
        public const string SourcePredicateVersion = "1";
        public const string SemanticSchema = "pnp-behavior-interaction-search-submit-semantic/v1";

        private static readonly string[] RequiredValuePaths =
        {
            "trigger.canonicalOwnerInstanceId",
            "target.canonicalOwnerInstanceId",
            "action.kind",
            "action.timeoutMilliseconds",
            "action.maximumAttempts",
            "configuration.allowEmptySearch",
            "configuration.tryInplaceQuery"
        };

        private static readonly string[] RequiredForbiddenBoundaries =
        {
            "source mutation",
            "cross-origin navigation",
            "download",
            "dialog",
            "destructive form submission",
            "external-service mutation"
        };

        public static BehaviorInteractionSearchSubmitNormalization Normalize(
            IngredientMaturityEvaluationContext context,
            BehaviorInteractionSearchSubmitSourceEvidence evidence)
        {
            var assertion = evidence?.Assertion;
            var action = assertion?.Intent?.Action;
            var semantic = new SearchSubmitSemanticProjection
            {
                Schema = SemanticSchema,
                Trigger = Endpoint(evidence?.Trigger),
                Target = Endpoint(evidence?.Target),
                InitialState = State(assertion?.Intent?.InitialState),
                Action = new ActionProjection
                {
                    ActionId = action?.ActionId,
                    Input = action?.Input,
                    Kind = action?.Kind,
                    MaximumAttempts = evidence?.MaximumAttempts ?? 0,
                    Selector = action?.Selector,
                    TimeoutMilliseconds = action?.TimeoutMilliseconds ?? 0
                },
                ExpectedFinalState = State(assertion?.Intent?.ExpectedFinalState),
                RuntimeBoundary = new RuntimeBoundaryProjection
                {
                    Allowed = CanonicalStrings(evidence?.RuntimeBoundary?.Allowed),
                    Forbidden = CanonicalStrings(evidence?.RuntimeBoundary?.Forbidden)
                },
                SearchConfiguration = Configuration(evidence?.SearchConfiguration),
                TypedVerdictPolicy = Verdicts(evidence?.TypedVerdictPolicy)
            };
            var canonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var runtimeValid = ValidateRuntimeContract(evidence, out var runtimeFailure);
            var predicateMatched = runtimeValid && SafeSearchSubmitPredicate(evidence);
            var identityMatched = IdentityMatches(context, assertion);
            var sourceMatched = SourceMatches(context, evidence);

            return new BehaviorInteractionSearchSubmitNormalization
            {
                SemanticCanonicalJson = canonical,
                RuntimeContractValid = runtimeValid,
                SourcePredicateMatched = predicateMatched,
                IdentityBindingMatched = identityMatched,
                SourceBindingMatched = sourceMatched,
                FailureReason = runtimeFailure,
                ValueDigests = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["trigger.canonicalOwnerInstanceId"] = ScalarDigest(evidence?.Trigger?.CanonicalOwnerInstanceId),
                    ["target.canonicalOwnerInstanceId"] = ScalarDigest(evidence?.Target?.CanonicalOwnerInstanceId),
                    ["action.kind"] = ScalarDigest(action?.Kind),
                    ["action.timeoutMilliseconds"] = ScalarDigest(action?.TimeoutMilliseconds ?? 0),
                    ["action.maximumAttempts"] = ScalarDigest(evidence?.MaximumAttempts ?? 0),
                    ["configuration.allowEmptySearch"] = ScalarDigest(evidence?.SearchConfiguration?.AllowEmptySearch ?? true),
                    ["configuration.tryInplaceQuery"] = ScalarDigest(evidence?.SearchConfiguration?.TryInplaceQuery ?? false)
                }
            };
        }

        public static IngredientLiveEvidence ProjectLiveEvidence(
            IngredientLiveEvidence evidence,
            BehaviorInteractionSearchSubmitNormalization normalized)
        {
            if (evidence == null)
            {
                return null;
            }

            var observations = (evidence.Observations ?? Array.Empty<IngredientValueObservation>())
                .Where(value => value != null)
                .ToList();
            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated
                    && ValuesMatch(observations, IngredientObservationOrigin.AuthenticatedSource, normalized?.ValueDigests),
                TargetFreshReadback = evidence.TargetFreshReadback
                    && ValuesMatch(observations, IngredientObservationOrigin.CupCollectFreshReadback, normalized?.ValueDigests),
                HistoricalOrSyntheticSubstitution = evidence.HistoricalOrSyntheticSubstitution,
                Observations = observations,
                SourceEvidenceReferences = (evidence.SourceEvidenceReferences ?? Array.Empty<string>()).ToList(),
                TargetEvidenceReferences = (evidence.TargetEvidenceReferences ?? Array.Empty<string>()).ToList()
            };
        }

        private static bool ValidateRuntimeContract(
            BehaviorInteractionSearchSubmitSourceEvidence evidence,
            out string failureReason)
        {
            try
            {
                RuntimeVerificationContractValidator.ValidateManifest(
                    new RuntimeVerificationManifest
                    {
                        SchemaVersion = RuntimeVerificationContractValidator.ManifestSchemaV2,
                        Requirements = new List<RuntimeVerificationRequirement>(),
                        Assertions = evidence?.Assertion == null
                            ? new List<RuntimeVerificationAssertion>()
                            : new List<RuntimeVerificationAssertion> { evidence.Assertion }
                    },
                    evidence?.IngredientGraph,
                    evidence?.IngredientActions);
                failureReason = null;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is InvalidDataException
                || exception is InvalidOperationException)
            {
                failureReason = exception.Message;
                return false;
            }
        }

        private static bool SafeSearchSubmitPredicate(BehaviorInteractionSearchSubmitSourceEvidence evidence)
        {
            var assertion = evidence?.Assertion;
            var action = assertion?.Intent?.Action;
            var references = assertion?.AttachedActionReferences ?? Array.Empty<RuntimeVerificationActionReference>();
            var forbidden = evidence?.RuntimeBoundary?.Forbidden ?? Array.Empty<string>();
            var verdicts = evidence?.TypedVerdictPolicy;
            var triggerId = evidence?.Trigger?.CanonicalOwnerInstanceId;
            var targetId = evidence?.Target?.CanonicalOwnerInstanceId;
            return string.Equals(assertion?.AssertionOwnerLane, Lane, StringComparison.Ordinal)
                && string.Equals(assertion.Subtype, Subtype, StringComparison.Ordinal)
                && string.Equals(assertion.SemanticRole, SemanticRole, StringComparison.Ordinal)
                && string.Equals(assertion.SourcePredicateId, SourcePredicateId, StringComparison.Ordinal)
                && string.Equals(assertion.SourcePredicateVersion, SourcePredicateVersion, StringComparison.Ordinal)
                && string.Equals(action?.Kind, "replace-text-and-submit-once", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(action.Input)
                && action.TimeoutMilliseconds > 0
                && action.TimeoutMilliseconds <= 15000
                && evidence.MaximumAttempts > 0
                && evidence.MaximumAttempts <= 2
                && evidence.SearchConfiguration?.TryInplaceQuery == true
                && evidence.SearchConfiguration.AllowEmptySearch == false
                && evidence.Trigger?.LocatorIsCanonicalIdentity == false
                && evidence.Target?.LocatorIsCanonicalIdentity == false
                && !string.IsNullOrWhiteSpace(triggerId)
                && !string.IsNullOrWhiteSpace(targetId)
                && references.Any(value => value != null
                    && string.Equals(value.ActionId, action.ActionId, StringComparison.Ordinal)
                    && EndsWithInstance(value.IngredientId, triggerId))
                && references.Any(value => value != null && EndsWithInstance(value.IngredientId, targetId))
                && (evidence.RuntimeBoundary?.Allowed ?? Array.Empty<string>())
                    .Contains("same-origin GET search request", StringComparer.Ordinal)
                && RequiredForbiddenBoundaries.All(value => forbidden.Contains(value, StringComparer.Ordinal))
                && CompleteVerdicts(verdicts);
        }

        private static bool IdentityMatches(
            IngredientMaturityEvaluationContext context,
            RuntimeVerificationAssertion assertion)
        {
            return context?.Identity != null
                && string.Equals(context.Identity.WorkItemType, IngredientMaturityContract.RuntimeVerificationWorkItem, StringComparison.Ordinal)
                && !context.Identity.Kind.HasValue
                && string.Equals(context.Identity.Lane, Lane, StringComparison.Ordinal)
                && string.Equals(context.Identity.IngredientId, assertion?.AssertionId, StringComparison.Ordinal)
                && string.Equals(context.Identity.Subtype, assertion?.Subtype, StringComparison.Ordinal)
                && string.Equals(context.Identity.SemanticRole, assertion?.SemanticRole, StringComparison.Ordinal)
                && string.Equals(context.Identity.SourcePredicateId, assertion?.SourcePredicateId, StringComparison.Ordinal);
        }

        private static bool SourceMatches(
            IngredientMaturityEvaluationContext context,
            BehaviorInteractionSearchSubmitSourceEvidence evidence)
        {
            var assertion = evidence?.Assertion;
            return context?.Source != null
                && string.Equals(context.Source.PageOrListItemIdentity, assertion?.SourcePageOrListItemIdentity, StringComparison.Ordinal)
                && string.Equals(context.Source.SourceVersion, assertion?.SourceVersionIdentity, StringComparison.Ordinal)
                && string.Equals(context.Source.SourceArtifactDigest, assertion?.SourceEvidenceDigestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Source.SourceSnapshotDigest, evidence?.SemanticDigest, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CompleteVerdicts(BehaviorInteractionTypedVerdictPolicy verdicts)
        {
            return verdicts != null
                && !string.IsNullOrWhiteSpace(verdicts.Pass)
                && !string.IsNullOrWhiteSpace(verdicts.Conditional)
                && !string.IsNullOrWhiteSpace(verdicts.Unsupported)
                && !string.IsNullOrWhiteSpace(verdicts.Unknown)
                && !string.IsNullOrWhiteSpace(verdicts.Fail);
        }

        private static bool EndsWithInstance(string ingredientId, string instanceId)
        {
            return !string.IsNullOrWhiteSpace(ingredientId)
                && !string.IsNullOrWhiteSpace(instanceId)
                && ingredientId.EndsWith(":" + instanceId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValuesMatch(
            IEnumerable<IngredientValueObservation> observations,
            IngredientObservationOrigin origin,
            IReadOnlyDictionary<string, string> expected)
        {
            if (expected == null)
            {
                return false;
            }
            foreach (var path in RequiredValuePaths)
            {
                var candidates = observations.Where(value => value.Origin == origin
                    && string.Equals(value.ValuePath, path, StringComparison.Ordinal)).ToArray();
                if (candidates.Length != 1
                    || !expected.TryGetValue(path, out var digest)
                    || !string.Equals(candidates[0].ValueDigest, digest, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static IList<string> CanonicalStrings(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }

        private static EndpointProjection Endpoint(BehaviorInteractionEndpointEvidence value)
        {
            return new EndpointProjection
            {
                CanonicalOwnerInstanceId = value?.CanonicalOwnerInstanceId,
                LocatorIsCanonicalIdentity = value?.LocatorIsCanonicalIdentity ?? true,
                LogicalRole = value?.LogicalRole,
                SourceObservationLocator = value?.SourceObservationLocator
            };
        }

        private static StateProjection State(RuntimeVerificationStateExpectation value)
        {
            return new StateProjection
            {
                EvidenceKind = value?.EvidenceKind,
                Predicate = value?.Predicate,
                StateId = value?.StateId
            };
        }

        private static SearchConfigurationProjection Configuration(BehaviorInteractionSearchConfiguration value)
        {
            return new SearchConfigurationProjection
            {
                AllowEmptySearch = value?.AllowEmptySearch ?? true,
                MaintainQueryState = value?.MaintainQueryState ?? false,
                MsBeforeShowingProgress = value?.MsBeforeShowingProgress ?? 0,
                QueryGroupNames = CanonicalStrings(value?.QueryGroupNames),
                ResultsPageAddress = value?.ResultsPageAddress,
                TryInplaceQuery = value?.TryInplaceQuery ?? false,
                UpdatePageTitle = value?.UpdatePageTitle ?? false
            };
        }

        private static TypedVerdictProjection Verdicts(BehaviorInteractionTypedVerdictPolicy value)
        {
            return new TypedVerdictProjection
            {
                Conditional = value?.Conditional,
                Fail = value?.Fail,
                Pass = value?.Pass,
                Unknown = value?.Unknown,
                Unsupported = value?.Unsupported
            };
        }

        private static string ScalarDigest(object value)
        {
            var canonical = value == null ? "null" : MigrationContractSerializer.SerializeCanonical(value);
            return MigrationDigest.ComputeSha256(canonical);
        }

        private sealed class SearchSubmitSemanticProjection
        {
            public string Schema { get; set; }

            public EndpointProjection Trigger { get; set; }

            public EndpointProjection Target { get; set; }

            public StateProjection InitialState { get; set; }

            public ActionProjection Action { get; set; }

            public StateProjection ExpectedFinalState { get; set; }

            public RuntimeBoundaryProjection RuntimeBoundary { get; set; }

            public SearchConfigurationProjection SearchConfiguration { get; set; }

            public TypedVerdictProjection TypedVerdictPolicy { get; set; }
        }

        private sealed class EndpointProjection
        {
            public string CanonicalOwnerInstanceId { get; set; }

            public bool LocatorIsCanonicalIdentity { get; set; }

            public string LogicalRole { get; set; }

            public string SourceObservationLocator { get; set; }
        }

        private sealed class StateProjection
        {
            public string EvidenceKind { get; set; }

            public string Predicate { get; set; }

            public string StateId { get; set; }
        }

        private sealed class ActionProjection
        {
            public string ActionId { get; set; }

            public string Input { get; set; }

            public string Kind { get; set; }

            public int MaximumAttempts { get; set; }

            public string Selector { get; set; }

            public int TimeoutMilliseconds { get; set; }
        }

        private sealed class RuntimeBoundaryProjection
        {
            public IList<string> Allowed { get; set; }

            public IList<string> Forbidden { get; set; }
        }

        private sealed class SearchConfigurationProjection
        {
            public bool AllowEmptySearch { get; set; }

            public bool MaintainQueryState { get; set; }

            public int MsBeforeShowingProgress { get; set; }

            public IList<string> QueryGroupNames { get; set; }

            public string ResultsPageAddress { get; set; }

            public bool TryInplaceQuery { get; set; }

            public bool UpdatePageTitle { get; set; }
        }

        private sealed class TypedVerdictProjection
        {
            public string Conditional { get; set; }

            public string Fail { get; set; }

            public string Pass { get; set; }

            public string Unknown { get; set; }

            public string Unsupported { get; set; }
        }
    }
}
