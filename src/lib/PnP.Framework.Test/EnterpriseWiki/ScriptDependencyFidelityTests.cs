using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.Publishing.Layouts;
using PnP.Framework.Migration.Pages.Publishing.Layouts.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Topology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class ScriptDependencyFidelityTests
    {
        private static readonly Guid WebPartId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        [DataTestMethod]
        [DataRow("ScriptEditorDocumentFilter.xml", "DocumentFilter.js", PageReferenceKind.Script)]
        [DataRow("ScriptEditorYammer.xml", "YammerScript.js", PageReferenceKind.Script)]
        [DataRow("ScriptEditorGuidanceTab.xml", "ipkitguidancetab.js", PageReferenceKind.Script)]
        [DataRow("ScriptEditorIPKitUsageData.xml", "IPKitUsageData.js", PageReferenceKind.Script)]
        public void RealScriptEditorFixturesProduceSemanticRenderableReferences(
            string fixtureName,
            string expectedSuffix,
            PageReferenceKind expectedKind)
        {
            var references = ReadReferences(fixtureName, null);

            var reference = references.Single(value => value.OriginalValue.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(expectedKind, reference.Kind);
            Assert.IsTrue(reference.IsRenderableResource);
            Assert.AreEqual($"webpart:{WebPartId}", reference.Consumer);
            Assert.AreEqual(PageCaptureStatus.CapturedWithLimitations, reference.CaptureStatus);
            Assert.IsNull(reference.ContentBase64);
            Assert.IsFalse(string.IsNullOrWhiteSpace(reference.Diagnostics.Single()));
        }

        [TestMethod]
        public void ScriptEditorXmlAndHtmlDecodingPreservesSemanticKindsAndConsumer()
        {
            var guidance = ReadReferences("ScriptEditorGuidanceTab.xml", null)
                .Single(value => value.OriginalValue.EndsWith("ipkitguidancetab.js", StringComparison.OrdinalIgnoreCase));
            var documentFilter = ReadReferences("ScriptEditorDocumentFilter.xml", null);

            Assert.AreEqual("~site/SiteAssets/Scripts/ipkitguidancetab.js", guidance.OriginalValue);
            Assert.AreEqual(
                "https://source.example/sites/ipkit/SiteAssets/Scripts/ipkitguidancetab.js",
                guidance.SourceAbsoluteUrl);
            Assert.AreEqual(PageReferenceKind.Script, guidance.Kind);
            Assert.IsTrue(documentFilter.Any(value => value.Kind == PageReferenceKind.Image
                && value.OriginalValue.EndsWith("loading.gif", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(documentFilter.Any(value => value.Kind == PageReferenceKind.StyleSheet
                && value.OriginalValue.EndsWith("DocumentFilter.css", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(documentFilter.Where(value => value.Kind == PageReferenceKind.Script).Count() >= 2);
            Assert.IsTrue(documentFilter.All(value => value.Consumer == $"webpart:{WebPartId}"));
        }

        [TestMethod]
        public void SourceContextCaptureRetainsPayloadLengthAndSha256()
        {
            using (var context = new ClientContext("https://source.example/sites/ipkit"))
            {
                var references = ReadReferences(
                    "ScriptEditorDocumentFilter.xml",
                    context,
                    path => Encoding.UTF8.GetBytes("sanitized-payload:" + path));
                var reference = references.Single(value => value.OriginalValue.EndsWith("DocumentFilter.js", StringComparison.OrdinalIgnoreCase));
                var expected = Encoding.UTF8.GetBytes("sanitized-payload:" + reference.SourceServerRelativeUrl);

                Assert.AreEqual(PageCaptureStatus.Captured, reference.CaptureStatus);
                Assert.AreEqual(Convert.ToBase64String(expected), reference.ContentBase64);
                Assert.AreEqual(expected.LongLength, reference.ContentLength);
                Assert.AreEqual(PublishingPageDigest.ComputeSha256(expected), reference.ContentSha256);
            }
        }

        [TestMethod]
        public void NonScriptEditorJsPropertyRemainsNonRenderableFallbackEvidence()
        {
            const string exportXml = @"<webParts><webPart xmlns=""http://schemas.microsoft.com/WebPart/v3""><metaData><type name=""Contoso.ScriptEditorWebPart, Contoso"" /></metaData><data><properties><property name=""Content"" type=""string"">&quot;https://source.example/sites/ipkit/SiteAssets/Scripts/inert.js&quot;</property></properties></data></webPart></webParts>";
            var source = SourceIdentity();
            var references = PageReferenceSnapshotReader.Read(
                null,
                source,
                null,
                null,
                new[] { new ClassicWebPartSnapshot { Id = WebPartId, ExportXml = exportXml } },
                new PageCaptureOptions { SourcePageServerRelativeUrl = source.PageServerRelativeUrl },
                new List<string>());

            Assert.IsTrue(references.Count > 0);
            Assert.IsTrue(references.All(reference => reference.Kind == PageReferenceKind.Unknown));
            Assert.IsTrue(references.All(reference => !reference.IsRenderableResource));
            Assert.IsTrue(references.All(reference => reference.CaptureStatus == PageCaptureStatus.Captured));
        }

        [TestMethod]
        public void ScriptEditorTextFallbackTypesOnlySafeJsAndCssPaths()
        {
            const string exportXml = @"<webParts><webPart xmlns=""http://schemas.microsoft.com/WebPart/v3""><metaData><type name=""Microsoft.SharePoint.WebPartPages.ScriptEditorWebPart"" /></metaData><data><properties><property name=""Content"" type=""string"">loader='https://source.example/sites/ipkit/SiteAssets/Scripts/fallback.js?rev=1'; theme=&quot;/sites/ipkit/Style Library/fallback.css#v2&quot;; endpoint='https://source.example/sites/ipkit/api/data';</property></properties></data></webPart></webParts>";
            var source = SourceIdentity();
            var references = PageReferenceSnapshotReader.Read(
                null,
                source,
                null,
                null,
                new[] { new ClassicWebPartSnapshot { Id = WebPartId, ExportXml = exportXml } },
                new PageCaptureOptions { SourcePageServerRelativeUrl = source.PageServerRelativeUrl },
                new List<string>());

            Assert.AreEqual(PageReferenceKind.Script, references.Single(value => value.OriginalValue.Contains("fallback.js")).Kind);
            Assert.AreEqual(PageReferenceKind.StyleSheet, references.Single(value => value.OriginalValue.Contains("fallback.css")).Kind);
            Assert.AreEqual(PageReferenceKind.Unknown, references.Single(value => value.OriginalValue.EndsWith("/api/data", StringComparison.Ordinal)).Kind);
        }

        [TestMethod]
        public void FieldHtmlReferencesRetainFieldConsumerForFreshReadbackBinding()
        {
            var source = SourceIdentity();
            var references = PageReferenceSnapshotReader.Read(
                null,
                source,
                null,
                null,
                null,
                new PageCaptureOptions { SourcePageServerRelativeUrl = source.PageServerRelativeUrl },
                new List<string>(),
                new[]
                {
                    new PnP.Framework.Migration.Pages.Fields.PageFieldValueSnapshot
                    {
                        InternalName = "CustomMarkup",
                        Kind = PnP.Framework.Migration.Pages.Fields.PageFieldValueKind.String,
                        Value = "<script src=\"/sites/ipkit/SiteAssets/Scripts/field.js\"></script>"
                    }
                });

            Assert.AreEqual("field:CustomMarkup", references.Single().Consumer);
            Assert.AreEqual(PageReferenceKind.Script, references.Single().Kind);
        }

        [TestMethod]
        public void PlannerNeverRewritesRenderableReferenceWithoutPayload()
        {
            var source = SourceIdentity();
            var dependency = ReadReferences("ScriptEditorDocumentFilter.xml", null)
                .Single(value => value.OriginalValue.EndsWith("DocumentFilter.js", StringComparison.OrdinalIgnoreCase));
            var mapping = SiteMapping();

            var preserved = PageReferencePlanner.BuildActions(
                source,
                new[] { dependency },
                "https://target.example/sites/ipkit-target",
                "/sites/ipkit-target",
                mapping,
                new PagePlanningOptions { AllowExternalResourceReferences = true },
                new List<string>()).Single();
            var blockers = new List<string>();
            var blocked = PageReferencePlanner.BuildActions(
                source,
                new[] { dependency },
                "https://target.example/sites/ipkit-target",
                "/sites/ipkit-target",
                mapping,
                new PagePlanningOptions { AllowExternalResourceReferences = false },
                blockers).Single();

            Assert.AreEqual(PageReferenceDisposition.PreserveExternal, preserved.Disposition);
            Assert.AreEqual(dependency.SourceAbsoluteUrl, preserved.TargetAbsoluteUrl);
            Assert.IsNull(preserved.TargetServerRelativeUrl);
            Assert.AreEqual(PageReferenceDisposition.Block, blocked.Disposition);
            Assert.AreEqual(1, blockers.Count);

            var bytes = Encoding.UTF8.GetBytes("console.log('captured');");
            dependency.CaptureStatus = PageCaptureStatus.Captured;
            dependency.ContentBase64 = Convert.ToBase64String(bytes);
            dependency.ContentLength = bytes.LongLength;
            dependency.ContentSha256 = PublishingPageDigest.ComputeSha256(bytes);
            var materialized = PageReferencePlanner.BuildActions(
                source,
                new[] { dependency },
                "https://target.example/sites/ipkit-target",
                "/sites/ipkit-target",
                mapping,
                new PagePlanningOptions(),
                new List<string>()).Single();
            Assert.AreEqual(PageReferenceDisposition.MaterializeAtTarget, materialized.Disposition);
        }

        [TestMethod]
        public void SharePointRuntimeScriptRewriteRequiresFreshTargetProof()
        {
            var source = SourceIdentity();
            var dependency = new PageReferenceSnapshot
            {
                Id = "runtime-script",
                OriginalValue = "/_layouts/15/init.js",
                SourceAbsoluteUrl = "https://source.example/_layouts/15/init.js",
                SourceServerRelativeUrl = "/_layouts/15/init.js",
                Consumer = $"webpart:{WebPartId}",
                Kind = PageReferenceKind.Script,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.CapturedWithLimitations
            };

            var action = PageReferencePlanner.BuildActions(
                source,
                new[] { dependency },
                "https://target.example/sites/ipkit-target",
                "/sites/ipkit-target",
                SiteMapping(),
                new PagePlanningOptions { AllowExternalResourceReferences = true },
                new List<string>()).Single();

            Assert.AreEqual(PageReferenceDisposition.RewriteToTarget, action.Disposition);
            Assert.AreEqual("https://target.example/_layouts/15/init.js", action.TargetAbsoluteUrl);
            var unavailable = PageReferenceVerification.InspectPlan(
                dependency,
                action,
                (_, __) => new PageReferenceTargetReadState
                {
                    Exists = false,
                    HttpStatusCode = 404
                },
                new Uri("https://target.example/sites/ipkit-target"));
            var exact = PageReferenceVerification.InspectPlan(
                dependency,
                action,
                (_, __) => new PageReferenceTargetReadState
                {
                    Exists = true,
                    HttpStatusCode = 200,
                    MediaType = "application/javascript",
                    ContentLength = 10,
                    ContentSha256 = new string('a', 64)
                },
                new Uri("https://target.example/sites/ipkit-target"));
            Assert.IsFalse(unavailable.Passed);
            Assert.IsTrue(exact.Passed);
        }

        [TestMethod]
        public void DynamicIpKitHomeLayoutEmitsExplicitUnresolvedRequiredScripts()
        {
            var markup = ReadFixture("IPKitHomeDynamicLayout.aspx");
            var parsed = PublishingPageLayoutMarkupParser.Parse(markup);
            var dynamicScripts = parsed.ResourceReferences
                .Where(value => value.Attribute == "dynamic-script:loadjsfile")
                .ToArray();

            Assert.AreEqual(3, dynamicScripts.Length);
            Assert.IsTrue(dynamicScripts.All(value => value.IsUnresolvedDynamic));
            Assert.IsTrue(dynamicScripts.All(value => !string.IsNullOrWhiteSpace(value.Diagnostic)));
            Assert.IsTrue(dynamicScripts.Any(value => value.Value.Contains("HomeIPKitBanner.js")));

            var snapshot = PublishingPageLayoutResourceSnapshotReader.Read(
                null,
                new Uri("https://source.example/sites/ipkit"),
                new Uri("https://source.example/sites/ipkit"),
                dynamicScripts[0],
                null);
            var plan = PublishingPageLayoutResourcePlanner.Create(
                snapshot,
                new Uri("https://source.example/sites/ipkit"),
                new Uri("https://source.example/sites/ipkit"),
                new Uri("https://target.example/sites/ipkit-target"),
                new Uri("https://target.example/sites/ipkit-target"),
                allowExternalResourceReferences: true);

            Assert.AreEqual(PublishingPageLayoutResourceEvidenceState.Unsupported, snapshot.EvidenceState);
            Assert.AreEqual(PublishingPageLayoutResourceMaterializationDisposition.Block, plan.Disposition);
            StringAssert.Contains(plan.Reason, "dynamic");
        }

        [TestMethod]
        public void LayoutPackageRejectsUnresolvedDynamicEvidenceOrActionForgery()
        {
            var reference = new PublishingPageLayoutResourceReference
            {
                Attribute = "dynamic-script:loadjsfile",
                Value = "buildPath(environmentContext())",
                IsUnresolvedDynamic = true,
                Diagnostic = "The required script URI is unresolved."
            };
            var snapshot = new PublishingPageLayoutSnapshot
            {
                EvidenceState = PublishingPageLayoutEvidenceState.Missing,
                ResourceReferences = new List<PublishingPageLayoutResourceReference> { reference },
                ResourceArtifacts = new List<PublishingPageLayoutResourceSnapshot>
                {
                    new PublishingPageLayoutResourceSnapshot
                    {
                        Reference = reference,
                        EvidenceState = PublishingPageLayoutResourceEvidenceState.Readable
                    }
                }
            };
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageLayoutPackageValidator.ValidateSnapshot(snapshot, null));

            var plan = new PublishingPageLayoutMaterializationPlan
            {
                TargetPageLayoutName = "custom",
                ResourceReferences = new List<PublishingPageLayoutResourceReference> { reference },
                ResourceMaterializations = new List<PublishingPageLayoutResourceMaterializationPlan>
                {
                    new PublishingPageLayoutResourceMaterializationPlan
                    {
                        SourceReference = reference.Value,
                        Disposition = PublishingPageLayoutResourceMaterializationDisposition.PreserveExternal
                    }
                }
            };
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageLayoutPackageValidator.ValidatePlan(
                    "custom",
                    false,
                    plan,
                    null,
                    new PublishingPageLayoutTargetAdmission()));
        }

        [TestMethod]
        public void DirectDynamicScriptAssignmentRetainsResolvedAndUnresolvedOccurrences()
        {
            const string markup = @"<script>
var first = document.createElement('script');
first.setAttribute('src', '~site/SiteAssets/Scripts/resolved.js');
var second = document.createElement('script');
second.setAttribute('src', chooseScript());
loadjsfile('~site/SiteAssets/Scripts/loadjsfile-resolved.js');
</script>";

            var parsed = PublishingPageLayoutMarkupParser.Parse(markup);

            Assert.IsTrue(parsed.ResourceReferences.Any(value =>
                value.Attribute == "dynamic-script:setAttribute-src"
                && value.Value == "~site/SiteAssets/Scripts/resolved.js"
                && !value.IsUnresolvedDynamic));
            Assert.IsTrue(parsed.ResourceReferences.Any(value =>
                value.Attribute == "dynamic-script:setAttribute-src"
                && value.IsUnresolvedDynamic
                && value.Value == "chooseScript()"));
            Assert.IsTrue(parsed.ResourceReferences.Any(value =>
                value.Attribute == "dynamic-script:loadjsfile"
                && !value.IsUnresolvedDynamic
                && value.Value == "~site/SiteAssets/Scripts/loadjsfile-resolved.js"));
        }

        [TestMethod]
        public void DynamicImageSourceAssignmentIsNotMisclassifiedAsAScriptDependency()
        {
            const string markup = @"<script>
var image = document.createElement('img');
image.setAttribute('src', chooseImage());
</script>";

            var parsed = PublishingPageLayoutMarkupParser.Parse(markup);

            Assert.IsFalse(parsed.ResourceReferences.Any(value =>
                value.Attribute != null
                && value.Attribute.StartsWith("dynamic-script:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void NestedDynamicCallsAreRetainedAsExplicitUnresolvedReferences()
        {
            const string markup = @"<script>
var script = document.createElement('script');
script.setAttribute('src', getUrl(environmentContext('stage')));
loadjsfile(buildPath(environmentContext('stage')));
</script>";

            var parsed = PublishingPageLayoutMarkupParser.Parse(markup);

            Assert.IsTrue(parsed.ResourceReferences.Any(value =>
                value.Attribute == "dynamic-script:setAttribute-src"
                && value.IsUnresolvedDynamic
                && value.Value == "getUrl(environmentContext('stage'))"));
            Assert.IsTrue(parsed.ResourceReferences.Any(value =>
                value.Attribute == "dynamic-script:loadjsfile"
                && value.IsUnresolvedDynamic
                && value.Value == "buildPath(environmentContext('stage'))"));
        }

        [TestMethod]
        public void CommentedAndStringLiteralDynamicExamplesDoNotCreateDependencies()
        {
            const string markup = @"<!-- <script>loadjsfile(commentedHtmlOnly())</script> -->
<%-- <script>loadjsfile(commentedAspNetOnly())</script> --%>
<script type=""text/html"">loadjsfile(templateOnly())</script>
<script type=""application/json"">{ ""example"": ""loadjsfile(jsonOnly())"" }</script>
<script>
// loadjsfile(makeUrl())
/* var ignored = document.createElement('script'); ignored.setAttribute('src', chooseScript()); */
var documentation = ""loadjsfile(documentedOnly())"";
</script>";

            var parsed = PublishingPageLayoutMarkupParser.Parse(markup);

            Assert.IsFalse(parsed.ResourceReferences.Any(value => value.IsUnresolvedDynamic));
        }

        [TestMethod]
        public void ResolvedLoadJsFileCallDoesNotSuppressIndependentDynamicScriptVariable()
        {
            const string markup = @"<script>
loadjsfile('/sites/ipkit/SiteAssets/Scripts/known.js');
var script = document.createElement('script');
script.setAttribute('src', dynamicUrl);
</script>";

            var parsed = PublishingPageLayoutMarkupParser.Parse(markup);

            Assert.IsTrue(parsed.ResourceReferences.Any(value =>
                value.Attribute == "dynamic-script:loadjsfile"
                && !value.IsUnresolvedDynamic
                && value.Value.EndsWith("known.js", StringComparison.Ordinal)));
            Assert.IsTrue(parsed.ResourceReferences.Any(value =>
                value.Attribute == "dynamic-script:setAttribute-src"
                && value.IsUnresolvedDynamic
                && value.Value == "dynamicUrl"));
        }

        [DataTestMethod]
        [DataRow(false, 404, "application/javascript")]
        [DataRow(true, 200, "text/html")]
        public void FreshTargetProbeRejects404AndMimeEquivalentAbsence(
            bool exists,
            int statusCode,
            string mediaType)
        {
            var snapshot = ScriptSnapshot(withPayload: false);
            var action = RewriteAction(snapshot.Id);

            var result = PageReferenceVerification.Verify(
                new[] { snapshot },
                new[] { action },
                _ => true,
                (_, __) => new PageReferenceTargetReadState
                {
                    Exists = exists,
                    HttpStatusCode = statusCode,
                    MediaType = mediaType
                }).Single();

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.TargetMatched);
        }

        [TestMethod]
        public void JavaScriptHtmlTemplateIsNotMisclassifiedAsAnHtmlErrorShell()
        {
            var script = Encoding.UTF8.GetBytes("const template = '<html><body>client template</body></html>'; ");
            var error = Encoding.UTF8.GetBytes("\uFEFF  <!DOCTYPE html><html><body>not found</body></html>");

            Assert.AreEqual(
                "application/javascript",
                PageReferenceVerification.InferMediaType("app.js", script, script.Length));
            Assert.AreEqual(
                "text/html",
                PageReferenceVerification.InferMediaType("app.js", error, error.Length));
            Assert.AreEqual(
                "text/html",
                PageReferenceVerification.ResolveMediaType(
                    "application/javascript",
                    "app.js",
                    error,
                    error.Length));
        }

        [TestMethod]
        public void AccessDeniedTextCannotPromote404OrUnknownFailureToAuthorization()
        {
            Assert.IsTrue(PublishingPageLayoutServerFailure.IsMissing(
                -2147024894,
                "System.IO.FileNotFoundException"));
            Assert.IsFalse(PublishingPageLayoutServerFailure.IsAccessDenied(
                -2147024894,
                "System.IO.FileNotFoundException"));
            Assert.IsFalse(PublishingPageLayoutServerFailure.IsAccessDenied(
                0,
                "AccessDenied text is not literal HTTP 401/403 evidence"));
            Assert.IsTrue(PublishingPageLayoutServerFailure.IsAccessDenied(
                -2147024891,
                "System.UnauthorizedAccessException"));
        }

        [TestMethod]
        public void FreshTargetDigestIsRequiredForMaterializationAndRenderableRewrite()
        {
            var snapshot = ScriptSnapshot(withPayload: true);
            var materialize = new PageReferenceAction
            {
                SnapshotDependencyId = snapshot.Id,
                Disposition = PageReferenceDisposition.MaterializeAtTarget,
                TargetAbsoluteUrl = "https://target.example/sites/ipkit-target/SiteAssets/Scripts/app.js",
                TargetServerRelativeUrl = "/sites/ipkit-target/SiteAssets/Scripts/app.js"
            };
            var exact = new PageReferenceTargetReadState
            {
                Exists = true,
                HttpStatusCode = 200,
                MediaType = "application/javascript",
                ContentLength = snapshot.ContentLength,
                ContentSha256 = snapshot.ContentSha256
            };

            var passed = PageReferenceVerification.Verify(
                new[] { snapshot },
                new[] { materialize },
                _ => true,
                (_, __) => exact).Single();
            exact.ContentSha256 = new string('f', 64);
            var drifted = PageReferenceVerification.Verify(
                new[] { snapshot },
                new[] { RewriteAction(snapshot.Id) },
                _ => true,
                (_, __) => exact).Single();

            Assert.IsTrue(passed.Passed);
            Assert.IsFalse(drifted.Passed);
        }

        [TestMethod]
        public void RenderableRewritePlanRequiresFreshExactTargetEvidence()
        {
            var snapshot = ScriptSnapshot(withPayload: false);
            var action = RewriteAction(snapshot.Id);

            var unavailable = PageReferenceVerification.InspectPlan(
                snapshot,
                action,
                (_, __) => null);
            var exact = PageReferenceVerification.InspectPlan(
                snapshot,
                action,
                (_, __) => new PageReferenceTargetReadState
                {
                    Exists = true,
                    HttpStatusCode = 200,
                    MediaType = "application/javascript",
                    ContentLength = 17,
                    ContentSha256 = new string('a', 64)
                });

            Assert.IsFalse(unavailable.Passed);
            Assert.IsTrue(exact.Passed);

            action.TargetAbsoluteUrl = "https://target.example/sites/ipkit-target/SiteAssets/Scripts/other.js";
            var pathMismatch = PageReferenceVerification.InspectPlan(
                snapshot,
                action,
                (_, __) => new PageReferenceTargetReadState
                {
                    Exists = true,
                    HttpStatusCode = 200,
                    MediaType = "application/javascript"
                },
                new Uri("https://target.example/sites/ipkit-target"));
            action.TargetAbsoluteUrl = "https://other.example/sites/ipkit-target/SiteAssets/Scripts/app.js";
            var authorityMismatch = PageReferenceVerification.InspectPlan(
                snapshot,
                action,
                (_, __) => new PageReferenceTargetReadState
                {
                    Exists = true,
                    HttpStatusCode = 200,
                    MediaType = "application/javascript"
                },
                new Uri("https://target.example/sites/ipkit-target"));
            Assert.IsFalse(pathMismatch.Passed);
            Assert.IsFalse(authorityMismatch.Passed);
        }

        [TestMethod]
        public void SharedTargetPathCountsOnceButVerifiesEveryConsumer()
        {
            var first = ScriptSnapshot(withPayload: true);
            first.Id = "script-first";
            first.Consumer = "webpart:11111111-1111-1111-1111-111111111111";
            var second = ScriptSnapshot(withPayload: true);
            second.Id = "script-second";
            second.Consumer = "webpart:22222222-2222-2222-2222-222222222222";
            var actions = new[]
            {
                MaterializeAction(first.Id),
                MaterializeAction(second.Id)
            };
            var readCount = 0;

            var results = PageReferenceVerification.Verify(
                new[] { first, second },
                actions,
                _ => true,
                (_, __) =>
                {
                    readCount++;
                    return new PageReferenceTargetReadState
                    {
                        Exists = true,
                        HttpStatusCode = 200,
                        MediaType = "application/javascript",
                        ContentLength = first.ContentLength,
                        ContentSha256 = first.ContentSha256
                    };
                });

            Assert.AreEqual(1, PageReferenceVerification.ExpectedMaterializationCount(actions));
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.All(value => value.Passed));
            Assert.AreEqual(1, readCount);
        }

        [TestMethod]
        public void ConflictingPayloadsForOneTargetPathBlockBeforeMutation()
        {
            var first = ScriptSnapshot(withPayload: true);
            first.Id = "script-first";
            var second = ScriptSnapshot(withPayload: true);
            second.Id = "script-second";
            second.ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("different"));
            second.ContentLength = 9;
            second.ContentSha256 = PublishingPageDigest.ComputeSha256(Encoding.UTF8.GetBytes("different"));
            var blockers = new List<string>();

            var actions = PageReferencePlanner.BuildActions(
                SourceIdentity(),
                new[] { first, second },
                "https://target.example/sites/ipkit-target",
                "/sites/ipkit-target",
                SiteMapping(),
                new PagePlanningOptions(),
                blockers);

            Assert.IsTrue(actions.All(value => value.Disposition == PageReferenceDisposition.Block));
            Assert.AreEqual(1, blockers.Count);
            StringAssert.Contains(blockers[0], "Conflicting dependency payloads");
        }

        [TestMethod]
        public void TargetReadLimitExpandsToTheSealedCapturedPayloadLength()
        {
            var snapshot = ScriptSnapshot(withPayload: true);
            snapshot.ContentLength = 32L * 1024 * 1024;

            Assert.AreEqual(32L * 1024 * 1024, PageReferenceVerification.TargetReadLimit(snapshot));
            Assert.IsTrue(PageReferenceVerification.TargetReadLimit(ScriptSnapshot(withPayload: false)) >= 16L * 1024 * 1024);
        }

        [TestMethod]
        public void ConsecutiveRuntimeProbesDoNotDisposeTheSharedHttpClientHandler()
        {
            var handler = new TrackingHttpMessageHandler();
            var client = new HttpClient(handler, disposeHandler: true);
            try
            {
                using (var first = new HttpRequestMessage(HttpMethod.Get, "https://target.example/_layouts/15/init.js"))
                using (var second = new HttpRequestMessage(HttpMethod.Get, "https://target.example/_layouts/15/core.js"))
                {
                    var firstResult = PageReferenceVerification.ReadHttpTargetResponse(
                        client,
                        first,
                        "init.js",
                        1024);
                    var secondResult = PageReferenceVerification.ReadHttpTargetResponse(
                        client,
                        second,
                        "core.js",
                        1024);

                    Assert.IsTrue(firstResult.Exists);
                    Assert.IsTrue(secondResult.Exists);
                    Assert.AreEqual(2, handler.RequestCount);
                    Assert.IsFalse(handler.Disposed);
                }
            }
            finally
            {
                client.Dispose();
            }
            Assert.IsTrue(handler.Disposed);
        }

        private static List<PageReferenceSnapshot> ReadReferences(
            string fixtureName,
            ClientContext context,
            Func<string, byte[]> payload = null)
        {
            var source = SourceIdentity();
            return PageReferenceSnapshotReader.Read(
                context,
                source,
                new SourceSiteCollectionSnapshot
                {
                    SiteId = source.SiteId,
                    RootWebId = source.WebId,
                    SiteCollectionUrl = source.WebUrl,
                    ServerRelativeUrl = source.WebServerRelativeUrl,
                    Webs = new List<SourceWebSnapshot>
                    {
                        new SourceWebSnapshot
                        {
                            SiteId = source.SiteId,
                            WebId = source.WebId,
                            WebUrl = source.WebUrl,
                            ServerRelativeUrl = source.WebServerRelativeUrl
                        }
                    }
                },
                null,
                new[]
                {
                    new ClassicWebPartSnapshot
                    {
                        Id = WebPartId,
                        ExportXml = ReadFixture(fixtureName)
                    }
                },
                new PageCaptureOptions
                {
                    SourcePageServerRelativeUrl = source.PageServerRelativeUrl,
                    MaximumDependencyBytes = 1024 * 1024
                },
                new List<string>(),
                payloadReader: payload == null
                    ? null
                    : new Func<Web, ClientContext, string, long, byte[]>((_, __, path, ___) => payload(path)));
        }

        private static PageIdentity SourceIdentity()
        {
            return new PageIdentity
            {
                SiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                WebId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                WebUrl = "https://source.example/sites/ipkit",
                WebServerRelativeUrl = "/sites/ipkit",
                PageServerRelativeUrl = "/sites/ipkit/Pages/home.aspx"
            };
        }

        private static SiteCollectionMappingPlan SiteMapping()
        {
            return new SiteCollectionMappingPlan
            {
                SourceSiteId = SourceIdentity().SiteId,
                SourceSiteCollectionUrl = "https://source.example/sites/ipkit",
                TargetSiteCollectionUrl = "https://target.example/sites/ipkit-target"
            };
        }

        private static PageReferenceSnapshot ScriptSnapshot(bool withPayload)
        {
            var bytes = Encoding.UTF8.GetBytes("console.log('fixture');");
            return new PageReferenceSnapshot
            {
                Id = "script-fixture",
                OriginalValue = "https://source.example/sites/ipkit/SiteAssets/Scripts/app.js",
                SourceAbsoluteUrl = "https://source.example/sites/ipkit/SiteAssets/Scripts/app.js",
                SourceServerRelativeUrl = "/sites/ipkit/SiteAssets/Scripts/app.js",
                Consumer = "script[src]",
                Kind = PageReferenceKind.Script,
                IsRenderableResource = true,
                CaptureStatus = withPayload ? PageCaptureStatus.Captured : PageCaptureStatus.CapturedWithLimitations,
                ContentBase64 = withPayload ? Convert.ToBase64String(bytes) : null,
                ContentLength = withPayload ? bytes.LongLength : 0,
                ContentSha256 = withPayload ? PublishingPageDigest.ComputeSha256(bytes) : null
            };
        }

        private static PageReferenceAction RewriteAction(string id)
        {
            return new PageReferenceAction
            {
                SnapshotDependencyId = id,
                Disposition = PageReferenceDisposition.RewriteToTarget,
                TargetAbsoluteUrl = "https://target.example/sites/ipkit-target/SiteAssets/Scripts/app.js",
                TargetServerRelativeUrl = "/sites/ipkit-target/SiteAssets/Scripts/app.js"
            };
        }

        private static PageReferenceAction MaterializeAction(string id)
        {
            return new PageReferenceAction
            {
                SnapshotDependencyId = id,
                Disposition = PageReferenceDisposition.MaterializeAtTarget,
                TargetAbsoluteUrl = "https://target.example/sites/ipkit-target/SiteAssets/Scripts/app.js",
                TargetServerRelativeUrl = "/sites/ipkit-target/SiteAssets/Scripts/app.js"
            };
        }

        private static string ReadFixture(string fileName)
        {
            var assembly = typeof(ScriptDependencyFidelityTests).GetTypeInfo().Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .Single(value => value.EndsWith("EnterpriseWiki.Fixtures." + fileName, StringComparison.Ordinal));
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Missing fixture: " + fileName)))
            {
                return reader.ReadToEnd();
            }
        }

        private sealed class TrackingHttpMessageHandler : HttpMessageHandler
        {
            public int RequestCount { get; private set; }

            public bool Disposed { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestCount++;
                var content = new ByteArrayContent(Encoding.UTF8.GetBytes("console.log('runtime');"));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/javascript");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = content
                });
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
