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
        public const string ProjectionVersion = "pnp-publishing-page-ingredient-projection/path-derived-topology-v2";

        public static CanonicalPageIngredientGraph Project(
            PublishingPageCaptureBundle snapshot,
            SharedTopologyPlan sharedPlan,
            SharedTopologyPageReference reference)
        {
            if (snapshot == null || snapshot.IngredientGraph == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            SharedTopologyPlanValidator.Validate(sharedPlan);
            SharedTopologyPageReferenceFactory.Validate(reference);
            var graph = Clone(snapshot.IngredientGraph);
            graph.SchemaVersion = "pnp-page-ingredient-graph/v2";
            graph.ProjectionVersion = ProjectionVersion;
            graph.ExternalReferences = new List<PageIngredientExternalReference>();

            foreach (var fidelity in reference.SourceFidelity)
            {
                graph.ExternalReferences.Add(new PageIngredientExternalReference
                {
                    IngredientId = fidelity.IngredientId,
                    Kind = PageIngredientKind.Web,
                    SharedPlanDigest = reference.SharedPlanDigest,
                    ExecutionGroupDigest = reference.ExecutionGroupDigest,
                    SupportCohortDigest = reference.SupportCohortDigest,
                    State = fidelity.State == SourceWebFidelityState.AuthorizationBlocked
                        ? PageExternalIngredientState.AuthorizationBlocked
                        : PageExternalIngredientState.EvidenceOnly,
                    EvidenceDigest = fidelity.EvidenceDigest
                });
                graph.Edges.Add(new PageIngredientEdge
                {
                    FromIngredientId = PublishingPageIngredientIds.PageArtifact,
                    ToIngredientId = fidelity.IngredientId,
                    Relationship = PageIngredientRelationship.GovernedBy,
                    Requirement = PageIngredientRequirement.Optional,
                    Condition = "Partial source Web fidelity is reported independently from target path provisioning."
                });
            }

            foreach (var required in reference.RequiredActions)
            {
                graph.ExternalReferences.Add(new PageIngredientExternalReference
                {
                    IngredientId = sharedPlan.TargetWebContainers.Single(value => value.LogicalActionKey == required.LogicalActionKey).IngredientId,
                    Kind = PageIngredientKind.Web,
                    SharedPlanDigest = reference.SharedPlanDigest,
                    ExecutionGroupDigest = reference.ExecutionGroupDigest,
                    SupportCohortDigest = reference.SupportCohortDigest,
                    TargetSlotKey = required.TargetSlotKey,
                    LogicalActionKey = required.LogicalActionKey,
                    ExecutionGrantSignature = required.ExecutionGrant.Signature,
                    OriginalIdentifier = required.OriginalIdentifier,
                    ExpectedOwnership = required.ExpectedOwnership.ToString(),
                    State = PageExternalIngredientState.PlannedGlobalAction,
                    TargetIdentity = required.TargetWebUrl,
                    EvidenceDigest = required.ExecutionGrant.SemanticDigest
                });
            }

            graph.Edges.Add(new PageIngredientEdge
            {
                FromIngredientId = PublishingPageIngredientIds.PageArtifact,
                ToIngredientId = reference.TargetLeafContainerIngredientId,
                Relationship = PageIngredientRelationship.DependsOn,
                Requirement = PageIngredientRequirement.Required,
                Condition = "The page is materialized only after the shared exact-path target Web action is verified."
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
                    Condition = "The List owner is the shared exact-path target Web action."
                });
            }
            graph.Edges = graph.Edges
                .GroupBy(value => value.FromIngredientId + "\u001f" + value.ToIngredientId + "\u001f" + value.Relationship + "\u001f" + value.Requirement + "\u001f" + value.Condition, StringComparer.Ordinal)
                .Select(value => value.First())
                .ToList();
            return graph;
        }

        private static CanonicalPageIngredientGraph Clone(CanonicalPageIngredientGraph graph)
        {
            return new CanonicalPageIngredientGraph
            {
                SchemaVersion = graph.SchemaVersion,
                ProjectionVersion = graph.ProjectionVersion,
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
                    ExecutionGroupDigest = value.ExecutionGroupDigest,
                    SupportCohortDigest = value.SupportCohortDigest,
                    TargetSlotKey = value.TargetSlotKey,
                    LogicalActionKey = value.LogicalActionKey,
                    ExecutionGrantSignature = value.ExecutionGrantSignature,
                    OriginalIdentifier = value.OriginalIdentifier,
                    ExpectedOwnership = value.ExpectedOwnership,
                    State = value.State,
                    TargetIdentity = value.TargetIdentity,
                    EvidenceDigest = value.EvidenceDigest
                }).ToList()
            };
        }
    }
}
