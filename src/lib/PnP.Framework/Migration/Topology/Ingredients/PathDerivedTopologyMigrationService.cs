using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class PathDerivedTopologyMigrationExecutionResult
    {
        public Guid OperationId { get; set; }

        public SharedTopologyGlobalMaterializationReceipt Receipt { get; set; }

        public IList<MigrationMutationReceipt> Steps { get; set; } = new List<MigrationMutationReceipt>();
    }

    public sealed class PathDerivedTopologyMigrationService
    {
        public SharedTopologyGlobalTargetAnalysis Inspect(ClientContext targetContext, SharedTopologyGlobalActionDag dag)
        {
            return Inspect(new CsomPathDerivedTopologyTargetRuntime(
                targetContext ?? throw new ArgumentNullException(nameof(targetContext))), dag);
        }

        public SharedTopologyGlobalTargetAnalysis Inspect(
            IPathDerivedTopologyTargetRuntime runtime,
            SharedTopologyGlobalActionDag dag)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            SharedTopologyGlobalExecutionValidator.ValidateDag(dag);
            return PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
        }

        public PathDerivedTopologyMigrationExecutionResult Ensure(
            ClientContext targetContext,
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis reviewedAnalysis,
            SharedTopologyGlobalActionPlan approvedActionPlan,
            IEnumerable<SharedTopologyPlan> sourcePlans,
            IMigrationExecutionJournal journal = null)
        {
            return Ensure(
                new CsomPathDerivedTopologyTargetRuntime(targetContext ?? throw new ArgumentNullException(nameof(targetContext))),
                dag,
                reviewedAnalysis,
                approvedActionPlan,
                sourcePlans,
                journal);
        }

        public PathDerivedTopologyMigrationExecutionResult Ensure(
            IPathDerivedTopologyTargetRuntime runtime,
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis reviewedAnalysis,
            SharedTopologyGlobalActionPlan approvedActionPlan,
            IEnumerable<SharedTopologyPlan> sourcePlans,
            IMigrationExecutionJournal journal = null)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            SharedTopologyGlobalExecutionValidator.ValidateActionPlan(dag, reviewedAnalysis, approvedActionPlan);
            if (!approvedActionPlan.IsExecutable)
            {
                throw new InvalidOperationException("The reviewed shared-topology action plan contains blocked actions.");
            }
            var plans = ValidateSourcePlans(sourcePlans, dag);
            var operationId = Guid.NewGuid();
            var recorder = new MigrationExecutionRecorder(operationId, approvedActionPlan.ActionPlanDigest, journal);
            recorder.RecordState(MigrationExecutionStatus.Running, "Shared path-derived topology materialization started.");
            try
            {
                var receipt = PathDerivedTopologyMaterializer.Ensure(
                    runtime,
                    plans,
                    dag,
                    reviewedAnalysis,
                    approvedActionPlan,
                    operationId,
                    recorder);
                recorder.RecordState(MigrationExecutionStatus.Succeeded, "Every global topology action passed a signed fresh-readback checkpoint.");
                return new PathDerivedTopologyMigrationExecutionResult
                {
                    OperationId = operationId,
                    Receipt = receipt,
                    Steps = new List<MigrationMutationReceipt>(recorder.Steps)
                };
            }
            catch
            {
                recorder.RecordState(MigrationExecutionStatus.FailedUnexpectedly, "Shared path-derived topology materialization failed.");
                throw;
            }
        }

        private static SharedTopologyPlan[] ValidateSourcePlans(
            IEnumerable<SharedTopologyPlan> sourcePlans,
            SharedTopologyGlobalActionDag dag)
        {
            var plans = (sourcePlans ?? Enumerable.Empty<SharedTopologyPlan>()).ToArray();
            if (plans.Length == 0)
            {
                throw new InvalidDataException("The source plans sealed into the global action DAG are required for execution.");
            }
            foreach (var plan in plans)
            {
                SharedTopologyPlanValidator.Validate(plan);
            }
            var compiled = SharedTopologyGlobalActionDagCompiler.Compile(plans);
            if (plans.Select(value => value.PlanDigest).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Length
                || !compiled.IsExecutable
                || !string.Equals(compiled.Dag?.DagDigest, dag.DagDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The supplied distinct source plans do not recompile to the exact global action DAG.");
            }
            return plans;
        }
    }

    internal static class PathDerivedTopologyMaterializer
    {
        public static SharedTopologyGlobalMaterializationReceipt Ensure(
            IPathDerivedTopologyTargetRuntime runtime,
            SharedTopologyPlan[] sourcePlans,
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis reviewedAnalysis,
            SharedTopologyGlobalActionPlan approvedActionPlan,
            Guid operationId,
            MigrationExecutionRecorder recorder)
        {
            SharedTopologyGlobalExecutionValidator.ValidateActionPlan(dag, reviewedAnalysis, approvedActionPlan);
            var startedAt = DateTimeOffset.UtcNow;
            var actionByKey = approvedActionPlan.Actions.ToDictionary(value => value.LogicalActionKey, StringComparer.Ordinal);
            var completed = new Dictionary<string, SharedTopologyGlobalActionReceipt>(StringComparer.Ordinal);

            foreach (var container in dag.Actions
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                .ThenBy(value => value.TargetSlotKey, StringComparer.Ordinal))
            {
                var approved = actionByKey[container.LogicalActionKey];
                Guid? expectedParentWebId = null;
                if (!container.IsTargetSiteRoot)
                {
                    if (!completed.TryGetValue(container.ParentLogicalActionKey, out var parent)
                        || !parent.FreshReadbackPassed)
                    {
                        throw new InvalidOperationException("The signed direct-parent topology checkpoint is unavailable.");
                    }
                    expectedParentWebId = parent.TargetWebId;
                }

                var fresh = InspectExactlyOne(runtime, container, expectedParentWebId);
                EnsureApprovedTransition(approved, container, fresh);
                var mutationAttempted = false;
                var executionOutcome = SharedTopologyActionExecutionOutcome.AlreadySatisfied;
                if (fresh.State == TargetWebContainerState.ReuseExplicitApprovedHost)
                {
                    recorder.RecordAlreadySatisfied(approved.ExecutionGrant, "Fresh target probe verified the exact external host without writing ownership markers.");
                    executionOutcome = SharedTopologyActionExecutionOutcome.ReusedExternal;
                }
                else if (fresh.State == TargetWebContainerState.ReuseOwned)
                {
                    recorder.RecordAlreadySatisfied(approved.ExecutionGrant, "Fresh target probe verified the exact migration-owned Web.");
                }
                else if (fresh.State == TargetWebContainerState.RecoverInterruptedCreate)
                {
                    mutationAttempted = true;
                    var recovery = recorder.Execute(
                        approved.ExecutionGrant,
                        "Recover the exact interrupted target Web.",
                        () => ExecuteRecoveryWithConvergence(runtime, container, expectedParentWebId),
                        value => value.Outcome == SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged
                            ? MutationOutcome.OutcomeUnknownButConverged
                            : MutationOutcome.Applied,
                        value => value.Message);
                    executionOutcome = recovery.Outcome == SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged
                        ? recovery.Outcome
                        : SharedTopologyActionExecutionOutcome.RecoveredInterruptedCreate;
                }
                else if (fresh.State == TargetWebContainerState.CreateMissing)
                {
                    mutationAttempted = true;
                    var attempt = recorder.Execute(
                        approved.ExecutionGrant,
                        "Create the migration-owned target Web.",
                        () => ExecuteCreateWithConvergence(runtime, container, expectedParentWebId),
                        value => value.Outcome == SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged
                            ? MutationOutcome.OutcomeUnknownButConverged
                            : MutationOutcome.Applied,
                        value => value.Message);
                    executionOutcome = attempt.Outcome;
                }
                else
                {
                    throw new InvalidOperationException("Fresh target state is not executable for logical action '" + container.LogicalActionKey + "'.");
                }

                var readback = InspectExactlyOne(runtime, container, expectedParentWebId);
                EnsureFinalState(container, readback);
                var actionReceipt = CreateReceipt(
                    operationId,
                    approvedActionPlan.ActionPlanDigest,
                    container,
                    approved.ExecutionGrant,
                    approved.SelectedAction,
                    readback,
                    mutationAttempted,
                    executionOutcome);
                recorder.RecordVerification(actionReceipt.VerificationCheckpoint);
                completed.Add(container.LogicalActionKey, actionReceipt);
            }

            var receipt = new SharedTopologyGlobalMaterializationReceipt
            {
                OperationId = operationId,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                SourcePlanDigests = dag.SourcePlanDigests.ToList(),
                GlobalActionDagDigest = dag.DagDigest,
                ActionPlanDigest = approvedActionPlan.ActionPlanDigest,
                ExecutionGroupDigests = dag.ExecutionGroupDigests.ToList(),
                SupportCohortDigests = dag.SupportCohortDigests.ToList(),
                Actions = completed.Values.OrderBy(value => value.TargetSlotKey, StringComparer.Ordinal).ToList(),
                SourceWebMappings = CreateSourceWebMappings(sourcePlans, completed),
                SourceFidelityAuthorizationLimited = sourcePlans.SelectMany(value => value.SourceWebFidelityIngredients)
                    .Any(value => value.State == SourceWebFidelityState.AuthorizationBlocked),
                FreshReadbackPassed = completed.Count == approvedActionPlan.Actions.Count
                    && completed.Values.All(value => value.FreshReadbackPassed),
                Diagnostics = new List<string>
                {
                    "Each global target-Web action was freshly probed before execution and sealed by a fresh verification checkpoint.",
                    "The journal is prior-attempt evidence only; every retry still requires a fresh target probe."
                }
            };
            receipt.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeReceipt(receipt);
            SharedTopologyGlobalExecutionValidator.ValidateReceipt(sourcePlans, dag, approvedActionPlan, receipt);
            return receipt;
        }

        private static CreateAttemptResult ExecuteCreateWithConvergence(
            IPathDerivedTopologyTargetRuntime runtime,
            TargetWebContainerIngredientPlan container,
            Guid? expectedParentWebId)
        {
            try
            {
                runtime.Create(container);
                return new CreateAttemptResult
                {
                    Outcome = SharedTopologyActionExecutionOutcome.Applied,
                    Message = "Target Web create returned; exact fresh readback is still required."
                };
            }
            catch (Exception exception)
            {
                var afterFailure = InspectExactlyOne(runtime, container, expectedParentWebId);
                if (afterFailure.State == TargetWebContainerState.RecoverInterruptedCreate)
                {
                    runtime.RecoverOwnership(container);
                    return new CreateAttemptResult
                    {
                        Outcome = SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged,
                        Message = "Create response was lost or failed after mutation; exact interrupted state was recovered and converged. " + exception.Message
                    };
                }
                if (afterFailure.State == TargetWebContainerState.ReuseOwned)
                {
                    return new CreateAttemptResult
                    {
                        Outcome = SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged,
                        Message = "Create response was lost or raced; exact owned state proves convergence. " + exception.Message
                    };
                }
                throw;
            }
        }

        private static CreateAttemptResult ExecuteRecoveryWithConvergence(
            IPathDerivedTopologyTargetRuntime runtime,
            TargetWebContainerIngredientPlan container,
            Guid? expectedParentWebId)
        {
            try
            {
                runtime.RecoverOwnership(container);
                return new CreateAttemptResult
                {
                    Outcome = SharedTopologyActionExecutionOutcome.RecoveredInterruptedCreate,
                    Message = "Interrupted target Web ownership recovery returned; exact fresh readback is still required."
                };
            }
            catch (Exception exception)
            {
                var afterFailure = InspectExactlyOne(runtime, container, expectedParentWebId);
                if (afterFailure.State == TargetWebContainerState.ReuseOwned)
                {
                    return new CreateAttemptResult
                    {
                        Outcome = SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged,
                        Message = "Ownership recovery response was lost; exact owned state proves convergence. " + exception.Message
                    };
                }
                throw;
            }
        }

        private static PathDerivedTargetWebProbe InspectExactlyOne(
            IPathDerivedTopologyTargetRuntime runtime,
            TargetWebContainerIngredientPlan container,
            Guid? expectedParentWebId)
        {
            var observations = runtime.Inspect(new[] { container });
            if (observations == null || observations.Count != 1)
            {
                throw new InvalidDataException("A per-action fresh probe must return exactly one observation.");
            }
            return PathDerivedTopologyTargetAnalyzer.AnalyzeContainer(container, observations[0], expectedParentWebId);
        }

        private static void EnsureApprovedTransition(
            SharedTopologyGlobalAction approved,
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebProbe fresh)
        {
            var allowed = approved.SelectedAction == SharedTopologyActionKind.CreateMissing
                ? fresh.State == TargetWebContainerState.CreateMissing
                    || fresh.State == TargetWebContainerState.RecoverInterruptedCreate
                    || fresh.State == TargetWebContainerState.ReuseOwned
                : approved.SelectedAction == SharedTopologyActionKind.RecoverInterruptedCreate
                    ? fresh.State == TargetWebContainerState.RecoverInterruptedCreate
                        || fresh.State == TargetWebContainerState.ReuseOwned
                    : approved.SelectedAction == SharedTopologyActionKind.ReuseOwned
                        ? fresh.State == TargetWebContainerState.ReuseOwned
                        : approved.SelectedAction == SharedTopologyActionKind.ReuseExplicitApprovedHost
                            && fresh.State == TargetWebContainerState.ReuseExplicitApprovedHost
                            && fresh.TargetWebId == container.ApprovedExistingTargetWebId;
            if (!allowed)
            {
                throw new InvalidOperationException(
                    "Fresh target state '" + fresh.State + "' differs from approved action '"
                    + approved.SelectedAction + "' for slot '" + container.TargetSlotKey + "'. Replan and reapprove.");
            }
        }

        private static void EnsureFinalState(
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebProbe readback)
        {
            var expected = container.ExpectedOwnership == SharedTopologyOwnership.ExternalApprovedHost
                ? TargetWebContainerState.ReuseExplicitApprovedHost
                : TargetWebContainerState.ReuseOwned;
            if (readback.State != expected
                || !readback.TargetSiteId.HasValue
                || !readback.TargetWebId.HasValue
                || (!container.IsTargetSiteRoot && !readback.TargetParentWebId.HasValue)
                || !readback.ObservedConfiguration.HasValue
                || !readback.ObservedLanguage.HasValue
                || !readback.ObservedHasUniqueRoleAssignments.HasValue
                || !string.Equals(readback.ObservedStateDigest, SharedTopologyDigest.ComputeObservedSemanticState(container), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Fresh target readback did not verify the exact generic action signature and ownership boundary.");
            }
        }

        private static SharedTopologyGlobalActionReceipt CreateReceipt(
            Guid operationId,
            string actionPlanDigest,
            TargetWebContainerIngredientPlan container,
            MigrationActionSignature executionGrant,
            SharedTopologyActionKind selectedAction,
            PathDerivedTargetWebProbe readback,
            bool mutationAttempted,
            SharedTopologyActionExecutionOutcome executionOutcome)
        {
            var ownership = readback.Ownership.Value;
            var verification = new MigrationMutationVerificationReceipt
            {
                OperationId = operationId,
                PlanDigest = actionPlanDigest,
                ActionId = executionGrant.ActionId,
                ActionSignature = executionGrant.Signature,
                VerifiedAtUtc = DateTimeOffset.UtcNow,
                FreshReadbackPassed = true,
                ObservedStateDigest = readback.ObservedStateDigest,
                Ownership = ownership == SharedTopologyOwnership.ExternalApprovedHost
                    ? MigrationTargetOwnership.External
                    : MigrationTargetOwnership.MigrationOwned,
                TargetIdentityDigest = executionGrant.TargetIdentityDigest,
                ProvenanceMatched = ownership == SharedTopologyOwnership.ExternalApprovedHost
                    || !string.IsNullOrWhiteSpace(readback.ObservedOriginalIdentifier),
                Message = "Fresh target Web readback matched the generic action signature."
            };
            var receipt = new SharedTopologyGlobalActionReceipt
            {
                TargetSlotKey = container.TargetSlotKey,
                LogicalActionKey = container.LogicalActionKey,
                ExecutionGrantSignature = executionGrant.Signature,
                SelectedAction = selectedAction,
                FinalState = readback.State,
                Ownership = ownership,
                TargetSiteId = readback.TargetSiteId.Value,
                TargetWebId = readback.TargetWebId.Value,
                TargetParentWebId = readback.TargetParentWebId.GetValueOrDefault(),
                TargetWebUrl = container.TargetWebUrl,
                TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                ObservedOriginalIdentifier = readback.ObservedOriginalIdentifier,
                ObservedMappingDigest = readback.ObservedMappingDigest,
                ObservedTitle = readback.ObservedTitle,
                ObservedDescription = readback.ObservedDescription,
                ObservedTemplate = readback.ObservedTemplate,
                ObservedConfiguration = readback.ObservedConfiguration.Value,
                ObservedLanguage = readback.ObservedLanguage.Value,
                ObservedHasUniqueRoleAssignments = readback.ObservedHasUniqueRoleAssignments.Value,
                ObservedStateDigest = readback.ObservedStateDigest,
                MutationAttempted = mutationAttempted,
                ExecutionOutcome = executionOutcome,
                FreshReadbackPassed = true,
                VerificationCheckpoint = verification,
                Diagnostic = executionOutcome == SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged
                    ? "Mutation was attempted and its response was inconclusive; fresh exact state proved convergence."
                    : ownership == SharedTopologyOwnership.ExternalApprovedHost
                        ? "Reused exact external host without writing migration ownership markers."
                        : "Verified exact migration-owned target Web state."
            };
            receipt.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeActionReceipt(receipt);
            return receipt;
        }

        private static IList<SharedTopologySourceWebMaterializationReceipt> CreateSourceWebMappings(
            IEnumerable<SharedTopologyPlan> sourcePlans,
            IReadOnlyDictionary<string, SharedTopologyGlobalActionReceipt> completed)
        {
            var result = new List<SharedTopologySourceWebMaterializationReceipt>();
            foreach (var group in sourcePlans.SelectMany(value => value.SourceWebBindings)
                .GroupBy(value => value.SourceOwnerKey, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var bindings = group.ToArray();
                if (bindings.Select(value => value.TargetLogicalActionKey).Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    throw new InvalidDataException("One source owner is bound to conflicting global topology actions.");
                }
                var binding = bindings[0];
                var target = completed[binding.TargetLogicalActionKey];
                var mapping = new SharedTopologySourceWebMaterializationReceipt
                {
                    SourceOwnerKey = binding.SourceOwnerKey,
                    SourceSiteId = binding.SourceSiteId,
                    SourceWebId = binding.SourceWebId,
                    SourceWebUrl = binding.SourceWebUrl,
                    SourceServerRelativeUrl = binding.SourceServerRelativeUrl,
                    TargetLogicalActionKey = binding.TargetLogicalActionKey,
                    TargetSiteId = target.TargetSiteId,
                    TargetWebId = target.TargetWebId,
                    TargetWebUrl = target.TargetWebUrl,
                    TargetServerRelativeUrl = target.TargetServerRelativeUrl,
                    Ownership = target.Ownership
                };
                mapping.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeSourceMappingReceipt(mapping);
                result.Add(mapping);
            }
            return result;
        }

        private sealed class CreateAttemptResult
        {
            public SharedTopologyActionExecutionOutcome Outcome { get; set; }

            public string Message { get; set; }
        }
    }
}
