using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public partial class PublishingPagePageReferenceAssetGraphTests
    {
        private const string PagePath = "/sites/DevCenter/Learn/Pages/and-submit-form-steps.aspx";
        private const string AssetPath = "/sites/DevCenter/Learn/PublishingImages/Pages/and-submit-form-steps/andManifest.png";
        private const string PageFileId = "5971c9ad-eb9b-4e2e-9c96-5e5707ad3e90";
        private const string PageETag = "\"{5971C9AD-EB9B-4E2E-9C96-5E5707AD3E90},17\"";
        private const string AssetDigest = "84b3dcb35fb0ff34d2a95b0f5cbf9cfd683bb05f6716d6819946000a1edf742f";
        private const string AssetId = "asset:page-reference:" + PageFileId + ":" + AssetPath;

        [TestMethod]
        public void FrozenOwnerRegistryRequiresTheReviewedTupleCorrection()
        {
            var snapshot = CreateClaimSnapshot();
            var asset = Project(snapshot).Nodes.Single(value => value.Subtype == "asset.image");
            asset.SemanticRole = "direct-page-reference";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, asset));

            asset.SemanticRole = "rendered-image-bytes";
            var correctedOwner = PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, asset);
            Assert.AreEqual("resource.image", correctedOwner.PrimaryOwnerLane);
            Assert.AreEqual(AssetId, asset.Id);
            Assert.AreEqual("asset.typed-image", asset.SourcePredicateId);
        }

        [TestMethod]
        public void ExactClaimPayloadProjectsOnceWithFrozenTupleAndReferenceProvenance()
        {
            var snapshot = CreateClaimSnapshot();
            snapshot.Dependencies.Add(Reference(snapshot, AssetPath, "field:PublishingPageContent"));
            var before = MigrationContractSerializer.SerializeCanonical(snapshot.Dependencies);
            var graph = Project(snapshot);
            var asset = graph.Nodes.Single(value => value.Kind == PageIngredientKind.Asset && value.Subtype == "asset.image");

            Assert.AreEqual(7862, ClaimedPng().Length);
            Assert.AreEqual(AssetDigest, MigrationDigest.ComputeSha256(ClaimedPng()));
            Assert.AreEqual(AssetId, asset.Id);
            Assert.AreEqual("pnp.asset", asset.KindId);
            Assert.AreEqual("rendered-image-bytes", asset.SemanticRole);
            Assert.AreEqual("asset.typed-image", asset.SourcePredicateId);
            Assert.AreEqual("resource.image", asset.PrimaryOwnerLane);
            Assert.AreEqual("resource.image", PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, asset).PrimaryOwnerLane);
            Assert.AreEqual(AssetDigest, asset.EvidenceDigest);
            Assert.IsTrue(asset.HasContent);
            StringAssert.Contains(asset.SourcePageOrListItemIdentity, "/240/" + PageFileId + "/" + PagePath);
            StringAssert.Contains(asset.SourceVersionIdentity, "version=" + PageETag + ";modified=2015-02-13T01:17:53.0000000Z;");
            CollectionAssert.AreEqual(snapshot.Dependencies.Select(value => "reference:" + value.Id)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray(), asset.EvidenceReferences.ToArray());
            Assert.AreEqual(2, graph.Edges.Count(value => value.ToIngredientId == asset.Id
                && value.Relationship == PageIngredientRelationship.BindsTo && value.Requirement == PageIngredientRequirement.Required));
            Assert.IsFalse(graph.Edges.Any(value => value.FromIngredientId == PublishingPageIngredientIds.PublishingContent
                && value.ToIngredientId == asset.Id && value.Requirement == PageIngredientRequirement.Required));
            Assert.AreEqual(before, MigrationContractSerializer.SerializeCanonical(snapshot.Dependencies));
        }

        [TestMethod]
        public void ReferenceOrderAndRepeatedSelectionHaveOneDeterministicPayloadNode()
        {
            var snapshot = CreateClaimSnapshot();
            snapshot.Dependencies.Add(Reference(snapshot, AssetPath, "field:PublishingPageContent"));
            var first = MigrationContractSerializer.SerializeCanonical(Project(snapshot));
            snapshot.Dependencies = snapshot.Dependencies.Reverse().ToList();
            var second = MigrationContractSerializer.SerializeCanonical(Project(snapshot));
            Assert.AreEqual(first, second);
        }

        [DataTestMethod]
        [DataRow("reference-only")]
        [DataRow("metadata-only")]
        [DataRow("missing")]
        [DataRow("failed")]
        [DataRow("limited")]
        [DataRow("401")]
        [DataRow("403")]
        [DataRow("html-200-denial")]
        [DataRow("non-image")]
        [DataRow("unreviewed-anchor")]
        [DataRow("bad-digest")]
        [DataRow("bad-length")]
        [DataRow("bad-base64")]
        [DataRow("external")]
        [DataRow("runtime")]
        [DataRow("query")]
        [DataRow("unknown-image-format")]
        public void UnavailableOrNonImageEvidenceCannotBecomeAnImageAndDoesNotStopIndependentAssets(string condition)
        {
            var snapshot = CreateClaimSnapshot();
            var reference = snapshot.Dependencies.Single();
            switch (condition)
            {
                case "reference-only": reference.ContentBase64 = null; reference.ContentSha256 = null; reference.ContentLength = 0; break;
                case "metadata-only": reference.ContentBase64 = null; break;
                case "missing": reference.CaptureStatus = PageCaptureStatus.NotReturned; break;
                case "failed": reference.CaptureStatus = PageCaptureStatus.Failed; break;
                case "limited": reference.CaptureStatus = PageCaptureStatus.CapturedWithLimitations; break;
                case "401":
                case "403":
                    // Even stale valid bytes cannot override a retained terminal denial.
                    reference.AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create("fixture-read", reference.SourceAbsoluteUrl,
                        int.Parse(condition), new DateTimeOffset(2026, 9, 9, 19, 26, 17, TimeSpan.Zero));
                    break;
                case "html-200-denial": SetPayload(reference, Encoding.UTF8.GetBytes("<!doctype html><title>Access Denied</title>")); break;
                case "non-image": reference.Kind = PageReferenceKind.Script; break;
                case "unreviewed-anchor": reference.Kind = PageReferenceKind.Anchor; reference.IsRenderableResource = false; break;
                case "bad-digest": reference.ContentSha256 = new string('f', 64); break;
                case "bad-length": reference.ContentLength++; break;
                case "bad-base64": reference.ContentBase64 = "not valid base64"; break;
                case "external": reference.SourceAbsoluteUrl = "https://external.example" + AssetPath; break;
                case "runtime": ChangePath(reference, "/_layouts/15/andManifest.png"); break;
                case "query": reference.SourceAbsoluteUrl += "?rendition=1"; break;
                case "unknown-image-format": ChangePath(reference, AssetPath.Replace(".png", ".jpg")); break;
            }
            var retained = MigrationContractSerializer.SerializeCanonical(reference);
            var independent = Reference(snapshot, AssetPath.Replace("andManifest.png", "independent.png"), "img[src]");
            snapshot.Dependencies.Add(independent);
            var graph = Project(snapshot);
            var image = graph.Nodes.Single(value => value.Subtype == "asset.image");

            Assert.AreEqual(independent.SourceServerRelativeUrl, image.Label);
            Assert.IsFalse(graph.Nodes.Any(value => value.Id == AssetId));
            Assert.IsTrue(graph.Nodes.Any(value => value.Id == "reference:" + reference.Id && value.Kind == PageIngredientKind.Reference));
            Assert.AreEqual(retained, MigrationContractSerializer.SerializeCanonical(reference));
        }

        [TestMethod]
        public void CapturedDirectFileAndCapturedEmptyFileAreNotRenderedImages()
        {
            foreach (var empty in new[] { false, true })
            {
                var snapshot = CreateClaimSnapshot();
                var reference = snapshot.Dependencies.Single();
                reference.Kind = PageReferenceKind.Anchor;
                ChangePath(reference, AssetPath.Replace(".png", empty ? ".txt" : ".pdf"));
                SetPayload(reference, empty ? Array.Empty<byte>() : Encoding.UTF8.GetBytes("%PDF-1.4\nfixture\n%%EOF"));
                var graph = Project(snapshot);
                var asset = graph.Nodes.Single(value => value.Subtype == "asset.page-referenced-file");
                Assert.AreEqual("page-referenced-file-bytes", asset.SemanticRole);
                Assert.AreEqual("asset.direct-page-file", asset.SourcePredicateId);
                Assert.AreEqual("resource.image", PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, asset).PrimaryOwnerLane);
                Assert.IsTrue(asset.HasContent);
                Assert.IsFalse(graph.Nodes.Any(value => value.Subtype == "asset.image"));
            }
        }

        [DataTestMethod]
        [DataRow("denied-pdf")]
        [DataRow("denied-txt")]
        [DataRow("denied-png")]
        [DataRow("valid-png")]
        [DataRow("valid-pdf")]
        [DataRow("empty-txt")]
        [DataRow("missing-pdf-independent")]
        public void R1SemanticAcquisitionProofClosesTheSevenProbeMatrix(string condition)
        {
            var snapshot = CreateClaimSnapshot();
            var reference = snapshot.Dependencies.Single();
            var expectedAssetPath = AssetPath;
            var expectedPredicate = "asset.typed-image";
            string unavailablePath = null;

            switch (condition)
            {
                case "denied-pdf":
                case "denied-txt":
                case "denied-png":
                    var extension = condition.Substring("denied".Length);
                    unavailablePath = AssetPath.Replace(".png", extension);
                    reference.Kind = extension == ".png" ? PageReferenceKind.Image : PageReferenceKind.Anchor;
                    ChangePath(reference, unavailablePath);
                    SetPayload(reference, Encoding.UTF8.GetBytes("<!doctype html><title>Access Denied</title>"));
                    var independent = Reference(snapshot, AssetPath.Replace("andManifest.png", "independent.png"), "img[src]");
                    snapshot.Dependencies.Add(independent);
                    expectedAssetPath = independent.SourceServerRelativeUrl;
                    break;
                case "valid-pdf":
                    reference.Kind = PageReferenceKind.Anchor;
                    ChangePath(reference, AssetPath.Replace(".png", ".pdf"));
                    SetPayload(reference, Encoding.UTF8.GetBytes("%PDF-1.4\nfixture\n%%EOF"));
                    expectedAssetPath = reference.SourceServerRelativeUrl;
                    expectedPredicate = "asset.direct-page-file";
                    break;
                case "empty-txt":
                    reference.Kind = PageReferenceKind.Anchor;
                    ChangePath(reference, AssetPath.Replace(".png", ".txt"));
                    SetPayload(reference, Array.Empty<byte>());
                    expectedAssetPath = reference.SourceServerRelativeUrl;
                    expectedPredicate = "asset.direct-page-file";
                    break;
                case "missing-pdf-independent":
                    reference.Kind = PageReferenceKind.Anchor;
                    unavailablePath = AssetPath.Replace(".png", ".pdf");
                    ChangePath(reference, unavailablePath);
                    reference.CaptureStatus = PageCaptureStatus.NotReturned;
                    var later = Reference(snapshot, AssetPath.Replace("andManifest.png", "independent.png"), "img[src]");
                    snapshot.Dependencies.Add(later);
                    expectedAssetPath = later.SourceServerRelativeUrl;
                    break;
            }
            var retained = MigrationContractSerializer.SerializeCanonical(reference);
            var graph = Project(snapshot);

            var asset = graph.Nodes.Single(value => value.Kind == PageIngredientKind.Asset);
            Assert.AreEqual(expectedAssetPath, asset.Label);
            Assert.AreEqual(expectedPredicate, asset.SourcePredicateId);
            Assert.IsFalse(unavailablePath != null
                && graph.Nodes.Any(value => value.Kind == PageIngredientKind.Asset && value.Label == unavailablePath));
            Assert.IsTrue(graph.Nodes.Any(value => value.Id == "reference:" + reference.Id
                && value.Kind == PageIngredientKind.Reference));
            Assert.AreEqual(retained, MigrationContractSerializer.SerializeCanonical(reference));
        }

        [TestMethod]
        public void ConflictingPayloadsPauseOnlyTheirCanonicalInstance()
        {
            var snapshot = CreateClaimSnapshot();
            var conflicting = Reference(snapshot, AssetPath, "field:PublishingPageContent");
            var bytes = ClaimedPng();
            bytes[100] ^= 1;
            SetPayload(conflicting, bytes);
            snapshot.Dependencies.Add(conflicting);
            snapshot.Dependencies.Add(Reference(snapshot, AssetPath.Replace("andManifest.png", "independent.png"), "img[src]"));
            var graph = Project(snapshot);
            Assert.AreEqual(1, graph.Nodes.Count(value => value.Subtype == "asset.image"));
            Assert.IsFalse(graph.Nodes.Any(value => value.Id == AssetId));
            Assert.AreEqual(3, graph.Nodes.Count(value => value.Kind == PageIngredientKind.Reference));
        }

        [DataTestMethod]
        [DataRow("absent")]
        [DataRow("file")]
        [DataRow("version")]
        [DataRow("length")]
        [DataRow("modified")]
        public void MissingForeignOrUnstablePageFenceDoesNotBindPayload(string change)
        {
            var snapshot = CreateClaimSnapshot();
            switch (change)
            {
                case "absent": snapshot.SourceFence = null; break;
                case "file": snapshot.SourceFence.FileUniqueId = Guid.Parse("11111111-aaaa-bbbb-cccc-222222222222"); break;
                case "version": snapshot.SourceFence.VersionLabel = "changed"; break;
                case "length": snapshot.SourceFence.Length++; break;
                case "modified": snapshot.SourceFence.ModifiedUtc = snapshot.SourceFence.ModifiedUtc.AddSeconds(1); break;
            }
            var graph = Project(snapshot);
            Assert.IsFalse(graph.Nodes.Any(value => value.Subtype == "asset.image"));
            Assert.IsTrue(graph.Nodes.Any(value => value.Id == "reference:" + snapshot.Dependencies.Single().Id));
        }

        [DataTestMethod]
        [DataRow("identity")]
        [DataRow("version")]
        [DataRow("digest")]
        [DataRow("provenance")]
        [DataRow("locator")]
        [DataRow("content")]
        [DataRow("node-id")]
        [DataRow("role")]
        [DataRow("source-drift")]
        [DataRow("payload-drift")]
        public void DefaultOwnerPredicateRejectsForgedOrStaleImageBindings(string field)
        {
            var snapshot = CreateClaimSnapshot();
            var asset = Project(snapshot).Nodes.Single(value => value.Subtype == "asset.image");
            switch (field)
            {
                case "identity": asset.SourcePageOrListItemIdentity = "another-page"; break;
                case "version": asset.SourceVersionIdentity = "version=other"; break;
                case "digest": asset.EvidenceDigest = new string('0', 64); break;
                case "provenance": asset.EvidenceReferences.Clear(); break;
                case "locator": asset.Label = "/another.png"; break;
                case "content": asset.HasContent = false; break;
                case "node-id": asset.Id += "-unobserved"; break;
                case "role": asset.SemanticRole = "direct-page-reference"; break;
                case "source-drift": snapshot.SourceFence.VersionLabel = "changed"; break;
                case "payload-drift": snapshot.Dependencies.Single().ContentSha256 = new string('f', 64); break;
            }
            Assert.ThrowsException<InvalidDataException>(() => PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, asset));
        }

        [TestMethod]
        public void HelperRetainsCatalogOwnershipAndUnknownFailClosedRules()
        {
            var snapshot = CreateClaimSnapshot();
            Assert.ThrowsException<InvalidDataException>(() => Project(snapshot, new SelectionHandler(lane: "resource.script")));
            Assert.ThrowsException<InvalidDataException>(() => Project(snapshot, new SelectionHandler(prefix: "asset:foreign:")));
            var handler = new SelectionHandler();
            snapshot.IngredientEvidence = new[] { Envelope(handler, "not-in-snapshot") };
            Assert.ThrowsException<InvalidDataException>(() => PublishingPageIngredientGraphProjector.Project(snapshot,
                new PublishingPageIngredientHandlerCatalog(new[] { handler })));
            snapshot.IngredientEvidence = new[] { Envelope(handler, snapshot.Dependencies.Single().Id) };
            Assert.ThrowsException<InvalidDataException>(() => PublishingPageIngredientGraphProjector.Project(snapshot,
                PublishingPageIngredientHandlerCatalog.Default));
        }

        [TestMethod]
        public void LegacyAndUnselectedV8ProjectionsDoNotAcquirePayloadNodes()
        {
            var snapshot = CreateClaimSnapshot();
            var legacy = PublishingPageIngredientGraphProjector.Project(snapshot);
            var extension = Project(snapshot, new SelectionHandler(projectAssets: false));
            Assert.AreEqual(PublishingPageIngredientGraphProjector.CurrentProjectionVersion, legacy.ProjectionVersion);
            Assert.AreEqual(PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion, extension.ProjectionVersion);
            Assert.IsFalse(legacy.Nodes.Any(value => value.Id == AssetId));
            Assert.IsFalse(extension.Nodes.Any(value => value.Id == AssetId));
            Assert.AreEqual(36, PublishingPageIngredientPrimaryOwnerRegistry.Default.Entries.Count);
        }

        [TestMethod]
        public void ExistingExportValidatorReprojectsSelectedAssetAndRejectsGraphTamper()
        {
            var snapshot = ExistingFixture<PublishingPageCaptureBundle>("CreateSnapshot");
            snapshot.Dependencies.Add(Reference(snapshot, "/sites/source/PublishingImages/andManifest.png", "img[src]"));
            var handler = new SelectionHandler();
            snapshot.IngredientGraph = Project(snapshot, handler);
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var selection = ExistingFixture<PublishingPageWorkflowSelection>("CreateSelection");
            var package = new PublishingPageExportPackage
            {
                SchemaVersion = PublishingPagePackageContract.IngredientExtensionExportSchemaVersion,
                ExportedAtUtc = new DateTimeOffset(2026, 9, 9, 19, 26, 17, TimeSpan.Zero),
                Selection = selection,
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(selection),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };
            var restored = PublishingPagePackageSerializer.Deserialize<PublishingPageExportPackage>(PublishingPagePackageSerializer.Serialize(package));
            PublishingPagePackageValidator.ValidateExport(restored, null, catalog);
            restored.Snapshot.IngredientGraph.Nodes.Single(value => value.Subtype == "asset.image").EvidenceDigest = new string('0', 64);
            restored.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(restored.Snapshot);
            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateExport(restored, null, catalog));
        }

        [TestMethod]
        public void GraphEvidenceDoesNotInventCopyActionOrMaturity()
        {
            var snapshot = CreateClaimSnapshot();
            var handler = new SelectionHandler();
            var graph = Project(snapshot, handler);
            var plan = ExistingFixture<PublishingPageMigrationPackage>("CreateMigrationPackage").Plan;
            var actions = PublishingPageIngredientActionProjector.Project(snapshot, plan, graph,
                new PublishingPageIngredientHandlerCatalog(new[] { handler }));
            var assetAction = actions.Single(value => value.IngredientId == AssetId);
            Assert.AreEqual(IngredientDisposition.Defer, assetAction.Disposition);
            Assert.AreEqual(IngredientCapability.Unknown, assetAction.Capability);
        }

        private static PublishingPageCaptureBundle CreateClaimSnapshot()
        {
            // Exact claimed page/file identity, ETag and PNG; the unrelated site,
            // runtime, layout and ASPX shell are hermetic test scaffolding, not
            // an asserted MSIT export. The ETag is an opaque test version token,
            // never a conversion to a SharePoint UI version such as "17.0".
            var snapshot = ExistingFixture<PublishingPageCaptureBundle>("CreateSnapshot");
            snapshot.Source.FileUniqueId = Guid.Parse(PageFileId);
            snapshot.Source.ListItemId = 240;
            snapshot.Source.PageServerRelativeUrl = PagePath;
            snapshot.Source.WebUrl = "https://microsoft.sharepoint.com/sites/DevCenter/Learn";
            snapshot.Source.WebServerRelativeUrl = "/sites/DevCenter/Learn";
            snapshot.Source.VersionLabel = PageETag;
            snapshot.Source.ModifiedUtc = new DateTime(2015, 2, 13, 1, 17, 53, DateTimeKind.Utc);
            snapshot.SourceFence.FileUniqueId = snapshot.Source.FileUniqueId;
            snapshot.SourceFence.VersionLabel = PageETag;
            snapshot.SourceFence.ModifiedUtc = snapshot.Source.ModifiedUtc;
            snapshot.PageArtifact.FileUniqueId = snapshot.Source.FileUniqueId;
            snapshot.PageArtifact.ServerRelativeUrl = PagePath;
            snapshot.PublishingPageContent = "<p><img src=\"" + AssetPath + "\"/><img src=\"" + AssetPath + "\"/></p>";
            snapshot.PublishingPageContentSha256 = PublishingPageDigest.ComputeSha256(snapshot.PublishingPageContent);
            snapshot.Dependencies.Add(Reference(snapshot, AssetPath, "img[src]"));
            return snapshot;
        }

        private static PageReferenceSnapshot Reference(PublishingPageCaptureBundle snapshot, string path, string consumer)
        {
            var absolute = new Uri(snapshot.Source.WebUrl).GetLeftPart(UriPartial.Authority) + path;
            var reference = new PageReferenceSnapshot
            {
                Id = MigrationDigest.ComputeSha256(consumer + "\n" + absolute),
                OriginalValue = path,
                SourceAbsoluteUrl = absolute,
                SourceServerRelativeUrl = path,
                Consumer = consumer,
                Kind = PageReferenceKind.Image,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.Captured
            };
            SetPayload(reference, ClaimedPng());
            return reference;
        }

        private static void SetPayload(PageReferenceSnapshot reference, byte[] bytes)
        {
            var artifact = MigrationArtifact.Describe(bytes);
            reference.ContentBase64 = Convert.ToBase64String(bytes);
            reference.ContentSha256 = artifact.Sha256;
            reference.ContentLength = artifact.Length;
        }

        private static void ChangePath(PageReferenceSnapshot reference, string path)
        {
            reference.SourceAbsoluteUrl = new Uri(reference.SourceAbsoluteUrl).GetLeftPart(UriPartial.Authority) + path;
            reference.SourceServerRelativeUrl = path;
            reference.OriginalValue = path;
            reference.Id = MigrationDigest.ComputeSha256(reference.Consumer + "\n" + reference.SourceAbsoluteUrl);
        }

        private static T ExistingFixture<T>(string name) => (T)typeof(EnterpriseWikiMigrationTests)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

        private static CanonicalPageIngredientGraph Project(PublishingPageCaptureBundle snapshot, SelectionHandler handler = null)
        {
            handler = handler ?? new SelectionHandler();
            snapshot.IngredientEvidence = new[] { Envelope(handler,
                snapshot.Dependencies.Select(value => value.Id).OrderBy(value => value, StringComparer.Ordinal).ToArray()) };
            return PublishingPageIngredientGraphProjector.Project(snapshot, new PublishingPageIngredientHandlerCatalog(new[] { handler }));
        }

        private static PublishingPageIngredientEvidenceEnvelope Envelope(SelectionHandler handler, params string[] ids) =>
            PublishingPageIngredientEvidenceEnvelope.Create(handler, "test.page-reference-selection/v1", "selected-files", new Selection { ReferenceIds = ids });

        private sealed class Selection
        {
            public string[] ReferenceIds { get; set; }
        }

        // Conformance adapter only. Product lane collection, validation, actions,
        // materialization, Compare and maturity contributors are intentionally absent.
        private sealed class SelectionHandler : PublishingPageIngredientHandler<Selection>
        {
            private readonly bool projectAssets;

            public SelectionHandler(string lane = "resource.image", string prefix = "asset:page-reference:", bool projectAssets = true)
            {
                this.projectAssets = projectAssets;
                Descriptor = new PageIngredientHandlerDescriptor(projectAssets ? "test.page-reference-assets/v1" : "test.no-assets/v1",
                    new PageIngredientLaneDescriptor(lane, new[] { "enterprise-wiki/v1" }),
                    new[] { "test.page-reference-selection/v1" }, PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion, 10,
                    new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix, prefix) });
            }

            public override PageIngredientHandlerDescriptor Descriptor { get; }

            protected override void ProjectGraph(PublishingPageIngredientGraphProjectionContext context,
                PublishingPageIngredientEvidenceEnvelope envelope, Selection evidence)
            {
                if (projectAssets)
                {
                    foreach (var referenceId in evidence.ReferenceIds)
                    {
                        context.AddPageReferencedAsset(referenceId);
                    }
                }
            }
        }
    }
}
