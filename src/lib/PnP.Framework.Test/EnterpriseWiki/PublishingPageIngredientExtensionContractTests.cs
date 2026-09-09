using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
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
                    SourcePageOrListItemIdentity = "source/page/" + evidence.NodeId,
                    SourceVersionIdentity = "version=1",
                    PrimaryOwnerLane = "dynamic.region",
                    Label = evidence.Value,
                    HasContent = true,
                    Ownership = PageIngredientOwnership.SourceOwned,
                    SourceAuthority = "Typed test evidence",
                    EvidenceDigest = envelope.EvidenceDigest,
                    EvidenceReferences = envelope.EvidenceReferences.ToList()
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
        }
    }
}
