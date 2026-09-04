using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Lists.Views;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Topology.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class PathDerivedSharedTopologyTests
    {
        private static readonly Guid SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid SourceRootWebId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        private static readonly Guid SourceLeafWebId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TargetSiteId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid TargetRootWebId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        [TestMethod]
        public void DecodedPathValidationRejectsQuestionFragmentReservedDoubleEncodingAndControl()
        {
            Assert.ThrowsException<ArgumentException>(() => Normalize("/sites/target/%3fquery"));
            Assert.ThrowsException<ArgumentException>(() => Normalize("/sites/target/%23fragment"));
            Assert.ThrowsException<ArgumentException>(() => Normalize("/sites/target/%3areserved"));
            Assert.ThrowsException<ArgumentException>(() => Normalize("/sites/target/%252fescape"));
            Assert.ThrowsException<ArgumentException>(() => Normalize("/sites/target/a\u0001b"));
            Assert.AreEqual(Normalize("/sites/target/Caf\u00e9"), Normalize("/sites/target/Cafe\u0301"));
        }

        [TestMethod]
        public void TargetSlotSeparatesCrossTenantSamePathAndSiteFence()
        {
            var first = BuildPlan("groups/engineering", targetAuthority: "https://target-a.example.com");
            var second = BuildPlan(
                "groups/engineering",
                sourceAuthority: "https://source-b.example.com",
                targetAuthority: "https://target-b.example.com",
                sourceSiteId: Guid.Parse("01010101-0101-0101-0101-010101010101"),
                sourceRootWebId: Guid.Parse("02020202-0202-0202-0202-020202020202"),
                sourceLeafWebId: Guid.Parse("03030303-0303-0303-0303-030303030303"),
                targetSiteId: Guid.Parse("04040404-0404-0404-0404-040404040404"),
                targetRootWebId: Guid.Parse("05050505-0505-0505-0505-050505050505"));
            Assert.AreNotEqual(first.TargetWebContainers.Last().TargetSlotKey, second.TargetWebContainers.Last().TargetSlotKey);
            Assert.AreNotEqual(first.ExecutionGroupDigest, second.ExecutionGroupDigest);
            Assert.AreEqual(first.SupportCohortDigest, second.SupportCohortDigest);

            var differentFence = BuildPlan(
                "groups/engineering",
                targetAuthority: "https://target-a.example.com",
                targetSiteId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            Assert.AreNotEqual(first.TargetWebContainers.Last().TargetSlotKey, differentFence.TargetWebContainers.Last().TargetSlotKey);
        }

        [TestMethod]
        public void PartialTopologyRetainsCapturedRootAndLeafWithIndependentUnknownAncestors()
        {
            var plan = BuildPlan("groups/engineering/guides");
            var ordered = plan.SourceWebFidelityIngredients
                .OrderBy(value => SharedTopologyPath.Depth(value.SourceServerRelativeUrl))
                .ToArray();
            Assert.AreEqual(4, ordered.Length);
            Assert.AreEqual(SourceRootWebId, ordered[0].SourceWebId);
            Assert.AreEqual(SourceWebFidelityState.Captured, ordered[0].State);
            Assert.AreEqual(Guid.Empty, ordered[1].SourceWebId);
            Assert.AreEqual(Guid.Empty, ordered[2].SourceWebId);
            Assert.AreEqual(SourceWebFidelityState.AuthorizationBlocked, ordered[1].State);
            Assert.AreEqual(SourceWebFidelityState.AuthorizationBlocked, ordered[2].State);
            Assert.AreEqual(SourceLeafWebId, ordered[3].SourceWebId);
            Assert.AreEqual(SourceWebFidelityState.Captured, ordered[3].State);
            Assert.AreEqual(ordered.Length, plan.TargetWebContainers.Count);
            Assert.AreEqual(ordered.Length, plan.SourceWebBindings.Count);
            Assert.IsTrue(plan.TargetWebContainers.First().IsTargetSiteRoot);
            Assert.AreEqual(SharedTopologyOwnership.ExternalApprovedHost, plan.TargetWebContainers.First().ExpectedOwnership);
        }

        [TestMethod]
        public void GlobalDagDeduplicatesEquivalentGenericSignaturesAndBlocksDifferentProfile()
        {
            var first = BuildPlan("groups/engineering");
            var equivalent = SharedTopologyGlobalActionDagCompiler.Compile(new[] { first, first });
            Assert.IsTrue(equivalent.IsExecutable);
            Assert.AreEqual(first.TargetWebContainers.Count, equivalent.Dag.Actions.Count);

            var permissionConflict = BuildPlan("groups/engineering", useSamePermissions: false);
            var conflict = SharedTopologyGlobalActionDagCompiler.Compile(new[] { first, permissionConflict });
            Assert.IsFalse(conflict.IsExecutable);
            Assert.IsTrue(conflict.Issues.Any(value => value.Code == "SharedTopologyTargetSlotSignatureConflict"));
        }

        [TestMethod]
        public void RootOwnerMappingSupportsRootContentTypeAndStyleLibraryForPage318Shape()
        {
            var plan = BuildPlan("groups/engineering/guides");
            var mappings = TopologyWebOwnerMappingCatalog.FromShared(plan);
            Assert.AreEqual(4, mappings.Count);
            var root = mappings.Single(value => value.SourceWebId == SourceRootWebId);
            Assert.AreEqual("/sites/target", root.TargetServerRelativeUrl);

            var contentType = new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                Availability = EvidenceAvailability.Captured,
                SourceWebUrl = "https://source.example.com/sites/source",
                SourceScope = "/sites/source",
                ContentTypeId = "0x010100AA",
                Name = "Root-owned page support",
                ParentContentTypeId = "0x0101",
                ParentContentTypeName = "Document"
            };
            var closure = ContentTypeClosurePlanner.CreateFromOwnerMappings(
                new[] { contentType },
                mappings,
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>());
            Assert.AreEqual(1, closure.Nodes.Count);
            Assert.AreEqual("https://target.example.com/sites/target", closure.Nodes[0].TargetOwnerWebUrl);

            var list = CreateMinimalLeafList();
            list.ViewRenderingResources.Add(new ListViewRenderingResourceSnapshot
            {
                Id = "root-style",
                Kind = ListViewRenderingResourceKind.JavaScript,
                SourceAbsoluteUrl = "https://source.example.com/sites/source/Style%20Library/root.js",
                SourceServerRelativeUrl = "/sites/source/Style Library/root.js",
                Availability = EvidenceAvailability.Partial
            });
            var listPlan = ListMigrationPlanFactory.CreateFromSharedTopology(
                new[] { list },
                Array.Empty<ListLookupDependency>(),
                plan,
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>());
            Assert.AreEqual(
                "/sites/target/Style Library/root.js",
                listPlan.Lists.Single().ViewRenderingResources.Single().TargetServerRelativeUrl);
        }

        [TestMethod]
        public void LiteralAuthorizationEvidenceIsBoundToOperationAuthorityUriAndAction()
        {
            var evidence = CreateEvidence("groups/engineering");
            BoundLiteralHttpAuthorizationEvidence.Validate(
                evidence.AncestorAuthorizationEvidence,
                PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadActionId);

            evidence.AncestorAuthorizationEvidence.ActionId = "wrong-action";
            evidence.AncestorAuthorizationEvidence.EvidenceSha256 = BoundLiteralHttpAuthorizationEvidence.ComputeDigest(
                evidence.AncestorAuthorizationEvidence);
            Assert.ThrowsException<InvalidDataException>(() =>
                BoundLiteralHttpAuthorizationEvidence.Validate(
                    evidence.AncestorAuthorizationEvidence,
                    PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadActionId));

            var uriEvidence = CreateEvidence("groups/engineering").AncestorAuthorizationEvidence;
            uriEvidence.ExpectedRequestUri = "https://different.example.com/_vti_bin/client.svc/ProcessQuery";
            uriEvidence.ExpectedAuthority = "different.example.com";
            uriEvidence.EvidenceSha256 = BoundLiteralHttpAuthorizationEvidence.ComputeDigest(uriEvidence);
            Assert.ThrowsException<InvalidDataException>(() =>
                BoundLiteralHttpAuthorizationEvidence.Validate(
                    uriEvidence,
                    PathDerivedSourceTopologyEvidenceFactory.SourceAncestorReadActionId));

            var plan = BuildPlan("guides");
            var dag = Compile(plan);
            var child = dag.Actions.Last();
            var targetRequest = child.TargetParentWebUrl + "/_vti_bin/client.svc/ProcessQuery";
            var wrongActionEvidence = BoundLiteralHttpAuthorizationEvidence.Create(
                "wrong-target-action",
                "InspectPathDerivedTargetWeb",
                targetRequest,
                LiteralHttpAuthorizationEvidence.Create(
                    "InspectPathDerivedTargetWeb",
                    targetRequest,
                    403,
                    DateTimeOffset.Parse("2026-09-04T00:00:00Z")));
            var targetAnalysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, new[]
            {
                ExactExternalRoot(dag.Actions.First()),
                new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = child.GlobalActionKey,
                    HttpStatusCode = 403,
                    AuthorizationEvidence = wrongActionEvidence
                }
            });
            Assert.AreEqual(TargetWebContainerState.CollisionBlocked, targetAnalysis.Probes.Last().State);
        }

        [TestMethod]
        public void Http404AndIdentityFreeMissingEvidenceNeverAuthorizeChildCreation()
        {
            var plan = BuildPlan("guides");
            var dag = Compile(plan);
            var root = dag.Actions.First();
            var child = dag.Actions.Last();
            var http404 = PathDerivedTopologyTargetAnalyzer.Analyze(dag, new[]
            {
                ExactExternalRoot(root),
                new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = child.GlobalActionKey,
                    HttpStatusCode = 404
                }
            });
            Assert.AreEqual(TargetWebContainerState.CollisionBlocked, http404.Probes.Last().State);

            var unboundMissing = PathDerivedTopologyTargetAnalyzer.Analyze(dag, new[]
            {
                ExactExternalRoot(root),
                new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = child.GlobalActionKey,
                    Exists = false,
                    TargetWebUrl = child.TargetWebUrl,
                    TargetServerRelativeUrl = child.TargetServerRelativeUrl
                }
            });
            Assert.AreEqual(TargetWebContainerState.CollisionBlocked, unboundMissing.Probes.Last().State);
        }

        [TestMethod]
        public void UnownedChildIsBlockedUnlessExactExternalHostWasApproved()
        {
            var plan = BuildPlan("guides");
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            runtime.SetUnowned(dag.Actions.Last().GlobalActionKey);
            var blocked = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            Assert.AreEqual(TargetWebContainerState.CollisionBlocked, blocked.Probes.Single(value => value.GlobalActionKey == dag.Actions.Last().GlobalActionKey).State);

            var approvedId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var approved = BuildPlan("guides", approvedLeafWebId: approvedId);
            var approvedDag = Compile(approved);
            var approvedRuntime = new FakeRuntime(approvedDag);
            approvedRuntime.SetExternal(approvedDag.Actions.Last().GlobalActionKey, approvedId);
            var admitted = PathDerivedTopologyTargetAnalyzer.Analyze(approvedDag, approvedRuntime.Inspect(approvedDag.Actions));
            Assert.AreEqual(TargetWebContainerState.ReuseExplicitApprovedHost, admitted.Probes.Last().State);
        }

        [TestMethod]
        public void MaterializerUsesGenericSignedJournalAndRecordsResponseLossConvergence()
        {
            var plan = BuildPlan("groups/engineering");
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);
            runtime.ResetCounters();
            runtime.RaceOnCreateKey = dag.Actions.First(value => !value.IsTargetSiteRoot).GlobalActionKey;
            var journal = new InMemoryMigrationExecutionJournal();

            var result = new PathDerivedTopologyMigrationService().Ensure(
                runtime,
                dag,
                analysis,
                actionPlan,
                new[] { plan },
                journal);

            var converged = result.Receipt.Actions.Single(value =>
                value.ExecutionOutcome == SharedTopologyActionExecutionOutcome.OutcomeUnknownButConverged);
            Assert.IsTrue(converged.MutationAttempted);
            Assert.IsTrue(converged.FreshReadbackPassed);
            Assert.AreEqual(MutationOutcome.OutcomeUnknownButConverged,
                journal.Receipts.Single(value => value.ActionSignature == converged.ActionSignature).Outcome);
            Assert.IsTrue(journal.Intents.All(value => !string.IsNullOrWhiteSpace(value.ActionSignature)));
            Assert.AreEqual(dag.Actions.Count, journal.Verifications.Count);
            Assert.IsTrue(journal.Verifications.All(value => value.FreshReadbackPassed));
            SharedTopologyGlobalExecutionValidator.ValidateReceipt(new[] { plan }, dag, actionPlan, result.Receipt);
        }

        [TestMethod]
        public void PageReferencePinsPlanDagActionPlanAndEveryRequiredAction()
        {
            var context = Execute(BuildPlan("groups/engineering/guides"));
            var reference = SharedTopologyPageReferenceFactory.Create(
                context.Plan,
                context.Dag,
                context.ActionPlan,
                SourceSiteId,
                SourceLeafWebId);
            Assert.AreEqual(context.Plan.PlanDigest, reference.SharedPlanDigest);
            Assert.AreEqual(context.Dag.DagDigest, reference.GlobalActionDagDigest);
            Assert.AreEqual(context.ActionPlan.ActionPlanDigest, reference.ActionPlanDigest);
            Assert.AreEqual(context.Plan.ExecutionGroupDigest, reference.ExecutionGroupDigest);
            Assert.AreEqual(context.Plan.SupportCohortDigest, reference.SupportCohortDigest);
            Assert.AreEqual(context.Plan.TargetWebContainers.Count, reference.RequiredActions.Count);
            Assert.IsTrue(reference.RequiredActions.All(value =>
                !string.IsNullOrWhiteSpace(value.TargetSlotKey)
                && value.ActionSignature != null
                && !string.IsNullOrWhiteSpace(value.OriginalIdentifier)));
            SharedTopologyPageReferenceFactory.ValidateReceipt(
                reference,
                new[] { context.Plan },
                context.Dag,
                context.ActionPlan,
                context.Result.Receipt);

            reference.SourceFidelity[1].EvidenceDigest = new string('a', 64);
            Assert.ThrowsException<InvalidDataException>(() =>
                SharedTopologyPageReferenceFactory.ValidateReceipt(
                    reference,
                    new[] { context.Plan },
                    context.Dag,
                    context.ActionPlan,
                    context.Result.Receipt));
        }

        [TestMethod]
        public void TamperedAggregateResealCannotHideStaleNestedActionReceipt()
        {
            var context = Execute(BuildPlan("groups/engineering"));
            var child = context.Result.Receipt.Actions.Last();
            child.TargetWebId = Guid.NewGuid();
            context.Result.Receipt.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeReceipt(context.Result.Receipt);

            Assert.ThrowsException<InvalidDataException>(() =>
                SharedTopologyGlobalExecutionValidator.ValidateReceipt(
                    new[] { context.Plan },
                    context.Dag,
                    context.ActionPlan,
                    context.Result.Receipt));
        }

        [TestMethod]
        public void ResealedActionPlanTamperStillFailsPinnedPageReference()
        {
            var context = Execute(BuildPlan("guides"));
            var reference = SharedTopologyPageReferenceFactory.Create(
                context.Plan,
                context.Dag,
                context.ActionPlan,
                SourceSiteId,
                SourceLeafWebId);
            context.ActionPlan.Actions.Last().SelectedAction = SharedTopologyActionKind.ReuseOwned;
            context.ActionPlan.Actions.Last().ReviewedState = TargetWebContainerState.ReuseOwned;
            context.ActionPlan.ActionPlanDigest = SharedTopologyGlobalExecutionDigest.ComputeActionPlan(context.ActionPlan);

            Assert.ThrowsException<InvalidDataException>(() =>
                SharedTopologyPageReferenceFactory.ValidateReceipt(
                    reference,
                    new[] { context.Plan },
                    context.Dag,
                    context.ActionPlan,
                    context.Result.Receipt));
        }

        [TestMethod]
        public void ResealedDagMustStillRecompileFromTheSuppliedSourcePlan()
        {
            var plan = BuildPlan("groups/engineering");
            var dag = Compile(plan);
            dag.Actions.Last().CollisionResolutionReason = "resealed drift outside the source plan";
            dag.DagDigest = SharedTopologyGlobalActionDagCompiler.ComputeDigest(dag);
            var runtime = new FakeRuntime(dag);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);

            Assert.ThrowsException<InvalidDataException>(() =>
                new PathDerivedTopologyMigrationService().Ensure(
                    runtime,
                    dag,
                    analysis,
                    actionPlan,
                    new[] { plan }));
            Assert.AreEqual(0, runtime.CreateCalls);
        }

        [TestMethod]
        public void ResealedReceiptCannotClaimMutationForAReviewedReuseAction()
        {
            var plan = BuildPlan("guides");
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var firstAnalysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var firstPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, firstAnalysis);
            new PathDerivedTopologyMigrationService().Ensure(
                runtime,
                dag,
                firstAnalysis,
                firstPlan,
                new[] { plan });

            var reuseAnalysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var reusePlan = SharedTopologyGlobalActionPlanProjector.Project(dag, reuseAnalysis);
            var reuse = new PathDerivedTopologyMigrationService().Ensure(
                runtime,
                dag,
                reuseAnalysis,
                reusePlan,
                new[] { plan }).Receipt;
            var child = reuse.Actions.Last();
            Assert.AreEqual(SharedTopologyActionKind.ReuseOwned, child.SelectedAction);
            child.MutationAttempted = true;
            child.ExecutionOutcome = SharedTopologyActionExecutionOutcome.Applied;
            child.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeActionReceipt(child);
            reuse.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeReceipt(reuse);

            Assert.ThrowsException<InvalidDataException>(() =>
                SharedTopologyGlobalExecutionValidator.ValidateReceipt(
                    new[] { plan },
                    dag,
                    reusePlan,
                    reuse));
        }

        [TestMethod]
        public void PageGraphKeepsUnknownAncestorsOptionalAndTargetActionsRequired()
        {
            var context = Execute(BuildPlan("groups/engineering/guides"));
            var reference = SharedTopologyPageReferenceFactory.Create(
                context.Plan,
                context.Dag,
                context.ActionPlan,
                SourceSiteId,
                SourceLeafWebId);
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = new PageIdentity { SiteId = SourceSiteId, WebId = SourceLeafWebId },
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
                        }
                    }
                }
            };
            var graph = PublishingPagePathDerivedTopologyIngredientGraphProjector.Project(snapshot, context.Plan, reference);
            Assert.AreEqual("pnp-page-ingredient-graph/v2", graph.SchemaVersion);
            Assert.AreEqual(reference.SourceFidelity.Count + reference.RequiredActions.Count, graph.ExternalReferences.Count);
            Assert.IsTrue(reference.SourceFidelity.Where(value => value.State == SourceWebFidelityState.AuthorizationBlocked)
                .All(fidelity => graph.Edges.Any(edge => edge.ToIngredientId == fidelity.IngredientId
                    && edge.Requirement == PageIngredientRequirement.Optional)));
            Assert.IsTrue(graph.Edges.Any(edge => edge.ToIngredientId == reference.TargetLeafContainerIngredientId
                && edge.Requirement == PageIngredientRequirement.Required));
        }

        [TestMethod]
        public void LegacyPublishingContractsOmitAbsentSharedTopologyExtensions()
        {
            var planJson = PublishingPagePackageSerializer.Serialize(new PublishingPageMigrationPlan());
            var receiptJson = PublishingPagePackageSerializer.Serialize(new PublishingPageImportReceipt());
            Assert.IsFalse(planJson.Contains("sharedTopologyReference", StringComparison.Ordinal));
            Assert.IsFalse(receiptJson.Contains("sharedTopologyMaterialization", StringComparison.Ordinal));
        }

        private static ExecutionContext Execute(SharedTopologyPlan plan)
        {
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);
            var result = new PathDerivedTopologyMigrationService().Ensure(
                runtime,
                dag,
                analysis,
                actionPlan,
                new[] { plan });
            return new ExecutionContext
            {
                Plan = plan,
                Dag = dag,
                ActionPlan = actionPlan,
                Result = result
            };
        }

        private static SharedTopologyPlan BuildPlan(
            string relativePath,
            string sourceAuthority = "https://source.example.com",
            string targetAuthority = "https://target.example.com",
            Guid? sourceSiteId = null,
            Guid? sourceRootWebId = null,
            Guid? sourceLeafWebId = null,
            Guid? targetSiteId = null,
            Guid? targetRootWebId = null,
            bool useSamePermissions = true,
            Guid? approvedLeafWebId = null)
        {
            var policy = new PathDerivedTargetWebProvisioningPolicy
            {
                DefaultTargetTemplate = "STS#0",
                DefaultTargetConfiguration = 0,
                DefaultTargetLanguage = 1033,
                DefaultUseSamePermissionsAsParentWeb = useSamePermissions
            };
            if (approvedLeafWebId.HasValue)
            {
                policy.ApprovedExistingWebs.Add(new TargetWebApprovedHost
                {
                    SourceRelativePath = relativePath,
                    ExpectedTargetWebId = approvedLeafWebId.Value
                });
            }
            var result = new PathDerivedTopologyPlanner().Build(new PathDerivedTopologyPlanningRequest
            {
                Source = CreateEvidence(
                    relativePath,
                    sourceAuthority,
                    sourceSiteId ?? SourceSiteId,
                    sourceRootWebId ?? SourceRootWebId,
                    sourceLeafWebId ?? SourceLeafWebId),
                TargetSiteCollectionUrl = targetAuthority + "/sites/target",
                TargetSiteServerRelativeUrl = "/sites/target",
                ExpectedTargetSiteId = targetSiteId ?? TargetSiteId,
                ExpectedTargetRootWebId = targetRootWebId ?? TargetRootWebId,
                TargetRootTitle = "Target root",
                TargetRootTemplate = "STS#3",
                TargetRootConfiguration = 3,
                TargetRootLanguage = 1033,
                TargetRootHasUniqueRoleAssignments = false,
                ProvisioningPolicy = policy
            });
            Assert.IsTrue(result.IsExecutable, string.Join("; ", result.Issues.Select(value => value.Message)));
            return result.Plan;
        }

        private static PathDerivedSourceTopologyEvidence CreateEvidence(
            string relativePath,
            string sourceAuthority = "https://source.example.com",
            Guid? siteId = null,
            Guid? rootWebId = null,
            Guid? leafWebId = null)
        {
            var sourceSite = siteId ?? SourceSiteId;
            var rootId = rootWebId ?? SourceRootWebId;
            var leafId = leafWebId ?? SourceLeafWebId;
            var sourceRootUrl = sourceAuthority + "/sites/source";
            var leafPath = "/sites/source/" + relativePath;
            var requestUri = sourceRootUrl + "/_vti_bin/client.svc/ProcessQuery";
            var literal = LiteralHttpAuthorizationEvidence.Create(
                "ReadSourceParentWeb",
                requestUri,
                403,
                DateTimeOffset.Parse("2026-09-04T00:00:00Z"));
            return PathDerivedSourceTopologyEvidenceFactory.CreateAuthorizationBlocked(
                new SourceWebSnapshot
                {
                    SiteId = sourceSite,
                    WebId = rootId,
                    SiteCollectionUrl = sourceRootUrl,
                    WebUrl = sourceRootUrl,
                    ServerRelativeUrl = "/sites/source",
                    Title = "Source root",
                    WebTemplate = "STS",
                    Configuration = 3,
                    Availability = EvidenceAvailability.Captured
                },
                new SourceWebSnapshot
                {
                    SiteId = sourceSite,
                    WebId = leafId,
                    SiteCollectionUrl = sourceRootUrl,
                    WebUrl = sourceAuthority + leafPath,
                    ServerRelativeUrl = leafPath,
                    Title = "Source leaf",
                    WebTemplate = "STS",
                    Configuration = 0,
                    Availability = EvidenceAvailability.Captured
                },
                "ReadSourceParentWeb",
                requestUri,
                literal);
        }

        private static ListDependencySnapshot CreateMinimalLeafList()
        {
            return new ListDependencySnapshot
            {
                SourceSiteId = SourceSiteId,
                SourceWebId = SourceLeafWebId,
                SourceWebUrl = "https://source.example.com/sites/source/groups/engineering/guides",
                SourceListId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                Title = "Data",
                BaseTemplate = 100,
                BaseType = "GenericList",
                RootFolderServerRelativeUrl = "/sites/source/groups/engineering/guides/Lists/Data",
                SourceItemCount = 0,
                Availability = EvidenceAvailability.Captured
            };
        }

        private static SharedTopologyGlobalActionDag Compile(SharedTopologyPlan plan)
        {
            var result = SharedTopologyGlobalActionDagCompiler.Compile(new[] { plan });
            Assert.IsTrue(result.IsExecutable, string.Join("; ", result.Issues.Select(value => value.Message)));
            return result.Dag;
        }

        private static string Normalize(string path)
        {
            return SharedTopologyPath.NormalizeServerRelativePath(path, nameof(path));
        }

        private static PathDerivedTargetWebObservation ExactExternalRoot(TargetWebContainerIngredientPlan root)
        {
            return new PathDerivedTargetWebObservation
            {
                GlobalActionKey = root.GlobalActionKey,
                Exists = true,
                TargetSiteId = root.ExpectedTargetSiteId,
                TargetWebId = root.ApprovedExistingTargetWebId,
                TargetWebUrl = root.TargetWebUrl,
                TargetServerRelativeUrl = root.TargetServerRelativeUrl,
                ExistingTitle = root.Provisioning.Title,
                ExistingTemplate = root.Provisioning.Template.Split('#')[0],
                ExistingConfiguration = root.Provisioning.Configuration,
                ExistingLanguage = root.Provisioning.Language,
                ExistingHasUniqueRoleAssignments = !root.Provisioning.UseSamePermissionsAsParentWeb,
                ExistingDescription = "ordinary external Web"
            };
        }

        private sealed class ExecutionContext
        {
            public SharedTopologyPlan Plan { get; set; }

            public SharedTopologyGlobalActionDag Dag { get; set; }

            public SharedTopologyGlobalActionPlan ActionPlan { get; set; }

            public PathDerivedTopologyMigrationExecutionResult Result { get; set; }
        }

        private sealed class FakeRuntime : IPathDerivedTopologyTargetRuntime
        {
            private readonly IDictionary<string, TargetWebContainerIngredientPlan> containers;
            private readonly IDictionary<string, PathDerivedTargetWebObservation> observations;
            private readonly IDictionary<string, Guid> targetWebIds;

            public FakeRuntime(SharedTopologyGlobalActionDag dag)
            {
                containers = dag.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
                targetWebIds = dag.Actions.ToDictionary(
                    value => value.GlobalActionKey,
                    value => value.ApprovedExistingTargetWebId ?? Guid.NewGuid(),
                    StringComparer.Ordinal);
                observations = new Dictionary<string, PathDerivedTargetWebObservation>(StringComparer.Ordinal);
                foreach (var container in dag.Actions)
                {
                    if (container.IsTargetSiteRoot)
                    {
                        SetExternal(container.GlobalActionKey, container.ApprovedExistingTargetWebId.Value);
                    }
                    else
                    {
                        observations[container.GlobalActionKey] = Missing(container);
                    }
                }
                InspectCounts = dag.Actions.ToDictionary(value => value.GlobalActionKey, value => 0, StringComparer.Ordinal);
            }

            public IDictionary<string, int> InspectCounts { get; }

            public int CreateCalls { get; private set; }

            public int RecoverCalls { get; private set; }

            public string RaceOnCreateKey { get; set; }

            public IList<PathDerivedTargetWebObservation> Inspect(IEnumerable<TargetWebContainerIngredientPlan> requested)
            {
                return requested.Select(value =>
                {
                    InspectCounts[value.GlobalActionKey]++;
                    return Clone(observations[value.GlobalActionKey]);
                }).ToList();
            }

            public PathDerivedTargetWebObservation Create(TargetWebContainerIngredientPlan container)
            {
                if (container.IsTargetSiteRoot)
                {
                    throw new InvalidOperationException("root create is not allowed");
                }
                CreateCalls++;
                SetOwned(container.GlobalActionKey);
                if (string.Equals(RaceOnCreateKey, container.GlobalActionKey, StringComparison.Ordinal))
                {
                    RaceOnCreateKey = null;
                    throw new InvalidOperationException("simulated lost create response");
                }
                return Current(container.GlobalActionKey);
            }

            public PathDerivedTargetWebObservation RecoverOwnership(TargetWebContainerIngredientPlan container)
            {
                RecoverCalls++;
                var current = observations[container.GlobalActionKey];
                if (!string.IsNullOrWhiteSpace(current.ExistingOriginalIdentifier)
                    || !string.IsNullOrWhiteSpace(current.ExistingMappingDigest))
                {
                    throw new InvalidOperationException("conflicting marker");
                }
                SetOwned(container.GlobalActionKey);
                return Current(container.GlobalActionKey);
            }

            public void SetUnowned(string key)
            {
                SetExisting(key, targetWebIds[key], null, null, "ordinary external Web");
            }

            public void SetExternal(string key, Guid webId)
            {
                targetWebIds[key] = webId;
                SetExisting(key, webId, null, null, "ordinary external Web");
            }

            public PathDerivedTargetWebObservation Current(string key)
            {
                return Clone(observations[key]);
            }

            public void ResetCounters()
            {
                foreach (var key in InspectCounts.Keys.ToArray())
                {
                    InspectCounts[key] = 0;
                }
                CreateCalls = 0;
                RecoverCalls = 0;
            }

            private void SetOwned(string key)
            {
                var container = containers[key];
                SetExisting(
                    key,
                    targetWebIds[key],
                    container.OriginalIdentifier,
                    container.SemanticMappingDigest,
                    PathDerivedTopologyTargetAnalyzer.InterruptedCreateDescription(container));
            }

            private void SetExisting(string key, Guid webId, string original, string mapping, string description)
            {
                var container = containers[key];
                observations[key] = new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = key,
                    Exists = true,
                    TargetSiteId = container.ExpectedTargetSiteId,
                    TargetWebId = webId,
                    TargetParentWebId = container.IsTargetSiteRoot ? (Guid?)null : ParentWebId(container),
                    TargetWebUrl = container.TargetWebUrl,
                    TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                    ExistingTitle = container.Provisioning.Title,
                    ExistingTemplate = container.Provisioning.Template.Split('#')[0],
                    ExistingConfiguration = container.Provisioning.Configuration,
                    ExistingLanguage = container.Provisioning.Language,
                    ExistingHasUniqueRoleAssignments = !container.Provisioning.UseSamePermissionsAsParentWeb,
                    ExistingDescription = description,
                    ExistingOriginalIdentifier = original,
                    ExistingMappingDigest = mapping
                };
            }

            private PathDerivedTargetWebObservation Missing(TargetWebContainerIngredientPlan container)
            {
                return new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = container.GlobalActionKey,
                    Exists = false,
                    TargetSiteId = container.ExpectedTargetSiteId,
                    TargetParentWebId = ParentWebId(container),
                    TargetWebUrl = container.TargetWebUrl,
                    TargetServerRelativeUrl = container.TargetServerRelativeUrl
                };
            }

            private Guid ParentWebId(TargetWebContainerIngredientPlan container)
            {
                return container.IsTargetSiteRoot
                    ? Guid.Empty
                    : targetWebIds[container.ParentGlobalActionKey];
            }

            private static PathDerivedTargetWebObservation Clone(PathDerivedTargetWebObservation value)
            {
                return new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = value.GlobalActionKey,
                    HttpStatusCode = value.HttpStatusCode,
                    AuthorizationEvidence = value.AuthorizationEvidence,
                    InspectionFailed = value.InspectionFailed,
                    IdentityConflict = value.IdentityConflict,
                    Exists = value.Exists,
                    TargetSiteId = value.TargetSiteId,
                    TargetWebId = value.TargetWebId,
                    TargetParentWebId = value.TargetParentWebId,
                    TargetWebUrl = value.TargetWebUrl,
                    TargetServerRelativeUrl = value.TargetServerRelativeUrl,
                    ExistingTitle = value.ExistingTitle,
                    ExistingTemplate = value.ExistingTemplate,
                    ExistingConfiguration = value.ExistingConfiguration,
                    ExistingLanguage = value.ExistingLanguage,
                    ExistingHasUniqueRoleAssignments = value.ExistingHasUniqueRoleAssignments,
                    ExistingDescription = value.ExistingDescription,
                    ExistingOriginalIdentifier = value.ExistingOriginalIdentifier,
                    ExistingMappingDigest = value.ExistingMappingDigest,
                    Diagnostic = value.Diagnostic
                };
            }
        }
    }
}
