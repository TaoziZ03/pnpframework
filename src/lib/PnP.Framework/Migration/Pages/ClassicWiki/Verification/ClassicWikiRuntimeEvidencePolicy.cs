using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.IO;
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
                || !TryReadDom(dom, out var surface, out var readyState, out var errorShell, out var authoredDigest))
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
                    && string.Equals(authoredDigest, binding.ExpectedAuthoredContentSha256, StringComparison.OrdinalIgnoreCase);
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
            using (var input = store.OpenRead(result.ScreenshotArtifactSha256))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                var bytes = copy.ToArray();
                return bytes.LongLength == result.ScreenshotArtifactLength.Value
                    && string.Equals(
                        MigrationDigest.ComputeSha256(bytes),
                        result.ScreenshotArtifactSha256,
                        StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsSuccessfulHtml(RuntimeVerificationResult result)
        {
            return result.Http.StatusCode >= 200
                && result.Http.StatusCode < 300
                && result.Http.ContentType?.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsDenialOrErrorShell(string value)
        {
            var text = value ?? string.Empty;
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
            out string authoredDigest)
        {
            surface = null;
            readyState = null;
            errorShell = true;
            authoredDigest = null;
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    surface = root.TryGetProperty("surface", out var surfaceValue) ? surfaceValue.GetString() : null;
                    readyState = root.TryGetProperty("readyState", out var readyValue) ? readyValue.GetString() : null;
                    errorShell = !root.TryGetProperty("errorShell", out var errorValue) || errorValue.GetBoolean();
                    authoredDigest = root.TryGetProperty("authoredContentSha256", out var digestValue) ? digestValue.GetString() : null;
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
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
