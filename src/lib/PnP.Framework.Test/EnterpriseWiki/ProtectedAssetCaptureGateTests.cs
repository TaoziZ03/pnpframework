using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.ContentTypes;
using PnP.Framework.Migration.Lists.Execution;
using PnP.Framework.Migration.Lists.Fields;
using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Lists.Packaging;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.Publishing.Reporting;
using PnP.Framework.Migration.Pages.Publishing.Reporting.Sections;
using PnP.Framework.Migration.Pages.Publishing.Verification;
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
            Assert.IsTrue(decision.SourceListIrmStateObserved);
            Assert.AreEqual(ProtectedAssetProtectionState.Protected, decision.ProtectionState);
            Assert.IsTrue(decision.IsMetadataOnly);
        }

        [TestMethod]
        public void ReaderPathFetchesOnlyWhenNegativeProtectionEvidenceIsComplete()
        {
            var calls = 0;
            var expected = CapturedBinary();
            var content = ListItemSnapshotReader.CaptureDocumentContent(
                CompleteNegativeProtectionFields(),
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
            Assert.IsTrue(protection.DecryptSkipReasonObserved);
            Assert.IsTrue(protection.HasEncryptedContentFieldObserved);
            Assert.IsTrue(protection.RmsTemplateIdFieldObserved);
            Assert.AreEqual(ProtectedAssetProtectionState.Unprotected, decision.ProtectionState);
            Assert.IsTrue(decision.SourceListIrmStateObserved);
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

        [DataTestMethod]
        [DataRow("decrypt")]
        [DataRow("encrypted")]
        [DataRow("rms")]
        public void ReaderPathDoesNotFetchWhenExtendedProtectionEvidenceIsPositive(string evidence)
        {
            var fields = CompleteNegativeProtectionFields();
            if (evidence == "decrypt")
            {
                fields["MetaInfo"] = "vti_decryptskipreason:SW|1";
            }
            else if (evidence == "encrypted")
            {
                fields["_HasEncryptedContent"] = "1";
            }
            else
            {
                fields["_RmsTemplateId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            }
            var calls = 0;

            var content = ListItemSnapshotReader.CaptureDocumentContent(
                fields,
                false,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.extended-positive." + evidence),
                () =>
                {
                    calls++;
                    return CapturedBinary();
                },
                out _,
                out var decision);

            Assert.IsNull(content);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(ProtectedAssetProtectionState.Protected, decision.ProtectionState);
        }

        [DataTestMethod]
        [DataRow("_IpLabelId")]
        [DataRow("_HasUserDefinedProtection")]
        [DataRow("_HasEncryptedContent")]
        [DataRow("_RmsTemplateId")]
        [DataRow("MetaInfo")]
        public void ReaderPathDoesNotFetchWhenAnyNegativeProtectionEvidenceIsMissing(string missingField)
        {
            var fields = CompleteNegativeProtectionFields();
            fields.Remove(missingField);
            var calls = 0;

            var content = ListItemSnapshotReader.CaptureDocumentContent(
                fields,
                false,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.extended-missing." + missingField),
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
        }

        [TestMethod]
        public void ExplicitPolicyDoesNotFetchWhenListIrmStateWasNotObserved()
        {
            var protection = ListDocumentInformationProtectionSnapshotReader.Read(
                CompleteNegativeProtectionFields(),
                true);
            var calls = 0;

            var content = ProtectedAssetCaptureGate.Capture(
                protection,
                false,
                false,
                ProtectedAssetCapturePolicy.MetadataOnly("policy.test.missing-list-irm"),
                () =>
                {
                    calls++;
                    return CapturedBinary();
                },
                out var decision);

            Assert.IsNull(content);
            Assert.AreEqual(0, calls);
            Assert.IsFalse(decision.SourceListIrmStateObserved);
            Assert.AreEqual(ProtectedAssetProtectionState.Unknown, decision.ProtectionState);
            Assert.IsTrue(decision.IsMetadataOnly);
        }

        [TestMethod]
        public void NullPolicyPreservesLegacyReaderSnapshotAndGraphDigest()
        {
            var fields = CompleteNegativeProtectionFields();
            fields["_HasUserDefinedProtection"] = "1";
            fields["_HasEncryptedContent"] = "1";
            fields["_RmsTemplateId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            fields["MetaInfo"] = "vti_decryptskipreason:SW|1";
            var calls = 0;

            var content = ListItemSnapshotReader.CaptureDocumentContent(
                fields,
                false,
                null,
                () =>
                {
                    calls++;
                    return CapturedBinary();
                },
                out var protection,
                out var decision);
            var source = LegacyDocumentList(content);
            var snapshot = new PublishingPageCaptureBundle
            {
                ListDependencies = new List<ListDependencySnapshot> { source }
            };
            var graph = new CanonicalPageIngredientGraph
            {
                ProjectionVersion = PublishingPageIngredientGraphProjector.CurrentProjectionVersion
            };
            PublishingPageListIngredientGraphProjector.Project(
                snapshot,
                graph,
                PublishingPageIngredientGraphProjectionRevision.CurrentV7);
            var graphDigest = MigrationDigest.ComputeSha256(
                PublishingPagePackageSerializer.SerializeCanonical(graph));

            Assert.AreEqual(1, calls);
            Assert.AreSame(content, source.Items.Single().Document.Content);
            Assert.IsNull(protection);
            Assert.IsNull(decision);
            Assert.IsFalse(PublishingPagePackageSerializer.SerializeCanonical(source.Items.Single().Document)
                .Contains("informationProtection", StringComparison.Ordinal));
            Assert.IsFalse(graph.Nodes.Any(value => value.Kind == PageIngredientKind.Policy));
            Assert.AreEqual("af3cb4d7ba414782ae0e1d8f567d0df4941fa6dda5bd2975dc5b20f68ef8fc41", graphDigest);
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
                DroppedItemDependencyDisposition.NeedsPolicyDecision,
                consumerPlan.DroppedItemDependencies.Single().Disposition);
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
            var scenario = CreateLookupScenario(DroppedItemDependencyDisposition.ClearValue);
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

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RequiredSingleAndMultiLookupRejectClearValue(bool multi)
        {
            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                CreateLookupScenario(
                    DroppedItemDependencyDisposition.ClearValue,
                    required: true,
                    multi: multi));

            StringAssert.Contains(exception.Message, "Required lookup field");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RequiredSingleAndMultiLookupAllowDropDependentItem(bool multi)
        {
            var scenario = CreateLookupScenario(
                DroppedItemDependencyDisposition.DropDependentItem,
                required: true,
                multi: multi);
            var dependency = scenario.PlanSet.Lists
                .Single(value => value.SourceListId == scenario.Consumer.SourceListId)
                .DroppedItemDependencies.Single();

            Assert.IsTrue(dependency.ConsumerEffectiveRequired);
            Assert.AreEqual(DroppedItemDependencyDisposition.DropDependentItem, dependency.Disposition);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ContentTypeRequiredSingleAndMultiLookupRejectClearValue(bool multi)
        {
            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                CreateLookupScenario(
                    DroppedItemDependencyDisposition.ClearValue,
                    multi: multi,
                    contentTypeRequired: true));

            StringAssert.Contains(exception.Message, "Required lookup field");
        }

        [TestMethod]
        public void ClearValueRequiresCapturedContentTypeRequirementEvidence()
        {
            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                CreateLookupScenario(
                    DroppedItemDependencyDisposition.ClearValue,
                    includeContentType: false));

            StringAssert.Contains(exception.Message, "ContentType requirement could not be determined");
        }

        [TestMethod]
        public void PlanValidatorRecomputesRequiredLookupAndRejectsTamperedClearDecision()
        {
            var scenario = CreateLookupScenario(
                DroppedItemDependencyDisposition.DropDependentItem,
                required: true);
            var consumerPlan = scenario.PlanSet.Lists
                .Single(value => value.SourceListId == scenario.Consumer.SourceListId);
            consumerPlan.DroppedItemDependencies.Single().Disposition = DroppedItemDependencyDisposition.ClearValue;
            consumerPlan.DroppedItemDependencies.Single().PolicyId = "policy.test.required.tampered-clear";
            foreach (var plan in scenario.PlanSet.Lists)
            {
                plan.Disposition = ListMaterializationDisposition.Block;
                plan.PlanDigest = ListMigrationPlanFactory.ComputePlanDigest(plan);
            }
            scenario.PlanSet.PlanDigest = ListMigrationPlanFactory.ComputeSetDigest(scenario.PlanSet);
            var clearDecision = DroppedLookupValueDecision.Create(
                scenario.Consumer.SourceListId,
                11,
                "ProtectedDocument",
                scenario.Provider.SourceListId,
                7,
                DroppedItemDependencyDisposition.ClearValue,
                "policy.test.required.tampered-clear");

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                ListMigrationPlanValidator.Validate(
                    new[] { scenario.Provider, scenario.Consumer },
                    scenario.LookupDependencies,
                    scenario.PlanSet,
                    new[] { clearDecision }));

            StringAssert.Contains(exception.Message, "Required lookup field");
        }

        [TestMethod]
        public void PlanValidatorRecomputesContentTypeFieldLinkRequiredAndRejectsTamperedClearDecision()
        {
            var scenario = CreateLookupScenario(
                DroppedItemDependencyDisposition.DropDependentItem,
                contentTypeRequired: true);
            var consumerPlan = scenario.PlanSet.Lists
                .Single(value => value.SourceListId == scenario.Consumer.SourceListId);
            var dependency = consumerPlan.DroppedItemDependencies.Single();
            Assert.IsFalse(dependency.ConsumerListFieldRequired);
            Assert.IsTrue(dependency.ConsumerContentTypeResolved);
            Assert.IsTrue(dependency.ConsumerContentTypeFieldLinkRequired);
            Assert.IsTrue(dependency.ConsumerEffectiveRequired);
            dependency.Disposition = DroppedItemDependencyDisposition.ClearValue;
            dependency.PolicyId = "policy.test.ct-required.tampered-clear";
            foreach (var plan in scenario.PlanSet.Lists)
            {
                plan.Disposition = ListMaterializationDisposition.Block;
                plan.PlanDigest = ListMigrationPlanFactory.ComputePlanDigest(plan);
            }
            scenario.PlanSet.PlanDigest = ListMigrationPlanFactory.ComputeSetDigest(scenario.PlanSet);
            var clearDecision = DroppedLookupValueDecision.Create(
                scenario.Consumer.SourceListId,
                11,
                "ProtectedDocument",
                scenario.Provider.SourceListId,
                7,
                DroppedItemDependencyDisposition.ClearValue,
                "policy.test.ct-required.tampered-clear");

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                ListMigrationPlanValidator.Validate(
                    new[] { scenario.Provider, scenario.Consumer },
                    scenario.LookupDependencies,
                    scenario.PlanSet,
                    new[] { clearDecision }));

            StringAssert.Contains(exception.Message, "Required lookup field");
        }

        [TestMethod]
        public void DropDependentLookupPolicyExcludesDependentButNotIndependentSibling()
        {
            var scenario = CreateLookupScenario(DroppedItemDependencyDisposition.DropDependentItem);
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
        public void DroppedLookupClosurePropagatesAcrossAtoBtoC()
        {
            var scenario = CreateLookupChainScenario(
                DroppedItemDependencyDisposition.DropDependentItem,
                DroppedItemDependencyDisposition.DropDependentItem);
            var middle = scenario.Consumer;
            var tail = scenario.Lists.Single(value => value.SourceListId != scenario.Provider.SourceListId
                && value.SourceListId != middle.SourceListId);
            var middleItemId = PublishingPageIngredientIds.ListItem(middle.SourceWebId, middle.SourceListId, 11);
            var tailItemId = PublishingPageIngredientIds.ListItem(tail.SourceWebId, tail.SourceListId, 21);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(scenario.Graph, scenario.Actions.Values);

            Assert.AreEqual(
                DroppedItemDependencyDisposition.DropDependentItem,
                scenario.PlanSet.Lists.Single(value => value.SourceListId == middle.SourceListId)
                    .DroppedItemDependencies.Single().Disposition);
            Assert.AreEqual(
                DroppedItemDependencyDisposition.DropDependentItem,
                scenario.PlanSet.Lists.Single(value => value.SourceListId == tail.SourceListId)
                    .DroppedItemDependencies.Single().Disposition);
            Assert.AreEqual(IngredientDisposition.Drop, scenario.Actions[middleItemId].Disposition);
            Assert.AreEqual(IngredientDisposition.Drop, scenario.Actions[tailItemId].Disposition);
            Assert.AreEqual(PageIngredientExecutionState.ExcludedByApprovedDisposition, evaluation.ExecutionFrontier.GetState(tailItemId));
        }

        [TestMethod]
        public void ClearValueStopsDroppedLookupClosurePropagation()
        {
            var scenario = CreateLookupChainScenario(
                DroppedItemDependencyDisposition.ClearValue,
                DroppedItemDependencyDisposition.DropDependentItem);
            var middle = scenario.Consumer;
            var tail = scenario.Lists.Single(value => value.SourceListId != scenario.Provider.SourceListId
                && value.SourceListId != middle.SourceListId);
            var middleItemId = PublishingPageIngredientIds.ListItem(middle.SourceWebId, middle.SourceListId, 11);
            var tailItemId = PublishingPageIngredientIds.ListItem(tail.SourceWebId, tail.SourceListId, 21);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(scenario.Graph, scenario.Actions.Values);

            Assert.AreEqual(IngredientDisposition.Transform, scenario.Actions[middleItemId].Disposition);
            Assert.IsNull(scenario.PlanSet.Lists.Single(value => value.SourceListId == tail.SourceListId).DroppedItemDependencies);
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState(tailItemId));
        }

        [TestMethod]
        public void NeedsDecisionLeavesBranchPendingAndDescendantSkipped()
        {
            var scenario = CreateLookupChainScenario(
                null,
                DroppedItemDependencyDisposition.DropDependentItem);
            var middle = scenario.Consumer;
            var tail = scenario.Lists.Single(value => value.SourceListId != scenario.Provider.SourceListId
                && value.SourceListId != middle.SourceListId);
            var middleItemId = PublishingPageIngredientIds.ListItem(middle.SourceWebId, middle.SourceListId, 11);
            var tailItemId = PublishingPageIngredientIds.ListItem(tail.SourceWebId, tail.SourceListId, 21);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(scenario.Graph, scenario.Actions.Values);

            Assert.AreEqual(IngredientDisposition.Defer, scenario.Actions[middleItemId].Disposition);
            Assert.IsNull(scenario.PlanSet.Lists.Single(value => value.SourceListId == tail.SourceListId).DroppedItemDependencies);
            Assert.AreEqual(PageIngredientExecutionState.SkippedByDeferredDependency, evaluation.ExecutionFrontier.GetState(tailItemId));
        }

        [TestMethod]
        public void PerEdgeMixedDecisionsAreSealedIndependently()
        {
            var scenario = CreateMixedLookupDecisionScenario();
            var plan = scenario.PlanSet.Lists.Single(value => value.SourceListId == scenario.Consumer.SourceListId);
            var clear = plan.DroppedItemDependencies.Single(value => value.ConsumerSourceItemId == 11);
            var drop = plan.DroppedItemDependencies.Single(value => value.ConsumerSourceItemId == 12);
            var clearItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                11);
            var dropItemId = PublishingPageIngredientIds.ListItem(
                scenario.Consumer.SourceWebId,
                scenario.Consumer.SourceListId,
                12);

            Assert.AreEqual(DroppedItemDependencyDisposition.ClearValue, clear.Disposition);
            Assert.AreEqual("policy.test.mixed.clear", clear.PolicyId);
            Assert.AreEqual(DroppedItemDependencyDisposition.DropDependentItem, drop.Disposition);
            Assert.AreEqual("policy.test.mixed.drop", drop.PolicyId);
            Assert.AreEqual(IngredientDisposition.Transform, scenario.Actions[clearItemId].Disposition);
            Assert.AreEqual(IngredientDisposition.Drop, scenario.Actions[dropItemId].Disposition);
        }

        [TestMethod]
        public void SealedPlanningPolicyCanonicalizesDecisionOrderAndResumeDigest()
        {
            var decisions = CanonicalDecisionPair();
            var first = PublishingPagePlanningPolicy.CopyOptions(
                new PagePlanningOptions
                {
                    TargetPageServerRelativeUrl = "/sites/target/pages/a.aspx",
                    DroppedLookupValueDecisions = decisions.Reverse().ToList()
                },
                "/sites/target/pages/a.aspx");
            var second = PublishingPagePlanningPolicy.CopyOptions(
                new PagePlanningOptions
                {
                    TargetPageServerRelativeUrl = "/sites/target/pages/a.aspx",
                    DroppedLookupValueDecisions = decisions.ToList()
                },
                "/sites/target/pages/a.aspx");
            var expectedKeys = decisions
                .Select(DroppedLookupValueDecision.ExactEdgeKey)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                expectedKeys,
                first.DroppedLookupValueDecisions
                    .Select(DroppedLookupValueDecision.ExactEdgeKey)
                    .ToArray());
            Assert.AreEqual(
                PublishingPagePackageSerializer.SerializeCanonical(first),
                PublishingPagePackageSerializer.SerializeCanonical(second));

            var firstPlan = new PublishingPageMigrationPlan { PlanningPolicy = first };
            var secondPlan = new PublishingPageMigrationPlan { PlanningPolicy = second };
            var firstDigest = PublishingPageDigest.ComputePlanDigest(firstPlan);
            Assert.AreEqual(firstDigest, PublishingPageDigest.ComputePlanDigest(secondPlan));
            var resumed = PublishingPagePackageSerializer.Deserialize<PublishingPageMigrationPlan>(
                PublishingPagePackageSerializer.Serialize(firstPlan));
            Assert.AreEqual(firstDigest, PublishingPageDigest.ComputePlanDigest(resumed));
            CollectionAssert.AreEqual(
                expectedKeys,
                resumed.PlanningPolicy.DroppedLookupValueDecisions
                    .Select(DroppedLookupValueDecision.ExactEdgeKey)
                    .ToArray());
        }

        [TestMethod]
        public void SealedPlanningPolicyNormalizesNullAndEmptyDecisionSetsToNull()
        {
            var fromNull = PublishingPagePlanningPolicy.CopyOptions(
                new PagePlanningOptions { TargetPageServerRelativeUrl = "/sites/target/pages/a.aspx" },
                "/sites/target/pages/a.aspx");
            var fromEmpty = PublishingPagePlanningPolicy.CopyOptions(
                new PagePlanningOptions
                {
                    TargetPageServerRelativeUrl = "/sites/target/pages/a.aspx",
                    DroppedLookupValueDecisions = new List<DroppedLookupValueDecision>()
                },
                "/sites/target/pages/a.aspx");

            Assert.IsNull(fromNull.DroppedLookupValueDecisions);
            Assert.IsNull(fromEmpty.DroppedLookupValueDecisions);
            Assert.AreEqual(
                PublishingPagePackageSerializer.SerializeCanonical(fromNull),
                PublishingPagePackageSerializer.SerializeCanonical(fromEmpty));
        }

        [TestMethod]
        public void PlanValidatorRejectsNonCanonicalDecisionOrder()
        {
            var reversed = DroppedLookupValueDecision.Canonicalize(CanonicalDecisionPair())
                .Reverse()
                .ToList();

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                ListMigrationPlanValidator.Validate(
                    Array.Empty<ListDependencySnapshot>(),
                    Array.Empty<ListLookupDependency>(),
                    null,
                    reversed));

            StringAssert.Contains(exception.Message, "canonical exact-edge order");
        }

        [TestMethod]
        public void PlanValidatorRejectsAnEmptyNonNullDecisionSet()
        {
            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                ListMigrationPlanValidator.Validate(
                    Array.Empty<ListDependencySnapshot>(),
                    Array.Empty<ListLookupDependency>(),
                    null,
                    new List<DroppedLookupValueDecision>()));

            StringAssert.Contains(exception.Message, "empty dropped lookup-value decision set as null");
        }

        [TestMethod]
        public void ListReportIncludesEverySuppliedExactEdgeDecision()
        {
            var scenario = CreateMixedLookupDecisionScenario();
            var writer = new MarkdownReportWriter();
            ListDependencyMigrationReportSection.Append(
                writer,
                new PublishingPageCaptureBundle
                {
                    ListDependencies = scenario.Lists,
                    ListLookupDependencies = scenario.LookupDependencies
                },
                new PublishingPageMigrationPlan
                {
                    PlanningPolicy = new PagePlanningOptions
                    {
                        DroppedLookupValueDecisions = DroppedLookupValueDecision.Canonicalize(
                            scenario.Decisions)
                    },
                    ListMigration = scenario.PlanSet
                });
            var report = writer.ToString();

            StringAssert.Contains(report, "Supplied dropped lookup-value decisions");
            foreach (var decision in scenario.Decisions)
            {
                StringAssert.Contains(report, decision.PolicyId);
                StringAssert.Contains(report, decision.ConsumerSourceItemId.ToString());
            }
        }

        [TestMethod]
        public void ClearValueRemovesOnlyTheExactDroppedLookupFromAMultiValueField()
        {
            var providerListId = Guid.Parse("33333333-4444-5555-6666-777777777777");
            var sourceValue = new ListItemValueSnapshot
            {
                InternalName = "RelatedDocuments",
                Kind = ListItemValueKind.LookupCollection,
                LookupValues = new List<ListItemLookupValueSnapshot>
                {
                    new ListItemLookupValueSnapshot { LookupId = 7 },
                    new ListItemLookupValueSnapshot { LookupId = 8 }
                }
            };
            var fieldPlan = new ListFieldMaterializationPlan
            {
                InternalName = sourceValue.InternalName,
                SourceLookupListId = providerListId,
                Disposition = ListFieldMaterializationDisposition.MapLookup
            };
            var dependencyPlans = new[]
            {
                new ListDroppedItemDependencyPlan
                {
                    Kind = ListItemDependencyKind.LookupValue,
                    ConsumerSourceItemId = 11,
                    ConsumerFieldInternalName = sourceValue.InternalName,
                    ProviderSourceListId = providerListId,
                    ProviderSourceItemId = 7,
                    Disposition = DroppedItemDependencyDisposition.ClearValue,
                    PolicyId = "policy.test.multi.exact-clear"
                }
            };
            var clearedIds = DroppedItemDependencyPlanner.ClearedLookupProviderItemIds(
                dependencyPlans,
                11,
                sourceValue.InternalName,
                providerListId);
            var receipts = new Dictionary<Guid, ListMaterializationReceipt>
            {
                [providerListId] = new ListMaterializationReceipt
                {
                    TargetItemIds = new Dictionary<int, int> { [8] = 108 }
                }
            };

            var projected = (FieldLookupValue[])ListItemValueWriter.ProjectLookupValue(
                sourceValue,
                fieldPlan,
                receipts,
                clearedIds);
            var actual = new ListItemValueSnapshot
            {
                InternalName = sourceValue.InternalName,
                Kind = ListItemValueKind.LookupCollection,
                LookupValues = projected
                    .Select(value => new ListItemLookupValueSnapshot { LookupId = value.LookupId })
                    .ToList()
            };

            Assert.AreEqual(1, projected.Length);
            Assert.AreEqual(108, projected[0].LookupId);
            Assert.IsTrue(ListItemValueComparer.Matches(
                sourceValue,
                actual,
                fieldPlan,
                receipts,
                clearedIds,
                out var mismatch), mismatch);
        }

        [TestMethod]
        public void DroppedFolderTransitivelyDropsNestedFoldersAndFiles()
        {
            var scenario = CreateFolderClosureScenario();
            var tree = scenario.Consumer;
            var plan = scenario.PlanSet.Lists.Single(value => value.SourceListId == tree.SourceListId);
            var droppedIds = DroppedItemDependencyPlanner.DroppedConsumerItemIds(plan.DroppedItemDependencies);

            CollectionAssert.AreEquivalent(new[] { 20, 21, 22 }, droppedIds.ToArray());
            Assert.AreEqual(3, plan.DroppedItemDependencies.Count);
            Assert.AreEqual(2, plan.DroppedItemDependencies.Count(value => value.Kind == ListItemDependencyKind.FolderPath));
            foreach (var sourceItemId in droppedIds)
            {
                var ingredientId = PublishingPageIngredientIds.ListItem(
                    tree.SourceWebId,
                    tree.SourceListId,
                    sourceItemId);
                Assert.AreEqual(IngredientDisposition.Drop, scenario.Actions[ingredientId].Disposition);
            }
        }

        [TestMethod]
        public void NoSeedFastPathDoesNotEnumerateItemsOrBuildAnItemGraph()
        {
            var sourceListId = Guid.Parse("12345678-1234-1234-1234-123456789012");
            var source = new ListDependencySnapshot
            {
                SourceListId = sourceListId,
                Items = new ThrowOnEnumerationList<ListItemSnapshot>()
            };
            var plan = new ListMaterializationPlan { SourceListId = sourceListId };

            var projection = DroppedItemDependencyPlanner.Project(
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                new[] { plan },
                null);

            Assert.AreEqual(0, projection.SourceEdges.Count);
            Assert.AreEqual(0, projection.DroppedItemKeys.Count);
            Assert.AreEqual(0, projection.PlansByConsumerList.Count);
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

        [TestMethod]
        public void DroppedDependentReceiptCountsAbsentAndPresentIdentities()
        {
            var listReceipts = new[]
            {
                new ListMaterializationReceipt
                {
                    DroppedDependentItemVerifications = new List<ListDroppedDependentItemVerification>
                    {
                        new ListDroppedDependentItemVerification
                        {
                            SourceItemId = 11,
                            Status = DroppedDependentTargetIdentityStatus.Absent
                        },
                        new ListDroppedDependentItemVerification
                        {
                            SourceItemId = 12,
                            Status = DroppedDependentTargetIdentityStatus.Present
                        }
                    }
                },
                new ListMaterializationReceipt
                {
                    DroppedDependentItemVerifications = new List<ListDroppedDependentItemVerification>
                    {
                        new ListDroppedDependentItemVerification
                        {
                            SourceItemId = 21,
                            Status = DroppedDependentTargetIdentityStatus.Absent
                        }
                    }
                }
            };
            var receipt = new PublishingPageImportReceipt
            {
                DroppedDependentItemAbsentCount = PublishingPageImportVerifier.DroppedDependentItemCount(
                    listReceipts,
                    DroppedDependentTargetIdentityStatus.Absent),
                DroppedDependentItemPresentCount = PublishingPageImportVerifier.DroppedDependentItemCount(
                    listReceipts,
                    DroppedDependentTargetIdentityStatus.Present)
            };
            var json = PublishingPagePackageSerializer.SerializeCanonical(receipt);

            Assert.AreEqual(2, receipt.DroppedDependentItemAbsentCount);
            Assert.AreEqual(1, receipt.DroppedDependentItemPresentCount);
            StringAssert.Contains(json, "\"droppedDependentItemAbsentCount\":2");
            StringAssert.Contains(json, "\"droppedDependentItemPresentCount\":1");
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

        private static LookupScenarioState CreateLookupScenario(
            DroppedItemDependencyDisposition? disposition,
            bool required = false,
            bool multi = false,
            bool contentTypeRequired = false,
            bool includeContentType = true)
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
                InformationRightsManagement = DisabledIrm(),
                SourceItemCount = 2,
                Fields = new List<ListFieldSnapshot>
                {
                    LookupField(fieldId, provider, required, multi)
                },
                ContentTypes = includeContentType
                    ? new List<ListContentTypeSnapshot>
                    {
                        LookupContentType(fieldId, "ProtectedDocument", contentTypeRequired)
                    }
                    : new List<ListContentTypeSnapshot>(),
                Items = new List<ListItemSnapshot>
                {
                    new ListItemSnapshot { SourceItemId = 10 },
                    new ListItemSnapshot
                    {
                        SourceItemId = 11,
                        Values = new List<ListItemValueSnapshot>
                        {
                            ContentTypeValue(),
                            new ListItemValueSnapshot
                            {
                                InternalName = "ProtectedDocument",
                                Kind = multi ? ListItemValueKind.LookupCollection : ListItemValueKind.Lookup,
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
            return BuildLookupScenario(
                provider,
                consumer,
                new[] { provider, consumer },
                lookupDependencies,
                disposition.HasValue
                    ? new[]
                    {
                        DroppedLookupValueDecision.Create(
                            consumer.SourceListId,
                            11,
                            "ProtectedDocument",
                            provider.SourceListId,
                            7,
                            disposition.Value,
                            "policy.test.lookup." + disposition.Value)
                    }
                    : null);
        }

        private static LookupScenarioState BuildLookupScenario(
            ListDependencySnapshot provider,
            ListDependencySnapshot consumer,
            IList<ListDependencySnapshot> lists,
            IList<ListLookupDependency> lookupDependencies,
            IEnumerable<DroppedLookupValueDecision> decisions)
        {
            var decisionValues = (decisions ?? Array.Empty<DroppedLookupValueDecision>()).ToList();
            var planSet = ListMigrationPlanFactory.Create(
                lists,
                lookupDependencies,
                Topology(provider),
                Array.Empty<PnP.Framework.Migration.Taxonomy.TaxonomyTargetMapping>(),
                Array.Empty<ListTargetOverride>(),
                decisionValues);
            var snapshot = new PublishingPageCaptureBundle
            {
                ListDependencies = lists,
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
                Lists = lists,
                LookupDependencies = lookupDependencies,
                Decisions = decisionValues,
                PlanSet = planSet,
                Graph = graph,
                Actions = actions
            };
        }

        private static LookupScenarioState CreateLookupChainScenario(
            DroppedItemDependencyDisposition? firstDisposition,
            DroppedItemDependencyDisposition? secondDisposition)
        {
            var provider = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.chain.provider"));
            var middle = LookupConsumerList(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                "Middle",
                11,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "ProviderA",
                provider,
                7);
            var tail = LookupConsumerList(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "Tail",
                21,
                Guid.Parse("88888888-8888-8888-8888-888888888888"),
                "ProviderB",
                middle,
                11);
            var lookupDependencies = new List<ListLookupDependency>
            {
                LookupDependency(middle, provider, middle.Fields.Single()),
                LookupDependency(tail, middle, tail.Fields.Single())
            };
            var decisions = new List<DroppedLookupValueDecision>();
            if (firstDisposition.HasValue)
            {
                decisions.Add(DroppedLookupValueDecision.Create(
                    middle.SourceListId,
                    11,
                    "ProviderA",
                    provider.SourceListId,
                    7,
                    firstDisposition.Value,
                    "policy.test.chain.first." + firstDisposition.Value));
            }
            if (secondDisposition.HasValue)
            {
                decisions.Add(DroppedLookupValueDecision.Create(
                    tail.SourceListId,
                    21,
                    "ProviderB",
                    middle.SourceListId,
                    11,
                    secondDisposition.Value,
                    "policy.test.chain.second." + secondDisposition.Value));
            }
            return BuildLookupScenario(
                provider,
                middle,
                new[] { provider, middle, tail },
                lookupDependencies,
                decisions);
        }

        private static LookupScenarioState CreateMixedLookupDecisionScenario()
        {
            var provider = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.mixed.provider"));
            var consumer = LookupConsumerList(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                "Mixed",
                11,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "ProtectedDocument",
                provider,
                7);
            consumer.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 12,
                Values = new List<ListItemValueSnapshot>
                {
                    ContentTypeValue(),
                    LookupValue("ProtectedDocument", 7)
                }
            });
            consumer.SourceItemCount = consumer.Items.Count;
            var lookupDependencies = new[]
            {
                LookupDependency(consumer, provider, consumer.Fields.Single())
            };
            var decisions = new[]
            {
                DroppedLookupValueDecision.Create(
                    consumer.SourceListId,
                    11,
                    "ProtectedDocument",
                    provider.SourceListId,
                    7,
                    DroppedItemDependencyDisposition.ClearValue,
                    "policy.test.mixed.clear"),
                DroppedLookupValueDecision.Create(
                    consumer.SourceListId,
                    12,
                    "ProtectedDocument",
                    provider.SourceListId,
                    7,
                    DroppedItemDependencyDisposition.DropDependentItem,
                    "policy.test.mixed.drop")
            };
            return BuildLookupScenario(
                provider,
                consumer,
                new[] { provider, consumer },
                lookupDependencies,
                decisions);
        }

        private static LookupScenarioState CreateFolderClosureScenario()
        {
            var provider = ProtectedList(ProtectedAssetCapturePolicy.MetadataOnly("policy.test.folder.provider"));
            var fieldId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var tree = new ListDependencySnapshot
            {
                SourceSiteId = provider.SourceSiteId,
                SourceWebId = provider.SourceWebId,
                SourceWebUrl = provider.SourceWebUrl,
                SourceListId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                Title = "Tree",
                Description = string.Empty,
                BaseTemplate = 101,
                BaseType = "DocumentLibrary",
                RootFolderServerRelativeUrl = "/sites/source/Tree",
                InformationRightsManagement = DisabledIrm(),
                Fields = new List<ListFieldSnapshot>
                {
                    LookupField(fieldId, provider, internalName: "ProtectedDocument")
                },
                Items = new List<ListItemSnapshot>
                {
                    new ListItemSnapshot
                    {
                        SourceItemId = 20,
                        Values = new List<ListItemValueSnapshot> { LookupValue("ProtectedDocument", 7) },
                        Document = new ListDocumentSnapshot
                        {
                            Kind = ListDocumentObjectKind.Folder,
                            Name = "F",
                            ServerRelativeUrl = "/sites/source/Tree/F"
                        }
                    },
                    new ListItemSnapshot
                    {
                        SourceItemId = 21,
                        Document = new ListDocumentSnapshot
                        {
                            Kind = ListDocumentObjectKind.Folder,
                            Name = "Sub",
                            ServerRelativeUrl = "/sites/source/Tree/F/Sub"
                        }
                    },
                    new ListItemSnapshot
                    {
                        SourceItemId = 22,
                        Document = new ListDocumentSnapshot
                        {
                            Kind = ListDocumentObjectKind.File,
                            Name = "file.txt",
                            ServerRelativeUrl = "/sites/source/Tree/F/Sub/file.txt",
                            Length = 1,
                            Content = CapturedBinary()
                        }
                    }
                },
                Availability = EvidenceAvailability.Captured
            };
            tree.SourceItemCount = tree.Items.Count;
            var lookupDependencies = new[]
            {
                LookupDependency(tree, provider, tree.Fields.Single())
            };
            var decisions = new[]
            {
                DroppedLookupValueDecision.Create(
                    tree.SourceListId,
                    20,
                    "ProtectedDocument",
                    provider.SourceListId,
                    7,
                    DroppedItemDependencyDisposition.DropDependentItem,
                    "policy.test.folder.drop")
            };
            return BuildLookupScenario(
                provider,
                tree,
                new[] { provider, tree },
                lookupDependencies,
                decisions);
        }

        private static ListDependencySnapshot LookupConsumerList(
            Guid listId,
            string title,
            int itemId,
            Guid fieldId,
            string fieldInternalName,
            ListDependencySnapshot provider,
            int providerItemId)
        {
            return new ListDependencySnapshot
            {
                SourceSiteId = provider.SourceSiteId,
                SourceWebId = provider.SourceWebId,
                SourceWebUrl = provider.SourceWebUrl,
                SourceListId = listId,
                Title = title,
                Description = string.Empty,
                BaseTemplate = 100,
                BaseType = "GenericList",
                RootFolderServerRelativeUrl = "/sites/source/Lists/" + title,
                InformationRightsManagement = DisabledIrm(),
                SourceItemCount = 1,
                Fields = new List<ListFieldSnapshot>
                {
                    LookupField(fieldId, provider, internalName: fieldInternalName)
                },
                ContentTypes = new List<ListContentTypeSnapshot>
                {
                    LookupContentType(fieldId, fieldInternalName, false)
                },
                Items = new List<ListItemSnapshot>
                {
                    new ListItemSnapshot
                    {
                        SourceItemId = itemId,
                        Values = new List<ListItemValueSnapshot>
                        {
                            ContentTypeValue(),
                            LookupValue(fieldInternalName, providerItemId)
                        }
                    }
                },
                Availability = EvidenceAvailability.Captured
            };
        }

        private static ListLookupDependency LookupDependency(
            ListDependencySnapshot consumer,
            ListDependencySnapshot provider,
            ListFieldSnapshot field)
        {
            return new ListLookupDependency
            {
                SourceListId = consumer.SourceListId,
                LookupListId = provider.SourceListId,
                FieldId = field.Id,
                FieldInternalName = field.InternalName
            };
        }

        private static ListItemValueSnapshot LookupValue(string fieldInternalName, int lookupId)
        {
            return new ListItemValueSnapshot
            {
                InternalName = fieldInternalName,
                Kind = ListItemValueKind.Lookup,
                LookupValues = new List<ListItemLookupValueSnapshot>
                {
                    new ListItemLookupValueSnapshot { LookupId = lookupId }
                }
            };
        }

        private static IList<DroppedLookupValueDecision> CanonicalDecisionPair()
        {
            return new List<DroppedLookupValueDecision>
            {
                DroppedLookupValueDecision.Create(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    10,
                    "FirstLookup",
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    7,
                    DroppedItemDependencyDisposition.ClearValue,
                    "policy.test.canonical.first"),
                DroppedLookupValueDecision.Create(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    20,
                    "SecondLookup",
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    8,
                    DroppedItemDependencyDisposition.DropDependentItem,
                    "policy.test.canonical.second")
            };
        }

        private static ListItemValueSnapshot ContentTypeValue()
        {
            return new ListItemValueSnapshot
            {
                InternalName = "ContentTypeId",
                Kind = ListItemValueKind.String,
                ScalarValue = "0x0100AABBCCDDEEFF"
            };
        }

        private static ListContentTypeSnapshot LookupContentType(
            Guid fieldId,
            string fieldInternalName,
            bool required)
        {
            return new ListContentTypeSnapshot
            {
                Id = "0x0100AABBCCDDEEFF",
                Name = "Lookup consumer",
                ParentId = "0x01",
                FieldLinks = new List<ListContentTypeFieldLinkSnapshot>
                {
                    new ListContentTypeFieldLinkSnapshot
                    {
                        FieldId = fieldId,
                        InternalName = fieldInternalName,
                        DisplayName = fieldInternalName,
                        Required = required
                    }
                }
            };
        }

        private static Dictionary<string, object> CompleteNegativeProtectionFields()
        {
            return new Dictionary<string, object>
            {
                ["_IpLabelId"] = string.Empty,
                ["_HasUserDefinedProtection"] = "0",
                ["_HasEncryptedContent"] = "0",
                ["_RmsTemplateId"] = string.Empty,
                ["MetaInfo"] = string.Empty
            };
        }

        private static ListInformationRightsManagementSnapshot DisabledIrm()
        {
            return new ListInformationRightsManagementSnapshot
            {
                IrmEnabled = false,
                Availability = EvidenceAvailability.Captured
            };
        }

        private static ListDependencySnapshot LegacyDocumentList(ListBinaryArtifactSnapshot content)
        {
            return new ListDependencySnapshot
            {
                SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SourceWebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                SourceWebUrl = "https://source.example/sites/source",
                SourceListId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                Title = "LegacyDocs",
                Description = string.Empty,
                BaseTemplate = 101,
                BaseType = "DocumentLibrary",
                RootFolderServerRelativeUrl = "/sites/source/LegacyDocs",
                InformationRightsManagement = DisabledIrm(),
                SourceItemCount = 1,
                Items = new List<ListItemSnapshot>
                {
                    new ListItemSnapshot
                    {
                        SourceItemId = 1,
                        Document = new ListDocumentSnapshot
                        {
                            Kind = ListDocumentObjectKind.File,
                            Name = "legacy.bin",
                            ServerRelativeUrl = "/sites/source/LegacyDocs/legacy.bin",
                            Length = 1,
                            Content = content
                        }
                    }
                },
                Availability = EvidenceAvailability.Captured
            };
        }

        private static ListFieldSnapshot LookupField(
            Guid fieldId,
            ListDependencySnapshot provider,
            bool required = false,
            bool multi = false,
            string internalName = "ProtectedDocument")
        {
            var schema = "<Field ID=\"{" + fieldId.ToString("D")
                + "}\" Name=\"" + internalName + "\" DisplayName=\"Protected document\" Type=\""
                + (multi ? "LookupMulti" : "Lookup") + "\" List=\"{"
                + provider.SourceListId.ToString("D") + "}\" ShowField=\"Title\" Required=\""
                + (required ? "TRUE" : "FALSE") + "\" />";
            return new ListFieldSnapshot
            {
                Id = fieldId,
                InternalName = internalName,
                Title = "Protected document",
                TypeAsString = multi ? "LookupMulti" : "Lookup",
                SchemaXml = schema,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(schema),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(schema),
                SourceLookupWebId = provider.SourceWebId,
                SourceLookupListId = provider.SourceListId,
                LookupField = "Title",
                Required = required,
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
                InformationRightsManagement = DisabledIrm(),
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

            public IList<ListDependencySnapshot> Lists { get; set; }

            public IList<ListLookupDependency> LookupDependencies { get; set; }

            public IList<DroppedLookupValueDecision> Decisions { get; set; }

            public ListMigrationPlanSet PlanSet { get; set; }

            public CanonicalPageIngredientGraph Graph { get; set; }

            public IDictionary<string, PageIngredientAction> Actions { get; set; }
        }

        private sealed class ThrowOnEnumerationList<T> : IList<T>
        {
            public T this[int index]
            {
                get => throw new InvalidOperationException("The no-seed fast path must not read item entries.");
                set => throw new NotSupportedException();
            }

            public int Count => 1;

            public bool IsReadOnly => true;

            public void Add(T item) => throw new NotSupportedException();

            public void Clear() => throw new NotSupportedException();

            public bool Contains(T item) => false;

            public void CopyTo(T[] array, int arrayIndex) => throw new NotSupportedException();

            public IEnumerator<T> GetEnumerator() =>
                throw new InvalidOperationException("The no-seed fast path must not enumerate source items.");

            public int IndexOf(T item) => -1;

            public void Insert(int index, T item) => throw new NotSupportedException();

            public bool Remove(T item) => throw new NotSupportedException();

            public void RemoveAt(int index) => throw new NotSupportedException();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
