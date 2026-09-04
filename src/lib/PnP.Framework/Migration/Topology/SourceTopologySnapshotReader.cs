using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Topology.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace PnP.Framework.Migration.Topology
{
    public sealed class SourceTopologyCaptureResult
    {
        public SourceSiteCollectionSnapshot SourceTopology { get; set; }

        public PathDerivedSourceTopologyEvidence PathDerivedEvidence { get; set; }
    }

    public static class SourceTopologySnapshotReader
    {
        public static SourceSiteCollectionSnapshot CaptureRequiredWebClosure(ClientContext context, IEnumerable<Guid> requiredWebIds)
        {
            var required = requiredWebIds?.Where(value => value != Guid.Empty).Distinct().ToArray();
            var result = CaptureRequiredWebClosureWithEvidence(
                context,
                required,
                required?.FirstOrDefault() ?? Guid.Empty);
            if (result.SourceTopology == null)
            {
                throw new InvalidDataException("Source topology closure could not be captured; consume the separately retained path-derived evidence with an explicit primary source Web.");
            }
            return result.SourceTopology;
        }

        public static SourceTopologyCaptureResult CaptureRequiredWebClosureWithEvidence(
            ClientContext context,
            IEnumerable<Guid> requiredWebIds,
            Guid primaryLeafWebId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (requiredWebIds == null)
            {
                throw new ArgumentNullException(nameof(requiredWebIds));
            }

            var site = context.Site;
            var root = site.RootWeb;
            context.Load(site, value => value.Id, value => value.ServerRelativeUrl);
            LoadWeb(context, root);
            context.ExecuteQueryRetry();
            var requested = requiredWebIds.Where(value => value != Guid.Empty).Distinct().ToList();
            if (!requested.Contains(root.Id))
            {
                requested.Add(root.Id);
            }
            if (primaryLeafWebId == Guid.Empty)
            {
                primaryLeafWebId = root.Id;
            }
            if (!requested.Contains(primaryLeafWebId))
            {
                throw new ArgumentException("An explicit primary leaf Web from the required source Web set is required.", nameof(primaryLeafWebId));
            }

            var captured = new Dictionary<Guid, SourceWebSnapshot>();
            var objects = new Dictionary<Guid, Web>();
            foreach (var webId in requested)
            {
                var web = webId == root.Id ? root : site.OpenWebById(webId);
                if (webId != root.Id)
                {
                    LoadWeb(context, web);
                }
                objects[webId] = web;
            }
            context.ExecuteQueryRetry();
            foreach (var pair in objects)
            {
                captured[pair.Key] = ToSnapshot(site.Id, root.Url, pair.Value, null);
            }

            while (captured.Values.Any(value => value.WebId != root.Id && !value.ParentWebId.HasValue))
            {
                var unresolved = captured.Values.Where(value => value.WebId != root.Id && !value.ParentWebId.HasValue).ToArray();
                var parents = new Dictionary<Guid, WebInformation>();
                foreach (var child in unresolved)
                {
                    var parent = objects[child.WebId].ParentWeb;
                    context.Load(parent, value => value.Id, value => value.ServerRelativeUrl, value => value.Title, value => value.WebTemplate, value => value.Configuration);
                    parents[child.WebId] = parent;
                }
                try
                {
                    context.ExecuteQueryRetry();
                }
                catch (Exception exception) when (TryCreateAuthorizationEvidence(
                    exception,
                    context.Url.TrimEnd('/') + "/_vti_bin/client.svc/ProcessQuery",
                    out var authorizationEvidence))
                {
                    if (!captured.TryGetValue(primaryLeafWebId, out var leaf))
                    {
                        throw new InvalidDataException("The primary source leaf Web was not captured before ancestor lookup failed.", exception);
                    }
                    return new SourceTopologyCaptureResult
                    {
                        PathDerivedEvidence = PathDerivedSourceTopologyEvidenceFactory.CreateAuthorizationBlocked(
                            captured[root.Id],
                            leaf,
                            authorizationEvidence.Operation,
                            context.Url.TrimEnd('/') + "/_vti_bin/client.svc/ProcessQuery",
                            authorizationEvidence,
                            new[] { "Ancestor Web identity, title, template, and configuration were not captured." })
                    };
                }
                foreach (var child in unresolved)
                {
                    var parent = parents[child.WebId];
                    if (parent.Id == Guid.Empty)
                    {
                        throw new InvalidDataException("Source Web '" + child.WebUrl + "' did not resolve a direct parent Web.");
                    }
                    child.ParentWebId = parent.Id;
                    if (captured.ContainsKey(parent.Id))
                    {
                        continue;
                    }
                    var parentUrl = new Uri(new Uri(root.Url).GetLeftPart(UriPartial.Authority) + parent.ServerRelativeUrl).AbsoluteUri.TrimEnd('/');
                    captured[parent.Id] = new SourceWebSnapshot
                    {
                        SiteId = site.Id,
                        WebId = parent.Id,
                        SiteCollectionUrl = root.Url.TrimEnd('/'),
                        WebUrl = parentUrl,
                        ServerRelativeUrl = parent.ServerRelativeUrl,
                        Title = parent.Title,
                        WebTemplate = parent.WebTemplate,
                        Configuration = parent.Configuration
                    };
                    if (parent.Id != root.Id)
                    {
                        var parentWeb = site.OpenWebById(parent.Id);
                        objects[parent.Id] = parentWeb;
                    }
                }
            }

            return new SourceTopologyCaptureResult
            {
                SourceTopology = new SourceSiteCollectionSnapshot
                {
                    SiteId = site.Id,
                    SiteCollectionUrl = root.Url.TrimEnd('/'),
                    ServerRelativeUrl = site.ServerRelativeUrl,
                    RootWebId = root.Id,
                    Webs = captured.Values.OrderBy(value => PathDepth(value.ServerRelativeUrl)).ThenBy(value => value.ServerRelativeUrl, StringComparer.OrdinalIgnoreCase).ToList(),
                    Availability = EvidenceAvailability.Captured
                }
            };
        }

        private static bool TryCreateAuthorizationEvidence(
            Exception exception,
            string requestUri,
            out LiteralHttpAuthorizationEvidence evidence)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is WebException webException
                    && webException.Response is HttpWebResponse response
                    && ((int)response.StatusCode == 401 || (int)response.StatusCode == 403))
                {
                    evidence = LiteralHttpAuthorizationEvidence.Create(
                        "ReadSourceParentWeb",
                        response.ResponseUri?.AbsoluteUri ?? requestUri,
                        (int)response.StatusCode,
                        DateTimeOffset.UtcNow);
                    return true;
                }
            }
            evidence = null;
            return false;
        }

        private static void LoadWeb(ClientContext context, Web web)
        {
            context.Load(web, value => value.Id, value => value.Url, value => value.ServerRelativeUrl, value => value.Title, value => value.WebTemplate, value => value.Configuration);
        }

        private static SourceWebSnapshot ToSnapshot(Guid siteId, string siteUrl, Web web, Guid? parentWebId)
        {
            return new SourceWebSnapshot
            {
                SiteId = siteId,
                WebId = web.Id,
                ParentWebId = parentWebId,
                SiteCollectionUrl = siteUrl.TrimEnd('/'),
                WebUrl = web.Url.TrimEnd('/'),
                ServerRelativeUrl = web.ServerRelativeUrl,
                Title = web.Title,
                WebTemplate = web.WebTemplate,
                Configuration = web.Configuration
            };
        }

        private static int PathDepth(string value)
        {
            return (value ?? string.Empty).Count(character => character == '/');
        }
    }
}
