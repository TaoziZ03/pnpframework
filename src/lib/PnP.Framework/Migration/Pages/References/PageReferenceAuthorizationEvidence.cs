using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Pages.Capture;
using System;
using System.IO;
using System.Net;
using System.Net.Http;

namespace PnP.Framework.Migration.Pages.References
{
    internal static class PageReferenceAuthorizationEvidence
    {
        public const string SourceCaptureOperation = "capture-page-reference-payload";

        public const string TargetCsomProbeOperation = "probe-target-page-reference-csom";

        public const string TargetHttpProbeOperation = "probe-target-page-reference-http";

        public static bool TryCreate(
            Exception exception,
            string operation,
            string requestUri,
            out LiteralHttpAuthorizationEvidence evidence)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is WebException webException
                    && webException.Status == WebExceptionStatus.ProtocolError
                    && webException.Response is HttpWebResponse response)
                {
                    var statusCode = (int)response.StatusCode;
                    if (statusCode == 401 || statusCode == 403)
                    {
                        evidence = LiteralHttpAuthorizationEvidence.Create(
                            operation,
                            response.ResponseUri?.AbsoluteUri ?? requestUri,
                            statusCode,
                            DateTimeOffset.UtcNow);
                        return true;
                    }
                }

                if (current is HttpRequestException httpException
                    && TryGetHttpRequestStatusCode(httpException, out var httpStatusCode)
                    && (httpStatusCode == 401 || httpStatusCode == 403))
                {
                    evidence = LiteralHttpAuthorizationEvidence.Create(
                        operation,
                        requestUri,
                        httpStatusCode,
                        DateTimeOffset.UtcNow);
                    return true;
                }
            }

            evidence = null;
            return false;
        }

        public static bool IsExpectedReadFailure(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is ServerException
                    || current is WebException
                    || current is HttpRequestException
                    || current is IOException
                    || current is InvalidOperationException
                    || current is TimeoutException
                    || current is System.Threading.Tasks.TaskCanceledException)
                {
                    return true;
                }
            }
            return false;
        }

        public static string CsomRequestUri(string webUrl)
        {
            if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var web)
                || web.Scheme != Uri.UriSchemeHttp && web.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Reference authorization evidence requires an absolute source or target Web URL.");
            }

            return web.AbsoluteUri.TrimEnd('/') + "/_vti_bin/client.svc/ProcessQuery";
        }

        public static string HttpRequestUri(string webUrl, string serverRelativeUrl)
        {
            if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var web)
                || web.Scheme != Uri.UriSchemeHttp && web.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrWhiteSpace(serverRelativeUrl))
            {
                throw new InvalidDataException("Reference HTTP authorization evidence requires an absolute target Web URL and target path.");
            }

            return web.GetLeftPart(UriPartial.Authority) + PagePath.Encode(serverRelativeUrl);
        }

        public static void ValidateSource(PageIdentity source, PageReferenceSnapshot reference)
        {
            var evidence = reference?.AuthorizationEvidence;
            if (evidence == null)
            {
                return;
            }

            LiteralHttpAuthorizationEvidence.Validate(evidence);
            if (source == null
                || reference == null
                || string.IsNullOrWhiteSpace(reference.Id)
                || !Uri.TryCreate(source.WebUrl, UriKind.Absolute, out var sourceWebUri)
                || !Uri.TryCreate(reference.SourceAbsoluteUrl, UriKind.Absolute, out var sourceReferenceUri)
                || sourceReferenceUri.Scheme != Uri.UriSchemeHttp && sourceReferenceUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(sourceWebUri.Authority, sourceReferenceUri.Authority, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Reference authorization evidence requires a same-authority source reference identity.");
            }
            var expectedCsomRequestUri = CsomRequestUri(source?.WebUrl);
            var exactReferenceRequest = SameUri(evidence.RequestUri, reference.SourceAbsoluteUrl);
            if (!string.Equals(evidence.Operation, SourceCaptureOperation, StringComparison.Ordinal)
                || !exactReferenceRequest && !SameUri(evidence.RequestUri, expectedCsomRequestUri)
                || !reference.IsRenderableResource
                || reference.CaptureStatus != PageCaptureStatus.Failed
                || reference.ContentBase64 != null
                || !string.IsNullOrWhiteSpace(reference.ContentSha256)
                || reference.ContentLength != 0)
            {
                throw new InvalidDataException(
                    $"Reference '{reference?.Id}' has authorization evidence that is not bound to its exact source capture operation and request URI.");
            }
        }

        public static void ValidateTarget(
            string targetWebUrl,
            PageReferenceAction action,
            PageReferenceTargetReadState state)
        {
            if (state == null)
            {
                return;
            }

            var evidence = state.AuthorizationEvidence;
            var hasAuthorizationStatus = state.HttpStatusCode == 401 || state.HttpStatusCode == 403;
            if (evidence == null)
            {
                if (hasAuthorizationStatus)
                {
                    throw new InvalidDataException(
                        $"Reference '{action?.SnapshotDependencyId}' claims HTTP {state.HttpStatusCode} without retained literal authorization evidence.");
                }
                return;
            }

            LiteralHttpAuthorizationEvidence.Validate(evidence);
            var expectedRequestUri = evidence.Operation == TargetHttpProbeOperation
                ? HttpRequestUri(targetWebUrl, action?.TargetServerRelativeUrl)
                : evidence.Operation == TargetCsomProbeOperation
                    ? CsomRequestUri(targetWebUrl)
                    : null;
            if (expectedRequestUri == null
                || !SameUri(evidence.RequestUri, expectedRequestUri)
                || state.HttpStatusCode != evidence.HttpStatusCode
                || state.Exists
                || state.EvidenceComplete
                || state.ContentLength.HasValue
                || !string.IsNullOrWhiteSpace(state.ContentSha256)
                || action == null
                || (action.Disposition != PageReferenceDisposition.RewriteToTarget
                    && action.Disposition != PageReferenceDisposition.MaterializeAtTarget))
            {
                throw new InvalidDataException(
                    $"Reference '{action?.SnapshotDependencyId}' has authorization evidence that is not bound to its exact target probe operation, request URI, and HTTP status.");
            }
        }

        private static bool TryGetHttpRequestStatusCode(
            HttpRequestException exception,
            out int statusCode)
        {
            statusCode = 0;
            var property = typeof(HttpRequestException).GetProperty("StatusCode");
            var value = property?.GetValue(exception);
            if (value is HttpStatusCode httpStatusCode)
            {
                statusCode = (int)httpStatusCode;
                return true;
            }
            if (value is int numericStatusCode)
            {
                statusCode = numericStatusCode;
                return true;
            }
            return false;
        }

        private static bool SameUri(string left, string right)
        {
            return Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
                && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
                && string.Equals(leftUri.AbsoluteUri, rightUri.AbsoluteUri, StringComparison.Ordinal);
        }
    }
}
