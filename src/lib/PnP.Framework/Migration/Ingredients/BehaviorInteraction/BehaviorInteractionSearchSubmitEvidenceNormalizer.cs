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

        public bool TargetRuntimePreconditionPassed { get; set; }

        public string TargetRuntimePreconditionReasonCode { get; set; }

        public string TargetRuntimePreconditionFailure { get; set; }

        public IReadOnlyDictionary<string, string> SourceValueDigests { get; set; }

        public IReadOnlyDictionary<string, string> TargetValueDigests { get; set; }
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
        public const string ResultScriptProviderType = "Microsoft.Office.Server.Search.WebControls.ResultScriptWebPart";
        public const string DependentResultProviderMissing = "DEPENDENT_RESULT_PROVIDER_MISSING";

        private static readonly string[] RequiredSourceValuePaths =
        {
            "trigger.canonicalOwnerInstanceId",
            "target.canonicalOwnerInstanceId",
            "action.kind",
            "action.timeoutMilliseconds",
            "action.maximumAttempts",
            "configuration.allowEmptySearch",
            "configuration.tryInplaceQuery",
            "topology.source.providerCanonicalInstanceId",
            "topology.source.dynamicRegionCanonicalId",
            "topology.source.pageIdentity",
            "topology.source.pageVersion",
            "topology.source.providerType",
            "topology.source.queryGroupName",
            "topology.source.updateAjaxNavigate",
            "topology.source.configurationDigest",
            "topology.source.availability"
        };

        private static readonly string[] RequiredTargetValuePaths =
        {
            "trigger.canonicalOwnerInstanceId",
            "action.kind",
            "action.timeoutMilliseconds",
            "action.maximumAttempts",
            "configuration.allowEmptySearch",
            "configuration.tryInplaceQuery",
            "topology.mapping.sourceSearchBoxInstanceId",
            "topology.mapping.targetSearchBoxInstanceId",
            "topology.mapping.sourceProviderInstanceId",
            "topology.mapping.targetProviderInstanceId",
            "topology.mapping.sourceDynamicRegionId",
            "topology.mapping.targetDynamicRegionId",
            "topology.mapping.sourcePageIdentity",
            "topology.mapping.sourcePageVersion",
            "topology.mapping.targetPageIdentity",
            "topology.mapping.targetPageVersion",
            "topology.target.providerCanonicalInstanceId",
            "topology.target.dynamicRegionCanonicalId",
            "topology.target.pageIdentity",
            "topology.target.pageVersion",
            "topology.target.providerType",
            "topology.target.queryGroupName",
            "topology.target.updateAjaxNavigate",
            "topology.target.configurationDigest",
            "topology.target.observedAtUtc",
            "topology.target.availability",
            "topology.target.operationReference",
            "topology.target.markerReference",
            "topology.searchBoxQueryGroupName",
            "topology.admittedReviewedConfigurationDigest",
            "topology.targetReadbackNotBeforeUtc",
            "topology.runtimeFinalEvidenceAtUtc",
            "topology.lease.leaseId",
            "topology.lease.status",
            "topology.lease.activeFromUtc",
            "topology.lease.retainThroughUtc",
            "topology.lease.evidenceReference",
            "topology.providerInventoryEvidenceReference"
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
                ResultScriptTopology = Topology(evidence?.ResultScriptTopology),
                TypedVerdictPolicy = Verdicts(evidence?.TypedVerdictPolicy)
            };
            var canonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var runtimeValid = ValidateRuntimeContract(evidence, out var runtimeFailure);
            var sourceTopologyValid = ValidateSourceTopology(evidence, out var sourceTopologyFailure);
            var predicateMatched = runtimeValid && sourceTopologyValid && SafeSearchSubmitPredicate(evidence);
            var identityMatched = IdentityMatches(context, assertion);
            var sourceMatched = SourceMatches(context, evidence);
            var targetTopologyReady = ValidateTargetTopology(evidence, out var targetTopologyFailure);

            return new BehaviorInteractionSearchSubmitNormalization
            {
                SemanticCanonicalJson = canonical,
                RuntimeContractValid = runtimeValid,
                SourcePredicateMatched = predicateMatched,
                IdentityBindingMatched = identityMatched,
                SourceBindingMatched = sourceMatched,
                FailureReason = runtimeFailure ?? sourceTopologyFailure,
                TargetRuntimePreconditionPassed = targetTopologyReady,
                TargetRuntimePreconditionReasonCode = targetTopologyReady ? null : DependentResultProviderMissing,
                TargetRuntimePreconditionFailure = targetTopologyFailure,
                SourceValueDigests = SourceValueDigests(evidence),
                TargetValueDigests = TargetValueDigests(evidence)
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
                    && ValuesMatch(
                        observations,
                        IngredientObservationOrigin.AuthenticatedSource,
                        RequiredSourceValuePaths,
                        normalized?.SourceValueDigests),
                TargetFreshReadback = evidence.TargetFreshReadback
                    && normalized?.TargetRuntimePreconditionPassed == true
                    && ValuesMatch(
                        observations,
                        IngredientObservationOrigin.CupCollectFreshReadback,
                        RequiredTargetValuePaths,
                        normalized.TargetValueDigests),
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

        private static bool ValidateSourceTopology(
            BehaviorInteractionSearchSubmitSourceEvidence evidence,
            out string failureReason)
        {
            var assertion = evidence?.Assertion;
            var topology = evidence?.ResultScriptTopology;
            var provider = topology?.SourceProvider;
            var mapping = topology?.TargetMapping;
            var references = assertion?.AttachedActionReferences ?? Array.Empty<RuntimeVerificationActionReference>();
            if (provider == null || mapping == null)
            {
                failureReason = "The typed source ResultScript provider topology or target mapping is missing.";
                return false;
            }
            if (provider.Availability != BehaviorInteractionProviderAvailability.Available
                || !string.Equals(provider.CanonicalProviderInstanceId, evidence?.Target?.CanonicalOwnerInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(provider.CanonicalProviderInstanceId, mapping.SourceProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(provider.CanonicalDynamicRegionId, mapping.SourceDynamicRegionId, StringComparison.Ordinal)
                || !references.Any(value => value != null
                    && string.Equals(value.IngredientId, provider.CanonicalDynamicRegionId, StringComparison.Ordinal)))
            {
                failureReason = "The source ResultScript provider identity is unavailable or does not match the attached dynamic region and target mapping.";
                return false;
            }
            if (!string.Equals(provider.PageIdentity, assertion?.SourcePageOrListItemIdentity, StringComparison.Ordinal)
                || !string.Equals(provider.PageVersion, assertion?.SourceVersionIdentity, StringComparison.Ordinal)
                || !string.Equals(mapping.SourcePageIdentity, provider.PageIdentity, StringComparison.Ordinal)
                || !string.Equals(mapping.SourcePageVersion, provider.PageVersion, StringComparison.Ordinal))
            {
                failureReason = "The source ResultScript provider does not bind the asserted page identity and version.";
                return false;
            }
            if (!ProviderConfigurationMatches(evidence, provider, topology)
                || provider.ObservedAtUtc == default
                || string.IsNullOrWhiteSpace(provider.OperationReference))
            {
                failureReason = "The source ResultScript provider type, query group, UpdateAjaxNavigate configuration, freshness, or operation reference is incomplete.";
                return false;
            }
            failureReason = null;
            return true;
        }

        private static bool ValidateTargetTopology(
            BehaviorInteractionSearchSubmitSourceEvidence evidence,
            out string failureReason)
        {
            if (!ValidateSourceTopology(evidence, out failureReason))
            {
                return false;
            }

            var topology = evidence.ResultScriptTopology;
            var provider = topology.TargetProvider;
            var mapping = topology.TargetMapping;
            var lease = topology.Lease;
            if (provider == null || lease == null)
            {
                failureReason = "The mapped target ResultScript provider or consumer lease is missing.";
                return false;
            }
            if (provider.Availability != BehaviorInteractionProviderAvailability.Available
                || provider.CleanupObservedAtUtc.HasValue
                    && provider.CleanupObservedAtUtc.Value <= topology.RuntimeFinalEvidenceAtUtc)
            {
                failureReason = "The mapped target ResultScript provider is missing or was cleaned before final runtime evidence.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(mapping.TargetSearchBoxInstanceId)
                || string.IsNullOrWhiteSpace(mapping.TargetProviderInstanceId)
                || string.IsNullOrWhiteSpace(mapping.TargetDynamicRegionId)
                || !string.Equals(mapping.SourceSearchBoxInstanceId, evidence.Trigger?.CanonicalOwnerInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(mapping.SourceProviderInstanceId, topology.SourceProvider.CanonicalProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(mapping.TargetProviderInstanceId, provider.CanonicalProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(mapping.TargetDynamicRegionId, provider.CanonicalDynamicRegionId, StringComparison.Ordinal)
                || !EndsWithInstance(provider.CanonicalDynamicRegionId, provider.CanonicalProviderInstanceId)
                || string.IsNullOrWhiteSpace(mapping.EvidenceReference))
            {
                failureReason = "The target Search Box, ResultScript provider, or dynamic-region mapping is incomplete or wrong.";
                return false;
            }
            if (!string.Equals(provider.PageIdentity, mapping.TargetPageIdentity, StringComparison.Ordinal)
                || !string.Equals(provider.PageVersion, mapping.TargetPageVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(provider.PageIdentity)
                || string.IsNullOrWhiteSpace(provider.PageVersion))
            {
                failureReason = "The mapped ResultScript provider is not freshly bound to the same target page identity and version as the Search Box.";
                return false;
            }
            if (!ProviderConfigurationMatches(evidence, provider, topology)
                || !IngredientMaturityEvaluator.IsSha256(topology.AdmittedReviewedConfigurationDigest)
                || !string.Equals(provider.ConfigurationDigest, topology.AdmittedReviewedConfigurationDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(topology.SourceProvider.ConfigurationDigest, topology.AdmittedReviewedConfigurationDigest, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "The mapped ResultScript provider does not match the Search Box query group or the admitted reviewed configuration.";
                return false;
            }
            if (topology.TargetReadbackNotBeforeUtc == default
                || topology.RuntimeFinalEvidenceAtUtc == default
                || topology.TargetReadbackNotBeforeUtc > topology.RuntimeFinalEvidenceAtUtc
                || provider.ObservedAtUtc < topology.TargetReadbackNotBeforeUtc
                || provider.ObservedAtUtc > topology.RuntimeFinalEvidenceAtUtc)
            {
                failureReason = "The mapped ResultScript provider readback is stale or falls outside the admitted runtime evidence window.";
                return false;
            }
            if (!string.Equals(lease.Status, "active", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(lease.LeaseId)
                || lease.ActiveFromUtc == default
                || lease.RetainThroughUtc == default
                || lease.ActiveFromUtc > provider.ObservedAtUtc
                || lease.RetainThroughUtc < topology.RuntimeFinalEvidenceAtUtc
                || lease.ReleasedAtUtc.HasValue && lease.ReleasedAtUtc.Value <= topology.RuntimeFinalEvidenceAtUtc
                || string.IsNullOrWhiteSpace(lease.EvidenceReference))
            {
                failureReason = "The ResultScript consumer lease is not active through final runtime evidence.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(provider.OperationReference)
                || string.IsNullOrWhiteSpace(provider.MarkerReference)
                || string.IsNullOrWhiteSpace(topology.ProviderInventoryEvidenceReference))
            {
                failureReason = "The target provider operation, ownership marker, or inventory evidence reference is missing.";
                return false;
            }
            failureReason = null;
            return true;
        }

        private static bool ProviderConfigurationMatches(
            BehaviorInteractionSearchSubmitSourceEvidence evidence,
            BehaviorInteractionResultScriptProviderEvidence provider,
            BehaviorInteractionResultScriptConsumerTopologyEvidence topology)
        {
            return provider != null
                && string.Equals(provider.ProviderType, ResultScriptProviderType, StringComparison.Ordinal)
                && provider.UpdateAjaxNavigate
                && !string.IsNullOrWhiteSpace(topology?.SearchBoxQueryGroupName)
                && string.Equals(provider.QueryGroupName, topology.SearchBoxQueryGroupName, StringComparison.Ordinal)
                && (evidence?.SearchConfiguration?.QueryGroupNames ?? Array.Empty<string>())
                    .Contains(topology.SearchBoxQueryGroupName, StringComparer.Ordinal)
                && IngredientMaturityEvaluator.IsSha256(provider.ConfigurationDigest);
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
            IEnumerable<string> requiredValuePaths,
            IReadOnlyDictionary<string, string> expected)
        {
            if (expected == null)
            {
                return false;
            }
            foreach (var path in requiredValuePaths)
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

        private static IReadOnlyDictionary<string, string> SourceValueDigests(
            BehaviorInteractionSearchSubmitSourceEvidence evidence)
        {
            var values = CommonValueDigests(evidence);
            var source = evidence?.ResultScriptTopology?.SourceProvider;
            values["target.canonicalOwnerInstanceId"] = ScalarDigest(evidence?.Target?.CanonicalOwnerInstanceId);
            values["topology.source.providerCanonicalInstanceId"] = ScalarDigest(source?.CanonicalProviderInstanceId);
            values["topology.source.dynamicRegionCanonicalId"] = ScalarDigest(source?.CanonicalDynamicRegionId);
            values["topology.source.pageIdentity"] = ScalarDigest(source?.PageIdentity);
            values["topology.source.pageVersion"] = ScalarDigest(source?.PageVersion);
            values["topology.source.providerType"] = ScalarDigest(source?.ProviderType);
            values["topology.source.queryGroupName"] = ScalarDigest(source?.QueryGroupName);
            values["topology.source.updateAjaxNavigate"] = ScalarDigest(source?.UpdateAjaxNavigate ?? false);
            values["topology.source.configurationDigest"] = ScalarDigest(source?.ConfigurationDigest);
            values["topology.source.availability"] = ScalarDigest(source?.Availability ?? BehaviorInteractionProviderAvailability.Unknown);
            return values;
        }

        private static IReadOnlyDictionary<string, string> TargetValueDigests(
            BehaviorInteractionSearchSubmitSourceEvidence evidence)
        {
            var values = CommonValueDigests(evidence);
            var topology = evidence?.ResultScriptTopology;
            var target = topology?.TargetProvider;
            var mapping = topology?.TargetMapping;
            var lease = topology?.Lease;
            values["topology.mapping.sourceSearchBoxInstanceId"] = ScalarDigest(mapping?.SourceSearchBoxInstanceId);
            values["topology.mapping.targetSearchBoxInstanceId"] = ScalarDigest(mapping?.TargetSearchBoxInstanceId);
            values["topology.mapping.sourceProviderInstanceId"] = ScalarDigest(mapping?.SourceProviderInstanceId);
            values["topology.mapping.targetProviderInstanceId"] = ScalarDigest(mapping?.TargetProviderInstanceId);
            values["topology.mapping.sourceDynamicRegionId"] = ScalarDigest(mapping?.SourceDynamicRegionId);
            values["topology.mapping.targetDynamicRegionId"] = ScalarDigest(mapping?.TargetDynamicRegionId);
            values["topology.mapping.sourcePageIdentity"] = ScalarDigest(mapping?.SourcePageIdentity);
            values["topology.mapping.sourcePageVersion"] = ScalarDigest(mapping?.SourcePageVersion);
            values["topology.mapping.targetPageIdentity"] = ScalarDigest(mapping?.TargetPageIdentity);
            values["topology.mapping.targetPageVersion"] = ScalarDigest(mapping?.TargetPageVersion);
            values["topology.target.providerCanonicalInstanceId"] = ScalarDigest(target?.CanonicalProviderInstanceId);
            values["topology.target.dynamicRegionCanonicalId"] = ScalarDigest(target?.CanonicalDynamicRegionId);
            values["topology.target.pageIdentity"] = ScalarDigest(target?.PageIdentity);
            values["topology.target.pageVersion"] = ScalarDigest(target?.PageVersion);
            values["topology.target.providerType"] = ScalarDigest(target?.ProviderType);
            values["topology.target.queryGroupName"] = ScalarDigest(target?.QueryGroupName);
            values["topology.target.updateAjaxNavigate"] = ScalarDigest(target?.UpdateAjaxNavigate ?? false);
            values["topology.target.configurationDigest"] = ScalarDigest(target?.ConfigurationDigest);
            values["topology.target.observedAtUtc"] = ScalarDigest(target?.ObservedAtUtc ?? default);
            values["topology.target.availability"] = ScalarDigest(target?.Availability ?? BehaviorInteractionProviderAvailability.Unknown);
            values["topology.target.operationReference"] = ScalarDigest(target?.OperationReference);
            values["topology.target.markerReference"] = ScalarDigest(target?.MarkerReference);
            values["topology.searchBoxQueryGroupName"] = ScalarDigest(topology?.SearchBoxQueryGroupName);
            values["topology.admittedReviewedConfigurationDigest"] = ScalarDigest(topology?.AdmittedReviewedConfigurationDigest);
            values["topology.targetReadbackNotBeforeUtc"] = ScalarDigest(topology?.TargetReadbackNotBeforeUtc ?? default);
            values["topology.runtimeFinalEvidenceAtUtc"] = ScalarDigest(topology?.RuntimeFinalEvidenceAtUtc ?? default);
            values["topology.lease.leaseId"] = ScalarDigest(lease?.LeaseId);
            values["topology.lease.status"] = ScalarDigest(lease?.Status);
            values["topology.lease.activeFromUtc"] = ScalarDigest(lease?.ActiveFromUtc ?? default);
            values["topology.lease.retainThroughUtc"] = ScalarDigest(lease?.RetainThroughUtc ?? default);
            values["topology.lease.evidenceReference"] = ScalarDigest(lease?.EvidenceReference);
            values["topology.providerInventoryEvidenceReference"] = ScalarDigest(topology?.ProviderInventoryEvidenceReference);
            return values;
        }

        private static Dictionary<string, string> CommonValueDigests(
            BehaviorInteractionSearchSubmitSourceEvidence evidence)
        {
            var action = evidence?.Assertion?.Intent?.Action;
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["trigger.canonicalOwnerInstanceId"] = ScalarDigest(evidence?.Trigger?.CanonicalOwnerInstanceId),
                ["action.kind"] = ScalarDigest(action?.Kind),
                ["action.timeoutMilliseconds"] = ScalarDigest(action?.TimeoutMilliseconds ?? 0),
                ["action.maximumAttempts"] = ScalarDigest(evidence?.MaximumAttempts ?? 0),
                ["configuration.allowEmptySearch"] = ScalarDigest(evidence?.SearchConfiguration?.AllowEmptySearch ?? true),
                ["configuration.tryInplaceQuery"] = ScalarDigest(evidence?.SearchConfiguration?.TryInplaceQuery ?? false)
            };
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

        private static ResultScriptTopologyProjection Topology(
            BehaviorInteractionResultScriptConsumerTopologyEvidence value)
        {
            return new ResultScriptTopologyProjection
            {
                AdmittedReviewedConfigurationDigest = value?.AdmittedReviewedConfigurationDigest,
                Lease = Lease(value?.Lease),
                ProviderInventoryEvidenceReference = value?.ProviderInventoryEvidenceReference,
                RuntimeFinalEvidenceAtUtc = value?.RuntimeFinalEvidenceAtUtc ?? default,
                SearchBoxQueryGroupName = value?.SearchBoxQueryGroupName,
                SourceProvider = Provider(value?.SourceProvider),
                TargetMapping = Mapping(value?.TargetMapping),
                TargetProvider = Provider(value?.TargetProvider),
                TargetReadbackNotBeforeUtc = value?.TargetReadbackNotBeforeUtc ?? default
            };
        }

        private static ResultScriptProviderProjection Provider(
            BehaviorInteractionResultScriptProviderEvidence value)
        {
            return new ResultScriptProviderProjection
            {
                Availability = value?.Availability ?? BehaviorInteractionProviderAvailability.Unknown,
                CanonicalDynamicRegionId = value?.CanonicalDynamicRegionId,
                CanonicalProviderInstanceId = value?.CanonicalProviderInstanceId,
                CleanupObservedAtUtc = value?.CleanupObservedAtUtc,
                ConfigurationDigest = value?.ConfigurationDigest,
                MarkerReference = value?.MarkerReference,
                ObservedAtUtc = value?.ObservedAtUtc ?? default,
                OperationReference = value?.OperationReference,
                PageIdentity = value?.PageIdentity,
                PageVersion = value?.PageVersion,
                ProviderType = value?.ProviderType,
                QueryGroupName = value?.QueryGroupName,
                UpdateAjaxNavigate = value?.UpdateAjaxNavigate ?? false
            };
        }

        private static ResultScriptTargetMappingProjection Mapping(
            BehaviorInteractionResultScriptTargetMappingEvidence value)
        {
            return new ResultScriptTargetMappingProjection
            {
                EvidenceReference = value?.EvidenceReference,
                SourceDynamicRegionId = value?.SourceDynamicRegionId,
                SourcePageIdentity = value?.SourcePageIdentity,
                SourcePageVersion = value?.SourcePageVersion,
                SourceProviderInstanceId = value?.SourceProviderInstanceId,
                SourceSearchBoxInstanceId = value?.SourceSearchBoxInstanceId,
                TargetDynamicRegionId = value?.TargetDynamicRegionId,
                TargetPageIdentity = value?.TargetPageIdentity,
                TargetPageVersion = value?.TargetPageVersion,
                TargetProviderInstanceId = value?.TargetProviderInstanceId,
                TargetSearchBoxInstanceId = value?.TargetSearchBoxInstanceId
            };
        }

        private static ResultScriptLeaseProjection Lease(
            BehaviorInteractionResultScriptLeaseEvidence value)
        {
            return new ResultScriptLeaseProjection
            {
                ActiveFromUtc = value?.ActiveFromUtc ?? default,
                EvidenceReference = value?.EvidenceReference,
                LeaseId = value?.LeaseId,
                ReleasedAtUtc = value?.ReleasedAtUtc,
                RetainThroughUtc = value?.RetainThroughUtc ?? default,
                Status = value?.Status
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

            public ResultScriptTopologyProjection ResultScriptTopology { get; set; }

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

        private sealed class ResultScriptTopologyProjection
        {
            public string AdmittedReviewedConfigurationDigest { get; set; }

            public ResultScriptLeaseProjection Lease { get; set; }

            public string ProviderInventoryEvidenceReference { get; set; }

            public DateTimeOffset RuntimeFinalEvidenceAtUtc { get; set; }

            public string SearchBoxQueryGroupName { get; set; }

            public ResultScriptProviderProjection SourceProvider { get; set; }

            public ResultScriptTargetMappingProjection TargetMapping { get; set; }

            public ResultScriptProviderProjection TargetProvider { get; set; }

            public DateTimeOffset TargetReadbackNotBeforeUtc { get; set; }
        }

        private sealed class ResultScriptProviderProjection
        {
            public BehaviorInteractionProviderAvailability Availability { get; set; }

            public string CanonicalDynamicRegionId { get; set; }

            public string CanonicalProviderInstanceId { get; set; }

            public DateTimeOffset? CleanupObservedAtUtc { get; set; }

            public string ConfigurationDigest { get; set; }

            public string MarkerReference { get; set; }

            public DateTimeOffset ObservedAtUtc { get; set; }

            public string OperationReference { get; set; }

            public string PageIdentity { get; set; }

            public string PageVersion { get; set; }

            public string ProviderType { get; set; }

            public string QueryGroupName { get; set; }

            public bool UpdateAjaxNavigate { get; set; }
        }

        private sealed class ResultScriptTargetMappingProjection
        {
            public string EvidenceReference { get; set; }

            public string SourceDynamicRegionId { get; set; }

            public string SourcePageIdentity { get; set; }

            public string SourcePageVersion { get; set; }

            public string SourceProviderInstanceId { get; set; }

            public string SourceSearchBoxInstanceId { get; set; }

            public string TargetDynamicRegionId { get; set; }

            public string TargetPageIdentity { get; set; }

            public string TargetPageVersion { get; set; }

            public string TargetProviderInstanceId { get; set; }

            public string TargetSearchBoxInstanceId { get; set; }
        }

        private sealed class ResultScriptLeaseProjection
        {
            public DateTimeOffset ActiveFromUtc { get; set; }

            public string EvidenceReference { get; set; }

            public string LeaseId { get; set; }

            public DateTimeOffset? ReleasedAtUtc { get; set; }

            public DateTimeOffset RetainThroughUtc { get; set; }

            public string Status { get; set; }
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
