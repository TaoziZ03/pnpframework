using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Test.EnterpriseWiki;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PnP.Framework.Test.Migration.Pages.Publishing.Comparison
{
    [TestClass]
    public class PublishingPageIngredientVerificationCatalogTests
    {
        [TestMethod]
        public void RegisteredContributorsAreDeterministicAndBindFreshObservations()
        {
            var request = CreateRequest();
            var selected = MaterialNodesWithActions(request).Take(2).ToList();
            var templates = request.Ingredients
                .Where(value => selected.Any(node => node.Id == value.IngredientId))
                .ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            request.Ingredients = request.Ingredients
                .Where(value => !templates.ContainsKey(value.IngredientId))
                .ToList();
            var runtimeRequirementId = request.Package.Plan.RuntimeVerification.Requirements.Single().Id;
            var ownerCatalog = OwnerCatalog(selected, out var ownerHandlers);
            var contributors = ownerHandlers.Select((handler, index) => new TestContributor(
                    handler.Descriptor.HandlerId,
                    handler.Descriptor.OrderGroup,
                    context => FreshObservation(
                        context,
                        templates[context.Ingredient.Id],
                        index == 0 ? runtimeRequirementId : null)))
                .Reverse()
                .Cast<IPublishingPageIngredientVerificationContributor>()
                .ToList();

            var forward = new PublishingPageIngredientVerificationCatalog(ownerCatalog, contributors)
                .Contribute(request);
            var reverse = new PublishingPageIngredientVerificationCatalog(
                    ownerCatalog,
                    contributors.AsEnumerable().Reverse())
                .Contribute(request);

            Assert.AreEqual(
                MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(forward)),
                MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(reverse)));
            CollectionAssert.AreEqual(
                forward.Select(value => value.IngredientId).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                forward.Select(value => value.IngredientId).ToArray());
            Assert.AreEqual(selected.Count, forward.Count);
            foreach (var node in selected)
            {
                var observation = forward.Single(value => value.IngredientId == node.Id);
                var result = PublishingPageCompareReconciler.Classify(request, observation, "passed");
                var action = request.Package.Plan.IngredientActions.Single(value => value.IngredientId == node.Id);
                Assert.AreEqual(PublishingPageCompareContract.ResultClasses.Exact, result.ResultClass);
                Assert.AreEqual(request.Package.PlanDigest, result.Lineage.PlanDigestSha256);
                Assert.AreEqual(node.Id, result.Lineage.SourceIngredientId);
                Assert.AreEqual(action.ActionId, result.Lineage.ActionId);
                Assert.AreEqual(action.TargetIdentity, result.Lineage.TargetIdentity);
                Assert.IsTrue(result.Lineage.EvidenceRefs.Contains(
                    "sha256:" + result.Lineage.SourceArtifactDigestSha256));
                Assert.IsTrue(result.Lineage.EvidenceRefs.Contains(
                    "sha256:" + result.Lineage.TargetEvidenceDigestSha256));
            }
            var runtimeBound = forward.Single(value =>
                value.RuntimeRequirementIds?.Contains(runtimeRequirementId) == true);
            Assert.IsTrue(runtimeBound.Lineage.EvidenceRefs.Contains(
                "sha256:" + request.RuntimeReceipt.Results.Single().EvidenceArtifactSha256));
        }

        [TestMethod]
        public void UnregisteredContributorAndWrongPlanBindingFailClosed()
        {
            var duplicateRequest = CreateRequest();
            var node = MaterialNodesWithActions(duplicateRequest).First();
            var template = duplicateRequest.Ingredients.Single(value => value.IngredientId == node.Id);
            duplicateRequest.Ingredients.Remove(template);
            var ownerCatalog = OwnerCatalog(new[] { node }, out var owners);
            var owner = owners.Single();
            var first = new TestContributor("pnp.test.a/v1", 10,
                context => FreshObservation(context, template, null));
            var second = new TestContributor("pnp.test.b/v1", 20,
                context => FreshObservation(context, template, null));

            Assert.ThrowsException<InvalidDataException>(() =>
                new PublishingPageIngredientVerificationCatalog(ownerCatalog, new[] { first, second }));

            var bindingRequest = CreateRequest();
            node = MaterialNodesWithActions(bindingRequest).First();
            template = bindingRequest.Ingredients.Single(value => value.IngredientId == node.Id);
            bindingRequest.Ingredients.Remove(template);
            ownerCatalog = OwnerCatalog(new[] { node }, out owners);
            owner = owners.Single();
            var wrongPlan = new TestContributor(owner.Descriptor.HandlerId, 10, context =>
            {
                var observation = FreshObservation(context, template, null);
                observation.Lineage.PlanDigestSha256 = Hash("wrong-plan");
                return observation;
            });

            Assert.ThrowsException<InvalidDataException>(() =>
                new PublishingPageIngredientVerificationCatalog(ownerCatalog, new[] { wrongPlan })
                    .Contribute(bindingRequest));

            var foreignOwnerRequest = CreateRequest();
            node = MaterialNodesWithActions(foreignOwnerRequest).First();
            template = foreignOwnerRequest.Ingredients.Single(value => value.IngredientId == node.Id);
            foreignOwnerRequest.Ingredients.Remove(template);
            ownerCatalog = OwnerCatalog(new[] { node }, out owners);
            owner = owners.Single();
            var contributor = new TestContributor(owner.Descriptor.HandlerId, 10,
                context => FreshObservation(context, template, null));
            node.SourcePredicateId = "foreign.owner.predicate";

            Assert.ThrowsException<InvalidDataException>(() =>
                new PublishingPageIngredientVerificationCatalog(ownerCatalog, new[] { contributor })
                    .Contribute(foreignOwnerRequest));
        }

        [TestMethod]
        public void EvidenceAndRuntimeFailuresCannotBecomeExactAndSourceVersionWins()
        {
            var request = CreateRequest();
            var observation = ExecutableObservation(request);
            var expectedClasses = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [IngredientTargetEvidenceStates.Unknown] = PublishingPageCompareContract.ResultClasses.Unknown,
                [IngredientTargetEvidenceStates.Partial] = PublishingPageCompareContract.ResultClasses.Unknown,
                [IngredientTargetEvidenceStates.Missing] = PublishingPageCompareContract.ResultClasses.Missing,
                [IngredientTargetEvidenceStates.Denied] = PublishingPageCompareContract.ResultClasses.AuthorizationBlocked,
                [IngredientTargetEvidenceStates.Expired] = PublishingPageCompareContract.ResultClasses.AuthExpired
            };
            foreach (var pair in expectedClasses)
            {
                observation.TargetEvidenceState = pair.Key;
                var result = PublishingPageCompareReconciler.Classify(request, observation, "passed");
                Assert.AreEqual(pair.Value, result.ResultClass, pair.Key);
                Assert.AreNotEqual(PublishingPageCompareContract.ResultClasses.Exact, result.ResultClass, pair.Key);
            }

            request.SourceVersionComparison.ObservedCompositeDigestSha256 = Hash("changed-source");
            request.SourceVersionComparison.Status = "changed";
            observation.TargetEvidenceState = IngredientTargetEvidenceStates.Missing;
            var sourceChanged = PublishingPageCompareReconciler.Classify(request, observation, "passed");
            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.SourceVersionChanged, sourceChanged.ResultClass);

            var sourceUnknownRequest = CreateRequest();
            sourceUnknownRequest.SourceVersionComparison.Status = "unknown";
            sourceUnknownRequest.SourceVersionComparison.ObservedCompositeDigestSha256 = null;
            var sourceUnknownObservation = ExecutableObservation(sourceUnknownRequest);
            sourceUnknownObservation.TargetEvidenceState = IngredientTargetEvidenceStates.Fresh;
            var sourceUnknown = PublishingPageCompareReconciler.Classify(
                sourceUnknownRequest,
                sourceUnknownObservation,
                "passed");
            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.Unknown, sourceUnknown.ResultClass);
            Assert.AreEqual(PublishingPageCompareContract.ReasonCodes.SourceVersionUnknown, sourceUnknown.ReasonCode);

            var runtimeFailedRequest = CreateRequest(runtimePassed: false);
            var runtimeFailedObservation = ExecutableObservation(runtimeFailedRequest);
            runtimeFailedObservation.TargetEvidenceState = IngredientTargetEvidenceStates.Fresh;
            runtimeFailedObservation.RuntimeRequirementIds = new List<string>
            {
                runtimeFailedRequest.Package.Plan.RuntimeVerification.Requirements.Single().Id
            };
            var runtimeFailed = PublishingPageCompareReconciler.Classify(
                runtimeFailedRequest,
                runtimeFailedObservation,
                "failed");
            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.Mismatch, runtimeFailed.ResultClass);
            Assert.AreEqual(PublishingPageCompareContract.ReasonCodes.RuntimeRequirementFailed, runtimeFailed.ReasonCode);

            var pendingRequest = CreateRequest(includeRuntimeReceipt: false);
            var pendingObservation = ExecutableObservation(pendingRequest);
            pendingObservation.TargetEvidenceState = IngredientTargetEvidenceStates.Fresh;
            pendingObservation.RuntimeRequirementIds = new List<string>
            {
                pendingRequest.Package.Plan.RuntimeVerification.Requirements.Single().Id
            };
            var pending = PublishingPageCompareReconciler.Classify(
                pendingRequest,
                pendingObservation,
                "pending");
            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.RuntimePending, pending.ResultClass);
        }

        [TestMethod]
        public void LegacyCompareEntryPointAndV1SerializationRemainCompatible()
        {
            var request = CreateRequest();

            var report = PublishingPageCompareReconciler.Reconcile(
                request,
                new PermissiveArtifactStore());
            var json = MigrationContractSerializer.SerializeCanonical(report);

            Assert.AreEqual(PublishingPageCompareContract.SchemaVersion, report.SchemaVersion);
            Assert.IsFalse(json.Contains("\"targetEvidenceState\""));
            Assert.IsFalse(json.Contains("\"observedAtUtc\""));
            Assert.IsFalse(json.Contains("\"targetEvidenceDigestSha256\""));
            Assert.AreEqual(report.ReportDigestSha256, PublishingPageCompareReconciler.ComputeReportDigest(report));
        }

        [TestMethod]
        public void V1NoRequiredRuntimeEvidencePreservesPassedAndNotRequiredReaderCompatibility()
        {
            foreach (var optionalResultPassed in new[] { true, false })
            {
                var optionalRequest = CreateRequest();
                foreach (var requirement in optionalRequest.Package.Plan.RuntimeVerification.Requirements)
                {
                    requirement.Required = false;
                }
                foreach (var result in optionalRequest.RuntimeReceipt.Results)
                {
                    result.Passed = optionalResultPassed;
                }
                ResealV1RuntimeRequest(optionalRequest);
                Assert.AreEqual("not-required", Reconcile(optionalRequest).Runtime.Status);
            }

            var optionalNotRequiredRequest = CreateRequest();
            foreach (var requirement in optionalNotRequiredRequest.Package.Plan.RuntimeVerification.Requirements)
            {
                requirement.Required = false;
            }
            optionalNotRequiredRequest.RuntimeReceipt.Status = RuntimeVerificationStatus.NotRequired;
            ResealV1RuntimeRequest(optionalNotRequiredRequest);
            Assert.AreEqual("not-required", Reconcile(optionalNotRequiredRequest).Runtime.Status);

            foreach (var status in new[]
            {
                RuntimeVerificationStatus.Passed,
                RuntimeVerificationStatus.NotRequired
            })
            {
                var emptyRequest = CreateRequest();
                emptyRequest.Package.Plan.RuntimeVerification.Requirements.Clear();
                emptyRequest.RuntimeReceipt.Results.Clear();
                foreach (var observation in emptyRequest.Ingredients)
                {
                    observation.RuntimeRequirementId = null;
                }
                emptyRequest.RuntimeReceipt.Status = status;
                ResealV1RuntimeRequest(emptyRequest);
                Assert.AreEqual("not-required", Reconcile(emptyRequest).Runtime.Status);
            }
        }

        [TestMethod]
        public void V2AssertionResultsContributeDeterministicCompareRowsAndPendingCoverage()
        {
            var request = CreateRequest();
            UpgradeToV2Assertion(request);
            var catalog = new PublishingPageIngredientVerificationCatalog(
                PublishingPageIngredientHandlerCatalog.Empty,
                Array.Empty<IPublishingPageIngredientVerificationContributor>());

            var report = PublishingPageCompareReconciler.ReconcileWithContributors(
                request,
                catalog,
                new PermissiveArtifactStore());

            Assert.AreEqual(1, report.Assertions.Count);
            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.Exact, report.Assertions[0].ResultClass);
            Assert.AreEqual(request.Package.PlanDigest, report.Assertions[0].PlanDigest);
            CollectionAssert.AreEqual(
                request.Package.Plan.RuntimeVerification.Assertions[0].AttachedActionReferences
                    .Select(value => value.ActionId)
                    .ToArray(),
                report.Assertions[0].AttachedActionIds.ToArray());
            Assert.AreEqual(3, report.Assertions[0].EvidenceRefs.Count);
            Assert.AreEqual(report.ReportDigestSha256, PublishingPageCompareReconciler.ComputeReportDigest(report));

            request.RuntimeReceipt = null;
            request.RuntimeReceiptDigestSha256 = null;
            request.Bindings.RuntimeReceiptSchemaVersion = null;
            request.Bindings.RuntimeReceiptDigestSha256 = null;
            var pending = PublishingPageCompareReconciler.ReconcileWithContributors(
                request,
                catalog,
                new PermissiveArtifactStore());
            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.RuntimePending, pending.Assertions[0].ResultClass);
        }

        [TestMethod]
        public void V2RequirementAndAssertionOutcomeMatrixReachesCompare()
        {
            foreach (var requirementPassed in new[] { true, false })
            {
                foreach (var assertionPassed in new[] { true, false })
                {
                    var request = CreateRequest(runtimePassed: requirementPassed);
                    UpgradeToV2Assertion(request);
                    request.RuntimeReceipt.AssertionResults[0].Passed = assertionPassed;
                    request.RuntimeReceipt.AssertionResults[0].FailureReasonCode = assertionPassed
                        ? null
                        : "ASSERTION_FAILED";
                    request.RuntimeReceipt.Status = requirementPassed && assertionPassed
                        ? RuntimeVerificationStatus.Passed
                        : RuntimeVerificationStatus.Failed;
                    request.ImportReceipt.RuntimeVerificationStatus = request.RuntimeReceipt.Status;
                    request.ImportReceiptDigestSha256 = ContractDigest(request.ImportReceipt);
                    request.Bindings.ImportReceiptDigestSha256 = request.ImportReceiptDigestSha256;
                    request.RuntimeReceiptDigestSha256 = ContractDigest(request.RuntimeReceipt);
                    request.Bindings.RuntimeReceiptDigestSha256 = request.RuntimeReceiptDigestSha256;

                    var report = PublishingPageCompareReconciler.ReconcileWithContributors(
                        request,
                        new PublishingPageIngredientVerificationCatalog(
                            PublishingPageIngredientHandlerCatalog.Empty,
                            Array.Empty<IPublishingPageIngredientVerificationContributor>()),
                        new PermissiveArtifactStore());

                    Assert.AreEqual(
                        requirementPassed && assertionPassed ? "passed" : "failed",
                        report.Runtime.Status);
                    Assert.AreEqual(
                        assertionPassed
                            ? PublishingPageCompareContract.ResultClasses.Exact
                            : PublishingPageCompareContract.ResultClasses.Mismatch,
                        report.Assertions.Single().ResultClass);
                }
            }
        }

        private static IngredientCompareObservation FreshObservation(
            PublishingPageIngredientVerificationContext context,
            IngredientCompareObservation template,
            string runtimeRequirementId)
        {
            var sourceDigest = IsDigest(context.Ingredient.EvidenceDigest)
                ? context.Ingredient.EvidenceDigest
                : template.Lineage.SourceArtifactDigestSha256;
            var targetDigest = Hash("fresh-target:" + context.Ingredient.Id);
            return new IngredientCompareObservation
            {
                IngredientId = context.Ingredient.Id,
                Kind = template.Kind,
                Material = true,
                TargetEvidenceComplete = true,
                TargetPresent = true,
                TargetEvidenceState = IngredientTargetEvidenceStates.Fresh,
                ObservedAtUtc = context.Request.GeneratedAtUtc,
                RuntimeRequirementIds = string.IsNullOrWhiteSpace(runtimeRequirementId)
                    ? null
                    : new List<string> { runtimeRequirementId },
                Lineage = new IngredientCompareLineage
                {
                    RequestedUrlHashSha256 = template.Lineage.RequestedUrlHashSha256,
                    SourceArtifactDigestSha256 = sourceDigest,
                    TargetEvidenceDigestSha256 = targetDigest,
                    EvidenceRefs = new List<string>()
                },
                Expected = new CompareDigestPair
                {
                    RawDigestSha256 = template.Expected.RawDigestSha256,
                    CanonicalDigestSha256 = template.Expected.CanonicalDigestSha256
                },
                Actual = new CompareDigestPair
                {
                    RawDigestSha256 = template.Actual.RawDigestSha256,
                    CanonicalDigestSha256 = template.Actual.CanonicalDigestSha256
                }
            };
        }

        private static IEnumerable<PageIngredientNode> MaterialNodesWithActions(
            PublishingPageCompareRequest request)
        {
            var actionIds = new HashSet<string>(
                request.Package.Plan.IngredientActions
                    .Where(value => !string.IsNullOrWhiteSpace(value.ActionId)
                        && !string.IsNullOrWhiteSpace(value.TargetIdentity))
                    .Select(value => value.IngredientId),
                StringComparer.Ordinal);
            return request.Package.Plan.IngredientGraph.Nodes
                .Where(value => value.HasContent
                    && actionIds.Contains(value.Id)
                    && request.Package.Plan.ExecutionFrontier.GetState(value.Id)
                        == PageIngredientExecutionState.Executable)
                .OrderBy(value => value.Id, StringComparer.Ordinal);
        }

        private static IngredientCompareObservation ExecutableObservation(
            PublishingPageCompareRequest request)
        {
            return request.Ingredients.First(value =>
                request.Package.Plan.ExecutionFrontier.GetState(value.IngredientId)
                    == PageIngredientExecutionState.Executable);
        }

        private static PublishingPageCompareRequest CreateRequest(
            bool includeRuntimeReceipt = true,
            StorageVerificationStatus storageStatus = StorageVerificationStatus.Passed,
            bool runtimePassed = true)
        {
            var method = typeof(EnterpriseWikiMigrationTests).GetMethod(
                "CreateMigrationPackage",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            var package = (PublishingPageMigrationPackage)method.Invoke(null, null);
            var fileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var canonicalTarget = "https://target.sharepoint.com/sites/target/pages/source.aspx";
            var importReceipt = new PublishingPageImportReceipt
            {
                ApprovedPlanDigest = package.PlanDigest,
                TargetWebUrl = package.Plan.TargetWebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                TargetFileUniqueId = fileId,
                TargetListItemId = 42,
                TargetVersionLabel = "1.0",
                FreshReadbackPassed = storageStatus == StorageVerificationStatus.Passed,
                StorageVerificationStatus = storageStatus,
                RuntimeVerificationStatus = includeRuntimeReceipt
                    ? (runtimePassed ? RuntimeVerificationStatus.Passed : RuntimeVerificationStatus.Failed)
                    : RuntimeVerificationStatus.Pending
            };
            var importDigest = ContractDigest(importReceipt);
            RuntimeVerificationReceipt runtimeReceipt = null;
            string runtimeDigest = null;
            if (includeRuntimeReceipt)
            {
                runtimeReceipt = new RuntimeVerificationReceipt
                {
                    PlanDigest = package.PlanDigest,
                    TargetIdentity = canonicalTarget,
                    CompletedAtUtc = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero),
                    Status = runtimePassed ? RuntimeVerificationStatus.Passed : RuntimeVerificationStatus.Failed,
                    Results = package.Plan.RuntimeVerification.Requirements.Select(requirement =>
                        new RuntimeVerificationResult
                        {
                            RequirementId = requirement.Id,
                            Passed = runtimePassed,
                            EvidenceArtifactSha256 = Hash("runtime-evidence:" + requirement.Id),
                            Message = "synthetic fixed evidence"
                        }).ToList()
                };
                runtimeDigest = ContractDigest(runtimeReceipt);
            }

            var request = new PublishingPageCompareRequest
            {
                GeneratedAtUtc = new DateTimeOffset(2026, 9, 9, 10, 5, 0, TimeSpan.Zero),
                Producer = new CompareProducer
                {
                    Id = "pnp-compare",
                    Version = "2.0.0-test",
                    ImplementationRef = "ccd-168-runtime-v2"
                },
                AssessmentHandoff = AssessmentHandoff(),
                AssessmentProducerRevisionId = PublishingPageCompareContract.AssessmentProducerRevisionId,
                Package = package,
                ImportReceipt = importReceipt,
                ImportReceiptDigestSha256 = importDigest,
                RuntimeReceipt = runtimeReceipt,
                RuntimeReceiptDigestSha256 = runtimeDigest,
                Bindings = new CompareBindings
                {
                    ManifestDigestSha256 = Hash("manifest"),
                    SourceCaptureReceiptDigestSha256 = Hash("capture-receipt"),
                    ExportSchemaVersion = package.ExportSchemaVersion,
                    SnapshotDigestSha256 = package.SnapshotDigest,
                    IngredientGraphSchemaVersion = package.Plan.IngredientGraph.SchemaVersion,
                    IngredientProjectionVersion = package.Plan.IngredientGraph.ProjectionVersion,
                    MigrationPackageSchemaVersion = package.SchemaVersion,
                    PlanDigestSha256 = package.PlanDigest,
                    ImportReceiptSchemaVersion = importReceipt.SchemaVersion,
                    ImportReceiptDigestSha256 = importDigest,
                    RuntimeReceiptSchemaVersion = runtimeReceipt?.SchemaVersion,
                    RuntimeReceiptDigestSha256 = runtimeDigest
                },
                SourceVersionComparison = new SourceVersionComparison
                {
                    ExpectedCompositeDigestSha256 = Hash("source-version"),
                    ObservedCompositeDigestSha256 = Hash("source-version"),
                    Status = "matched"
                },
                PlannedTargetIdentity = new CompareTargetIdentity
                {
                    WebUrlHashSha256 = Hash(package.Plan.TargetWebUrl.ToLowerInvariant()),
                    PageServerRelativeUrlHashSha256 = Hash(package.Plan.TargetPageServerRelativeUrl.ToLowerInvariant()),
                    FileUniqueId = fileId,
                    ListItemId = 42,
                    VersionLabel = "1.0",
                    CanonicalIdentity = canonicalTarget
                }
            };
            request.AssessmentHandoffDigestSha256 = ContractDigest(request.AssessmentHandoff);
            request.Ingredients = package.Plan.IngredientGraph.Nodes.Select(node =>
            {
                var action = package.Plan.IngredientActions.SingleOrDefault(value => value.IngredientId == node.Id);
                var executionState = package.Plan.ExecutionFrontier.GetState(node.Id);
                var digest = Hash("ingredient:" + node.Id);
                return new IngredientCompareObservation
                {
                    IngredientId = node.Id,
                    Kind = node.Kind.ToString(),
                    Material = executionState == PageIngredientExecutionState.Executable,
                    RuntimeRequirementId = node.RuntimeRequirement,
                    Lineage = new IngredientCompareLineage
                    {
                        RequestedUrlHashSha256 = Hash("requested-url"),
                        SourceArtifactDigestSha256 = Hash("source-artifact:" + node.Id),
                        SourceIngredientId = node.Id,
                        ActionId = action?.ActionId,
                        TargetIdentity = action?.TargetIdentity,
                        EvidenceRefs = new List<string> { "sha256:" + Hash("evidence:" + node.Id) }
                    },
                    Expected = new CompareDigestPair { RawDigestSha256 = digest, CanonicalDigestSha256 = digest },
                    Actual = new CompareDigestPair { RawDigestSha256 = digest, CanonicalDigestSha256 = digest }
                };
            }).ToList();
            return request;
        }

        private static AssessmentCaptureHandoff AssessmentHandoff()
        {
            return new AssessmentCaptureHandoff
            {
                Schema = PublishingPageCompareContract.AssessmentHandoffSchemaVersion,
                ManifestRevisionId = "sha256:" + Hash("assessment-manifest-revision"),
                AllowlistRevisionId = "sha256:" + Hash("assessment-allowlist-revision"),
                SampleId = "A1-001",
                StageMembership = new List<string> { "A0", "A1" },
                CanonicalLocatorRef = "restricted://inventory/A1-001",
                CanonicalLocatorHash = "sha256:" + Hash("assessment-canonical-locator"),
                ResourceIdentityHash = "sha256:" + Hash("assessment-resource-identity"),
                ApprovedHostHash = "hmac-sha256:" + Hash("assessment-approved-host"),
                StratumId = "S1-known-classic",
                ExpectedProfile = "publishing_page",
                PermissionSignal = "known_readable",
                RedirectSignal = "none_observed",
                UnknownSignals = new List<string>(),
                RequestPolicy = new AssessmentCaptureRequestPolicy
                {
                    Methods = new List<string> { "HEAD", "GET" },
                    AutoDiscovery = false,
                    FollowRedirects = "same-approved-host-only",
                    MaxRedirects = 3,
                    MutationAllowed = false
                }
            };
        }

        private static void UpgradeToV2Assertion(PublishingPageCompareRequest request)
        {
            var action = request.Package.Plan.IngredientActions.First(value =>
                request.Package.Plan.IngredientGraph.Nodes.Any(node =>
                    node.Id == value.IngredientId && node.HasContent));
            var assertion = new RuntimeVerificationAssertion
            {
                AssertionId = "ccd.runtime-verification.interaction/v1:source:search-submit",
                AssertionOwnerLane = "behavior.interaction",
                Subtype = "interaction.search-submit",
                SemanticRole = "interaction-state-transition",
                SourcePredicateId = "interaction.search-box.try-in-place",
                SourcePredicateVersion = "1",
                SourcePageOrListItemIdentity = "source-page:item-7",
                SourceVersionIdentity = "version:41",
                SourceEvidenceDigestSha256 = Hash("assertion-source-evidence"),
                StableAssertionKey = "search-submit",
                TargetProfileId = "cupcollect-classic-page/v1",
                FixtureContractVersion = "pnp-ingredient-fixture/v1",
                AttachedActionReferences = new List<RuntimeVerificationActionReference>
                {
                    new RuntimeVerificationActionReference
                    {
                        IngredientId = action.IngredientId,
                        ActionId = action.ActionId,
                        DependencyRole = "read-only-interaction-container"
                    }
                },
                Intent = new RuntimeVerificationAssertionIntent
                {
                    InitialState = new RuntimeVerificationStateExpectation
                    {
                        StateId = "search-ready",
                        Predicate = "search box is ready",
                        EvidenceKind = "dom-accessibility-snapshot"
                    },
                    Action = new RuntimeVerificationActionIntent
                    {
                        ActionId = action.ActionId,
                        Kind = "fill-and-submit",
                        Selector = "input[type=search]",
                        Input = "ccd",
                        TimeoutMilliseconds = 5000
                    },
                    ExpectedFinalState = new RuntimeVerificationStateExpectation
                    {
                        StateId = "results-updated",
                        Predicate = "results reflect the query",
                        EvidenceKind = "dom-accessibility-network-snapshot"
                    }
                }
            };
            request.Package.Plan.RuntimeVerification.SchemaVersion = RuntimeVerificationContractValidator.ManifestSchemaV2;
            request.Package.Plan.RuntimeVerification.Assertions = new List<RuntimeVerificationAssertion> { assertion };
            request.Package.PlanDigest = PublishingPageDigest.ComputePlanDigest(request.Package.Plan);
            request.Bindings.PlanDigestSha256 = request.Package.PlanDigest;
            request.ImportReceipt.ApprovedPlanDigest = request.Package.PlanDigest;
            request.ImportReceiptDigestSha256 = ContractDigest(request.ImportReceipt);
            request.Bindings.ImportReceiptDigestSha256 = request.ImportReceiptDigestSha256;

            var initial = request.GeneratedAtUtc.AddMinutes(-3);
            var actionAt = initial.AddSeconds(1);
            var final = actionAt.AddSeconds(1);
            request.RuntimeReceipt.SchemaVersion = RuntimeVerificationContractValidator.ReceiptSchemaV2;
            request.RuntimeReceipt.PlanDigest = request.Package.PlanDigest;
            request.RuntimeReceipt.CompletedAtUtc = final.AddSeconds(1);
            request.RuntimeReceipt.AssertionResults = new List<RuntimeVerificationAssertionResult>
            {
                new RuntimeVerificationAssertionResult
                {
                    AssertionId = assertion.AssertionId,
                    PlanDigest = request.Package.PlanDigest,
                    TargetIdentity = request.PlannedTargetIdentity.CanonicalIdentity,
                    ObservedAtUtc = final,
                    Passed = true,
                    InitialStateEvidence = AssertionEvidence("initial", assertion, action.ActionId, request, initial),
                    ActionEvidence = AssertionEvidence("action", assertion, action.ActionId, request, actionAt),
                    FinalStateEvidence = AssertionEvidence("final", assertion, action.ActionId, request, final)
                }
            };
            request.RuntimeReceiptDigestSha256 = ContractDigest(request.RuntimeReceipt);
            request.Bindings.RuntimeReceiptSchemaVersion = request.RuntimeReceipt.SchemaVersion;
            request.Bindings.RuntimeReceiptDigestSha256 = request.RuntimeReceiptDigestSha256;
        }

        private static PublishingPageCompareReport Reconcile(PublishingPageCompareRequest request)
        {
            return PublishingPageCompareReconciler.ReconcileWithContributors(
                request,
                new PublishingPageIngredientVerificationCatalog(
                    PublishingPageIngredientHandlerCatalog.Empty,
                    Array.Empty<IPublishingPageIngredientVerificationContributor>()),
                new PermissiveArtifactStore());
        }

        private static void ResealV1RuntimeRequest(PublishingPageCompareRequest request)
        {
            request.Package.PlanDigest = PublishingPageDigest.ComputePlanDigest(request.Package.Plan);
            request.Bindings.PlanDigestSha256 = request.Package.PlanDigest;
            request.ImportReceipt.ApprovedPlanDigest = request.Package.PlanDigest;
            request.ImportReceipt.RuntimeVerificationStatus = request.RuntimeReceipt.Status;
            request.RuntimeReceipt.PlanDigest = request.Package.PlanDigest;
            request.ImportReceiptDigestSha256 = ContractDigest(request.ImportReceipt);
            request.Bindings.ImportReceiptDigestSha256 = request.ImportReceiptDigestSha256;
            request.RuntimeReceiptDigestSha256 = ContractDigest(request.RuntimeReceipt);
            request.Bindings.RuntimeReceiptDigestSha256 = request.RuntimeReceiptDigestSha256;
        }

        private static RuntimeVerificationStateEvidence AssertionEvidence(
            string stage,
            RuntimeVerificationAssertion assertion,
            string actionId,
            PublishingPageCompareRequest request,
            DateTimeOffset observedAtUtc)
        {
            return new RuntimeVerificationStateEvidence
            {
                Stage = stage,
                AssertionId = assertion.AssertionId,
                ActionId = actionId,
                PlanDigest = request.Package.PlanDigest,
                TargetIdentity = request.PlannedTargetIdentity.CanonicalIdentity,
                ObservedAtUtc = observedAtUtc,
                ObservedStateDigestSha256 = Hash(stage + ":state"),
                EvidenceArtifactSha256 = Hash(stage + ":artifact")
            };
        }

        private static PublishingPageIngredientHandlerCatalog OwnerCatalog(
            IEnumerable<PageIngredientNode> nodes,
            out IList<OwnerHandler> handlers)
        {
            var entries = new List<PageIngredientPrimaryOwnerDescriptor>();
            var predicates = new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(StringComparer.Ordinal);
            var values = new List<OwnerHandler>();
            var index = 0;
            foreach (var node in nodes)
            {
                var laneId = "lane.test." + index;
                var predicateId = "predicate.test." + index;
                node.Subtype = "subtype.test." + index;
                node.SemanticRole = "semantic-role-test";
                node.SourcePredicateId = predicateId;
                node.SourcePageOrListItemIdentity = "source:test";
                node.SourceVersionIdentity = "version:test";
                node.PrimaryOwnerLane = laneId;
                entries.Add(new PageIngredientPrimaryOwnerDescriptor(
                    "owner.test." + index,
                    node.Kind,
                    node.Subtype,
                    node.SemanticRole,
                    predicateId,
                    laneId));
                predicates.Add(predicateId, _ => true);
                values.Add(new OwnerHandler("pnp.test." + index + "/v1", 100 - index, node.Id, laneId));
                index++;
            }
            handlers = values;
            return new PublishingPageIngredientHandlerCatalog(
                values,
                new PublishingPageIngredientPrimaryOwnerRegistry(entries, predicates));
        }

        private static string Hash(string value) => MigrationDigest.ComputeSha256(value);

        private static string ContractDigest<T>(T value) =>
            MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value));

        private static bool IsDigest(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(character => (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }

        private sealed class TestContributor : IPublishingPageIngredientVerificationContributor
        {
            private readonly Func<PublishingPageIngredientVerificationContext, IngredientCompareObservation> observe;

            public TestContributor(
                string handlerId,
                int orderGroup,
                Func<PublishingPageIngredientVerificationContext, IngredientCompareObservation> observe)
            {
                HandlerId = handlerId;
                OrderGroup = orderGroup;
                this.observe = observe;
            }

            public string HandlerId { get; }

            public int OrderGroup { get; }

            public IngredientCompareObservation Observe(PublishingPageIngredientVerificationContext context)
            {
                return observe(context);
            }
        }

        private sealed class OwnerHandler : PublishingPageIngredientHandler<object>
        {
            private readonly PageIngredientHandlerDescriptor descriptor;

            public OwnerHandler(string handlerId, int orderGroup, string ingredientId, string laneId)
            {
                descriptor = new PageIngredientHandlerDescriptor(
                    handlerId,
                    new PageIngredientLaneDescriptor(laneId, new[] { "enterprise-wiki/v1" }),
                    new[] { "pnp-test-owner-evidence/v1" },
                    PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                    orderGroup,
                    new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Exact, ingredientId) });
            }

            public override PageIngredientHandlerDescriptor Descriptor => descriptor;

            protected override void ProjectGraph(
                PublishingPageIngredientGraphProjectionContext context,
                PublishingPageIngredientEvidenceEnvelope envelope,
                object evidence)
            {
            }
        }

        private sealed class PermissiveArtifactStore : IMigrationArtifactStore
        {
            public bool Contains(string sha256) => true;

            public Stream OpenRead(string sha256) => throw new NotSupportedException();

            public ArtifactReference Put(Stream content, string mediaType = null, string originalName = null)
            {
                throw new NotSupportedException();
            }
        }
    }
}
