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
            return Capture(
                protection,
                sourceListIrmEnabled,
                true,
                policy,
                binaryFetcher,
                out decision);
        }

        public static T Capture<T>(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled,
            bool sourceListIrmStateObserved,
            ProtectedAssetCapturePolicy policy,
            Func<T> binaryFetcher,
            out ProtectedAssetCaptureDecision decision)
        {
            decision = Decide(
                protection,
                sourceListIrmEnabled,
                sourceListIrmStateObserved,
                policy);
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
            return Decide(protection, sourceListIrmEnabled, true, policy);
        }

        public static ProtectedAssetCaptureDecision Decide(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled,
            bool sourceListIrmStateObserved,
            ProtectedAssetCapturePolicy policy)
        {
            if (policy == null)
            {
                return null;
            }
            ValidatePolicy(policy);

            var state = ProtectionState(
                protection,
                sourceListIrmEnabled,
                sourceListIrmStateObserved);
            var metadataOnly = state != ProtectedAssetProtectionState.Unprotected;
            var decision = new ProtectedAssetCaptureDecision
            {
                PolicyId = policy.PolicyId,
                FailClosedOnUnknown = policy.FailClosedOnUnknown,
                SourceListIrmEnabled = sourceListIrmEnabled,
                SourceListIrmStateObserved = sourceListIrmStateObserved,
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
                        : "The explicitly selected source-capture policy fails closed because List IRM state and item metadata did not completely prove that the document is unprotected."
                    : "The source library is not IRM-enabled and captured item metadata explicitly proves that label, user-defined protection, encrypted-content, decrypt-skip, and RMS-template evidence are all absent, so the binary request is safe to issue."
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
            ValidateDecision(
                protection,
                sourceListIrmEnabled,
                true,
                policy,
                decision);
        }

        public static void ValidateDecision(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled,
            bool sourceListIrmStateObserved,
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

            var expected = Decide(
                protection,
                sourceListIrmEnabled,
                sourceListIrmStateObserved,
                policy);
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
            return ProtectionState(protection, sourceListIrmEnabled, true);
        }

        public static ProtectedAssetProtectionState ProtectionState(
            ListDocumentInformationProtectionSnapshot protection,
            bool sourceListIrmEnabled,
            bool sourceListIrmStateObserved)
        {
            if (sourceListIrmEnabled
                || HasItemProtection(protection))
            {
                return ProtectedAssetProtectionState.Protected;
            }
            if (sourceListIrmStateObserved
                && protection != null
                && protection.LabelFieldObserved
                && protection.UserDefinedProtectionFieldObserved
                && protection.DecryptSkipReasonObserved
                && protection.HasEncryptedContentFieldObserved
                && protection.RmsTemplateIdFieldObserved
                && string.IsNullOrWhiteSpace(protection.LabelId)
                && IsFalse(protection.HasUserDefinedProtection)
                && IsFalse(protection.HasEncryptedContent)
                && IsZeroLike(protection.DecryptSkipReason)
                && IsZeroLike(protection.RmsTemplateId))
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
                    || IsTrue(protection.HasUserDefinedProtection)
                    || IsTrue(protection.HasEncryptedContent)
                    || !IsZeroLike(protection.DecryptSkipReason)
                    || !IsZeroLike(protection.RmsTemplateId));
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

        private static bool IsZeroLike(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "0", StringComparison.Ordinal)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return Guid.TryParse(value, out var guid) && guid == Guid.Empty;
        }
    }
}
