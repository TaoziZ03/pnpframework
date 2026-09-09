using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Topology.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class PublishingPageIngredientExtensionContractTests
    {
        [TestMethod]
        public void CatalogFreezesDeterministicOrder()
        {
            var zulu = CreateHandler("pnp.zulu/v1", 20, "zulu:");
            var alpha = CreateHandler("pnp.alpha/v1", 20, "alpha:");
            var first = CreateHandler("pnp.first/v1", 10, "first:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new PublishingPageIngredientHandler[]
            {
                zulu,
                alpha,
                first
            });

            CollectionAssert.AreEqual(
                new[] { "pnp.first/v1", "pnp.alpha/v1", "pnp.zulu/v1" },
                catalog.Handlers.Select(value => value.Descriptor.HandlerId).ToArray());
            Assert.ThrowsException<NotSupportedException>(() =>
                ((IList<PublishingPageIngredientHandler>)catalog.Handlers).Add(CreateHandler("pnp.late/v1", 30, "late:")));

            var orderedEvidence = catalog.OrderEvidence(new[]
            {
                PublishingPageIngredientEvidenceEnvelope.Create(zulu, TestHandler.EvidenceSchema, "z", new TestEvidence { NodeId = "zulu:z", Value = "z" }),
                PublishingPageIngredientEvidenceEnvelope.Create(alpha, TestHandler.EvidenceSchema, "a", new TestEvidence { NodeId = "alpha:a", Value = "a" }),
                PublishingPageIngredientEvidenceEnvelope.Create(first, TestHandler.EvidenceSchema, "f", new TestEvidence { NodeId = "first:f", Value = "f" })
            });
            CollectionAssert.AreEqual(
                new[] { first.Descriptor.HandlerId, alpha.Descriptor.HandlerId, zulu.Descriptor.HandlerId },
                orderedEvidence.Select(value => value.HandlerId).ToArray());
        }

        [TestMethod]
        public void CatalogRejectsDuplicateHandlerIdentity()
        {
            Assert.ThrowsException<ArgumentException>(() => new PublishingPageIngredientHandlerCatalog(new[]
            {
                CreateHandler("pnp.duplicate/v1", 10, "one:"),
                CreateHandler("pnp.duplicate/v1", 20, "two:")
            }));
        }

        [TestMethod]
        public void CatalogRejectsOverlappingExactAndPrefixOwnership()
        {
            var prefix = CreateHandler("pnp.prefix/v1", 10, "dynamic-region:");
            var exact = new TestHandler(new PageIngredientHandlerDescriptor(
                "pnp.exact/v1",
                Lane("lane.exact"),
                new[] { TestHandler.EvidenceSchema },
                PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                20,
                new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Exact, "dynamic-region:hero") }));

            Assert.ThrowsException<ArgumentException>(() =>
                new PublishingPageIngredientHandlerCatalog(new PublishingPageIngredientHandler[] { prefix, exact }));
        }

        [TestMethod]
        public void PrimaryOwnerRegistryCoversAllKindsAndRejectsUnboundUnknownAndOverlappingPredicates()
        {
            var registry = PublishingPageIngredientPrimaryOwnerRegistry.Default;
            Assert.AreEqual(36, registry.Entries.Count);
            CollectionAssert.AreEquivalent(
                Enum.GetValues(typeof(PageIngredientKind)).Cast<PageIngredientKind>().ToArray(),
                registry.Entries.Select(value => value.Kind).Distinct().ToArray());
            Assert.AreEqual(
                "resource.image",
                registry.Entries.Single(value => value.Id == "document.page-referenced").PrimaryOwnerLane);
            Assert.AreEqual(
                "shared.cross-site-repro-integration",
                registry.Entries.Single(value => value.Id == "document.list-closure").PrimaryOwnerLane);

            var entry = new PageIngredientPrimaryOwnerDescriptor(
                "test.owner",
                PageIngredientKind.Runtime,
                "runtime.test",
                "test-role",
                "predicate.test",
                "lane.test");
            Assert.ThrowsException<ArgumentException>(() =>
                new PublishingPageIngredientPrimaryOwnerRegistry(
                    new[] { entry },
                    new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>()));

            var predicates = new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>
            {
                ["predicate.one"] = _ => true
            };
            var overlapping = new PublishingPageIngredientPrimaryOwnerRegistry(
                new[]
                {
                    new PageIngredientPrimaryOwnerDescriptor("one", PageIngredientKind.Runtime, "runtime.*", "test-role", "predicate.one", "lane.one"),
                    new PageIngredientPrimaryOwnerDescriptor("two", PageIngredientKind.Runtime, "runtime.test", "test-role", "predicate.one", "lane.two")
                },
                predicates);
            var node = OwnershipNode("predicate.one");
            Assert.ThrowsException<InvalidDataException>(() => overlapping.Resolve(null, node));
            node.SourcePredicateId = "predicate.unknown";
            Assert.ThrowsException<InvalidDataException>(() => overlapping.Resolve(null, node));
        }

        [DataTestMethod]
        [DataRow("runtime.dynamic-region", "dynamic-region:missing-provider")]
        [DataRow("content.wiki-field", "content:missing-wiki-field")]
        [DataRow("asset.script", "asset:script:missing-binding")]
        [DataRow("webpart.classic-instance", "webpart:11111111-1111-1111-1111-111111111111")]
        [DataRow("document.page-referenced", "document:page-reference:missing-edge")]
        [DataRow("attachment.page-referenced", "attachment:page-reference:missing-edge")]
        public void DefaultOwnerRejectsClaimsWithoutBoundTypedSource(string entryId, string ingredientId)
        {
            var entry = PublishingPageIngredientPrimaryOwnerRegistry.Default.Entries
                .Single(value => value.Id == entryId);
            var node = new PageIngredientNode
            {
                Id = ingredientId,
                Kind = entry.Kind,
                KindId = PageIngredientKindIdentity.FromLegacyKind(entry.Kind),
                Subtype = entry.Subtype,
                SemanticRole = entry.SemanticRole,
                SourcePredicateId = entry.SourcePredicateId,
                SourcePageOrListItemIdentity = "unrelated/source/page",
                SourceVersionIdentity = "unrelated-version",
                PrimaryOwnerLane = entry.PrimaryOwnerLane,
                EvidenceDigest = "not-a-sha256-digest"
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(null, node));
        }

        [TestMethod]
        public void OwnerResolutionRejectsWrongSourceVersionSubtypeAndRole()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var snapshot = CreateValidProjectionSnapshot();
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            var node = PublishingPageIngredientGraphProjector.Project(
                    snapshot,
                    new PublishingPageIngredientHandlerCatalog(new[] { handler }))
                .Nodes.Single(value => value.Id == "dynamic-region:hero");

            node.SourcePageOrListItemIdentity += "/wrong";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, node));
            node.SourcePageOrListItemIdentity = PublishingPageIngredientSourceBinding.SourceIdentity(snapshot, node.Id);
            node.SourceVersionIdentity += "/wrong";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, node));
            node.SourceVersionIdentity = PublishingPageIngredientSourceBinding.SourceVersionIdentity(snapshot);
            node.Subtype = "runtime.page";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, node));
            node.Subtype = "runtime.dynamic-region";
            node.SemanticRole = "persisted-instance";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, node));
        }

        [TestMethod]
        public void BodyFieldCannotClaimGenericSchemaLane()
        {
            var snapshot = CreateValidProjectionSnapshot();
            var node = new PageIngredientNode
            {
                Id = "field:WikiField",
                Kind = PageIngredientKind.Field,
                KindId = PageIngredientKindIdentity.FromLegacyKind(PageIngredientKind.Field),
                Subtype = "field.generic-value-or-schema",
                SemanticRole = "non-body-field",
                SourcePredicateId = "field.non-body",
                SourcePageOrListItemIdentity = PublishingPageIngredientSourceBinding.SourceIdentity(snapshot, "field:WikiField"),
                SourceVersionIdentity = PublishingPageIngredientSourceBinding.SourceVersionIdentity(snapshot),
                PrimaryOwnerLane = "shared.pnp-framework",
                EvidenceDigest = PublishingPageDigest.ComputeSha256("unsupported generic WikiField claim")
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, node));
        }

        [TestMethod]
        public void DirectPageReferencesAndListClosureSelectDifferentOwners()
        {
            var snapshot = CreateValidProjectionSnapshot();
            var siteId = snapshot.Source.SiteId;
            var webId = snapshot.Source.WebId;
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var list = (ListDependencySnapshot)Fixture("CreateListSnapshot", siteId, webId, listId, "Documents");
            list.Items.Add(new PnP.Framework.Migration.Lists.Items.ListItemSnapshot
            {
                SourceItemId = 7,
                Document = new PnP.Framework.Migration.Lists.Items.ListDocumentSnapshot
                {
                    Name = "source.docx",
                    ServerRelativeUrl = "/sites/source/Documents/source.docx"
                },
                Attachments = new List<PnP.Framework.Migration.Lists.Items.ListAttachmentSnapshot>
                {
                    new PnP.Framework.Migration.Lists.Items.ListAttachmentSnapshot
                    {
                        FileName = "evidence.txt",
                        ServerRelativeUrl = "/sites/source/Lists/Documents/Attachments/7/evidence.txt"
                    }
                }
            });
            snapshot.ListDependencies = new List<ListDependencySnapshot> { list };

            var listDocument = OwnershipNode(
                snapshot,
                PublishingPageIngredientIds.ListDocument(webId, listId, 7),
                PageIngredientKind.Document,
                "document.list-closure",
                "generic-list-document",
                "document.list-item-member",
                "shared.cross-site-repro-integration");
            var listAttachment = OwnershipNode(
                snapshot,
                PublishingPageIngredientIds.ListAttachment(webId, listId, 7, "evidence.txt"),
                PageIngredientKind.Attachment,
                "attachment.list-closure",
                "generic-list-attachment",
                "attachment.list-item-member",
                "shared.cross-site-repro-integration");

            var directHandler = new TestHandler(new PageIngredientHandlerDescriptor(
                "pnp.direct-file/v1",
                Lane("resource.image"),
                new[] { TestHandler.EvidenceSchema },
                PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                10,
                new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix, "document:page-reference:") }));
            var attachmentHandler = new TestHandler(new PageIngredientHandlerDescriptor(
                "pnp.direct-attachment/v1",
                Lane("resource.image"),
                new[] { TestHandler.EvidenceSchema },
                PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                20,
                new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix, "attachment:page-reference:") }));
            snapshot.Dependencies.Add(new PageReferenceSnapshot
            {
                Id = "direct-file",
                Kind = PageReferenceKind.Anchor,
                Consumer = "PublishingPageContent",
                IsRenderableResource = true,
                ContentSha256 = PublishingPageDigest.ComputeSha256("direct file")
            });
            snapshot.Dependencies.Add(new PageReferenceSnapshot
            {
                Id = "direct-attachment",
                Kind = PageReferenceKind.Anchor,
                Consumer = "PublishingPageContent",
                IsRenderableResource = true,
                ContentSha256 = PublishingPageDigest.ComputeSha256("direct attachment")
            });
            var documentEnvelope = PublishingPageIngredientEvidenceEnvelope.Create(
                directHandler,
                TestHandler.EvidenceSchema,
                "direct-file",
                new TestEvidence { NodeId = "document:page-reference:direct-file", Value = "direct" },
                new[] { PublishingPageIngredientIds.Reference("direct-file") });
            var attachmentEnvelope = PublishingPageIngredientEvidenceEnvelope.Create(
                attachmentHandler,
                TestHandler.EvidenceSchema,
                "direct-attachment",
                new TestEvidence { NodeId = "attachment:page-reference:direct-attachment", Value = "direct" },
                new[] { PublishingPageIngredientIds.Reference("direct-attachment") });
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                documentEnvelope,
                attachmentEnvelope
            };
            var directDocument = new PageIngredientNode
            {
                Id = "document:page-reference:direct-file",
                Kind = PageIngredientKind.Document,
                KindId = PageIngredientKindIdentity.FromLegacyKind(PageIngredientKind.Document),
                Subtype = "document.page-referenced-file",
                SemanticRole = "direct-page-dependency",
                SourcePredicateId = "document.direct-page-reference",
                SourcePageOrListItemIdentity = PublishingPageIngredientSourceBinding.SourceIdentity(snapshot, "document:page-reference:direct-file"),
                SourceVersionIdentity = PublishingPageIngredientSourceBinding.SourceVersionIdentity(snapshot),
                PrimaryOwnerLane = "resource.image",
                EvidenceDigest = documentEnvelope.EvidenceDigest,
                EvidenceReferences = documentEnvelope.EvidenceReferences.ToList()
            };
            var directAttachment = new PageIngredientNode
            {
                Id = "attachment:page-reference:direct-attachment",
                Kind = PageIngredientKind.Attachment,
                KindId = PageIngredientKindIdentity.FromLegacyKind(PageIngredientKind.Attachment),
                Subtype = "attachment.page-referenced-file",
                SemanticRole = "direct-page-dependency",
                SourcePredicateId = "attachment.direct-page-reference",
                SourcePageOrListItemIdentity = PublishingPageIngredientSourceBinding.SourceIdentity(snapshot, "attachment:page-reference:direct-attachment"),
                SourceVersionIdentity = PublishingPageIngredientSourceBinding.SourceVersionIdentity(snapshot),
                PrimaryOwnerLane = "resource.image",
                EvidenceDigest = attachmentEnvelope.EvidenceDigest,
                EvidenceReferences = attachmentEnvelope.EvidenceReferences.ToList()
            };

            Assert.AreEqual("shared.cross-site-repro-integration",
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, listDocument).PrimaryOwnerLane);
            Assert.AreEqual("shared.cross-site-repro-integration",
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, listAttachment).PrimaryOwnerLane);
            Assert.AreEqual("resource.image",
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, directDocument).PrimaryOwnerLane);
            Assert.AreEqual("resource.image",
                PublishingPageIngredientPrimaryOwnerRegistry.Default.Resolve(snapshot, directAttachment).PrimaryOwnerLane);
        }

        [TestMethod]
        public void OwnerResolutionIsIndependentFromRegistryInputOrder()
        {
            var predicates = new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>
            {
                ["predicate.a"] = _ => true,
                ["predicate.b"] = _ => true
            };
            var first = new PageIngredientPrimaryOwnerDescriptor(
                "a", PageIngredientKind.Runtime, "runtime.a", "role-a", "predicate.a", "lane.a");
            var second = new PageIngredientPrimaryOwnerDescriptor(
                "b", PageIngredientKind.Runtime, "runtime.b", "role-b", "predicate.b", "lane.b");
            var node = new PageIngredientNode
            {
                Id = "runtime:a",
                Kind = PageIngredientKind.Runtime,
                Subtype = "runtime.a",
                SemanticRole = "role-a",
                SourcePredicateId = "predicate.a"
            };

            var forward = new PublishingPageIngredientPrimaryOwnerRegistry(new[] { first, second }, predicates);
            var reverse = new PublishingPageIngredientPrimaryOwnerRegistry(new[] { second, first }, predicates);

            Assert.AreEqual(forward.Resolve(null, node).Id, reverse.Resolve(null, node).Id);
        }

        [TestMethod]
        public void EvidenceEnvelopeRoundTripsInsideExportPackageAndBindsDigest()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var envelope = PublishingPageIngredientEvidenceEnvelope.Create(
                handler,
                TestHandler.EvidenceSchema,
                "hero",
                new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" },
                new[] { "artifact:b", "artifact:a", "artifact:a" });
            var package = new PublishingPageExportPackage
            {
                SchemaVersion = PublishingPagePackageContract.IngredientExtensionExportSchemaVersion,
                Snapshot = new PublishingPageCaptureBundle
                {
                    IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope> { envelope }
                }
            };

            var roundTrip = PublishingPagePackageSerializer.Deserialize<PublishingPageExportPackage>(
                PublishingPagePackageSerializer.Serialize(package));

            catalog.ValidateEvidence(roundTrip.Snapshot.IngredientEvidence);
            Assert.AreEqual(envelope.EvidenceDigest, roundTrip.Snapshot.IngredientEvidence.Single().EvidenceDigest);
            CollectionAssert.AreEqual(
                new[] { "artifact:a", "artifact:b" },
                roundTrip.Snapshot.IngredientEvidence.Single().EvidenceReferences.ToArray());
        }

        [TestMethod]
        public void EvidenceEnvelopeRejectsPayloadTamperEvenWhenPackageIsRedeserialized()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var envelope = PublishingPageIngredientEvidenceEnvelope.Create(
                handler,
                TestHandler.EvidenceSchema,
                "hero",
                new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" });
            using (var document = JsonDocument.Parse("{\"nodeId\":\"dynamic-region:hero\",\"value\":\"Tampered\"}"))
            {
                envelope.CanonicalPayload = document.RootElement.Clone();
            }

            Assert.ThrowsException<InvalidDataException>(() => catalog.ValidateEvidence(new[] { envelope }));
        }

        [TestMethod]
        public void Version4ExportPackageRoundTripsAndRejectsRedigestedEvidenceTamper()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var snapshot = CreateValidProjectionSnapshot();
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot, catalog);
            var selection = CreateValidSelection();
            var package = new PublishingPageExportPackage
            {
                SchemaVersion = PublishingPagePackageContract.IngredientExtensionExportSchemaVersion,
                ExportedAtUtc = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
                Selection = selection,
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(selection),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            var roundTrip = PublishingPagePackageSerializer.Deserialize<PublishingPageExportPackage>(
                PublishingPagePackageSerializer.Serialize(package));
            PublishingPagePackageValidator.ValidateExport(roundTrip, null, catalog);

            var envelope = roundTrip.Snapshot.IngredientEvidence.Single();
            using (var document = JsonDocument.Parse("{\"nodeId\":\"dynamic-region:hero\",\"value\":\"Tampered\"}"))
            {
                envelope.CanonicalPayload = document.RootElement.Clone();
            }
            roundTrip.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(roundTrip.Snapshot);

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateExport(roundTrip, null, catalog));
        }

        [TestMethod]
        public void Version4MigrationUsesCatalogForActionsFileStoreRoundTripAndTamperRejection()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var package = CreateValidMigrationPackage();
            package.Snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            package.SchemaVersion = PublishingPagePackageContract.IngredientExtensionMigrationSchemaVersion;
            package.ExportSchemaVersion = PublishingPagePackageContract.IngredientExtensionExportSchemaVersion;
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot, catalog);
            package.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.Plan.IngredientGraph = package.Snapshot.IngredientGraph;
            package.Plan.IngredientActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                package.Plan.IngredientGraph,
                catalog);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Plan.IngredientGraph,
                package.Plan.IngredientActions);
            package.Plan.MigrationOutcome = evaluation.Outcome;
            package.Plan.IngredientIssues = evaluation.Issues;
            package.Plan.ExecutionFrontier = evaluation.ExecutionFrontier;
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

            PublishingPagePackageValidator.ValidateMigration(package, null, catalog);
            var directory = Path.Combine(Path.GetTempPath(), "pnp-ccd165-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = PublishingPagePackageFileStore.SaveMigrationWithIngredientHandlers(
                    directory,
                    package,
                    catalog);
                var roundTrip = PublishingPagePackageFileStore.LoadMigrationWithIngredientHandlers(path, catalog);
                Assert.AreEqual(package.PlanDigest, roundTrip.PlanDigest);
                Assert.AreEqual(
                    IngredientCapability.Available,
                    roundTrip.Plan.IngredientActions.Single(value => value.IngredientId == "dynamic-region:hero").Capability);

                roundTrip.Snapshot.IngredientEvidence.Single().LaneId = "lane.tampered";
                roundTrip.Snapshot.IngredientEvidence.Single().EvidenceDigest =
                    PublishingPageIngredientEvidenceEnvelope.ComputeDigest(roundTrip.Snapshot.IngredientEvidence.Single());
                roundTrip.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(roundTrip.Snapshot);
                roundTrip.Plan.SourceSnapshotDigest = roundTrip.SnapshotDigest;
                roundTrip.PlanDigest = PublishingPageDigest.ComputePlanDigest(roundTrip.Plan);
                Assert.ThrowsException<InvalidDataException>(() =>
                    PublishingPagePackageValidator.ValidateMigration(roundTrip, null, catalog));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ExistingNullArtifactStoreCallsRemainSourceCompatible()
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateExport((PublishingPageExportPackage)null, null));
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateMigration((PublishingPageMigrationPackage)null, null));
        }

        [TestMethod]
        public void EvidenceEnvelopeRejectsUnknownHandlerAndSchema()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var envelope = PublishingPageIngredientEvidenceEnvelope.Create(
                handler,
                TestHandler.EvidenceSchema,
                "hero",
                new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" });

            envelope.HandlerId = "pnp.unknown/v1";
            Assert.ThrowsException<InvalidDataException>(() => catalog.ValidateEvidence(new[] { envelope }));

            envelope.HandlerId = handler.Descriptor.HandlerId;
            envelope.EvidenceSchemaVersion = "pnp-test-evidence/v99";
            Assert.ThrowsException<InvalidDataException>(() => catalog.ValidateEvidence(new[] { envelope }));
        }

        [TestMethod]
        public void GraphV2CarriesStableKindIdentityWithoutChangingLegacyEnum()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var snapshot = CreateOwnershipProjectionSnapshot();
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };

            var graph = PublishingPageIngredientGraphProjector.Project(snapshot, catalog);
            var custom = graph.Nodes.Single(value => value.Id == "dynamic-region:hero");
            var builtIn = graph.Nodes.Single(value => value.Id == PublishingPageIngredientIds.PublishingContent);

            Assert.AreEqual(CanonicalPageIngredientGraph.SchemaVersionV2, graph.SchemaVersion);
            Assert.AreEqual(PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion, graph.ProjectionVersion);
            Assert.AreEqual(PageIngredientKind.Runtime, custom.Kind);
            Assert.AreEqual("pnp.runtime", custom.KindId);
            Assert.AreEqual("runtime.dynamic-region", custom.Subtype);
            Assert.AreEqual("provider-derived-runtime-region", custom.SemanticRole);
            Assert.AreEqual("dynamic.region", custom.PrimaryOwnerLane);
            Assert.AreEqual(PageIngredientKind.Content, builtIn.Kind);
            Assert.AreEqual("pnp.content", builtIn.KindId);
        }

        [TestMethod]
        public void HandlerCatalogProjectsActionsAndAssessmentThroughSharedSeams()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var package = CreateValidMigrationPackage();
            package.Snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            var graph = PublishingPageIngredientGraphProjector.Project(package.Snapshot, catalog);

            var actions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                graph,
                catalog);
            var accumulator = new PublishingPageAssessmentAccumulator(graph);
            catalog.ContributeAssessment(package.Snapshot, graph, accumulator);
            var assessments = accumulator.Complete();

            Assert.AreEqual(
                IngredientCapability.Available,
                actions.Single(value => value.IngredientId == "dynamic-region:hero").Capability);
            var assessment = assessments.Single(value => value.IngredientId == "dynamic-region:hero");
            Assert.AreEqual(PageIngredientAssessmentState.Determined, assessment.State);
            Assert.AreEqual("policy.test-handler", assessment.PolicyId);
        }

        [TestMethod]
        public void UnsupportedPageFamilyFailsClosedBeforeHandlerDispatch()
        {
            var handler = new TestHandler(new PageIngredientHandlerDescriptor(
                "pnp.unsupported-family/v1",
                new PageIngredientLaneDescriptor("dynamic.region", new[] { "unrelated-family/v99" }),
                new[] { TestHandler.EvidenceSchema },
                PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                10,
                new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix, "dynamic-region:") }));
            var snapshot = CreateValidProjectionSnapshot();
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientGraphProjector.Project(
                    snapshot,
                    new PublishingPageIngredientHandlerCatalog(new[] { handler })));
        }

        [TestMethod]
        public void AssessmentContributorRejectsForeignIngredient()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            handler.AddForeignAssessment = true;
            var snapshot = CreateValidProjectionSnapshot();
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var graph = PublishingPageIngredientGraphProjector.Project(snapshot, catalog);

            Assert.ThrowsException<InvalidDataException>(() => catalog.ContributeAssessment(
                snapshot,
                graph,
                new PublishingPageAssessmentAccumulator(graph)));
        }

        [TestMethod]
        public void ActionContributorReceivesAnIsolatedReadOnlySourceView()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            handler.MutateSource = true;
            var package = CreateValidMigrationPackage();
            package.Snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var graph = PublishingPageIngredientGraphProjector.Project(package.Snapshot, catalog);
            var before = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot);

            PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                graph,
                catalog);

            Assert.AreEqual(before, PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot));
        }

        [TestMethod]
        public void ExtensionV8KeepsIndependentListTransactionSemantics()
        {
            var package = CreateValidMigrationPackage();
            var snapshot = package.Snapshot;
            var siteId = snapshot.Source.SiteId;
            var webId = snapshot.Source.WebId;
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var list = (ListDependencySnapshot)Fixture("CreateListSnapshot", siteId, webId, listId, "Items");
            snapshot.ListDependencies = new List<ListDependencySnapshot> { list };
            package.Plan.ListMigration = ListMigrationPlanFactory.Create(
                snapshot.ListDependencies,
                null,
                (TopologyPlan)Fixture("CreateTopology", siteId, webId),
                null,
                null);
            package.Plan.ListMigration.Lists.Single().Issues.Add(new MigrationIssue
            {
                Code = "UnsupportedFieldType",
                Severity = MigrationIssueSeverity.Blocker,
                Message = "A child field gap must not disable the independent List object transaction."
            });
            var listIngredientId = PublishingPageIngredientIds.List(webId, listId);
            var legacyGraph = PublishingPageIngredientGraphProjector.Project(snapshot);
            var legacyAction = PublishingPageIngredientActionProjector.Project(snapshot, package.Plan, legacyGraph)
                .Single(value => value.IngredientId == listIngredientId);

            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var extensionGraph = PublishingPageIngredientGraphProjector.Project(snapshot, catalog);
            var extensionAction = PublishingPageIngredientActionProjector.Project(
                    snapshot,
                    package.Plan,
                    extensionGraph,
                    catalog)
                .Single(value => value.IngredientId == listIngredientId);

            Assert.AreEqual(IngredientCapability.Available, legacyAction.Capability);
            Assert.AreEqual(legacyAction.Capability, extensionAction.Capability);
            Assert.AreEqual(legacyAction.Disposition, extensionAction.Disposition);
        }

        [TestMethod]
        public void ProductionSelectionPreservesCombinedExtensionAndPathDerivedGraph()
        {
            var topology = CreateSharedTopologyReference();
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var snapshot = CreateValidProjectionSnapshot();
            snapshot.PathDerivedTopologyEvidence = topology.Evidence;
            snapshot.SourceTopology = null;
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot, catalog);
            var snapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot);
            var dependencyGraph = PublishingPagePathDerivedTopologyIngredientGraphProjector.Project(
                snapshot,
                topology.Plan,
                topology.Reference);
            var dependencyPlan = new PublishingPageDependencyPlan
            {
                SharedTopologyReference = topology.Reference,
                IngredientGraph = dependencyGraph
            };

            var selected = new PublishingPageMigrationPlanner(catalog)
                .SelectPlanningIngredientGraph(snapshot, dependencyPlan);
            var plan = new PublishingPageMigrationPlan
            {
                SharedTopologyReference = topology.Reference,
                IngredientGraph = selected
            };

            Assert.AreSame(dependencyGraph, selected);
            Assert.AreEqual(
                PublishingPagePathDerivedTopologyIngredientGraphProjector.ProjectionVersion,
                selected.ProjectionVersion);
            Assert.IsTrue(selected.Nodes.Any(value => value.Id == "dynamic-region:hero"));
            Assert.IsTrue(selected.ExternalReferences.Any());
            Assert.IsTrue(selected.Edges.Any(value =>
                value.ToIngredientId == topology.Reference.TargetLeafContainerIngredientId
                && value.Requirement == PageIngredientRequirement.Required));
            PublishingPageMigrationPackageValidator.ValidatePathDerivedPlanningIngredientGraph(snapshot, plan);
            Assert.AreEqual(snapshotDigest, PublishingPageDigest.ComputeSnapshotDigest(snapshot));
        }

        [TestMethod]
        public void PathDerivedGraphV2PreservesLegacyAndExtensionNodeIdentityAndSnapshotDigest()
        {
            var topology = CreateSharedTopologyReference();
            var legacySnapshot = CreateValidProjectionSnapshot();
            var legacyDigest = PublishingPageDigest.ComputeSnapshotDigest(legacySnapshot);
            var legacyGraph = PublishingPagePathDerivedTopologyIngredientGraphProjector.Project(
                legacySnapshot,
                topology.Plan,
                topology.Reference);

            Assert.AreEqual(CanonicalPageIngredientGraph.SchemaVersionV2, legacyGraph.SchemaVersion);
            Assert.AreEqual(
                PublishingPagePathDerivedTopologyIngredientGraphProjector.ProjectionVersion,
                legacyGraph.ProjectionVersion);
            Assert.IsTrue(legacyGraph.Nodes.All(value => string.IsNullOrWhiteSpace(value.KindId)));
            Assert.AreEqual(legacyDigest, PublishingPageDigest.ComputeSnapshotDigest(legacySnapshot));

            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var extensionSnapshot = CreateValidProjectionSnapshot();
            extensionSnapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            extensionSnapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(extensionSnapshot, catalog);
            var extensionDigest = PublishingPageDigest.ComputeSnapshotDigest(extensionSnapshot);
            var extensionGraph = PublishingPagePathDerivedTopologyIngredientGraphProjector.Project(
                extensionSnapshot,
                topology.Plan,
                topology.Reference);
            var sourceNode = extensionSnapshot.IngredientGraph.Nodes.Single(value => value.Id == "dynamic-region:hero");
            var projectedNode = extensionGraph.Nodes.Single(value => value.Id == sourceNode.Id);

            Assert.AreEqual(CanonicalPageIngredientGraph.SchemaVersionV2, extensionGraph.SchemaVersion);
            Assert.AreEqual(
                PublishingPagePathDerivedTopologyIngredientGraphProjector.ProjectionVersion,
                extensionGraph.ProjectionVersion);
            Assert.AreEqual(sourceNode.KindId, projectedNode.KindId);
            Assert.AreEqual(sourceNode.Subtype, projectedNode.Subtype);
            Assert.AreEqual(sourceNode.SemanticRole, projectedNode.SemanticRole);
            Assert.AreEqual(sourceNode.SourcePredicateId, projectedNode.SourcePredicateId);
            Assert.AreEqual(sourceNode.SourcePageOrListItemIdentity, projectedNode.SourcePageOrListItemIdentity);
            Assert.AreEqual(sourceNode.SourceVersionIdentity, projectedNode.SourceVersionIdentity);
            Assert.AreEqual(sourceNode.PrimaryOwnerLane, projectedNode.PrimaryOwnerLane);
            Assert.AreEqual(sourceNode.EvidenceDigest, projectedNode.EvidenceDigest);
            Assert.AreEqual(extensionDigest, PublishingPageDigest.ComputeSnapshotDigest(extensionSnapshot));
        }

        [TestMethod]
        public void ExtensionGraphRejectsPathDerivedDiscriminatorAfterRedigest()
        {
            var handler = CreateHandler("pnp.dynamic-region/v1", 10, "dynamic-region:");
            var catalog = new PublishingPageIngredientHandlerCatalog(new[] { handler });
            var snapshot = CreateValidProjectionSnapshot();
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    handler,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot, catalog);
            snapshot.IngredientGraph.ProjectionVersion =
                PublishingPagePathDerivedTopologyIngredientGraphProjector.ProjectionVersion;
            var selection = CreateValidSelection();
            var package = new PublishingPageExportPackage
            {
                SchemaVersion = PublishingPagePackageContract.IngredientExtensionExportSchemaVersion,
                ExportedAtUtc = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
                Selection = selection,
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(selection),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateExport(package, null, catalog));
        }

        [TestMethod]
        public void ExtensionProjectionRejectsDuplicateBuiltInNodeAndDisconnectedEdge()
        {
            var duplicate = CreateHandler("pnp.duplicate-node/v1", 10, "content:");
            var snapshot = CreateOwnershipProjectionSnapshot();
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    duplicate,
                    TestHandler.EvidenceSchema,
                    "duplicate",
                    new TestEvidence { NodeId = PublishingPageIngredientIds.PublishingContent, Value = "duplicate" })
            };
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientGraphProjector.Project(
                    snapshot,
                    new PublishingPageIngredientHandlerCatalog(new[] { duplicate })));

            var disconnected = CreateHandler("pnp.disconnected/v1", 10, "dynamic-region:", "missing:dependency");
            snapshot.IngredientEvidence = new List<PublishingPageIngredientEvidenceEnvelope>
            {
                PublishingPageIngredientEvidenceEnvelope.Create(
                    disconnected,
                    TestHandler.EvidenceSchema,
                    "hero",
                    new TestEvidence { NodeId = "dynamic-region:hero", Value = "Welcome" })
            };
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientGraphProjector.Project(
                    snapshot,
                    new PublishingPageIngredientHandlerCatalog(new[] { disconnected })));
        }

        [TestMethod]
        public void HistoricalProjectionDispatchV2ThroughV7MatchesGoldenDigests()
        {
            var snapshot = CreateProjectionSnapshot();
            var versions = new[]
            {
                PublishingPageIngredientGraphProjector.ProjectionVersionV2,
                PublishingPageIngredientGraphProjector.ProjectionVersionV3,
                PublishingPageIngredientGraphProjector.ProjectionVersionV4,
                PublishingPageIngredientGraphProjector.ProjectionVersionV5,
                PublishingPageIngredientGraphProjector.ProjectionVersionV6,
                PublishingPageIngredientGraphProjector.CurrentProjectionVersion
            };
            var actual = versions.Select(version => PublishingPageDigest.ComputeSha256(
                PublishingPagePackageSerializer.SerializeCanonical(
                    PublishingPageIngredientGraphProjector.ProjectForVersion(snapshot, version)))).ToArray();

            CollectionAssert.AreEqual(new[]
            {
                "1024ae3d520d38549878d1668e9d388f827da4a4aea897909184b365596781f4",
                "da4a3f6c73dd727128bb858ce0c395af0ce90e30e55c18e2a0e858fb221cfb0b",
                "e6f6ae3575b6f67c8120659f35eb6ba15e213689fe6ebfa2e026667e30610ce0",
                "a544487ebe0d30ce914ba202993f86297785c2ab08eca12a182eb82c95eac70e",
                "fe393984961f265ab1fc7ab4e6b9feb48e1cf84907e03c2faa154f1f31313cec",
                "8a5d492b6be7ff11a80603b4c576e0d5a09a094436fa9d54a346fcf0628dfd64"
            }, actual);
        }

        [TestMethod]
        public void LegacyGraphSerializationDoesNotGainExtensionProperties()
        {
            var graph = PublishingPageIngredientGraphProjector.ProjectVersion2(CreateProjectionSnapshot());
            var json = PublishingPagePackageSerializer.SerializeCanonical(graph);

            StringAssert.Contains(json, "\"schemaVersion\":\"pnp-page-ingredient-graph/v1\"");
            Assert.IsFalse(json.Contains("\"kindId\"", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("\"ingredientEvidence\"", StringComparison.Ordinal));
        }

        private static TestHandler CreateHandler(
            string handlerId,
            int orderGroup,
            string ownedPrefix,
            string dependencyId = null)
        {
            return new TestHandler(
                new PageIngredientHandlerDescriptor(
                    handlerId,
                    Lane(ownedPrefix.StartsWith("dynamic-region:", StringComparison.Ordinal)
                        ? "dynamic.region"
                        : "lane." + handlerId),
                    new[] { TestHandler.EvidenceSchema },
                    PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                    orderGroup,
                    new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix, ownedPrefix) }),
                dependencyId);
        }

        private static PageIngredientLaneDescriptor Lane(string laneId)
        {
            return new PageIngredientLaneDescriptor(laneId, new[] { "enterprise-wiki/v1" });
        }

        private static PublishingPageCaptureBundle CreateProjectionSnapshot()
        {
            return new PublishingPageCaptureBundle
            {
                Source = new PageIdentity(),
                PublishingPageContent = "<p>source</p>",
                PublishingPageContentSha256 = PublishingPageDigest.ComputeSha256("<p>source</p>")
            };
        }

        private static PublishingPageCaptureBundle CreateOwnershipProjectionSnapshot()
        {
            return CreateValidProjectionSnapshot();
        }

        private static PublishingPageCaptureBundle CreateValidProjectionSnapshot()
        {
            return (PublishingPageCaptureBundle)typeof(EnterpriseWikiMigrationTests)
                .GetMethod("CreateSnapshot", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
        }

        private static PublishingPageWorkflowSelection CreateValidSelection()
        {
            return (PublishingPageWorkflowSelection)typeof(EnterpriseWikiMigrationTests)
                .GetMethod("CreateSelection", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
        }

        private static PublishingPageMigrationPackage CreateValidMigrationPackage()
        {
            return (PublishingPageMigrationPackage)typeof(EnterpriseWikiMigrationTests)
                .GetMethod("CreateMigrationPackage", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
        }

        private static (SharedTopologyPlan Plan, SharedTopologyPageReference Reference, PathDerivedSourceTopologyEvidence Evidence) CreateSharedTopologyReference()
        {
            var testType = typeof(PathDerivedSharedTopologyTests);
            var createEvidence = testType.GetMethod("CreateEvidence", BindingFlags.NonPublic | BindingFlags.Static);
            var evidenceArguments = createEvidence.GetParameters().Select(_ => Type.Missing).Cast<object>().ToArray();
            evidenceArguments[0] = "groups/engineering/guides";
            var evidence = (PathDerivedSourceTopologyEvidence)createEvidence.Invoke(null, evidenceArguments);
            var buildPlan = testType.GetMethod("BuildPlan", BindingFlags.NonPublic | BindingFlags.Static);
            var arguments = buildPlan.GetParameters().Select(_ => Type.Missing).Cast<object>().ToArray();
            arguments[0] = "groups/engineering/guides";
            arguments[arguments.Length - 1] = evidence;
            var plan = (SharedTopologyPlan)buildPlan.Invoke(null, arguments);
            var execution = testType
                .GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { plan });
            var executionType = execution.GetType();
            var dag = (SharedTopologyGlobalActionDag)executionType.GetProperty("Dag").GetValue(execution);
            var actionPlan = (SharedTopologyGlobalActionPlan)executionType.GetProperty("ActionPlan").GetValue(execution);
            var leaf = plan.TargetWebContainers.Single(value =>
                !plan.TargetWebContainers.Any(candidate => string.Equals(
                    candidate.ParentLogicalActionKey,
                    value.LogicalActionKey,
                    StringComparison.Ordinal)));
            var binding = plan.SourceWebBindings.Single(value =>
                string.Equals(value.TargetLogicalActionKey, leaf.LogicalActionKey, StringComparison.Ordinal));
            return (
                plan,
                SharedTopologyPageReferenceFactory.Create(
                    plan,
                    dag,
                    actionPlan,
                    binding.SourceSiteId,
                    binding.SourceWebId),
                evidence);
        }

        private static object Fixture(string name, params object[] arguments)
        {
            return typeof(EnterpriseWikiMigrationTests)
                .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, arguments);
        }

        private static PageIngredientNode OwnershipNode(
            PublishingPageCaptureBundle snapshot,
            string id,
            PageIngredientKind kind,
            string subtype,
            string role,
            string predicate,
            string lane)
        {
            var node = new PageIngredientNode
            {
                Id = id,
                Kind = kind,
                KindId = PageIngredientKindIdentity.FromLegacyKind(kind),
                Subtype = subtype,
                SemanticRole = role,
                SourcePredicateId = predicate,
                SourcePageOrListItemIdentity = PublishingPageIngredientSourceBinding.SourceIdentity(snapshot, id),
                SourceVersionIdentity = PublishingPageIngredientSourceBinding.SourceVersionIdentity(snapshot),
                PrimaryOwnerLane = lane
            };
            node.EvidenceDigest = PublishingPageIngredientSourceBinding.BindBuiltInEvidence(snapshot, node);
            return node;
        }

        private static PageIngredientNode OwnershipNode(string predicateId)
        {
            return new PageIngredientNode
            {
                Id = "runtime:test",
                Kind = PageIngredientKind.Runtime,
                KindId = "pnp.runtime",
                Subtype = "runtime.test",
                SemanticRole = "test-role",
                SourcePredicateId = predicateId,
                SourcePageOrListItemIdentity = "source/page",
                SourceVersionIdentity = "version=1"
            };
        }

        private sealed class TestEvidence
        {
            public string NodeId { get; set; }
            public string Value { get; set; }
        }

        private sealed class TestHandler : PublishingPageIngredientHandler<TestEvidence>
        {
            public const string EvidenceSchema = "pnp-test-evidence/v1";
            private readonly PageIngredientHandlerDescriptor descriptor;
            private readonly string dependencyId;

            public TestHandler(PageIngredientHandlerDescriptor descriptor, string dependencyId = null)
            {
                this.descriptor = descriptor;
                this.dependencyId = dependencyId;
            }

            public override PageIngredientHandlerDescriptor Descriptor => descriptor;

            public bool AddForeignAssessment { get; set; }

            public bool MutateSource { get; set; }

            protected override void ValidateEvidence(TestEvidence evidence)
            {
                if (string.IsNullOrWhiteSpace(evidence.NodeId) || string.IsNullOrWhiteSpace(evidence.Value))
                {
                    throw new InvalidDataException("Test evidence is incomplete.");
                }
            }

            protected override void ProjectGraph(
                PublishingPageIngredientGraphProjectionContext context,
                PublishingPageIngredientEvidenceEnvelope envelope,
                TestEvidence evidence)
            {
                context.AddNode(new PageIngredientNode
                {
                    Id = evidence.NodeId,
                    Kind = PageIngredientKind.Runtime,
                    KindId = "pnp.runtime",
                    Subtype = "runtime.dynamic-region",
                    SemanticRole = "provider-derived-runtime-region",
                    SourcePredicateId = "runtime.dynamic-region.typed-provider-binding",
                    SourcePageOrListItemIdentity = context.SourceIdentity(evidence.NodeId),
                    SourceVersionIdentity = context.SourceVersionIdentity,
                    PrimaryOwnerLane = "dynamic.region",
                    Label = evidence.Value,
                    HasContent = true,
                    Ownership = PageIngredientOwnership.SourceOwned,
                    SourceAuthority = "Typed test evidence",
                    EvidenceDigest = context.EvidenceDigest,
                    EvidenceReferences = context.EvidenceReferences.ToList()
                });
                if (!string.IsNullOrWhiteSpace(dependencyId))
                {
                    context.AddEdge(new PageIngredientEdge
                    {
                        FromIngredientId = evidence.NodeId,
                        ToIngredientId = dependencyId,
                        Relationship = PageIngredientRelationship.DependsOn,
                        Requirement = PageIngredientRequirement.Required
                    });
                }
            }

            protected override void ProjectActions(
                PublishingPageIngredientActionProjectionContext context,
                PublishingPageIngredientEvidenceEnvelope envelope,
                TestEvidence evidence)
            {
                if (MutateSource)
                {
                    context.Snapshot.PublishingPageContent = "mutated outside the owning lane";
                }
                context.AddAction(new PageIngredientAction
                {
                    ActionId = "action:" + evidence.NodeId,
                    IngredientId = evidence.NodeId,
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Preserve,
                    Realization = "test-handler",
                    PolicyId = "policy.test-handler",
                    PolicyVersion = "1",
                    Reason = "Typed test handler action."
                });
            }

            protected override void ContributeAssessment(
                PublishingPageIngredientAssessmentContext context,
                PublishingPageIngredientEvidenceEnvelope envelope,
                TestEvidence evidence)
            {
                context.AddAssessment(
                    AddForeignAssessment ? PublishingPageIngredientIds.PublishingContent : evidence.NodeId,
                    PageIngredientAssessmentState.Determined,
                    IngredientCapability.Available,
                    IngredientDisposition.Preserve,
                    "test-handler",
                    "policy.test-handler",
                    "Typed test handler assessment.");
            }
        }
    }
}
