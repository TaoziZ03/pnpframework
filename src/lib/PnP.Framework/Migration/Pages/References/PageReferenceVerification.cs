using Microsoft.SharePoint.Client;
using PnP.Framework.Http;
using PnP.Framework.Migration.Pages.Capture;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace PnP.Framework.Migration.Pages.References
{
    public sealed class PageReferenceTargetReadState
    {
        public bool Exists { get; set; }

        public int? HttpStatusCode { get; set; }

        public string MediaType { get; set; }

        public long? ContentLength { get; set; }

        public string ContentSha256 { get; set; }

        public bool EvidenceComplete { get; set; } = true;

        public IList<string> Diagnostics { get; set; } = new List<string>();
    }

    public sealed class PageReferenceVerificationResult
    {
        public string SnapshotDependencyId { get; set; }

        public PageReferenceDisposition Disposition { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public bool ConsumerMatched { get; set; }

        public bool TargetMatched { get; set; }

        public bool Passed => ConsumerMatched && TargetMatched;

        public PageReferenceTargetReadState TargetRead { get; set; }

        public IList<string> Diagnostics { get; set; } = new List<string>();
    }

    internal static class PageReferenceVerification
    {
        private const long MaximumTargetReferenceBytes = 16 * 1024 * 1024;
        private const int MediaSniffBytes = 512;

        public static IList<PageReferenceVerificationResult> Verify(
            IEnumerable<PageReferenceSnapshot> snapshots,
            IEnumerable<PageReferenceAction> actions,
            Func<PageReferenceSnapshot, bool> consumerMatcher,
            Func<PageReferenceSnapshot, PageReferenceAction, PageReferenceTargetReadState> targetReader,
            Uri approvedTargetAuthority = null)
        {
            var snapshotById = (snapshots ?? Array.Empty<PageReferenceSnapshot>())
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Id))
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var results = new List<PageReferenceVerificationResult>();
            var targetReads = new Dictionary<string, PageReferenceTargetReadState>(StringComparer.OrdinalIgnoreCase);
            PageReferenceTargetReadState ReadTarget(PageReferenceSnapshot snapshot, PageReferenceAction action)
            {
                if (string.IsNullOrWhiteSpace(action?.TargetServerRelativeUrl))
                {
                    return targetReader?.Invoke(snapshot, action);
                }
                if (!targetReads.TryGetValue(action.TargetServerRelativeUrl, out var state))
                {
                    state = targetReader?.Invoke(snapshot, action);
                    targetReads[action.TargetServerRelativeUrl] = state;
                }
                return state;
            }
            foreach (var action in actions ?? Array.Empty<PageReferenceAction>())
            {
                if (action == null
                    || string.IsNullOrWhiteSpace(action.SnapshotDependencyId)
                    || !snapshotById.TryGetValue(action.SnapshotDependencyId ?? string.Empty, out var snapshot))
                {
                    results.Add(Failure(
                        action,
                        "The reference action does not resolve exactly one captured dependency."));
                    continue;
                }

                var result = new PageReferenceVerificationResult
                {
                    SnapshotDependencyId = snapshot.Id,
                    Disposition = action.Disposition,
                    TargetServerRelativeUrl = action.TargetServerRelativeUrl,
                    ConsumerMatched = consumerMatcher?.Invoke(snapshot) == true
                };
                if (!result.ConsumerMatched)
                {
                    result.Diagnostics.Add(
                        $"The persisted consumer '{snapshot.Consumer ?? "unknown"}' did not verify the captured reference.");
                }

                switch (action.Disposition)
                {
                    case PageReferenceDisposition.PreserveExternal:
                        result.TargetMatched = string.IsNullOrWhiteSpace(action.TargetServerRelativeUrl)
                            && string.Equals(action.TargetAbsoluteUrl, snapshot.SourceAbsoluteUrl, StringComparison.Ordinal);
                        if (!result.TargetMatched)
                        {
                            result.Diagnostics.Add("PreserveExternal must retain the exact captured source URL and no target-relative path.");
                        }
                        break;
                    case PageReferenceDisposition.RewriteToTarget:
                        VerifyRewrite(snapshot, action, ReadTarget, approvedTargetAuthority, result);
                        break;
                    case PageReferenceDisposition.MaterializeAtTarget:
                        VerifyMaterialization(snapshot, action, ReadTarget, approvedTargetAuthority, result);
                        break;
                    default:
                        result.TargetMatched = false;
                        result.Diagnostics.Add(
                            $"Disposition '{action.Disposition}' is not an executable reference action and cannot be verified as completed.");
                        break;
                }
                results.Add(result);
            }
            return results;
        }

        public static int ExpectedMaterializationCount(
            IEnumerable<PageReferenceAction> actions)
        {
            return (actions ?? Array.Empty<PageReferenceAction>())
                .Where(value => value != null
                    && value.Disposition == PageReferenceDisposition.MaterializeAtTarget
                    && !string.IsNullOrWhiteSpace(value.TargetServerRelativeUrl))
                .Select(value => value.TargetServerRelativeUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        public static long TargetReadLimit(PageReferenceSnapshot snapshot)
        {
            return Math.Max(MaximumTargetReferenceBytes, snapshot?.ContentLength ?? 0);
        }

        public static PageReferenceVerificationResult InspectPlan(
            PageReferenceSnapshot snapshot,
            PageReferenceAction action,
            Func<PageReferenceSnapshot, PageReferenceAction, PageReferenceTargetReadState> targetReader,
            Uri approvedTargetAuthority = null)
        {
            if (snapshot == null || action == null
                || !string.Equals(snapshot.Id, action.SnapshotDependencyId, StringComparison.Ordinal))
            {
                return Failure(action, "The reference action does not resolve its captured dependency.");
            }

            if (action.Disposition == PageReferenceDisposition.MaterializeAtTarget)
            {
                var result = new PageReferenceVerificationResult
                {
                    SnapshotDependencyId = snapshot.Id,
                    Disposition = action.Disposition,
                    TargetServerRelativeUrl = action.TargetServerRelativeUrl,
                    ConsumerMatched = true,
                    TargetMatched = snapshot.ContentBase64 != null
                        && !string.IsNullOrWhiteSpace(snapshot.ContentSha256)
                        && TargetCoordinatesMatch(action, approvedTargetAuthority, null)
                };
                if (!result.TargetMatched)
                {
                    result.Diagnostics.Add("MaterializeAtTarget requires captured bytes, SHA-256, and an exact target path.");
                }
                else
                {
                    result.Diagnostics.Add(
                        "Planning verifies captured bytes and target coordinates; target-library write capability and exact bytes require mutation-time fresh readback.");
                }
                return result;
            }

            if (action.Disposition == PageReferenceDisposition.Block
                || action.Disposition == PageReferenceDisposition.Delegate)
            {
                return new PageReferenceVerificationResult
                {
                    SnapshotDependencyId = snapshot.Id,
                    Disposition = action.Disposition,
                    TargetServerRelativeUrl = action.TargetServerRelativeUrl,
                    ConsumerMatched = true,
                    TargetMatched = true,
                    Diagnostics = new List<string>
                    {
                        $"The reference is explicitly {action.Disposition} and is not claimed as an executable target rewrite."
                    }
                };
            }

            return Verify(
                new[] { snapshot },
                new[] { action },
                _ => true,
                targetReader,
                approvedTargetAuthority).Single();
        }

        public static PageReferenceTargetReadState ReadTarget(
            ClientContext context,
            Web owner,
            string serverRelativeUrl,
            long maximumBytes = MaximumTargetReferenceBytes)
        {
            if (context == null || string.IsNullOrWhiteSpace(serverRelativeUrl))
            {
                return new PageReferenceTargetReadState
                {
                    Exists = false,
                    Diagnostics = new List<string> { "The target reference path or owner Web is unavailable." }
                };
            }

            if (PageReferenceSnapshotReader.IsSharePointRuntimePath(serverRelativeUrl))
            {
                return ReadHttpTarget(context, serverRelativeUrl, maximumBytes);
            }
            if (owner == null)
            {
                return new PageReferenceTargetReadState
                {
                    Exists = false,
                    Diagnostics = new List<string> { "The target reference owner Web is unavailable." }
                };
            }

            var file = owner.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(serverRelativeUrl));
            var stream = file.OpenBinaryStream();
            context.Load(file, value => value.Exists, value => value.Length, value => value.Name);
            try
            {
                context.ExecuteQueryRetry();
            }
            catch (ServerException exception)
            {
                return new PageReferenceTargetReadState
                {
                    Exists = false,
                    HttpStatusCode = IsMissing(exception)
                        ? 404
                        : exception.ServerErrorCode == -2147024891
                            ? 403
                            : (int?)null,
                    EvidenceComplete = false,
                    Diagnostics = new List<string> { exception.Message }
                };
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException)
            {
                return new PageReferenceTargetReadState
                {
                    Exists = false,
                    EvidenceComplete = false,
                    Diagnostics = new List<string> { exception.Message }
                };
            }

            if (!file.Exists || stream.Value == null)
            {
                return new PageReferenceTargetReadState
                {
                    Exists = false,
                    HttpStatusCode = 404,
                    Diagnostics = new List<string> { "The target reference returned no file or binary stream." }
                };
            }
            try
            {
                if (file.Length > maximumBytes)
                {
                    stream.Value.Dispose();
                    return new PageReferenceTargetReadState
                    {
                        Exists = true,
                        HttpStatusCode = 200,
                        ContentLength = file.Length,
                        EvidenceComplete = false,
                        Diagnostics = new List<string>
                        {
                            $"The target reference is {file.Length} bytes, above the {maximumBytes}-byte verification limit."
                        }
                    };
                }

                using (stream.Value)
                {
                    return ReadStreamEvidence(stream.Value, file.Name, null, file.Length, maximumBytes);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException)
            {
                stream.Value?.Dispose();
                return new PageReferenceTargetReadState
                {
                    Exists = false,
                    EvidenceComplete = false,
                    Diagnostics = new List<string> { exception.Message }
                };
            }
        }

        private static PageReferenceTargetReadState ReadHttpTarget(
            ClientContext context,
            string serverRelativeUrl,
            long maximumBytes)
        {
            var requestUri = new Uri(context.Url).GetLeftPart(UriPartial.Authority)
                + PagePath.Encode(serverRelativeUrl);
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    PnPHttpClient.AuthenticateRequestAsync(request, context).GetAwaiter().GetResult();
                    var client = PnPHttpClient.Instance.GetHttpClient(context);
                    return ReadHttpTargetResponse(
                        client,
                        request,
                        Path.GetFileName(Uri.UnescapeDataString(new Uri(requestUri).AbsolutePath)),
                        maximumBytes);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException
                || exception is System.Threading.Tasks.TaskCanceledException
                || exception is IOException
                || exception is InvalidOperationException)
            {
                return new PageReferenceTargetReadState
                {
                    Exists = false,
                    EvidenceComplete = false,
                    Diagnostics = new List<string> { exception.Message }
                };
            }
        }

        internal static PageReferenceTargetReadState ReadHttpTargetResponse(
            HttpClient client,
            HttpRequestMessage request,
            string fileName,
            long maximumBytes)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using (var response = client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                var statusCode = (int)response.StatusCode;
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!response.IsSuccessStatusCode)
                {
                    return new PageReferenceTargetReadState
                    {
                        Exists = false,
                        HttpStatusCode = statusCode,
                        MediaType = mediaType,
                        Diagnostics = new List<string>
                        {
                            $"The target runtime reference returned HTTP {statusCode}."
                        }
                    };
                }

                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength.HasValue && declaredLength.Value > maximumBytes)
                {
                    return new PageReferenceTargetReadState
                    {
                        Exists = true,
                        HttpStatusCode = statusCode,
                        MediaType = mediaType,
                        ContentLength = declaredLength,
                        EvidenceComplete = false,
                        Diagnostics = new List<string>
                        {
                            $"The target runtime reference is {declaredLength.Value} bytes, above the {maximumBytes}-byte verification limit."
                        }
                    };
                }

                using (var content = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                {
                    var state = ReadStreamEvidence(
                        content,
                        fileName,
                        mediaType,
                        declaredLength,
                        maximumBytes);
                    state.HttpStatusCode = statusCode;
                    return state;
                }
            }
        }

        private static PageReferenceTargetReadState ReadStreamEvidence(
            Stream stream,
            string fileName,
            string mediaType,
            long? declaredLength,
            long maximumBytes)
        {
            var buffer = new byte[81920];
            var prefix = new byte[MediaSniffBytes];
            var prefixLength = 0;
            long length = 0;
            using (var algorithm = SHA256.Create())
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    length += read;
                    if (length > maximumBytes)
                    {
                        return new PageReferenceTargetReadState
                        {
                            Exists = true,
                            ContentLength = declaredLength ?? length,
                            MediaType = mediaType,
                            EvidenceComplete = false,
                            Diagnostics = new List<string>
                            {
                                $"The target reference exceeded the {maximumBytes}-byte verification limit while reading."
                            }
                        };
                    }

                    if (prefixLength < prefix.Length)
                    {
                        var copy = Math.Min(read, prefix.Length - prefixLength);
                        Buffer.BlockCopy(buffer, 0, prefix, prefixLength, copy);
                        prefixLength += copy;
                    }
                    algorithm.TransformBlock(buffer, 0, read, buffer, 0);
                }
                algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return new PageReferenceTargetReadState
                {
                    Exists = true,
                    HttpStatusCode = 200,
                    MediaType = ResolveMediaType(mediaType, fileName, prefix, prefixLength),
                    ContentLength = length,
                    ContentSha256 = string.Concat(algorithm.Hash.Select(value =>
                        value.ToString("x2", CultureInfo.InvariantCulture)))
                };
            }
        }

        private static void VerifyRewrite(
            PageReferenceSnapshot snapshot,
            PageReferenceAction action,
            Func<PageReferenceSnapshot, PageReferenceAction, PageReferenceTargetReadState> targetReader,
            Uri approvedTargetAuthority,
            PageReferenceVerificationResult result)
        {
            if (!TargetCoordinatesMatch(action, approvedTargetAuthority, result.Diagnostics))
            {
                return;
            }
            if (!snapshot.IsRenderableResource)
            {
                result.TargetMatched = true;
                return;
            }

            result.TargetRead = targetReader?.Invoke(snapshot, action);
            result.TargetMatched = TargetFileMatches(snapshot, result.TargetRead, requireDigest: false, result.Diagnostics);
        }

        private static void VerifyMaterialization(
            PageReferenceSnapshot snapshot,
            PageReferenceAction action,
            Func<PageReferenceSnapshot, PageReferenceAction, PageReferenceTargetReadState> targetReader,
            Uri approvedTargetAuthority,
            PageReferenceVerificationResult result)
        {
            if (snapshot.ContentBase64 == null
                || string.IsNullOrWhiteSpace(snapshot.ContentSha256)
                || !TargetCoordinatesMatch(action, approvedTargetAuthority, result.Diagnostics))
            {
                if (snapshot.ContentBase64 == null || string.IsNullOrWhiteSpace(snapshot.ContentSha256))
                {
                    result.Diagnostics.Add("MaterializeAtTarget requires captured bytes and SHA-256.");
                }
                return;
            }

            result.TargetRead = targetReader?.Invoke(snapshot, action);
            result.TargetMatched = TargetFileMatches(snapshot, result.TargetRead, requireDigest: true, result.Diagnostics);
        }

        private static bool TargetFileMatches(
            PageReferenceSnapshot snapshot,
            PageReferenceTargetReadState state,
            bool requireDigest,
            ICollection<string> diagnostics)
        {
            if (state == null)
            {
                diagnostics.Add("Fresh target reference evidence is unavailable.");
                return false;
            }
            if (!state.EvidenceComplete)
            {
                diagnostics.Add("Fresh target reference evidence is incomplete.");
                return false;
            }
            if (state.HttpStatusCode.HasValue
                && (state.HttpStatusCode.Value < 200 || state.HttpStatusCode.Value >= 300))
            {
                diagnostics.Add($"The fresh target reference returned HTTP {state.HttpStatusCode.Value}.");
                return false;
            }
            if (!state.Exists)
            {
                diagnostics.Add("The fresh target reference does not exist.");
                return false;
            }
            if (!MediaTypeMatches(snapshot.Kind, state.MediaType))
            {
                diagnostics.Add(
                    $"The fresh target reference media type '{state.MediaType ?? "unknown"}' is incompatible with {snapshot.Kind}.");
                return false;
            }
            if (requireDigest || !string.IsNullOrWhiteSpace(snapshot.ContentSha256))
            {
                if (string.IsNullOrWhiteSpace(state.ContentSha256)
                    || !string.Equals(state.ContentSha256, snapshot.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add("The fresh target reference SHA-256 differs from the captured source bytes.");
                    return false;
                }
            }
            if (requireDigest && state.ContentLength.HasValue
                && state.ContentLength.Value != snapshot.ContentLength)
            {
                diagnostics.Add("The fresh target reference length differs from the captured source bytes.");
                return false;
            }
            return true;
        }

        private static bool TargetCoordinatesMatch(
            PageReferenceAction action,
            Uri approvedTargetAuthority,
            ICollection<string> diagnostics)
        {
            if (action == null
                || string.IsNullOrWhiteSpace(action.TargetAbsoluteUrl)
                || string.IsNullOrWhiteSpace(action.TargetServerRelativeUrl)
                || !Uri.TryCreate(action.TargetAbsoluteUrl, UriKind.Absolute, out var absolute)
                || absolute.Scheme != Uri.UriSchemeHttps && absolute.Scheme != Uri.UriSchemeHttp)
            {
                diagnostics?.Add("The reference action requires exact target absolute and server-relative HTTP(S) URLs.");
                return false;
            }

            var absolutePath = Uri.UnescapeDataString(absolute.AbsolutePath);
            var targetPath = Uri.UnescapeDataString(action.TargetServerRelativeUrl);
            if (!string.Equals(absolutePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics?.Add("The target absolute URL path does not equal the target server-relative path.");
                return false;
            }
            if (approvedTargetAuthority != null
                && (!string.Equals(absolute.Scheme, approvedTargetAuthority.Scheme, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(absolute.Authority, approvedTargetAuthority.Authority, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics?.Add("The target absolute URL authority differs from the approved target tenant authority.");
                return false;
            }
            return true;
        }

        private static bool MediaTypeMatches(PageReferenceKind kind, string mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return true;
            }

            var normalized = mediaType.Split(';')[0].Trim().ToLowerInvariant();
            if (normalized == "text/html" || normalized == "application/xhtml+xml")
            {
                return false;
            }
            switch (kind)
            {
                case PageReferenceKind.Script:
                    return normalized.Contains("javascript")
                        || normalized.Contains("ecmascript")
                        || normalized == "text/plain"
                        || normalized == "application/octet-stream";
                case PageReferenceKind.StyleSheet:
                    return normalized == "text/css"
                        || normalized == "text/plain"
                        || normalized == "application/octet-stream";
                case PageReferenceKind.Image:
                    return normalized.StartsWith("image/", StringComparison.Ordinal)
                        || normalized == "application/octet-stream";
                default:
                    return true;
            }
        }

        internal static string InferMediaType(string fileName, byte[] bytes, int length)
        {
            var prefix = Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>(), 0, Math.Min(length, bytes?.Length ?? 0))
                .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return "text/html";
            }
            if ((fileName ?? string.Empty).EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                return "application/javascript";
            }
            if ((fileName ?? string.Empty).EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                return "text/css";
            }
            return null;
        }

        internal static string ResolveMediaType(
            string declaredMediaType,
            string fileName,
            byte[] bytes,
            int length)
        {
            var inferred = InferMediaType(fileName, bytes, length);
            return string.Equals(inferred, "text/html", StringComparison.OrdinalIgnoreCase)
                ? inferred
                : declaredMediaType ?? inferred;
        }

        private static PageReferenceVerificationResult Failure(
            PageReferenceAction action,
            string diagnostic)
        {
            return new PageReferenceVerificationResult
            {
                SnapshotDependencyId = action?.SnapshotDependencyId,
                Disposition = action?.Disposition ?? PageReferenceDisposition.Block,
                TargetServerRelativeUrl = action?.TargetServerRelativeUrl,
                ConsumerMatched = false,
                TargetMatched = false,
                Diagnostics = new List<string> { diagnostic }
            };
        }

        private static bool IsMissing(ServerException exception)
        {
            return string.Equals(exception.ServerErrorTypeName, "System.IO.FileNotFoundException", StringComparison.Ordinal)
                || exception.ServerErrorCode == -2147024894;
        }
    }
}
