using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public sealed class SharedTopologyMaterializer
    {
        public SharedTopologyMaterializationReceipt Execute(
            SharedTopologyPlan plan,
            SharedTopologyTargetAnalysis approvedAnalysis,
            SharedTopologyActionPlan actionPlan,
            string approvedActionPlanDigest,
            ISharedTopologyTargetRuntime runtime,
            IMigrationExecutionJournal journal = null)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            SharedTopologyExecutionValidator.ValidateActionPlan(plan, approvedAnalysis, actionPlan);
            if (!actionPlan.IsExecutable)
            {
                throw new InvalidOperationException("The shared topology action plan contains a blocked or dependency-skipped target Web.");
            }
            if (string.IsNullOrWhiteSpace(approvedActionPlanDigest)
                || !string.Equals(approvedActionPlanDigest, actionPlan.ActionPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The approved shared topology action-plan digest does not match the sealed action plan.");
            }

            var freshBefore = Inspect(runtime, plan);
            var freshActions = SharedTopologyActionPlanProjector.Project(plan, freshBefore);
            if (!string.Equals(freshActions.ActionPlanDigest, actionPlan.ActionPlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Fresh target inspection changed the approved shared topology actions.");
            }

            var recorder = new MigrationExecutionRecorder(Guid.NewGuid(), actionPlan.ActionPlanDigest, journal);
            recorder.RecordState(MigrationExecutionStatus.Running, "Shared target-Web topology materialization is starting.");
            var containerById = plan.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            var probeById = freshBefore.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            var receipt = new SharedTopologyMaterializationReceipt
            {
                SharedTopologyPlanDigest = plan.PlanDigest,
                ActionPlanDigest = actionPlan.ActionPlanDigest
            };
            foreach (var action in actionPlan.Actions.OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl)))
            {
                var container = containerById[action.IngredientId];
                TargetWebContainerObservation observation;
                SharedTopologyReceiptDisposition disposition;
                if (action.Action == SharedTopologyActionKind.Reuse)
                {
                    var probe = probeById[action.IngredientId];
                    if (!probe.TargetSiteId.HasValue || !probe.TargetWebId.HasValue || !probe.TargetParentWebId.HasValue)
                    {
                        throw new InvalidDataException("A reusable target-Web probe has no runtime Site/Web/parent identity.");
                    }
                    recorder.RecordAlreadySatisfied(ActionId(action.IngredientId), "Reuse shared target Web '" + container.TargetWebUrl + "'.");
                    observation = Observation(probe);
                    disposition = SharedTopologyReceiptDisposition.Reused;
                }
                else if (action.Action == SharedTopologyActionKind.CreateMissing)
                {
                    observation = recorder.Execute(
                        ActionId(action.IngredientId),
                        "Create shared target Web '" + container.TargetWebUrl + "' with reviewed target-only provisioning values.",
                        () => runtime.CreateTargetWebContainer(plan, container),
                        value => MutationOutcome.Applied,
                        value => "Created target Web '" + container.TargetWebUrl + "'.");
                    disposition = SharedTopologyReceiptDisposition.Created;
                }
                else
                {
                    throw new InvalidOperationException("Unexpected shared topology action '" + action.Action + "'.");
                }

                EnsureSuccessfulObservation(container, observation);
                receipt.Webs.Add(new SharedTopologyWebReceipt
                {
                    IngredientId = container.IngredientId,
                    TargetSiteId = observation.TargetSiteId.Value,
                    TargetWebId = observation.TargetWebId.Value,
                    TargetParentWebId = observation.TargetParentWebId.Value,
                    TargetWebUrl = container.TargetWebUrl,
                    TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                    Disposition = disposition,
                    IngredientDigest = container.IngredientDigest
                });
            }

            var finalAnalysis = Inspect(runtime, plan);
            var verification = SharedTopologyVerifier.Verify(plan, actionPlan, receipt, finalAnalysis);
            if (!verification.Passed)
            {
                throw new InvalidOperationException("Fresh shared topology verification failed: " + string.Join("; ", verification.Mismatches));
            }
            receipt.FreshReadbackPassed = true;
            receipt.Diagnostics.Add("Fresh target readback verified " + receipt.Webs.Count + " shared target-Web container(s).");
            receipt.ReceiptDigest = SharedTopologyExecutionDigest.ComputeReceipt(receipt);
            SharedTopologyExecutionValidator.ValidateReceipt(plan, actionPlan, receipt);
            recorder.RecordState(MigrationExecutionStatus.Succeeded, "Shared target-Web topology materialization and fresh verification completed.");
            return receipt;
        }

        private static SharedTopologyTargetAnalysis Inspect(ISharedTopologyTargetRuntime runtime, SharedTopologyPlan plan)
        {
            return SharedTopologyTargetAnalyzer.Analyze(
                plan,
                runtime.InspectTargetSite(plan),
                runtime.InspectTargetWebContainers(plan));
        }

        private static TargetWebContainerObservation Observation(TargetWebContainerProbe probe)
        {
            return new TargetWebContainerObservation
            {
                IngredientId = probe.IngredientId,
                Exists = probe.Exists,
                TargetSiteId = probe.TargetSiteId,
                TargetWebId = probe.TargetWebId,
                TargetParentWebId = probe.TargetParentWebId,
                TargetWebUrl = probe.TargetWebUrl,
                TargetServerRelativeUrl = probe.TargetServerRelativeUrl
            };
        }

        private static void EnsureSuccessfulObservation(
            TargetWebContainerIngredientPlan container,
            TargetWebContainerObservation observation)
        {
            if (observation == null
                || observation.HttpStatusCode.HasValue
                || !observation.Exists
                || !observation.TargetSiteId.HasValue
                || !observation.TargetWebId.HasValue
                || !observation.TargetParentWebId.HasValue
                || !SharedTopologyPath.EqualsUrl(observation.TargetWebUrl, container.TargetWebUrl)
                || !SharedTopologyPath.EqualsPath(observation.TargetServerRelativeUrl, container.TargetServerRelativeUrl))
            {
                throw new InvalidOperationException("The target runtime did not return exact readback for shared topology ingredient '" + container.IngredientId + "'.");
            }
        }

        private static string ActionId(string ingredientId)
        {
            return "topology.shared." + ingredientId;
        }
    }
}
