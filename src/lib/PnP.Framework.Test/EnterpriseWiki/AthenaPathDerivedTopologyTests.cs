using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Topology.Ingredients;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Lists.Capture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class AthenaPathDerivedTopologyTests
    {
        private static readonly Guid SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid SourceLeafWebId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TargetSiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid TargetRootWebId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        [TestMethod]
        public void ParentWeb403BuildsThreeIndependentExactPathContainersWithoutSourceMetadata()
        {
            var first = BuildAthenaPlan();
            var second = BuildAthenaPlan();

            Assert.IsTrue(first.IsExecutable, Issues(first));
            Assert.AreEqual(first.Plan.PlanDigest, second.Plan.PlanDigest);
            Assert.AreEqual(1, first.Plan.SourceWebFidelityIngredients.Count);
            Assert.AreEqual(SourceWebFidelityState.AuthorizationBlocked, first.Plan.SourceWebFidelityIngredients[0].State);
            Assert.AreEqual(403, first.Plan.SourceWebFidelityIngredients[0].AuthorizationEvidence.HttpStatusCode);
            Assert.AreEqual("ReadSourceParentWeb", first.Plan.SourceWebFidelityIngredients[0].AuthorizationEvidence.Operation);

            var containers = first.Plan.TargetWebContainers;
            CollectionAssert.AreEqual(new[]
            {
                "/teams/athena-pnp/gkb",
                "/teams/athena-pnp/gkb/projects",
                "/teams/athena-pnp/gkb/projects/AthenaWiki"
            }, containers.Select(value => value.TargetServerRelativeUrl).ToArray());
            Assert.AreEqual(first.Plan.TargetSite.IngredientId, containers[0].ParentIngredientId);
            Assert.AreEqual(containers[0].IngredientId, containers[1].ParentIngredientId);
            Assert.AreEqual(containers[1].IngredientId, containers[2].ParentIngredientId);
            Assert.IsTrue(containers.All(value => value.IdentityBasis == SharedTopologyIdentityBasis.ExactRelativePath));
            Assert.IsTrue(containers.All(value => !value.CollisionResolved));
            Assert.IsTrue(containers.All(value => value.Provisioning.TemplateSource == TargetWebProvisioningValueSource.ExplicitTargetPolicy));
            Assert.IsTrue(containers.All(value => value.Provisioning.ExpectedMetadataDifferences.Count == 2));
            Assert.AreEqual(
                "topology:target-web-container:/teams/athena-pnp/gkb/projects/athenawiki",
                containers[2].IngredientId);
            Assert.AreEqual(SourceLeafWebId, first.Plan.SourceWebBindings.Single().SourceWebId);
            Assert.AreEqual(containers[2].IngredientId, first.Plan.SourceWebBindings.Single().TargetContainerIngredientId);

            // Target-only containers intentionally have no SourceWebId, parent ID,
            // Source title, Source template, or Source configuration properties.
            var propertyNames = typeof(TargetWebContainerIngredientPlan).GetProperties().Select(value => value.Name).ToArray();
            Assert.IsFalse(propertyNames.Contains("SourceWebId"));
            Assert.IsFalse(propertyNames.Contains("SourceParentWebId"));
            Assert.IsFalse(propertyNames.Contains("SourceTitle"));
            Assert.IsFalse(propertyNames.Contains("SourceWebTemplate"));
            Assert.IsFalse(propertyNames.Contains("SourceConfiguration"));
        }

        [TestMethod]
        public void MissingAndExistingTargetNodesProduceIndependentCreateAndReuseActions()
        {
            var plan = BuildAthenaPlan().Plan;
            var first = plan.TargetWebContainers[0];
            var analysis = Analyze(plan, new[]
            {
                Existing(plan, first, 1),
                Missing(plan.TargetWebContainers[1]),
                Missing(plan.TargetWebContainers[2])
            });
            var actions = SharedTopologyActionPlanProjector.Project(plan, analysis);

            Assert.AreEqual(TargetWebContainerState.Reuse, analysis.TargetWebContainers[0].State);
            Assert.AreEqual(TargetWebContainerState.CreateMissing, analysis.TargetWebContainers[1].State);
            Assert.AreEqual(TargetWebContainerState.CreateMissing, analysis.TargetWebContainers[2].State);
            CollectionAssert.AreEqual(
                new[] { SharedTopologyActionKind.Reuse, SharedTopologyActionKind.CreateMissing, SharedTopologyActionKind.CreateMissing },
                actions.Actions.Select(value => value.Action).ToArray());
            Assert.AreEqual(3, actions.Actions.Select(value => value.IngredientId).Distinct(StringComparer.Ordinal).Count());
            Assert.IsTrue(actions.IsExecutable);
        }

        [TestMethod]
        public void NoObservationKeepsTheFirstContainerAtTargetInspectionRequired()
        {
            var plan = BuildAthenaPlan().Plan;
            var analysis = Analyze(plan, Array.Empty<TargetWebContainerObservation>());

            Assert.AreEqual(TargetWebContainerState.TargetInspectionRequired, analysis.TargetWebContainers[0].State);
            Assert.AreEqual(TargetWebContainerState.SkippedByDependency, analysis.TargetWebContainers[1].State);
            Assert.AreEqual(TargetWebContainerState.SkippedByDependency, analysis.TargetWebContainers[2].State);
            Assert.IsFalse(analysis.IsActionable);
        }

        [TestMethod]
        public void LiteralAuthorizationIsScopedAndRetryableStatusesAreNeverAuthorizationBlocked()
        {
            var plan = BuildAthenaPlan().Plan;
            var first = plan.TargetWebContainers[0];
            var second = plan.TargetWebContainers[1];
            var authorization = Analyze(plan, new[]
            {
                Existing(plan, first, 1),
                Failure(second, 403),
                Missing(plan.TargetWebContainers[2])
            });

            Assert.AreEqual(TargetWebContainerState.Reuse, authorization.TargetWebContainers[0].State);
            Assert.AreEqual(TargetWebContainerState.AuthorizationBlocked, authorization.TargetWebContainers[1].State);
            Assert.AreEqual(TargetWebContainerState.SkippedByDependency, authorization.TargetWebContainers[2].State);
            Assert.AreEqual(1, authorization.TargetWebContainers.Count(value => value.State == TargetWebContainerState.AuthorizationBlocked));

            foreach (var status in new[] { 409, 423, 429, 500, 503 })
            {
                var retryable = Analyze(plan, new[]
                {
                    Failure(first, status),
                    Missing(second),
                    Missing(plan.TargetWebContainers[2])
                });
                Assert.AreEqual(TargetWebContainerState.RetryableFailure, retryable.TargetWebContainers[0].State, "HTTP " + status);
                Assert.AreNotEqual(TargetWebContainerState.AuthorizationBlocked, retryable.TargetWebContainers[0].State, "HTTP " + status);
            }
        }

        [TestMethod]
        public void InvalidEscapingAndAmbiguousPathsFailClosed()
        {
            foreach (var path in new[]
            {
                "/teams/athena/../escape/AthenaWiki",
                "/teams/athena/%2e%2e/escape/AthenaWiki",
                "/teams/athena/gkb//AthenaWiki",
                "/teams/athena/gkb/projects./AthenaWiki",
                "/teams/athena/gkb%2fprojects/AthenaWiki"
            })
            {
                var request = AthenaRequest();
                request.Source.SourceLeafWebServerRelativeUrl = path;
                request.Source.SourceLeafWebUrl = "https://microsoft.sharepoint.com" + path;
                request.Source.EvidenceSha256 = RecomputeEvidence(request.Source);
                var result = new PathDerivedTopologyPlanner().Build(request);
                Assert.IsFalse(result.IsExecutable, path);
                Assert.IsTrue(result.Issues.Any(), path);
            }
        }

        [TestMethod]
        public void StableSuffixRequiresExplicitConfirmedCollisionPolicyAndAthenaAddsNone()
        {
            var noCollision = BuildAthenaPlan().Plan;
            Assert.IsTrue(noCollision.TargetWebContainers.All(value => !value.CollisionResolved));
            Assert.AreEqual("/teams/athena-pnp/gkb/projects/AthenaWiki", noCollision.TargetWebContainers.Last().TargetServerRelativeUrl);

            var blockedRequest = AthenaRequest();
            blockedRequest.ConfirmedForeignCollisionServerRelativeUrls.Add("/teams/athena-pnp/gkb");
            var blocked = new PathDerivedTopologyPlanner().Build(blockedRequest);
            Assert.IsFalse(blocked.IsExecutable);
            Assert.IsTrue(blocked.Issues.Any(value => value.Code == "TargetWebPathCollision"));

            var suffixRequest = AthenaRequest();
            suffixRequest.ProvisioningPolicy.CollisionPolicy = TargetWebCollisionPolicy.StableSuffix;
            suffixRequest.ConfirmedForeignCollisionServerRelativeUrls.Add("/teams/athena-pnp/gkb");
            var suffixed = new PathDerivedTopologyPlanner().Build(suffixRequest);
            Assert.IsTrue(suffixed.IsExecutable, Issues(suffixed));
            StringAssert.StartsWith(suffixed.Plan.TargetWebContainers[0].TargetServerRelativeUrl, "/teams/athena-pnp/gkb-pnp-");
            Assert.IsTrue(suffixed.Plan.TargetWebContainers[0].CollisionResolved);
            Assert.AreEqual("projects", suffixed.Plan.TargetWebContainers[1].SourcePathSegment);
            Assert.AreEqual("AthenaWiki", suffixed.Plan.TargetWebContainers[2].SourcePathSegment);
        }

        [TestMethod]
        public void TwoPagesReferenceOneSharedActionAndReceiptSet()
        {
            var plan = BuildAthenaPlan().Plan;
            var analysis = Analyze(plan, plan.TargetWebContainers.Select(Missing));
            var actions = SharedTopologyActionPlanProjector.Project(plan, analysis);
            var page218 = SharedTopologyPageReferenceFactory.Create(plan, analysis, actions, SourceSiteId, SourceLeafWebId);
            var page350 = SharedTopologyPageReferenceFactory.Create(plan, analysis, actions, SourceSiteId, SourceLeafWebId);

            Assert.AreEqual(page218.SharedTopologyPlanDigest, page350.SharedTopologyPlanDigest);
            Assert.AreEqual(page218.ActionPlanDigest, page350.ActionPlanDigest);
            CollectionAssert.AreEqual(page218.RequiredTargetContainerIngredientIds.ToArray(), page350.RequiredTargetContainerIngredientIds.ToArray());
            Assert.AreEqual(3, actions.Actions.Count);

            var runtime = new InMemoryTopologyRuntime(plan);
            var journal = new RecordingJournal();
            var receipt = new SharedTopologyMaterializer().Execute(
                plan,
                analysis,
                actions,
                actions.ActionPlanDigest,
                runtime,
                journal);

            Assert.AreEqual(3, receipt.Webs.Count);
            Assert.AreEqual(3, runtime.CreateCalls);
            Assert.AreEqual(3, journal.Intents.Count);
            Assert.AreEqual(3, journal.Receipts.Count);
            SharedTopologyPageReferenceFactory.ValidateReceipt(page218, receipt);
            SharedTopologyPageReferenceFactory.ValidateReceipt(page350, receipt);
            Assert.IsTrue(receipt.FreshReadbackPassed);
            receipt.Webs[0].TargetWebUrl += "-tampered";
            Assert.ThrowsException<InvalidDataException>(() => SharedTopologyExecutionValidator.ValidateReceipt(plan, actions, receipt));
        }

        [TestMethod]
        public void SourceFidelity403DoesNotCascadeToPageOrDownstreamIngredients()
        {
            AssertNoCascade(210);
            AssertNoCascade(8);
        }

        [TestMethod]
        public void PlanGraphReferencesSharedTopologyWithoutCopyingWebNodes()
        {
            var plan = BuildAthenaPlan().Plan;
            var analysis = Analyze(plan, plan.TargetWebContainers.Select(value => Existing(plan, value, plan.TargetWebContainers.IndexOf(value) + 1)));
            var actions = SharedTopologyActionPlanProjector.Project(plan, analysis);
            var reference = SharedTopologyPageReferenceFactory.Create(plan, analysis, actions, SourceSiteId, SourceLeafWebId);
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = new PageIdentity
                {
                    SiteId = SourceSiteId,
                    WebId = SourceLeafWebId,
                    WebUrl = "https://microsoft.sharepoint.com/teams/athena/gkb/projects/AthenaWiki",
                    WebServerRelativeUrl = "/teams/athena/gkb/projects/AthenaWiki"
                },
                IngredientGraph = new CanonicalPageIngredientGraph
                {
                    Nodes = new List<PageIngredientNode>
                    {
                        new PageIngredientNode
                        {
                            Id = PublishingPageIngredientIds.PageArtifact,
                            Kind = PageIngredientKind.PageArtifact,
                            HasContent = true,
                            Ownership = PageIngredientOwnership.SourceOwned
                        },
                        new PageIngredientNode
                        {
                            Id = PublishingPageIngredientIds.List(SourceLeafWebId, listId),
                            Kind = PageIngredientKind.List,
                            HasContent = true,
                            Ownership = PageIngredientOwnership.Shared
                        }
                    }
                },
                ListDependencies = new List<ListDependencySnapshot>
                {
                    new ListDependencySnapshot
                    {
                        SourceSiteId = SourceSiteId,
                        SourceWebId = SourceLeafWebId,
                        SourceListId = listId
                    }
                }
            };

            var graph = PublishingPagePathDerivedTopologyIngredientGraphProjector.Project(snapshot, plan, analysis, reference);

            Assert.AreEqual("pnp-page-ingredient-graph/v2", graph.SchemaVersion);
            Assert.AreEqual(0, graph.Nodes.Count(value => value.Kind == PageIngredientKind.Web));
            Assert.AreEqual(5, graph.ExternalReferences.Count);
            Assert.AreEqual(1, graph.ExternalReferences.Count(value => value.State == PageExternalIngredientState.AuthorizationBlocked));
            Assert.IsTrue(graph.Edges.Any(value => value.FromIngredientId == PublishingPageIngredientIds.PageArtifact
                && value.ToIngredientId == reference.SourceWebFidelityIngredientId
                && value.Requirement == PageIngredientRequirement.Optional));
            Assert.IsTrue(graph.Edges.Any(value => value.FromIngredientId == PublishingPageIngredientIds.PageArtifact
                && value.ToIngredientId == reference.TargetLeafContainerIngredientId
                && value.Requirement == PageIngredientRequirement.Required));
            Assert.IsTrue(graph.Edges.Any(value => value.FromIngredientId == PublishingPageIngredientIds.List(SourceLeafWebId, listId)
                && value.ToIngredientId == reference.TargetLeafContainerIngredientId
                && value.Requirement == PageIngredientRequirement.Required));
        }

        [TestMethod]
        public void OptionalSharedFieldsDoNotChangeLegacyCanonicalPayloads()
        {
            var legacyGraphJson = PublishingPagePackageSerializer.Serialize(new CanonicalPageIngredientGraph());
            var legacySnapshotJson = PublishingPagePackageSerializer.Serialize(new PublishingPageCaptureBundle());

            Assert.IsFalse(legacyGraphJson.Contains("externalReferences", StringComparison.Ordinal));
            Assert.IsFalse(legacySnapshotJson.Contains("pathDerivedTopologyEvidence", StringComparison.Ordinal));
        }

        [TestMethod]
        public void AuthorizationEvidenceRejectsNonLiteralAuthorizationStatus()
        {
            Assert.ThrowsException<InvalidDataException>(() => PathDerivedSourceTopologyEvidenceFactory.CreateAuthorizationBlocked(
                SourceSiteId,
                "https://microsoft.sharepoint.com/teams/athena",
                "/teams/athena",
                SourceLeafWebId,
                "https://microsoft.sharepoint.com/teams/athena/gkb/projects/AthenaWiki",
                "/teams/athena/gkb/projects/AthenaWiki",
                "ReadSourceParentWeb",
                "https://microsoft.sharepoint.com/teams/athena/gkb/projects/AthenaWiki/_api/web/ParentWeb",
                500,
                DateTimeOffset.UtcNow));
        }

        [TestMethod]
        public void ValidatorRejectsStalePlanAnalysisAndReceiptDigests()
        {
            var plan = BuildAthenaPlan().Plan;
            var analysis = Analyze(plan, plan.TargetWebContainers.Select(Missing));
            var actions = SharedTopologyActionPlanProjector.Project(plan, analysis);

            var originalTitle = plan.TargetWebContainers[2].Provisioning.Title;
            plan.TargetWebContainers[2].Provisioning.Title = originalTitle + " tampered";
            Assert.ThrowsException<InvalidDataException>(() => SharedTopologyPlanValidator.Validate(plan));
            plan.TargetWebContainers[2].Provisioning.Title = originalTitle;

            analysis.TargetWebContainers[0].TargetWebUrl += "-tampered";
            Assert.ThrowsException<InvalidDataException>(() => SharedTopologyExecutionValidator.ValidateAnalysis(plan, analysis));

            analysis = Analyze(plan, plan.TargetWebContainers.Select(Missing));
            actions = SharedTopologyActionPlanProjector.Project(plan, analysis);
            actions.Actions.RemoveAt(2);
            Assert.ThrowsException<InvalidDataException>(() => SharedTopologyExecutionValidator.ValidateActionPlan(plan, analysis, actions));
        }

        private static void AssertNoCascade(int downstreamCount)
        {
            var plan = BuildAthenaPlan().Plan;
            var analysis = Analyze(plan, plan.TargetWebContainers.Select(value => Existing(plan, value, plan.TargetWebContainers.IndexOf(value) + 1)));
            var actions = SharedTopologyActionPlanProjector.Project(plan, analysis);
            var reference = SharedTopologyPageReferenceFactory.Create(plan, analysis, actions, SourceSiteId, SourceLeafWebId);
            var graph = new CanonicalPageIngredientGraph
            {
                SchemaVersion = "pnp-page-ingredient-graph/v2",
                ExternalReferences = new List<PageIngredientExternalReference>
                {
                    new PageIngredientExternalReference
                    {
                        IngredientId = reference.SourceWebFidelityIngredientId,
                        Kind = PageIngredientKind.Web,
                        SharedPlanDigest = plan.PlanDigest,
                        State = PageExternalIngredientState.AuthorizationBlocked,
                        EvidenceDigest = plan.SourceWebFidelityIngredients.Single().EvidenceSha256
                    },
                    new PageIngredientExternalReference
                    {
                        IngredientId = reference.TargetLeafContainerIngredientId,
                        Kind = PageIngredientKind.Web,
                        SharedPlanDigest = plan.PlanDigest,
                        State = PageExternalIngredientState.SatisfiedBySharedPlan,
                        TargetIdentity = reference.TargetWebUrl
                    }
                }
            };
            var page = new PageIngredientNode
            {
                Id = "artifact:page",
                Kind = PageIngredientKind.PageArtifact,
                Label = "Athena page",
                HasContent = true,
                Ownership = PageIngredientOwnership.SourceOwned
            };
            graph.Nodes.Add(page);
            graph.Edges.Add(new PageIngredientEdge
            {
                FromIngredientId = page.Id,
                ToIngredientId = reference.SourceWebFidelityIngredientId,
                Relationship = PageIngredientRelationship.GovernedBy,
                Requirement = PageIngredientRequirement.Optional
            });
            graph.Edges.Add(new PageIngredientEdge
            {
                FromIngredientId = page.Id,
                ToIngredientId = reference.TargetLeafContainerIngredientId,
                Relationship = PageIngredientRelationship.DependsOn,
                Requirement = PageIngredientRequirement.Required
            });
            var localActions = new List<PageIngredientAction> { Preserve(page.Id) };
            for (var index = 0; index < downstreamCount; index++)
            {
                var id = "downstream:" + index;
                graph.Nodes.Add(new PageIngredientNode
                {
                    Id = id,
                    Kind = index % 5 == 0 ? PageIngredientKind.List
                        : index % 5 == 1 ? PageIngredientKind.Field
                            : index % 5 == 2 ? PageIngredientKind.View
                                : index % 5 == 3 ? PageIngredientKind.Reference
                                    : PageIngredientKind.WebPart,
                    Label = id,
                    HasContent = true,
                    Ownership = PageIngredientOwnership.Shared
                });
                graph.Edges.Add(new PageIngredientEdge
                {
                    FromIngredientId = id,
                    ToIngredientId = reference.TargetLeafContainerIngredientId,
                    Relationship = PageIngredientRelationship.DependsOn,
                    Requirement = PageIngredientRequirement.Required
                });
                localActions.Add(Preserve(id));
            }

            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, localActions);
            Assert.AreNotEqual(PageMigrationOutcome.Blocked, evaluation.Outcome);
            Assert.AreEqual(0, evaluation.Issues.Count, string.Join(Environment.NewLine, evaluation.Issues.Select(value => value.Message)));
            Assert.AreEqual(1, graph.ExternalReferences.Count(value => value.State == PageExternalIngredientState.AuthorizationBlocked));
        }

        private static PageIngredientAction Preserve(string ingredientId)
        {
            return new PageIngredientAction
            {
                ActionId = "action:" + ingredientId,
                IngredientId = ingredientId,
                Capability = IngredientCapability.Available,
                Disposition = IngredientDisposition.Preserve,
                PolicyId = "policy.test",
                PolicyVersion = "1"
            };
        }

        private static SharedTopologyPlanBuildResult BuildAthenaPlan()
        {
            return new PathDerivedTopologyPlanner().Build(AthenaRequest());
        }

        private static PathDerivedTopologyPlanningRequest AthenaRequest()
        {
            return new PathDerivedTopologyPlanningRequest
            {
                Source = PathDerivedSourceTopologyEvidenceFactory.CreateAuthorizationBlocked(
                    SourceSiteId,
                    "https://microsoft.sharepoint.com/teams/athena",
                    "/teams/athena",
                    SourceLeafWebId,
                    "https://microsoft.sharepoint.com/teams/athena/gkb/projects/AthenaWiki",
                    "/teams/athena/gkb/projects/AthenaWiki",
                    "ReadSourceParentWeb",
                    "https://microsoft.sharepoint.com/teams/athena/gkb/projects/AthenaWiki/_api/web/ParentWeb",
                    403,
                    new DateTimeOffset(2026, 9, 4, 1, 2, 3, TimeSpan.Zero)),
                TargetSiteCollectionUrl = "https://microsoft.sharepoint.com/teams/athena-pnp",
                TargetSiteServerRelativeUrl = "/teams/athena-pnp",
                ExpectedTargetSiteId = TargetSiteId,
                ProvisioningPolicy = new PathDerivedTargetWebProvisioningPolicy
                {
                    DefaultTargetTemplate = "STS",
                    DefaultTargetConfiguration = 0,
                    AllowReuseExistingExactPath = true,
                    CollisionPolicy = TargetWebCollisionPolicy.Block
                }
            };
        }

        private static string RecomputeEvidence(PathDerivedSourceTopologyEvidence evidence)
        {
            evidence.EvidenceSha256 = null;
            return PathDerivedSourceTopologyEvidenceFactory.ComputeDigest(evidence);
        }

        private static SharedTopologyTargetAnalysis Analyze(
            SharedTopologyPlan plan,
            IEnumerable<TargetWebContainerObservation> observations)
        {
            return SharedTopologyTargetAnalyzer.Analyze(plan, SiteObservation(), observations);
        }

        private static SharedTopologyTargetSiteObservation SiteObservation()
        {
            return new SharedTopologyTargetSiteObservation
            {
                Exists = true,
                TargetSiteId = TargetSiteId,
                TargetRootWebId = TargetRootWebId,
                TargetSiteCollectionUrl = "https://microsoft.sharepoint.com/teams/athena-pnp"
            };
        }

        private static TargetWebContainerObservation Missing(TargetWebContainerIngredientPlan container)
        {
            return new TargetWebContainerObservation
            {
                IngredientId = container.IngredientId,
                Exists = false,
                TargetSiteId = TargetSiteId,
                TargetParentWebId = TargetRootWebId,
                TargetWebUrl = container.TargetWebUrl,
                TargetServerRelativeUrl = container.TargetServerRelativeUrl
            };
        }

        private static TargetWebContainerObservation Existing(
            SharedTopologyPlan plan,
            TargetWebContainerIngredientPlan container,
            int ordinal)
        {
            return new TargetWebContainerObservation
            {
                IngredientId = container.IngredientId,
                Exists = true,
                TargetSiteId = TargetSiteId,
                TargetWebId = Guid.Parse("00000000-0000-0000-0000-" + ordinal.ToString("D12")),
                TargetParentWebId = ordinal == 1 ? TargetRootWebId : Guid.Parse("00000000-0000-0000-0000-" + (ordinal - 1).ToString("D12")),
                TargetWebUrl = container.TargetWebUrl,
                TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                ExistingTitle = container.Provisioning.Title,
                ExistingTemplate = "STS",
                ExistingConfiguration = 0,
                ExistingIngredientId = container.IngredientId,
                ExistingPlanDigest = plan.PlanDigest
            };
        }

        private static TargetWebContainerObservation Failure(TargetWebContainerIngredientPlan container, int status)
        {
            return new TargetWebContainerObservation
            {
                IngredientId = container.IngredientId,
                HttpStatusCode = status,
                InspectionFailed = true,
                TargetWebUrl = container.TargetWebUrl,
                TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                Diagnostic = "fixture"
            };
        }

        private static string Issues(SharedTopologyPlanBuildResult result)
        {
            return string.Join(Environment.NewLine, result.Issues.Select(value => value.Code + ": " + value.Message));
        }

        private sealed class InMemoryTopologyRuntime : ISharedTopologyTargetRuntime
        {
            private readonly SharedTopologyPlan plan;
            private readonly Dictionary<string, TargetWebContainerObservation> existing = new Dictionary<string, TargetWebContainerObservation>(StringComparer.Ordinal);

            public InMemoryTopologyRuntime(SharedTopologyPlan plan)
            {
                this.plan = plan;
            }

            public int CreateCalls { get; private set; }

            public SharedTopologyTargetSiteObservation InspectTargetSite(SharedTopologyPlan ignored)
            {
                return SiteObservation();
            }

            public IList<TargetWebContainerObservation> InspectTargetWebContainers(SharedTopologyPlan ignored)
            {
                return plan.TargetWebContainers.Select((container, index) => existing.TryGetValue(container.IngredientId, out var observation)
                    ? observation
                    : Missing(container)).ToList();
            }

            public TargetWebContainerObservation CreateTargetWebContainer(SharedTopologyPlan ignored, TargetWebContainerIngredientPlan container)
            {
                CreateCalls++;
                var index = plan.TargetWebContainers.IndexOf(container) + 1;
                var observation = Existing(plan, container, index);
                existing.Add(container.IngredientId, observation);
                return observation;
            }
        }

        private sealed class RecordingJournal : IMigrationExecutionJournal
        {
            public IList<MigrationMutationIntent> Intents { get; } = new List<MigrationMutationIntent>();

            public IList<MigrationMutationReceipt> Receipts { get; } = new List<MigrationMutationReceipt>();

            public IList<MigrationExecutionStateReceipt> States { get; } = new List<MigrationExecutionStateReceipt>();

            public void WriteExecutionState(MigrationExecutionStateReceipt state)
            {
                States.Add(state);
            }

            public void WriteIntent(MigrationMutationIntent intent)
            {
                Intents.Add(intent);
            }

            public void WriteReceipt(MigrationMutationReceipt receipt)
            {
                Receipts.Add(receipt);
            }
        }
    }

}
