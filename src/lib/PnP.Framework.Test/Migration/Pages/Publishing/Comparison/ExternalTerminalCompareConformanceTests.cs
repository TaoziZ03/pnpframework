using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Assessment.Maturity.External;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Test.Migration.Pages.Assessment;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PnP.Framework.Test.Migration.Pages.Publishing.Comparison
{
    [TestClass]
    public class ExternalTerminalCompareConformanceTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void OriginalConsumerBytesProduceCanonicalFailureWithoutRelabelingOrMaturity()
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            // Optional artifact-generation clock supplied by the operator.
            // The default UT remains deterministic and independent of time.
            if (TestContext.Properties.Contains("ExternalReportGeneratedAtUtc"))
                fixture.Request.GeneratedAtUtc = DateTimeOffset.Parse((string)TestContext.Properties["ExternalReportGeneratedAtUtc"]);
            var report = fixture.Reconcile();
            Assert.AreEqual("pnp-page-compare-report/v3", report.SchemaVersion);
            Assert.AreEqual("fail", report.Acceptance.Verdict);
            Assert.AreEqual("failed", report.Runtime.Status);
            Assert.AreEqual("BROWSER_RUNTIME_FAIL", report.ExternalEvidence.BrowserVerdict);
            Assert.AreEqual("modern-sharepoint-shell", report.ExternalEvidence.BrowserSurfaceClassification);
            CollectionAssert.AreEquivalent(new[] { "ASPNET_FORM_MISSING", "CLASSIC_SHAREPOINT_SURFACE_MISSING", "WIKI_RUNTIME_SIGNAL_MISSING" },
                report.ExternalEvidence.BrowserFailedGates.ToArray());
            Assert.AreEqual("unavailable", report.ExternalEvidence.NativeCleanupReceiptStatus);
            Assert.IsNull(report.ExternalEvidence.NativeCleanupReceiptDigestSha256);
            Assert.AreEqual("FRESH_POST_CLEANUP_ABSENCE_PASS", report.ExternalEvidence.PostCleanupStatus);
            Assert.AreEqual("verified", report.ExternalEvidence.StorageIdentityStatus);
            Assert.AreEqual("not-run", report.Storage.Status); // Identity is not domain value fidelity.
            Assert.AreEqual("admitted-digest-reference-only", report.ExternalEvidence.PlanEvidenceStatus);
            Assert.AreEqual("ccd.shared-target-lifecycle-receipt/v2", report.Bindings.ImportReceiptSchemaVersion);
            Assert.AreEqual("ccd.browser-runtime-review/v1", report.Bindings.RuntimeReceiptSchemaVersion);
            Assert.AreEqual(fixture.Admission.Binding.PlanDigest, report.Bindings.PlanDigestSha256);
            Assert.AreEqual(fixture.Admission.FreshReadbackSha256, report.Bindings.ImportReceiptDigestSha256);
            Assert.AreEqual(fixture.Admission.BrowserReviewSha256, report.Bindings.RuntimeReceiptDigestSha256);
            Assert.AreEqual(fixture.Admission.Binding.NativeOperationId, report.ExternalEvidence.NativeOperationId);
            Assert.AreEqual(fixture.Admission.Binding.SourcePage.ETag, report.ExternalEvidence.SourceETag);
            Assert.AreEqual(fixture.Admission.Target.ListId, report.ExternalEvidence.TargetListId);
            Assert.AreEqual(fixture.Admission.Binding.Identity.IngredientId, report.Ingredients.Single().IngredientId);
            Assert.AreEqual(PublishingPageCompareReconciler.ComputeReportDigest(report), report.ReportDigestSha256);
            PublishingPageCompareReconciler.ValidateExternalTerminalReport(report, fixture.Request, fixture.Admission, fixture.Store);
            Assert.IsNull(report.Ingredients.Single().RuntimeRequirementIds);
            CollectionAssert.Contains(report.Acceptance.ReasonCodes.ToArray(), "NATIVE_CLEANUP_RECEIPT_UNAVAILABLE");
            var json = MigrationContractSerializer.SerializeCanonical(report);
            Assert.IsFalse(json.Contains("attainedMaturity"));
            Assert.IsFalse(json.Contains("pnp-publishing-page-import-receipt"));
            Assert.IsFalse(json.Contains("pnp-migration-runtime-verification-receipt"));
            var path = Path.Combine(TestContext.TestRunResultsDirectory, "ccd147-external-terminal-compare-v3.json");
            File.WriteAllText(path, json, new UTF8Encoding(false));
            TestContext.AddResultFile(path);
        }

        [DataTestMethod]
        [DataRow("Manifest", "ffb1298445fce2d95d9ec2eae52bccba61e2832d0a51222cde42590ccd15d3b8", 3989)]
        [DataRow("FreshReadback", "8d604a593164aa84e3cc0aba19311404573c5c524875aa14aa228ae8f471e5e4", 11162)]
        [DataRow("BrowserReview", "d8bd73c570e9df2b1dd4b48b8e05e4520e39ae3c6c43f710ff0c187a9b6539c9", 6494)]
        [DataRow("CleanupUnavailable", "809b6360f2533de68003bf9ca976bac6e34029c74b4c5245d25e8fa54c18ca6c", 1496)]
        [DataRow("PostCleanup", "bca338a1c1049b900fb3183c66f031965601d421bf33f265b298998db0925147", 3468)]
        public void FixtureMembersAreExactHistoricalBytes(string name, string sha256, int length)
        {
            var bytes = ExternalTerminalCompareTestFixture.OriginalBytes(name);
            Assert.AreEqual(sha256, MigrationDigest.ComputeSha256(bytes));
            Assert.AreEqual(length, bytes.Length);
        }

        [DataTestMethod]
        [DataRow("Manifest")]
        [DataRow("FreshReadback")]
        [DataRow("BrowserReview")]
        [DataRow("CleanupUnavailable")]
        [DataRow("PostCleanup")]
        public void EveryOriginalArtifactRequiresPresentUnchangedBytesAndIndependentDigest(string name)
        {
            foreach (var mode in new[] { "missing", "corrupt", "length", "unavailable", "wrong-pin", "wrong-schema", "duplicate-property", "invalid-json" })
            {
                var fixture = new ExternalTerminalCompareTestFixture();
                var artifact = fixture.Artifact(name);
                switch (mode)
                {
                    case "missing": fixture.Store.Remove(artifact.Sha256); break;
                    case "corrupt": fixture.Store.Corrupt(artifact.Sha256); break;
                    case "length": artifact.Length++; break;
                    case "unavailable": artifact.Availability = PnP.Framework.Migration.Evidence.EvidenceAvailability.Unavailable; break;
                    case "wrong-pin": fixture.Pin(name, new string('a', 64)); break;
                    case "wrong-schema": fixture.Mutate(name, value => value["schema"] = "foreign/v1"); break;
                    case "invalid-json": fixture.Replace(name, Encoding.UTF8.GetBytes("not-json")); break;
                    case "duplicate-property":
                        var original = Encoding.UTF8.GetString(fixture.Store.Bytes(artifact.Sha256));
                        fixture.Replace(name, Encoding.UTF8.GetBytes(original.Insert(original.IndexOf('{') + 1, "\"schema\":\"foreign/v1\",")));
                        break;
                }
                Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), name + ": " + mode);
            }
        }

        [DataTestMethod]
        [DataRow("claim")]
        [DataRow("ingredient")]
        [DataRow("kind")]
        [DataRow("subtype")]
        [DataRow("source-list")]
        [DataRow("source-item")]
        [DataRow("source-file")]
        [DataRow("source-version")]
        [DataRow("source-url")]
        [DataRow("plan")]
        [DataRow("producer")]
        [DataRow("profile")]
        [DataRow("run")]
        [DataRow("consumer")]
        [DataRow("manifest-time")]
        public void ResealedManifestCannotChangeTheIndependentConsumerBinding(string mutation)
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            fixture.Mutate("Manifest", root =>
            {
                var execution = root["execution"];
                switch (mutation)
                {
                    case "claim": execution["claimId"] = new string('a', 64); break;
                    case "ingredient": execution["ingredient"]["id"] = "foreign"; break;
                    case "kind": execution["ingredient"]["kind"] = "Asset"; break;
                    case "subtype": execution["ingredient"]["subtype"] = "foreign"; break;
                    case "source-list": execution["source"]["listId"] = ExternalTerminalCompareTestFixture.OtherGuid; break;
                    case "source-item": execution["source"]["itemId"] = 2; break;
                    case "source-file": execution["source"]["fileUniqueId"] = ExternalTerminalCompareTestFixture.OtherGuid; break;
                    case "source-version": execution["source"]["etag"] = "changed"; break;
                    case "source-url": execution["source"]["pageUrl"] = "https://source.example/foreign.aspx"; break;
                    case "plan": execution["admittedPlanDigest"] = new string('a', 64); break;
                    case "producer": execution["pnpImplementation"]["commit"] = new string('a', 40); break;
                    case "profile": execution["targetProfile"] = "foreign/v1"; break;
                    case "run": root["runId"] = ExternalTerminalCompareTestFixture.OtherGuid; break;
                    case "consumer": execution["consumerIssue"] = "foreign"; break;
                    case "manifest-time": root["generatedAtUtc"] = "2026-09-11T20:09:00Z"; break;
                }
            });
            Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), mutation);
        }

        [DataTestMethod]
        [DataRow("FreshReadback")]
        [DataRow("PostCleanup")]
        public void LifecycleHeadersReuseTheSharedBindingRejections(string phase)
        {
            foreach (var field in new[] { "runId", "operationId", "actionId", "planDigest", "lifecycleDigest", "targetMappingDigest", "targetOrigin", "ownershipMarker" })
            {
                var fixture = new ExternalTerminalCompareTestFixture();
                fixture.Mutate(phase, value => value[field] = "foreign");
                Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), phase + ": " + field);
            }
            foreach (var field in new[] { "claimId", "operationId", "actionId", "targetPath" })
            {
                var fixture = new ExternalTerminalCompareTestFixture();
                fixture.Mutate(phase, value => value["pages"][0][field] = "foreign");
                Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), phase + ": page " + field);
            }
        }

        [DataTestMethod]
        [DataRow("file")]
        [DataRow("list")]
        [DataRow("item")]
        [DataRow("etag")]
        [DataRow("poll-time")]
        [DataRow("poll-state")]
        [DataRow("poll-count")]
        [DataRow("poll-id")]
        [DataRow("poll-denied")]
        [DataRow("before-poll")]
        [DataRow("stale-phase")]
        [DataRow("missing-request")]
        public void FreshIdentityCannotBeForgedWithAResealedSummary(string mutation)
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            fixture.Mutate("FreshReadback", root =>
            {
                var page = root["pages"][0];
                var identity = page["identity"];
                switch (mutation)
                {
                    case "file": identity["fileUniqueId"] = ExternalTerminalCompareTestFixture.OtherGuid; break;
                    case "list": identity["parentListId"] = ExternalTerminalCompareTestFixture.OtherGuid; break;
                    case "item": identity["itemId"] = 42; break;
                    case "etag": identity["fileStability"]["lastBody"]["ETag"] = "changed"; break;
                    case "poll-time": identity["fileStability"]["startedAtUtc"] = "2026-09-10T19:46:29Z"; break;
                    case "poll-state": identity["itemStability"]["state"] = "pending"; break;
                    case "poll-count": identity["itemStability"]["attempts"] = 99; break;
                    case "poll-id": identity["itemStability"]["samples"][0]["objectId"] = 42; break;
                    case "poll-denied": identity["fileStability"]["samples"][0]["status"] = 403; break;
                    case "before-poll": page["timestampUtc"] = "2026-09-11T19:46:26.282Z"; break;
                    case "stale-phase": root["startedAtUtc"] = "2026-09-10T19:46:26.281Z"; break;
                    case "missing-request": identity["requestGuid"] = null; break;
                }
            });
            Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), mutation);
        }

        [DataTestMethod]
        [DataRow("claim")]
        [DataRow("operation")]
        [DataRow("plan")]
        [DataRow("target")]
        [DataRow("file")]
        [DataRow("run")]
        [DataRow("stale")]
        [DataRow("future")]
        [DataRow("cache")]
        [DataRow("digest")]
        [DataRow("self-pass")]
        [DataRow("missing-gates")]
        [DataRow("weak-policy")]
        [DataRow("redirect")]
        [DataRow("access-denied")]
        public void BrowserEvidenceIsBoundAndAdverseSignalsCannotBecomePass(string mutation)
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            fixture.Mutate("BrowserReview", root =>
            {
                switch (mutation)
                {
                    case "claim": root["frozenBinding"]["claimId"] = new string('a', 64); break;
                    case "operation": root["frozenBinding"]["operationId"] = "foreign"; break;
                    case "plan": root["frozenBinding"]["planDigest"] = new string('a', 64); break;
                    case "target": root["observation"]["location"]["href"] = "https://target.example/other.aspx"; break;
                    case "file": root["frozenBinding"]["targetFileUniqueId"] = ExternalTerminalCompareTestFixture.OtherGuid; break;
                    case "run": root["reviewRunId"] = fixture.Admission.Binding.RunId; break;
                    case "stale": root["observation"]["observedAtUtc"] = "2026-09-11T19:40:00Z"; break;
                    case "future": root["observedAtUtc"] = "2026-09-12T19:54:34Z"; break;
                    case "cache": root["browser"]["cacheDisabled"] = false; break;
                    case "digest": root["observationSha256"] = new string('a', 64); break;
                    case "self-pass": root["verdict"] = "BROWSER_RUNTIME_PASS"; break;
                    case "missing-gates": root["failedGates"] = new JsonArray(); break;
                    case "weak-policy": root["gates"]["requireClassicSurface"] = false; break;
                    case "redirect": root["observation"]["sameOriginRequest"]["redirected"] = true; break;
                    case "access-denied": root["observation"]["semantic"]["accessDenied"] = true; break;
                }
                if (mutation != "digest") ExternalTerminalCompareTestFixture.SealObservation(root);
            });
            Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), mutation);
        }

        [DataTestMethod]
        [DataRow("claim")]
        [DataRow("plan")]
        [DataRow("run")]
        [DataRow("wrong-post-receipt")]
        [DataRow("native-receipt-present")]
        [DataRow("m4-pass")]
        [DataRow("future")]
        public void CleanupTransportObservationCannotManufactureNativeReceipt(string mutation)
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            fixture.Mutate("CleanupUnavailable", root =>
            {
                switch (mutation)
                {
                    case "claim": root["claimId"] = new string('a', 64); break;
                    case "plan": root["planDigest"] = new string('a', 64); break;
                    case "run": root["runId"] = ExternalTerminalCompareTestFixture.OtherGuid; break;
                    case "wrong-post-receipt": root["freshPostCleanupReceiptSha256"] = new string('a', 64); break;
                    case "native-receipt-present": root["nativeReceiptPersisted"] = true; break;
                    case "m4-pass": root["m4Disposition"] = "pass"; break;
                    case "future": root["recordedAtUtc"] = "2026-09-12T20:09:33Z"; break;
                }
            });
            Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), mutation);
        }

        [DataTestMethod]
        [DataRow("page")]
        [DataRow("site")]
        [DataRow("marker")]
        [DataRow("foreign-site")]
        [DataRow("foreign-marker")]
        [DataRow("before-runtime")]
        public void PostCleanupAbsenceRequiresTheExactBoundTarget(string mutation)
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            fixture.Mutate("PostCleanup", root =>
            {
                switch (mutation)
                {
                    case "page": root["pages"][0]["status"] = 200; break;
                    case "site": root["sites"][0]["status"] = 200; break;
                    case "marker": root["ownership"]["status"] = 200; break;
                    case "foreign-site": root["sites"][0]["path"] = "/sites/foreign"; break;
                    case "foreign-marker": root["ownership"]["path"] = "/SiteAssets/foreign.json"; break;
                    case "before-runtime": root["startedAtUtc"] = "2026-09-11T19:50:00Z"; break;
                }
            });
            fixture.Mutate("CleanupUnavailable", value => value["freshPostCleanupReceiptSha256"] = fixture.Admission.PostCleanupSha256);
            Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), mutation);
        }

        [TestMethod]
        public void PositiveBrowserControlStillCannotAwardPassWithoutNativeCleanupOrDomainComparison()
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            fixture.Mutate("BrowserReview", root =>
            {
                root["observation"]["aspNetFormSurface"] = true;
                root["observation"]["classicSurface"] = true;
                root["observation"]["wikiRuntimeSignal"] = true;
                root["observation"]["surfaceClassification"] = "synthetic-positive-control";
                root["failedGates"] = new JsonArray();
                root["verdict"] = "BROWSER_RUNTIME_PASS";
                ExternalTerminalCompareTestFixture.SealObservation(root);
            });
            var report = fixture.Reconcile();
            Assert.AreEqual("passed", report.Runtime.Status);
            Assert.AreEqual("conditional", report.Acceptance.Verdict);
            Assert.AreEqual("unavailable", report.ExternalEvidence.NativeCleanupReceiptStatus);
        }

        [TestMethod]
        public void DomainPayloadAndSelfAuthoredPassDoNotBecomeSharedFidelityAuthority()
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            fixture.Mutate("FreshReadback", root =>
            {
                root["pages"][0]["layoutEvidence"] = null;
                root["pages"][0]["ingredientAssessment"] = new JsonObject { ["verdict"] = "pass", ["attainedMaturity"] = "M5" };
            });
            var report = fixture.Reconcile();
            Assert.AreEqual("not-run", report.Storage.Status);
            Assert.AreEqual("fail", report.Acceptance.Verdict);
            Assert.IsFalse(MigrationContractSerializer.SerializeCanonical(report).Contains("M5"));
        }

        [TestMethod]
        public void NativeAndExternalModesCannotBeMixedAndLegacySchemasDoNotChange()
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            Assert.ThrowsException<InvalidDataException>(() => PublishingPageCompareReconciler.Reconcile(fixture.Request, fixture.Store));
            fixture.Request.ImportReceipt = new PublishingPageImportReceipt();
            Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile());
            Assert.AreEqual("pnp-page-compare-report/v1", PublishingPageCompareContract.SchemaVersion);
            Assert.AreEqual("pnp-page-compare-report/v2", PublishingPageCompareContract.IngredientContributionSchemaVersion);
            Assert.IsFalse(MigrationContractSerializer.SerializeCanonical(new PublishingPageCompareReport()).Contains("externalEvidence"));
            var external = new ExternalTerminalCompareTestFixture().Reconcile();
            external.SchemaVersion = PublishingPageCompareContract.SchemaVersion;
            Assert.ThrowsException<InvalidDataException>(() => PublishingPageCompareReconciler.ComputeReportDigest(external));
        }

        [TestMethod]
        public void MissingAdmissionIdentityAndMalformedWindowsFailClosed()
        {
            foreach (var mutation in new[] { "missing", "role", "subtype", "kind", "empty-window", "offset", "future-window", "null-policy" })
            {
                var fixture = new ExternalTerminalCompareTestFixture();
                switch (mutation)
                {
                    case "missing": fixture.Admission.SchemaVersion = "foreign"; break;
                    case "role": fixture.Admission.Binding.Identity.SemanticRole = null; break;
                    case "subtype": fixture.Admission.Binding.Identity.Subtype = null; break;
                    case "kind": fixture.Admission.Binding.Identity.Kind = (PageIngredientKind)999; break;
                    case "empty-window": fixture.Admission.ExecutionWindowStartUtc = default; break;
                    case "offset": fixture.Admission.ExecutionWindowStartUtc = fixture.Admission.ExecutionWindowStartUtc.ToOffset(TimeSpan.FromHours(1)); break;
                    case "future-window": fixture.Admission.ExecutionWindowEndUtc = fixture.Request.GeneratedAtUtc.AddHours(1); break;
                    case "null-policy": fixture.Admission.BrowserPolicy = null; break;
                }
                Assert.ThrowsException<InvalidDataException>(() => fixture.Reconcile(), mutation);
            }
        }

        [TestMethod]
        public void ReportDigestSealsTimesFailuresAndCleanupAndDoesNotAliasTheRequest()
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            var report = fixture.Reconcile();
            var original = report.ReportDigestSha256;
            Assert.AreEqual(original, fixture.Reconcile().ReportDigestSha256);
            fixture.Request.Producer.Id = "mutated";
            fixture.Admission.Binding.Identity.ClaimId = new string('a', 64);
            Assert.AreEqual(original, PublishingPageCompareReconciler.ComputeReportDigest(report));
            report.GeneratedAtUtc = report.GeneratedAtUtc.AddSeconds(1);
            Assert.AreNotEqual(original, PublishingPageCompareReconciler.ComputeReportDigest(report));
            report.GeneratedAtUtc = report.GeneratedAtUtc.AddSeconds(-1);
            report.ExternalEvidence.NativeCleanupReceiptStatus = "passed";
            Assert.AreNotEqual(original, PublishingPageCompareReconciler.ComputeReportDigest(report));
        }

        [DataTestMethod]
        [DataRow("pass")]
        [DataRow("runtime")]
        [DataRow("cleanup")]
        [DataRow("domain")]
        [DataRow("schema")]
        [DataRow("plan")]
        [DataRow("source")]
        [DataRow("time")]
        public void AResealedReportStillRequiresContextBoundReconciliation(string mutation)
        {
            var fixture = new ExternalTerminalCompareTestFixture();
            var report = fixture.Reconcile();
            switch (mutation)
            {
                case "pass": report.Acceptance.Verdict = "pass"; break;
                case "runtime": report.Runtime.Status = "passed"; break;
                case "cleanup": report.ExternalEvidence.NativeCleanupReceiptStatus = "passed"; break;
                case "domain": report.Storage.Status = "passed"; break;
                case "schema": report.Bindings.ImportReceiptSchemaVersion = "pnp-publishing-page-import-receipt/v5"; break;
                case "plan": report.Bindings.PlanDigestSha256 = new string('a', 64); break;
                case "source": report.ExternalEvidence.SourceETag = "changed"; break;
                case "time": report.ExternalEvidence.BrowserObservedAtUtc = report.ExternalEvidence.BrowserObservedAtUtc.AddHours(-1); break;
            }
            report.ReportDigestSha256 = PublishingPageCompareReconciler.ComputeReportDigest(report);
            Assert.ThrowsException<InvalidDataException>(() => PublishingPageCompareReconciler.ValidateExternalTerminalReport(
                report, fixture.Request, fixture.Admission, fixture.Store), mutation);
        }
    }

    // Consumer pins are explicit, not extracted from the bytes being checked.
    // Historical live evidence belongs to 8de62c9d. A fresh author test is NOT a
    // rerun of the tenant operation under the new Compare implementation.
    internal sealed class ExternalTerminalCompareTestFixture
    {
        public const string OtherGuid = "11111111-1111-1111-1111-111111111111";
        private static readonly ResourceManager Resources = new ResourceManager(
            "PnP.Framework.Test.Migration.Pages.Publishing.Comparison.ExternalTerminalCompareFixtures",
            typeof(ExternalTerminalCompareTestFixture).Assembly);
        public IngredientExternalEvidenceTestFixture.MemoryStore Store { get; } = new IngredientExternalEvidenceTestFixture.MemoryStore();
        public PublishingPageCompareRequest Request { get; }
        public ExternalTerminalCompareAdmission Admission { get; }

        public ExternalTerminalCompareTestFixture()
        {
            const string targetOrigin = "https://a830edad9050849cupcollect.sharepoint.com";
            const string sitePath = "/sites/biatmicrosoft-ccd35-f92cd61c";
            const string webPath = sitePath + "/blog_Archive";
            const string targetPath = webPath + "/SitePages/rss.aspx";
            Admission = new ExternalTerminalCompareAdmission
            {
                Binding = new IngredientExternalPlanBinding
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = "f411b3cf66979cbcb16479dd79ef2faefecc237df164c6d1e3642967d869c7c7",
                        IngredientId = "ccd.ingredient.page.layout/v1:df8c03a5-8f4b-4966-bbe6-fc82992c0234:wiki-runtime-binding",
                        Lane = "page.layout", Kind = PageIngredientKind.Layout, Subtype = "layout.wiki",
                        SemanticRole = "wiki-page-family-layout-binding", SourcePredicateId = "layout.wiki.source-binding/v1"
                    },
                    SourcePage = new IngredientExternalPageIdentity
                    {
                        PageUrl = "https://microsoft.sharepoint.com/sites/biatmicrosoft/blog_Archive/SitePages/rss.aspx",
                        FileServerRelativeUrl = "/sites/biatmicrosoft/blog_Archive/SitePages/rss.aspx",
                        ListId = "6779e40e-3e8f-41c3-bbd4-9a5020797f2f", ItemId = 1,
                        FileUniqueId = "df8c03a5-8f4b-4966-bbe6-fc82992c0234",
                        ETag = "\"{DF8C03A5-8F4B-4966-BBE6-FC82992C0234},12\""
                    },
                    Producer = new IngredientMaturityProducerBinding { ImplementationCommit = "8de62c9d4268f9017f6f51a4b5a9447b5eb2e6ec" },
                    Target = new IngredientMaturityTargetBinding { TargetIdentity = targetOrigin + targetPath, TargetProfile = "cupcollect-classic-page/v1" },
                    PlanDigest = "8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f",
                    PlanSchema = "ccd.batch1-repro-plan/v1",
                    RunId = "f92cd61c-afcc-43f1-9134-118b9b7120de",
                    LifecycleDigest = "be976626b4029cee5ecb369759876deb841e98aa1f31f6c714f31bcbb5884ec7",
                    TargetMappingDigest = "aa923cd981c2a543e91d31954bea053102f84942fbd6e59974fe21c70d0310ae",
                    RowId = "row-00003", RowNumber = 3,
                    NativeOperationId = "ccd143-native-page-e3ddf6375cb221e8",
                    NativeActionId = "ccd143-action-native-create-ccd143-native-page-e3ddf6375cb221e8",
                    OwnershipMarker = "[CCD-143 f92cd61c]",
                    TargetOrigin = targetOrigin, TargetSitePath = sitePath, TargetWebPath = webPath,
                    TargetListPath = webPath + "/SitePages", TargetPath = targetPath,
                    LifecycleProducer = new IngredientExternalProducerReference
                    {
                        Kind = "workspace-file", Path = "ccd-143/scripts/build-lifecycle-expressions.mjs",
                        Sha256 = "441ce088a3e2d4f5860412d8ce4d99e4c83f0ddc9e3890e8a843cff56fde19ca"
                    }
                },
                Target = new IngredientExternalTargetIdentity
                {
                    ListId = "e7514557-2e60-4eda-a31b-8d044a75f677", ItemId = 3,
                    FileUniqueId = "5842ce20-27a4-4d8f-acd9-53be246b54ef",
                    ETag = "\"{5842CE20-27A4-4D8F-ACD9-53BE246B54EF},1\""
                },
                ConsumerIssue = "CCD-147", ImplementationBranch = "ingredient/page-layout/ccd109-r00003-v15",
                ImplementationTree = "4a498a34a0f3d7b95957638937c7706e8490d08a",
                BrowserReviewIssue = "CCD-618", BrowserReviewRunId = "1571b922-a7f4-4a75-8e63-f6e198d01856",
                ExecutionWindowStartUtc = DateTimeOffset.Parse("2026-09-11T19:46:00Z"),
                ExecutionWindowEndUtc = DateTimeOffset.Parse("2026-09-11T20:10:00Z"),
                ManifestSha256 = "ffb1298445fce2d95d9ec2eae52bccba61e2832d0a51222cde42590ccd15d3b8",
                FreshReadbackSha256 = "8d604a593164aa84e3cc0aba19311404573c5c524875aa14aa228ae8f471e5e4",
                BrowserReviewSha256 = "d8bd73c570e9df2b1dd4b48b8e05e4520e39ae3c6c43f710ff0c187a9b6539c9",
                CleanupUnavailableSha256 = "809b6360f2533de68003bf9ca976bac6e34029c74b4c5245d25e8fa54c18ca6c",
                PostCleanupSha256 = "bca338a1c1049b900fb3183c66f031965601d421bf33f265b298998db0925147",
                BrowserPolicy = new ExternalCompareBrowserPolicy
                {
                    MinimumBodyTextLength = 100, RequireAspNetForm = true, RequireClassicSurface = true, RequireWikiRuntimeSignal = true
                },
                // Explicit consumer bounds; not a claim that the absent plan
                // or lifecycle-input artifact was reopened and revalidated.
                ReadbackMaximumAttempts = 24, ReadbackTimeoutMilliseconds = 15000
            };
            Request = new PublishingPageCompareRequest
            {
                GeneratedAtUtc = DateTimeOffset.Parse("2026-09-11T20:20:00Z"),
                Producer = new CompareProducer
                {
                    Id = "PnP.Framework.PublishingPageCompareReconciler", Version = "external-terminal/v1",
                    ImplementationRef = "assembly:PnP.Framework@" + typeof(PublishingPageCompareReconciler).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion
                },
                ExternalEvidence = new ExternalTerminalCompareEvidence()
            };
            foreach (var name in new[] { "Manifest", "FreshReadback", "BrowserReview", "CleanupUnavailable", "PostCleanup" })
                Replace(name, OriginalBytes(name), false);
        }

        public PublishingPageCompareReport Reconcile() => PublishingPageCompareReconciler.ReconcileExternalTerminal(Request, Admission, Store);
        public static byte[] OriginalBytes(string name) => Convert.FromBase64String(Resources.GetString(name));
        public ArtifactReference Artifact(string name) => (ArtifactReference)typeof(ExternalTerminalCompareEvidence).GetProperty(name).GetValue(Request.ExternalEvidence);
        public void Pin(string name, string digest) => typeof(ExternalTerminalCompareAdmission).GetProperty(name + "Sha256").SetValue(Admission, digest);
        public void Replace(string name, byte[] bytes, bool repin = true)
        {
            var artifact = Store.Add(bytes);
            typeof(ExternalTerminalCompareEvidence).GetProperty(name).SetValue(Request.ExternalEvidence, artifact);
            if (repin) Pin(name, artifact.Sha256);
        }
        public void Mutate(string name, Action<JsonNode> mutation)
        {
            var node = JsonNode.Parse(Store.Bytes(Artifact(name).Sha256));
            mutation(node);
            Replace(name, Encoding.UTF8.GetBytes(MigrationContractSerializer.SerializeCanonical(node)));
        }
        public static void SealObservation(JsonNode root)
        {
            root["observationSha256"] = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(root["observation"]));
        }
    }
}
