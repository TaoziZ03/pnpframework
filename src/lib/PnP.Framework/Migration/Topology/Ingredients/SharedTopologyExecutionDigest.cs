using PnP.Framework.Migration.Packaging;
using System;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal static class SharedTopologyExecutionDigest
    {
        public static string ComputeAnalysis(SharedTopologyTargetAnalysis analysis)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException(nameof(analysis));
            }
            var canonical = new SharedTopologyTargetAnalysis
            {
                SchemaVersion = analysis.SchemaVersion,
                SharedTopologyPlanDigest = analysis.SharedTopologyPlanDigest,
                TargetSite = analysis.TargetSite,
                TargetWebContainers = analysis.TargetWebContainers,
                Issues = analysis.Issues,
                AnalysisDigest = null
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(canonical));
        }

        public static string ComputeActionPlan(SharedTopologyActionPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            var canonical = new SharedTopologyActionPlan
            {
                SchemaVersion = plan.SchemaVersion,
                SharedTopologyPlanDigest = plan.SharedTopologyPlanDigest,
                TargetAnalysisDigest = plan.TargetAnalysisDigest,
                Actions = plan.Actions,
                ActionPlanDigest = null
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(canonical));
        }

        public static string ComputeReceipt(SharedTopologyMaterializationReceipt receipt)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            var canonical = new SharedTopologyMaterializationReceipt
            {
                SchemaVersion = receipt.SchemaVersion,
                SharedTopologyPlanDigest = receipt.SharedTopologyPlanDigest,
                ActionPlanDigest = receipt.ActionPlanDigest,
                Webs = receipt.Webs,
                FreshReadbackPassed = receipt.FreshReadbackPassed,
                Diagnostics = receipt.Diagnostics,
                ReceiptDigest = null
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(canonical));
        }
    }
}
