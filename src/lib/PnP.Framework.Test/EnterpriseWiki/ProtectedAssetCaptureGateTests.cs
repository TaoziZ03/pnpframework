using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Execution;
using PnP.Framework.Migration.Lists.Fields;
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
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.Publishing.Reporting;
using PnP.Framework.Migration.Pages.Publishing.Reporting.Sections;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Schema.Fields;
using PnP.Framework.Migration.Topology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

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
                "policy.test.protected-metadata-only");
            var protection = Protection();

            var value = ProtectedAssetCaptureGate.Capture(
                protection,
                false,
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
            ProtectedAssetCaptureGate.ValidateDecision(protection, false, policy, decision);
        }

        [TestMethod]
        public void NullPolicyPreservesHistoricalBinaryCaptureAndCanonicalShape()
        {
            var calls = 0;
            var value = ProtectedAssetCaptureGate.Capture(
                Protection(),
                false,
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
        public void UnknownProtectionFailsClosedAndUnsafePolicyIsRejectedBeforeFetch()
        {
            var failClosedCalls = 0;
            var failClosedValue = ProtectedAssetCaptureGate.Capture(
                null,
                false,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.fail-closed"),
                () =>
                {
                    failClosedCalls++;
                    return "payload";
                },
                out var failClosedDecision);
            var unsafePolicy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.unsafe");
            unsafePolicy.FailClosedOnUnknown = false;
            var unsafeCalls = 0;

            Assert.IsNull(failClosedValue);
            Assert.AreEqual(0, failClosedCalls);
            Assert.IsTrue(failClosedDecision.IsMetadataOnly);
            Assert.ThrowsException<InvalidDataException>(() => ProtectedAssetCaptureGate.Capture(
                null,
                false,
                unsafePolicy,
                () =>
                {
                    unsafeCalls++;
                    return "payload";
                },
                out _));
            Assert.AreEqual(0, unsafeCalls);
        }

        [TestMethod]
        public void ReaderPathTreatsEmptyLabelAndUserDefinedProtectionAsProtected()
        {
            var calls = 0;
            var content = ListItemSnapshotReader.CaptureDocumentContent(
                new Dictionary<string, object>
                {
                    ["_IpLabelId"] = string.Empty,
                    ["_HasUserDefinedProtection"] = "1"
                },
                false,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.user-protected"),
                () =>
                {
                    calls++;
                    return CapturedBinary();
                },
                out var protection,
                out var decision);

            Assert.IsNull(content);
            Assert.AreEqual(0, calls);
            Assert.IsNotNull(protection);
            Assert.AreEqual(string.Empty, protection.LabelId);
            Assert.AreEqual("1", protection.HasUserDefinedProtection);
            Assert.AreEqual(ProtectedAssetProtectionState.Protected, decision.ProtectionState);
            Assert.IsTrue(decision.IsMetadataOnly);
        }

        [TestMethod]
        public void ReaderPathTreatsIrmEnabledLibraryAsProtectedBeforeBinaryRead()
        {
            var calls = 0;
            var content = ListItemSnapshotReader.CaptureDocumentContent(
                new Dictionary<string, object>
                {
                    ["_IpLabelId"] = string.Empty,
                    ["_HasUserDefinedProtection"] = "0"
                },
                true,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.irm"),
                () =>
                {
                    calls++;
                    return CapturedBinary();
                },
                out _,
                out var decision);

            Assert.IsNull(content);
            Assert.AreEqual(0, calls);
            Assert.IsTrue(decision.SourceListIrmEnabled);
            Assert.AreEqual(ProtectedAssetProtectionState.Protected, decision.ProtectionState);
            Assert.IsTrue(decision.IsMetadataOnly);
        }

        [TestMethod]
        public void ReaderPathFetchesOnlyWhenNegativeProtectionEvidenceIsComplete()
        {
            var calls = 0;
            var expected = CapturedBinary();
            var content = ListItemSnapshotReader.CaptureDocumentContent(
                new Dictionary<string, object>
                {
                    ["_IpLabelId"] = string.Empty,
                    ["_HasUserDefinedProtection"] = "0"
                },
                false,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.safe"),
                () =>
                {
                    calls++;
                    return expected;
                },
                out var protection,
                out var decision);

            Assert.AreSame(expected, content);
            Assert.AreEqual(1, calls);
            Assert.IsTrue(protection.LabelFieldObserved);
            Assert.IsTrue(protection.UserDefinedProtectionFieldObserved);
            Assert.AreEqual(ProtectedAssetProtectionState.Unprotected, decision.ProtectionState);
            Assert.AreEqual(ProtectedAssetCaptureDisposition.SafeToCapture, decision.Disposition);
            Assert.IsTrue(decision.IsSafeToCapture);
            ProtectedAssetCaptureGate.ValidateDecision(protection, false, ProtectedAssetCapturePolicy.MetadataOnly("policy.test.safe"), decision);
        }

        [TestMethod]
        public void ReaderPathDoesNotTreatMissingUserProtectionEvidenceAsUnprotected()
        {
            var calls = 0;
            var content = ListItemSnapshotReader.CaptureDocumentContent(
                new Dictionary<string, object> { ["_IpLabelId"] = string.Empty },
                false,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.incomplete-negative-evidence"),
                () =>
                {
                    calls++;
                    return CapturedBinary();
                },
                out _,
                out var decision);

            Assert.IsNull(content);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(ProtectedAssetProtectionState.Unknown, decision.ProtectionState);
            Assert.IsTrue(decision.IsMetadataOnly);
        }

        [TestMethod]
        public void MetadataOnlyTamperedDocumentArtifactIsRejectedWithoutArtifactRead()
        {
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.zero-read-document");
            var source = ProtectedList(policy);
            source.Items.Single().Document.Content = CapturedBinary();
            var store = new SpyArtifactStore();

            Assert.ThrowsException<InvalidDataException>(() => ValidateSource(source, policy, store));
            Assert.AreEqual(0, store.OpenReadCalls);
        }

        [TestMethod]
        public void MetadataOnlyTamperedAttachmentArtifactIsRejectedWithoutArtifactRead()
        {
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.zero-read-attachment");
            var source = ProtectedList(policy);
            source.Items.Single().Attachments.Add(new ListAttachmentSnapshot
            {
                FileName = "tampered.bin",
                ServerRelativeUrl = "/sites/source/Docs/Attachments/7/tampered.bin",
                Content = CapturedBinary()
            });
            var store = new SpyArtifactStore();

            Assert.ThrowsException<InvalidDataException>(() => ValidateSource(source, policy, store));
            Assert.AreEqual(0, store.OpenReadCalls);
        }

        [TestMethod]
        public void CaptureDecisionTamperingIsRejectedByPackageValidation()
        {
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.sealed");
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
            source.Items.Single().Attachments.Add(new ListAttachmentSnapshot
            {
                FileName = "must-not-open.bin",
                Content = CapturedBinary()
            });
            var store = new SpyArtifactStore();
            Assert.ThrowsException<InvalidDataException>(() => ValidateSource(source, policy, store));
            Assert.AreEqual(0, store.OpenReadCalls);
        }

        [TestMethod]
        public void MetadataOnlyDocumentProducesOneListLocalApprovedExclusion()
        {
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.plan");
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
            var policy = ProtectedAssetCapturePolicy.MetadataOnly("policy.test.frontier");
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
            var source = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.assessment"));
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
        public void LookupConsumerWithoutPolicyIsDeferredWhileIndependentSiblingContinues()
        {
            var scenario = CreateLookupScenario(null);
            var consumerPlan = scenario.PlanSet.Lists.Single(value => value.SourceListId == scenario.Consumer.SourceListId);
            var providerItemId = PublishingPageIngredientIds.ListItem(
                scenario.Provider.SourceWebId,
                scenario.Provider.SourceListId,
                7);
            var independentItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                10);
            var dependentItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                11);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(scenario.Graph, scenario.Actions.Values);

            Assert.IsFalse(scenario.PlanSet.IsExecutable);
            Assert.AreEqual(
                DroppedLookupValueDisposition.NeedsPolicyDecision,
                consumerPlan.DroppedLookupValueDependencies.Single().Disposition);
            Assert.IsTrue(scenario.Graph.Edges.Any(value =>
                value.FromIngredientId == dependentItemId
                && value.ToIngredientId == providerItemId
                && value.Requirement == PageIngredientRequirement.Required));
            Assert.AreEqual(IngredientDisposition.Defer, scenario.Actions[dependentItemId].Disposition);
            Assert.AreEqual(PageIngredientExecutionState.Deferred, evaluation.ExecutionFrontier.GetState(dependentItemId));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState(independentItemId));
            Assert.AreEqual(PageIngredientExecutionState.ExcludedByApprovedDisposition, evaluation.ExecutionFrontier.GetState(providerItemId));
        }

        [TestMethod]
        public void ClearLookupPolicyReleasesOnlyExcludedProviderValueDependency()
        {
            var scenario = CreateLookupScenario(DroppedLookupValuePolicy.Clear("policy.test.lookup.clear"));
            var dependentItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                11);
            var independentItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                10);
            var providerItemId = PublishingPageIngredientIds.ListItem(
                scenario.Provider.SourceWebId,
                scenario.Provider.SourceListId,
                7);
            var action = scenario.Actions[dependentItemId];
            var evaluation = PageIngredientPlanEvaluator.Evaluate(scenario.Graph, scenario.Actions.Values);

            Assert.IsTrue(scenario.PlanSet.IsExecutable);
            Assert.AreEqual(IngredientDisposition.Transform, action.Disposition);
            CollectionAssert.AreEqual(new[] { providerItemId }, action.ReleasedDependencyIngredientIds.ToArray());
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState(dependentItemId));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState(independentItemId));
            Assert.IsFalse(evaluation.Issues.Any(value => value.Code == "RequiredIngredientDependencyUnsatisfied"));
        }

        [TestMethod]
        public void DropDependentLookupPolicyExcludesDependentButNotIndependentSibling()
        {
            var scenario = CreateLookupScenario(DroppedLookupValuePolicy.DropDependent("policy.test.lookup.drop"));
            var dependentItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                11);
            var independentItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                10);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(scenario.Graph, scenario.Actions.Values);

            Assert.IsTrue(scenario.PlanSet.IsExecutable);
            Assert.AreEqual(IngredientDisposition.Drop, scenario.Actions[dependentItemId].Disposition);
            Assert.AreEqual(PageIngredientExecutionState.ExcludedByApprovedDisposition, evaluation.ExecutionFrontier.GetState(dependentItemId));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState(independentItemId));
        }

        [TestMethod]
        public void ProtectedExclusionTracksAListLeafCollisionRetarget()
        {
            var source = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.collision"));
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

        [TestMethod]
        public void ProtectedExclusionTracksFullResolvedCollisionPath()
        {
            var source = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.collision.full-path"));
            var listPlan = ListMigrationPlanFactory.Create(
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                Topology(source),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>()).Lists.Single();
            var resolution = ListTargetPathResolver.Resolve(
                listPlan,
                source.BaseTemplate,
                new[]
                {
                    new ListTargetInventoryItem
                    {
                        ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        RootFolderServerRelativeUrl = listPlan.PreferredTargetRootFolderServerRelativeUrl,
                        Title = listPlan.PreferredTargetTitle,
                        BaseTemplate = source.BaseTemplate
                    }
                });

            ListMigrationPlanFactory.RetargetProtectedDocumentExclusions(
                listPlan,
                listPlan.TargetRootFolderServerRelativeUrl,
                resolution.TargetRootFolderServerRelativeUrl);

            Assert.IsTrue(resolution.CollisionResolved);
            StringAssert.StartsWith(resolution.TargetRootFolderServerRelativeUrl, "/sites/target/Docs-pnp-");
            Assert.AreEqual(
                resolution.TargetRootFolderServerRelativeUrl + "/protected.docx",
                listPlan.ApprovedProtectedDocumentExclusions.Single().TargetServerRelativeUrl);
        }

        [TestMethod]
        public void LegacyV2CaptureOptionsGoldenRoundTripsWithoutNewOptionalProperties()
        {
            const string golden = "{\"sourcePageServerRelativeUrl\":\"/Pages/Legacy.aspx\",\"includeWebParts\":true,\"maximumDependencyBytes\":10485760}";

            var options = PublishingPagePackageSerializer.Deserialize<PageCaptureOptions>(golden);

            Assert.IsNull(options.ProtectedAssets);
            Assert.AreEqual(golden, PublishingPagePackageSerializer.SerializeCanonical(options));
            Assert.AreEqual("pnp-publishing-page-export/v2", PublishingPagePackageContract.ExportSchemaVersion);
            Assert.AreEqual("pnp-publishing-page-migration-package/v2", PublishingPagePackageContract.MigrationSchemaVersion);
            Assert.AreEqual("pnp-publishing-page-import-receipt/v4", PublishingPagePackageContract.ReceiptSchemaVersion);
        }

        [TestMethod]
        public void ExclusionPlanReportAndReceiptRetainPerItemOutcome()
        {
            var source = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.report"));
            var planSet = ListMigrationPlanFactory.Create(
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                Topology(source),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>());
            var writer = new MarkdownReportWriter();
            ListDependencyMigrationReportSection.Append(
                writer,
                new PublishingPageCaptureBundle
                {
                    ListDependencies = new List<ListDependencySnapshot> { source }
                },
                new PublishingPageMigrationPlan { ListMigration = planSet });
            var report = writer.ToString();
            var exclusion = planSet.Lists.Single().ApprovedProtectedDocumentExclusions.Single();
            var receipt = new ListMaterializationReceipt
            {
                ProtectedDocumentExclusionVerifications = new List<ListProtectedDocumentExclusionVerification>
                {
                    new ListProtectedDocumentExclusionVerification
                    {
                        SourceItemId = exclusion.SourceItemId,
                        SourceServerRelativeUrl = exclusion.SourceServerRelativeUrl,
                        TargetServerRelativeUrl = exclusion.TargetServerRelativeUrl,
                        PolicyId = exclusion.PolicyId,
                        CaptureDecisionDigest = exclusion.CaptureDecisionDigest,
                        Status = ProtectedDocumentTargetAbsenceStatus.Absent,
                        HttpStatusCode = 404,
                        Diagnostic = "The excluded path is absent."
                    }
                }
            };
            var receiptJson = PublishingPagePackageSerializer.SerializeCanonical(receipt);

            StringAssert.Contains(report, exclusion.TargetServerRelativeUrl);
            StringAssert.Contains(report, exclusion.PolicyId);
            StringAssert.Contains(report, exclusion.ReasonCode);
            StringAssert.Contains(receiptJson, "\"sourceItemId\":7");
            StringAssert.Contains(receiptJson, "\"status\":\"Absent\"");
            StringAssert.Contains(receiptJson, "\"httpStatusCode\":404");
        }

        [TestMethod]
        public void ExclusionForcesOwnedIdentityReadWhenReceiptHasNoTargetItemIds()
        {
            var plan = new ListMaterializationPlan
            {
                ApprovedProtectedDocumentExclusions = new List<ListProtectedDocumentExclusionPlan>
                {
                    new ListProtectedDocumentExclusionPlan { SourceItemId = 7 }
                }
            };
            var receipt = new ListMaterializationReceipt();

            Assert.AreEqual(0, receipt.TargetItemIds.Count);
            Assert.IsTrue(ListItemVerifier.RequiresOwnedItemRead(receipt, plan));
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

        [TestMethod]
        public void ExpectedAbsentProbeFindsNestedWebExceptionHttpStatus()
        {
            var nested = new ClientRequestException(
                "outer",
                new IOException(
                    "middle",
                    new WebException(
                        "forbidden",
                        null,
                        WebExceptionStatus.ProtocolError,
                        new TestHttpStatusResponse(HttpStatusCode.Forbidden))));

            Assert.AreEqual(403, ProtectedDocumentTargetAbsenceProbe.ExtractHttpStatusCode(nested));
            Assert.AreEqual(
                ProtectedDocumentTargetAbsenceStatus.AuthorizationBlocked,
                ProtectedDocumentTargetAbsenceProbe.ClassifyException(nested));
        }

        private static void ValidateSource(
            ListDependencySnapshot source,
            ProtectedAssetCapturePolicy policy,
            IMigrationArtifactStore artifactStore)
        {
            ListDependencyPackageValidator.Validate(
                Array.Empty<PnP.Framework.Migration.Pages.ClassicWebParts.ClassicWebPartSnapshot>(),
                Array.Empty<PnP.Framework.Migration.Pages.ClassicWebParts.Bindings.ClassicListWebPartBindingSnapshot>(),
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                null,
                artifactStore,
                policy);
        }

        private static ListBinaryArtifactSnapshot CapturedBinary()
        {
            return new ListBinaryArtifactSnapshot
            {
                Artifact = new ArtifactReference
                {
                    Sha256 = new string('a', 64),
                    Length = 1,
                    MediaType = "application/octet-stream",
                    Availability = EvidenceAvailability.Captured
                },
                Availability = EvidenceAvailability.Captured,
                RepresentationKind = ListBinaryRepresentationKind.OrdinaryFilePayload
            };
        }

        private static LookupScenarioState CreateLookupScenario(DroppedLookupValuePolicy policy)
        {
            var provider = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.lookup.provider"));
            var fieldId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var consumer = new ListDependencySnapshot
            {
                SourceSiteId = provider.SourceSiteId,
                SourceWebId = provider.SourceWebId,
                SourceWebUrl = provider.SourceWebUrl,
                SourceListId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Title = "Consumers",
                Description = string.Empty,
                BaseTemplate = 100,
                BaseType = "GenericList",
                RootFolderServerRelativeUrl = "/sites/source/Lists/Consumers",
                InformationRightsManagement = new ListInformationRightsManagementSnapshot
                {
                    IrmEnabled = false,
                    Availability = EvidenceAvailability.Captured
                },
                SourceItemCount = 2,
                Fields = new List<ListFieldSnapshot>
                {
                    LookupField(fieldId, provider)
                },
                Items = new List<ListItemSnapshot>
                {
                    new ListItemSnapshot { SourceItemId = 10 },
                    new ListItemSnapshot
                    {
                        SourceItemId = 11,
                        Values = new List<ListItemValueSnapshot>
                        {
                            new ListItemValueSnapshot
                            {
                                InternalName = "ProtectedDocument",
                                Kind = ListItemValueKind.Lookup,
                                LookupValues = new List<ListItemLookupValueSnapshot>
                                {
                                    new ListItemLookupValueSnapshot
                                    {
                                        LookupId = 7,
                                        LookupValue = "protected.docx"
                                    }
                                }
                            }
                        }
                    }
                },
                Availability = EvidenceAvailability.Captured
            };
            var lookupDependencies = new List<ListLookupDependency>
            {
                new ListLookupDependency
                {
                    SourceListId = consumer.SourceListId,
                    LookupListId = provider.SourceListId,
                    FieldId = fieldId,
                    FieldInternalName = "ProtectedDocument"
                }
            };
            var planSet = ListMigrationPlanFactory.Create(
                new[] { provider, consumer },
                lookupDependencies,
                Topology(provider),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>(),
                policy);
            var snapshot = new PublishingPageCaptureBundle
            {
                ListDependencies = new List<ListDependencySnapshot> { provider, consumer },
                ListLookupDependencies = lookupDependencies
            };
            var graph = new CanonicalPageIngredientGraph
            {
                ProjectionVersion = PublishingPageIngredientGraphProjector.CurrentProjectionVersion
            };
            PublishingPageListIngredientGraphProjector.Project(
                snapshot,
                graph,
                PublishingPageIngredientGraphProjectionRevision.CurrentV7);
            var actions = new Dictionary<string, PageIngredientAction>(StringComparer.Ordinal);
            PublishingPageListIngredientActionProjector.Project(
                snapshot,
                new PublishingPageMigrationPlan { ListMigration = planSet },
                actions,
                true);
            return new LookupScenarioState
            {
                Provider = provider,
                Consumer = consumer,
                PlanSet = planSet,
                Graph = graph,
                Actions = actions
            };
        }

        private static ListFieldSnapshot LookupField(Guid fieldId, ListDependencySnapshot provider)
        {
            var schema = "<Field ID=\"{" + fieldId.ToString("D")
                + "}\" Name=\"ProtectedDocument\" DisplayName=\"Protected document\" Type=\"Lookup\" List=\"{"
                + provider.SourceListId.ToString("D") + "}\" ShowField=\"Title\" />";
            return new ListFieldSnapshot
            {
                Id = fieldId,
                InternalName = "ProtectedDocument",
                Title = "Protected document",
                TypeAsString = "Lookup",
                SchemaXml = schema,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(schema),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(schema),
                SourceLookupWebId = provider.SourceWebId,
                SourceLookupListId = provider.SourceListId,
                LookupField = "Title",
                Availability = EvidenceAvailability.Captured
            };
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
            var decision = ProtectedAssetCaptureGate.Decide(protection, false, policy);
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
                InformationRightsManagement = new ListInformationRightsManagementSnapshot
                {
                    IrmEnabled = false,
                    Availability = EvidenceAvailability.Captured
                },
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

        private sealed class LookupScenarioState
        {
            public ListDependencySnapshot Provider { get; set; }

            public ListDependencySnapshot Consumer { get; set; }

            public ListMigrationPlanSet PlanSet { get; set; }

            public CanonicalPageIngredientGraph Graph { get; set; }

            public IDictionary<string, PageIngredientAction> Actions { get; set; }
        }

        private sealed class SpyArtifactStore : IMigrationArtifactStore
        {
            public int OpenReadCalls { get; private set; }

            public bool Contains(string sha256)
            {
                return true;
            }

            public Stream OpenRead(string sha256)
            {
                OpenReadCalls++;
                throw new InvalidOperationException("The metadata-only validator must not open an artifact.");
            }

            public ArtifactReference Put(Stream content, string mediaType = null, string originalName = null)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class TestHttpStatusResponse : WebResponse
        {
            public TestHttpStatusResponse(HttpStatusCode statusCode)
            {
                StatusCode = statusCode;
            }

            public HttpStatusCode StatusCode { get; }
        }
    }
}
