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
        public const string SemanticSchema = "pnp-behavior-interaction-search-submit-semantic/v2";
        public const string LegacySemanticSchema = "pnp-behavior-interaction-search-submit-semantic/v1";
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
            "topology.mapping.targetIdentity",
            "topology.mapping.searchBoxActionId",
            "topology.mapping.providerActionId",
            "topology.mapping.admittedPlanDigest",
            "topology.searchBox.canonicalInstanceId",
            "topology.searchBox.pageIdentity",
            "topology.searchBox.pageVersion",
            "topology.searchBox.configuration.allowEmptySearch",
            "topology.searchBox.configuration.maintainQueryState",
            "topology.searchBox.configuration.queryGroupNames",
            "topology.searchBox.configuration.resultsPageAddress",
            "topology.searchBox.configuration.tryInplaceQuery",
            "topology.searchBox.configuration.updatePageTitle",
            "topology.searchBox.configuration.msBeforeShowingProgress",
            "topology.searchBox.observedAtUtc",
            "topology.searchBox.operationReference",
            "topology.searchBox.markerReference",
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
            "topology.admittedTargetConfigurationDigest",
            "topology.admittedPlanDigest",
            "topology.targetReadbackNotBeforeUtc",
            "topology.runtimeFinalEvidenceAtUtc",
            "topology.lease.leaseId",
            "topology.lease.status",
            "topology.lease.activeFromUtc",
            "topology.lease.retainThroughUtc",
            "topology.lease.claimId",
            "topology.lease.sourceVersion",
            "topology.lease.targetIdentity",
            "topology.lease.searchBoxInstanceId",
            "topology.lease.providerInstanceId",
            "topology.lease.operationReference",
            "topology.lease.markerReference",
            "topology.lease.planDigest",
            "topology.lease.evidenceReference",
            "topology.providerInventoryEvidenceReference"
        };

        private static readonly string[] ComparableValuePaths =
        {
            "trigger.canonicalOwnerInstanceId",
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
                ResultScriptTopology = SourceTopology(evidence?.ResultScriptTopology),
                TypedVerdictPolicy = Verdicts(evidence?.TypedVerdictPolicy)
            };
            var canonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var runtimeValid = ValidateRuntimeContract(evidence, out var runtimeFailure);
            var sourceTopologyValid = ValidateSourceTopology(evidence, out var sourceTopologyFailure);
            var predicateMatched = runtimeValid && sourceTopologyValid && SafeSearchSubmitPredicate(evidence);
            var identityMatched = IdentityMatches(context, assertion);
            var sourceMatched = SourceMatches(context, evidence);
            var targetTopologyReady = ValidateTargetTopology(context, evidence, out var targetTopologyFailure);

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
            IngredientMaturityEvaluationContext context,
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
            var comparableObservations = observations
                .Where(value => ComparableValuePaths.Contains(value.ValuePath, StringComparer.Ordinal))
                .ToList();
            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated
                    && BoundValuesMatch(
                        context,
                        evidence,
                        observations,
                        IngredientObservationOrigin.AuthenticatedSource,
                        RequiredSourceValuePaths,
                        normalized?.SourceValueDigests),
                TargetFreshReadback = evidence.TargetFreshReadback
                    && normalized?.TargetRuntimePreconditionPassed == true
                    && BoundValuesMatch(
                        context,
                        evidence,
                        observations,
                        IngredientObservationOrigin.CupCollectFreshReadback,
                        RequiredTargetValuePaths,
                        normalized.TargetValueDigests),
                HistoricalOrSyntheticSubstitution = evidence.HistoricalOrSyntheticSubstitution,
                ReadbackStartedAtUtc = evidence.ReadbackStartedAtUtc,
                Observations = comparableObservations,
                SourceEvidenceReferences = (evidence.SourceEvidenceReferences ?? Array.Empty<string>()).ToList(),
                TargetEvidenceReferences = (evidence.TargetEvidenceReferences ?? Array.Empty<string>()).ToList()
            };
        }

        public static IngredientRuntimeAssertionEvidence ProjectRuntimeAssertionEvidence(
            BehaviorInteractionSearchSubmitSourceEvidence evidence)
        {
            var assertion = evidence?.Assertion;
            var actionId = assertion?.Intent?.Action?.ActionId;
            var actions = (evidence?.IngredientActions ?? Array.Empty<PageIngredientAction>())
                .Where(value => value != null
                    && string.Equals(value.ActionId, actionId, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            var action = actions.Length == 1 ? actions[0] : null;
            var ingredients = (evidence?.IngredientGraph?.Nodes ?? Array.Empty<PageIngredientNode>())
                .Where(value => value != null && action != null
                    && string.Equals(value.Id, action.IngredientId, StringComparison.Ordinal))
                .Take(2)
                .ToArray();

            return new IngredientRuntimeAssertionEvidence
            {
                SourcePredicateId = assertion?.SourcePredicateId,
                CanonicalIngredient = ingredients.Length == 1 ? ingredients[0] : null,
                Action = action
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
            var references = assertion?.AttachedActionReferences ?? Array.Empty<RuntimeVerificationActionReference>();
            if (provider == null)
            {
                failureReason = "The typed source ResultScript provider topology is missing.";
                return false;
            }
            if (provider.Availability != BehaviorInteractionProviderAvailability.Available
                || !string.Equals(provider.CanonicalProviderInstanceId, evidence?.Target?.CanonicalOwnerInstanceId, StringComparison.OrdinalIgnoreCase)
                || !references.Any(value => value != null
                    && string.Equals(value.IngredientId, provider.CanonicalDynamicRegionId, StringComparison.Ordinal)))
            {
                failureReason = "The source ResultScript provider identity is unavailable or does not match the attached dynamic region.";
                return false;
            }
            if (!string.Equals(provider.PageIdentity, assertion?.SourcePageOrListItemIdentity, StringComparison.Ordinal)
                || !string.Equals(provider.PageVersion, assertion?.SourceVersionIdentity, StringComparison.Ordinal))
            {
                failureReason = "The source ResultScript provider does not bind the asserted page identity and version.";
                return false;
            }
            if (!ProviderConfigurationMatches(evidence, provider, topology)
                || !IngredientMaturityEvaluator.IsSha256(topology.AdmittedReviewedConfigurationDigest)
                || !string.Equals(provider.ConfigurationDigest, topology.AdmittedReviewedConfigurationDigest, StringComparison.OrdinalIgnoreCase)
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
            IngredientMaturityEvaluationContext context,
            BehaviorInteractionSearchSubmitSourceEvidence evidence,
            out string failureReason)
        {
            if (!ValidateSourceTopology(evidence, out failureReason))
            {
                return false;
            }

            var topology = evidence.ResultScriptTopology;
            var provider = topology.TargetProvider;
            var searchBox = topology.TargetSearchBox;
            var mapping = topology.TargetMapping;
            var lease = topology.Lease;
            if (provider == null || searchBox == null || mapping == null || lease == null)
            {
                failureReason = "The independently observed target Search Box, mapped ResultScript provider, target mapping, or consumer lease is missing.";
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
                || !string.Equals(mapping.TargetSearchBoxInstanceId, searchBox.CanonicalSearchBoxInstanceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(searchBox.CanonicalSearchBoxInstanceId, provider.CanonicalProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(mapping.TargetDynamicRegionId, provider.CanonicalDynamicRegionId, StringComparison.Ordinal)
                || !EndsWithInstance(provider.CanonicalDynamicRegionId, provider.CanonicalProviderInstanceId)
                || string.IsNullOrWhiteSpace(mapping.EvidenceReference))
            {
                failureReason = "The target Search Box, ResultScript provider, or dynamic-region mapping is incomplete or wrong.";
                return false;
            }
            if (!string.Equals(context?.Target?.TargetProfile, evidence.Assertion?.TargetProfileId, StringComparison.Ordinal)
                || !string.Equals(context?.Target?.TargetIdentity, mapping.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(context?.Target?.TargetIdentity, mapping.TargetPageIdentity, StringComparison.Ordinal)
                || !string.Equals(mapping.SourcePageIdentity, evidence.Assertion?.SourcePageOrListItemIdentity, StringComparison.Ordinal)
                || !string.Equals(mapping.SourcePageVersion, evidence.Assertion?.SourceVersionIdentity, StringComparison.Ordinal)
                || !string.Equals(mapping.SourcePageIdentity, topology.SourceProvider.PageIdentity, StringComparison.Ordinal)
                || !string.Equals(mapping.SourcePageVersion, topology.SourceProvider.PageVersion, StringComparison.Ordinal)
                || !string.Equals(provider.PageIdentity, mapping.TargetPageIdentity, StringComparison.Ordinal)
                || !string.Equals(provider.PageVersion, mapping.TargetPageVersion, StringComparison.Ordinal)
                || !string.Equals(searchBox.PageIdentity, mapping.TargetPageIdentity, StringComparison.Ordinal)
                || !string.Equals(searchBox.PageVersion, mapping.TargetPageVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(provider.PageIdentity)
                || string.IsNullOrWhiteSpace(provider.PageVersion))
            {
                failureReason = "The source-to-target mapping is not bound to the asserted source and evaluation target page identities and versions.";
                return false;
            }
            if (!TargetActionsMatch(context, evidence, mapping)
                || !SearchBoxConfigurationMatches(evidence.SearchConfiguration, searchBox.Configuration)
                || !ProviderConfigurationMatches(evidence, provider, topology)
                || !IngredientMaturityEvaluator.IsSha256(topology.AdmittedReviewedConfigurationDigest)
                || !IngredientMaturityEvaluator.IsSha256(topology.AdmittedTargetConfigurationDigest)
                || !IngredientMaturityEvaluator.IsSha256(topology.AdmittedPlanDigest)
                || !string.Equals(provider.ConfigurationDigest, topology.AdmittedTargetConfigurationDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(topology.SourceProvider.ConfigurationDigest, topology.AdmittedReviewedConfigurationDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(mapping.AdmittedPlanDigest, topology.AdmittedPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "The mapped ResultScript provider does not match the Search Box query group or the admitted reviewed configuration.";
                return false;
            }
            if (topology.TargetReadbackNotBeforeUtc == default
                || topology.RuntimeFinalEvidenceAtUtc == default
                || topology.TargetReadbackNotBeforeUtc > topology.RuntimeFinalEvidenceAtUtc
                || searchBox.ObservedAtUtc < topology.TargetReadbackNotBeforeUtc
                || searchBox.ObservedAtUtc > topology.RuntimeFinalEvidenceAtUtc
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
                || !string.Equals(lease.ClaimId, context?.Identity?.ClaimId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(lease.SourceVersion, evidence.Assertion?.SourceVersionIdentity, StringComparison.Ordinal)
                || !string.Equals(lease.TargetIdentity, context?.Target?.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(lease.SearchBoxInstanceId, searchBox.CanonicalSearchBoxInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(lease.ProviderInstanceId, provider.CanonicalProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(lease.OperationReference, provider.OperationReference, StringComparison.Ordinal)
                || !string.Equals(lease.OperationReference, searchBox.OperationReference, StringComparison.Ordinal)
                || !string.Equals(lease.MarkerReference, provider.MarkerReference, StringComparison.Ordinal)
                || !string.Equals(lease.MarkerReference, searchBox.MarkerReference, StringComparison.Ordinal)
                || !string.Equals(lease.PlanDigest, topology.AdmittedPlanDigest, StringComparison.OrdinalIgnoreCase)
                || !ReferenceContainsToken(lease.MarkerReference, lease.LeaseId)
                || !ReferenceContainsToken(lease.EvidenceReference, lease.LeaseId)
                || string.IsNullOrWhiteSpace(lease.EvidenceReference))
            {
                failureReason = "The ResultScript consumer lease is not active through final runtime evidence.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(provider.OperationReference)
                || string.IsNullOrWhiteSpace(provider.MarkerReference)
                || string.IsNullOrWhiteSpace(searchBox.OperationReference)
                || string.IsNullOrWhiteSpace(searchBox.MarkerReference)
                || string.IsNullOrWhiteSpace(topology.ProviderInventoryEvidenceReference))
            {
                failureReason = "The target provider operation, ownership marker, or inventory evidence reference is missing.";
                return false;
            }
            failureReason = null;
            return true;
        }

        private static bool TargetActionsMatch(
            IngredientMaturityEvaluationContext context,
            BehaviorInteractionSearchSubmitSourceEvidence evidence,
            BehaviorInteractionResultScriptTargetMappingEvidence mapping)
        {
            var actions = evidence?.IngredientActions ?? Array.Empty<PageIngredientAction>();
            var searchBoxActions = actions.Where(value => value != null
                && string.Equals(value.ActionId, mapping?.SearchBoxActionId, StringComparison.Ordinal)).ToArray();
            var providerActions = actions.Where(value => value != null
                && string.Equals(value.ActionId, mapping?.ProviderActionId, StringComparison.Ordinal)).ToArray();
            return searchBoxActions.Length == 1
                && providerActions.Length == 1
                && string.Equals(mapping.SearchBoxActionId, evidence.Assertion?.Intent?.Action?.ActionId, StringComparison.Ordinal)
                && EndsWithInstance(searchBoxActions[0].IngredientId, mapping.SourceSearchBoxInstanceId)
                && string.Equals(providerActions[0].IngredientId, mapping.SourceDynamicRegionId, StringComparison.Ordinal)
                && string.Equals(searchBoxActions[0].TargetIdentity, context?.Target?.TargetIdentity, StringComparison.Ordinal)
                && string.Equals(providerActions[0].TargetIdentity, context?.Target?.TargetIdentity, StringComparison.Ordinal);
        }

        private static bool SearchBoxConfigurationMatches(
            BehaviorInteractionSearchConfiguration expected,
            BehaviorInteractionSearchConfiguration observed)
        {
            return expected != null
                && observed != null
                && expected.AllowEmptySearch == observed.AllowEmptySearch
                && expected.MaintainQueryState == observed.MaintainQueryState
                && expected.TryInplaceQuery == observed.TryInplaceQuery
                && expected.UpdatePageTitle == observed.UpdatePageTitle
                && expected.MsBeforeShowingProgress == observed.MsBeforeShowingProgress
                && string.Equals(expected.ResultsPageAddress, observed.ResultsPageAddress, StringComparison.Ordinal)
                && CanonicalStrings(expected.QueryGroupNames)
                    .SequenceEqual(CanonicalStrings(observed.QueryGroupNames), StringComparer.Ordinal);
        }

        private static bool ReferenceContainsToken(string reference, string token)
        {
            return !string.IsNullOrWhiteSpace(reference)
                && !string.IsNullOrWhiteSpace(token)
                && reference.IndexOf(token, StringComparison.Ordinal) >= 0;
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

        private static bool BoundValuesMatch(
            IngredientMaturityEvaluationContext context,
            IngredientLiveEvidence evidence,
            IEnumerable<IngredientValueObservation> observations,
            IngredientObservationOrigin origin,
            IEnumerable<string> requiredValuePaths,
            IReadOnlyDictionary<string, string> expected)
        {
            if (context?.Identity == null
                || context.Source == null
                || context.Target == null
                || evidence == null
                || expected == null
                || !context.ObservationWindowStartUtc.HasValue
                || !context.ObservationWindowEndUtc.HasValue
                || context.ObservationWindowStartUtc.Value == default
                || context.ObservationWindowEndUtc.Value == default
                || context.ObservationWindowStartUtc.Value.Offset != TimeSpan.Zero
                || context.ObservationWindowEndUtc.Value.Offset != TimeSpan.Zero
                || context.ObservationWindowStartUtc > context.ObservationWindowEndUtc
                || evidence.ReadbackStartedAtUtc == default
                || evidence.ReadbackStartedAtUtc.Offset != TimeSpan.Zero
                || evidence.ReadbackStartedAtUtc < context.ObservationWindowStartUtc
                || evidence.ReadbackStartedAtUtc > context.ObservationWindowEndUtc)
            {
                return false;
            }
            var expectedSource = MigrationContractSerializer.SerializeCanonical(context.Source);
            var expectedTarget = MigrationContractSerializer.SerializeCanonical(context.Target);
            foreach (var path in requiredValuePaths)
            {
                var candidates = observations.Where(value => value.Origin == origin
                    && string.Equals(value.ValuePath, path, StringComparison.Ordinal)).ToArray();
                if (candidates.Length != 1
                    || !expected.TryGetValue(path, out var digest)
                    || !ObservationMatches(
                        context,
                        evidence,
                        candidates[0],
                        origin,
                        digest,
                        expectedSource,
                        expectedTarget))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ObservationMatches(
            IngredientMaturityEvaluationContext context,
            IngredientLiveEvidence evidence,
            IngredientValueObservation observation,
            IngredientObservationOrigin origin,
            string expectedDigest,
            string expectedSource,
            string expectedTarget)
        {
            if (observation == null
                || observation.Origin != origin
                || !string.Equals(observation.ClaimId, context.Identity.ClaimId, StringComparison.Ordinal)
                || !string.Equals(observation.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                || observation.Source == null
                || observation.Target == null
                || !string.Equals(
                    MigrationContractSerializer.SerializeCanonical(observation.Source),
                    expectedSource,
                    StringComparison.Ordinal)
                || !string.Equals(
                    MigrationContractSerializer.SerializeCanonical(observation.Target),
                    expectedTarget,
                    StringComparison.Ordinal)
                || !string.Equals(observation.ValueDigest, expectedDigest, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(observation.EvidenceReference)
                || observation.ObservedAtUtc == default
                || observation.ObservedAtUtc.Offset != TimeSpan.Zero
                || observation.ObservedAtUtc < context.ObservationWindowStartUtc
                || observation.ObservedAtUtc > context.ObservationWindowEndUtc)
            {
                return false;
            }

            if (origin == IngredientObservationOrigin.AuthenticatedSource)
            {
                return observation.ObservedAtUtc <= evidence.ReadbackStartedAtUtc
                    && evidence.SourceEvidenceReferences != null
                    && evidence.SourceEvidenceReferences.Contains(
                        observation.EvidenceReference,
                        StringComparer.Ordinal);
            }

            return observation.ObservedAtUtc >= evidence.ReadbackStartedAtUtc
                && evidence.TargetEvidenceReferences != null
                && evidence.TargetEvidenceReferences.Contains(
                    observation.EvidenceReference,
                    StringComparer.Ordinal);
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
            var searchBox = topology?.TargetSearchBox;
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
            values["topology.mapping.targetIdentity"] = ScalarDigest(mapping?.TargetIdentity);
            values["topology.mapping.searchBoxActionId"] = ScalarDigest(mapping?.SearchBoxActionId);
            values["topology.mapping.providerActionId"] = ScalarDigest(mapping?.ProviderActionId);
            values["topology.mapping.admittedPlanDigest"] = ScalarDigest(mapping?.AdmittedPlanDigest);
            values["topology.searchBox.canonicalInstanceId"] = ScalarDigest(searchBox?.CanonicalSearchBoxInstanceId);
            values["topology.searchBox.pageIdentity"] = ScalarDigest(searchBox?.PageIdentity);
            values["topology.searchBox.pageVersion"] = ScalarDigest(searchBox?.PageVersion);
            values["topology.searchBox.configuration.allowEmptySearch"] = ScalarDigest(searchBox?.Configuration?.AllowEmptySearch ?? true);
            values["topology.searchBox.configuration.maintainQueryState"] = ScalarDigest(searchBox?.Configuration?.MaintainQueryState ?? false);
            values["topology.searchBox.configuration.queryGroupNames"] = ScalarDigest(CanonicalStrings(searchBox?.Configuration?.QueryGroupNames));
            values["topology.searchBox.configuration.resultsPageAddress"] = ScalarDigest(searchBox?.Configuration?.ResultsPageAddress);
            values["topology.searchBox.configuration.tryInplaceQuery"] = ScalarDigest(searchBox?.Configuration?.TryInplaceQuery ?? false);
            values["topology.searchBox.configuration.updatePageTitle"] = ScalarDigest(searchBox?.Configuration?.UpdatePageTitle ?? false);
            values["topology.searchBox.configuration.msBeforeShowingProgress"] = ScalarDigest(searchBox?.Configuration?.MsBeforeShowingProgress ?? 0);
            values["topology.searchBox.observedAtUtc"] = ScalarDigest(searchBox?.ObservedAtUtc ?? default);
            values["topology.searchBox.operationReference"] = ScalarDigest(searchBox?.OperationReference);
            values["topology.searchBox.markerReference"] = ScalarDigest(searchBox?.MarkerReference);
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
            values["topology.admittedTargetConfigurationDigest"] = ScalarDigest(topology?.AdmittedTargetConfigurationDigest);
            values["topology.admittedPlanDigest"] = ScalarDigest(topology?.AdmittedPlanDigest);
            values["topology.targetReadbackNotBeforeUtc"] = ScalarDigest(topology?.TargetReadbackNotBeforeUtc ?? default);
            values["topology.runtimeFinalEvidenceAtUtc"] = ScalarDigest(topology?.RuntimeFinalEvidenceAtUtc ?? default);
            values["topology.lease.leaseId"] = ScalarDigest(lease?.LeaseId);
            values["topology.lease.status"] = ScalarDigest(lease?.Status);
            values["topology.lease.activeFromUtc"] = ScalarDigest(lease?.ActiveFromUtc ?? default);
            values["topology.lease.retainThroughUtc"] = ScalarDigest(lease?.RetainThroughUtc ?? default);
            values["topology.lease.claimId"] = ScalarDigest(lease?.ClaimId);
            values["topology.lease.sourceVersion"] = ScalarDigest(lease?.SourceVersion);
            values["topology.lease.targetIdentity"] = ScalarDigest(lease?.TargetIdentity);
            values["topology.lease.searchBoxInstanceId"] = ScalarDigest(lease?.SearchBoxInstanceId);
            values["topology.lease.providerInstanceId"] = ScalarDigest(lease?.ProviderInstanceId);
            values["topology.lease.operationReference"] = ScalarDigest(lease?.OperationReference);
            values["topology.lease.markerReference"] = ScalarDigest(lease?.MarkerReference);
            values["topology.lease.planDigest"] = ScalarDigest(lease?.PlanDigest);
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

        private static SourceResultScriptTopologyProjection SourceTopology(
            BehaviorInteractionResultScriptConsumerTopologyEvidence value)
        {
            return new SourceResultScriptTopologyProjection
            {
                AdmittedReviewedConfigurationDigest = value?.AdmittedReviewedConfigurationDigest,
                SearchBoxQueryGroupName = value?.SearchBoxQueryGroupName,
                SourceProvider = SourceProvider(value?.SourceProvider)
            };
        }

        private static SourceResultScriptProviderProjection SourceProvider(
            BehaviorInteractionResultScriptProviderEvidence value)
        {
            return new SourceResultScriptProviderProjection
            {
                Availability = value?.Availability ?? BehaviorInteractionProviderAvailability.Unknown,
                CanonicalDynamicRegionId = value?.CanonicalDynamicRegionId,
                CanonicalProviderInstanceId = value?.CanonicalProviderInstanceId,
                ConfigurationDigest = value?.ConfigurationDigest,
                PageIdentity = value?.PageIdentity,
                PageVersion = value?.PageVersion,
                ProviderType = value?.ProviderType,
                QueryGroupName = value?.QueryGroupName,
                UpdateAjaxNavigate = value?.UpdateAjaxNavigate ?? false
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

            public SourceResultScriptTopologyProjection ResultScriptTopology { get; set; }

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

        private sealed class SourceResultScriptTopologyProjection
        {
            public string AdmittedReviewedConfigurationDigest { get; set; }

            public string SearchBoxQueryGroupName { get; set; }

            public SourceResultScriptProviderProjection SourceProvider { get; set; }
        }

        private sealed class SourceResultScriptProviderProjection
        {
            public BehaviorInteractionProviderAvailability Availability { get; set; }

            public string CanonicalDynamicRegionId { get; set; }

            public string CanonicalProviderInstanceId { get; set; }

            public string ConfigurationDigest { get; set; }

            public string PageIdentity { get; set; }

            public string PageVersion { get; set; }

            public string ProviderType { get; set; }

            public string QueryGroupName { get; set; }

            public bool UpdateAjaxNavigate { get; set; }
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
