using Microsoft.SharePoint.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    /// <summary>
    /// CSOM transport adapter for the shared topology executor. It never infers
    /// authorization from exception text; only a retained wire status can produce
    /// HTTP 401/403 in an observation.
    /// </summary>
    public sealed class CsomSharedTopologyTargetRuntime : ISharedTopologyTargetRuntime
    {
        public const string IngredientIdPropertyName = "pnp_reserved_topology_ingredient_id";
        public const string SharedPlanDigestPropertyName = "pnp_reserved_shared_topology_plan_digest";

        private readonly ClientContext anchorContext;

        public CsomSharedTopologyTargetRuntime(ClientContext anchorContext)
        {
            this.anchorContext = anchorContext ?? throw new ArgumentNullException(nameof(anchorContext));
        }

        public SharedTopologyTargetSiteObservation InspectTargetSite(SharedTopologyPlan plan)
        {
            SharedTopologyPlanValidator.Validate(plan);
            try
            {
                using (var context = anchorContext.Clone(plan.TargetSite.TargetSiteCollectionUrl))
                {
                    var site = context.Site;
                    var root = site.RootWeb;
                    context.Load(site, value => value.Id);
                    context.Load(root, value => value.Id, value => value.Url);
                    context.ExecuteQueryRetry();
                    return new SharedTopologyTargetSiteObservation
                    {
                        Exists = true,
                        TargetSiteId = site.Id,
                        TargetRootWebId = root.Id,
                        TargetSiteCollectionUrl = root.Url
                    };
                }
            }
            catch (Exception exception) when (IsTransportFailure(exception))
            {
                return FailedSite(plan, exception);
            }
        }

        public IList<TargetWebContainerObservation> InspectTargetWebContainers(SharedTopologyPlan plan)
        {
            SharedTopologyPlanValidator.Validate(plan);
            var observations = new List<TargetWebContainerObservation>();
            var unavailableParents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var container in plan.TargetWebContainers.OrderBy(value => SharedTopologyPath.Depth(value.TargetServerRelativeUrl)))
            {
                if (unavailableParents.Contains(container.ParentIngredientId))
                {
                    unavailableParents.Add(container.IngredientId);
                    continue;
                }
                var observation = InspectContainer(plan, container);
                observations.Add(observation);
                if (!observation.Exists || observation.InspectionFailed || observation.HttpStatusCode.HasValue)
                {
                    unavailableParents.Add(container.IngredientId);
                }
            }
            return observations;
        }

        public TargetWebContainerObservation CreateTargetWebContainer(
            SharedTopologyPlan plan,
            TargetWebContainerIngredientPlan container)
        {
            SharedTopologyPlanValidator.Validate(plan);
            if (container == null || !plan.TargetWebContainers.Any(value => value.IngredientId == container.IngredientId))
            {
                throw new ArgumentException("The target-Web container does not belong to the shared topology plan.", nameof(container));
            }
            using (var context = anchorContext.Clone(container.TargetParentWebUrl))
            {
                var parent = context.Web;
                context.Load(parent, value => value.Id, value => value.Url, value => value.ServerRelativeUrl);
                context.ExecuteQueryRetry();
                if (!SharedTopologyPath.EqualsUrl(parent.Url, container.TargetParentWebUrl))
                {
                    throw new InvalidOperationException("The runtime connection did not resolve the planned direct parent Web.");
                }
                var segment = SharedTopologyPath.Leaf(container.TargetServerRelativeUrl);
                var target = parent.Webs.Add(new WebCreationInformation
                {
                    Url = segment,
                    Title = container.Provisioning.Title,
                    Description = "PnP shared topology ingredient " + container.IngredientId,
                    Language = container.Provisioning.Language,
                    UseSamePermissionsAsParentSite = true,
                    WebTemplate = NormalizeTemplate(container.Provisioning.Template, container.Provisioning.Configuration)
                });
                context.Load(target, value => value.Id, value => value.Url, value => value.ServerRelativeUrl, value => value.AllProperties);
                context.ExecuteQueryRetry();
                target.AllProperties[IngredientIdPropertyName] = container.IngredientId;
                target.AllProperties[SharedPlanDigestPropertyName] = plan.PlanDigest;
                target.Update();
                context.ExecuteQueryRetry();
            }
            var observation = InspectContainer(plan, container);
            if (observation.InspectionFailed || observation.HttpStatusCode.HasValue || !observation.Exists)
            {
                throw new InvalidOperationException("The created target Web did not pass immediate exact-path readback: " + observation.Diagnostic);
            }
            return observation;
        }

        private TargetWebContainerObservation InspectContainer(
            SharedTopologyPlan plan,
            TargetWebContainerIngredientPlan container)
        {
            try
            {
                using (var context = anchorContext.Clone(container.TargetParentWebUrl))
                {
                    var parent = context.Web;
                    context.Load(context.Site, value => value.Id);
                    context.Load(parent, value => value.Id, value => value.Url);
                    context.Load(parent.Webs, values => values.Include(
                        value => value.Id,
                        value => value.Url,
                        value => value.ServerRelativeUrl,
                        value => value.Title,
                        value => value.WebTemplate,
                        value => value.Configuration,
                        value => value.AllProperties));
                    context.ExecuteQueryRetry();
                    if (!SharedTopologyPath.EqualsUrl(parent.Url, container.TargetParentWebUrl))
                    {
                        return FailedContainer(container, null, "The observed parent URL differs from the planned direct parent.");
                    }
                    var candidate = parent.Webs.AsEnumerable().SingleOrDefault(value =>
                        SharedTopologyPath.EqualsPath(value.ServerRelativeUrl, container.TargetServerRelativeUrl));
                    if (candidate == null)
                    {
                        return new TargetWebContainerObservation
                        {
                            IngredientId = container.IngredientId,
                            Exists = false,
                            TargetSiteId = context.Site.Id,
                            TargetParentWebId = parent.Id,
                            TargetWebUrl = container.TargetWebUrl,
                            TargetServerRelativeUrl = container.TargetServerRelativeUrl
                        };
                    }
                    return new TargetWebContainerObservation
                    {
                        IngredientId = container.IngredientId,
                        Exists = true,
                        TargetSiteId = context.Site.Id,
                        TargetWebId = candidate.Id,
                        TargetParentWebId = parent.Id,
                        TargetWebUrl = candidate.Url,
                        TargetServerRelativeUrl = candidate.ServerRelativeUrl,
                        ExistingTitle = candidate.Title,
                        ExistingTemplate = candidate.WebTemplate,
                        ExistingConfiguration = candidate.Configuration,
                        ExistingIngredientId = Property(candidate.AllProperties, IngredientIdPropertyName),
                        ExistingPlanDigest = Property(candidate.AllProperties, SharedPlanDigestPropertyName)
                    };
                }
            }
            catch (Exception exception) when (IsTransportFailure(exception))
            {
                return FailedContainer(container, exception, exception.Message);
            }
        }

        private static SharedTopologyTargetSiteObservation FailedSite(SharedTopologyPlan plan, Exception exception)
        {
            return new SharedTopologyTargetSiteObservation
            {
                HttpStatusCode = TopologyHttpStatusExtractor.TryGetLiteralStatus(exception, out var status) ? status : (int?)null,
                InspectionFailed = true,
                Exists = false,
                TargetSiteCollectionUrl = plan.TargetSite.TargetSiteCollectionUrl,
                Diagnostic = exception.Message
            };
        }

        private static TargetWebContainerObservation FailedContainer(
            TargetWebContainerIngredientPlan container,
            Exception exception,
            string diagnostic)
        {
            return new TargetWebContainerObservation
            {
                IngredientId = container.IngredientId,
                HttpStatusCode = exception != null && TopologyHttpStatusExtractor.TryGetLiteralStatus(exception, out var status) ? status : (int?)null,
                InspectionFailed = true,
                Exists = false,
                TargetWebUrl = container.TargetWebUrl,
                TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                Diagnostic = diagnostic
            };
        }

        private static string Property(PropertyValues values, string name)
        {
            object value;
            return values != null && values.FieldValues.TryGetValue(name, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static string NormalizeTemplate(string template, int configuration)
        {
            return (template ?? string.Empty).IndexOf('#') >= 0
                ? template
                : template + "#" + configuration.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsTransportFailure(Exception exception)
        {
            return exception is ServerException
                || exception is ClientRequestException
                || exception is WebException
                || exception is InvalidOperationException;
        }
    }

}
