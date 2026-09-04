using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
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
        private static readonly Guid SourceWebId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TargetSiteId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid TargetRootWebId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        [TestMethod]
        public void PathValidationRejectsDoubleEncodingAndCanonicalizesUnicode()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                SharedTopologyPath.NormalizeServerRelativePath("/sites/target/%252fescape", "path"));
            Assert.ThrowsException<ArgumentException>(() =>
                SharedTopologyPath.NormalizeServerRelativePath("/sites/target/%255cescape", "path"));
            Assert.ThrowsException<ArgumentException>(() =>
                SharedTopologyPath.NormalizeServerRelativePath("/sites/target/%252e%252e", "path"));
            Assert.ThrowsException<ArgumentException>(() =>
                SharedTopologyPath.NormalizeServerRelativePath("/sites/target/a\u0001b", "path"));

            var composed = SharedTopologyPath.NormalizeServerRelativePath("/sites/target/Caf\u00e9", "path");
            var decomposed = SharedTopologyPath.NormalizeServerRelativePath("/sites/target/Cafe\u0301", "path");
            Assert.AreEqual(composed, decomposed);
            Assert.AreEqual(
                SharedTopologyIdentity.TargetSlot(composed),
                SharedTopologyIdentity.TargetSlot(decomposed));
        }

        [TestMethod]
        public void GlobalDagDeduplicatesEquivalentActionsAndRejectsSlotSignatureConflict()
        {
            var first = BuildPlan("groups/engineering/guides", "STS#0");
            var equivalent = SharedTopologyGlobalActionDagCompiler.Compile(new[] { first, first });
            Assert.IsTrue(equivalent.IsExecutable);
            Assert.AreEqual(first.TargetWebContainers.Count, equivalent.Dag.Actions.Count);

            var conflicting = BuildPlan("groups/engineering/guides", "BLOG#0");
            var result = SharedTopologyGlobalActionDagCompiler.Compile(new[] { first, conflicting });
            Assert.IsFalse(result.IsExecutable);
            Assert.IsNull(result.Dag);
            Assert.IsTrue(result.Issues.Any(value => value.Code == "SharedTopologyTargetSlotSignatureConflict"));

            var permissionConflict = BuildPlan("groups/engineering/guides", "STS#0", null, false);
            var permissionResult = SharedTopologyGlobalActionDagCompiler.Compile(new[] { first, permissionConflict });
            Assert.IsFalse(permissionResult.IsExecutable);
            Assert.IsTrue(permissionResult.Issues.Any(value => value.Code == "SharedTopologyTargetSlotSignatureConflict"));
        }

        [TestMethod]
        public void UnownedExactPathIsBlockedUnlessExactHostIdentityWasApproved()
        {
            var unapproved = BuildPlan("guides", "STS#0");
            var unapprovedDag = Compile(unapproved);
            var unapprovedRuntime = new FakeRuntime(unapprovedDag);
            unapprovedRuntime.SetUnowned(unapprovedDag.Actions.Single().GlobalActionKey);
            var blocked = PathDerivedTopologyTargetAnalyzer.Analyze(
                unapprovedDag,
                unapprovedRuntime.Inspect(unapprovedDag.Actions));
            Assert.AreEqual(TargetWebContainerState.CollisionBlocked, blocked.Probes.Single().State);

            var approvedWebId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var approved = BuildPlan("guides", "STS#0", approvedWebId);
            var approvedDag = Compile(approved);
            var approvedRuntime = new FakeRuntime(approvedDag);
            approvedRuntime.SetExternal(approvedDag.Actions.Single().GlobalActionKey, approvedWebId);
            var admitted = PathDerivedTopologyTargetAnalyzer.Analyze(
                approvedDag,
                approvedRuntime.Inspect(approvedDag.Actions));
            Assert.AreEqual(TargetWebContainerState.ReuseExplicitApprovedHost, admitted.Probes.Single().State);
            Assert.AreEqual(SharedTopologyOwnership.ExternalApprovedHost, admitted.Probes.Single().Ownership);
        }

        [TestMethod]
        public void LiteralAuthorizationNeedsActualDigestValidEvidence()
        {
            var plan = BuildPlan("guides", "STS#0");
            var dag = Compile(plan);
            var action = dag.Actions.Single();
            var missingEvidence = PathDerivedTopologyTargetAnalyzer.Analyze(dag, new[]
            {
                new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = action.GlobalActionKey,
                    HttpStatusCode = 403
                }
            });
            Assert.AreEqual(TargetWebContainerState.CollisionBlocked, missingEvidence.Probes.Single().State);

            var authorization = LiteralHttpAuthorizationEvidence.Create(
                "InspectPathDerivedTargetWeb",
                action.TargetParentWebUrl,
                403,
                DateTimeOffset.Parse("2026-09-04T00:00:00Z"));
            var literal = PathDerivedTopologyTargetAnalyzer.Analyze(dag, new[]
            {
                new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = action.GlobalActionKey,
                    HttpStatusCode = 403,
                    AuthorizationEvidence = authorization
                }
            });
            Assert.AreEqual(TargetWebContainerState.AuthorizationBlocked, literal.Probes.Single().State);

            var textOnly = PathDerivedTopologyTargetAnalyzer.Analyze(dag, new[]
            {
                new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = action.GlobalActionKey,
                    InspectionFailed = true,
                    Diagnostic = "server said 403 in a message"
                }
            });
            Assert.AreEqual(TargetWebContainerState.RetryRequired, textOnly.Probes.Single().State);
        }

        [TestMethod]
        public void MaterializerFreshProbesEveryActionAndSafelyConvergesCreateRace()
        {
            var plan = BuildPlan("groups/engineering", "STS#0");
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);
            runtime.ResetCounters();
            runtime.RaceOnCreateKey = dag.Actions.First().GlobalActionKey;
            var journal = new InMemoryMigrationExecutionJournal();

            var result = new PathDerivedTopologyMigrationService().Ensure(
                runtime,
                dag,
                analysis,
                actionPlan,
                new[] { plan },
                journal);

            Assert.IsTrue(result.Receipt.FreshReadbackPassed);
            Assert.IsTrue(result.Receipt.SourceFidelityAuthorizationLimited);
            Assert.AreEqual(dag.Actions.Count, result.Receipt.Actions.Count);
            Assert.AreEqual(1, result.Receipt.SourceWebMappings.Count);
            Assert.AreEqual(SourceWebId, result.Receipt.SourceWebMappings.Single().SourceWebId);
            Assert.AreEqual(dag.Actions.Last().GlobalActionKey, result.Receipt.SourceWebMappings.Single().TargetGlobalActionKey);
            Assert.IsTrue(result.Receipt.Actions.All(value => value.FinalState == TargetWebContainerState.ReuseOwned));
            Assert.IsTrue(dag.Actions.All(value => runtime.InspectCounts[value.GlobalActionKey] >= 2));
            Assert.AreEqual(dag.Actions.Count, runtime.CreateCalls);
            Assert.IsTrue(journal.Intents.Count >= dag.Actions.Count);
            Assert.IsTrue(journal.Receipts.Any(value => value.Outcome == MutationOutcome.Failed));
            SharedTopologyGlobalExecutionValidator.ValidateReceipt(dag, actionPlan, result.Receipt);
            SharedTopologyPageReferenceFactory.ValidateReceipt(
                SharedTopologyPageReferenceFactory.Create(plan, SourceSiteId, SourceWebId),
                result.Receipt);
        }

        [TestMethod]
        public void FreshDriftRequiresReplanAndDoesNotOverwriteForeignMarkers()
        {
            var plan = BuildPlan("groups/engineering", "STS#0");
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);
            runtime.ResetCounters();
            var first = dag.Actions.First();
            runtime.SetConflictingMarkers(first.GlobalActionKey);

            Assert.ThrowsException<InvalidOperationException>(() =>
                new PathDerivedTopologyMigrationService().Ensure(
                    runtime,
                    dag,
                    analysis,
                    actionPlan,
                    new[] { plan }));
            Assert.AreEqual(0, runtime.CreateCalls);
            Assert.AreEqual(0, runtime.RecoverCalls);
            Assert.AreEqual("urn:foreign:web", runtime.Current(first.GlobalActionKey).ExistingOriginalIdentifier);
            Assert.AreEqual("foreign-digest", runtime.Current(first.GlobalActionKey).ExistingMappingDigest);
        }

        [TestMethod]
        public void ExternalApprovedHostReceiptKeepsExternalOwnershipAndWritesNoMarker()
        {
            var approvedWebId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var plan = BuildPlan("guides", "STS#0", approvedWebId);
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            runtime.SetExternal(dag.Actions.Single().GlobalActionKey, approvedWebId);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);
            runtime.ResetCounters();

            var result = new PathDerivedTopologyMigrationService().Ensure(
                runtime,
                dag,
                analysis,
                actionPlan,
                new[] { plan });

            var receipt = result.Receipt.Actions.Single();
            Assert.AreEqual(SharedTopologyOwnership.ExternalApprovedHost, receipt.Ownership);
            Assert.AreEqual(TargetWebContainerState.ReuseExplicitApprovedHost, receipt.FinalState);
            Assert.IsFalse(receipt.ChangedTarget);
            Assert.AreEqual(0, runtime.CreateCalls);
            Assert.AreEqual(0, runtime.RecoverCalls);
            Assert.IsNull(runtime.Current(receipt.GlobalActionKey).ExistingOriginalIdentifier);
            Assert.IsNull(runtime.Current(receipt.GlobalActionKey).ExistingMappingDigest);
        }

        [TestMethod]
        public void InterruptedCreateIsRecoveredFromExactFingerprint()
        {
            var plan = BuildPlan("guides", "STS#0");
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var action = dag.Actions.Single();
            runtime.SetInterrupted(action.GlobalActionKey);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);
            runtime.ResetCounters();

            var result = new PathDerivedTopologyMigrationService().Ensure(
                runtime,
                dag,
                analysis,
                actionPlan,
                new[] { plan });

            Assert.AreEqual(1, runtime.RecoverCalls);
            Assert.AreEqual(TargetWebContainerState.ReuseOwned, result.Receipt.Actions.Single().FinalState);
            Assert.AreEqual(action.OriginalIdentifier, runtime.Current(action.GlobalActionKey).ExistingOriginalIdentifier);
            Assert.AreEqual(action.ActionSignatureDigest, runtime.Current(action.GlobalActionKey).ExistingMappingDigest);
        }

        [TestMethod]
        public void ReceiptRejectsChangedParentIdentityEvenWithRecomputedDigest()
        {
            var plan = BuildPlan("groups/engineering", "STS#0");
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
            result.Receipt.Actions.Last().TargetParentWebId = Guid.NewGuid();
            result.Receipt.ReceiptDigest = SharedTopologyGlobalExecutionDigest.ComputeReceipt(result.Receipt);

            Assert.ThrowsException<InvalidDataException>(() =>
                SharedTopologyGlobalExecutionValidator.ValidateReceipt(dag, actionPlan, result.Receipt));
        }

        [TestMethod]
        public void SourceAuthorizationLimitationDoesNotBlockTargetActionsAndSurvivesPageReference()
        {
            var plan = BuildPlan("groups/engineering/guides", "STS#0");
            Assert.AreEqual(SourceWebFidelityState.AuthorizationBlocked, plan.SourceWebFidelityIngredients.Single().State);
            var reference = SharedTopologyPageReferenceFactory.Create(plan, SourceSiteId, SourceWebId);
            Assert.AreEqual(SourceWebFidelityState.AuthorizationBlocked, reference.SourceFidelityState);
            Assert.AreEqual(403, reference.SourceAuthorizationEvidence.HttpStatusCode);
            Assert.AreEqual(plan.TargetWebContainers.Count, reference.RequiredGlobalActionKeys.Count);

            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            Assert.IsTrue(analysis.IsExecutable);
            Assert.IsTrue(SharedTopologyGlobalActionPlanProjector.Project(dag, analysis).IsExecutable);
        }

        [TestMethod]
        public void ActionPlanCannotChangeReviewedDispositionEvenWhenDigestIsResealed()
        {
            var plan = BuildPlan("guides", "STS#0");
            var dag = Compile(plan);
            var runtime = new FakeRuntime(dag);
            var analysis = PathDerivedTopologyTargetAnalyzer.Analyze(dag, runtime.Inspect(dag.Actions));
            var actionPlan = SharedTopologyGlobalActionPlanProjector.Project(dag, analysis);
            actionPlan.Actions.Single().SelectedAction = SharedTopologyActionKind.ReuseOwned;
            actionPlan.ActionPlanDigest = SharedTopologyGlobalExecutionDigest.ComputeActionPlan(actionPlan);

            Assert.ThrowsException<InvalidDataException>(() =>
                SharedTopologyGlobalExecutionValidator.ValidateActionPlan(dag, analysis, actionPlan));
        }

        [TestMethod]
        public void PageGraphReferencesSharedProducerAndKeepsSource403Optional()
        {
            var plan = BuildPlan("groups/engineering/guides", "STS#0");
            var reference = SharedTopologyPageReferenceFactory.Create(plan, SourceSiteId, SourceWebId);
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = new PageIdentity
                {
                    SiteId = SourceSiteId,
                    WebId = SourceWebId
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
                        }
                    }
                }
            };

            var graph = PublishingPagePathDerivedTopologyIngredientGraphProjector.Project(snapshot, plan, reference);

            Assert.AreEqual("pnp-page-ingredient-graph/v2", graph.SchemaVersion);
            Assert.AreEqual(
                plan.TargetWebContainers.Count + 1,
                graph.ExternalReferences.Count);
            var fidelity = graph.ExternalReferences.Single(value => value.IngredientId == reference.SourceWebFidelityIngredientId);
            Assert.AreEqual(PageExternalIngredientState.AuthorizationBlocked, fidelity.State);
            Assert.IsTrue(graph.Edges.Any(value => value.ToIngredientId == fidelity.IngredientId
                && value.Requirement == PageIngredientRequirement.Optional));
            Assert.IsTrue(graph.Edges.Any(value => value.ToIngredientId == reference.TargetLeafContainerIngredientId
                && value.Requirement == PageIngredientRequirement.Required));
        }

        [TestMethod]
        public void LegacyPublishingContractsOmitAbsentSharedTopologyExtensions()
        {
            var planJson = PublishingPagePackageSerializer.Serialize(new PublishingPageMigrationPlan());
            var receiptJson = PublishingPagePackageSerializer.Serialize(new PublishingPageImportReceipt());

            Assert.IsFalse(planJson.Contains("sharedTopologyReference", StringComparison.Ordinal));
            Assert.IsFalse(receiptJson.Contains("sharedTopologyMaterialization", StringComparison.Ordinal));
        }

        private static SharedTopologyPlan BuildPlan(
            string relativePath,
            string template,
            Guid? approvedLeafWebId = null,
            bool useSamePermissionsAsParent = true)
        {
            var sourcePath = "/sites/source/" + relativePath;
            var policy = new PathDerivedTargetWebProvisioningPolicy
            {
                DefaultTargetTemplate = template,
                DefaultTargetConfiguration = 0,
                DefaultTargetLanguage = 1033,
                DefaultUseSamePermissionsAsParentWeb = useSamePermissionsAsParent
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
                Source = PathDerivedSourceTopologyEvidenceFactory.CreateAuthorizationBlocked(
                    SourceSiteId,
                    "https://source.example.com/sites/source",
                    "/sites/source",
                    SourceWebId,
                    "https://source.example.com" + sourcePath,
                    sourcePath,
                    "CaptureRequiredWebClosure",
                    "https://source.example.com/sites/source/_api/web",
                    403,
                    DateTimeOffset.Parse("2026-09-04T00:00:00Z")),
                TargetSiteCollectionUrl = "https://target.example.com/sites/target",
                TargetSiteServerRelativeUrl = "/sites/target",
                ExpectedTargetSiteId = TargetSiteId,
                ProvisioningPolicy = policy
            });
            Assert.IsTrue(result.IsExecutable, string.Join("; ", result.Issues.Select(value => value.Message)));
            return result.Plan;
        }

        private static SharedTopologyGlobalActionDag Compile(SharedTopologyPlan plan)
        {
            var result = SharedTopologyGlobalActionDagCompiler.Compile(new[] { plan });
            Assert.IsTrue(result.IsExecutable, string.Join("; ", result.Issues.Select(value => value.Message)));
            return result.Dag;
        }

        private sealed class FakeRuntime : IPathDerivedTopologyTargetRuntime
        {
            private readonly IDictionary<string, TargetWebContainerIngredientPlan> containers;
            private readonly IDictionary<string, PathDerivedTargetWebObservation> observations;
            private readonly IDictionary<string, Guid> targetWebIds;

            public FakeRuntime(SharedTopologyGlobalActionDag dag)
            {
                containers = dag.Actions.ToDictionary(value => value.GlobalActionKey, StringComparer.Ordinal);
                targetWebIds = dag.Actions.ToDictionary(value => value.GlobalActionKey, value => Guid.NewGuid(), StringComparer.Ordinal);
                observations = dag.Actions.ToDictionary(
                    value => value.GlobalActionKey,
                    value => Missing(value),
                    StringComparer.Ordinal);
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
                CreateCalls++;
                SetOwned(container.GlobalActionKey);
                if (string.Equals(RaceOnCreateKey, container.GlobalActionKey, StringComparison.Ordinal))
                {
                    RaceOnCreateKey = null;
                    throw new InvalidOperationException("simulated create race after another writer completed the same signature");
                }
                return Clone(observations[container.GlobalActionKey]);
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
                return Clone(observations[container.GlobalActionKey]);
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

            public void SetInterrupted(string key)
            {
                var container = containers[key];
                SetExisting(
                    key,
                    targetWebIds[key],
                    null,
                    null,
                    PathDerivedTopologyTargetAnalyzer.InterruptedCreateDescription(container));
            }

            public void SetConflictingMarkers(string key)
            {
                SetExisting(key, targetWebIds[key], "urn:foreign:web", "foreign-digest", "foreign Web");
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
                    container.ActionSignatureDigest,
                    PathDerivedTopologyTargetAnalyzer.InterruptedCreateDescription(container));
            }

            private void SetExisting(
                string key,
                Guid webId,
                string originalIdentifier,
                string mappingDigest,
                string description)
            {
                var container = containers[key];
                observations[key] = new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = key,
                    Exists = true,
                    TargetSiteId = TargetSiteId,
                    TargetWebId = webId,
                    TargetParentWebId = ParentWebId(container),
                    TargetWebUrl = container.TargetWebUrl,
                    TargetServerRelativeUrl = container.TargetServerRelativeUrl,
                    ExistingTitle = container.Provisioning.Title,
                    ExistingTemplate = container.Provisioning.Template.Split('#')[0],
                    ExistingConfiguration = container.Provisioning.Configuration,
                    ExistingHasUniqueRoleAssignments = !container.Provisioning.UseSamePermissionsAsParentWeb,
                    ExistingDescription = description,
                    ExistingOriginalIdentifier = originalIdentifier,
                    ExistingMappingDigest = mappingDigest
                };
            }

            private PathDerivedTargetWebObservation Missing(TargetWebContainerIngredientPlan container)
            {
                return new PathDerivedTargetWebObservation
                {
                    GlobalActionKey = container.GlobalActionKey,
                    Exists = false,
                    TargetSiteId = TargetSiteId,
                    TargetParentWebId = ParentWebId(container),
                    TargetWebUrl = container.TargetWebUrl,
                    TargetServerRelativeUrl = container.TargetServerRelativeUrl
                };
            }

            private Guid ParentWebId(TargetWebContainerIngredientPlan container)
            {
                return string.IsNullOrWhiteSpace(container.ParentGlobalActionKey)
                    ? TargetRootWebId
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
