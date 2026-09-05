using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal static class SharedTopologyTerminalActionReceiptFactory
    {
        public static SharedTopologyGlobalTerminalActionReceipt CreateAuthorizationBlocked(
            TargetWebContainerIngredientPlan container,
            SharedTopologyGlobalAction approved,
            PathDerivedTargetWebProbe fresh)
        {
            BoundLiteralHttpAuthorizationEvidence.Validate(
                fresh.AuthorizationEvidence,
                container.LogicalActionKey,
                PathDerivedTopologyTargetAnalyzer.TargetInspectionOperation,
                new Uri(PathDerivedTopologyTargetAnalyzer.ExpectedInspectionRequestUri(container)).Authority,
                PathDerivedTopologyTargetAnalyzer.ExpectedInspectionRequestUri(container));
            var receipt = new SharedTopologyGlobalTerminalActionReceipt
            {
                TargetSlotKey = container.TargetSlotKey,
                LogicalActionKey = container.LogicalActionKey,
                ExecutionGrantSignature = approved.ExecutionGrant.Signature,
                SelectedAction = approved.SelectedAction,
                FinalState = TargetWebContainerState.AuthorizationBlocked,
                ExecutionOutcome = SharedTopologyActionExecutionOutcome.AuthorizationBlocked,
                AuthorizationEvidence = fresh.AuthorizationEvidence,
                CauseLogicalActionKeys = new List<string> { container.LogicalActionKey },
                Diagnostic = "Fresh target inspection returned a bound literal HTTP 401/403; only this ingredient is authorization-blocked."
            };
            receipt.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeTerminalActionReceipt(receipt);
            return receipt;
        }

        public static SharedTopologyGlobalTerminalActionReceipt CreateDependencySkipped(
            TargetWebContainerIngredientPlan container,
            SharedTopologyGlobalAction approved,
            PathDerivedTargetWebProbe reviewed,
            IReadOnlyDictionary<string, SharedTopologyGlobalTerminalActionReceipt> terminal)
        {
            if (string.IsNullOrWhiteSpace(container.ParentLogicalActionKey)
                || !terminal.TryGetValue(container.ParentLogicalActionKey, out var parent))
            {
                throw new InvalidDataException("A dependency-skipped topology ingredient lacks its terminal direct-parent receipt.");
            }
            var causes = new HashSet<string>(reviewed.CauseLogicalActionKeys ?? Array.Empty<string>(), StringComparer.Ordinal)
            {
                parent.LogicalActionKey
            };
            foreach (var cause in parent.CauseLogicalActionKeys)
            {
                causes.Add(cause);
            }
            var receipt = new SharedTopologyGlobalTerminalActionReceipt
            {
                TargetSlotKey = container.TargetSlotKey,
                LogicalActionKey = container.LogicalActionKey,
                ExecutionGrantSignature = approved.ExecutionGrant.Signature,
                SelectedAction = approved.SelectedAction,
                FinalState = TargetWebContainerState.SkippedByDependency,
                ExecutionOutcome = SharedTopologyActionExecutionOutcome.SkippedByDependency,
                CauseLogicalActionKeys = causes.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                Diagnostic = "This ingredient was not attempted because its hard topology dependency did not reach a verified target state."
            };
            receipt.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeTerminalActionReceipt(receipt);
            return receipt;
        }
    }
}
