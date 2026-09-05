using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.ContentTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace PnP.Framework.Migration.Schema.ContentTypes
{
    internal static class ContentTypeClosureSnapshotReader
    {
        private const int MaximumMemberCaptureAttempts = 3;

        public static IList<ContentTypeSchemaSnapshot> Read(
            ClientContext context,
            Web sourceWeb,
            IEnumerable<ListContentTypeSnapshot> listContentTypes,
            ICollection<string> diagnostics)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (sourceWeb == null)
            {
                throw new ArgumentNullException(nameof(sourceWeb));
            }

            var roots = (listContentTypes ?? Enumerable.Empty<ListContentTypeSnapshot>())
                .Select(value => value.ParentId)
                .Where(value => !string.IsNullOrWhiteSpace(value) && !ContentTypeRuntimeCatalog.IsTargetRuntime(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Read(
                roots,
                sourceWeb.Url,
                contentTypeId => CaptureMember(context, sourceWeb.Url, contentTypeId),
                diagnostics);
        }

        internal static IList<ContentTypeSchemaSnapshot> Read(
            IEnumerable<string> roots,
            string sourceWebUrl,
            Func<string, ContentTypeSchemaSnapshot> captureMember,
            ICollection<string> diagnostics)
        {
            if (captureMember == null)
            {
                throw new ArgumentNullException(nameof(captureMember));
            }

            var pending = new Queue<string>((roots ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value) && !ContentTypeRuntimeCatalog.IsTargetRuntime(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ContentTypeSchemaSnapshot>();
            while (pending.Count > 0)
            {
                var contentTypeId = pending.Dequeue();
                if (!observed.Add(contentTypeId) || ContentTypeRuntimeCatalog.IsTargetRuntime(contentTypeId))
                {
                    continue;
                }

                ContentTypeSchemaSnapshot snapshot;
                try
                {
                    snapshot = captureMember(contentTypeId);
                }
                catch (Exception exception) when (IsMemberCaptureFailure(exception))
                {
                    snapshot = Partial(contentTypeId, sourceWebUrl, exception);
                }
                result.Add(snapshot);
                foreach (var diagnostic in snapshot.Diagnostics ?? Enumerable.Empty<string>())
                {
                    diagnostics?.Add("Site content type '" + contentTypeId + "': " + diagnostic);
                }

                var parentId = snapshot.ParentContentTypeId;
                if (!string.IsNullOrWhiteSpace(parentId)
                    && !ContentTypeRuntimeCatalog.IsTargetRuntime(parentId)
                    && !observed.Contains(parentId))
                {
                    pending.Enqueue(parentId);
                }
            }
            return result.OrderBy(value => value.ContentTypeId.Length)
                .ThenBy(value => value.ContentTypeId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ContentTypeSchemaSnapshot CaptureMember(
            ClientContext context,
            string sourceWebUrl,
            string contentTypeId)
        {
            Exception lastFailure = null;
            for (var attempt = 1; attempt <= MaximumMemberCaptureAttempts; attempt++)
            {
                try
                {
                    using (var isolatedContext = context.Clone(sourceWebUrl))
                    {
                        var localDiagnostics = new List<string>();
                        var snapshot = ContentTypeSchemaSnapshotReader.ReadAllFieldLinks(
                            isolatedContext,
                            isolatedContext.Web,
                            contentTypeId,
                            localDiagnostics);
                        if (snapshot == null
                            || snapshot.EvidenceState == ContentTypeSchemaEvidenceState.Missing
                            || string.IsNullOrWhiteSpace(snapshot.ContentTypeId))
                        {
                            throw new InvalidOperationException(
                                "The isolated site content type capture did not return identity evidence.");
                        }
                        snapshot.Diagnostics = localDiagnostics;
                        snapshot.SourceWebUrl = ScopeOwnerUrl(snapshot.SourceScope, sourceWebUrl);
                        return snapshot;
                    }
                }
                catch (Exception exception) when (IsMemberCaptureFailure(exception))
                {
                    lastFailure = exception;
                }
            }

            throw lastFailure ?? new InvalidOperationException(
                "The isolated site content type capture failed without an exception.");
        }

        private static bool IsMemberCaptureFailure(Exception exception)
        {
            return exception is ServerException
                || exception is InvalidOperationException && !(exception is WebException);
        }

        private static ContentTypeSchemaSnapshot Partial(
            string contentTypeId,
            string sourceWebUrl,
            Exception exception)
        {
            var diagnostic = "ContentTypeClosureMemberCapturePartial: contentTypeId=" + contentTypeId
                + "; exceptionType=" + exception.GetType().FullName
                + "; attempts=" + MaximumMemberCaptureAttempts + ".";
            var sourceScope = Uri.UnescapeDataString(new Uri(sourceWebUrl).AbsolutePath).TrimEnd('/');
            return new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Partial,
                SourceWebUrl = sourceWebUrl.TrimEnd('/'),
                SourceScope = sourceScope.Length == 0 ? "/" : sourceScope,
                ContentTypeId = contentTypeId,
                Availability = EvidenceAvailability.Partial,
                Diagnostics = new List<string> { diagnostic }
            };
        }

        private static string ScopeOwnerUrl(string scope, string fallbackWebUrl)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                return fallbackWebUrl.TrimEnd('/');
            }
            Uri absolute;
            if (Uri.TryCreate(scope, UriKind.Absolute, out absolute)
                && (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return absolute.AbsoluteUri.TrimEnd('/');
            }
            var origin = new Uri(fallbackWebUrl).GetLeftPart(UriPartial.Authority).TrimEnd('/');
            return new Uri(origin + "/" + scope.Trim('/')).AbsoluteUri.TrimEnd('/');
        }
    }
}
