using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.BehaviorInteraction
{
    internal sealed class BehaviorInteractionRuntimeProducerBinding
    {
        public string ProducerId { get; set; }

        public string ImplementationCommit { get; set; }

        public string BinaryDigestSha256 { get; set; }
    }

    internal sealed class BehaviorInteractionRuntimeStateObservation
    {
        public DateTimeOffset ObservedAtUtc { get; set; }

        public string RawStateJson { get; set; }
    }

    internal sealed class BehaviorInteractionRuntimeReceiptRequest
    {
        public RuntimeVerificationManifest Manifest { get; set; }

        public CanonicalPageIngredientGraph IngredientGraph { get; set; }

        public IList<PageIngredientAction> IngredientActions { get; set; } =
            new List<PageIngredientAction>();

        public string AssertionId { get; set; }

        public string PlanDigest { get; set; }

        public string TargetIdentity { get; set; }

        public string OperationId { get; set; }

        public BehaviorInteractionRuntimeProducerBinding Producer { get; set; }

        public DateTimeOffset ExecutionStartedAtUtc { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public BehaviorInteractionRuntimeStateObservation InitialState { get; set; }

        public BehaviorInteractionRuntimeStateObservation Action { get; set; }

        public BehaviorInteractionRuntimeStateObservation FinalState { get; set; }

        public bool Passed { get; set; }

        public string FailureReasonCode { get; set; }

        public string Message { get; set; }
    }

    internal sealed class BehaviorInteractionRuntimeStateArtifact
    {
        public ArtifactReference Reference { get; set; }

        public string ContentBase64 { get; set; }
    }

    internal sealed class BehaviorInteractionRuntimeReceiptProjection
    {
        public RuntimeVerificationReceipt Receipt { get; set; }

        public IList<BehaviorInteractionRuntimeStateArtifact> StateEvidenceArtifacts { get; set; } =
            new List<BehaviorInteractionRuntimeStateArtifact>();
    }

    /// <summary>
    /// Projects lane-owned interaction observations into the frozen shared v2
    /// receipt contract. Each state artifact retains the exact observed JSON as
    /// well as its canonical semantic form, and the receipt points to the
    /// content-addressed artifact digest.
    /// </summary>
    internal static class BehaviorInteractionRuntimeReceiptProjector
    {
        public const string EvidenceSchemaVersion =
            "pnp-behavior-interaction-runtime-state-evidence/v1";

        private const string InitialStateNotReady =
            "INITIAL_STATE_NOT_READY";
        private const string ActionAttemptBoundViolated =
            "ACTION_ATTEMPT_BOUND_VIOLATED";
        private const string SubmittedQueryNotObserved =
            "SUBMITTED_QUERY_NOT_OBSERVED";
        private const string RuntimeBoundaryEvidenceIncomplete =
            "RUNTIME_BOUNDARY_EVIDENCE_INCOMPLETE";
        private const string UnsafeRuntimeBoundaryObserved =
            "UNSAFE_RUNTIME_BOUNDARY_OBSERVED";
        private const string ExpectedSearchRequestNotObserved =
            "EXPECTED_SEARCH_REQUEST_NOT_OBSERVED";
        private const string NetworkRequestBoundExceeded =
            "NETWORK_REQUEST_BOUND_EXCEEDED";
        private const string InteractionTimeoutExceeded =
            "INTERACTION_TIMEOUT_EXCEEDED";
        private const string ExpectedInPlaceTransitionNotObserved =
            "EXPECTED_IN_PLACE_TRANSITION_NOT_OBSERVED";

        public static BehaviorInteractionRuntimeReceiptProjection Project(
            BehaviorInteractionRuntimeReceiptRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            RuntimeVerificationContractValidator.ValidateManifest(
                request.Manifest,
                request.IngredientGraph,
                request.IngredientActions);

            var assertion = request.Manifest?.Assertions?.SingleOrDefault(value =>
                value != null
                && string.Equals(value.AssertionId, request.AssertionId, StringComparison.Ordinal));
            if (assertion == null)
            {
                throw new InvalidDataException(
                    "The interaction receipt request is not bound to exactly one sealed assertion.");
            }
            ValidateExecutionBinding(request, assertion);

            var verdict = EvaluateSearchSubmitVerdict(request, assertion);
            ValidateDeclaredVerdict(request, verdict);

            var artifacts = new List<BehaviorInteractionRuntimeStateArtifact>();
            var initial = ProjectState("initial", assertion, request, request.InitialState, artifacts);
            var action = ProjectState("action", assertion, request, request.Action, artifacts);
            var final = ProjectState("final", assertion, request, request.FinalState, artifacts);

            var result = new RuntimeVerificationAssertionResult
            {
                AssertionId = assertion.AssertionId,
                PlanDigest = request.PlanDigest,
                TargetIdentity = request.TargetIdentity,
                ObservedAtUtc = request.FinalState.ObservedAtUtc,
                InitialStateEvidence = initial,
                ActionEvidence = action,
                FinalStateEvidence = final,
                Passed = verdict.Passed,
                FailureReasonCode = verdict.FailureReasonCode,
                Message = request.Message
            };
            var receipt = new RuntimeVerificationReceipt
            {
                SchemaVersion = RuntimeVerificationContractValidator.ReceiptSchemaV2,
                PlanDigest = request.PlanDigest,
                TargetIdentity = request.TargetIdentity,
                CompletedAtUtc = request.CompletedAtUtc,
                AssertionResults = new List<RuntimeVerificationAssertionResult> { result },
                Status = verdict.Passed
                    ? RuntimeVerificationStatus.Passed
                    : RuntimeVerificationStatus.Failed
            };
            var projection = new BehaviorInteractionRuntimeReceiptProjection
            {
                Receipt = receipt,
                StateEvidenceArtifacts = artifacts
            };

            Validate(request, projection);
            return projection;
        }

        public static void Validate(
            BehaviorInteractionRuntimeReceiptRequest request,
            BehaviorInteractionRuntimeReceiptProjection projection)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (projection?.Receipt == null)
            {
                throw new InvalidDataException("The projected interaction runtime receipt is missing.");
            }

            RuntimeVerificationContractValidator.ValidateReceipt(
                request.Manifest,
                projection.Receipt,
                request.PlanDigest,
                request.TargetIdentity,
                request.ExecutionStartedAtUtc);

            var assertion = request.Manifest.Assertions.Single(value =>
                string.Equals(value.AssertionId, request.AssertionId, StringComparison.Ordinal));
            var result = projection.Receipt.AssertionResults.Single(value =>
                string.Equals(value.AssertionId, assertion.AssertionId, StringComparison.Ordinal));
            ValidateArtifact(request, assertion, result.InitialStateEvidence, projection.StateEvidenceArtifacts);
            ValidateArtifact(request, assertion, result.ActionEvidence, projection.StateEvidenceArtifacts);
            ValidateArtifact(request, assertion, result.FinalStateEvidence, projection.StateEvidenceArtifacts);
        }

        private static void ValidateExecutionBinding(
            BehaviorInteractionRuntimeReceiptRequest request,
            RuntimeVerificationAssertion assertion)
        {
            if (!IsDigest(request.PlanDigest)
                || string.IsNullOrWhiteSpace(request.TargetIdentity)
                || string.IsNullOrWhiteSpace(request.OperationId)
                || request.Producer == null
                || string.IsNullOrWhiteSpace(request.Producer.ProducerId)
                || !IsCommit(request.Producer.ImplementationCommit)
                || !IsDigest(request.Producer.BinaryDigestSha256)
                || request.ExecutionStartedAtUtc == default
                || request.ExecutionStartedAtUtc.Offset != TimeSpan.Zero
                || request.CompletedAtUtc == default
                || request.CompletedAtUtc.Offset != TimeSpan.Zero
                || request.CompletedAtUtc < request.ExecutionStartedAtUtc
                || (!request.Passed && string.IsNullOrWhiteSpace(request.FailureReasonCode)))
            {
                throw new InvalidDataException(
                    "The interaction receipt execution, operation, producer, or verdict binding is incomplete.");
            }
            if (assertion.Intent?.Action == null)
            {
                throw new InvalidDataException("The sealed interaction assertion action is missing.");
            }

            ValidateObservation(request.InitialState, request.ExecutionStartedAtUtc, "initial");
            ValidateObservation(request.Action, request.InitialState.ObservedAtUtc, "action");
            ValidateObservation(request.FinalState, request.Action.ObservedAtUtc, "final");
            if (request.CompletedAtUtc < request.FinalState.ObservedAtUtc)
            {
                throw new InvalidDataException(
                    "The interaction runtime receipt completes before its final-state evidence.");
            }
        }

        private static BehaviorInteractionRuntimeVerdict EvaluateSearchSubmitVerdict(
            BehaviorInteractionRuntimeReceiptRequest request,
            RuntimeVerificationAssertion assertion)
        {
            if (!string.Equals(
                    assertion.AssertionOwnerLane,
                    BehaviorInteractionSearchSubmitEvidenceNormalizer.Lane,
                    StringComparison.Ordinal)
                || !string.Equals(
                    assertion.Subtype,
                    BehaviorInteractionSearchSubmitEvidenceNormalizer.Subtype,
                    StringComparison.Ordinal)
                || !string.Equals(
                    assertion.SemanticRole,
                    BehaviorInteractionSearchSubmitEvidenceNormalizer.SemanticRole,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The interaction runtime projector only accepts the sealed search-submit assertion contract.");
            }

            using (var initial = ParseObservation(request.InitialState, "initial"))
            using (var action = ParseObservation(request.Action, "action"))
            using (var final = ParseObservation(request.FinalState, "final"))
            {
                if (!IsReadyInitialState(initial.RootElement))
                {
                    return Failed(InitialStateNotReady);
                }

                var expectedQuery = assertion.Intent.Action.Input;
                if (!TryGetInt32(action.RootElement, "action", "attempt", out var attempt)
                    || attempt != 1)
                {
                    return Failed(ActionAttemptBoundViolated);
                }
                if (!TryGetString(action.RootElement, "action", "kind", out var actionKind)
                    || !string.Equals(actionKind, assertion.Intent.Action.Kind, StringComparison.Ordinal)
                    || !TryGetString(action.RootElement, "action", "submittedQuery", out var actionQuery)
                    || !string.Equals(actionQuery, expectedQuery, StringComparison.Ordinal))
                {
                    return Failed(SubmittedQueryNotObserved);
                }

                if (!TryGetBoolean(action.RootElement, "boundary", "crossOriginNavigation", out var crossOriginNavigation)
                    || !TryGetBoolean(action.RootElement, "boundary", "dialog", out var dialog)
                    || !TryGetBoolean(action.RootElement, "boundary", "download", out var download))
                {
                    return Failed(RuntimeBoundaryEvidenceIncomplete);
                }
                if (crossOriginNavigation || dialog || download)
                {
                    return Failed(UnsafeRuntimeBoundaryObserved);
                }

                if (!TryGetInt32(action.RootElement, "network", "sameOriginSearchRequestCount", out var requestCount)
                    || requestCount < 1)
                {
                    return Failed(ExpectedSearchRequestNotObserved);
                }
                if (requestCount > 1)
                {
                    return Failed(NetworkRequestBoundExceeded);
                }

                if (request.FinalState.ObservedAtUtc - request.Action.ObservedAtUtc
                    > TimeSpan.FromMilliseconds(assertion.Intent.Action.TimeoutMilliseconds))
                {
                    return Failed(InteractionTimeoutExceeded);
                }

                if (!TryGetBoolean(final.RootElement, "page", "sameDocument", out var sameDocument)
                    || !sameDocument
                    || !TryGetString(final.RootElement, "query", "submitted", out var finalQuery)
                    || !string.Equals(finalQuery, expectedQuery, StringComparison.Ordinal)
                    || !HasMaterialResultTransition(final.RootElement))
                {
                    return Failed(ExpectedInPlaceTransitionNotObserved);
                }

                return new BehaviorInteractionRuntimeVerdict { Passed = true };
            }
        }

        private static void ValidateDeclaredVerdict(
            BehaviorInteractionRuntimeReceiptRequest request,
            BehaviorInteractionRuntimeVerdict derived)
        {
            if (request.Passed != derived.Passed
                || (!derived.Passed
                    && !string.Equals(
                        request.FailureReasonCode,
                        derived.FailureReasonCode,
                        StringComparison.Ordinal))
                || (derived.Passed && !string.IsNullOrWhiteSpace(request.FailureReasonCode)))
            {
                throw new InvalidDataException(
                    "The declared interaction verdict contradicts the bound runtime state evidence."
                    + (derived.Passed
                        ? " Derived verdict: pass."
                        : " Derived verdict: fail / " + derived.FailureReasonCode + "."));
            }
        }

        private static JsonDocument ParseObservation(
            BehaviorInteractionRuntimeStateObservation observation,
            string stage)
        {
            try
            {
                var document = JsonDocument.Parse(observation.RawStateJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    document.Dispose();
                    throw new InvalidDataException(
                        "The interaction " + stage + " state observation must be a JSON object.");
                }
                return document;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The interaction " + stage + " state observation is not valid JSON.",
                    exception);
            }
        }

        private static bool IsReadyInitialState(JsonElement initial)
        {
            return TryGetBoolean(initial, "input", "enabled", out var enabled)
                && enabled
                && TryGetString(initial, "input", "value", out var value)
                && string.Equals(value, string.Empty, StringComparison.Ordinal)
                && TryGetBoolean(initial, "network", "ownedSubmitInFlight", out var submitInFlight)
                && !submitInFlight
                && TryGetBoolean(initial, "page", "sameDocument", out var sameDocument)
                && sameDocument;
        }

        private static bool HasMaterialResultTransition(JsonElement final)
        {
            if (!TryGetBoolean(final, "resultRegion", "changed", out var changed)
                || !TryGetBoolean(final, "resultRegion", "explicitEmpty", out var explicitEmpty))
            {
                return false;
            }
            if (explicitEmpty)
            {
                return true;
            }
            if (!changed
                || !TryGetString(final, "resultRegion", "stateDigestBefore", out var before)
                || !TryGetString(final, "resultRegion", "stateDigestAfter", out var after))
            {
                return false;
            }
            return IsDigest(before)
                && IsDigest(after)
                && !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetBoolean(
            JsonElement root,
            string parentName,
            string propertyName,
            out bool value)
        {
            value = default;
            if (!TryGetProperty(root, parentName, propertyName, out var property)
                || (property.ValueKind != JsonValueKind.True
                    && property.ValueKind != JsonValueKind.False))
            {
                return false;
            }
            value = property.GetBoolean();
            return true;
        }

        private static bool TryGetInt32(
            JsonElement root,
            string parentName,
            string propertyName,
            out int value)
        {
            value = default;
            return TryGetProperty(root, parentName, propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value);
        }

        private static bool TryGetString(
            JsonElement root,
            string parentName,
            string propertyName,
            out string value)
        {
            value = null;
            if (!TryGetProperty(root, parentName, propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            value = property.GetString();
            return value != null;
        }

        private static bool TryGetProperty(
            JsonElement root,
            string parentName,
            string propertyName,
            out JsonElement property)
        {
            property = default;
            return root.TryGetProperty(parentName, out var parent)
                && parent.ValueKind == JsonValueKind.Object
                && parent.TryGetProperty(propertyName, out property);
        }

        private static BehaviorInteractionRuntimeVerdict Failed(string reasonCode)
        {
            return new BehaviorInteractionRuntimeVerdict
            {
                Passed = false,
                FailureReasonCode = reasonCode
            };
        }

        private static void ValidateObservation(
            BehaviorInteractionRuntimeStateObservation observation,
            DateTimeOffset notBeforeUtc,
            string stage)
        {
            if (observation == null
                || observation.ObservedAtUtc == default
                || observation.ObservedAtUtc.Offset != TimeSpan.Zero
                || observation.ObservedAtUtc < notBeforeUtc
                || string.IsNullOrWhiteSpace(observation.RawStateJson))
            {
                throw new InvalidDataException(
                    "The interaction " + stage + " observation is missing, stale, or not UTC-bound.");
            }
        }

        private static RuntimeVerificationStateEvidence ProjectState(
            string stage,
            RuntimeVerificationAssertion assertion,
            BehaviorInteractionRuntimeReceiptRequest request,
            BehaviorInteractionRuntimeStateObservation observation,
            ICollection<BehaviorInteractionRuntimeStateArtifact> artifacts)
        {
            var rawBytes = Encoding.UTF8.GetBytes(observation.RawStateJson);
            string canonicalState;
            JsonElement normalizedState;
            try
            {
                using (var document = JsonDocument.Parse(rawBytes))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidDataException(
                            "An interaction runtime state observation must be a JSON object.");
                    }
                    normalizedState = document.RootElement.Clone();
                    canonicalState = MigrationContractSerializer.SerializeCanonical(normalizedState);
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The interaction " + stage + " state observation is not valid JSON.",
                    exception);
            }

            var stateDigest = MigrationDigest.ComputeSha256(canonicalState);
            var envelope = new StateEvidenceEnvelope
            {
                SchemaVersion = EvidenceSchemaVersion,
                Stage = stage,
                AssertionId = assertion.AssertionId,
                ActionId = assertion.Intent.Action.ActionId,
                PlanDigest = request.PlanDigest,
                TargetIdentity = request.TargetIdentity,
                SourcePageOrListItemIdentity = assertion.SourcePageOrListItemIdentity,
                SourceVersionIdentity = assertion.SourceVersionIdentity,
                SourceEvidenceDigestSha256 = assertion.SourceEvidenceDigestSha256,
                OperationId = request.OperationId,
                Producer = request.Producer,
                ObservedAtUtc = observation.ObservedAtUtc,
                RawStateJson = observation.RawStateJson,
                RawStateDigestSha256 = MigrationDigest.ComputeSha256(rawBytes),
                NormalizedState = normalizedState,
                ObservedStateDigestSha256 = stateDigest
            };
            var artifactBytes = Encoding.UTF8.GetBytes(
                MigrationContractSerializer.SerializeCanonical(envelope));
            var artifact = new BehaviorInteractionRuntimeStateArtifact
            {
                Reference = MigrationArtifact.Describe(
                    artifactBytes,
                    "application/json",
                    "behavior-interaction-" + stage + "-state-evidence.json"),
                ContentBase64 = Convert.ToBase64String(artifactBytes)
            };
            artifacts.Add(artifact);

            return new RuntimeVerificationStateEvidence
            {
                Stage = stage,
                AssertionId = assertion.AssertionId,
                ActionId = assertion.Intent.Action.ActionId,
                PlanDigest = request.PlanDigest,
                TargetIdentity = request.TargetIdentity,
                ObservedAtUtc = observation.ObservedAtUtc,
                ObservedStateDigestSha256 = stateDigest,
                EvidenceArtifactSha256 = artifact.Reference.Sha256
            };
        }

        private static void ValidateArtifact(
            BehaviorInteractionRuntimeReceiptRequest request,
            RuntimeVerificationAssertion assertion,
            RuntimeVerificationStateEvidence evidence,
            IEnumerable<BehaviorInteractionRuntimeStateArtifact> artifacts)
        {
            var matches = (artifacts ?? Array.Empty<BehaviorInteractionRuntimeStateArtifact>())
                .Where(value => value?.Reference != null
                    && string.Equals(
                        value.Reference.Sha256,
                        evidence.EvidenceArtifactSha256,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    "The interaction " + evidence.Stage
                    + " state evidence artifact is missing or duplicated.");
            }

            var bytes = MigrationArtifact.ReadAllBytes(
                matches[0].Reference,
                matches[0].ContentBase64);
            StateEvidenceEnvelope envelope;
            try
            {
                var json = Encoding.UTF8.GetString(bytes);
                envelope = MigrationContractSerializer.Deserialize<StateEvidenceEnvelope>(json);
                if (!string.Equals(
                    json,
                    MigrationContractSerializer.SerializeCanonical(envelope),
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The interaction state evidence artifact is not canonical JSON.");
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The interaction state evidence artifact is not valid JSON.",
                    exception);
            }

            var rawBytes = Encoding.UTF8.GetBytes(envelope.RawStateJson ?? string.Empty);
            string canonicalState;
            try
            {
                using (var state = JsonDocument.Parse(rawBytes))
                {
                    canonicalState = MigrationContractSerializer.SerializeCanonical(state.RootElement);
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The interaction state evidence artifact contains invalid raw state JSON.",
                    exception);
            }

            var normalizedCanonical = MigrationContractSerializer.SerializeCanonical(envelope.NormalizedState);
            if (!string.Equals(envelope.SchemaVersion, EvidenceSchemaVersion, StringComparison.Ordinal)
                || !string.Equals(envelope.Stage, evidence.Stage, StringComparison.Ordinal)
                || !string.Equals(envelope.AssertionId, assertion.AssertionId, StringComparison.Ordinal)
                || !string.Equals(envelope.ActionId, assertion.Intent.Action.ActionId, StringComparison.Ordinal)
                || !string.Equals(envelope.PlanDigest, request.PlanDigest, StringComparison.Ordinal)
                || !string.Equals(envelope.TargetIdentity, request.TargetIdentity, StringComparison.Ordinal)
                || !string.Equals(envelope.SourcePageOrListItemIdentity, assertion.SourcePageOrListItemIdentity, StringComparison.Ordinal)
                || !string.Equals(envelope.SourceVersionIdentity, assertion.SourceVersionIdentity, StringComparison.Ordinal)
                || !string.Equals(envelope.SourceEvidenceDigestSha256, assertion.SourceEvidenceDigestSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(envelope.OperationId, request.OperationId, StringComparison.Ordinal)
                || !ProducerMatches(envelope.Producer, request.Producer)
                || envelope.ObservedAtUtc != evidence.ObservedAtUtc
                || !string.Equals(MigrationDigest.ComputeSha256(rawBytes), envelope.RawStateDigestSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(canonicalState, normalizedCanonical, StringComparison.Ordinal)
                || !string.Equals(MigrationDigest.ComputeSha256(normalizedCanonical), envelope.ObservedStateDigestSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(envelope.ObservedStateDigestSha256, evidence.ObservedStateDigestSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The interaction " + evidence.Stage
                    + " state evidence artifact is corrupt or bound to another execution.");
            }
        }

        private static bool ProducerMatches(
            BehaviorInteractionRuntimeProducerBinding actual,
            BehaviorInteractionRuntimeProducerBinding expected)
        {
            return actual != null
                && expected != null
                && string.Equals(actual.ProducerId, expected.ProducerId, StringComparison.Ordinal)
                && string.Equals(actual.ImplementationCommit, expected.ImplementationCommit, StringComparison.Ordinal)
                && string.Equals(actual.BinaryDigestSha256, expected.BinaryDigestSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCommit(string value)
        {
            return value != null
                && value.Length == 40
                && value.All(IsHex);
        }

        private static bool IsDigest(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(IsHex);
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }

        private sealed class StateEvidenceEnvelope
        {
            public StateEvidenceEnvelope()
            {
            }

            public string SchemaVersion { get; set; }

            public string Stage { get; set; }

            public string AssertionId { get; set; }

            public string ActionId { get; set; }

            public string PlanDigest { get; set; }

            public string TargetIdentity { get; set; }

            public string SourcePageOrListItemIdentity { get; set; }

            public string SourceVersionIdentity { get; set; }

            public string SourceEvidenceDigestSha256 { get; set; }

            public string OperationId { get; set; }

            public BehaviorInteractionRuntimeProducerBinding Producer { get; set; }

            public DateTimeOffset ObservedAtUtc { get; set; }

            public string RawStateJson { get; set; }

            public string RawStateDigestSha256 { get; set; }

            public JsonElement NormalizedState { get; set; }

            public string ObservedStateDigestSha256 { get; set; }
        }

        private sealed class BehaviorInteractionRuntimeVerdict
        {
            public bool Passed { get; set; }

            public string FailureReasonCode { get; set; }
        }
    }
}
