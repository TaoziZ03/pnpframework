using PnP.Framework.Migration.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    internal static class ProtectedAssetCaptureGate
    {
        public static T Capture<T>(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled,
            ProtectedAssetCapturePolicy policy,
            Func<T> binaryFetcher,
            out ProtectedAssetCaptureDecision decision)
        {
            decision = Decide(protection, sourceListIrmEnabled, policy);
            if (decision?.IsMetadataOnly == true)
            {
                return default(T);
            }
            if (binaryFetcher == null)
            {
                throw new ArgumentNullException(nameof(binaryFetcher));
            }
            return binaryFetcher();
        }

        public static ProtectedAssetCaptureDecision Decide(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled,
            ProtectedAssetCapturePolicy policy)
        {
            if (policy == null)
            {
                return null;
            }
            ValidatePolicy(policy);

            var state = ProtectionState(protection, sourceListIrmEnabled);
            var metadataOnly = state != ProtectedAssetProtectionState.Unprotected;
            var decision = new ProtectedAssetCaptureDecision
            {
                PolicyId = policy.PolicyId,
                FailClosedOnUnknown = policy.FailClosedOnUnknown,
                SourceListIrmEnabled = sourceListIrmEnabled,
                ProtectionState = state,
                Disposition = metadataOnly
                    ? ProtectedAssetCaptureDisposition.MetadataOnly
                    : ProtectedAssetCaptureDisposition.SafeToCapture,
                ReasonCode = metadataOnly
                    ? state == ProtectedAssetProtectionState.Protected
                        ? "ProtectedPayloadExcludedByPolicy"
                        : "ProtectionStateUnknownFailClosed"
                    : "UnprotectedPayloadSafeToCapture",
                Reason = metadataOnly
                    ? state == ProtectedAssetProtectionState.Protected
                        ? sourceListIrmEnabled
                            ? "The explicitly selected source-capture policy retains document metadata but does not request payload bytes from an IRM-enabled source library."
                            : "The explicitly selected source-capture policy retains document metadata but does not request protected payload bytes."
                        : "The explicitly selected source-capture policy fails closed because item metadata did not prove that the document is unprotected."
                    : "The source library is not IRM-enabled and captured item metadata explicitly proves that neither a label nor user-defined protection is present, so the binary request is safe to issue."
            };
            decision.DecisionDigest = ComputeDigest(decision);
            return decision;
        }

        public static void ValidatePolicy(ProtectedAssetCapturePolicy policy)
        {
            if (policy == null)
            {
                return;
            }
            if (!string.Equals(policy.SchemaVersion, ProtectedAssetCapturePolicy.ContractVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(policy.PolicyId)
                || !policy.FailClosedOnUnknown)
            {
                throw new InvalidDataException("The protected-asset capture policy has an unsupported schema, missing policy ID, or unsafe fail-open mode.");
            }
        }

        public static void ValidateDecision(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled,
            ProtectedAssetCapturePolicy policy,
            ProtectedAssetCaptureDecision decision)
        {
            if (policy == null)
            {
                if (decision != null)
                {
                    throw new InvalidDataException("A protected-asset capture decision requires an explicit capture policy.");
                }
                return;
            }

            var expected = Decide(protection, sourceListIrmEnabled, policy);
            if (decision == null
                || !string.Equals(decision.SchemaVersion, ProtectedAssetCaptureDecision.ContractVersion, StringComparison.Ordinal)
                || !string.Equals(decision.DecisionDigest, ComputeDigest(decision), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    MigrationContractSerializer.SerializeCanonical(expected),
                    MigrationContractSerializer.SerializeCanonical(decision),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The protected-asset capture decision is missing, stale, or differs from its sealed policy and metadata evidence.");
            }
        }

        public static string ComputeDigest(ProtectedAssetCaptureDecision decision)
        {
            if (decision == null)
            {
                throw new ArgumentNullException(nameof(decision));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    decision,
                    nameof(ProtectedAssetCaptureDecision.DecisionDigest)));
        }

        public static ProtectedAssetProtectionState ProtectionState(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled)
        {
            if (sourceListIrmEnabled
                || HasItemProtection(protection))
            {
                return ProtectedAssetProtectionState.Protected;
            }
            if (protection != null
                && protection.LabelFieldObserved
                && protection.UserDefinedProtectionFieldObserved
                && string.IsNullOrWhiteSpace(protection.LabelId)
                && IsFalse(protection.HasUserDefinedProtection))
            {
                return ProtectedAssetProtectionState.Unprotected;
            }
            return ProtectedAssetProtectionState.Unknown;
        }

        internal static bool HasItemProtection(
            ListDocumentInformationProtectionSnapshot protection)
        {
            return protection != null
                && (!string.IsNullOrWhiteSpace(protection.LabelId)
                    || IsTrue(protection.HasUserDefinedProtection));
        }

        private static bool IsTrue(string value)
        {
            return bool.TryParse(value, out var parsed) && parsed
                || string.Equals(value, "1", StringComparison.Ordinal);
        }

        private static bool IsFalse(string value)
        {
            return bool.TryParse(value, out var parsed) && !parsed
                || string.Equals(value, "0", StringComparison.Ordinal);
        }
    }
}
