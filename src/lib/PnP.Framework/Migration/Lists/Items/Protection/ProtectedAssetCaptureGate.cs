using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    internal static class ListDocumentInformationProtectionSnapshotReader
    {
        private static readonly string[] ProtectionFields =
        {
            "_IpLabelId",
            "_IpLabelAssignmentMethod",
            "_HasUserDefinedProtection",
            "_IpLabelHash",
            "_IpLabelPromotionCtagVersion"
        };

        public static ListDocumentInformationProtectionSnapshot Read(IDictionary<string, object> fieldValues)
        {
            var labelId = ReadValue(fieldValues, "_IpLabelId");
            var userDefined = ReadValue(fieldValues, "_HasUserDefinedProtection");
            var anyFieldObserved = ProtectionFields.Any(name => ContainsField(fieldValues, name));
            var protectedByLabel = !string.IsNullOrWhiteSpace(labelId);
            var protectedByFlag = IsTrue(userDefined);
            var state = protectedByLabel || protectedByFlag
                ? ProtectedAssetProtectionState.Protected
                : anyFieldObserved && ContainsField(fieldValues, "_IpLabelId") && !string.IsNullOrWhiteSpace(userDefined)
                    ? ProtectedAssetProtectionState.Unprotected
                    : ProtectedAssetProtectionState.Unknown;
            var result = new ListDocumentInformationProtectionSnapshot
            {
                State = state,
                LabelId = labelId,
                AssignmentMethod = ReadValue(fieldValues, "_IpLabelAssignmentMethod"),
                HasUserDefinedProtection = userDefined,
                LabelHash = ReadValue(fieldValues, "_IpLabelHash"),
                PromotionCtagVersion = ReadValue(fieldValues, "_IpLabelPromotionCtagVersion"),
                EvidenceSource = "SharePoint.ListItem.FieldValues"
            };
            if (state == ProtectedAssetProtectionState.Unknown)
            {
                result.Diagnostics.Add("InformationProtectionStateUnknown: the item metadata did not contain enough explicit fields to prove that binary export is safe.");
            }
            return result;
        }

        private static bool ContainsField(IDictionary<string, object> values, string name)
        {
            return values != null && values.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadValue(IDictionary<string, object> values, string name)
        {
            if (values == null)
            {
                return null;
            }
            foreach (var value in values)
            {
                if (string.Equals(value.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToString(value.Value, CultureInfo.InvariantCulture);
                }
            }
            return null;
        }

        private static bool IsTrue(string value)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) && parsed
                || string.Equals(value, "1", StringComparison.Ordinal);
        }
    }

    internal static class ProtectedAssetCaptureGate
    {
        public static ProtectedAssetCaptureDecision Decide(
            ListDocumentInformationProtectionSnapshot protection,
            ProtectedAssetCapturePolicy policy)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (!string.Equals(policy.SchemaVersion, ProtectedAssetCapturePolicy.ContractVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(policy.PolicyId))
            {
                throw new InvalidOperationException("The protected-asset capture policy is missing a supported schema version or policy ID.");
            }

            var state = protection?.State ?? ProtectedAssetProtectionState.Unknown;
            var metadataOnly = policy.Profile == ProtectedAssetCaptureProfile.MicrosoftTenantMetadataOnly
                && (state == ProtectedAssetProtectionState.Protected
                    || state == ProtectedAssetProtectionState.Unknown && policy.FailClosedOnUnknown);
            var decision = new ProtectedAssetCaptureDecision
            {
                Profile = policy.Profile,
                PolicyId = policy.PolicyId,
                ProtectionState = state,
                Disposition = metadataOnly
                    ? ProtectedAssetBinaryCaptureDisposition.MetadataOnly
                    : ProtectedAssetBinaryCaptureDisposition.CaptureBinary,
                ReasonCode = metadataOnly
                    ? state == ProtectedAssetProtectionState.Protected
                        ? "MicrosoftProtectedAssetExportDenied"
                        : "InformationProtectionStateUnknownFailClosed"
                    : "ProtectedAssetBinaryCaptureAllowed",
                Reason = metadataOnly
                    ? state == ProtectedAssetProtectionState.Protected
                        ? "Microsoft-tenant policy prohibits protected company asset bytes from leaving the source tenant."
                        : "Microsoft-tenant policy fails closed because metadata did not prove that requesting bytes is safe."
                    : "The explicitly selected fidelity profile allows source binary capture."
            };
            decision.DecisionDigest = ComputeDigest(decision);
            return decision;
        }

        public static T Capture<T>(
            ListDocumentInformationProtectionSnapshot protection,
            ProtectedAssetCapturePolicy policy,
            Func<T> binaryFetcher,
            out ProtectedAssetCaptureDecision decision)
        {
            decision = Decide(protection, policy);
            if (decision.IsMetadataOnly)
            {
                return default(T);
            }
            if (binaryFetcher == null)
            {
                throw new ArgumentNullException(nameof(binaryFetcher));
            }
            return binaryFetcher();
        }

        public static string ComputeDigest(ProtectedAssetCaptureDecision decision)
        {
            if (decision == null)
            {
                throw new ArgumentNullException(nameof(decision));
            }
            var value = decision.DecisionDigest;
            decision.DecisionDigest = null;
            try
            {
                return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(decision));
            }
            finally
            {
                decision.DecisionDigest = value;
            }
        }

        public static bool IsControlledAsset(ListDocumentSnapshot document)
        {
            return document != null
                && document.Kind == ListDocumentObjectKind.File
                && (document.CaptureDecision?.IsMetadataOnly == true
                    || document.InformationProtection?.State == ProtectedAssetProtectionState.Protected);
        }
    }
}
