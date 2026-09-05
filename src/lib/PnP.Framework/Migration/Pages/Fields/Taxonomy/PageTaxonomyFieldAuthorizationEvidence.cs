using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using System;
using System.IO;
using System.Net;
using System.Net.Http;

namespace PnP.Framework.Migration.Pages.Fields.Taxonomy
{
    internal static class PageTaxonomyFieldAuthorizationEvidence
    {
        public const string SourceBindingCaptureOperation = "capture-page-taxonomy-field-binding";

        public static bool TryCreate(
            Exception exception,
            string sourceWebUrl,
            PageFieldValueSnapshot field,
            out BoundLiteralHttpAuthorizationEvidence evidence)
        {
            var requestUri = CsomRequestUri(sourceWebUrl);
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is WebException webException
                    && webException.Status == WebExceptionStatus.ProtocolError
                    && webException.Response is HttpWebResponse response)
                {
                    var statusCode = (int)response.StatusCode;
                    if (statusCode == 401 || statusCode == 403)
                    {
                        var literal = LiteralHttpAuthorizationEvidence.Create(
                            SourceBindingCaptureOperation,
                            requestUri,
                            statusCode,
                            DateTimeOffset.UtcNow);
                        evidence = BoundLiteralHttpAuthorizationEvidence.Create(
                            FieldActionId(field),
                            SourceBindingCaptureOperation,
                            requestUri,
                            literal);
                        return true;
                    }
                }

                if (current is HttpRequestException httpException
                    && TryGetHttpRequestStatusCode(httpException, out var httpStatusCode)
                    && (httpStatusCode == 401 || httpStatusCode == 403))
                {
                    var literal = LiteralHttpAuthorizationEvidence.Create(
                        SourceBindingCaptureOperation,
                        requestUri,
                        httpStatusCode,
                        DateTimeOffset.UtcNow);
                    evidence = BoundLiteralHttpAuthorizationEvidence.Create(
                        FieldActionId(field),
                        SourceBindingCaptureOperation,
                        requestUri,
                        literal);
                    return true;
                }
            }

            evidence = null;
            return false;
        }

        public static void ValidateSource(
            PageIdentity source,
            PageFieldValueSnapshot field)
        {
            var evidence = field?.AuthorizationEvidence;
            if (evidence == null)
            {
                return;
            }

            var expectedRequestUri = CsomRequestUri(source?.WebUrl);
            var expectedActionId = FieldActionId(field);
            BoundLiteralHttpAuthorizationEvidence.Validate(
                evidence,
                expectedActionId,
                SourceBindingCaptureOperation,
                new Uri(expectedRequestUri).Authority,
                expectedRequestUri);
            if (source == null
                || field == null
                || field.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(field.InternalName)
                || !PageTaxonomyRelationshipEvidence.IsTaxonomyField(field)
                || field.CaptureStatus != PageCaptureStatus.CapturedWithLimitations
                || PageTaxonomyRelationshipEvidence.HasCompleteFieldBinding(field)
                || !string.Equals(
                    evidence.ActionId,
                    expectedActionId,
                    StringComparison.Ordinal)
                || !SameUri(evidence.ExpectedRequestUri, expectedRequestUri))
            {
                throw new InvalidDataException(
                    $"Field '{field?.InternalName}' has authorization evidence that is not bound to its exact source taxonomy binding capture request.");
            }
        }

        public static string CsomRequestUri(string webUrl)
        {
            if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var web)
                || web.Scheme != Uri.UriSchemeHttp && web.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException(
                    "Taxonomy field authorization evidence requires an absolute source Web URL.");
            }

            return web.AbsoluteUri.TrimEnd('/') + "/_vti_bin/client.svc/ProcessQuery";
        }

        public static string FieldActionId(PageFieldValueSnapshot field)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.InternalName))
            {
                throw new InvalidDataException(
                    "Taxonomy field authorization evidence requires an exact field internal name.");
            }
            return "field:" + field.InternalName;
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
