using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    public static class SharedTopologyVerifier
    {
        public static SharedTopologyVerificationResult Verify(
            SharedTopologyPlan plan,
            SharedTopologyActionPlan actionPlan,
            SharedTopologyMaterializationReceipt receipt,
            SharedTopologyTargetAnalysis freshAnalysis)
        {
            var mismatches = new List<string>();
            if (receipt == null)
            {
                mismatches.Add("The shared topology materialization receipt is missing.");
            }
            if (freshAnalysis == null
                || !string.Equals(freshAnalysis.SharedTopologyPlanDigest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add("Fresh target analysis is missing or references a different shared topology plan.");
            }
            else
            {
                var freshById = freshAnalysis.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
                foreach (var container in plan.TargetWebContainers)
                {
                    if (!freshById.TryGetValue(container.IngredientId, out var probe)
                        || probe.State != TargetWebContainerState.Reuse
                        || !probe.Exists
                        || !probe.TargetSiteId.HasValue
                        || !probe.TargetWebId.HasValue
                        || !probe.TargetParentWebId.HasValue)
                    {
                        mismatches.Add("Target Web '" + container.TargetWebUrl + "' did not pass fresh exact-path readback.");
                    }
                }
            }
            if (receipt != null)
            {
                var receipts = receipt.Webs.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
                foreach (var container in plan.TargetWebContainers)
                {
                    if (!receipts.TryGetValue(container.IngredientId, out var web)
                        || !string.Equals(web.IngredientDigest, container.IngredientDigest, StringComparison.OrdinalIgnoreCase)
                        || !SharedTopologyPath.EqualsUrl(web.TargetWebUrl, container.TargetWebUrl)
                        || !SharedTopologyPath.EqualsPath(web.TargetServerRelativeUrl, container.TargetServerRelativeUrl))
                    {
                        mismatches.Add("Receipt coverage differs for topology ingredient '" + container.IngredientId + "'.");
                    }
                }
            }
            return new SharedTopologyVerificationResult
            {
                SharedTopologyPlanDigest = plan.PlanDigest,
                ReceiptDigest = receipt?.ReceiptDigest,
                Passed = mismatches.Count == 0,
                Mismatches = mismatches
            };
        }
    }
}
