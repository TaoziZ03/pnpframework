using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Layouts;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PnP.Framework.Test.Migration.Pages.Assessment
{
    [TestClass]
    public class PublishingPageJsLinkM0Tests
    {
        // Frozen CCD-157 identity/digest metadata only. URLs and minimal property JSON are
        // hermetic test scaffolding, not customer script bytes or live evidence.
        private const string ClaimId = "bf157aae38e9e5b8c2b2b98d4ecf778e27ec887738b2bb8b237ce25b7cbacc02";
        private const string IngredientId = "ccd.ingredient.resource.script/v1:4d8c5b32-891a-43de-a6f3-bba584d1a222:ffbfdc2b-f759-4edf-93b6-bc3278132b4f:JSLink:~Site/SiteAssets/bloghome.js";
        private const string SemanticRole = "Posts list-view Web Part persisted JSLink binding to a same-site script asset";
        private const string SourceETag = "\"{4D8C5B32-891A-43DE-A6F3-BBA584D1A222},6\"";
        private const string ScriptDigest = "9b8fe6d5ccd86c8ea9b1f108bed0eecfb905b3814cac2702095d2bf9b44b8f0d";
        private static readonly Guid PageId = Guid.Parse("4d8c5b32-891a-43de-a6f3-bba584d1a222");
        private static readonly Guid HostId = Guid.Parse("ffbfdc2b-f759-4edf-93b6-bc3278132b4f");
        private static readonly Guid ForeignHostId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        [TestMethod]
        public void FrozenActiveClaimProjectsOneSourceBoundOwnerAndPassesUnmodifiedM0()
        {
            var fixture = new Fixture();
            var graph = fixture.Project();
            var node = graph.Nodes.Single(value => value.Id == IngredientId);
            var owner = PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(fixture.Snapshot, node);
            Assert.AreEqual("reference.jslink", owner.Id);
            Assert.AreEqual("resource.script", owner.PrimaryOwnerLane);
            Assert.AreEqual(1, graph.Nodes.Count(value => value.PrimaryOwnerLane == "resource.script"));
            Assert.AreEqual(IngredientId, node.Id);
            Assert.AreEqual("reference.jslink", node.Subtype);
            Assert.AreEqual(SemanticRole, node.SemanticRole);
            Assert.AreEqual("etag=" + SourceETag, node.SourceVersionIdentity);
            Assert.AreEqual(ScriptDigest, node.EvidenceDigest);
            Assert.AreEqual(2328L, fixture.Evidence.Reference.ContentLength);
            Assert.AreEqual(1, fixture.Evidence.HostWebPartOrder);
            Assert.AreEqual(0, fixture.Evidence.PersistedReferenceOrder);
            Assert.IsNull(fixture.Snapshot.WebParts[1].ExportXml);
            Assert.IsNull(fixture.Snapshot.WebParts[1].ExportSha256);
            Assert.IsNull(fixture.Snapshot.WebParts[1].TypeName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(PnP.Framework.Migration.Pages.ClassicWebParts.Planning.ClassicWebPartReplayCapabilityPolicy.GetBlocker(fixture.Snapshot.WebParts[1].ExportXml)),
                "A REST property observation must not authorize native Web Part replay.");
            Assert.IsTrue(graph.Edges.Any(value => value.FromIngredientId == IngredientId
                && value.ToIngredientId == "webpart:" + HostId.ToString("D")
                && value.Relationship == PageIngredientRelationship.DependsOn
                && value.Requirement == PageIngredientRequirement.Required));
            Assert.AreEqual("webpart.instance", graph.Nodes.Single(value => value.Id == "webpart:" + HostId.ToString("D")).PrimaryOwnerLane);
            var receipts = fixture.ValidateM0(node);
            Assert.AreEqual(3, receipts.Count);
            Assert.IsTrue(receipts.All(value => value.Passed), string.Join("; ", receipts.Select(value => value.FailureReason)));
            Assert.AreEqual("pnp-ingredient-maturity-assessment/v1", IngredientMaturityContract.SchemaVersion);
        }

        [TestMethod]
        public void TypedEnvelopeRoundTripsWithoutChangingClaimRoleOrderOrDigest()
        {
            var fixture = new Fixture();
            fixture.Seal();
            fixture.Snapshot = MigrationContractSerializer.Deserialize<PublishingPageCaptureBundle>(MigrationContractSerializer.SerializeCanonical(fixture.Snapshot));
            var envelope = fixture.Snapshot.IngredientEvidence.Single();
            var evidence = PublishingPageJsLinkReferenceProjector.ReadEvidence(envelope);
            Assert.AreEqual(IngredientId, evidence.IngredientId);
            Assert.AreEqual(SemanticRole, evidence.SemanticRole);
            Assert.AreEqual("~Site/SiteAssets/bloghome.js", evidence.PersistedPropertyValue);
            Assert.AreEqual("rest-expanded-webpart-json", evidence.HostEvidenceFormat);
            StringAssert.Contains(fixture.Snapshot.WebParts[1].PropertyEvidenceJson, "sp.ui.blogs.js");
            Assert.AreEqual(ScriptDigest, evidence.Reference.ContentSha256);
            Assert.AreEqual(SourceETag, evidence.SourcePageETag);
            Assert.IsNull(evidence.Reference.ContentBase64);
            var graph = PublishingPageIngredientGraphProjector.Project(fixture.Snapshot, fixture.Catalog);
            Assert.IsTrue(fixture.ValidateM0(graph.Nodes.Single(value => value.Id == IngredientId)).All(value => value.Passed));
        }

        [DataTestMethod]
        [DataRow("subtype")]
        [DataRow("host")]
        [DataRow("host-order")]
        [DataRow("reference-order")]
        [DataRow("source-version")]
        [DataRow("source-page")]
        [DataRow("source-item")]
        [DataRow("missing-digest")]
        [DataRow("invalid-digest")]
        [DataRow("missing-host-digest")]
        [DataRow("reference-form")]
        [DataRow("locator")]
        [DataRow("normalized-locator")]
        [DataRow("duplicate-occurrence")]
        [DataRow("consumer")]
        [DataRow("access-denied")]
        public void RedigestedInvalidTypedEvidenceFailsClosed(string corruption)
        {
            var fixture = new Fixture();
            switch (corruption)
            {
                case "subtype": fixture.Evidence.Subtype = "reference.script"; break;
                case "host": fixture.Evidence.HostWebPartId = ForeignHostId; break;
                case "host-order": fixture.Evidence.HostWebPartOrder = 0; break;
                case "reference-order": fixture.Evidence.PersistedReferenceOrder = 1; break;
                case "source-version": fixture.Evidence.SourcePageETag = SourceETag.Replace(",6", ",5"); break;
                case "source-page": fixture.Evidence.SourcePageFileUniqueId = ForeignHostId; break;
                case "source-item": fixture.Evidence.SourceListItemId = 3; break;
                case "missing-digest": fixture.Evidence.Reference.ContentSha256 = null; break;
                case "invalid-digest": fixture.Evidence.Reference.ContentSha256 = new string('z', 64); break;
                case "missing-host-digest": fixture.Evidence.HostEvidenceSha256 = null; break;
                case "reference-form": fixture.Evidence.ReferenceForm = "XmlDefinition.JSLink"; break;
                case "locator": fixture.Evidence.Reference.OriginalValue = "~Site/SiteAssets/other.js"; break;
                case "normalized-locator":
                    fixture.Evidence.Reference.SourceAbsoluteUrl = "https://source.example.invalid/sites/canary/SiteAssets/other.js";
                    fixture.Evidence.Reference.SourceServerRelativeUrl = "/sites/canary/SiteAssets/other.js";
                    break;
                case "duplicate-occurrence": fixture.Evidence.PersistedPropertyValue += "|~Site/SiteAssets/bloghome.js"; break;
                case "consumer": fixture.Evidence.Reference.Consumer = "webpart:" + ForeignHostId.ToString("D"); break;
                case "access-denied": fixture.Evidence.Reference.AuthorizationEvidence = new PnP.Framework.Migration.Evidence.LiteralHttpAuthorizationEvidence { HttpStatusCode = 403 }; break;
            }
            Assert.ThrowsException<InvalidDataException>(() => fixture.Project(), corruption);
        }

        [DataTestMethod]
        [DataRow("subtype")]
        [DataRow("semantic-role")]
        [DataRow("host-id")]
        [DataRow("source-version")]
        [DataRow("source-identity")]
        [DataRow("digest")]
        public void M0RejectsNodeCorruptionEvenWhenCallerCopiesCorruptFieldsIntoClaim(string corruption)
        {
            var fixture = new Fixture();
            var node = fixture.Project().Nodes.Single(value => value.Id == IngredientId);
            switch (corruption)
            {
                case "subtype": node.Subtype = "reference.script"; break;
                case "semantic-role": node.SemanticRole = "unrelated role"; break;
                case "host-id": node.Id = IngredientId.Replace(HostId.ToString("D"), ForeignHostId.ToString("D")); break;
                case "source-version": node.SourceVersionIdentity = "etag=" + SourceETag.Replace(",6", ",5"); break;
                case "source-identity": node.SourcePageOrListItemIdentity = "foreign/page"; break;
                case "digest": node.EvidenceDigest = new string('a', 64); break;
            }
            var context = fixture.Context();
            context.Identity.IngredientId = node.Id;
            context.Identity.Subtype = node.Subtype;
            context.Identity.SemanticRole = node.SemanticRole;
            context.Source.PageOrListItemIdentity = node.SourcePageOrListItemIdentity;
            context.Source.SourceVersion = node.SourceVersionIdentity;
            context.Source.SourceArtifactDigest = node.EvidenceDigest;
            var receipts = IngredientMaturityEvidenceValidator.ValidateM0(context, node, fixture.Snapshot,
                PublishingPageIngredientPrimaryOwnerRegistry.Default, new[] { "fixture:ccd306-m0" });
            Assert.IsFalse(receipts.Single(value => value.GateId == IngredientMaturityGateCatalog.PrimaryOwner).Passed, corruption);
        }

        [TestMethod]
        public void M0RejectsStaleClaimAgainstFreshProjectedNode()
        {
            var fixture = new Fixture();
            var node = fixture.Project().Nodes.Single(value => value.Id == IngredientId);
            var context = fixture.Context();
            context.Source.SourceVersion = "etag=" + SourceETag.Replace(",6", ",5");
            var receipts = IngredientMaturityEvidenceValidator.ValidateM0(context, node, fixture.Snapshot,
                PublishingPageIngredientPrimaryOwnerRegistry.Default, new[] { "fixture:stale-source" });
            Assert.IsFalse(receipts.Single(value => value.GateId == IngredientMaturityGateCatalog.SourceBinding).Passed);
        }

        [DataTestMethod]
        [DataRow("missing-host")]
        [DataRow("foreign-host-with-valid-claim")]
        [DataRow("missing-etag")]
        [DataRow("foreign-fence")]
        [DataRow("changed-host-payload")]
        [DataRow("changed-property-redigested-payload")]
        [DataRow("embedded-only-jslink")]
        [DataRow("unknown-host-format")]
        [DataRow("rest-payload-foreign-host")]
        [DataRow("rest-payload-wrong-zone")]
        [DataRow("rest-payload-duplicate-jslink")]
        [DataRow("duplicate-host")]
        [DataRow("duplicate-generic-reference")]
        public void CapturedSourceMustIndependentlyConfirmTheClaim(string corruption)
        {
            var fixture = new Fixture();
            switch (corruption)
            {
                case "missing-host": fixture.Snapshot.WebParts.RemoveAt(1); break;
                case "foreign-host-with-valid-claim":
                    var other = Guid.Parse("33333333-3333-4333-8333-333333333333");
                    fixture.Evidence.HostWebPartId = other;
                    fixture.Evidence.Reference.Consumer = "webpart:" + other.ToString("D");
                    fixture.Evidence.IngredientId = IngredientId.Replace(HostId.ToString("D"), other.ToString("D"));
                    break;
                case "missing-etag": fixture.Snapshot.SourceFence.ETag = null; break;
                case "foreign-fence": fixture.Snapshot.SourceFence.FileUniqueId = ForeignHostId; break;
                case "changed-host-payload": fixture.Snapshot.WebParts[1].PropertyEvidenceJson += " "; break;
                case "changed-property-redigested-payload":
                    var host = fixture.Snapshot.WebParts[1];
                    host.PropertyEvidenceJson = host.PropertyEvidenceJson.Replace("bloghome.js", "other.js");
                    fixture.RefreshHostArtifact();
                    break;
                case "embedded-only-jslink":
                    fixture.Snapshot.WebParts[1].PropertyEvidenceJson = fixture.Snapshot.WebParts[1].PropertyEvidenceJson.Replace("\"JSLink\":", "\"NotJSLink\":");
                    fixture.RefreshHostArtifact();
                    break;
                case "unknown-host-format": fixture.Snapshot.WebParts[1].PropertyEvidenceFormat = "guessed-export"; break;
                case "rest-payload-foreign-host":
                    fixture.Snapshot.WebParts[1].PropertyEvidenceJson = fixture.Snapshot.WebParts[1].PropertyEvidenceJson.Replace(HostId.ToString("D"), ForeignHostId.ToString("D"));
                    fixture.RefreshHostArtifact();
                    break;
                case "rest-payload-wrong-zone":
                    fixture.Snapshot.WebParts[1].PropertyEvidenceJson = fixture.Snapshot.WebParts[1].PropertyEvidenceJson.Replace("\"zoneIndex\":1", "\"zoneIndex\":7");
                    fixture.RefreshHostArtifact();
                    break;
                case "rest-payload-duplicate-jslink":
                    fixture.Snapshot.WebParts[1].PropertyEvidenceJson = fixture.Snapshot.WebParts[1].PropertyEvidenceJson.Replace("\"JSLink\":", "\"JSLink\":\"other.js\",\"JSLink\":");
                    fixture.RefreshHostArtifact();
                    break;
                case "duplicate-host": fixture.Snapshot.WebParts.Add(fixture.Snapshot.WebParts[1]); break;
                case "duplicate-generic-reference": fixture.Snapshot.Dependencies.Add(fixture.Evidence.Reference); break;
            }
            Assert.ThrowsException<InvalidDataException>(() => fixture.Project(), corruption);
        }

        [TestMethod]
        public void CatalogStillRejectsUnknownHandlerAndOverlappingLaneOwnership()
        {
            var fixture = new Fixture();
            fixture.Seal();
            Assert.ThrowsException<InvalidDataException>(() => PublishingPageIngredientHandlerCatalog.Default.ValidateEvidence(fixture.Snapshot.IngredientEvidence));
            Assert.ThrowsException<ArgumentException>(() => new PublishingPageIngredientHandlerCatalog(new PublishingPageIngredientHandler[]
            {
                new ProjectionOnlyHandler("test.jslink.one/v1"), new ProjectionOnlyHandler("test.jslink.two/v1")
            }));
        }

        [TestMethod]
        public void GenericScriptReferenceKeepsItsLegacyIdentityAndOwner()
        {
            var fixture = new Fixture();
            fixture.Snapshot.Dependencies.Add(new PageReferenceSnapshot
            {
                Id = "independent-body-script", Kind = PageReferenceKind.Script,
                OriginalValue = "/scripts/body.js", Consumer = "body", ContentSha256 = new string('a', 64)
            });
            var graph = fixture.Project();
            var generic = graph.Nodes.Single(value => value.Id == "reference:independent-body-script");
            Assert.AreEqual("reference.script", generic.Subtype);
            Assert.AreEqual("typed-script-reference", generic.SemanticRole);
            Assert.AreEqual("reference.kind-script", generic.SourcePredicateId);
            Assert.AreEqual("resource.script", PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(fixture.Snapshot, generic).PrimaryOwnerLane);
            fixture.Snapshot.IngredientEvidence = null;
            var legacy = PublishingPageIngredientGraphProjector.Project(fixture.Snapshot);
            Assert.AreEqual(PublishingPageIngredientGraphProjector.CurrentProjectionVersion, legacy.ProjectionVersion);
            Assert.IsFalse(legacy.Nodes.Any(value => value.Id == IngredientId));
        }

        [TestMethod]
        public void AbsentETagDoesNotChangeLegacySourceFenceCanonicalJson()
        {
            var json = MigrationContractSerializer.SerializeCanonical(new SourcePageFence
            {
                FileUniqueId = PageId, Length = 42, ModifiedUtc = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc), VersionLabel = "1.0"
            });
            Assert.AreEqual("{\"fileUniqueId\":\"4d8c5b32-891a-43de-a6f3-bba584d1a222\",\"versionLabel\":\"1.0\",\"length\":42,\"modifiedUtc\":\"2026-09-09T00:00:00Z\"}", json);
        }

        [TestMethod]
        public void ZoneIndexBindingIsNotTheEvidenceArrayOrdinal()
        {
            var fixture = new Fixture();
            fixture.Snapshot.WebParts = fixture.Snapshot.WebParts.Reverse().ToList();
            var node = fixture.Project().Nodes.Single(value => value.Id == IngredientId);
            Assert.IsTrue(fixture.ValidateM0(node).All(value => value.Passed));
        }

        [TestMethod]
        public void NativeExportIsAnExplicitAlternativeAndRetainsPipeOrder()
        {
            var fixture = new Fixture();
            var host = fixture.Snapshot.WebParts[1];
            var property = "~Site/SiteAssets/bloghome.js|companion.js";
            host.ExportXml = new XElement("webParts", new XElement("webPart", new XElement("data", new XElement("properties",
                new XElement("property", new XAttribute("name", "ListName"), "{44444444-4444-4444-8444-444444444444}"),
                new XElement("property", new XAttribute("name", "XmlDefinition"), "<View><Query /><JSLink>sp.ui.blogs.js</JSLink></View>"),
                new XElement("property", new XAttribute("name", "JSLink"), property))))).ToString(SaveOptions.DisableFormatting);
            host.ExportSha256 = MigrationDigest.ComputeSha256(host.ExportXml);
            host.PropertyEvidenceFormat = null;
            host.PropertyEvidenceJson = null;
            host.PropertyEvidenceArtifact = null;
            fixture.Evidence.HostEvidenceFormat = ClassicWebPartPropertyEvidenceValidator.NativeExportFormat;
            fixture.Evidence.HostEvidenceSha256 = host.ExportSha256;
            fixture.Evidence.PersistedPropertyValue = property;
            var node = fixture.Project().Nodes.Single(value => value.Id == IngredientId);
            Assert.IsTrue(fixture.ValidateM0(node).All(value => value.Passed));
        }

        [TestMethod]
        public void MissingPropertyObservationDoesNotChangeLegacyWebPartSerialization()
        {
            var json = MigrationContractSerializer.SerializeCanonical(new ClassicWebPartSnapshot { Id = HostId });
            Assert.AreEqual("{\"id\":\"ffbfdc2b-f759-4edf-93b6-bc3278132b4f\",\"title\":null,\"typeName\":null,\"zoneId\":null,\"zoneIndex\":0,\"hidden\":false,\"exportXml\":null,\"exportSha256\":null}", json);
        }

        private sealed class Fixture
        {
            public PublishingPageCaptureBundle Snapshot { get; set; }
            public PublishingPageJsLinkReferenceEvidence Evidence { get; }
            public PublishingPageIngredientHandlerCatalog Catalog { get; }
            private readonly ProjectionOnlyHandler handler = new ProjectionOnlyHandler("test.ccd306.jslink/v1");

            public Fixture()
            {
                var property = "~Site/SiteAssets/bloghome.js";
                var hostJson = MigrationContractSerializer.SerializeCanonical(new
                {
                    id = HostId.ToString("D"),
                    properties = new Dictionary<string, string>
                    {
                        ["JSLink"] = property,
                        ["ListName"] = "{44444444-4444-4444-8444-444444444444}",
                        ["XmlDefinition"] = "<View><Query /><JSLink>sp.ui.blogs.js</JSLink></View>"
                    },
                    title = "Posts", type = (string)null, zoneIndex = 1
                });
                var hostDigest = MigrationDigest.ComputeSha256(hostJson);
                Snapshot = new PublishingPageCaptureBundle
                {
                    Source = new PageIdentity
                    {
                        SiteId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), WebId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
                        WebUrl = "https://source.example.invalid/sites/canary", PageServerRelativeUrl = "/sites/canary/SitePages/defaulttest.aspx",
                        FileUniqueId = PageId, ListItemId = 2, ContentTypeId = "0x010100", VersionLabel = "fixture-version"
                    },
                    SourceFence = new SourcePageFence { FileUniqueId = PageId, ETag = SourceETag },
                    PageArtifact = new PageArtifactSnapshot { FileUniqueId = PageId },
                    Runtime = new PageRuntimeSnapshot { AdapterId = "test.classic-page/v1" },
                    Layout = new PublishingPageLayoutSnapshot(),
                    PublishingPageContent = string.Empty,
                    Security = new PageSecuritySnapshot(), Lifecycle = new PageLifecycleSnapshot(),
                    WebParts = new List<ClassicWebPartSnapshot>
                    {
                        new ClassicWebPartSnapshot { Id = ForeignHostId, ExportXml = "<webParts />", ExportSha256 = MigrationDigest.ComputeSha256("<webParts />") },
                        new ClassicWebPartSnapshot
                        {
                            Id = HostId, Title = "Posts", ZoneIndex = 1,
                            PropertyEvidenceFormat = ClassicWebPartPropertyEvidenceValidator.RestExpandedFormat, PropertyEvidenceJson = hostJson,
                            PropertyEvidenceArtifact = new ArtifactReference { Sha256 = hostDigest, Length = Encoding.UTF8.GetByteCount(hostJson), MediaType = "application/json" }
                        }
                    }
                };
                Evidence = new PublishingPageJsLinkReferenceEvidence
                {
                    IngredientId = PublishingPageJsLinkM0Tests.IngredientId, Subtype = "reference.jslink", SemanticRole = PublishingPageJsLinkM0Tests.SemanticRole,
                    SourcePageFileUniqueId = PageId, SourceListItemId = 2, SourcePageServerRelativeUrl = Snapshot.Source.PageServerRelativeUrl,
                    SourcePageETag = SourceETag, HostWebPartId = HostId, HostWebPartOrder = 1,
                    HostEvidenceSha256 = hostDigest, HostEvidenceFormat = ClassicWebPartPropertyEvidenceValidator.RestExpandedFormat,
                    ReferenceForm = "JSLink", PersistedPropertyValue = property, PersistedReferenceOrder = 0,
                    Reference = new PageReferenceSnapshot
                    {
                        Id = "captured-binding-reference", Kind = PageReferenceKind.Script, OriginalValue = "~Site/SiteAssets/bloghome.js",
                        Consumer = "webpart:" + HostId.ToString("D"), SourceAbsoluteUrl = "https://source.example.invalid/sites/canary/SiteAssets/bloghome.js",
                        SourceServerRelativeUrl = "/sites/canary/SiteAssets/bloghome.js", ContentLength = 2328, ContentSha256 = ScriptDigest,
                        IsRenderableResource = true, CaptureStatus = PageCaptureStatus.Captured
                    }
                };
                Catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            }

            public void Seal()
            {
                Snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
                {
                    PublishingPageIngredientEvidenceEnvelope.Create(handler, PublishingPageJsLinkReferenceEvidence.SchemaVersion,
                        Evidence.IngredientId, Evidence, new[] { "fixture:ccd306-active-claim-metadata", "fixture:ccd306-minimal-rest-properties" })
                };
            }

            public void RefreshHostArtifact()
            {
                var host = Snapshot.WebParts.Single(value => value.Id == HostId);
                host.PropertyEvidenceArtifact.Sha256 = MigrationDigest.ComputeSha256(host.PropertyEvidenceJson);
                host.PropertyEvidenceArtifact.Length = Encoding.UTF8.GetByteCount(host.PropertyEvidenceJson);
                Evidence.HostEvidenceSha256 = host.PropertyEvidenceArtifact.Sha256;
            }

            public CanonicalPageIngredientGraph Project()
            {
                Seal();
                return PublishingPageIngredientGraphProjector.Project(Snapshot, Catalog);
            }

            public IngredientMaturityEvaluationContext Context() => new IngredientMaturityEvaluationContext
            {
                Identity = new IngredientMaturityIdentity
                {
                    ClaimId = PublishingPageJsLinkM0Tests.ClaimId, Lane = "resource.script", IngredientId = PublishingPageJsLinkM0Tests.IngredientId,
                    Kind = PageIngredientKind.Reference, Subtype = "reference.jslink", SemanticRole = PublishingPageJsLinkM0Tests.SemanticRole,
                    SourcePredicateId = "reference.persisted-webpart-jslink/v1"
                },
                Source = new IngredientMaturitySourceBinding
                {
                    PageOrListItemIdentity = Snapshot.Source.SiteId.ToString("D") + "/" + Snapshot.Source.WebId.ToString("D")
                        + "/2/" + PageId.ToString("D") + "/" + Snapshot.Source.PageServerRelativeUrl + "/" + PublishingPageJsLinkM0Tests.IngredientId,
                    SourceVersion = "etag=" + SourceETag, SourceArtifactDigest = ScriptDigest,
                    SourceSnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(Snapshot)
                }
            };

            public IReadOnlyList<IngredientMaturityGateReceipt> ValidateM0(PageIngredientNode node) =>
                IngredientMaturityEvidenceValidator.ValidateM0(Context(), node, Snapshot, PublishingPageIngredientPrimaryOwnerRegistry.Default, new[] { "fixture:ccd306-m0" });
        }

        // The lane can copy this identity-only template and add its own action
        // and maturity contributors. It supplies no private registry or outcome.
        private sealed class ProjectionOnlyHandler : PublishingPageIngredientHandler<PublishingPageJsLinkReferenceEvidence>
        {
            private readonly PageIngredientHandlerDescriptor descriptor;
            public ProjectionOnlyHandler(string handlerId)
            {
                descriptor = new PageIngredientHandlerDescriptor(handlerId, new PageIngredientLaneDescriptor("resource.script", new[] { "classic-wiki", "publishing" }),
                    new[] { PublishingPageJsLinkReferenceEvidence.SchemaVersion }, PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                    700, new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix, PublishingPageJsLinkReferenceEvidence.IngredientIdPrefix) });
            }
            public override PageIngredientHandlerDescriptor Descriptor => descriptor;
            protected override void ProjectGraph(PublishingPageIngredientGraphProjectionContext context, PublishingPageIngredientEvidenceEnvelope envelope, PublishingPageJsLinkReferenceEvidence evidence)
                => context.AddPersistedJsLinkReference(envelope, evidence);
        }
    }
}
