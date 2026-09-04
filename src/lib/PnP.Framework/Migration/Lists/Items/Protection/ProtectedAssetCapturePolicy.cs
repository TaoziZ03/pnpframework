using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    public enum ProtectedAssetCaptureProfile
    {
        MicrosoftTenantMetadataOnly = 1,
        FidelityAllowed = 2
    }

    public enum ProtectedAssetProtectionState
    {
        Unknown = 0,
        Unprotected = 1,
        Protected = 2
    }

    public enum ProtectedAssetBinaryCaptureDisposition
    {
        CaptureBinary = 1,
        MetadataOnly = 2
    }

    /// <summary>
    /// Selects the source-side security boundary before any document binary is
    /// requested. The Microsoft-tenant profile is deliberately fail-closed.
    /// </summary>
    public sealed class ProtectedAssetCapturePolicy
    {
        public const string ContractVersion = "pnp-protected-asset-capture-policy/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public ProtectedAssetCaptureProfile Profile { get; set; } = ProtectedAssetCaptureProfile.MicrosoftTenantMetadataOnly;

        public string PolicyId { get; set; } = "policy.protected-asset.microsoft-tenant-metadata-only";

        public bool FailClosedOnUnknown { get; set; } = true;

        public static ProtectedAssetCapturePolicy MicrosoftTenant()
        {
            return new ProtectedAssetCapturePolicy();
        }

        public static ProtectedAssetCapturePolicy FidelityAllowed(string policyId = "policy.protected-asset.fidelity-allowed")
        {
            return new ProtectedAssetCapturePolicy
            {
                Profile = ProtectedAssetCaptureProfile.FidelityAllowed,
                PolicyId = policyId,
                FailClosedOnUnknown = false
            };
        }
    }

    /// <summary>
    /// Immutable metadata evidence used to decide whether requesting file bytes
    /// is allowed. No payload-derived signal is used because that would be too
    /// late to enforce the tenant boundary.
    /// </summary>
    public sealed class ListDocumentInformationProtectionSnapshot
    {
        public ProtectedAssetProtectionState State { get; set; }

        public string LabelId { get; set; }

        public string AssignmentMethod { get; set; }

        public string HasUserDefinedProtection { get; set; }

        public string LabelHash { get; set; }

        public string PromotionCtagVersion { get; set; }

        public string EvidenceSource { get; set; }

        public IList<string> Diagnostics { get; set; } = new List<string>();
    }

    public sealed class ProtectedAssetCaptureDecision
    {
        public const string ContractVersion = "pnp-protected-asset-capture-decision/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public ProtectedAssetCaptureProfile Profile { get; set; }

        public string PolicyId { get; set; }

        public ProtectedAssetProtectionState ProtectionState { get; set; }

        public ProtectedAssetBinaryCaptureDisposition Disposition { get; set; }

        public string ReasonCode { get; set; }

        public string Reason { get; set; }

        public string DecisionDigest { get; set; }

        public bool IsMetadataOnly => Disposition == ProtectedAssetBinaryCaptureDisposition.MetadataOnly;
    }
}
