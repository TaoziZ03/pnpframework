using PnP.Framework.Migration.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    internal static class ProtectedAssetCaptureGate
    {
        public static T Capture<T>(
            ListDocumentInformationProtectionSnapshot protection,
            ProtectedAssetCapturePolicy policy,
            Func<T> binaryFetcher,
            out ProtectedAssetCaptureDecision decision)
        {
            decision = Decide(protection, policy);
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
            ProtectedAssetCapturePolicy policy)
        {
            if (policy == null)
            {
                return null;
            }
            ValidatePolicy(policy);

            var state = ProtectionState(protection);
            var metadataOnly = state == ProtectedAssetProtectionState.Protected
                || state == ProtectedAssetProtectionState.Unknown && policy.FailClosedOnUnknown;
            var decision = new ProtectedAssetCaptureDecision
            {
                PolicyId = policy.PolicyId,
                FailClosedOnUnknown = policy.FailClosedOnUnknown,
                ProtectionState = state,
                Disposition = metadataOnly
                    ? ProtectedAssetCaptureDisposition.MetadataOnly
                    : ProtectedAssetCaptureDisposition.CaptureBinary,
                ReasonCode = metadataOnly
                    ? state == ProtectedAssetProtectionState.Protected
                        ? "ProtectedPayloadExcludedByPolicy"
                        : "ProtectionStateUnknownFailClosed"
                    : "BinaryCaptureAllowedByPolicy",
                Reason = metadataOnly
                    ? state == ProtectedAssetProtectionState.Protected
                        ? "The explicitly selected source-capture policy retains document metadata but does not request protected payload bytes."
                        : "The explicitly selected source-capture policy fails closed because item metadata did not prove that the document is unprotected."
                    : "The explicitly selected source-capture policy allows this document binary request."
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
                || string.IsNullOrWhiteSpace(policy.PolicyId))
            {
                throw new InvalidDataException("The protected-asset capture policy has an unsupported schema or missing policy ID.");
            }
        }

        public static void ValidateDecision(
            ListDocumentInformationProtectionSnapshot protection,
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

            var expected = Decide(protection, policy);
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
            ListDocumentInformationProtectionSnapshot protection)
        {
            return protection != null
                && (!string.IsNullOrWhiteSpace(protection.LabelId)
                    || IsTrue(protection.HasUserDefinedProtection))
                ? ProtectedAssetProtectionState.Protected
                : ProtectedAssetProtectionState.Unknown;
        }

        private static bool IsTrue(string value)
        {
            return bool.TryParse(value, out var parsed) && parsed
                || string.Equals(value, "1", StringComparison.Ordinal);
        }
    }
}
