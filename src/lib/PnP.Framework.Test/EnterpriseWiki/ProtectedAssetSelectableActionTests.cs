using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Execution;
using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Layouts;
using PnP.Framework.Migration.Pages.Publishing.Layouts.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.Publishing.Reporting;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Topology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class ProtectedAssetSelectableActionTests
    {
        [TestMethod]
        public void MicrosoftProfileDoesNotFetchProtectedBinary()
        {
            var evidence = ListDocumentInformationProtectionSnapshotReader.Read(new Dictionary<string, object>
            {
                ["_IpLabelId"] = "9fbde396-1a24-4c79-8edf-9254a0f35055",
                ["_HasUserDefinedProtection"] = true
            });
            var calls = 0;

            var value = ProtectedAssetCaptureGate.Capture(
                evidence,
                ProtectedAssetCapturePolicy.MicrosoftTenant(),
                () =>
                {
                    calls++;
                    return "payload";
                },
                out var decision);

            Assert.IsNull(value);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(ProtectedAssetBinaryCaptureDisposition.MetadataOnly, decision.Disposition);
            Assert.AreEqual("MicrosoftProtectedAssetExportDenied", decision.ReasonCode);
        }

        [TestMethod]
        public void MicrosoftProfileFailsClosedWhenProtectionIsUnknown()
        {
            var evidence = ListDocumentInformationProtectionSnapshotReader.Read(new Dictionary<string, object>());
            var calls = 0;

            ProtectedAssetCaptureGate.Capture(
                evidence,
                ProtectedAssetCapturePolicy.MicrosoftTenant(),
                () =>
                {
                    calls++;
                    return new object();
                },
                out var decision);

            Assert.AreEqual(ProtectedAssetProtectionState.Unknown, evidence.State);
            Assert.AreEqual(0, calls);
            Assert.AreEqual("InformationProtectionStateUnknownFailClosed", decision.ReasonCode);

            var snapshot = Snapshot(ProtectedAssetCapturePolicy.MicrosoftTenant(), evidence, decision, withPayload: false);
            var plan = PublishingPageProtectedAssetActionPlanner.Create(snapshot, "snapshot-a", null);
            Assert.IsFalse(plan.Actions.Any(value => value.IngredientId.StartsWith("information-protection-relationship:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void FidelityAllowedProfileCanCaptureAndOfferReproduction()
        {
            var evidence = ProtectedEvidence();
            var calls = 0;
            var policy = ProtectedAssetCapturePolicy.FidelityAllowed();

            var value = ProtectedAssetCaptureGate.Capture(
                evidence,
                policy,
                () =>
                {
                    calls++;
                    return "payload";
                },
                out var decision);
            var snapshot = Snapshot(policy, evidence, decision, withPayload: true);
            var plan = PublishingPageProtectedAssetActionPlanner.Create(snapshot, "snapshot-a", null);
            var payload = plan.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));

            Assert.AreEqual("payload", value);
            Assert.AreEqual(1, calls);
            Assert.IsTrue(payload.CandidateActions.Any(candidate => candidate.Action == IngredientSelectableAction.Reproduce));
            Assert.AreEqual(IngredientSelectableAction.Reproduce, payload.SelectedAction.Action);
        }

        [TestMethod]
        public void MicrosoftPolicyFiltersReproduceAndRejectsIllegalSelection()
        {
            var snapshot = MicrosoftSnapshot();
            var initial = PublishingPageProtectedAssetActionPlanner.Create(
                snapshot,
                "snapshot-a",
                null,
                new PageIngredientSelectionAudit
                {
                    SelectedBy = "migration-owner",
                    ApprovalReference = "https://example.test/approvals/191"
                });
            var payload = initial.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));

            Assert.IsFalse(payload.CandidateActions.Any(candidate => candidate.Action == IngredientSelectableAction.Reproduce));
            Assert.AreEqual(IngredientSelectableAction.Exclude, payload.SelectedAction.Action);
            Assert.AreEqual(IngredientTerminalStatus.SatisfiedByPolicy, payload.TerminalStatus);
            Assert.AreEqual("https://example.test/approvals/191", payload.SelectionReceipt.ApprovalReference);

            Assert.ThrowsException<InvalidDataException>(() => PublishingPageProtectedAssetActionPlanner.Create(
                snapshot,
                "snapshot-a",
                new[]
                {
                    new PageIngredientActionSelectionRequest
                    {
                        IngredientId = payload.IngredientId,
                        CandidateActionId = payload.IngredientId + ":reproduce",
                        SnapshotDigest = "snapshot-a",
                        SelectedBy = "reviewer"
                    }
                }));
        }

        [TestMethod]
        public void PayloadOnlyScopeAppliesToTheBinaryChild()
        {
            var policy = ProtectedAssetCapturePolicy.FidelityAllowed();
            var evidence = ProtectedEvidence();
            var snapshot = Snapshot(policy, evidence, ProtectedAssetCaptureGate.Decide(evidence, policy), withPayload: true);
            var initial = PublishingPageProtectedAssetActionPlanner.Create(snapshot, "snapshot-a", null);
            var asset = initial.Actions.Single(value => value.IngredientId.StartsWith("protected-asset:", StringComparison.Ordinal));
            var excludePayload = asset.CandidateActions.Single(value => value.Action == IngredientSelectableAction.Exclude
                && value.Scope == IngredientActionScope.PayloadOnly);

            var selected = PublishingPageProtectedAssetActionPlanner.Create(snapshot, "snapshot-a", new[]
            {
                new PageIngredientActionSelectionRequest
                {
                    IngredientId = asset.IngredientId,
                    CandidateActionId = excludePayload.CandidateActionId,
                    SnapshotDigest = "snapshot-a",
                    SelectedBy = "reviewer"
                }
            });
            var payload = selected.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));
            var identity = selected.Actions.Single(value => value.IngredientId.StartsWith("document-identity:", StringComparison.Ordinal));
            var relationship = selected.Actions.Single(value => value.IngredientId.StartsWith("information-protection-relationship:", StringComparison.Ordinal));

            Assert.AreEqual(IngredientSelectableAction.Exclude, payload.SelectedAction.Action);
            Assert.AreEqual(IngredientComparisonRule.ExpectedAbsent, payload.SelectionReceipt.ComparisonRule);
            Assert.AreEqual(IngredientSelectableAction.EvidenceOnly, identity.SelectedAction.Action);
            Assert.AreEqual(IngredientSelectableAction.EvidenceOnly, relationship.SelectedAction.Action);
        }

        [TestMethod]
        public void SelectionIsBoundToSnapshotAndTamperingIsRejected()
        {
            var snapshot = MicrosoftSnapshot();
            var initial = PublishingPageProtectedAssetActionPlanner.Create(snapshot, "snapshot-a", null);
            var payload = initial.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));

            Assert.ThrowsException<InvalidDataException>(() => PublishingPageProtectedAssetActionPlanner.Create(
                snapshot,
                "snapshot-a",
                new[]
                {
                    new PageIngredientActionSelectionRequest
                    {
                        IngredientId = payload.IngredientId,
                        CandidateActionId = payload.SelectedAction.CandidateActionId,
                        SnapshotDigest = "stale-snapshot",
                        SelectedBy = "reviewer"
                    }
                }));

            payload.SelectionReceipt.ReasonCode = "tampered";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageProtectedAssetActionPlanner.Validate(snapshot, "snapshot-a", initial));
        }

        [TestMethod]
        public void ExcludeSatisfiesOptionalBranchButNotHardDependency()
        {
            var protectedPlan = PublishingPageProtectedAssetActionPlanner.Create(MicrosoftSnapshot(), "snapshot-a", null);
            var payload = protectedPlan.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));
            var consumer = ReproduceAction("consumer");
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    Node("consumer"),
                    Node(payload.IngredientId)
                },
                Edges = new List<PageIngredientEdge>
                {
                    new PageIngredientEdge
                    {
                        FromIngredientId = "consumer",
                        ToIngredientId = payload.IngredientId,
                        Relationship = PageIngredientRelationship.DependsOn,
                        Requirement = PageIngredientRequirement.Optional
                    }
                }
            };

            var optional = PageIngredientPlanEvaluator.Evaluate(graph, new[] { consumer, payload });
            Assert.AreEqual(PageMigrationOutcome.ExecutableWithApprovedExclusions, optional.Outcome);

            graph.Edges[0].Requirement = PageIngredientRequirement.HardRequired;
            var hard = PageIngredientPlanEvaluator.Evaluate(graph, new[] { consumer, payload });
            Assert.AreEqual(PageMigrationOutcome.Invalid, hard.Outcome);
            Assert.IsTrue(hard.Issues.Any(value => value.Code == "RequiredIngredientDependencyUnsatisfied"));
        }

        [TestMethod]
        public void IdentityEvidenceCanSatisfyIdentityButNotPayloadDependency()
        {
            var protectedPlan = PublishingPageProtectedAssetActionPlanner.Create(MicrosoftSnapshot(), "snapshot-a", null);
            var identity = protectedPlan.Actions.Single(value => value.IngredientId.StartsWith("document-identity:", StringComparison.Ordinal));
            var consumer = ReproduceAction("consumer");
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode> { Node("consumer"), Node(identity.IngredientId) },
                Edges = new List<PageIngredientEdge>
                {
                    new PageIngredientEdge
                    {
                        FromIngredientId = "consumer",
                        ToIngredientId = identity.IngredientId,
                        Relationship = PageIngredientRelationship.DependsOn,
                        Requirement = PageIngredientRequirement.IdentityRequired
                    }
                }
            };

            Assert.AreEqual(
                PageMigrationOutcome.ExecutableWithApprovedExclusions,
                PageIngredientPlanEvaluator.Evaluate(graph, new[] { consumer, identity }).Outcome);

            graph.Edges[0].Requirement = PageIngredientRequirement.PayloadRequired;
            Assert.AreEqual(
                PageMigrationOutcome.Invalid,
                PageIngredientPlanEvaluator.Evaluate(graph, new[] { consumer, identity }).Outcome);
        }

        [TestMethod]
        public void ProtectedDocumentProjectsIdentityPayloadAndPolicyRelationship()
        {
            var source = MicrosoftSnapshot().ListDependencies.Single();
            var graph = new CanonicalPageIngredientGraph();

            PublishingPageListContentIngredientGraphProjector.Project(source, "list", graph);

            Assert.IsTrue(graph.Nodes.Any(value => value.Kind == PageIngredientKind.ProtectedAsset));
            Assert.IsTrue(graph.Nodes.Any(value => value.Kind == PageIngredientKind.DocumentIdentity));
            Assert.IsTrue(graph.Nodes.Any(value => value.Kind == PageIngredientKind.BinaryPayload));
            Assert.IsTrue(graph.Nodes.Any(value => value.Kind == PageIngredientKind.InformationProtectionRelationship));
            Assert.IsTrue(graph.Edges.Any(value => value.Requirement == PageIngredientRequirement.IdentityRequired));
            Assert.IsTrue(graph.Edges.Any(value => value.Requirement == PageIngredientRequirement.PayloadRequired));
            Assert.IsTrue(graph.Edges.Any(value => value.Requirement == PageIngredientRequirement.HardRequired));
        }

        [TestMethod]
        public void ExecutorSkipsExcludedPayloadAndReturnsSelectionReceipt()
        {
            var protectedPlan = PublishingPageProtectedAssetActionPlanner.Create(MicrosoftSnapshot(), "snapshot-a", null);
            var payload = protectedPlan.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));
            var mutationCalls = 0;

            var receipt = ProtectedAssetExecutionPolicy.Execute(
                payload.SelectionReceipt,
                () => mutationCalls++);

            Assert.AreEqual(0, mutationCalls);
            Assert.AreEqual(payload.SelectionReceipt.ReceiptDigest, receipt.ReceiptDigest);
            Assert.AreEqual(IngredientComparisonRule.ExpectedAbsent, receipt.ComparisonRule);
        }

        [TestMethod]
        public void MicrosoftProtectedPayloadExclusionUnblocksTheOwningListPlan()
        {
            var snapshot = MicrosoftSnapshot();
            var source = snapshot.ListDependencies.Single();
            source.SourceSiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            source.SourceWebUrl = "https://source.example/sites/source";
            source.RootFolderServerRelativeUrl = "/sites/source/Docs";
            source.Items.Single().Document.ServerRelativeUrl = "/sites/source/Docs/protected.pptx";
            source.Title = "Docs";
            source.Description = string.Empty;
            source.BaseTemplate = 101;
            source.BaseType = "DocumentLibrary";
            source.SourceItemCount = 1;
            var protectedPlan = PublishingPageProtectedAssetActionPlanner.Create(snapshot, "snapshot-a", null);
            var topology = new TopologyPlan
            {
                SiteCollections = new List<SiteCollectionMappingPlan>
                {
                    new SiteCollectionMappingPlan
                    {
                        SourceSiteId = source.SourceSiteId,
                        Webs = new List<WebMappingPlan>
                        {
                            new WebMappingPlan
                            {
                                SourceSiteId = source.SourceSiteId,
                                SourceWebId = source.SourceWebId,
                                SourceWebUrl = source.SourceWebUrl,
                                SourceServerRelativeUrl = "/sites/source",
                                TargetWebUrl = "https://target.example/sites/target",
                                TargetServerRelativeUrl = "/sites/target"
                            }
                        }
                    }
                }
            };

            var plan = ListMigrationPlanFactory.Create(
                snapshot.ListDependencies,
                Array.Empty<ListLookupDependency>(),
                topology,
                null,
                null,
                protectedPlan);
            var list = plan.Lists.Single();

            Assert.IsFalse(list.Issues.Any(value => value.Code == "ListBinaryEvidenceUnavailable"));
            Assert.AreEqual(ListItemMaterializationDisposition.ExcludeProtectedAsset, list.ItemDecisions.Single().Disposition);
            Assert.IsTrue(list.IsExecutable);
        }

        [TestMethod]
        public void CompareClassifiesApprovedAbsenceButRejectsUnexpectedAbsence()
        {
            var protectedPlan = PublishingPageProtectedAssetActionPlanner.Create(MicrosoftSnapshot(), "snapshot-a", null);
            var payload = protectedPlan.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));

            var approved = PageIngredientComparisonPolicy.ComparePresence(payload, true, false, "/Docs/protected.pptx");
            var unexpectedlyPresent = PageIngredientComparisonPolicy.ComparePresence(payload, true, true, "/Docs/protected.pptx");
            var unexpected = PageIngredientComparisonPolicy.ComparePresence(ReproduceAction("ordinary"), true, false, "/Docs/ordinary.pptx");

            Assert.AreEqual(IngredientComparisonOutcome.ExpectedDifference, approved.Outcome);
            Assert.AreEqual(IngredientDifferenceKind.ExpectedAbsent, approved.Difference);
            Assert.AreEqual(IngredientComparisonOutcome.UnexpectedDifference, unexpectedlyPresent.Outcome);
            Assert.AreEqual(IngredientDifferenceKind.UnexpectedPresent, unexpectedlyPresent.Difference);
            Assert.AreEqual(IngredientComparisonOutcome.UnexpectedDifference, unexpected.Outcome);
            Assert.AreEqual(IngredientDifferenceKind.UnexpectedAbsent, unexpected.Difference);
        }

        [TestMethod]
        public void ApprovedExclusionProducesNonExactPageOutcome()
        {
            var protectedPlan = PublishingPageProtectedAssetActionPlanner.Create(MicrosoftSnapshot(), "snapshot-a", null);
            var payload = protectedPlan.Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));

            var outcome = PageReproductionOutcomePolicy.Evaluate(
                true,
                PageMigrationOutcome.ExecutableWithApprovedExclusions,
                new[] { payload.SelectionReceipt });

            Assert.AreEqual(PageReproductionOutcome.ReproducedWithApprovedExclusions, outcome);
            Assert.AreNotEqual(PageReproductionOutcome.ExactReproduction, outcome);
        }

        [TestMethod]
        public void ForgedAuthorizationStatusWithoutRetainedWireEvidenceIsRejected()
        {
            var forged = new PageIngredientAction
            {
                IngredientId = "forged-authorization",
                Capability = IngredientCapability.Missing,
                Disposition = IngredientDisposition.Block,
                TerminalStatus = IngredientTerminalStatus.AuthorizationBlocked,
                AuthorizationStatusCode = 403
            };
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode> { Node("forged-authorization") }
            };

            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, new[] { forged });

            Assert.AreEqual(PageMigrationOutcome.Invalid, evaluation.Outcome);
            Assert.IsTrue(evaluation.Issues.Any(value => value.Code == "IngredientAuthorizationStatusCodeUnretained"));
            Assert.IsTrue(evaluation.Issues.Any(value => value.Code == "IngredientAuthorizationTerminalStatusUnretained"));
            Assert.IsTrue(evaluation.Issues.Any(value => value.Code == "IngredientBlockWithoutAuthorizationEvidence"));
            Assert.AreEqual(PageIngredientExecutionState.Deferred, evaluation.ExecutionFrontier.GetState(forged.IngredientId));
            Assert.IsFalse(evaluation.ExecutionFrontier.HasAuthorizationBlockedIngredients);
        }

        [TestMethod]
        public void Retained403PrunesOnlyItsRequiredConsumerBranch()
        {
            var graph = DependencyGraph();
            var blocked = new PageIngredientAction
            {
                ActionId = "action:dependency",
                IngredientId = "dependency",
                Capability = IngredientCapability.Missing,
                Disposition = IngredientDisposition.Block,
                TerminalStatus = IngredientTerminalStatus.AuthorizationBlocked,
                AuthorizationStatusCode = 403
            };
            var evidence = new Dictionary<string, LiteralHttpAuthorizationEvidence>(StringComparer.Ordinal)
            {
                ["dependency"] = LiteralHttpAuthorizationEvidence.Create(
                    "capture-layout",
                    "https://source.example/_vti_bin/client.svc/ProcessQuery",
                    403,
                    DateTimeOffset.UtcNow)
            };

            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                graph,
                new[] { ReproduceAction("consumer"), blocked, ReproduceAction("independent") },
                evidence);

            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, evaluation.Outcome);
            Assert.IsTrue(evaluation.IsExecutable);
            Assert.AreEqual(PageIngredientExecutionState.AuthorizationBlocked, evaluation.ExecutionFrontier.GetState("dependency"));
            Assert.AreEqual(PageIngredientExecutionState.SkippedByAuthorizationDependency, evaluation.ExecutionFrontier.GetState("consumer"));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState("independent"));
            Assert.IsFalse(evaluation.ExecutionFrontier.ShouldStopWholeItem);

            var authorizationOnly = PageIngredientPlanEvaluator.Evaluate(
                new CanonicalPageIngredientGraph
                {
                    Nodes = new List<PageIngredientNode> { Node("dependency") }
                },
                new[] { blocked },
                evidence);
            Assert.AreEqual(PageMigrationOutcome.AuthorizationBlocked, authorizationOnly.Outcome);
            Assert.IsFalse(authorizationOnly.IsExecutable);
            Assert.IsTrue(authorizationOnly.ExecutionFrontier.ShouldStopWholeItem);
        }

        [TestMethod]
        public void TamperedAuthorizationEvidenceDigestIsRejected()
        {
            var blocked = new PageIngredientAction
            {
                ActionId = "action:dependency",
                IngredientId = "dependency",
                Capability = IngredientCapability.Missing,
                Disposition = IngredientDisposition.Block,
                TerminalStatus = IngredientTerminalStatus.AuthorizationBlocked,
                AuthorizationStatusCode = 403
            };
            var evidence = LiteralHttpAuthorizationEvidence.Create(
                "capture-layout",
                "https://source.example/_vti_bin/client.svc/ProcessQuery",
                403,
                DateTimeOffset.UtcNow);
            evidence.EvidenceSha256 = new string('0', 64);

            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                new CanonicalPageIngredientGraph
                {
                    Nodes = new List<PageIngredientNode> { Node("dependency") }
                },
                new[] { blocked },
                new Dictionary<string, LiteralHttpAuthorizationEvidence>(StringComparer.Ordinal)
                {
                    ["dependency"] = evidence
                });

            Assert.AreEqual(PageMigrationOutcome.Invalid, evaluation.Outcome);
            Assert.IsTrue(evaluation.Issues.Any(value => value.Code == "IngredientAuthorizationEvidenceInvalid"));
            Assert.IsFalse(evaluation.ExecutionFrontier.HasAuthorizationBlockedIngredients);
        }

        [TestMethod]
        public void LiteralAuthorizationEvidenceRoundTripsOnlyInAuthorizationState()
        {
            var evidence = LiteralHttpAuthorizationEvidence.Create(
                "capture-layout",
                "https://source.example/_vti_bin/client.svc/ProcessQuery",
                401,
                DateTimeOffset.UtcNow);
            var snapshot = new PublishingPageLayoutSnapshot
            {
                EvidenceState = PublishingPageLayoutEvidenceState.AuthorizationBlocked,
                Availability = EvidenceAvailability.Unavailable,
                AuthorizationEvidence = evidence
            };

            var roundTrip = PublishingPagePackageSerializer.Deserialize<PublishingPageLayoutSnapshot>(
                PublishingPagePackageSerializer.Serialize(snapshot));

            PublishingPageLayoutPackageValidator.ValidateSnapshot(roundTrip, null);
            Assert.AreEqual(evidence.EvidenceSha256, roundTrip.AuthorizationEvidence.EvidenceSha256);

            roundTrip.EvidenceState = PublishingPageLayoutEvidenceState.AccessDenied;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageLayoutPackageValidator.ValidateSnapshot(roundTrip, null));
        }

        [TestMethod]
        public void DeferredBranchDoesNotBlockIndependentBranch()
        {
            var deferred = new PageIngredientAction
            {
                ActionId = "action:dependency",
                IngredientId = "dependency",
                Capability = IngredientCapability.Incompatible,
                Disposition = IngredientDisposition.Defer,
                TerminalStatus = IngredientTerminalStatus.DecisionRequired,
                Reason = "Awaiting a reviewed replacement."
            };

            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                DependencyGraph(),
                new[] { ReproduceAction("consumer"), deferred, ReproduceAction("independent") });

            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, evaluation.Outcome);
            Assert.IsTrue(evaluation.IsExecutable);
            Assert.IsTrue(evaluation.Issues.Any(value => value.Code == "IngredientMitigationPending"
                && value.Severity == PnP.Framework.Migration.Diagnostics.MigrationIssueSeverity.Warning));
            Assert.AreEqual(PageIngredientExecutionState.Deferred, evaluation.ExecutionFrontier.GetState("dependency"));
            Assert.AreEqual(PageIngredientExecutionState.SkippedByDeferredDependency, evaluation.ExecutionFrontier.GetState("consumer"));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState("independent"));
            Assert.AreEqual(
                PageReproductionOutcome.Rejected,
                PageReproductionOutcomePolicy.Evaluate(true, evaluation.Outcome, Array.Empty<PageIngredientActionSelectionReceipt>()));
        }

        [TestMethod]
        public void PolicyExclusionIsSatisfiedButIsNotAuthorizationEvidence()
        {
            var payload = PublishingPageProtectedAssetActionPlanner.Create(MicrosoftSnapshot(), "snapshot-a", null)
                .Actions.Single(value => value.IngredientId.StartsWith("binary-payload:", StringComparison.Ordinal));
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode> { Node(payload.IngredientId) }
            };

            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, new[] { payload });

            Assert.AreEqual(PageMigrationOutcome.ExecutableWithApprovedExclusions, evaluation.Outcome);
            Assert.AreEqual(PageIngredientExecutionState.SatisfiedByPolicy, evaluation.ExecutionFrontier.GetState(payload.IngredientId));
            Assert.IsFalse(evaluation.ExecutionFrontier.HasAuthorizationBlockedIngredients);

            var invalidEvidence = new Dictionary<string, LiteralHttpAuthorizationEvidence>(StringComparer.Ordinal)
            {
                [payload.IngredientId] = LiteralHttpAuthorizationEvidence.Create(
                    "capture-protected-payload",
                    "https://source.example/Docs/protected.pptx",
                    403,
                    DateTimeOffset.UtcNow)
            };
            var withUnconsumedEvidence = PageIngredientPlanEvaluator.Evaluate(graph, new[] { payload }, invalidEvidence);

            Assert.AreEqual(PageMigrationOutcome.Invalid, withUnconsumedEvidence.Outcome);
            Assert.IsTrue(withUnconsumedEvidence.Issues.Any(value => value.Code == "IngredientAuthorizationEvidenceUnconsumed"));
            Assert.AreEqual(PageIngredientExecutionState.SatisfiedByPolicy, withUnconsumedEvidence.ExecutionFrontier.GetState(payload.IngredientId));
            Assert.IsFalse(withUnconsumedEvidence.ExecutionFrontier.HasAuthorizationBlockedIngredients);
        }

        [TestMethod]
        public void FidelityAllowedProfileRequiresExplicitOptInAndRoundTrips()
        {
            var secureDefault = new PageCaptureOptions();
            Assert.AreEqual(ProtectedAssetCaptureProfile.MicrosoftTenantMetadataOnly, secureDefault.ProtectedAssets.Profile);
            Assert.IsTrue(secureDefault.ProtectedAssets.FailClosedOnUnknown);

            var explicitOocl = new PageCaptureOptions
            {
                ProtectedAssets = ProtectedAssetCapturePolicy.FidelityAllowed("policy.protected-asset.oocl-fidelity")
            };
            var roundTrip = PublishingPagePackageSerializer.Deserialize<PageCaptureOptions>(
                PublishingPagePackageSerializer.Serialize(explicitOocl));

            Assert.AreEqual(ProtectedAssetCaptureProfile.FidelityAllowed, roundTrip.ProtectedAssets.Profile);
            Assert.AreEqual("policy.protected-asset.oocl-fidelity", roundTrip.ProtectedAssets.PolicyId);
            Assert.IsFalse(roundTrip.ProtectedAssets.FailClosedOnUnknown);
        }

        [TestMethod]
        public void ProtectedSelectionRoundTripsAndReportListsApprovedExclusion()
        {
            var snapshot = MicrosoftSnapshot();
            var protectedPlan = PublishingPageProtectedAssetActionPlanner.Create(
                snapshot,
                "snapshot-a",
                null,
                new PageIngredientSelectionAudit
                {
                    SelectedBy = "migration-owner",
                    ApprovalReference = "https://example.test/approvals/191"
                });
            var json = PublishingPagePackageSerializer.Serialize(protectedPlan);
            var roundTrip = PublishingPagePackageSerializer.Deserialize<ProtectedAssetActionPlan>(json);
            PublishingPageProtectedAssetActionPlanner.Validate(snapshot, "snapshot-a", roundTrip, null, new PageIngredientSelectionAudit
            {
                SelectedBy = "migration-owner",
                ApprovalReference = "https://example.test/approvals/191"
            });
            var report = PublishingPagePlanReportFactory.Create(snapshot, new PublishingPageMigrationPlan
            {
                ProtectedAssets = roundTrip,
                MigrationOutcome = PageMigrationOutcome.ExecutableWithApprovedExclusions
            });

            Assert.AreEqual(1, report.ApprovedExclusions.Count);
            StringAssert.Contains(report.ApprovedExclusions[0], "/Docs/protected.pptx");
            StringAssert.Contains(report.ApprovedExclusions[0], "MicrosoftProtectedAssetExportDenied");
            StringAssert.Contains(report.ApprovedExclusions[0], "approvals/191");
        }

        [TestMethod]
        public void PreviousPackageSchemaRequiresExplicitReExport()
        {
            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateExport(new PublishingPageExportPackage
                {
                    SchemaVersion = PublishingPagePackageContract.PreviousExportSchemaVersion
                }));

            StringAssert.Contains(exception.Message, "Re-export");
            StringAssert.Contains(exception.Message, PublishingPagePackageContract.ExportSchemaVersion);
        }

        private static PublishingPageCaptureBundle MicrosoftSnapshot()
        {
            var policy = ProtectedAssetCapturePolicy.MicrosoftTenant();
            var evidence = ProtectedEvidence();
            var decision = ProtectedAssetCaptureGate.Decide(evidence, policy);
            return Snapshot(policy, evidence, decision, withPayload: false);
        }

        private static PublishingPageCaptureBundle Snapshot(
            ProtectedAssetCapturePolicy policy,
            ListDocumentInformationProtectionSnapshot evidence,
            ProtectedAssetCaptureDecision decision,
            bool withPayload)
        {
            var document = new ListDocumentSnapshot
            {
                Kind = ListDocumentObjectKind.File,
                Name = "protected.pptx",
                ServerRelativeUrl = "/Docs/protected.pptx",
                Length = 7,
                MajorVersion = 1,
                InformationProtection = evidence,
                CaptureDecision = decision,
                Content = withPayload
                    ? new ListBinaryArtifactSnapshot
                    {
                        Artifact = MigrationArtifact.Describe(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, "application/vnd.openxmlformats-officedocument.presentationml.presentation", "protected.pptx")
                    }
                    : null
            };
            return new PublishingPageCaptureBundle
            {
                CapturePolicy = new PageCaptureOptions { ProtectedAssets = policy },
                ListDependencies = new List<ListDependencySnapshot>
                {
                    new ListDependencySnapshot
                    {
                        SourceWebId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        SourceListId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        Items = new List<ListItemSnapshot>
                        {
                            new ListItemSnapshot
                            {
                                SourceItemId = 13,
                                SourceUniqueId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                                Document = document
                            }
                        }
                    }
                }
            };
        }

        private static ListDocumentInformationProtectionSnapshot ProtectedEvidence()
        {
            return ListDocumentInformationProtectionSnapshotReader.Read(new Dictionary<string, object>
            {
                ["_IpLabelId"] = "9fbde396-1a24-4c79-8edf-9254a0f35055",
                ["_IpLabelAssignmentMethod"] = "1",
                ["_HasUserDefinedProtection"] = true
            });
        }

        private static PageIngredientNode Node(string id)
        {
            return new PageIngredientNode
            {
                Id = id,
                Kind = PageIngredientKind.BinaryPayload,
                HasContent = true,
                EvidenceReferences = new List<string>()
            };
        }

        private static PageIngredientAction ReproduceAction(string ingredientId)
        {
            return new PageIngredientAction
            {
                ActionId = "action:" + ingredientId,
                IngredientId = ingredientId,
                Capability = IngredientCapability.Available,
                Disposition = IngredientDisposition.Preserve,
                PolicyId = "policy.test",
                PolicyVersion = "1",
                TerminalStatus = IngredientTerminalStatus.Executable
            };
        }

        private static CanonicalPageIngredientGraph DependencyGraph()
        {
            return new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    Node("consumer"),
                    Node("dependency"),
                    Node("independent")
                },
                Edges = new List<PageIngredientEdge>
                {
                    new PageIngredientEdge
                    {
                        FromIngredientId = "consumer",
                        ToIngredientId = "dependency",
                        Relationship = PageIngredientRelationship.DependsOn,
                        Requirement = PageIngredientRequirement.Required
                    }
                }
            };
        }
    }
}
