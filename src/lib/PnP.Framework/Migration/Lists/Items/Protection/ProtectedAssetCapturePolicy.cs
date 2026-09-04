using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    public enum ProtectedAssetProtectionState
    {
        Unknown = 0,
        Protected = 1,
        Unprotected = 2
    }

    public enum ProtectedAssetCaptureDisposition
    {
        SafeToCapture = 1,
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

        /// <summary>
        /// Retained in the v1 decision contract for deterministic validation.
        /// Explicit protected-asset policies are always fail closed; false is
        /// rejected before any source payload request can be issued.
        /// </summary>
        public bool FailClosedOnUnknown { get; set; } = true;

        public static ProtectedAssetCapturePolicy MetadataOnly(string policyId)
        {
            return new ProtectedAssetCapturePolicy
            {
                PolicyId = policyId,
                FailClosedOnUnknown = true
            };
        }
    }

    public sealed class ProtectedAssetCaptureDecision
    {
        public const string ContractVersion = "pnp-protected-asset-capture-decision/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public string PolicyId { get; set; }

        public bool FailClosedOnUnknown { get; set; }

        public bool SourceListIrmEnabled { get; set; }

        public bool SourceListIrmStateObserved { get; set; }

        public ProtectedAssetProtectionState ProtectionState { get; set; }

        public ProtectedAssetCaptureDisposition Disposition { get; set; }

        public string ReasonCode { get; set; }

        public string Reason { get; set; }

        public string DecisionDigest { get; set; }

        [JsonIgnore]
        public bool IsMetadataOnly => Disposition == ProtectedAssetCaptureDisposition.MetadataOnly;

        [JsonIgnore]
        public bool IsSafeToCapture => Disposition == ProtectedAssetCaptureDisposition.SafeToCapture
            && ProtectionState == ProtectedAssetProtectionState.Unprotected;
    }
}
