using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Verification
{
    public static class RuntimeVerificationReceiptValidator
    {
        public static void ValidateEvidence(
            RuntimeVerificationReceipt receipt,
            RuntimeVerificationManifest requirementsManifest,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            Require(receipt != null, "A runtime verification receipt is required.");
            Require(requirementsManifest != null && requirementsManifest.Requirements != null,
                "A runtime verification manifest is required.");
            Require(artifactStore != null, "An exact runtime artifact resolver is required.");
            ValidateImplementationRef(expectedImplementationRef);
            Require(string.Equals(receipt.ImplementationRef, expectedImplementationRef, StringComparison.OrdinalIgnoreCase),
                "The runtime receipt implementation ref is missing or foreign.");

            var manifestDigest = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonical(requirementsManifest));
            Require(string.Equals(
                    receipt.RequirementsManifestDigestSha256,
                    manifestDigest,
                    StringComparison.OrdinalIgnoreCase),
                "The runtime receipt requirements manifest is missing, foreign, or stale.");

            ValidateBrowserContext(receipt.BrowserContext, receipt.CompletedAtUtc);
            var results = receipt.Results ?? new List<RuntimeVerificationResult>();
            foreach (var result in results)
            {
                Require(result != null, "A runtime result is required.");
                Require(string.Equals(result.ImplementationRef, receipt.ImplementationRef, StringComparison.OrdinalIgnoreCase),
                    "Runtime result implementation ref is missing or foreign.");
                Require(string.Equals(result.BrowserContextId, receipt.BrowserContext.BrowserContextId, StringComparison.Ordinal),
                    "Runtime result browser context is missing or foreign.");
                Require(IsSafeRelativeLocator(result.EvidenceArtifactLocator),
                    "Runtime HTML evidence locator is missing or unsafe.");
                ValidateArtifact(
                    artifactStore,
                    result.EvidenceArtifactSha256,
                    result.EvidenceArtifactLength,
                    "runtime HTML evidence");

                Require(result.Http != null
                    && string.Equals(result.Http.Method, "GET", StringComparison.Ordinal)
                    && string.Equals(result.Http.RequestedUrl, receipt.TargetIdentity, StringComparison.Ordinal)
                    && string.Equals(result.Http.FinalUrl, receipt.TargetIdentity, StringComparison.Ordinal)
                    && result.Http.StatusCode >= 100
                    && result.Http.StatusCode <= 599
                    && result.Http.CapturedAtUtc >= receipt.BrowserContext.FirstNavigationAtUtc
                    && result.Http.CapturedAtUtc <= receipt.CompletedAtUtc,
                    "Runtime HTTP evidence is missing, foreign, or stale.");
                ValidateDigest(result.Http.ResponseHeadersDigestSha256, "runtime response headers digest");
                Require(!string.IsNullOrWhiteSpace(result.Http.ContentType),
                    "Runtime HTTP content type evidence is required.");

                Require(result.Cache != null
                    && string.Equals(result.Cache.RequestMode, "no-store", StringComparison.Ordinal)
                    && result.Cache.CacheDisabled
                    && ContainsDirective(result.Cache.RequestCacheControl, "no-store")
                    && ContainsDirective(result.Cache.RequestPragma, "no-cache")
                    && !result.Cache.FromDiskCache
                    && !result.Cache.FromServiceWorker,
                    "Runtime cache:no-store request/response evidence is missing or inconsistent.");

                Require(IsSafeRelativeLocator(result.DomProbeArtifactLocator),
                    "Runtime DOM probe locator is missing or unsafe.");
                ValidateArtifact(
                    artifactStore,
                    result.DomProbeArtifactSha256,
                    result.DomProbeArtifactLength,
                    "runtime DOM probe evidence");

                var screenshotFields = new[]
                {
                    !string.IsNullOrWhiteSpace(result.ScreenshotArtifactSha256),
                    result.ScreenshotArtifactLength.HasValue,
                    !string.IsNullOrWhiteSpace(result.ScreenshotArtifactLocator)
                };
                Require(screenshotFields.All(value => value) || screenshotFields.All(value => !value),
                    "Runtime screenshot evidence is partial.");
                if (screenshotFields[0])
                {
                    Require(IsSafeRelativeLocator(result.ScreenshotArtifactLocator),
                        "Runtime screenshot locator is unsafe.");
                    ValidateArtifact(
                        artifactStore,
                        result.ScreenshotArtifactSha256,
                        result.ScreenshotArtifactLength.Value,
                        "runtime screenshot evidence");
                }

                if (result.Passed)
                {
                    Require(result.Http.StatusCode >= 200
                        && result.Http.StatusCode < 300
                        && result.Http.ContentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0,
                        "HTTP status alone cannot establish a runtime pass.");
                }
            }
        }

        private static void ValidateBrowserContext(
            RuntimeBrowserContextIdentity browserContext,
            DateTimeOffset completedAtUtc)
        {
            Require(browserContext != null
                && browserContext.FreshContext
                && !string.IsNullOrWhiteSpace(browserContext.BrowserProduct)
                && !string.IsNullOrWhiteSpace(browserContext.BrowserVersion)
                && !string.IsNullOrWhiteSpace(browserContext.ProtocolVersion)
                && !string.IsNullOrWhiteSpace(browserContext.BrowserContextId)
                && !string.IsNullOrWhiteSpace(browserContext.TargetId),
                "A fresh isolated browser context identity is required.");
            ValidateDigest(browserContext.ProfileIdentitySha256, "browser profile identity digest");
            Require(browserContext.CreatedAtUtc != default
                && browserContext.FirstNavigationAtUtc >= browserContext.CreatedAtUtc
                && completedAtUtc >= browserContext.FirstNavigationAtUtc,
                "The fresh browser-context timeline is invalid.");
        }

        private static void ValidateArtifact(
            IMigrationArtifactStore artifactStore,
            string expectedDigest,
            long expectedLength,
            string name)
        {
            ValidateDigest(expectedDigest, name + " digest");
            Require(expectedLength > 0, "The " + name + " length must be positive.");
            Require(artifactStore.Contains(expectedDigest), "The " + name + " is missing.");
            try
            {
                using (var input = artifactStore.OpenRead(expectedDigest))
                using (var copy = new MemoryStream())
                {
                    input.CopyTo(copy);
                    var bytes = copy.ToArray();
                    Require(bytes.LongLength == expectedLength, "The " + name + " length is stale or corrupt.");
                    Require(string.Equals(
                            MigrationDigest.ComputeSha256(bytes),
                            expectedDigest,
                            StringComparison.OrdinalIgnoreCase),
                        "The " + name + " bytes are foreign or corrupt.");
                }
            }
            catch (FileNotFoundException)
            {
                throw new InvalidDataException("The " + name + " is missing.");
            }
        }

        private static bool IsSafeRelativeLocator(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !Path.IsPathRooted(value)
                && !value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => string.Equals(part, "..", StringComparison.Ordinal));
        }

        private static bool ContainsDirective(string value, string directive)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Split(',').Any(part => string.Equals(part.Trim(), directive, StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateImplementationRef(string value)
        {
            Require(value != null && value.Length == 40 && value.All(IsHex),
                "The runtime implementation ref must be a full Git SHA.");
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
