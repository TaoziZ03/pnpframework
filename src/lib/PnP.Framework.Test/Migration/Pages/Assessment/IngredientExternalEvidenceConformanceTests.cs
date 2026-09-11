using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Assessment.Maturity.External;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PnP.Framework.Test.Migration.Pages.Assessment
{
    [TestClass]
    public class IngredientExternalEvidenceConformanceTests
    {
        [TestMethod]
        public void ExactBatchPlanBytesKeepTheirOriginalSchemaAndDistinctDigests()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            var original = IngredientExternalEvidenceTestFixture.OriginalBytes("OriginalPlan");
            Assert.AreEqual(IngredientExternalEvidenceTestFixture.OriginalPlanSha, MigrationDigest.ComputeSha256(original));
            using var parsed = JsonDocument.Parse(original);
            Assert.AreEqual(IngredientExternalEvidenceTestFixture.PlanDigest, ExternalEvidenceJson.BatchPlanDigest(parsed.RootElement));
            Assert.AreNotEqual(fixture.Plan.ExpectedPlanDigest, fixture.Plan.External.PlanArtifact.Sha256);
            Assert.IsNull(fixture.Plan.Plan);
            AllPassed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Context, fixture.Plan));
            CollectionAssert.AreEqual(original, fixture.Store.Bytes(fixture.Plan.External.PlanArtifact.Sha256));
        }

        [TestMethod]
        public void OriginalLifecycleV2ValidatesWithoutSyntheticGuidOrPublishingReceipts()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            AllPassed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational));
            Assert.AreEqual("ccd143-native-page-1d6417d33f4582f4", fixture.Binding.NativeOperationId);
            Assert.AreEqual("ccd143-action-native-create-ccd143-native-page-1d6417d33f4582f4", fixture.Binding.ActionSignature.ActionId);
            Assert.IsFalse(Guid.TryParse(fixture.Binding.NativeOperationId, out _));
            Assert.IsNull(fixture.Operational.ImportReceipt);
            Assert.IsNull(fixture.Operational.JournalState);
            Assert.IsNull(fixture.Operational.RuntimeReceipt);
            Assert.IsFalse(fixture.Operational.AdmissionPassed); // No caller PASS has authority.
        }

        [TestMethod]
        public void EvaluatorPreservesContinuityAndConditionalOutcomeWithoutAwardingM5()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            var assessment = fixture.Evaluate();
            Assert.AreEqual(IngredientMaturityLevel.M4, assessment.AttainedMaturity, Failures(assessment));
            Assert.AreEqual(IngredientTechnicalStatus.Conditional, assessment.TechnicalOutcome.Status);
            Assert.AreEqual("pnp-ingredient-maturity-evaluator/v3", assessment.EvaluatorVersion);
            Assert.AreEqual("pnp-ingredient-maturity-assessment/v1", assessment.SchemaVersion);
            IngredientMaturityEvaluator.ValidateAssessment(assessment, fixture.Context,
                new IngredientMaturityContributorCatalog(new[] { fixture }));
            fixture.IncludeM1 = false;
            Assert.AreEqual(IngredientMaturityLevel.M0, fixture.Evaluate().AttainedMaturity);
            Assert.ThrowsException<InvalidDataException>(() => IngredientMaturityEvaluator.ValidateAssessment(
                assessment, fixture.Context, new IngredientMaturityContributorCatalog(new[] { fixture })));
        }

        [TestMethod]
        public void UnboundLegacyOverloadsAndMissingIndependentPinsCannotAdmitExternalEvidence()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Plan));
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Operational));
            fixture.Context.ExternalAdmission = null;
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Context, fixture.Plan));
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational));
        }

        [TestMethod]
        public void MixedExternalAndPublishingRepresentationsFailClosed()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.Plan.Plan = new PublishingPageMigrationPlan();
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Context, fixture.Plan));
            fixture.Operational.Plan = new PublishingPageMigrationPlan();
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational));
        }

        [DataTestMethod]
        [DataRow("claim")]
        [DataRow("ingredient")]
        [DataRow("source-version")]
        [DataRow("source-file")]
        [DataRow("snapshot")]
        [DataRow("target-profile")]
        [DataRow("target-path")]
        [DataRow("producer")]
        [DataRow("plan-schema")]
        [DataRow("plan-digest")]
        [DataRow("plan-producer")]
        [DataRow("operation")]
        [DataRow("action")]
        [DataRow("owner")]
        [DataRow("unknown-kind")]
        [DataRow("unknown-graph")]
        [DataRow("unknown-disposition")]
        [DataRow("unknown-external-state")]
        [DataRow("missing-policy")]
        [DataRow("missing-action")]
        [DataRow("duplicate-action")]
        [DataRow("illegal-dependency-release")]
        [DataRow("missing-dependency")]
        [DataRow("signature")]
        [DataRow("optional-runtime")]
        [DataRow("invented-dom-authority")]
        public void ResealedAnnexStillRequiresExactIndependentBindingAndPnPPolicy(string mutation)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            var binding = fixture.Binding;
            switch (mutation)
            {
                case "claim": binding.Identity.ClaimId = new string('a', 64); break;
                case "ingredient": binding.Identity.IngredientId += "-foreign"; break;
                case "source-version": binding.Source.SourceVersion = "etag:13"; break;
                case "source-file": binding.SourcePage.FileUniqueId = "11111111-1111-1111-1111-111111111111"; break;
                case "snapshot": binding.SourceSnapshotArtifact = fixture.Store.Json(new { foreign = true }); break;
                case "target-profile": binding.Target.TargetProfile = "foreign/v1"; break;
                case "target-path": binding.TargetPath += "-foreign"; break;
                case "producer": binding.Producer.ImplementationCommit = IngredientExternalEvidenceTestFixture.CurrentCandidate; break;
                case "plan-schema": binding.PlanSchema = "unsupported/v1"; break;
                case "plan-digest": binding.PlanDigest = new string('a', 64); break;
                case "plan-producer": binding.PlanProducerCommit = IngredientExternalEvidenceTestFixture.CurrentCandidate; break;
                case "operation": binding.ExternalPlanOperationId = "ccd109-apply-r00002"; break;
                case "action": binding.NativeActionId += "-foreign"; break;
                case "owner": binding.Graph.Nodes[0].PrimaryOwnerLane = "content.text"; break;
                case "unknown-kind": binding.Graph.Nodes[0].Kind = (PageIngredientKind)9999; break;
                case "unknown-graph": binding.Graph.SchemaVersion = "pnp-page-ingredient-graph/v999"; break;
                case "unknown-disposition": binding.Actions[0].Disposition = (IngredientDisposition)9999; break;
                case "unknown-external-state":
                    binding.Graph.ExternalReferences = new List<PageIngredientExternalReference>
                    {
                        new PageIngredientExternalReference
                        {
                            IngredientId = "shared:dependency", Kind = PageIngredientKind.Asset,
                            State = (PageExternalIngredientState)9999,
                            ExecutionGroupDigest = new string('a', 64), SupportCohortDigest = new string('b', 64)
                        }
                    };
                    break;
                case "missing-policy": binding.Actions[0].PolicyVersion = null; break;
                case "missing-action": binding.Actions.Clear(); break;
                case "duplicate-action": binding.Actions.Add(binding.Actions[0]); break;
                case "illegal-dependency-release": binding.Actions[0].ReleasedDependencyIngredientIds.Add("foreign"); break;
                case "missing-dependency":
                    binding.Graph.Edges.Add(new PageIngredientEdge { FromIngredientId = binding.Identity.IngredientId,
                        ToIngredientId = "missing", Relationship = PageIngredientRelationship.DependsOn, Requirement = PageIngredientRequirement.Required });
                    break;
                case "signature": binding.ActionSignature.ActionId += "-foreign"; break;
                case "optional-runtime": binding.RuntimeManifest.Requirements[0].Required = false; break;
                case "invented-dom-authority": binding.RuntimeManifest.Requirements[0].Kind = RuntimeVerificationRequirementKind.AuthoredDomEquality; break;
                default: Assert.Fail("Unknown mutation"); break;
            }
            fixture.ResealBinding(mutation != "signature");
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Context, fixture.Plan), mutation);
        }

        [TestMethod]
        public void HistoricalReceiptCannotBeRelabeledAsTheCurrentCandidate()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.Context.Producer.ImplementationCommit = IngredientExternalEvidenceTestFixture.CurrentCandidate;
            fixture.Binding.Producer.ImplementationCommit = IngredientExternalEvidenceTestFixture.CurrentCandidate;
            fixture.ResealBinding();
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational));
        }

        [DataTestMethod]
        [DataRow("native-create", "schema", "unsupported/v1")]
        [DataRow("native-create", "operationId", "foreign-operation")]
        [DataRow("native-create", "actionId", "foreign-action")]
        [DataRow("native-create", "pages/0/operationId", "foreign-page-operation")]
        [DataRow("native-create", "pages/0/actionId", "foreign-page-action")]
        [DataRow("native-create", "executionBinding/pnpCommit", "1111111111111111111111111111111111111111")]
        [DataRow("native-create", "executionBinding/targetProfile", "foreign/v1")]
        [DataRow("native-create", "executionBinding/consumerIssue", "foreign-consumer")]
        [DataRow("native-create", "executionBinding/sourceVersion/etag", "etag:13")]
        [DataRow("native-create", "pages/0/sourceVersion/etag", "etag:13")]
        [DataRow("native-create", "pages/0/claimId", "foreign-claim")]
        [DataRow("native-create", "planDigest", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [DataRow("native-create", "producerRef/sha256", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [DataRow("native-create", "targetMappingDigest", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [DataRow("native-create", "ownershipMarker", "[foreign]")]
        [DataRow("native-create", "pages/0/targetPath", "/foreign.aspx")]
        [DataRow("fresh-readback", "pages/0/identity/fileUniqueId", "11111111-1111-1111-1111-111111111111")]
        [DataRow("fresh-readback", "pages/0/identity/parentListId", "11111111-1111-1111-1111-111111111111")]
        [DataRow("fresh-readback", "pages/0/identity/fileStability/lastBody/ETag", "etag:stale")]
        [DataRow("fresh-readback", "pages/0/planDigest", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [DataRow("fresh-readback", "pages/0/lifecycleDigest", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [DataRow("fresh-readback", "pages/0/producerRef/sha256", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [DataRow("fresh-readback", "pages/0/identity/fileStability/samples/0/atUtc", "2026-09-10T10:17:06.673")]
        [DataRow("fresh-readback", "startedAtUtc", "2026-09-10T10:15:00Z")]
        [DataRow("fresh-readback", "pages/0/timestampUtc", "2026-09-10T10:16:30Z")]
        [DataRow("fresh-readback", "pages/0/runtime/timestampUtc", "2026-09-10T10:16:30Z")]
        [DataRow("fresh-readback", "pages/0/runtime/finalUrl", "https://foreign.invalid/page.aspx")]
        [DataRow("cleanup", "pages/0/observedFileUniqueId", "11111111-1111-1111-1111-111111111111")]
        [DataRow("cleanup", "pages/0/ownedParentListId", "11111111-1111-1111-1111-111111111111")]
        [DataRow("cleanup", "pages/0/deleteRequestGuid", "")]
        [DataRow("post-cleanup", "pages/0/timestampUtc", "2026-09-10T10:17:00Z")]
        public void ResealedLifecycleMutationFailsIdentityLineageAndFreshness(string phase, string path, string value)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.MutateReceipt(phase, root => Set(root, path, value));
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational), phase + "/" + path);
        }

        [DataTestMethod]
        [DataRow("semantic-denial")]
        [DataRow("http-denial")]
        [DataRow("server-error")]
        [DataRow("wrong-item")]
        [DataRow("ownership")]
        [DataRow("unbounded-retry")]
        [DataRow("incomplete-retry")]
        [DataRow("delete-failed")]
        [DataRow("still-present")]
        [DataRow("missing-runtime")]
        public void RuntimeCleanupAndRetryCannotBeOverriddenByCallerPass(string mutation)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            switch (mutation)
            {
                case "semantic-denial": fixture.MutateReceipt("fresh-readback", root => root["pages"][0]["runtime"]["containsAccessDenied"] = true); break;
                case "http-denial": fixture.MutateReceipt("fresh-readback", root => root["pages"][0]["runtime"]["status"] = 403); break;
                case "server-error": fixture.MutateReceipt("fresh-readback", root => root["pages"][0]["runtime"]["containsServerError"] = true); break;
                case "wrong-item": fixture.MutateReceipt("fresh-readback", root => root["pages"][0]["identity"]["itemId"] = 99); break;
                case "ownership": fixture.MutateReceipt("cleanup", root => root["ownership"]["valid"] = false); break;
                case "unbounded-retry": fixture.MutateReceipt("fresh-readback", root => root["pages"][0]["identity"]["fileStability"]["attempts"] = 999); break;
                case "incomplete-retry": fixture.MutateReceipt("fresh-readback", root => root["pages"][0]["identity"]["fileStability"]["samples"].AsArray().RemoveAt(0)); break;
                case "delete-failed": fixture.MutateReceipt("cleanup", root => root["pages"][0]["deleteStatus"] = 500); break;
                case "still-present": fixture.MutateReceipt("post-cleanup", root => root["pages"][0]["status"] = 200); break;
                case "missing-runtime": fixture.MutateReceipt("fresh-readback", root => root["pages"][0]["runtime"] = null); break;
                default: Assert.Fail("Unknown mutation"); break;
            }
            fixture.Operational.AdmissionPassed = true;
            fixture.Operational.CleanupPassed = true;
            fixture.Operational.RetryPassed = true;
            fixture.Operational.RuntimeRequired = false;
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational), mutation);
        }

        [DataTestMethod]
        [DataRow("missing-cleanup")]
        [DataRow("duplicate-receipt")]
        [DataRow("receipt-order")]
        [DataRow("unknown-manifest")]
        [DataRow("corrupt-plan")]
        [DataRow("missing-snapshot")]
        [DataRow("corrupt-readback")]
        [DataRow("no-store")]
        [DataRow("wrong-target")]
        [DataRow("stale-window")]
        public void ArtifactClosureAndConsumerPinsFailClosed(string mutation)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            switch (mutation)
            {
                case "missing-cleanup": fixture.ReceiptSet.Receipts.RemoveAt(6); fixture.ResealReceipts(); break;
                case "duplicate-receipt": fixture.ReceiptSet.Receipts[7] = fixture.ReceiptSet.Receipts[6]; fixture.ResealReceipts(); break;
                case "receipt-order":
                    var first = fixture.ReceiptSet.Receipts[0];
                    fixture.ReceiptSet.Receipts[0] = fixture.ReceiptSet.Receipts[1];
                    fixture.ReceiptSet.Receipts[1] = first;
                    fixture.ResealReceipts(); break;
                case "unknown-manifest": fixture.ReceiptSet.SchemaVersion = "unknown/v1"; fixture.ResealReceipts(); break;
                case "corrupt-plan": fixture.Store.Corrupt(fixture.Plan.External.PlanArtifact.Sha256); break;
                case "missing-snapshot": fixture.Store.Remove(fixture.Binding.SourceSnapshotArtifact.Sha256); break;
                case "corrupt-readback": fixture.Store.Corrupt(fixture.ReceiptSet.Receipts[5].Sha256); break;
                case "no-store": fixture.Plan.External.ArtifactStore = null; break;
                case "wrong-target": fixture.Context.ExternalAdmission.Target.SiteId = "11111111-1111-1111-1111-111111111111"; break;
                case "stale-window": fixture.Context.ExternalAdmission.ExecutionWindowStartUtc = IngredientExternalEvidenceTestFixture.Utc("2026-09-11T00:00:00Z"); break;
                default: Assert.Fail("Unknown mutation"); break;
            }
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational), mutation);
        }

        [TestMethod]
        public void InputDigestAndDuplicateJsonKeysCannotHideCorruption()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.MutateInput(root => root["pages"][0]["preferredPath"] = "/foreign.aspx");
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Context, fixture.Plan));
            fixture = new IngredientExternalEvidenceTestFixture();
            var bytes = Encoding.UTF8.GetBytes("{\"schema\":\"first\",\"schema\":\"second\"}");
            fixture.Plan.External.PlanArtifact = fixture.Store.Add(bytes);
            fixture.Context.ExternalAdmission.PlanArtifactSha256 = fixture.Plan.External.PlanArtifact.Sha256;
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Context, fixture.Plan));
        }

        [DataTestMethod]
        [DataRow(IngredientDisposition.Drop)]
        [DataRow(IngredientDisposition.Delegate)]
        [DataRow(IngredientDisposition.Defer)]
        public void NativeMutationCannotVerifyAnExcludedOrDeferredAction(IngredientDisposition disposition)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.Binding.Actions[0].Disposition = disposition;
            fixture.ResealBinding(true);
            AllPassed(IngredientMaturityEvidenceValidator.ValidateM3(fixture.Context, fixture.Plan));
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational));
        }

        [TestMethod]
        public void MissingPageAdmissionFenceCannotBeReplacedByAggregatePass()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.MutateReceipt("capability", root => root["pageAdmissions"].AsArray().Clear());
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational));
        }

        // CCD-565's twelve independently discovered counterexamples. Re-admit
        // the mutated bytes in the test-only fixture so a raw hash check cannot
        // conceal an incomplete primitive-evidence validator.
        [DataTestMethod]
        [DataRow("web-create-http403", "provision", "webs/0/create/status", "403")]
        [DataRow("web-create-http200-csom-error", "provision", "webs/0/create/errorInfo",
            "{\"ErrorCode\":-2147024891,\"ErrorMessage\":\"Access denied\",\"ErrorTypeName\":\"System.UnauthorizedAccessException\"}")]
        [DataRow("web-create-missing-operation-result", "provision", "webs/0/create", "null")]
        [DataRow("web-create-preexisting-target", "provision", "webs/0/preStatus", "200")]
        [DataRow("web-create-out-of-phase-time", "provision", "webs/0/timestampUtc", "\"2026-09-11T00:00:00Z\"")]
        [DataRow("web-readiness-http403", "readiness", "webs/0/samples/0/status", "403")]
        [DataRow("web-readiness-missing-samples", "readiness", "webs/0/samples", "[]")]
        [DataRow("web-readiness-unbounded-attempts", "readiness", "webs/0/attempts", "999")]
        [DataRow("web-readiness-semantic-error", "readiness", "webs/0/samples/0/errorInfo",
            "{\"ErrorCode\":-2147024891,\"ErrorMessage\":\"Access denied\"}")]
        [DataRow("web-readiness-foreign-sample-identity", "readiness", "webs/0/samples/0/web/webId",
            "\"11111111-1111-1111-1111-111111111111\"")]
        [DataRow("capability-library-foreign-list", "capability", "libraries/0/listId",
            "\"11111111-1111-1111-1111-111111111111\"")]
        [DataRow("capability-library-http403", "capability", "libraries/0/readStatus", "403")]
        public void LifecycleDependencyPrimitivesCannotBeReplacedBySummaryPass(
            string counterexample, string phase, string path, string valueJson)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.MutateReceipt(phase, root => SetJson(root, path, JsonNode.Parse(valueJson)));
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational), counterexample);
            Assert.AreEqual(IngredientMaturityLevel.M3, fixture.Evaluate().AttainedMaturity, counterexample);
            // Denial/missing dependency evidence is local to the assessed
            // instance, not an exception that stops independent ingredients.
            Assert.AreEqual(IngredientMaturityLevel.M4, new IngredientExternalEvidenceTestFixture().Evaluate().AttainedMaturity);
        }

        [DataTestMethod]
        [DataRow("provision", "webs/0/parentPath", "\"/foreign-parent\"")]
        [DataRow("provision", "webs/0/create", "{\"status\":200,\"requestGuid\":\"test-only\"}")]
        [DataRow("readiness", "webs/0/ownedObjectIdentity", "\"foreign-accepted-object\"")]
        [DataRow("readiness", "webs/0/timeoutMs", "999")]
        [DataRow("readiness", "webs/0/elapsedMs", "999999")]
        [DataRow("readiness", "webs/0/startedAtUtc", "\"2026-09-10T10:16:00Z\"")]
        [DataRow("readiness", "webs/0/samples/0/attempt", "2")]
        [DataRow("readiness", "webs/0/samples/0/atUtc", "\"2026-09-10T10:16:00Z\"")]
        [DataRow("readiness", "webs/0/samples/0/requestGuid", "\"\"")]
        [DataRow("readiness", "webs/0/samples/0/web/url", "\"https://foreign.invalid/web\"")]
        [DataRow("readiness", "webs/0/samples/0/web/description", "\"foreign-owner\"")]
        [DataRow("readiness", "webs/0/samples/0/identityMatches", "false")]
        [DataRow("readiness", "webs/0/samples/0/ownershipMatches", "false")]
        [DataRow("readiness", "webs/0/samples/0/ready", "false")]
        [DataRow("readiness", "webs/0/identity/objectIdentity", "\"foreign-terminal-object\"")]
        [DataRow("capability", "libraries", "[]")]
        [DataRow("capability", "libraries/0/webPath", "\"/foreign-web\"")]
        [DataRow("capability", "libraries/0/rootPath", "\"/foreign-list\"")]
        [DataRow("capability", "libraries/0/preStatus", "403")]
        [DataRow("capability", "libraries/0/preStatus", "404")]
        [DataRow("native-create", "pages/0/nativeCreate/status", "403")]
        public void NativeDependencyProtocolMetadataFailsClosed(string phase, string path, string valueJson)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.MutateReceipt(phase, root => SetJson(root, path, JsonNode.Parse(valueJson)));
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational), phase + "/" + path);
        }

        [DataTestMethod]
        [DataRow("single-success")]
        [DataRow("accepted-create-incomplete")]
        [DataRow("incomplete-then-ready")]
        [DataRow("request-exception-then-ready")]
        [DataRow("http-unavailable-then-ready")]
        public void NativeWebPollingRetainsItsFirstSuccessProtocol(string observation)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            if (observation == "accepted-create-incomplete")
                fixture.MutateReceipt("provision", root => root["webs"][0]["identity"]["isProvisioningComplete"] = false);
            else if (observation != "single-success")
                AddNonReadyWebAttempt(fixture, observation);
            AllPassed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational));
        }

        [DataTestMethod]
        [DataRow("denied-before-success")]
        [DataRow("semantic-error-before-success")]
        [DataRow("success-before-terminal")]
        [DataRow("mixed-exception-observation")]
        [DataRow("http-exception-observation")]
        [DataRow("unknown-request-exception")]
        [DataRow("foreign-operation-pair")]
        [DataRow("duplicate-library")]
        public void LaterSuccessCannotEraseContradictoryDependencyEvidence(string mutation)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            if (mutation == "foreign-operation-pair")
            {
                fixture.MutateReceipt("provision", root => root["webs"][0]["operationId"] = "foreign-pair");
                fixture.MutateReceipt("readiness", root => root["webs"][0]["operationId"] = "foreign-pair");
            }
            else if (mutation == "duplicate-library")
                fixture.MutateReceipt("capability", root => root["libraries"].AsArray().Add(root["libraries"][0].DeepClone()));
            else
            {
                AddNonReadyWebAttempt(fixture, mutation == "unknown-request-exception"
                    ? "request-exception-then-ready" : "incomplete-then-ready");
                fixture.MutateReceipt("readiness", root =>
                {
                    var sample = root["webs"][0]["samples"][0];
                    if (mutation == "denied-before-success") sample["status"] = 403;
                    else if (mutation == "semantic-error-before-success")
                        sample["errorInfo"] = JsonNode.Parse("{\"ErrorCode\":-2147024891,\"ErrorMessage\":\"Access denied\"}");
                    else if (mutation == "mixed-exception-observation")
                    { sample["status"] = null; sample["error"] = "TimeoutError"; }
                    else if (mutation == "http-exception-observation") sample["error"] = "TypeError";
                    else if (mutation == "unknown-request-exception") sample["error"] = "UnauthorizedAccessException";
                    else { sample["ready"] = true; sample["web"]["isProvisioningComplete"] = true; }
                });
            }
            AnyFailed(IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational), mutation);
        }

        [DataTestMethod]
        [DataRow("valid", true)]
        [DataRow("foreign-list", false)]
        [DataRow("missing-readiness", false)]
        [DataRow("unfenced-create", false)]
        public void CapabilityListCreationRequiresItsOwnBoundedIdentityEvidence(string mutation, bool expectedPass)
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            fixture.MutateReceipt("capability", root =>
            {
                var library = root["libraries"][0];
                library["preStatus"] = mutation == "unfenced-create" ? 200 : 404;
                library["create"] = new JsonObject { ["status"] = 201, ["requestGuid"] = "test-only-list-create" };
                if (mutation == "missing-readiness") return;
                library["readiness"] = new JsonObject
                {
                    ["state"] = "ready", ["attempts"] = 3, ["timeoutMs"] = 15000,
                    ["startedAtUtc"] = "2026-09-10T10:16:41.200Z", ["finishedAtUtc"] = "2026-09-10T10:16:43.000Z",
                    ["samples"] = new JsonArray(
                        new JsonObject { ["attempt"] = 1, ["atUtc"] = "2026-09-10T10:16:41.500Z", ["status"] = 200, ["requestGuid"] = "test-only-1" },
                        new JsonObject { ["attempt"] = 2, ["atUtc"] = "2026-09-10T10:16:42.250Z", ["status"] = 200, ["requestGuid"] = "test-only-2" },
                        new JsonObject { ["attempt"] = 3, ["atUtc"] = "2026-09-10T10:16:43.000Z", ["status"] = 200, ["requestGuid"] = "test-only-3" }),
                    ["lastBody"] = new JsonObject
                    {
                        ["Id"] = mutation == "foreign-list" ? "11111111-1111-1111-1111-111111111111" : fixture.Context.ExternalAdmission.Target.ListId,
                        ["RootFolder"] = new JsonObject { ["ServerRelativeUrl"] = fixture.Binding.TargetListPath }
                    }
                };
            });
            var receipts = IngredientMaturityEvidenceValidator.ValidateM4(fixture.Context, fixture.Operational);
            if (expectedPass) AllPassed(receipts);
            else AnyFailed(receipts, mutation);
        }

        private static void AddNonReadyWebAttempt(IngredientExternalEvidenceTestFixture fixture, string observation)
        {
            fixture.MutateReceipt("readiness", root =>
            {
                root["startedAtUtc"] = "2026-09-10T10:16:34.220Z";
                root["finishedAtUtc"] = "2026-09-10T10:16:39.501Z";
                var web = root["webs"][0];
                web["startedAtUtc"] = "2026-09-10T10:16:34.220Z";
                web["finishedAtUtc"] = "2026-09-10T10:16:39.500Z";
                web["elapsedMs"] = 5280;
                web["attempts"] = 2;
                var samples = web["samples"].AsArray();
                var first = samples[0].DeepClone();
                if (observation == "request-exception-then-ready")
                    first = new JsonObject { ["status"] = null, ["error"] = "TimeoutError" };
                else if (observation == "http-unavailable-then-ready")
                {
                    first["status"] = 503;
                    first["identityMatches"] = false;
                    first["ownershipMatches"] = false;
                    foreach (var field in first["web"].AsObject().Select(value => value.Key).ToArray()) first["web"][field] = null;
                }
                else first["web"]["isProvisioningComplete"] = false;
                first["attempt"] = 1;
                first["atUtc"] = "2026-09-10T10:16:34.221Z";
                first["ready"] = false;
                samples[0]["attempt"] = 2;
                samples[0]["atUtc"] = "2026-09-10T10:16:39.500Z";
                samples.Insert(0, first);
            });
        }

        [TestMethod]
        public void DeniedInstanceFailsLocallyAndIndependentInstanceContinues()
        {
            var denied = new IngredientExternalEvidenceTestFixture();
            denied.MutateReceipt("fresh-readback", root => root["pages"][0]["runtime"]["containsAccessDenied"] = true);
            Assert.AreEqual(IngredientMaturityLevel.M3, denied.Evaluate().AttainedMaturity);
            Assert.AreEqual(IngredientMaturityLevel.M4, new IngredientExternalEvidenceTestFixture().Evaluate().AttainedMaturity);
        }

        [TestMethod]
        public void PriorEvaluatorAndValidatorSummariesRequireRegeneration()
        {
            var fixture = new IngredientExternalEvidenceTestFixture();
            var assessment = fixture.Evaluate();
            assessment.EvaluatorVersion = "pnp-ingredient-maturity-evaluator/v2";
            assessment.AssessmentDigest = IngredientMaturityEvaluator.ComputeDigest(assessment);
            Assert.ThrowsException<InvalidDataException>(() => IngredientMaturityEvaluator.ValidateAssessment(assessment));
            assessment = fixture.Evaluate();
            assessment.Levels[0].Gates[0].ValidatorVersion = "v2";
            assessment.AssessmentDigest = IngredientMaturityEvaluator.ComputeDigest(assessment);
            Assert.ThrowsException<InvalidDataException>(() => IngredientMaturityEvaluator.ValidateAssessment(assessment));
        }

        private static void Set(JsonNode root, string path, string value) => SetJson(root, path, JsonValue.Create(value));

        private static void SetJson(JsonNode root, string path, JsonNode value)
        {
            var parts = path.Split('/');
            foreach (var part in parts.Take(parts.Length - 1))
                root = int.TryParse(part, out var index) ? root[index] : root[part];
            root[parts.Last()] = value;
        }

        private static void AllPassed(IEnumerable<IngredientMaturityGateReceipt> receipts)
        {
            Assert.IsTrue(receipts.All(value => value.Passed),
                string.Join("\n", receipts.Where(value => !value.Passed).Select(value => value.GateId + ": " + value.FailureReason)));
        }

        private static void AnyFailed(IEnumerable<IngredientMaturityGateReceipt> receipts, string message = null)
        {
            Assert.IsTrue(receipts.Any(value => !value.Passed), message ?? "Untrusted evidence awarded maturity.");
        }

        private static string Failures(IngredientMaturityAssessment assessment) =>
            string.Join("\n", assessment.Levels.SelectMany(level => level.Gates).Where(gate => gate.Status != IngredientMaturityGateStatus.Passed)
                .Select(gate => gate.GateId + ": " + gate.FailureReason));
    }
}
