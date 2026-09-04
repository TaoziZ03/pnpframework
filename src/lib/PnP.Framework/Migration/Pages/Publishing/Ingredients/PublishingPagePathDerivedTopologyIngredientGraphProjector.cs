using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Topology.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPagePathDerivedTopologyIngredientGraphProjector
    {
        public static CanonicalPageIngredientGraph Project(
            PublishingPageCaptureBundle snapshot,
            SharedTopologyPlan sharedPlan,
            SharedTopologyTargetAnalysis analysis,
            SharedTopologyPageReference reference)
        {
            if (snapshot == null || snapshot.IngredientGraph == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            var graph = Clone(snapshot.IngredientGraph);
            graph.SchemaVersion = "pnp-page-ingredient-graph/v2";
            graph.ExternalReferences = new List<PageIngredientExternalReference>();

            var fidelity = sharedPlan.SourceWebFidelityIngredients.Single(value =>
                string.Equals(value.IngredientId, reference.SourceWebFidelityIngredientId, StringComparison.Ordinal));
            graph.ExternalReferences.Add(new PageIngredientExternalReference
            {
                IngredientId = fidelity.IngredientId,
                Kind = PageIngredientKind.Web,
                SharedPlanDigest = sharedPlan.PlanDigest,
                State = fidelity.State == SourceWebFidelityState.AuthorizationBlocked
                    ? PageExternalIngredientState.AuthorizationBlocked
                    : fidelity.State == SourceWebFidelityState.Captured
                        ? PageExternalIngredientState.SatisfiedBySharedPlan
                        : PageExternalIngredientState.Blocked,
                TargetIdentity = null,
                EvidenceDigest = fidelity.EvidenceSha256
            });
            graph.Edges.Add(new PageIngredientEdge
            {
                FromIngredientId = PublishingPageIngredientIds.PageArtifact,
                ToIngredientId = fidelity.IngredientId,
                Relationship = PageIngredientRelationship.GovernedBy,
                Requirement = PageIngredientRequirement.Optional,
                Condition = "Source Web fidelity evidence is reported independently from target path provisioning."
            });

            graph.ExternalReferences.Add(new PageIngredientExternalReference
            {
                IngredientId = sharedPlan.TargetSite.IngredientId,
                Kind = PageIngredientKind.Topology,
                SharedPlanDigest = sharedPlan.PlanDigest,
                State = PageExternalIngredientState.SatisfiedBySharedPlan,
                TargetIdentity = sharedPlan.TargetSite.TargetSiteCollectionUrl
            });
            var probes = analysis.TargetWebContainers.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            foreach (var container in sharedPlan.TargetWebContainers.OrderBy(value => value.TargetServerRelativeUrl, StringComparer.OrdinalIgnoreCase))
            {
                var probe = probes[container.IngredientId];
                graph.ExternalReferences.Add(new PageIngredientExternalReference
                {
                    IngredientId = container.IngredientId,
                    Kind = PageIngredientKind.Web,
                    SharedPlanDigest = sharedPlan.PlanDigest,
                    State = ExternalState(probe.State),
                    TargetIdentity = container.TargetWebUrl,
                    EvidenceDigest = container.IngredientDigest
                });
            }

            graph.Edges.Add(new PageIngredientEdge
            {
                FromIngredientId = PublishingPageIngredientIds.PageArtifact,
                ToIngredientId = reference.TargetLeafContainerIngredientId,
                Relationship = PageIngredientRelationship.DependsOn,
                Requirement = PageIngredientRequirement.Required,
                Condition = "The page is materialized only after the shared exact-path target Web container is ready."
            });
            foreach (var list in snapshot.ListDependencies.Where(value => value != null
                         && value.SourceSiteId == snapshot.Source.SiteId
                         && value.SourceWebId == snapshot.Source.WebId))
            {
                graph.Edges.Add(new PageIngredientEdge
                {
                    FromIngredientId = PublishingPageIngredientIds.List(list.SourceWebId, list.SourceListId),
                    ToIngredientId = reference.TargetLeafContainerIngredientId,
                    Relationship = PageIngredientRelationship.DependsOn,
                    Requirement = PageIngredientRequirement.Required,
                    Condition = "The List owner is the shared exact-path target Web container, not the unavailable source ancestor closure."
                });
            }
            graph.Edges = graph.Edges
                .GroupBy(value => value.FromIngredientId + "\u001f" + value.ToIngredientId + "\u001f" + value.Relationship + "\u001f" + value.Requirement + "\u001f" + value.Condition, StringComparer.Ordinal)
                .Select(value => value.First())
                .ToList();
            return graph;
        }

        private static PageExternalIngredientState ExternalState(TargetWebContainerState state)
        {
            switch (state)
            {
                case TargetWebContainerState.Reuse:
                case TargetWebContainerState.CreateMissing:
                    return PageExternalIngredientState.SatisfiedBySharedPlan;
                case TargetWebContainerState.TargetInspectionRequired:
                    return PageExternalIngredientState.TargetInspectionRequired;
                case TargetWebContainerState.AuthorizationBlocked:
                    return PageExternalIngredientState.AuthorizationBlocked;
                case TargetWebContainerState.RetryableFailure:
                    return PageExternalIngredientState.RetryableFailure;
                default:
                    return PageExternalIngredientState.Blocked;
            }
        }

        private static CanonicalPageIngredientGraph Clone(CanonicalPageIngredientGraph graph)
        {
            return new CanonicalPageIngredientGraph
            {
                SchemaVersion = graph.SchemaVersion,
                Nodes = graph.Nodes.Select(value => new PageIngredientNode
                {
                    Id = value.Id,
                    Kind = value.Kind,
                    Label = value.Label,
                    HasContent = value.HasContent,
                    Ownership = value.Ownership,
                    SourceAuthority = value.SourceAuthority,
                    EvidenceDigest = value.EvidenceDigest,
                    RuntimeRequirement = value.RuntimeRequirement,
                    EvidenceReferences = (value.EvidenceReferences ?? Array.Empty<string>()).ToList()
                }).ToList(),
                Edges = graph.Edges.Select(value => new PageIngredientEdge
                {
                    FromIngredientId = value.FromIngredientId,
                    ToIngredientId = value.ToIngredientId,
                    Relationship = value.Relationship,
                    Requirement = value.Requirement,
                    Condition = value.Condition
                }).ToList(),
                ExternalReferences = graph.ExternalReferences?.Select(value => new PageIngredientExternalReference
                {
                    IngredientId = value.IngredientId,
                    Kind = value.Kind,
                    Ownership = value.Ownership,
                    SharedPlanDigest = value.SharedPlanDigest,
                    State = value.State,
                    TargetIdentity = value.TargetIdentity,
                    EvidenceDigest = value.EvidenceDigest
                }).ToList()
            };
        }
    }
}
