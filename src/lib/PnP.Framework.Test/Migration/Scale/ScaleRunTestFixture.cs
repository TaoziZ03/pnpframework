using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Scale;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Test.Migration.Scale
{
    internal static class ScaleRunTestFixture
    {
        public static ScaleRunManifest Manifest(int pageCount)
        {
            return ScaleRunManifestValidator.Seal(new ScaleRunManifest
            {
                LoopId = "loop-001",
                RunKey = "campaign-enterprise-wiki",
                MutationMode = ScaleRunMutationMode.Simulation,
                Policy = new ScaleRunPolicy
                {
                    QueueCapacity = Math.Max(2, pageCount),
                    MaximumAttemptsPerStage = 3,
                    RetryBaseDelayMilliseconds = 1,
                    MaximumUnverifiedTargets = 2
                },
                Pages = Enumerable.Range(0, pageCount).Select(index => new ScaleRunPage
                {
                    PageKey = "page-" + index.ToString("D3"),
                    Ordinal = index,
                    PageFamily = "enterprise-wiki",
                    SourceReferenceKey = "source/page-" + index.ToString("D3"),
                    TargetReferenceKey = "target/page-" + index.ToString("D3"),
                    SupportCohortSignature = MigrationDigest.ComputeSha256("support/default"),
                    ExecutionCohortSignature = MigrationDigest.ComputeSha256("execution/normal"),
                    LoadBucket = "normal"
                }).ToList()
            });
        }

        public static List<FakeStageExecutor> Executors()
        {
            return ScaleRunManifestValidator.Stages.Select(value => new FakeStageExecutor(value)).ToList();
        }

        public static void SetConcurrency(ScaleRunManifest manifest, ScaleRunStage stage, int maximum)
        {
            manifest.Policy.StageConcurrency.Single(value => value.Stage == stage).Maximum = maximum;
            ScaleRunManifestValidator.Seal(manifest);
        }

        public static ScaleStageExecutionJournalRecord StartRecord(
            Guid operationId,
            MigrationActionSignature action)
        {
            return new ScaleStageExecutionJournalRecord
            {
                RecordKind = ScaleStageExecutionJournalRecordKind.AttemptStarted,
                RecordedAtUtc = DateTimeOffset.Parse("2026-09-05T00:00:00Z"),
                OperationId = operationId,
                ManifestDigest = MigrationDigest.ComputeSha256("manifest"),
                PageKey = "page-000",
                Stage = ScaleRunStage.Collect,
                Attempt = 1,
                ActionId = action.ActionId,
                ActionSignature = action.Signature,
                DiagnosticCode = "started"
            };
        }

        public static ScaleStageExecutionJournalRecord CompletedRecord(
            Guid operationId,
            MigrationActionSignature action)
        {
            var artifact = new ScaleStageArtifact
            {
                Kind = ScaleStageArtifactKind.Output,
                RelativePath = "items/evidence.json",
                Sha256 = MigrationDigest.ComputeSha256("artifact"),
                Length = 8,
                MediaType = "application/json",
                SchemaVersion = "test/v1"
            };
            return new ScaleStageExecutionJournalRecord
            {
                RecordKind = ScaleStageExecutionJournalRecordKind.AttemptCompleted,
                RecordedAtUtc = DateTimeOffset.Parse("2026-09-05T00:00:01Z"),
                OperationId = operationId,
                ManifestDigest = MigrationDigest.ComputeSha256("manifest"),
                PageKey = "page-000",
                Stage = ScaleRunStage.Collect,
                Attempt = 1,
                ActionId = action.ActionId,
                ActionSignature = action.Signature,
                Outcome = ScaleStageOutcome.Succeeded,
                Verified = true,
                ProvenanceMatched = true,
                ObservedStateDigest = action.SemanticDigest,
                TargetIdentityDigest = action.TargetIdentityDigest,
                ArtifactSetDigest = ScaleRunStorage.ComputeArtifactReferenceSetDigest(new[] { artifact }),
                Artifacts = new List<ScaleStageArtifact> { artifact },
                DiagnosticCode = "complete"
            };
        }
    }

    internal sealed class FakeStageExecutor : IScaleRunStageExecutor
    {
        private readonly ConcurrentDictionary<string, int> executes =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> probes =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly ISet<string> targets = new HashSet<string>(StringComparer.Ordinal);
        private readonly object gate = new object();

        public FakeStageExecutor(ScaleRunStage stage)
        {
            Stage = stage;
            ContractDigest = MigrationDigest.ComputeSha256("fake-scale-stage/" + stage);
            MutatesTarget = stage == ScaleRunStage.Repro;
            ResumePolicy = stage == ScaleRunStage.Repro
                ? ScaleStageResumePolicy.FreshProbe
                : stage == ScaleRunStage.TargetRecapture
                    ? ScaleStageResumePolicy.AlwaysExecute
                    : ScaleStageResumePolicy.ArtifactCheckpoint;
        }

        public ScaleRunStage Stage { get; }
        public string ContractDigest { get; }
        public bool MutatesTarget { get; }
        public bool AllowsLiveMutation => AllowLiveMutation;
        public ScaleStageResumePolicy ResumePolicy { get; }
        public int DelayMilliseconds { get; set; }
        public bool RetryFirstAttempt { get; set; }
        public bool ResponseLossOnFirstRepro { get; set; }
        public bool ReturnInvalidArtifact { get; set; }
        public bool AllowLiveMutation { get; set; }
        public int? AuthorizationStatusCode { get; set; }
        public string AuthorizationLimitedPageKey { get; set; }
        public string AuthorizationLimitedIngredientId { get; set; }
        public string AuthorizationLimitedDependentIngredientId { get; set; }
        public string AuthorizationLimitedDependentCauseIngredientId { get; set; }
        public int AuthorizationLimitedStatusCode { get; set; } = 403;
        public ScaleStageProbeState? ProbeStateOverride { get; set; }
        public Action<ScaleRunStageContext> OnExecute { get; set; }

        public int ExecuteCount(string pageKey) => executes.TryGetValue(pageKey, out var value) ? value : 0;
        public int ProbeCount(string pageKey) => probes.TryGetValue(pageKey, out var value) ? value : 0;

        public void MarkTarget(string pageKey)
        {
            lock (gate)
            {
                targets.Add(pageKey);
            }
        }

        public Task<ScaleStageProbeResult> ProbeAsync(
            ScaleRunStageContext context,
            CancellationToken cancellationToken)
        {
            probes.AddOrUpdate(context.Page.PageKey, 1, (_, current) => current + 1);
            bool exact;
            lock (gate)
            {
                exact = targets.Contains(context.Page.PageKey);
            }
            var state = ProbeStateOverride
                ?? (exact ? ScaleStageProbeState.Exact : ScaleStageProbeState.Absent);
            var artifacts = state != ScaleStageProbeState.Absent
                ? new List<ScaleStageArtifact>
                {
                    WriteArtifact(
                        context,
                        "fresh-probe.json",
                        "probe|" + context.Action.Signature,
                        ScaleStageArtifactKind.Evidence,
                        "fake-probe/v1")
                }
                : new List<ScaleStageArtifact>();
            return Task.FromResult(new ScaleStageProbeResult
            {
                State = state,
                FreshProbePerformed = true,
                ProvenanceMatched = state == ScaleStageProbeState.Exact,
                ObservedStateDigest = state == ScaleStageProbeState.Exact
                    ? context.Action.SemanticDigest
                    : state == ScaleStageProbeState.Drifted ? MigrationDigest.ComputeSha256("drift") : null,
                TargetIdentityDigest = state == ScaleStageProbeState.Exact
                    ? context.Action.TargetIdentityDigest
                    : state == ScaleStageProbeState.Drifted ? context.Action.TargetIdentityDigest : null,
                DiagnosticCode = state.ToString(),
                Artifacts = artifacts,
                Requests = new List<ScaleRequestMetric>
                {
                    new ScaleRequestMetric
                    {
                        Operation = "repro.probe",
                        DurationMilliseconds = 1,
                        HttpStatusCode = 200
                    }
                }
            });
        }

        public async Task<ScaleStageExecutionResult> ExecuteAsync(
            ScaleRunStageContext context,
            CancellationToken cancellationToken)
        {
            OnExecute?.Invoke(context);
            var count = executes.AddOrUpdate(context.Page.PageKey, 1, (_, current) => current + 1);
            if (DelayMilliseconds > 0)
            {
                await Task.Delay(DelayMilliseconds, cancellationToken);
            }
            if (ReturnInvalidArtifact)
            {
                return Success(context, new ScaleStageArtifact
                {
                    RelativePath = "missing.json",
                    Sha256 = MigrationDigest.ComputeSha256("missing"),
                    Length = 7,
                    MediaType = "application/json",
                    SchemaVersion = "missing/v1"
                });
            }
            if (AuthorizationStatusCode.HasValue)
            {
                return AuthorizationFailure(context, AuthorizationStatusCode.Value);
            }
            if (string.Equals(AuthorizationLimitedPageKey, context.Page.PageKey, StringComparison.Ordinal))
            {
                return AuthorizationLimitedSuccess(context);
            }
            if (RetryFirstAttempt && count == 1)
            {
                return Retry(context, false);
            }
            if (ResponseLossOnFirstRepro && Stage == ScaleRunStage.Repro && count == 1)
            {
                MarkTarget(context.Page.PageKey);
                return Retry(context, true);
            }

            var artifact = WriteArtifact(
                context,
                "artifact.json",
                context.Page.PageKey + "|" + Stage + "|" + context.Action.Signature,
                ScaleStageArtifactKind.Output,
                "fake-stage-artifact/v1");
            if (Stage == ScaleRunStage.Repro)
            {
                MarkTarget(context.Page.PageKey);
            }
            return Success(context, artifact);
        }

        private ScaleStageExecutionResult AuthorizationFailure(
            ScaleRunStageContext context,
            int status)
        {
            var operation = Stage.ToString().ToLowerInvariant() + ".request";
            var evidence = new ScaleHttpAuthorizationEvidence
            {
                ActionSignature = context.Action.Signature,
                TargetIdentityDigest = context.Action.TargetIdentityDigest,
                Operation = operation,
                HttpStatusCode = status,
                CapturedAtUtc = DateTimeOffset.Parse("2026-09-05T00:00:00Z")
            };
            return new ScaleStageExecutionResult
            {
                Outcome = ScaleStageOutcome.AuthorizationBlocked,
                DiagnosticCode = "LiteralHttp" + status,
                Artifacts = new List<ScaleStageArtifact>
                {
                    WriteArtifact(
                        context,
                        "http-authorization.json",
                        ScaleRunContractSerializer.SerializeCanonical(evidence),
                        ScaleStageArtifactKind.HttpAuthorizationEvidence,
                        ScaleHttpAuthorizationEvidence.CurrentSchemaVersion)
                },
                Requests = new List<ScaleRequestMetric>
                {
                    new ScaleRequestMetric
                    {
                        Operation = operation,
                        DurationMilliseconds = 3,
                        HttpStatusCode = status
                    }
                }
            };
        }

        private ScaleStageExecutionResult AuthorizationLimitedSuccess(ScaleRunStageContext context)
        {
            var ingredientId = AuthorizationLimitedIngredientId ?? "ingredient.protected-payload";
            var dependentId = AuthorizationLimitedDependentIngredientId ?? "ingredient.protected-payload-consumer";
            var operation = Stage.ToString().ToLowerInvariant() + ".ingredient-request";
            var evidence = new ScaleHttpAuthorizationEvidence
            {
                ActionSignature = context.Action.Signature,
                IngredientId = ingredientId,
                TargetIdentityDigest = context.Action.TargetIdentityDigest,
                Operation = operation,
                HttpStatusCode = AuthorizationLimitedStatusCode,
                CapturedAtUtc = DateTimeOffset.Parse("2026-09-05T00:00:00Z")
            };
            var output = WriteArtifact(
                context,
                "artifact.json",
                context.Page.PageKey + "|" + Stage + "|" + context.Action.Signature,
                ScaleStageArtifactKind.Output,
                "fake-stage-artifact/v1");
            var authorization = WriteArtifact(
                context,
                "ingredient-http-authorization.json",
                ScaleRunContractSerializer.SerializeCanonical(evidence),
                ScaleStageArtifactKind.HttpAuthorizationEvidence,
                ScaleHttpAuthorizationEvidence.CurrentSchemaVersion);
            return new ScaleStageExecutionResult
            {
                Outcome = ScaleStageOutcome.Succeeded,
                Verified = true,
                ProvenanceMatched = true,
                ObservedStateDigest = context.Action.SemanticDigest,
                TargetIdentityDigest = context.Action.TargetIdentityDigest,
                DiagnosticCode = "VerifiedWithAuthorizationLimitedIngredient",
                Artifacts = new List<ScaleStageArtifact> { output, authorization },
                Requests = new List<ScaleRequestMetric>
                {
                    new ScaleRequestMetric
                    {
                        Operation = operation,
                        DurationMilliseconds = 3,
                        HttpStatusCode = AuthorizationLimitedStatusCode
                    }
                },
                Ingredients = new List<ScaleIngredientRunResult>
                {
                    new ScaleIngredientRunResult
                    {
                        IngredientId = ingredientId,
                        Outcome = ScaleIngredientOutcome.AuthorizationBlocked,
                        AuthorizationEvidenceArtifactSha256 = authorization.Sha256,
                        DiagnosticCode = "LiteralHttp" + AuthorizationLimitedStatusCode
                    },
                    new ScaleIngredientRunResult
                    {
                        IngredientId = dependentId,
                        Outcome = ScaleIngredientOutcome.SkippedByDependency,
                        DependencyIngredientIds = new List<string>
                        {
                            AuthorizationLimitedDependentCauseIngredientId ?? ingredientId
                        },
                        DiagnosticCode = "HardDependencyAuthorizationBlocked"
                    }
                }
            };
        }

        private ScaleStageExecutionResult Retry(
            ScaleRunStageContext context,
            bool mutationAttempted)
        {
            return new ScaleStageExecutionResult
            {
                Outcome = ScaleStageOutcome.RetryableTransient,
                MutationAttempted = mutationAttempted,
                DiagnosticCode = mutationAttempted ? "ResponseLost" : "Throttled",
                RetryAfter = TimeSpan.FromMilliseconds(25),
                Artifacts = new List<ScaleStageArtifact>
                {
                    WriteArtifact(
                        context,
                        "retry-evidence.json",
                        "retry",
                        ScaleStageArtifactKind.Evidence,
                        "fake-retry/v1")
                },
                Requests = new List<ScaleRequestMetric>
                {
                    new ScaleRequestMetric
                    {
                        Operation = Stage.ToString().ToLowerInvariant() + ".request",
                        DurationMilliseconds = 5,
                        HttpStatusCode = mutationAttempted ? null : 429
                    }
                }
            };
        }

        private ScaleStageExecutionResult Success(
            ScaleRunStageContext context,
            ScaleStageArtifact artifact)
        {
            return new ScaleStageExecutionResult
            {
                Outcome = ScaleStageOutcome.Succeeded,
                Verified = true,
                MutationAttempted = Stage == ScaleRunStage.Repro && AllowLiveMutation,
                ProvenanceMatched = true,
                ObservedStateDigest = context.Action.SemanticDigest,
                TargetIdentityDigest = context.Action.TargetIdentityDigest,
                DiagnosticCode = "Verified",
                Artifacts = new List<ScaleStageArtifact> { artifact },
                Requests = new List<ScaleRequestMetric>
                {
                    new ScaleRequestMetric
                    {
                        Operation = Stage.ToString().ToLowerInvariant() + ".request",
                        DurationMilliseconds = 4,
                        HttpStatusCode = 200,
                        ResponseBytes = artifact.Length
                    }
                }
            };
        }

        private static ScaleStageArtifact WriteArtifact(
            ScaleRunStageContext context,
            string name,
            string content,
            ScaleStageArtifactKind kind,
            string schema)
        {
            Directory.CreateDirectory(context.StageOutputRoot);
            var path = Path.Combine(context.StageOutputRoot, name);
            File.WriteAllText(path, content);
            return new ScaleStageArtifact
            {
                Kind = kind,
                RelativePath = Path.GetRelativePath(context.OutputRoot, path).Replace('\\', '/'),
                Sha256 = ScaleRunStorage.ComputeFileSha256(path),
                Length = new FileInfo(path).Length,
                MediaType = "application/json",
                SchemaVersion = schema
            };
        }
    }

    internal sealed class RecordingClock : IScaleRunClock
    {
        public IList<TimeSpan> Delays { get; } = new List<TimeSpan>();

        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-05T00:00:00Z");

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }
}
