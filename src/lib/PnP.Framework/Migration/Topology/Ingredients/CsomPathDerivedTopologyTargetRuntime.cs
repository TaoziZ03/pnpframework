using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    /// <summary>
    /// CSOM implementation for target-only path containers. It never infers
    /// authorization from exception text: only an actual HTTP response can
    /// produce literal 401/403 evidence.
    /// </summary>
    public sealed class CsomPathDerivedTopologyTargetRuntime : IPathDerivedTopologyTargetRuntime
    {
        private readonly ClientContext anchorContext;

        public CsomPathDerivedTopologyTargetRuntime(ClientContext anchorContext)
        {
            this.anchorContext = anchorContext ?? throw new ArgumentNullException(nameof(anchorContext));
        }

        public IList<PathDerivedTargetWebObservation> Inspect(
            IEnumerable<TargetWebContainerIngredientPlan> containers)
        {
            var requested = (containers ?? Enumerable.Empty<TargetWebContainerIngredientPlan>())
                .OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl))
                .ThenBy(value => value.TargetSlotKey, StringComparer.Ordinal)
                .ToArray();
            var requestedKeys = new HashSet<string>(requested.Select(value => value.GlobalActionKey), StringComparer.Ordinal);
            var byAction = new Dictionary<string, PathDerivedTargetWebObservation>(StringComparer.Ordinal);
            var result = new List<PathDerivedTargetWebObservation>();
            foreach (var container in requested)
            {
                if (!string.IsNullOrWhiteSpace(container.ParentGlobalActionKey)
                    && requestedKeys.Contains(container.ParentGlobalActionKey)
                    && byAction.TryGetValue(container.ParentGlobalActionKey, out var parent)
                    && (!parent.Exists || parent.HttpStatusCode.HasValue || parent.InspectionFailed || parent.IdentityConflict))
                {
                    continue;
                }
                var observation = InspectOne(container);
                byAction[container.GlobalActionKey] = observation;
                result.Add(observation);
            }
            return result;
        }

        public PathDerivedTargetWebObservation Create(TargetWebContainerIngredientPlan container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }
            using (var context = anchorContext.Clone(container.TargetParentWebUrl))
            {
                var parent = context.Web;
                context.Load(context.Site, value => value.Id);
                context.Load(parent, value => value.Id, value => value.Url, value => value.ServerRelativeUrl);
                context.ExecuteQueryRetry();
                ValidateParent(container, parent.Url, parent.ServerRelativeUrl);

                var target = parent.Webs.Add(new WebCreationInformation
                {
                    Url = SharedTopologyPath.Leaf(container.TargetServerRelativeUrl),
                    Title = container.Provisioning.Title,
                    Description = PathDerivedTopologyTargetAnalyzer.InterruptedCreateDescription(container),
                    Language = container.Provisioning.Language,
                    UseSamePermissionsAsParentSite = container.Provisioning.UseSamePermissionsAsParentWeb,
                    WebTemplate = NormalizeTemplate(container.Provisioning.Template, container.Provisioning.Configuration)
                });
                context.Load(
                    target,
                    value => value.Id,
                    value => value.Url,
                    value => value.ServerRelativeUrl,
                    value => value.Title,
                    value => value.Description,
                    value => value.WebTemplate,
                    value => value.Configuration,
                    value => value.HasUniqueRoleAssignments,
                    value => value.AllProperties);
                context.ExecuteQueryRetry();
                ApplyOwnership(context, target, container);
            }
            return Inspect(new[] { container }).Single();
        }

        public PathDerivedTargetWebObservation RecoverOwnership(TargetWebContainerIngredientPlan container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }
            var observation = Inspect(new[] { container }).Single();
            var probe = PathDerivedTopologyTargetAnalyzer.AnalyzeContainer(container, observation);
            if (probe.State == TargetWebContainerState.ReuseOwned)
            {
                return observation;
            }
            if (probe.State != TargetWebContainerState.RecoverInterruptedCreate || !probe.TargetWebId.HasValue)
            {
                throw new InvalidOperationException("Only an exact freshly observed interrupted create may be claimed.");
            }

            using (var context = anchorContext.Clone(container.TargetParentWebUrl))
            {
                var parent = context.Web;
                var target = context.Site.OpenWebById(probe.TargetWebId.Value);
                context.Load(context.Site, value => value.Id);
                context.Load(parent, value => value.Id, value => value.Url, value => value.ServerRelativeUrl);
                context.Load(
                    target,
                    value => value.Id,
                    value => value.Url,
                    value => value.ServerRelativeUrl,
                    value => value.Title,
                    value => value.Description,
                    value => value.WebTemplate,
                    value => value.Configuration,
                    value => value.HasUniqueRoleAssignments,
                    value => value.AllProperties);
                context.ExecuteQueryRetry();
                ValidateParent(container, parent.Url, parent.ServerRelativeUrl);
                var refreshed = ToObservation(container, context.Site.Id, parent.Id, target);
                if (PathDerivedTopologyTargetAnalyzer.AnalyzeContainer(container, refreshed).State
                    != TargetWebContainerState.RecoverInterruptedCreate)
                {
                    throw new InvalidOperationException("The interrupted-create fingerprint changed before ownership could be written.");
                }
                ApplyOwnership(context, target, container);
            }
            return Inspect(new[] { container }).Single();
        }

        private PathDerivedTargetWebObservation InspectOne(TargetWebContainerIngredientPlan container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }
            try
            {
                using (var context = anchorContext.Clone(container.TargetParentWebUrl))
                {
                    var parent = context.Web;
                    var targetPath = container.TargetServerRelativeUrl;
                    context.Load(context.Site, value => value.Id);
                    context.Load(parent, value => value.Id, value => value.Url, value => value.ServerRelativeUrl);
                    context.Load(parent.Webs, values => values
                        .Where(value => value.ServerRelativeUrl == targetPath)
                        .Include(
                            value => value.Id,
                            value => value.Url,
                            value => value.ServerRelativeUrl,
                            value => value.Title,
                            value => value.Description,
                            value => value.WebTemplate,
                            value => value.Configuration,
                            value => value.HasUniqueRoleAssignments,
                            value => value.AllProperties));
                    context.ExecuteQueryRetry();
                    if (!ParentMatches(container, parent.Url, parent.ServerRelativeUrl))
                    {
                        return new PathDerivedTargetWebObservation
                        {
                            GlobalActionKey = container.GlobalActionKey,
                            IdentityConflict = true,
                            Diagnostic = "The target connection did not resolve the approved direct parent Web."
                        };
                    }
                    var candidates = parent.Webs.AsEnumerable().ToArray();
                    if (candidates.Length == 0)
                    {
                        return new PathDerivedTargetWebObservation
                        {
                            GlobalActionKey = container.GlobalActionKey,
                            Exists = false,
                            TargetSiteId = context.Site.Id,
                            TargetParentWebId = parent.Id,
                            TargetWebUrl = container.TargetWebUrl,
                            TargetServerRelativeUrl = container.TargetServerRelativeUrl
                        };
                    }
                    if (candidates.Length != 1)
                    {
                        return new PathDerivedTargetWebObservation
                        {
                            GlobalActionKey = container.GlobalActionKey,
                            IdentityConflict = true,
                            Diagnostic = "The direct parent returned more than one Web for the exact target path."
                        };
                    }
                    return ToObservation(container, context.Site.Id, parent.Id, candidates[0]);
                }
            }
            catch (Exception exception)
            {
                if (TryGetHttpResponse(exception, out var response))
                {
                    var status = (int)response.StatusCode;
                    return new PathDerivedTargetWebObservation
                    {
                        GlobalActionKey = container.GlobalActionKey,
                        HttpStatusCode = status,
                        AuthorizationEvidence = status == 401 || status == 403
                            ? LiteralHttpAuthorizationEvidence.Create(
                                "InspectPathDerivedTargetWeb",
                                response.ResponseUri?.AbsoluteUri ?? container.TargetParentWebUrl,
                                status,
                                DateTimeOffset.UtcNow)
                            : null,
                        Diagnostic = "Target Web inspection returned literal HTTP "
                            + status.ToString(CultureInfo.InvariantCulture) + "."
                    };
                }
                return new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = container.GlobalActionKey,
                    InspectionFailed = true,
                    Diagnostic = exception.GetType().Name + ": " + exception.Message
                };
            }
        }

        private static PathDerivedTargetWebObservation ToObservation(
            TargetWebContainerIngredientPlan container,
            Guid targetSiteId,
            Guid targetParentWebId,
            Web target)
        {
            return new PathDerivedTargetWebObservation
            {
                GlobalActionKey = container.GlobalActionKey,
                Exists = true,
                TargetSiteId = targetSiteId,
                TargetWebId = target.Id,
                TargetParentWebId = targetParentWebId,
                TargetWebUrl = target.Url,
                TargetServerRelativeUrl = target.ServerRelativeUrl,
                ExistingTitle = target.Title,
                ExistingDescription = target.Description,
                ExistingTemplate = target.WebTemplate,
                ExistingConfiguration = target.Configuration,
                ExistingHasUniqueRoleAssignments = target.HasUniqueRoleAssignments,
                ExistingOriginalIdentifier = Property(target.AllProperties, TopologyPlanner.WebOriginalIdentifierPropertyName),
                ExistingMappingDigest = Property(target.AllProperties, TopologyPlanner.WebPlanDigestPropertyName)
            };
        }

        private static void ApplyOwnership(
            ClientContext context,
            Web target,
            TargetWebContainerIngredientPlan container)
        {
            var existingOriginal = Property(target.AllProperties, TopologyPlanner.WebOriginalIdentifierPropertyName);
            var existingDigest = Property(target.AllProperties, TopologyPlanner.WebPlanDigestPropertyName);
            if (!string.IsNullOrWhiteSpace(existingOriginal)
                && !string.Equals(existingOriginal, container.OriginalIdentifier, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(existingDigest)
                    && !string.Equals(existingDigest, container.ActionSignatureDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Target Web ownership provenance conflicts with the approved global action; existing values were not overwritten.");
            }
            if (string.Equals(existingOriginal, container.OriginalIdentifier, StringComparison.Ordinal)
                && string.Equals(existingDigest, container.ActionSignatureDigest, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            target.AllProperties[TopologyPlanner.WebOriginalIdentifierPropertyName] = container.OriginalIdentifier;
            target.AllProperties[TopologyPlanner.WebPlanDigestPropertyName] = container.ActionSignatureDigest;
            target.Update();
            context.ExecuteQueryRetry();
        }

        private static void ValidateParent(
            TargetWebContainerIngredientPlan container,
            string observedParentUrl,
            string observedParentPath)
        {
            if (!ParentMatches(container, observedParentUrl, observedParentPath))
            {
                throw new InvalidOperationException("The target connection did not resolve the approved direct parent Web.");
            }
        }

        private static bool ParentMatches(
            TargetWebContainerIngredientPlan container,
            string observedParentUrl,
            string observedParentPath)
        {
            return SharedTopologyPath.EqualsUrl(observedParentUrl, container.TargetParentWebUrl)
                && SharedTopologyPath.EqualsPath(
                    SharedTopologyPath.Combine(observedParentPath, SharedTopologyPath.Leaf(container.TargetServerRelativeUrl)),
                    container.TargetServerRelativeUrl);
        }

        private static string NormalizeTemplate(string template, int configuration)
        {
            return (template ?? string.Empty).IndexOf('#') >= 0
                ? template
                : template + "#" + configuration.ToString(CultureInfo.InvariantCulture);
        }

        private static string Property(PropertyValues values, string key)
        {
            if (values == null || !values.FieldValues.TryGetValue(key, out var value))
            {
                return null;
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool TryGetHttpResponse(Exception exception, out HttpWebResponse response)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is WebException webException
                    && webException.Response is HttpWebResponse httpResponse)
                {
                    response = httpResponse;
                    return true;
                }
            }
            response = null;
            return false;
        }
    }
}
