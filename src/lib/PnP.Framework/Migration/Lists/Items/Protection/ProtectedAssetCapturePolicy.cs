using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    public enum ProtectedAssetProtectionState
    {
        Unknown = 0,
        Protected = 1
    }

    public enum ProtectedAssetCaptureDisposition
    {
        CaptureBinary = 1,
        MetadataOnly = 2
    }

    /// <summary>
    /// Explicit source-capture policy for omitting protected document payloads.
    /// A null policy preserves the historical capture-all behavior.
    /// </summary>
    public sealed class ProtectedAssetCapturePolicy
    {
        public const string ContractVersion = "pnp-protected-asset-capture-policy/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public string PolicyId { get; set; }

        public bool FailClosedOnUnknown { get; set; }

        public static ProtectedAssetCapturePolicy MetadataOnly(
            string policyId,
            bool failClosedOnUnknown = true)
        {
            return new ProtectedAssetCapturePolicy
            {
                PolicyId = policyId,
                FailClosedOnUnknown = failClosedOnUnknown
            };
        }
    }

    public sealed class ProtectedAssetCaptureDecision
    {
        public const string ContractVersion = "pnp-protected-asset-capture-decision/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public string PolicyId { get; set; }

        public bool FailClosedOnUnknown { get; set; }

        public ProtectedAssetProtectionState ProtectionState { get; set; }

        public ProtectedAssetCaptureDisposition Disposition { get; set; }

        public string ReasonCode { get; set; }

        public string Reason { get; set; }

        public string DecisionDigest { get; set; }

        [JsonIgnore]
        public bool IsMetadataOnly => Disposition == ProtectedAssetCaptureDisposition.MetadataOnly;
    }
}
