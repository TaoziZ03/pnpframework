using PnP.Framework.Migration.Packaging;
using System;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal static class SharedTopologyGlobalExecutionDigest
    {
        public static string ComputeAnalysis(SharedTopologyGlobalTargetAnalysis analysis)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    analysis ?? throw new ArgumentNullException(nameof(analysis)),
                    nameof(SharedTopologyGlobalTargetAnalysis.AnalysisDigest)));
        }

        public static string ComputeActionPlan(SharedTopologyGlobalActionPlan plan)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    plan ?? throw new ArgumentNullException(nameof(plan)),
                    nameof(SharedTopologyGlobalActionPlan.ActionPlanDigest)));
        }

        public static string ComputeReceipt(SharedTopologyGlobalMaterializationReceipt receipt)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    receipt ?? throw new ArgumentNullException(nameof(receipt)),
                    nameof(SharedTopologyGlobalMaterializationReceipt.ReceiptDigest)));
        }

        public static string ComputeActionReceipt(SharedTopologyGlobalActionReceipt receipt)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    receipt ?? throw new ArgumentNullException(nameof(receipt)),
                    nameof(SharedTopologyGlobalActionReceipt.ReceiptDigest)));
        }

        public static string ComputeSourceMappingReceipt(SharedTopologySourceWebMaterializationReceipt receipt)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    receipt ?? throw new ArgumentNullException(nameof(receipt)),
                    nameof(SharedTopologySourceWebMaterializationReceipt.ReceiptDigest)));
        }
    }
}
