using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public sealed class ClassicWikiRuntimeEvidencePolicy : INativePageRuntimeEvidencePolicy
    {
        public string ProfileId => NativePageRuntimeContract.ClassicWikiProfile;

        public string PolicyVersion => NativePageRuntimeContract.ClassicWikiPolicyVersion;

        public void ValidateBinding(NativePageRuntimeBinding binding, IMigrationArtifactStore artifactStore)
        {
            if (binding == null)
            {
                throw new InvalidDataException("A Classic Wiki native runtime binding is required.");
            }
            if (!string.Equals(binding.ProfileId, ProfileId, StringComparison.Ordinal)
                || !string.Equals(binding.PolicyVersion, PolicyVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The runtime binding profile/policy is foreign to Classic Wiki.");
            }
            if (artifactStore == null)
            {
                throw new InvalidDataException("The Classic Wiki runtime policy requires an exact artifact resolver.");
            }
        }

        public bool VerifyResult(
            NativePageRuntimeBinding binding,
            RuntimeVerificationResult result,
            IMigrationArtifactStore artifactStore)
        {
            ValidateBinding(binding, artifactStore);
            if (result == null || result.Http == null)
            {
                return false;
            }
            if (string.Equals(result.RequirementId, NativePageRuntimeContract.ScreenshotRequirementId, StringComparison.Ordinal))
            {
                return HasValidScreenshot(result, artifactStore);
            }

            var html = ReadText(artifactStore, result.EvidenceArtifactSha256);
            var dom = ReadText(artifactStore, result.DomProbeArtifactSha256);
            if (!IsSuccessfulHtml(result)
                || ContainsDenialOrErrorShell(html)
                || !TryReadDom(
                    dom,
                    out var surface,
                    out var readyState,
                    out var errorShell,
                    out var observedUrl,
                    out var authoredContent))
            {
                return false;
            }

            if (string.Equals(result.RequirementId, NativePageRuntimeContract.ErrorShellRequirementId, StringComparison.Ordinal))
            {
                return !errorShell;
            }
            if (string.Equals(result.RequirementId, NativePageRuntimeContract.RuntimeRequirementId, StringComparison.Ordinal))
            {
                return !errorShell
                    && string.Equals(surface, "classic-wiki", StringComparison.Ordinal)
                    && string.Equals(readyState, "complete", StringComparison.Ordinal)
                    && string.Equals(observedUrl, result.Http.FinalUrl, StringComparison.Ordinal)
                    && string.Equals(
                        MigrationDigest.ComputeSha256(authoredContent),
                        binding.ExpectedAuthoredContentSha256,
                        StringComparison.OrdinalIgnoreCase)
                    && ContainsAuthoredSurface(html, authoredContent);
            }
            return false;
        }

        private static bool HasValidScreenshot(RuntimeVerificationResult result, IMigrationArtifactStore store)
        {
            if (string.IsNullOrWhiteSpace(result.ScreenshotArtifactSha256)
                || !result.ScreenshotArtifactLength.HasValue
                || result.ScreenshotArtifactLength.Value <= 0
                || !store.Contains(result.ScreenshotArtifactSha256))
            {
                return false;
            }
            var bytes = ReadBytes(store, result.ScreenshotArtifactSha256);
            return bytes != null
                && bytes.LongLength == result.ScreenshotArtifactLength.Value
                && string.Equals(
                    MigrationDigest.ComputeSha256(bytes),
                    result.ScreenshotArtifactSha256,
                    StringComparison.OrdinalIgnoreCase)
                && IsSupportedImage(bytes);
        }

        private static bool IsSuccessfulHtml(RuntimeVerificationResult result)
        {
            return result.Http.StatusCode >= 200
                && result.Http.StatusCode < 300
                && result.Http.ContentType?.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsDenialOrErrorShell(string value)
        {
            var text = DecodeHtml(value);
            return text.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("errorshell", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("error-shell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadDom(
            string json,
            out string surface,
            out string readyState,
            out bool errorShell,
            out string observedUrl,
            out string authoredContent)
        {
            surface = null;
            readyState = null;
            errorShell = true;
            observedUrl = null;
            authoredContent = null;
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !TryGetString(root, "schemaVersion", out var schemaVersion)
                        || !string.Equals(schemaVersion, "pnp-classic-wiki-runtime-dom/v1", StringComparison.Ordinal)
                        || !TryGetString(root, "surface", out surface)
                        || !TryGetString(root, "readyState", out readyState)
                        || !TryGetString(root, "observedUrl", out observedUrl)
                        || !TryGetString(root, "authoredContent", out authoredContent)
                        || !root.TryGetProperty("errorShell", out var errorValue)
                        || errorValue.ValueKind != JsonValueKind.False && errorValue.ValueKind != JsonValueKind.True)
                    {
                        return false;
                    }
                    errorShell = errorValue.GetBoolean();
                    return root.EnumerateObject().Count() == 6;
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryGetString(JsonElement root, string name, out string value)
        {
            value = null;
            return root.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value = property.GetString());
        }

        private static string DecodeHtml(string value)
        {
            var current = value ?? string.Empty;
            for (var index = 0; index < 3; index++)
            {
                var decoded = WebUtility.HtmlDecode(current);
                if (string.Equals(decoded, current, StringComparison.Ordinal))
                {
                    break;
                }
                current = decoded;
            }
            return current;
        }

        private static bool ContainsAuthoredSurface(string html, string authoredContent)
        {
            return !string.IsNullOrWhiteSpace(authoredContent)
                && DecodeHtml(html).IndexOf(DecodeHtml(authoredContent), StringComparison.Ordinal) >= 0;
        }

        private static bool IsSupportedImage(byte[] bytes)
        {
            return bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47
                && bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a && bytes[7] == 0x0a
                || bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        }

        private static byte[] ReadBytes(IMigrationArtifactStore store, string digest)
        {
            if (store == null || string.IsNullOrWhiteSpace(digest) || !store.Contains(digest))
            {
                return null;
            }
            using (var input = store.OpenRead(digest))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static string ReadText(IMigrationArtifactStore store, string digest)
        {
            if (store == null || string.IsNullOrWhiteSpace(digest) || !store.Contains(digest))
            {
                return null;
            }
            using (var input = store.OpenRead(digest))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                return Encoding.UTF8.GetString(copy.ToArray());
            }
        }
    }
}
