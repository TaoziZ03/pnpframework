using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
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

        public static string SealManifest(ProducerBuildProvenanceManifest manifest)
        {
            Require(manifest != null, "A producer build provenance manifest is required.");
            manifest.ContentSha256 = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    manifest,
                    nameof(ProducerBuildProvenanceManifest.ContentSha256)));
            ValidateManifestAndComputeDigest(manifest);
            return manifest.ContentSha256;
        }

        public static string ValidateManifestAndComputeDigest(ProducerBuildProvenanceManifest manifest)
        {
            Require(manifest != null, "A producer build provenance manifest is required.");
            Require(string.Equals(manifest.SchemaVersion, ManifestSchemaVersion, StringComparison.Ordinal),
                "The producer build provenance manifest schema is unsupported.");
            ValidateDigest(manifest.ContentSha256, "producer provenance manifest content seal");
            var computed = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    manifest,
                    nameof(ProducerBuildProvenanceManifest.ContentSha256)));
            Require(DigestEquals(manifest.ContentSha256, computed),
                "The producer build provenance manifest content seal is stale or corrupt.");
            Require(!string.IsNullOrWhiteSpace(manifest.ProducerId)
                && !string.IsNullOrWhiteSpace(manifest.ProducerVersion)
                && !string.IsNullOrWhiteSpace(manifest.RepositoryIdentity),
                "Producer and repository identity are required.");
            ValidateImplementationRef(manifest.SubjectImplementationRef, "subject implementation ref");
            ValidateImplementationRef(manifest.BuildDriverImplementationRef, "build-driver implementation ref");
            Require(IsGitObject(manifest.TreeId), "The producer source tree ID is invalid.");
            ValidateDigest(manifest.CleanSourceReceiptDigestSha256, "clean-source receipt digest");
            ValidateDigest(manifest.LockedDependencyGraphSha256, "locked dependency graph digest");
            ValidateOptionalDigest(manifest.RequestedHistoricalBinarySha256, "requested historical binary digest");
            ValidateDigestList(manifest.SourceArtifactDigestsSha256, "source artifact digest");
            ValidateDigestList(manifest.SubmoduleDigestsSha256, "submodule digest");
            ValidateDigestList(manifest.InputArtifactDigestsSha256, "input artifact digest");
            Require(manifest.Toolchain != null
                && !string.IsNullOrWhiteSpace(manifest.Toolchain.SdkVersion)
                && !string.IsNullOrWhiteSpace(manifest.Toolchain.MsBuildVersion)
                && !string.IsNullOrWhiteSpace(manifest.Toolchain.RuntimeVersion)
                && !string.IsNullOrWhiteSpace(manifest.Toolchain.OperatingSystem)
                && !string.IsNullOrWhiteSpace(manifest.Toolchain.RuntimeIdentifier)
                && !string.IsNullOrWhiteSpace(manifest.Toolchain.TargetFramework)
                && !string.IsNullOrWhiteSpace(manifest.Toolchain.Configuration),
                "The exact producer build toolchain is required.");
            Require(manifest.Command != null
                && !string.IsNullOrWhiteSpace(manifest.Command.Executable)
                && (manifest.Command.Arguments?.Count ?? 0) > 0,
                "The exact producer build command is required.");
            Require(manifest.StartedAtUtc != default
                && manifest.CompletedAtUtc >= manifest.StartedAtUtc
                && !string.IsNullOrWhiteSpace(manifest.Result),
                "The producer build timeline and result are required.");
            ValidateArtifactReference(manifest.RestoreLog, "restore log");
            ValidateArtifactReference(manifest.BuildLog, "build log");
            ValidateArtifacts(manifest.Outputs, "build output");
            ValidateArtifacts(manifest.RuntimeLoadClosure, "runtime load closure");
            Require(manifest.Outputs.Any(value => string.Equals(value.Role, "producer", StringComparison.Ordinal)),
                "The provenance manifest must identify the producer binary output.");
            Require((manifest.SchemaCompatibilitySet?.Count ?? 0) > 0,
                "The producer schema compatibility set is required.");
            return computed;
        }

        public static string SealReceipt(ProducerBuildProvenanceReceipt receipt)
        {
            Require(receipt != null, "A producer build provenance receipt is required.");
            receipt.ContentSha256 = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    receipt,
                    nameof(ProducerBuildProvenanceReceipt.ContentSha256)));
            return receipt.ContentSha256;
        }

        public static string ValidateReceiptAndComputeDigest(
            ProducerBuildProvenanceReceipt receipt,
            ProducerBuildProvenanceManifest manifest)
        {
            Require(receipt != null, "A producer build provenance receipt is required.");
            Require(string.Equals(receipt.SchemaVersion, ReceiptSchemaVersion, StringComparison.Ordinal),
                "The producer build provenance receipt schema is unsupported.");
            ValidateDigest(receipt.ContentSha256, "producer provenance receipt content seal");
            var computedReceipt = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    receipt,
                    nameof(ProducerBuildProvenanceReceipt.ContentSha256)));
            Require(DigestEquals(receipt.ContentSha256, computedReceipt),
                "The producer provenance receipt content seal is stale or corrupt.");
            var manifestDigest = ValidateManifestAndComputeDigest(manifest);
            Require(DigestEquals(receipt.ManifestDigestSha256, manifestDigest),
                "The producer provenance receipt is foreign to its manifest.");
            Require(IsKnownStatus(receipt.SourceBindingStatus)
                && IsKnownStatus(receipt.ArtifactHashStatus)
                && IsKnownStatus(receipt.RebuildStatus)
                && IsKnownStatus(receipt.HistoricalBinaryMatchStatus)
                && IsKnownStatus(receipt.VerificationStatus),
                "The producer provenance verification status is unsupported.");
            if (string.Equals(receipt.VerificationStatus, Verified, StringComparison.Ordinal))
            {
                Require(!string.IsNullOrWhiteSpace(receipt.VerifierId),
                    "A verified producer provenance receipt requires verifier identity.");
                ValidateImplementationRef(receipt.VerifierImplementationRef, "verifier implementation ref");
                Require(receipt.VerifiedAtUtc != default,
                    "A verified producer provenance receipt requires verifier time.");
                Require(AllVerified(receipt),
                    "Producer provenance cannot be VERIFIED while an applicable gate is not VERIFIED.");
                ValidateArtifacts(receipt.ActualArtifacts, "verified producer artifact");
                Require((receipt.OpenedBytes?.Count ?? 0) > 0,
                    "Verified producer provenance requires independently reopened bytes.");
                foreach (var artifact in receipt.OpenedBytes)
                {
                    ValidateArtifactReference(artifact, "opened producer bytes");
                }
            }
            return computedReceipt;
        }

        public static ProducerBuildProvenanceReceipt CreateUnverified(
            ProducerBuildProvenanceManifest manifest,
            string reason = null)
        {
            var manifestDigest = ValidateManifestAndComputeDigest(manifest);
            var receipt = new ProducerBuildProvenanceReceipt
            {
                ManifestDigestSha256 = manifestDigest,
                VerificationStatus = Unverified,
                SourceBindingStatus = Unverified,
                ArtifactHashStatus = Unverified,
                RebuildStatus = Unverified,
                HistoricalBinaryMatchStatus = Unverified,
                ReasonCodes = new List<string> { reason ?? "PROVENANCE_VERIFIER_NOT_SUPPLIED" }
            };
            SealReceipt(receipt);
            return receipt;
        }

        private static bool AllVerified(ProducerBuildProvenanceReceipt receipt)
        {
            return string.Equals(receipt.SourceBindingStatus, Verified, StringComparison.Ordinal)
                && string.Equals(receipt.ArtifactHashStatus, Verified, StringComparison.Ordinal)
                && string.Equals(receipt.RebuildStatus, Verified, StringComparison.Ordinal)
                && string.Equals(receipt.HistoricalBinaryMatchStatus, Verified, StringComparison.Ordinal);
        }

        private static void ValidateArtifacts(IEnumerable<ProducerBuildArtifact> artifacts, string name)
        {
            var values = artifacts?.ToList() ?? new List<ProducerBuildArtifact>();
            Require(values.Count > 0, "At least one " + name + " is required.");
            Require(values.All(value => value != null
                && !string.IsNullOrWhiteSpace(value.Role)
                && !string.IsNullOrWhiteSpace(value.Path)
                && value.Length > 0),
                "A " + name + " is incomplete.");
            foreach (var value in values)
            {
                ValidateDigest(value.Sha256, name + " digest");
            }
        }

        private static void ValidateArtifactReference(
            PnP.Framework.Migration.Verification.NativePageRuntime.NativePageRuntimeArtifactReference artifact,
            string name)
        {
            Require(artifact != null && artifact.Length > 0 && !string.IsNullOrWhiteSpace(artifact.MediaType),
                "The " + name + " reference is incomplete.");
            ValidateDigest(artifact.Sha256, name + " digest");
        }

        private static void ValidateDigestList(IEnumerable<string> values, string name)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                ValidateDigest(value, name);
            }
        }

        private static bool IsKnownStatus(string value)
        {
            return string.Equals(value, Unverified, StringComparison.Ordinal)
                || string.Equals(value, Verified, StringComparison.Ordinal)
                || string.Equals(value, Rejected, StringComparison.Ordinal);
        }

        private static bool IsGitObject(string value)
        {
            return value != null && value.Length == 40 && value.All(IsHex);
        }

        private static void ValidateImplementationRef(string value, string name)
        {
            Require(value != null && value.Length == 40 && value.All(IsHex),
                "The producer " + name + " must be a full Git SHA.");
        }

        private static void ValidateOptionalDigest(string value, string name)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidateDigest(value, name);
            }
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex),
                "The " + name + " must be a SHA-256 digest.");
        }

        private static bool DigestEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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
