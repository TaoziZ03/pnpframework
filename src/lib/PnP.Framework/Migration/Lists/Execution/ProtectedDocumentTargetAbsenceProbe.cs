using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Lists.Planning;
using System;
using System.IO;
using System.Net;

namespace PnP.Framework.Migration.Lists.Execution
{
    internal static class ProtectedDocumentTargetAbsenceProbe
    {
        public static ListProtectedDocumentExclusionVerification Inspect(
            ClientContext context,
            ListProtectedDocumentExclusionPlan exclusion)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (exclusion == null || string.IsNullOrWhiteSpace(exclusion.TargetServerRelativeUrl))
            {
                throw new ArgumentException("A protected-document exclusion with a target path is required.", nameof(exclusion));
            }

            try
            {
                var file = context.Web.GetFileByServerRelativePath(
                    ResourcePath.FromDecodedUrl(exclusion.TargetServerRelativeUrl));
                context.Load(file, value => value.Exists);
                context.ExecuteQueryRetry();
                return Result(
                    exclusion,
                    file.Exists
                        ? ProtectedDocumentTargetAbsenceStatus.Present
                        : ProtectedDocumentTargetAbsenceStatus.Absent,
                    null,
                    file.Exists
                        ? "The excluded protected document exists at the target path."
                        : "The excluded protected document is absent from the target path.");
            }
            catch (Exception exception) when (
                exception is ServerException
                || exception is ClientRequestException
                || exception is WebException
                || exception is TimeoutException)
            {
                var statusCode = ExtractHttpStatusCode(exception);
                var status = Classify(exception, statusCode);
                return Result(exclusion, status, statusCode, exception.Message);
            }
        }

        internal static ProtectedDocumentTargetAbsenceStatus ClassifyHttpStatus(int httpStatusCode)
        {
            if (httpStatusCode == 404)
            {
                return ProtectedDocumentTargetAbsenceStatus.Absent;
            }
            if (httpStatusCode == 401 || httpStatusCode == 403)
            {
                return ProtectedDocumentTargetAbsenceStatus.AuthorizationBlocked;
            }
            if (httpStatusCode == 408
                || httpStatusCode == 409
                || httpStatusCode == 423
                || httpStatusCode == 429
                || httpStatusCode >= 500 && httpStatusCode <= 599)
            {
                return ProtectedDocumentTargetAbsenceStatus.RetryableFailure;
            }
            return ProtectedDocumentTargetAbsenceStatus.Failed;
        }

        internal static ProtectedDocumentTargetAbsenceStatus ClassifyException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }
            return Classify(exception, ExtractHttpStatusCode(exception));
        }

        private static ProtectedDocumentTargetAbsenceStatus Classify(
            Exception exception,
            int? httpStatusCode)
        {
            if (httpStatusCode.HasValue)
            {
                return ClassifyHttpStatus(httpStatusCode.Value);
            }
            if (IsMissing(exception))
            {
                return ProtectedDocumentTargetAbsenceStatus.Absent;
            }
            if (IsRetryableTransport(exception))
            {
                return ProtectedDocumentTargetAbsenceStatus.RetryableFailure;
            }
            return ProtectedDocumentTargetAbsenceStatus.Failed;
        }

        private static bool IsMissing(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is FileNotFoundException)
                {
                    return true;
                }
                if (current is ServerException server
                    && (server.ServerErrorCode == -2147024894
                        || string.Equals(server.ServerErrorTypeName, "System.IO.FileNotFoundException", StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsRetryableTransport(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is TimeoutException)
                {
                    return true;
                }
                if (current is WebException web
                    && (web.Status == WebExceptionStatus.Timeout
                        || web.Status == WebExceptionStatus.ConnectFailure
                        || web.Status == WebExceptionStatus.ConnectionClosed
                        || web.Status == WebExceptionStatus.NameResolutionFailure
                        || web.Status == WebExceptionStatus.ReceiveFailure
                        || web.Status == WebExceptionStatus.SendFailure))
                {
                    return true;
                }
            }
            return false;
        }

        internal static int? ExtractHttpStatusCode(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (!(current is WebException web) || web.Response == null)
                {
                    continue;
                }
                if (web.Response is HttpWebResponse response)
                {
                    return (int)response.StatusCode;
                }
                // WebException.Response is typed as WebResponse. Supporting a
                // StatusCode property keeps nested HTTP evidence testable while
                // preserving the ordinary HttpWebResponse path in production.
                var property = web.Response.GetType().GetProperty("StatusCode");
                var value = property?.GetValue(web.Response);
                if (value is HttpStatusCode status)
                {
                    return (int)status;
                }
                if (value is int numeric)
                {
                    return numeric;
                }
            }
            return null;
        }

        private static ListProtectedDocumentExclusionVerification Result(
            ListProtectedDocumentExclusionPlan exclusion,
            ProtectedDocumentTargetAbsenceStatus status,
            int? httpStatusCode,
            string diagnostic)
        {
            return new ListProtectedDocumentExclusionVerification
            {
                SourceItemId = exclusion.SourceItemId,
                SourceServerRelativeUrl = exclusion.SourceServerRelativeUrl,
                TargetServerRelativeUrl = exclusion.TargetServerRelativeUrl,
                PolicyId = exclusion.PolicyId,
                CaptureDecisionDigest = exclusion.CaptureDecisionDigest,
                Status = status,
                HttpStatusCode = httpStatusCode,
                Diagnostic = diagnostic
            };
        }
    }
}
