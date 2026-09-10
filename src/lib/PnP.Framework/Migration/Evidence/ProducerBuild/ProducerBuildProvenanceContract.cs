using PnP.Framework.Migration.Packaging;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Evidence.ProducerBuild
{
    public static class ProducerBuildProvenanceContract
    {
        public const string ManifestSchemaVersion = "pnp-producer-build-provenance-manifest/v1";
        public const string ReceiptSchemaVersion = "pnp-producer-build-provenance-receipt/v1";
        public const string Unverified = "UNVERIFIED";
        public const string Verified = "VERIFIED";
        public const string Rejected = "REJECTED";

        public static string ValidateManifestAndComputeDigest(ProducerBuildProvenanceManifest manifest)
        {
            Require(manifest != null, "A producer build provenance manifest is required.");
            Require(string.Equals(manifest.SchemaVersion, ManifestSchemaVersion, StringComparison.Ordinal),
                "The producer build provenance manifest schema is unsupported.");
            Require(!string.IsNullOrWhiteSpace(manifest.ProducerId)
                && !string.IsNullOrWhiteSpace(manifest.ProducerVersion)
                && !string.IsNullOrWhiteSpace(manifest.BinaryName),
                "Producer identity and binary name are required.");
            ValidateImplementationRef(manifest.ImplementationRef);
            ValidateDigest(manifest.BinarySha256, "producer binary digest");
            Require(!string.IsNullOrWhiteSpace(manifest.TargetFramework)
                && !string.IsNullOrWhiteSpace(manifest.BuildConfiguration),
                "Producer target framework and build configuration are required.");
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(manifest));
        }

        public static string ValidateReceiptAndComputeDigest(
            ProducerBuildProvenanceReceipt receipt,
            ProducerBuildProvenanceManifest manifest)
        {
            Require(receipt != null, "A producer build provenance receipt is required.");
            Require(string.Equals(receipt.SchemaVersion, ReceiptSchemaVersion, StringComparison.Ordinal),
                "The producer build provenance receipt schema is unsupported.");
            var manifestDigest = ValidateManifestAndComputeDigest(manifest);
            Require(string.Equals(receipt.ManifestDigestSha256, manifestDigest, StringComparison.OrdinalIgnoreCase),
                "The producer provenance receipt is foreign to its manifest.");
            Require(string.Equals(receipt.BinarySha256, manifest.BinarySha256, StringComparison.OrdinalIgnoreCase),
                "The producer provenance receipt binary is foreign to its manifest.");
            Require(IsKnownStatus(receipt.VerificationStatus),
                "The producer provenance verification status is unsupported.");
            if (!string.Equals(receipt.VerificationStatus, Unverified, StringComparison.Ordinal))
            {
                Require(!string.IsNullOrWhiteSpace(receipt.VerifierId) && receipt.VerifiedAtUtc != default,
                    "A terminal producer provenance receipt requires verifier identity and time.");
            }
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(receipt));
        }

        public static ProducerBuildProvenanceReceipt CreateUnverified(
            ProducerBuildProvenanceManifest manifest,
            string reason = null)
        {
            var manifestDigest = ValidateManifestAndComputeDigest(manifest);
            return new ProducerBuildProvenanceReceipt
            {
                ManifestDigestSha256 = manifestDigest,
                BinarySha256 = manifest.BinarySha256,
                VerificationStatus = Unverified,
                Reason = reason ?? "No producer build provenance verifier was supplied."
            };
        }

        private static bool IsKnownStatus(string value)
        {
            return string.Equals(value, Unverified, StringComparison.Ordinal)
                || string.Equals(value, Verified, StringComparison.Ordinal)
                || string.Equals(value, Rejected, StringComparison.Ordinal);
        }

        private static void ValidateImplementationRef(string value)
        {
            Require(value != null && value.Length == 40 && value.All(IsHex),
                "The producer implementation ref must be a full Git SHA.");
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex),
                "The " + name + " must be a SHA-256 digest.");
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
