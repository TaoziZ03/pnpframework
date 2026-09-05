using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleStageResultValidator
    {
        public static void ValidateProbe(
            string outputRoot,
            MigrationActionSignature action,
            ScaleStageProbeResult probe,
            bool requireExactEvidence)
        {
            if (probe == null
                || !Enum.IsDefined(typeof(ScaleStageProbeState), probe.State)
                || probe.Artifacts == null
                || probe.Requests == null
                || !IsSafeDiagnosticCode(probe.DiagnosticCode))
            {
                throw new InvalidDataException("A scale stage probe returned an incomplete result.");
            }
            ValidateRequests(probe.Requests);
            if (probe.State != ScaleStageProbeState.Exact)
            {
                return;
            }
            if (!probe.FreshProbePerformed
                || !probe.ProvenanceMatched
                || !string.Equals(probe.ObservedStateDigest, action.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(probe.TargetIdentityDigest, action.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase)
                || requireExactEvidence && probe.Artifacts.Count == 0)
            {
                throw new InvalidDataException("An exact target probe lacks fresh provenance, action identity, or retained evidence.");
            }
            foreach (var artifact in probe.Artifacts)
            {
                ValidateArtifact(outputRoot, artifact);
            }
        }

        public static void ValidateExecutionResult(
            string outputRoot,
            IScaleRunStageExecutor executor,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            if (result == null
                || !Enum.IsDefined(typeof(ScaleStageOutcome), result.Outcome)
                || result.Artifacts == null
                || result.Requests == null
                || result.Ingredients == null
                || result.Artifacts.Count == 0
                || !IsSafeDiagnosticCode(result.DiagnosticCode))
            {
                throw new InvalidDataException("A scale stage result requires an outcome and content-addressed output or failure evidence.");
            }
            ValidateRequests(result.Requests);
            if (result.DiscoveredProfile != null)
            {
                ScalePageProfile.Validate(result.DiscoveredProfile);
            }
            foreach (var artifact in result.Artifacts)
            {
                ValidateArtifact(outputRoot, artifact);
            }
            ScaleIngredientResultValidator.Validate(outputRoot, action, result);
            if (result.MutationAttempted && (!executor.MutatesTarget || !executor.AllowsLiveMutation))
            {
                throw new InvalidDataException("Only a live-capable mutating executor may report that a target mutation was attempted.");
            }
            if (result.Outcome == ScaleStageOutcome.AuthorizationBlocked)
            {
                ValidateAuthorizationEvidence(outputRoot, action, result);
            }
            if (!ScaleStageOutcomeRules.IsSuccessful(result.Outcome))
            {
                if (!result.Artifacts.Any(value => value.Kind != ScaleStageArtifactKind.Output))
                {
                    throw new InvalidDataException("A non-success stage result must retain diagnostic evidence rather than only output artifacts.");
                }
                return;
            }
            if (!result.Verified
                || !result.ProvenanceMatched
                || !string.Equals(result.ObservedStateDigest, action.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(result.TargetIdentityDigest, action.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase)
                || result.Outcome == ScaleStageOutcome.OutcomeUnknownButConverged && !result.MutationAttempted
                || result.MutationAttempted && !executor.MutatesTarget)
            {
                throw new InvalidDataException("A successful scale stage result lacks exact verification, provenance, artifacts, or action identity.");
            }
        }

        public static ScaleStageExecutionResult CreateSanitizedFailure(
            string outputRoot,
            string stageOutputRoot,
            ScaleRunStage stage,
            int attempt,
            MigrationActionSignature action,
            Exception exception,
            DateTimeOffset capturedAtUtc)
        {
            Directory.CreateDirectory(stageOutputRoot);
            var evidence = new ScaleControllerFailureEvidence
            {
                Stage = stage,
                Attempt = attempt,
                ActionSignature = action.Signature,
                ExceptionType = exception?.GetType().FullName ?? typeof(Exception).FullName,
                CapturedAtUtc = capturedAtUtc
            };
            var path = Path.Combine(
                stageOutputRoot,
                "controller-failure-" + attempt + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(
                path,
                MigrationContractSerializer.SerializeCanonical(evidence) + "\n",
                new UTF8Encoding(false));
            var info = new FileInfo(path);
            return new ScaleStageExecutionResult
            {
                Outcome = ScaleStageOutcome.FailedUnexpectedly,
                DiagnosticCode = exception?.GetType().Name ?? nameof(Exception),
                Artifacts = new List<ScaleStageArtifact>
                {
                    new ScaleStageArtifact
                    {
                        Kind = ScaleStageArtifactKind.Evidence,
                        RelativePath = ScaleRunStorage.ToRelativeArtifactPath(outputRoot, path),
                        Sha256 = ScaleRunStorage.ComputeFileSha256(path),
                        Length = info.Length,
                        MediaType = "application/json",
                        SchemaVersion = ScaleControllerFailureEvidence.CurrentSchemaVersion
                    }
                },
                Requests = new List<ScaleRequestMetric>()
            };
        }

        private static void ValidateAuthorizationEvidence(
            string outputRoot,
            MigrationActionSignature action,
            ScaleStageExecutionResult result)
        {
            var statuses = result.Requests
                .Where(value => value.HttpStatusCode == 401 || value.HttpStatusCode == 403)
                .Select(value => new { value.Operation, Status = value.HttpStatusCode.Value })
                .ToArray();
            if (statuses.Length == 0)
            {
                throw new InvalidDataException("Only a retained literal HTTP 401/403 response may produce AuthorizationBlocked.");
            }
            var evidenceArtifacts = result.Artifacts
                .Where(value => value.Kind == ScaleStageArtifactKind.HttpAuthorizationEvidence)
                .ToArray();
            if (evidenceArtifacts.Length == 0)
            {
                throw new InvalidDataException("AuthorizationBlocked requires sanitized content-addressed HTTP evidence.");
            }
            foreach (var artifact in evidenceArtifacts)
            {
                var path = ScaleRunStorage.ResolveArtifactPath(outputRoot, artifact.RelativePath);
                var raw = File.ReadAllText(path, Encoding.UTF8).TrimEnd('\r', '\n');
                var evidence = MigrationContractSerializer.Deserialize<ScaleHttpAuthorizationEvidence>(raw);
                if (evidence == null
                    || !string.Equals(evidence.SchemaVersion, ScaleHttpAuthorizationEvidence.CurrentSchemaVersion, StringComparison.Ordinal)
                    || !string.Equals(raw, MigrationContractSerializer.SerializeCanonical(evidence), StringComparison.Ordinal)
                    || !string.Equals(evidence.ActionSignature, action.Signature, StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(evidence.IngredientId)
                    || !string.Equals(evidence.TargetIdentityDigest, action.TargetIdentityDigest, StringComparison.OrdinalIgnoreCase)
                    || evidence.CapturedAtUtc == default(DateTimeOffset)
                    || !statuses.Any(value => value.Status == evidence.HttpStatusCode
                        && string.Equals(value.Operation, evidence.Operation, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException("The retained HTTP authorization evidence does not match the action and request telemetry.");
                }
            }
        }

        private static void ValidateArtifact(string outputRoot, ScaleStageArtifact artifact)
        {
            if (artifact == null
                || !Enum.IsDefined(typeof(ScaleStageArtifactKind), artifact.Kind)
                || !MigrationActionSignature.IsSha256(artifact.Sha256)
                || artifact.Length < 0
                || string.IsNullOrWhiteSpace(artifact.MediaType)
                || string.IsNullOrWhiteSpace(artifact.SchemaVersion))
            {
                throw new InvalidDataException("A scale stage artifact reference is incomplete.");
            }
            var path = ScaleRunStorage.ResolveArtifactPath(outputRoot, artifact.RelativePath);
            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length != artifact.Length
                || !string.Equals(ScaleRunStorage.ComputeFileSha256(path), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A scale stage artifact is missing or differs from its content-addressed reference.");
            }
        }

        private static void ValidateRequests(IEnumerable<ScaleRequestMetric> requests)
        {
            foreach (var request in requests)
            {
                if (request == null
                    || string.IsNullOrWhiteSpace(request.Operation)
                    || request.Operation.Length > 256
                    || request.Operation.Any(character => !(char.IsLetterOrDigit(character)
                        || character == '-'
                        || character == '_'
                        || character == '.'
                        || character == '/'))
                    || request.DurationMilliseconds < 0
                    || request.ResponseBytes < 0
                    || request.RetryAfterWaitMilliseconds < 0)
                {
                    throw new InvalidDataException("Scale request telemetry must contain safe operation names and non-negative measurements, never request URLs.");
                }
            }
        }

        internal static bool IsSafeDiagnosticCode(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= 256
                && value.All(character => char.IsLetterOrDigit(character)
                    || character == '-'
                    || character == '_'
                    || character == '.'
                    || character == '/');
        }

    }
}
