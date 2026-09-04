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

    /// <summary>
    /// Executes a reviewed global target-Web action DAG. The reviewed analysis is
    /// evidence for approval, not mutation authority: every action is freshly
    /// inspected immediately before execution and read back immediately afterward.
    /// </summary>
    public sealed class PathDerivedTopologyMigrationService
    {
        public SharedTopologyGlobalTargetAnalysis Inspect(
            ClientContext targetContext,
            SharedTopologyGlobalActionDag dag)
        {
            if (targetContext == null)
            {
                throw new ArgumentNullException(nameof(targetContext));
            }
            return Inspect(new CsomPathDerivedTopologyTargetRuntime(targetContext), dag);
        }

        public SharedTopologyGlobalTargetAnalysis Inspect(
            IPathDerivedTopologyTargetRuntime runtime,
            SharedTopologyGlobalActionDag dag)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (dag == null)
            {
                throw new ArgumentNullException(nameof(dag));
            }
            var observations = runtime.Inspect(dag.Actions);
            return PathDerivedTopologyTargetAnalyzer.Analyze(dag, observations);
        }

        public PathDerivedTopologyMigrationExecutionResult Ensure(
            ClientContext targetContext,
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis reviewedAnalysis,
            SharedTopologyGlobalActionPlan approvedActionPlan,
            IEnumerable<SharedTopologyPlan> sourcePlans,
            IMigrationExecutionJournal journal = null)
        {
            if (targetContext == null)
            {
                throw new ArgumentNullException(nameof(targetContext));
            }
            return Ensure(
                new CsomPathDerivedTopologyTargetRuntime(targetContext),
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
                throw new InvalidOperationException("The reviewed shared-topology action plan contains blocked or dependency-skipped actions.");
            }

            var validatedSourcePlans = ValidateSourcePlans(dag, sourcePlans);
            var fidelityLimited = validatedSourcePlans.SelectMany(value => value.SourceWebFidelityIngredients)
                .Any(value => value.State == SourceWebFidelityState.AuthorizationBlocked);
            var operationId = Guid.NewGuid();
            var recorder = new MigrationExecutionRecorder(operationId, approvedActionPlan.ActionPlanDigest, journal);
            recorder.RecordState(MigrationExecutionStatus.Running, "Shared path-derived topology materialization started.");
            try
            {
                var receipt = PathDerivedTopologyMaterializer.Ensure(
                    runtime,
                    dag,
                    reviewedAnalysis,
                    approvedActionPlan,
                    operationId,
                    fidelityLimited,
                    validatedSourcePlans,
                    recorder);
                recorder.RecordState(MigrationExecutionStatus.Succeeded, "Every global topology action passed a fresh readback.");
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
            SharedTopologyGlobalActionDag dag,
            IEnumerable<SharedTopologyPlan> sourcePlans)
        {
            var materialized = (sourcePlans ?? Enumerable.Empty<SharedTopologyPlan>()).ToArray();
            if (materialized.Length == 0)
            {
                throw new InvalidDataException("The source plans sealed into the global action DAG are required for execution and fidelity reporting.");
            }
            foreach (var plan in materialized)
            {
                SharedTopologyPlanValidator.Validate(plan);
            }
            var expected = new HashSet<string>(dag.SourcePlanDigests, StringComparer.OrdinalIgnoreCase);
            var actual = new HashSet<string>(materialized.Select(value => value.PlanDigest), StringComparer.OrdinalIgnoreCase);
            if (!expected.SetEquals(actual))
            {
                throw new InvalidDataException("The supplied source plans do not exactly match the global action DAG plan set.");
            }
            return materialized;
        }
    }

    internal static class PathDerivedTopologyMaterializer
    {
        public static SharedTopologyGlobalMaterializationReceipt Ensure(
            IPathDerivedTopologyTargetRuntime runtime,
            SharedTopologyGlobalActionDag dag,
            SharedTopologyGlobalTargetAnalysis reviewedAnalysis,
            SharedTopologyGlobalActionPlan approvedActionPlan,
            Guid operationId,
            bool sourceFidelityAuthorizationLimited,
            IEnumerable<SharedTopologyPlan> sourcePlans,
            MigrationExecutionRecorder recorder)
        {
            SharedTopologyGlobalExecutionValidator.ValidateActionPlan(dag, reviewedAnalysis, approvedActionPlan);
            var startedAt = DateTimeOffset.UtcNow;
            var actionByKey = approvedActionPlan.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
            var completed = new Dictionary<string, SharedTopologyGlobalActionReceipt>(StringComparer.Ordinal);
            var receipts = new List<SharedTopologyGlobalActionReceipt>();

            foreach (var container in dag.Actions
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                .ThenBy(value => value.TargetSlotKey, StringComparer.Ordinal))
            {
                var approved = actionByKey[container.GlobalActionKey];
                Guid? expectedParentWebId = null;
                if (!string.IsNullOrWhiteSpace(container.ParentGlobalActionKey))
                {
                    if (!completed.TryGetValue(container.ParentGlobalActionKey, out var parent)
                        || !parent.FreshReadbackPassed)
                    {
                        throw new InvalidOperationException("The verified direct-parent global topology action is unavailable.");
                    }
                    expectedParentWebId = parent.TargetWebId;
                }

                var fresh = InspectExactlyOne(runtime, container, expectedParentWebId);
                EnsureApprovedTransition(approved, container, fresh);
                var changedTarget = false;
                var selectedAction = approved.SelectedAction;

                if (fresh.State == TargetWebContainerState.ReuseOwned
                    || fresh.State == TargetWebContainerState.ReuseExplicitApprovedHost)
                {
                    recorder.RecordAlreadySatisfied(
                        container.GlobalActionKey,
                        "Fresh target probe verified reusable Web '" + container.TargetWebUrl + "'.");
                }
                else if (fresh.State == TargetWebContainerState.RecoverInterruptedCreate)
                {
                    recorder.Execute(
                        container.GlobalActionKey,
                        "Recover exact interrupted target Web '" + container.TargetWebUrl + "'.",
                        () => runtime.RecoverOwnership(container));
                    changedTarget = true;
                }
                else if (fresh.State == TargetWebContainerState.CreateMissing)
                {
                    try
                    {
                        recorder.Execute(
                            container.GlobalActionKey,
                            "Create migration-owned target Web '" + container.TargetWebUrl + "'.",
                            () => runtime.Create(container));
                        changedTarget = true;
                    }
                    catch
                    {
                        var afterFailure = InspectExactlyOne(runtime, container, expectedParentWebId);
                        if (afterFailure.State == TargetWebContainerState.RecoverInterruptedCreate)
                        {
                            recorder.Execute(
                                container.GlobalActionKey + ".recover-after-create",
                                "Recover an exact create whose completion was interrupted.",
                                () => runtime.RecoverOwnership(container));
                            changedTarget = true;
                        }
                        else if (afterFailure.State != TargetWebContainerState.ReuseOwned)
                        {
                            throw;
                        }
                    }
                }
                else
                {
                    throw new InvalidOperationException("Fresh target state is not executable for global action '" + container.GlobalActionKey + "'.");
                }

                var readback = InspectExactlyOne(runtime, container, expectedParentWebId);
                EnsureFinalState(approved, container, readback);
                var actionReceipt = CreateReceipt(container, selectedAction, readback, changedTarget);
                receipts.Add(actionReceipt);
                completed.Add(container.GlobalActionKey, actionReceipt);
            }

            var receipt = new SharedTopologyGlobalMaterializationReceipt
            {
                OperationId = operationId,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                GlobalActionDagDigest = dag.DagDigest,
                ActionPlanDigest = approvedActionPlan.ActionPlanDigest,
                Actions = receipts,
                SourceWebMappings = CreateSourceWebMappings(sourcePlans, completed),
                SourceFidelityAuthorizationLimited = sourceFidelityAuthorizationLimited,
                FreshReadbackPassed = receipts.Count == approvedActionPlan.Actions.Count
                    && receipts.All(value => value.FreshReadbackPassed),
                Diagnostics = new List<string>
                {
                    "Each global target-Web action was freshly probed before execution and read back afterward.",
                    sourceFidelityAuthorizationLimited
                        ? "Source Web fidelity remains authorization-limited and is retained as an acceptance limitation."
                        : "No authorization-limited source Web fidelity was supplied with the global action DAG."
                }
            };
            receipt.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeReceipt(receipt);
            SharedTopologyGlobalExecutionValidator.ValidateReceipt(dag, approvedActionPlan, receipt);
            return receipt;
        }

        private static IList<SharedTopologySourceWebMaterializationReceipt> CreateSourceWebMappings(
            IEnumerable<SharedTopologyPlan> sourcePlans,
            IDictionary<string, SharedTopologyGlobalActionReceipt> completed)
        {
            var result = new List<SharedTopologySourceWebMaterializationReceipt>();
            foreach (var group in sourcePlans.SelectMany(value => value.SourceWebBindings)
                .GroupBy(value => value.SourceSiteId.ToString("D") + "/" + value.SourceWebId.ToString("D"), StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var bindings = group.ToArray();
                if (bindings.Select(value => value.TargetGlobalActionKey).Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    throw new InvalidDataException("One source Web is bound to conflicting global topology actions.");
                }
                var binding = bindings[0];
                if (!completed.TryGetValue(binding.TargetGlobalActionKey, out var target))
                {
                    throw new InvalidDataException("A source-Web binding has no completed global topology action.");
                }
                result.Add(new SharedTopologySourceWebMaterializationReceipt
                {
                    SourceSiteId = binding.SourceSiteId,
                    SourceWebId = binding.SourceWebId,
                    TargetGlobalActionKey = binding.TargetGlobalActionKey,
                    TargetSiteId = target.TargetSiteId,
                    TargetWebId = target.TargetWebId,
                    TargetWebUrl = target.TargetWebUrl,
                    TargetServerRelativeUrl = target.TargetServerRelativeUrl,
                    Ownership = target.Ownership
                });
            }
            return result;
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
            SharedTopologyGlobalAction approved,
            TargetWebContainerIngredientPlan container,
            PathDerivedTargetWebProbe readback)
        {
            var external = approved.SelectedAction == SharedTopologyActionKind.ReuseExplicitApprovedHost;
            var expected = external
                ? TargetWebContainerState.ReuseExplicitApprovedHost
                : TargetWebContainerState.ReuseOwned;
            if (readback.State != expected
                || !readback.TargetSiteId.HasValue
                || !readback.TargetWebId.HasValue
                || !readback.TargetParentWebId.HasValue
                || !readback.ObservedConfiguration.HasValue
                || !readback.ObservedHasUniqueRoleAssignments.HasValue
                || external && readback.TargetWebId != container.ApprovedExistingTargetWebId)
            {
                throw new InvalidOperationException("Fresh target readback did not verify the exact approved ownership boundary.");
            }
        }

        private static SharedTopologyGlobalActionReceipt CreateReceipt(
            TargetWebContainerIngredientPlan container,
            SharedTopologyActionKind selectedAction,
            PathDerivedTargetWebProbe readback,
            bool changedTarget)
        {
            return new SharedTopologyGlobalActionReceipt
            {
                TargetSlotKey = container.TargetSlotKey,
                GlobalActionKey = container.GlobalActionKey,
                ActionSignatureDigest = container.ActionSignatureDigest,
                SelectedAction = selectedAction,
                FinalState = readback.State,
                Ownership = readback.Ownership.Value,
                TargetSiteId = readback.TargetSiteId.Value,
                TargetWebId = readback.TargetWebId.Value,
                TargetParentWebId = readback.TargetParentWebId.Value,
                TargetWebUrl = container.TargetWebUrl,
                TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                ObservedOriginalIdentifier = readback.ObservedOriginalIdentifier,
                ObservedMappingDigest = readback.ObservedMappingDigest,
                ObservedTitle = readback.ObservedTitle,
                ObservedDescription = readback.ObservedDescription,
                ObservedTemplate = readback.ObservedTemplate,
                ObservedConfiguration = readback.ObservedConfiguration.Value,
                ObservedHasUniqueRoleAssignments = readback.ObservedHasUniqueRoleAssignments.Value,
                ChangedTarget = changedTarget,
                FreshReadbackPassed = true,
                Diagnostic = readback.Ownership == SharedTopologyOwnership.ExternalApprovedHost
                    ? "Reused exact externally owned Web without writing migration ownership markers."
                    : "Verified exact migration ownership markers after action completion."
            };
        }
    }
}
