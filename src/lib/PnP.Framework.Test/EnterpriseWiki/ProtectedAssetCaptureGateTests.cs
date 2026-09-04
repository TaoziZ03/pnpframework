using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Execution;
using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Lists.Packaging;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Topology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class ProtectedAssetCaptureGateTests
    {
        [TestMethod]
        public void ExplicitMetadataOnlyPolicyDoesNotInvokeBinaryFetcher()
        {
            var calls = 0;
            var policy = ProtectedAssetCapturePolicy.MetadataOnly(
                "policy.test.protected-metadata-only",
                false);
            var protection = Protection();

            var value = ProtectedAssetCaptureGate.Capture(
                protection,
                policy,
                () =>
                {
                    calls++;
                    return "payload";
                },
                out var decision);

            Assert.IsNull(value);
            Assert.AreEqual(0, calls);
            Assert.IsTrue(decision.IsMetadataOnly);
            Assert.AreEqual(ProtectedAssetProtectionState.Protected, decision.ProtectionState);
            Assert.AreEqual("ProtectedPayloadExcludedByPolicy", decision.ReasonCode);
            ProtectedAssetCaptureGate.ValidateDecision(protection, policy, decision);
        }

        [TestMethod]
        public void NullPolicyPreservesHistoricalBinaryCaptureAndCanonicalShape()
        {
            var calls = 0;
            var value = ProtectedAssetCaptureGate.Capture(
                Protection(),
                null,
                () =>
                {
                    calls++;
                    return "payload";
                },
                out var decision);

            Assert.AreEqual("payload", value);
            Assert.AreEqual(1, calls);
            Assert.IsNull(decision);
            Assert.IsFalse(PublishingPagePackageSerializer.SerializeCanonical(new PageCaptureOptions())
                .Contains("protectedAssets", StringComparison.Ordinal));
            Assert.IsFalse(PublishingPagePackageSerializer.SerializeCanonical(new ListDocumentSnapshot())
                .Contains("captureDecision", StringComparison.Ordinal));
            Assert.AreEqual("pnp-publishing-page-export/v2", PublishingPagePackageContract.ExportSchemaVersion);
            Assert.AreEqual("pnp-publishing-page-migration-package/v2", PublishingPagePackageContract.MigrationSchemaVersion);
            Assert.AreEqual("pnp-publishing-page-import-receipt/v4", PublishingPagePackageContract.ReceiptSchemaVersion);
        }

        [TestMethod]
        public void UnknownProtectionIsSkippedOnlyByExplicitFailClosedPolicy()
        {
            var failClosedCalls = 0;
            var failClosedValue = ProtectedAssetCaptureGate.Capture(
                null,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.fail-closed"),
                () =>
                {
                    failClosedCalls++;
                    return "payload";
                },
                out var failClosedDecision);
            var allowCalls = 0;
            var allowedValue = ProtectedAssetCaptureGate.Capture(
                null,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.known-only", false),
                () =>
                {
                    allowCalls++;
                    return "payload";
                },
                out var allowedDecision);

            Assert.IsNull(failClosedValue);
            Assert.AreEqual(0, failClosedCalls);
            Assert.IsTrue(failClosedDecision.IsMetadataOnly);
            Assert.AreEqual("payload", allowedValue);
            Assert.AreEqual(1, allowCalls);
            Assert.AreEqual(ProtectedAssetCaptureDisposition.CaptureBinary, allowedDecision.Disposition);
        }

        [TestMethod]
        public void CaptureDecisionTamperingIsRejectedByPackageValidation()
        {
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.sealed", false);
            var source = ProtectedList(policy);

            ListDependencyPackageValidator.Validate(
                Array.Empty<PnP.Framework.Migration.Pages.ClassicWebParts.ClassicWebPartSnapshot>(),
                Array.Empty<PnP.Framework.Migration.Pages.ClassicWebParts.Bindings.ClassicListWebPartBindingSnapshot>(),
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                null,
                null,
                policy);

            source.Items.Single().Document.CaptureDecision.ReasonCode = "tampered";
            Assert.ThrowsException<InvalidDataException>(() => ListDependencyPackageValidator.Validate(
                Array.Empty<PnP.Framework.Migration.Pages.ClassicWebParts.ClassicWebPartSnapshot>(),
                Array.Empty<PnP.Framework.Migration.Pages.ClassicWebParts.Bindings.ClassicListWebPartBindingSnapshot>(),
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                null,
                null,
                policy));
        }

        [TestMethod]
        public void MetadataOnlyDocumentProducesOneListLocalApprovedExclusion()
        {
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.plan", false);
            var source = ProtectedList(policy);
            var planSet = ListMigrationPlanFactory.Create(
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                Topology(source),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>());

            var plan = planSet.Lists.Single();
            var exclusion = plan.ApprovedProtectedDocumentExclusions.Single();
            Assert.AreEqual(7, exclusion.SourceItemId);
            Assert.AreEqual("/sites/target/Docs/protected.docx", exclusion.TargetServerRelativeUrl);
            Assert.AreEqual(source.Items.Single().Document.CaptureDecision.DecisionDigest, exclusion.CaptureDecisionDigest);
            Assert.IsFalse(plan.Issues.Any(value => value.Code == "ListBinaryEvidenceUnavailable"));
        }

        [TestMethod]
        public void ExistingGraphAndFrontierExcludeOnlyTheProtectedItemSubtree()
        {
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.frontier", false);
            var source = ProtectedList(policy);
            var listPlan = ListMigrationPlanFactory.Create(
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                Topology(source),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>()).Lists.Single();
            var listIngredientId = PublishingPageIngredientIds.List(source.SourceWebId, source.SourceListId);
            var graph = new CanonicalPageIngredientGraph
            {
                ProjectionVersion = PublishingPageIngredientGraphProjector.CurrentProjectionVersion
            };
            graph.Nodes.Add(new PageIngredientNode
            {
                Id = listIngredientId,
                Kind = PageIngredientKind.List,
                HasContent = true,
                Ownership = PageIngredientOwnership.SourceOwned
            });
            PublishingPageListContentIngredientGraphProjector.Project(
                source,
                listIngredientId,
                graph,
                PublishingPageIngredientGraphProjectionRevision.CurrentV7,
                null);
            var actions = new Dictionary<string, PageIngredientAction>(StringComparer.Ordinal)
            {
                [listIngredientId] = PublishingPageIngredientActionFactory.Create(
                    listIngredientId,
                    IngredientCapability.Available,
                    IngredientDisposition.Preserve,
                    "create-or-reuse-list",
                    "policy.test",
                    "The owning List remains executable.")
            };
            PublishingPageListContentIngredientActionProjector.Project(
                source,
                listPlan,
                false,
                actions,
                true);

            var itemId = PublishingPageIngredientIds.ListItem(source.SourceWebId, source.SourceListId, 7);
            var documentId = PublishingPageIngredientIds.ListDocument(source.SourceWebId, source.SourceListId, 7);
            var policyId = PublishingPageIngredientIds.ListDocumentInformationProtection(source.SourceWebId, source.SourceListId, 7);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, actions.Values);

            Assert.AreEqual(IngredientDisposition.Drop, actions[itemId].Disposition);
            Assert.AreEqual(IngredientDisposition.Drop, actions[documentId].Disposition);
            Assert.AreEqual(IngredientDisposition.Drop, actions[policyId].Disposition);
            Assert.AreEqual(PageMigrationOutcome.ExecutableWithLoss, evaluation.Outcome);
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState(listIngredientId));
            Assert.AreEqual(PageIngredientExecutionState.ExcludedByApprovedDisposition, evaluation.ExecutionFrontier.GetState(itemId));
            Assert.AreEqual(PageIngredientExecutionState.ExcludedByApprovedDisposition, evaluation.ExecutionFrontier.GetState(documentId));
        }

        [TestMethod]
        public void SourceAssessmentTreatsMetadataOnlyDocumentAsDeterminedExclusion()
        {
            var source = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.assessment", false));
            var planSet = ListMigrationPlanFactory.Create(
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                Topology(source),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>());
            var listIngredientId = PublishingPageIngredientIds.List(source.SourceWebId, source.SourceListId);
            var graph = new CanonicalPageIngredientGraph
            {
                ProjectionVersion = PublishingPageIngredientGraphProjector.CurrentProjectionVersion
            };
            graph.Nodes.Add(new PageIngredientNode
            {
                Id = listIngredientId,
                Kind = PageIngredientKind.List,
                HasContent = true,
                Ownership = PageIngredientOwnership.SourceOwned
            });
            PublishingPageListContentIngredientGraphProjector.Project(
                source,
                listIngredientId,
                graph,
                PublishingPageIngredientGraphProjectionRevision.CurrentV7,
                null);
            var accumulator = new PublishingPageAssessmentAccumulator(graph);
            PublishingPageListAssessmentProjector.Project(
                new PublishingPageAssessmentContext
                {
                    Snapshot = new PublishingPageCaptureBundle
                    {
                        IngredientGraph = graph,
                        ListDependencies = new List<ListDependencySnapshot> { source }
                    },
                    ListPlan = planSet
                },
                accumulator);
            var assessments = accumulator.Complete().ToDictionary(value => value.IngredientId);

            var itemId = PublishingPageIngredientIds.ListItem(source.SourceWebId, source.SourceListId, 7);
            var documentId = PublishingPageIngredientIds.ListDocument(source.SourceWebId, source.SourceListId, 7);
            var informationProtectionId = PublishingPageIngredientIds.ListDocumentInformationProtection(source.SourceWebId, source.SourceListId, 7);
            Assert.AreEqual(PnP.Framework.Migration.Pages.Assessment.PageIngredientAssessmentState.Determined, assessments[itemId].State);
            Assert.AreEqual(IngredientDisposition.Drop, assessments[itemId].ProposedDisposition);
            Assert.AreEqual(PnP.Framework.Migration.Pages.Assessment.PageIngredientAssessmentState.Determined, assessments[documentId].State);
            Assert.AreEqual(IngredientDisposition.Drop, assessments[documentId].ProposedDisposition);
            Assert.AreEqual(PnP.Framework.Migration.Pages.Assessment.PageIngredientAssessmentState.Determined, assessments[informationProtectionId].State);
            Assert.AreEqual(IngredientDisposition.Drop, assessments[informationProtectionId].ProposedDisposition);
        }

        [TestMethod]
        public void ProtectedExclusionTracksAListLeafCollisionRetarget()
        {
            var source = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.collision", false));
            var listPlan = ListMigrationPlanFactory.Create(
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                Topology(source),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>()).Lists.Single();

            ListMigrationPlanFactory.RetargetProtectedDocumentExclusions(
                listPlan,
                "/sites/target/Docs",
                "/sites/target/Docs-pnp-1234");

            Assert.AreEqual(
                "/sites/target/Docs-pnp-1234/protected.docx",
                listPlan.ApprovedProtectedDocumentExclusions.Single().TargetServerRelativeUrl);
        }

        [DataTestMethod]
        [DataRow(404, ProtectedDocumentTargetAbsenceStatus.Absent)]
        [DataRow(401, ProtectedDocumentTargetAbsenceStatus.AuthorizationBlocked)]
        [DataRow(403, ProtectedDocumentTargetAbsenceStatus.AuthorizationBlocked)]
        [DataRow(409, ProtectedDocumentTargetAbsenceStatus.RetryableFailure)]
        [DataRow(423, ProtectedDocumentTargetAbsenceStatus.RetryableFailure)]
        [DataRow(429, ProtectedDocumentTargetAbsenceStatus.RetryableFailure)]
        [DataRow(503, ProtectedDocumentTargetAbsenceStatus.RetryableFailure)]
        [DataRow(418, ProtectedDocumentTargetAbsenceStatus.Failed)]
        public void ExpectedAbsentProbePreservesHttpSemantics(
            int httpStatusCode,
            ProtectedDocumentTargetAbsenceStatus expected)
        {
            Assert.AreEqual(expected, ProtectedDocumentTargetAbsenceProbe.ClassifyHttpStatus(httpStatusCode));
        }

        private static ListDocumentInformationProtectionSnapshot Protection()
        {
            return new ListDocumentInformationProtectionSnapshot
            {
                LabelId = "9fbde396-1a24-4c79-8edf-9254a0f35055",
                AssignmentMethod = "1",
                HasUserDefinedProtection = "0",
                LabelHash = "label-hash;00",
                Availability = EvidenceAvailability.Captured
            };
        }

        private static ListDependencySnapshot ProtectedList(ProtectedAssetCapturePolicy policy)
        {
            var protection = Protection();
            var decision = ProtectedAssetCaptureGate.Decide(protection, policy);
            return new ListDependencySnapshot
            {
                SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SourceWebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                SourceWebUrl = "https://source.example/sites/source",
                SourceListId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Title = "Docs",
                Description = string.Empty,
                BaseTemplate = 101,
                BaseType = "DocumentLibrary",
                RootFolderServerRelativeUrl = "/sites/source/Docs",
                SourceItemCount = 1,
                Items = new List<ListItemSnapshot>
                {
                    new ListItemSnapshot
                    {
                        SourceItemId = 7,
                        SourceUniqueId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        Document = new ListDocumentSnapshot
                        {
                            Kind = ListDocumentObjectKind.File,
                            Name = "protected.docx",
                            ServerRelativeUrl = "/sites/source/Docs/protected.docx",
                            Length = 123,
                            MajorVersion = 1,
                            InformationProtection = protection,
                            CaptureDecision = decision,
                            Content = null
                        }
                    }
                },
                Availability = EvidenceAvailability.Captured
            };
        }

        private static TopologyPlan Topology(ListDependencySnapshot source)
        {
            return new TopologyPlan
            {
                SiteCollections = new List<SiteCollectionMappingPlan>
                {
                    new SiteCollectionMappingPlan
                    {
                        SourceSiteId = source.SourceSiteId,
                        SourceSiteCollectionUrl = source.SourceWebUrl,
                        TargetSiteCollectionUrl = "https://target.example/sites/target",
                        Webs = new List<WebMappingPlan>
                        {
                            new WebMappingPlan
                            {
                                Kind = TopologyNodeKind.SiteCollectionRoot,
                                SourceSiteId = source.SourceSiteId,
                                SourceWebId = source.SourceWebId,
                                SourceSiteCollectionUrl = source.SourceWebUrl,
                                SourceWebUrl = source.SourceWebUrl,
                                SourceServerRelativeUrl = "/sites/source",
                                TargetSiteCollectionUrl = "https://target.example/sites/target",
                                TargetWebUrl = "https://target.example/sites/target",
                                TargetServerRelativeUrl = "/sites/target"
                            }
                        }
                    }
                }
            };
        }
    }
}
