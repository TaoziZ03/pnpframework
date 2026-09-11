using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal static partial class IngredientMaturityEvidenceValidator
    {
        internal const string ValidatorId = "pnp-ingredient-maturity-common-validator";
        internal const string ValidatorVersion = "v3";

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM0(
            IngredientMaturityEvaluationContext context,
            PageIngredientNode node,
            PublishingPageCaptureBundle snapshot,
            PublishingPageIngredientPrimaryOwnerRegistry ownerRegistry,
            IEnumerable<string> evidenceReferences,
            IngredientRuntimeAssertionEvidence runtimeAssertion = null)
        {
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.CanonicalIdentity, evidenceReferences, () =>
                {
                    IngredientMaturityEvaluator.ValidateContext(context);
                    Require(IngredientMaturityEvaluator.IsSha256(context.Identity.ClaimId), "The claim ID is not a canonical SHA-256 identity.");
                    Require(!string.IsNullOrWhiteSpace(context.Identity.IngredientId)
                        && !string.IsNullOrWhiteSpace(context.Identity.Subtype)
                        && !string.IsNullOrWhiteSpace(context.Identity.SemanticRole)
                        && !string.IsNullOrWhiteSpace(context.Identity.SourcePredicateId),
                        "The ingredient tuple is incomplete.");
                    var runtimeWork = string.Equals(context.Identity.WorkItemType, IngredientMaturityContract.RuntimeVerificationWorkItem, StringComparison.Ordinal);
                    if (runtimeWork)
                    {
                        Require(node == null
                            && !context.Identity.Kind.HasValue
                            && string.Equals(context.Identity.Lane, "behavior.interaction", StringComparison.Ordinal),
                            "A runtime-verification assertion cannot claim a persisted PageIngredientKind node.");
                        RequireRuntimeAssertion(context, runtimeAssertion);
                        return;
                    }
                    Require(node != null
                        && context.Identity.Kind == node.Kind
                        && string.Equals(context.Identity.IngredientId, node.Id, StringComparison.Ordinal)
                        && string.Equals(context.Identity.Subtype, node.Subtype, StringComparison.Ordinal)
                        && string.Equals(context.Identity.SemanticRole, node.SemanticRole, StringComparison.Ordinal)
                        && string.Equals(context.Identity.SourcePredicateId, node.SourcePredicateId, StringComparison.Ordinal),
                        "The claimed canonical identity does not match the projected ingredient node.");
                }),
                Validate(IngredientMaturityGateCatalog.PrimaryOwner, evidenceReferences, () =>
                {
                    Require(context?.Identity != null, "The canonical identity is missing.");
                    if (string.Equals(context.Identity.WorkItemType, IngredientMaturityContract.RuntimeVerificationWorkItem, StringComparison.Ordinal))
                    {
                        Require(node == null && !context.Identity.Kind.HasValue
                            && string.Equals(context.Identity.Lane, "behavior.interaction", StringComparison.Ordinal),
                            "Only behavior.interaction owns runtime-verification assertions.");
                        return;
                    }
                    Require(ownerRegistry != null, "The primary-owner registry is missing.");
                    var owner = ownerRegistry.Resolve(snapshot, node);
                    Require(string.Equals(owner.PrimaryOwnerLane, context.Identity.Lane, StringComparison.Ordinal)
                        && string.Equals(node.PrimaryOwnerLane, context.Identity.Lane, StringComparison.Ordinal),
                        "The canonical owner registry does not resolve exactly one matching primary owner.");
                }),
                Validate(IngredientMaturityGateCatalog.SourceBinding, evidenceReferences, () =>
                {
                    Require(context?.Source != null
                        && !string.IsNullOrWhiteSpace(context.Source.PageOrListItemIdentity)
                        && !string.IsNullOrWhiteSpace(context.Source.SourceVersion)
                        && IngredientMaturityEvaluator.IsSha256(context.Source.SourceArtifactDigest)
                        && IngredientMaturityEvaluator.IsSha256(context.Source.SourceSnapshotDigest),
                        "The version-bound source evidence is incomplete.");
                    var boundNode = node;
                    if (string.Equals(context.Identity?.WorkItemType, IngredientMaturityContract.RuntimeVerificationWorkItem, StringComparison.Ordinal))
                    {
                        RequireRuntimeAssertion(context, runtimeAssertion);
                        boundNode = runtimeAssertion.CanonicalIngredient;
                    }
                    Require(boundNode != null
                        && string.Equals(boundNode.SourcePageOrListItemIdentity, context.Source.PageOrListItemIdentity, StringComparison.Ordinal)
                        && string.Equals(boundNode.SourceVersionIdentity, context.Source.SourceVersion, StringComparison.Ordinal)
                        && string.Equals(boundNode.EvidenceDigest, context.Source.SourceArtifactDigest, StringComparison.OrdinalIgnoreCase),
                        "The source binding differs from the canonical ingredient evidence.");
                })
            };
        }

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM1(IngredientLiveEvidence evidence)
        {
            // Preserve source compatibility, but unbound v1 evidence cannot grant M1.
            return ValidateM1(null, evidence);
        }

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM1(
            IngredientMaturityEvaluationContext context,
            IngredientLiveEvidence evidence)
        {
            var observations = evidence?.Observations ?? new List<IngredientValueObservation>();
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.AuthenticatedSourceCollect, evidence?.SourceEvidenceReferences, () =>
                {
                    RequireLiveContext(context, evidence);
                    Require(evidence != null && evidence.SourceAuthenticated && !evidence.HistoricalOrSyntheticSubstitution,
                        "Authenticated source collection is absent or substituted.");
                    Require(observations.Any(value => value?.Origin == IngredientObservationOrigin.AuthenticatedSource),
                        "No authenticated source observation is present.");
                    foreach (var observation in observations.Where(value => value?.Origin == IngredientObservationOrigin.AuthenticatedSource))
                    {
                        RequireObservation(context, evidence, observation);
                    }
                }),
                Validate(IngredientMaturityGateCatalog.CupCollectFreshReadback, evidence?.TargetEvidenceReferences, () =>
                {
                    RequireLiveContext(context, evidence);
                    Require(evidence != null && evidence.TargetFreshReadback && !evidence.HistoricalOrSyntheticSubstitution,
                        "Fresh CUPCollect readback is absent or substituted.");
                    Require(observations.Any(value => value?.Origin == IngredientObservationOrigin.CupCollectFreshReadback),
                        "No fresh target observation is present.");
                    foreach (var observation in observations.Where(value => value?.Origin == IngredientObservationOrigin.CupCollectFreshReadback))
                    {
                        RequireObservation(context, evidence, observation);
                    }
                }),
                Validate(IngredientMaturityGateCatalog.PerValueObservation,
                    observations.Where(value => value != null).Select(value => value.EvidenceReference), () =>
                {
                    RequireLiveContext(context, evidence);
                    Require(evidence.SourceAuthenticated && evidence.TargetFreshReadback
                        && !evidence.HistoricalOrSyntheticSubstitution && observations.Count > 0,
                        "Per-value evidence is absent or substituted.");
                    foreach (var observation in observations)
                    {
                        RequireObservation(context, evidence, observation);
                    }
                    foreach (var pair in observations.GroupBy(value => value.ValuePath, StringComparer.Ordinal))
                    {
                        Require(pair.Count() == 2
                            && pair.Count(value => value.Origin == IngredientObservationOrigin.AuthenticatedSource) == 1
                            && pair.Count(value => value.Origin == IngredientObservationOrigin.CupCollectFreshReadback) == 1,
                            "Every canonical value path requires exactly one source and one target observation.");
                    }
                })
            };
        }

        private static void RequireRuntimeAssertion(
            IngredientMaturityEvaluationContext context,
            IngredientRuntimeAssertionEvidence evidence)
        {
            Require(context?.Identity != null && !string.IsNullOrWhiteSpace(context.Identity.SourcePredicateId)
                && evidence?.CanonicalIngredient != null && evidence.Action != null
                && string.Equals(evidence.SourcePredicateId, context.Identity.SourcePredicateId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(evidence.CanonicalIngredient.Id)
                && !string.IsNullOrWhiteSpace(evidence.Action.ActionId)
                && string.Equals(evidence.Action.IngredientId, evidence.CanonicalIngredient.Id, StringComparison.Ordinal)
                && evidence.Action.VerificationAssertions != null
                && evidence.Action.VerificationAssertions.Count(value =>
                    string.Equals(value, context.Identity.IngredientId, StringComparison.Ordinal)) == 1,
                "The runtime predicate/assertion is not bound to one canonical ingredient and action.");
        }

        private static void RequireLiveContext(
            IngredientMaturityEvaluationContext context,
            IngredientLiveEvidence evidence)
        {
            IngredientMaturityEvaluator.ValidateContext(context);
            Require(evidence != null
                && context.ObservationWindowStartUtc.HasValue
                && context.ObservationWindowEndUtc.HasValue
                && context.ObservationWindowStartUtc.Value != default
                && context.ObservationWindowEndUtc.Value != default
                && context.ObservationWindowStartUtc.Value.Offset == TimeSpan.Zero
                && context.ObservationWindowEndUtc.Value.Offset == TimeSpan.Zero
                && context.ObservationWindowStartUtc <= context.ObservationWindowEndUtc
                && evidence.ReadbackStartedAtUtc != default
                && evidence.ReadbackStartedAtUtc.Offset == TimeSpan.Zero
                && evidence.ReadbackStartedAtUtc >= context.ObservationWindowStartUtc
                && evidence.ReadbackStartedAtUtc <= context.ObservationWindowEndUtc,
                "Live observations require the consumer's UTC time fence and a bound target readback start.");
        }

        private static void RequireObservation(
            IngredientMaturityEvaluationContext context,
            IngredientLiveEvidence evidence,
            IngredientValueObservation observation)
        {
            Require(observation != null
                && string.Equals(observation.ClaimId, context.Identity.ClaimId, StringComparison.Ordinal)
                && string.Equals(observation.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                && observation.Source != null && observation.Target != null
                && string.Equals(MigrationContractSerializer.SerializeCanonical(observation.Source),
                    MigrationContractSerializer.SerializeCanonical(context.Source), StringComparison.Ordinal)
                && string.Equals(MigrationContractSerializer.SerializeCanonical(observation.Target),
                    MigrationContractSerializer.SerializeCanonical(context.Target), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(observation.ValuePath)
                && IngredientMaturityEvaluator.IsSha256(observation.ValueDigest)
                && !string.IsNullOrWhiteSpace(observation.EvidenceReference)
                && observation.ObservedAtUtc.Offset == TimeSpan.Zero
                && observation.ObservedAtUtc >= context.ObservationWindowStartUtc
                && observation.ObservedAtUtc <= context.ObservationWindowEndUtc,
                "The value observation is incomplete, stale, or bound to a different claim/source/target.");
            if (observation.Origin == IngredientObservationOrigin.AuthenticatedSource)
            {
                Require(observation.ObservedAtUtc <= evidence.ReadbackStartedAtUtc
                    && evidence.SourceEvidenceReferences != null
                    && evidence.SourceEvidenceReferences.Contains(observation.EvidenceReference, StringComparer.Ordinal),
                    "The source observation is not bound to collection before target readback.");
            }
            else
            {
                Require(observation.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                    && observation.ObservedAtUtc >= evidence.ReadbackStartedAtUtc
                    && evidence.TargetEvidenceReferences != null
                    && evidence.TargetEvidenceReferences.Contains(observation.EvidenceReference, StringComparer.Ordinal),
                    "The target observation is not fresh readback or uses unsupported/historical/synthetic evidence.");
            }
        }

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM2(IngredientContentIntegrityEvidence evidence)
        {
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.RawArtifactIntegrity, evidence?.EvidenceReferences, () =>
                {
                    Require(evidence != null && (!string.IsNullOrWhiteSpace(evidence.ContentBase64) || evidence.ArtifactStore != null),
                        "Artifact bytes or an artifact store are required for independent recomputation.");
                    MigrationArtifactContractValidator.Validate(
                        evidence.Artifact,
                        evidence.ContentBase64,
                        evidence.ArtifactStore,
                        "ingredient maturity raw");
                }),
                Validate(IngredientMaturityGateCatalog.SemanticIntegrity, evidence?.EvidenceReferences, () =>
                {
                    Require(evidence != null
                        && !string.IsNullOrWhiteSpace(evidence.SemanticValueCanonicalJson)
                        && IngredientMaturityEvaluator.IsSha256(evidence.SemanticDigest),
                        "The semantic value or digest is missing.");
                    using (var document = JsonDocument.Parse(evidence.SemanticValueCanonicalJson))
                    {
                        var canonical = MigrationContractSerializer.SerializeCanonical(document.RootElement);
                        Require(string.Equals(MigrationDigest.ComputeSha256(canonical), evidence.SemanticDigest, StringComparison.OrdinalIgnoreCase),
                            "The normalized semantic digest does not recompute.");
                        Require(string.Equals(canonical, evidence.SemanticValueCanonicalJson, StringComparison.Ordinal),
                            "The semantic value is not canonical JSON.");
                    }
                })
            };
        }

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM3(IngredientPlanEvidence evidence)
        {
            if (evidence?.External != null)
            {
                // External evidence cannot grant maturity without independent pins.
                return ValidateM3(null, evidence);
            }
            PageIngredientPlanEvaluation evaluation = null;
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.SnapshotPlanBinding, evidence?.SnapshotPlanEvidenceReferences, () =>
                {
                    Require(evidence?.Plan != null
                        && IngredientMaturityEvaluator.IsSha256(evidence.ExpectedSourceSnapshotDigest)
                        && IngredientMaturityEvaluator.IsSha256(evidence.ExpectedPlanDigest),
                        "The typed plan or sealed digest binding is missing.");
                    Require(string.Equals(evidence.Plan.SourceSnapshotDigest, evidence.ExpectedSourceSnapshotDigest, StringComparison.OrdinalIgnoreCase),
                        "The plan does not bind the expected source snapshot digest.");
                    Require(string.Equals(PublishingPageDigest.ComputePlanDigest(evidence.Plan), evidence.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase),
                        "The plan digest does not recompute.");
                }),
                Validate(IngredientMaturityGateCatalog.ActionDisposition, evidence?.ActionEvidenceReferences, () =>
                {
                    evaluation = PageIngredientPlanEvaluator.Evaluate(evidence?.Plan?.IngredientGraph, evidence?.Plan?.IngredientActions);
                    Require(evaluation.Outcome != PageMigrationOutcome.Invalid
                        && !evaluation.Issues.Any(value => value.Severity == MigrationIssueSeverity.Error),
                        "The ingredient plan is invalid.");
                    var node = evidence.Plan.IngredientGraph.Nodes.SingleOrDefault(value => value?.Id == evidence.IngredientId);
                    Require(node != null, "The assessed ingredient is absent from the sealed graph.");
                    var actions = evidence.Plan.IngredientActions.Where(value => value?.IngredientId == evidence.IngredientId).ToArray();
                    Require(!node.HasContent || actions.Length == 1,
                        "Every nonempty ingredient must have exactly one legal disposition.");
                }),
                Validate(IngredientMaturityGateCatalog.DependencyPolicy, evidence?.DependencyPolicyEvidenceReferences, () =>
                {
                    evaluation = evaluation ?? PageIngredientPlanEvaluator.Evaluate(evidence?.Plan?.IngredientGraph, evidence?.Plan?.IngredientActions);
                    Require(!evaluation.Issues.Any(value => value.Severity == MigrationIssueSeverity.Error
                        && (value.Code?.IndexOf("Dependency", StringComparison.Ordinal) >= 0
                            || value.Code?.IndexOf("Edge", StringComparison.Ordinal) >= 0
                            || value.Code?.IndexOf("ExternalIngredient", StringComparison.Ordinal) >= 0)),
                        "The dependency closure or release policy is invalid.");
                })
            };
        }

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM4(IngredientOperationalEvidence evidence)
        {
            if (evidence?.External != null)
            {
                return ValidateM4(null, evidence);
            }
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.AdmittedExactPlan, evidence?.PlanAdmissionEvidenceReferences, () =>
                {
                    Require(evidence?.Plan != null && evidence.AdmissionPassed
                        && IngredientMaturityEvaluator.IsSha256(evidence.AdmittedPlanDigest)
                        && string.Equals(PublishingPageDigest.ComputePlanDigest(evidence.Plan), evidence.AdmittedPlanDigest, StringComparison.OrdinalIgnoreCase),
                        "The exact plan was not admitted with its recomputed digest.");
                }),
                Validate(IngredientMaturityGateCatalog.OperationActionBinding, evidence?.ActionEvidenceReferences, () =>
                {
                    MigrationActionSignature.Validate(evidence?.ActionSignature);
                    var action = evidence.Plan?.IngredientActions?.SingleOrDefault(value => value?.IngredientId == evidence.IngredientId);
                    Require(action != null
                        && string.Equals(action.ActionId, evidence.ActionSignature.ActionId, StringComparison.Ordinal)
                        && string.Equals(action.TargetIdentity, evidence.ActionSignature.TargetIdentity, StringComparison.Ordinal),
                        "The sealed action signature does not bind the assessed action and target.");
                }),
                Validate(IngredientMaturityGateCatalog.MutationJournalVerification, evidence?.ReceiptEvidenceReferences, () =>
                {
                    Require(evidence?.ImportReceipt != null && evidence.JournalState != null && evidence.VerificationReceipt != null,
                        "Mutation, journal, and verification receipts are required.");
                    var operationId = evidence.ImportReceipt.OperationId;
                    var step = evidence.ImportReceipt.Steps?.SingleOrDefault(value => value?.ActionId == evidence.ActionSignature.ActionId);
                    Require(operationId != Guid.Empty
                        && string.Equals(evidence.ImportReceipt.ApprovedPlanDigest, evidence.AdmittedPlanDigest, StringComparison.OrdinalIgnoreCase)
                        && step != null
                        && step.OperationId == operationId
                        && string.Equals(step.PlanDigest, evidence.AdmittedPlanDigest, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(step.ActionSignature, evidence.ActionSignature.Signature, StringComparison.OrdinalIgnoreCase)
                        && step.CompletedAtUtc >= evidence.ExecutionStartedAtUtc
                        && (step.Outcome == MutationOutcome.Applied
                            || step.Outcome == MutationOutcome.AlreadySatisfied
                            || step.Outcome == MutationOutcome.OutcomeUnknownButConverged)
                        && evidence.JournalState.OperationId == operationId
                        && string.Equals(evidence.JournalState.PlanDigest, evidence.AdmittedPlanDigest, StringComparison.OrdinalIgnoreCase)
                        && evidence.JournalState.RecordedAtUtc >= evidence.ExecutionStartedAtUtc
                        && (evidence.JournalState.Status == MigrationExecutionStatus.Succeeded
                            || evidence.JournalState.Status == MigrationExecutionStatus.PartiallySucceeded)
                        && evidence.VerificationReceipt.OperationId == operationId
                        && string.Equals(evidence.VerificationReceipt.PlanDigest, evidence.AdmittedPlanDigest, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(evidence.VerificationReceipt.ActionId, evidence.ActionSignature.ActionId, StringComparison.Ordinal)
                        && string.Equals(evidence.VerificationReceipt.ActionSignature, evidence.ActionSignature.Signature, StringComparison.OrdinalIgnoreCase)
                        && evidence.VerificationReceipt.VerifiedAtUtc >= evidence.ExecutionStartedAtUtc
                        && evidence.ImportReceipt.CompletedAtUtc >= evidence.ExecutionStartedAtUtc,
                        "Operation, plan, action, mutation, journal, or verification receipt bindings do not match.");
                }),
                Validate(IngredientMaturityGateCatalog.OwnershipProvenance, evidence?.ReceiptEvidenceReferences, () =>
                {
                    Require(evidence?.ImportReceipt?.OwnershipMatched == true
                        && evidence.VerificationReceipt?.Ownership == MigrationTargetOwnership.MigrationOwned
                        && evidence.VerificationReceipt.ProvenanceMatched,
                        "Target ownership or provenance did not match the admitted operation.");
                }),
                Validate(IngredientMaturityGateCatalog.FreshStorage, evidence?.FreshReadbackEvidenceReferences, () =>
                {
                    Require(evidence?.ImportReceipt?.FreshReadbackPassed == true
                        && evidence.ImportReceipt.StorageVerificationStatus == StorageVerificationStatus.Passed
                        && evidence.VerificationReceipt?.FreshReadbackPassed == true
                        && (evidence.ImportReceipt.VerifiedIngredientIds?.Contains(evidence.IngredientId, StringComparer.Ordinal) == true),
                        "Fresh storage readback did not verify the assessed ingredient.");
                }),
                Validate(IngredientMaturityGateCatalog.RuntimeCleanupRetry, evidence?.RuntimeCleanupRetryEvidenceReferences, () =>
                {
                    Require(evidence != null
                        && (!evidence.CleanupRequired || evidence.CleanupPassed)
                        && (!evidence.RetryEvidenceRequired || evidence.RetryPassed),
                        "Required cleanup or retry evidence failed.");
                    if (evidence.RuntimeRequired)
                    {
                        Require(evidence.RuntimeReceipt != null
                            && evidence.RuntimeReceipt.Status == RuntimeVerificationStatus.Passed,
                            "Required runtime verification is pending, failed, or absent.");
                        RuntimeVerificationContractValidator.ValidateReceipt(
                            evidence.Plan.RuntimeVerification,
                            evidence.RuntimeReceipt,
                            evidence.AdmittedPlanDigest,
                            evidence.ActionSignature.TargetIdentity,
                            evidence.ExecutionStartedAtUtc);
                    }
                    else
                    {
                        Require(evidence.ImportReceipt.RuntimeVerificationStatus == RuntimeVerificationStatus.NotRequired
                            || evidence.ImportReceipt.RuntimeVerificationStatus == RuntimeVerificationStatus.Passed,
                            "Runtime verification is unexpectedly pending or failed.");
                    }
                })
            };
        }

        public static IReadOnlyList<IngredientMaturityGateReceipt> ValidateM5(
            IngredientProductizationEvidence evidence,
            IngredientMaturityProducerBinding producer,
            string expectedPlanDigest)
        {
            return new[]
            {
                Validate(IngredientMaturityGateCatalog.HermeticUtRedGreen,
                    Refs(evidence?.RedTestEvidenceReference, evidence?.GreenTestEvidenceReference), () =>
                {
                    Require(evidence != null && evidence.HermeticUnitTest
                        && !string.IsNullOrWhiteSpace(evidence.TestId)
                        && !string.IsNullOrWhiteSpace(evidence.RedTestEvidenceReference)
                        && !string.IsNullOrWhiteSpace(evidence.GreenTestEvidenceReference),
                        "Permanent hermetic UT RED to GREEN evidence is missing.");
                }),
                Validate(IngredientMaturityGateCatalog.ExactCodeBuild, Refs(evidence?.BuildEvidenceReference), () =>
                {
                    Require(producer != null
                        && IngredientMaturityEvaluator.IsCommit(evidence?.ImplementationCommit)
                        && string.Equals(evidence.ImplementationCommit, evidence.BuildCommit, StringComparison.Ordinal)
                        && string.Equals(evidence.ImplementationCommit, producer.ImplementationCommit, StringComparison.Ordinal)
                        && IngredientMaturityEvaluator.IsSha256(evidence.BinaryDigest)
                        && string.Equals(evidence.BinaryDigest, producer.BinaryDigest, StringComparison.OrdinalIgnoreCase),
                        "The code, build, producer, or binary identity does not match.");
                }),
                Validate(IngredientMaturityGateCatalog.SameCommitE2E, Refs(evidence?.EndToEndEvidenceReference), () =>
                {
                    Require(string.Equals(evidence?.EndToEndCommit, evidence?.ImplementationCommit, StringComparison.Ordinal)
                        && string.Equals(evidence.EndToEndPlanDigest, expectedPlanDigest, StringComparison.OrdinalIgnoreCase),
                        "CUPCollect E2E evidence is not bound to the exact implementation commit and plan.");
                }),
                Validate(IngredientMaturityGateCatalog.DeterministicCompare, Refs(evidence?.CompareEvidenceReference), () =>
                {
                    Require(evidence?.CompareReport != null
                        && IngredientMaturityEvaluator.IsSha256(evidence.CompareReportDigest)
                        && string.Equals(evidence.CompareReportDigest, PublishingPageCompareReconciler.ComputeReportDigest(evidence.CompareReport), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(evidence.CompareReport.ReportDigestSha256, evidence.CompareReportDigest, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(evidence.CompareReport.Bindings?.PlanDigestSha256, expectedPlanDigest, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(evidence.CompareReport.Acceptance?.Verdict),
                        "The deterministic Compare result or plan binding is invalid.");
                }),
                ValidateReview(IngredientMaturityGateCatalog.ArchitectReview, evidence?.ArchitectReviewVerdict, evidence?.ArchitectReviewEvidenceReference),
                ValidateReview(IngredientMaturityGateCatalog.CtoReview, evidence?.CtoReviewVerdict, evidence?.CtoReviewEvidenceReference),
                ValidateReview(IngredientMaturityGateCatalog.IndependentVerification, evidence?.IndependentVerificationVerdict, evidence?.IndependentVerificationEvidenceReference),
                Validate(IngredientMaturityGateCatalog.PrReady, Refs(evidence?.PrReadyEvidenceReference), () =>
                {
                    Require(string.Equals(evidence?.PrReadyCommit, evidence?.ImplementationCommit, StringComparison.Ordinal)
                        && IngredientMaturityEvaluator.IsCommit(evidence.PrReadyCommit),
                        "The Draft PR or PR-ready commit is missing or points to different code.");
                })
            };
        }

        private static IngredientMaturityGateReceipt ValidateReview(string gateId, string verdict, string evidenceReference)
        {
            return Validate(gateId, Refs(evidenceReference), () =>
            {
                Require(string.Equals(verdict, "APPROVED", StringComparison.Ordinal)
                    || string.Equals(verdict, "PASS", StringComparison.Ordinal)
                    || string.Equals(verdict, "PASSED", StringComparison.Ordinal),
                    $"Required review '{gateId}' did not pass.");
            });
        }

        private static IngredientMaturityGateReceipt Validate(
            string gateId,
            IEnumerable<string> evidenceReferences,
            Action validation)
        {
            var definition = IngredientMaturityGateCatalog.Require(gateId);
            var receipt = new IngredientMaturityGateReceipt
            {
                Level = definition.Level,
                GateId = definition.GateId,
                Category = definition.Category,
                ValidatorId = ValidatorId,
                ValidatorVersion = ValidatorVersion,
                EvidenceReferences = CanonicalRefs(evidenceReferences)
            };
            try
            {
                validation();
                receipt.Passed = true;
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is InvalidDataException
                || exception is InvalidOperationException
                || exception is JsonException)
            {
                receipt.Passed = false;
                receipt.FailureReason = exception.Message;
            }
            return receipt;
        }

        private static IList<string> CanonicalRefs(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }

        private static IEnumerable<string> Refs(params string[] values)
        {
            return values ?? Array.Empty<string>();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
