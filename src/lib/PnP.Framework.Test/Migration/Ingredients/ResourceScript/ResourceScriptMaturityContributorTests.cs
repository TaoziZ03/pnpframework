using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Ingredients.ResourceScript;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWebParts.Bindings;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Layouts;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace PnP.Framework.Test.Migration.Ingredients.ResourceScript
{
    [TestClass]
    public class ResourceScriptMaturityContributorTests
    {
        [TestMethod]
        public void HermeticFixturePassesM0AndM2ButStopsBeforeFreshTargetReadback()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.SourceBinding, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
            AssertGate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void ExactFreshSourceAndTargetObservationsReachM2Continuously()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PerValueObservation, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void ContributorEmitsReceiptsAndNeverAssignsAttainedMaturity()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var contribution = fixture.Contribute();

            Assert.IsFalse(contribution.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Any(value => string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(ResourceScriptEvidenceNormalizer.ContributorId, contribution.ContributorId);
            Assert.AreEqual(ResourceScriptEvidenceNormalizer.Lane, contribution.Lane);
            Assert.IsTrue(contribution.GateReceipts.Count > 0);
        }

        [TestMethod]
        public void DirectJsLinkAndEmbeddedCompanionRemainDistinct()
        {
            var fixture = Fixture.Create();

            Assert.AreEqual("~Site/SiteAssets/canary.js", fixture.Evidence.Source.PersistedPropertyValue);
            Assert.AreEqual("~Site/SiteAssets/canary.js", fixture.Evidence.Source.Reference.OriginalValue);
            StringAssert.Contains(
                fixture.Evidence.Snapshot.WebParts.Single().PropertyEvidenceJson,
                "<JSLink>sp.ui.blogs.js</JSLink>");
            Assert.IsFalse(fixture.Evidence.Source.PersistedPropertyValue.Contains("sp.ui.blogs.js", StringComparison.Ordinal));
        }

        [TestMethod]
        public void WrongSubtypeFailsClosedInSharedDomainProjection()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.Subtype = "reference.script";

            Assert.ThrowsException<InvalidDataException>(() => fixture.RefreshNormalization());
        }

        [TestMethod]
        public void WrongZoneIndexFailsClosedInsteadOfUsingArrayOrder()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.HostWebPartOrder = 0;

            Assert.ThrowsException<InvalidDataException>(() => fixture.RefreshNormalization());
        }

        [TestMethod]
        public void StaleOpaqueSourceETagFailsClosed()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.SourcePageETag = "\"{11111111-1111-4111-8111-111111111111},5\"";

            Assert.ThrowsException<InvalidDataException>(() => fixture.RefreshNormalization());
        }

        [TestMethod]
        public void TamperedScriptBytesFailM2WithoutChangingM0Identity()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.ScriptContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("window.tampered = true;"));
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M1, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void HistoricalObservationCannotSubstituteForFreshCollect()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations(IngredientObservationOrigin.Historical);
            fixture.AddTargetObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect, IngredientMaturityGateStatus.Failed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PerValueObservation, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void TargetObservationOlderThanSourceFailsFreshReadback()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations(Fixture.SourceObservedAt.AddSeconds(-1));

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void WrongTargetContentDigestFailsFreshReadback()
        {
            var fixture = Fixture.Create();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            fixture.Evidence.Live.Observations.Single(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                && string.Equals(value.ValuePath, "content.raw", StringComparison.Ordinal)).ValueDigest = new string('f', 64);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void LiteralAccessDenialEvidenceIsRejectedByPositiveContributor()
        {
            var fixture = Fixture.Create();
            fixture.Evidence.Source.Reference.CaptureStatus = PageCaptureStatus.Failed;

            Assert.ThrowsException<InvalidDataException>(() => fixture.RefreshNormalization());
        }

        [TestMethod]
        public void ComposedCatalogPreservesResourceScriptAndForeignEvidenceAcrossValidationProjectionAndPlanning()
        {
            var package = CreateValidMigrationPackage();
            var resourceFixture = Fixture.Create();
            var resourceHandler = new ResourceScriptIdentityHandler();
            var foreignHandler = new ForeignIdentityHandler();
            var catalog = new PublishingPageIngredientHandlerCatalog(new PublishingPageIngredientHandler[]
            {
                resourceHandler,
                foreignHandler
            });
            var resourceEnvelope = AddResourceScriptEvidence(package.Snapshot, resourceFixture, resourceHandler);
            package.Plan.WebPartActions.Add(new ClassicWebPartAction
            {
                SourceWebPartId = Guid.Parse("77777777-7777-4777-8777-777777777777"),
                Disposition = ClassicWebPartDisposition.Block,
                Reason = "REST property evidence is not a native replay export."
            });
            var foreignEnvelope = PublishingPageIngredientEvidenceEnvelope.Create(
                foreignHandler,
                ForeignIdentityHandler.EvidenceSchema,
                "foreign",
                new ForeignEvidence { NodeId = "dynamic-region:foreign", Value = "foreign-envelope" },
                new[] { "fixture:foreign-envelope" });
            package.Snapshot.IngredientEvidence = catalog.OrderEvidence(new[]
            {
                resourceEnvelope,
                foreignEnvelope
            }).ToList();

            var graph = PublishingPageIngredientGraphProjector.Project(package.Snapshot, catalog);
            package.Snapshot.IngredientGraph = graph;
            package.SchemaVersion = PublishingPagePackageContract.IngredientExtensionMigrationSchemaVersion;
            package.ExportSchemaVersion = PublishingPagePackageContract.IngredientExtensionExportSchemaVersion;
            package.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.Plan.IngredientGraph = graph;
            package.Plan.IngredientActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                graph,
                catalog);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, package.Plan.IngredientActions);
            package.Plan.MigrationOutcome = evaluation.Outcome;
            package.Plan.IngredientIssues = evaluation.Issues;
            package.Plan.ExecutionFrontier = evaluation.ExecutionFrontier;
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

            PublishingPagePackageValidator.ValidateMigration(package, null, catalog);
            var roundTrip = PublishingPagePackageSerializer.Deserialize<PublishingPageMigrationPackage>(
                PublishingPagePackageSerializer.Serialize(package));
            PublishingPagePackageValidator.ValidateMigration(roundTrip, null, catalog);

            CollectionAssert.AreEqual(
                package.Snapshot.IngredientEvidence.Select(value => value.HandlerId).ToArray(),
                roundTrip.Snapshot.IngredientEvidence.Select(value => value.HandlerId).ToArray());
            Assert.AreEqual(1, graph.Nodes.Count(value => value.Id == resourceEnvelope.IngredientKey));
            Assert.AreEqual(1, graph.Nodes.Count(value => value.Id == "dynamic-region:foreign"));
            Assert.AreEqual(1, package.Plan.IngredientActions.Count(value => value.IngredientId == resourceEnvelope.IngredientKey));
            Assert.AreEqual(1, package.Plan.IngredientActions.Count(value => value.IngredientId == "dynamic-region:foreign"));

            var planner = new PublishingPageMigrationPlanner(catalog);
            var plannerCatalog = (PublishingPageIngredientHandlerCatalog)typeof(PublishingPageMigrationPlanner)
                .GetField("handlerCatalog", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(planner);
            Assert.AreSame(catalog, plannerCatalog);

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateMigration(roundTrip, null, ResourceScriptIngredientCatalog.Create()));
        }

        [TestMethod]
        public void ComposedCatalogStillRejectsUnknownDuplicateAndOverlappingEvidenceOwnership()
        {
            var fixture = Fixture.Create();
            var resourceHandler = new ResourceScriptIdentityHandler();
            var foreignHandler = new ForeignIdentityHandler();
            var catalog = new PublishingPageIngredientHandlerCatalog(new PublishingPageIngredientHandler[]
            {
                resourceHandler,
                foreignHandler
            });
            var envelope = PublishingPageIngredientEvidenceEnvelope.Create(
                resourceHandler,
                PublishingPageJsLinkReferenceEvidence.SchemaVersion,
                fixture.Evidence.Source.IngredientId,
                fixture.Evidence.Source,
                fixture.Evidence.EvidenceReferences);

            Assert.ThrowsException<InvalidDataException>(() => catalog.ValidateEvidence(new[] { envelope, envelope }));

            var unknown = MigrationContractSerializer.Deserialize<PublishingPageIngredientEvidenceEnvelope>(
                MigrationContractSerializer.SerializeCanonical(envelope));
            unknown.HandlerId = "pnp.unknown/v1";
            Assert.ThrowsException<InvalidDataException>(() => catalog.ValidateEvidence(new[] { unknown }));

            Assert.ThrowsException<ArgumentException>(() =>
                new PublishingPageIngredientHandlerCatalog(new PublishingPageIngredientHandler[]
                {
                    resourceHandler,
                    new OverlappingResourceHandler()
                }));
        }

        private static PublishingPageIngredientEvidenceEnvelope AddResourceScriptEvidence(
            PublishingPageCaptureBundle snapshot,
            Fixture fixture,
            ResourceScriptIdentityHandler handler)
        {
            snapshot.Source.ListItemId = 17;
            snapshot.SourceFence.ETag = "\"{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee},1\"";
            var hostId = Guid.Parse("77777777-7777-4777-8777-777777777777");
            var rawLocator = fixture.Evidence.Source.Reference.OriginalValue;
            var hostJson = MigrationContractSerializer.SerializeCanonical(new
            {
                id = hostId.ToString("D"),
                properties = new Dictionary<string, string>
                {
                    ["JSLink"] = rawLocator,
                    ["XmlDefinition"] = "<View><Query /><JSLink>sp.ui.blogs.js</JSLink></View>"
                },
                title = "Resource script host",
                type = (string)null,
                zoneIndex = 7
            });
            var hostDigest = MigrationDigest.ComputeSha256(hostJson);
            snapshot.WebParts.Add(new ClassicWebPartSnapshot
            {
                Id = hostId,
                Title = "Resource script host",
                ZoneIndex = 7,
                ExportSha256 = MigrationDigest.ComputeSha256(string.Empty),
                PropertyEvidenceFormat = ClassicWebPartPropertyEvidenceValidator.RestExpandedFormat,
                PropertyEvidenceJson = hostJson,
                PropertyEvidenceArtifact = new ArtifactReference
                {
                    Sha256 = hostDigest,
                    Length = Encoding.UTF8.GetByteCount(hostJson),
                    MediaType = "application/json"
                }
            });

            var source = MigrationContractSerializer.Deserialize<PublishingPageJsLinkReferenceEvidence>(
                MigrationContractSerializer.SerializeCanonical(fixture.Evidence.Source));
            source.SourcePageFileUniqueId = snapshot.Source.FileUniqueId;
            source.SourceListItemId = snapshot.Source.ListItemId;
            source.SourcePageServerRelativeUrl = snapshot.Source.PageServerRelativeUrl;
            source.SourcePageETag = snapshot.SourceFence.ETag;
            source.HostWebPartId = hostId;
            source.HostWebPartOrder = 7;
            source.HostEvidenceSha256 = hostDigest;
            source.Reference.Consumer = PublishingPageIngredientIds.WebPart(hostId);
            source.Reference.SourceAbsoluteUrl = snapshot.Source.WebUrl.TrimEnd('/') + "/SiteAssets/canary.js";
            source.Reference.SourceServerRelativeUrl = snapshot.Source.WebServerRelativeUrl.TrimEnd('/') + "/SiteAssets/canary.js";
            source.IngredientId = PublishingPageJsLinkReferenceEvidence.IngredientIdPrefix
                + snapshot.Source.FileUniqueId.ToString("D") + ":" + hostId.ToString("D") + ":JSLink:" + rawLocator;

            return PublishingPageIngredientEvidenceEnvelope.Create(
                handler,
                PublishingPageJsLinkReferenceEvidence.SchemaVersion,
                source.IngredientId,
                source,
                fixture.Evidence.EvidenceReferences);
        }

        private static PublishingPageMigrationPackage CreateValidMigrationPackage()
        {
            return (PublishingPageMigrationPackage)typeof(PnP.Framework.Test.EnterpriseWiki.EnterpriseWikiMigrationTests)
                .GetMethod("CreateMigrationPackage", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
        }

        private static void AssertGate(
            IngredientMaturityAssessment assessment,
            string gateId,
            IngredientMaturityGateStatus expected)
        {
            var gate = assessment.Levels.SelectMany(value => value.Gates)
                .Single(value => string.Equals(value.GateId, gateId, StringComparison.Ordinal));
            Assert.AreEqual(expected, gate.Status, gateId);
        }

        private sealed class Fixture
        {
            public static readonly DateTimeOffset SourceObservedAt =
                new DateTimeOffset(2026, 9, 9, 20, 1, 15, TimeSpan.Zero);

            private Fixture()
            {
            }

            public ResourceScriptMaturityEvidence Evidence { get; private set; }

            public IngredientMaturityEvaluationContext Context { get; private set; }

            public ResourceScriptNormalization Normalized { get; private set; }

            public static Fixture Create([CallerFilePath] string callerFile = null)
            {
                var repositoryRoot = FindRepositoryRoot(callerFile);
                var resourceRoot = Path.Combine(
                    repositoryRoot,
                    "src",
                    "lib",
                    "PnP.Framework.Test",
                    "Resources",
                    "Migration",
                    "Ingredients",
                    "ResourceScript");
                var data = MigrationContractSerializer.Deserialize<FixtureData>(
                    File.ReadAllText(Path.Combine(resourceRoot, "jslink-same-site.fixture.v1.json")));
                var scriptBytes = File.ReadAllBytes(Path.Combine(resourceRoot, data.Ingredient.Payload.File));
                Assert.AreEqual(data.Ingredient.Payload.Bytes, scriptBytes.LongLength);
                Assert.AreEqual(data.Ingredient.Payload.Sha256, MigrationDigest.ComputeSha256(scriptBytes));
                Assert.IsFalse(data.Provenance.ContainsCustomerIdentity);
                Assert.IsFalse(data.Provenance.ContainsSourceScriptBytes);
                Assert.IsFalse(data.Provenance.NetworkRequired);
                Assert.IsFalse(data.Provenance.CurrentTimeRequired);

                var pageId = Guid.Parse(data.Source.PageUniqueId);
                var hostId = Guid.Parse(data.Host.InstanceId);
                var hostJson = MigrationContractSerializer.SerializeCanonical(new
                {
                    id = hostId.ToString("D"),
                    properties = new Dictionary<string, string>
                    {
                        ["JSLink"] = data.Ingredient.RawLocator,
                        ["XmlDefinition"] = "<View><Query /><JSLink>" + data.Host.EmbeddedCompanionJSLink + "</JSLink></View>"
                    },
                    title = data.Host.Title,
                    type = (string)null,
                    zoneIndex = data.Host.ZoneIndex
                });
                var hostDigest = MigrationDigest.ComputeSha256(hostJson);
                var ingredientId = PublishingPageJsLinkReferenceEvidence.IngredientIdPrefix
                    + pageId.ToString("D") + ":" + hostId.ToString("D") + ":JSLink:" + data.Ingredient.RawLocator;

                var fixture = new Fixture
                {
                    Evidence = new ResourceScriptMaturityEvidence
                    {
                        Snapshot = new PublishingPageCaptureBundle
                        {
                            Source = new PageIdentity
                            {
                                SiteId = Guid.Parse(data.Source.SiteId),
                                WebId = Guid.Parse(data.Source.WebId),
                                WebUrl = data.Source.WebUrl,
                                PageServerRelativeUrl = data.Source.PageServerRelativeUrl,
                                FileUniqueId = pageId,
                                ListItemId = data.Source.PageItemId,
                                ContentTypeId = "0x010100",
                                VersionLabel = "fixture-version"
                            },
                            SourceFence = new SourcePageFence
                            {
                                FileUniqueId = pageId,
                                ETag = data.Source.PageETag
                            },
                            PageArtifact = new PageArtifactSnapshot { FileUniqueId = pageId },
                            Runtime = new PageRuntimeSnapshot { AdapterId = "test.classic-page/v1" },
                            Layout = new PublishingPageLayoutSnapshot(),
                            PublishingPageContent = string.Empty,
                            Security = new PageSecuritySnapshot(),
                            Lifecycle = new PageLifecycleSnapshot(),
                            WebParts = new List<ClassicWebPartSnapshot>
                            {
                                new ClassicWebPartSnapshot
                                {
                                    Id = hostId,
                                    Title = data.Host.Title,
                                    ZoneIndex = data.Host.ZoneIndex,
                                    PropertyEvidenceFormat = ClassicWebPartPropertyEvidenceValidator.RestExpandedFormat,
                                    PropertyEvidenceJson = hostJson,
                                    PropertyEvidenceArtifact = new ArtifactReference
                                    {
                                        Sha256 = hostDigest,
                                        Length = Encoding.UTF8.GetByteCount(hostJson),
                                        MediaType = "application/json"
                                    }
                                }
                            }
                        },
                        Source = new PublishingPageJsLinkReferenceEvidence
                        {
                            IngredientId = ingredientId,
                            Subtype = data.Ingredient.Subtype,
                            SemanticRole = data.Ingredient.SemanticRole,
                            SourcePageFileUniqueId = pageId,
                            SourceListItemId = data.Source.PageItemId,
                            SourcePageServerRelativeUrl = data.Source.PageServerRelativeUrl,
                            SourcePageETag = data.Source.PageETag,
                            HostWebPartId = hostId,
                            HostWebPartOrder = data.Host.ZoneIndex,
                            HostEvidenceFormat = ClassicWebPartPropertyEvidenceValidator.RestExpandedFormat,
                            HostEvidenceSha256 = hostDigest,
                            ReferenceForm = "JSLink",
                            PersistedPropertyValue = data.Ingredient.RawLocator,
                            PersistedReferenceOrder = data.Ingredient.PersistedReferenceOrder,
                            Reference = new PageReferenceSnapshot
                            {
                                Id = "fixture-jslink",
                                OriginalValue = data.Ingredient.RawLocator,
                                SourceAbsoluteUrl = data.Ingredient.NormalizedLocator,
                                SourceServerRelativeUrl = data.Ingredient.SourceRelativePath,
                                Consumer = PublishingPageIngredientIds.WebPart(hostId),
                                Kind = PageReferenceKind.Script,
                                IsRenderableResource = true,
                                ContentSha256 = data.Ingredient.Payload.Sha256,
                                ContentLength = data.Ingredient.Payload.Bytes,
                                CaptureStatus = PageCaptureStatus.Captured
                            }
                        },
                        ScriptContentBase64 = Convert.ToBase64String(scriptBytes),
                        EvidenceReferences = new List<string>
                        {
                            "fixture:resource-script/canary.js",
                            "fixture:resource-script/jslink-same-site.fixture.v1.json"
                        },
                        Live = new IngredientLiveEvidence
                        {
                            SourceAuthenticated = true,
                            TargetFreshReadback = false,
                            SourceEvidenceReferences = new List<string> { "receipt:source-no-store" },
                            TargetEvidenceReferences = new List<string> { "receipt:target-readback" }
                        }
                    },
                    Context = new IngredientMaturityEvaluationContext
                    {
                        Identity = new IngredientMaturityIdentity
                        {
                            ClaimId = MigrationDigest.ComputeSha256(ingredientId),
                            Lane = ResourceScriptEvidenceNormalizer.Lane,
                            IngredientId = ingredientId,
                            Kind = PageIngredientKind.Reference,
                            Subtype = data.Ingredient.Subtype,
                            SemanticRole = data.Ingredient.SemanticRole,
                            SourcePredicateId = PublishingPageJsLinkReferenceEvidence.SourcePredicateId
                        },
                        Target = new IngredientMaturityTargetBinding
                        {
                            TargetProfile = "cupcollect-classic-page/v1",
                            TargetIdentity = "https://target.example.invalid/sites/canary/SiteAssets/canary.js"
                        },
                        Producer = new IngredientMaturityProducerBinding
                        {
                            ProducerId = "PnP.Framework",
                            ProducerVersion = "ccd-157-fixture",
                            ImplementationCommit = "3341d89f87e3d747120138be83b6f9f5267a1677",
                            BinaryDigest = new string('a', 64)
                        },
                        TargetMaturity = IngredientMaturityLevel.M5,
                        TechnicalOutcome = new IngredientTechnicalOutcome
                        {
                            PnPMigrationOutcome = PageMigrationOutcome.MitigationPending,
                            Status = IngredientTechnicalStatus.Unverified,
                            ReasonCode = "fixture-no-live-target"
                        }
                    }
                };
                fixture.RefreshNormalization();
                return fixture;
            }

            public void RefreshNormalization()
            {
                Normalized = ResourceScriptEvidenceNormalizer.Normalize(Context, Evidence);
                Context.Source = new IngredientMaturitySourceBinding
                {
                    PageOrListItemIdentity = Normalized.Node.SourcePageOrListItemIdentity,
                    SourceVersion = Normalized.Node.SourceVersionIdentity,
                    SourceArtifactDigest = Normalized.Node.EvidenceDigest,
                    SourceSnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(Normalized.Snapshot)
                };
                Normalized = ResourceScriptEvidenceNormalizer.Normalize(Context, Evidence);
            }

            public void AddSourceObservations(
                IngredientObservationOrigin origin = IngredientObservationOrigin.AuthenticatedSource)
            {
                AddObservations(origin, Normalized.SourceObservationDigests, SourceObservedAt, "source");
            }

            public void AddTargetObservations(DateTimeOffset? observedAt = null)
            {
                AddObservations(
                    IngredientObservationOrigin.CupCollectFreshReadback,
                    Normalized.TargetObservationDigests,
                    observedAt ?? SourceObservedAt.AddSeconds(1),
                    "target");
                Evidence.Live.TargetFreshReadback = true;
            }

            public IngredientMaturityContribution Contribute()
            {
                return new ResourceScriptMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new IIngredientMaturityContributor[]
                    {
                        new ResourceScriptMaturityContributor(Evidence)
                    }));
            }

            private void AddObservations(
                IngredientObservationOrigin origin,
                IReadOnlyDictionary<string, string> values,
                DateTimeOffset observedAt,
                string prefix)
            {
                foreach (var value in values)
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        Origin = origin,
                        ValuePath = value.Key,
                        ValueDigest = value.Value,
                        ObservedAtUtc = observedAt,
                        EvidenceReference = "receipt:" + prefix + "/" + value.Key
                    });
                }
            }

            private static string FindRepositoryRoot(string callerFile)
            {
                var current = new DirectoryInfo(Path.GetDirectoryName(callerFile));
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "src"))
                        && (Directory.Exists(Path.Combine(current.FullName, ".git"))
                            || File.Exists(Path.Combine(current.FullName, ".git"))))
                    {
                        return current.FullName;
                    }
                    current = current.Parent;
                }
                throw new DirectoryNotFoundException("Could not locate the repository root for the resource.script fixture.");
            }
        }

        private sealed class FixtureData
        {
            public string Schema { get; set; }
            public string FixtureId { get; set; }
            public FixtureProvenance Provenance { get; set; }
            public FixtureSource Source { get; set; }
            public FixtureHost Host { get; set; }
            public FixtureIngredient Ingredient { get; set; }
        }

        private sealed class FixtureProvenance
        {
            public string DerivedFromClaimId { get; set; }
            public bool ContainsCustomerIdentity { get; set; }
            public bool ContainsSourceScriptBytes { get; set; }
            public bool NetworkRequired { get; set; }
            public bool CurrentTimeRequired { get; set; }
        }

        private sealed class FixtureSource
        {
            public string SiteId { get; set; }
            public string WebId { get; set; }
            public string PageUniqueId { get; set; }
            public int PageItemId { get; set; }
            public string PageETag { get; set; }
            public string WebUrl { get; set; }
            public string PageServerRelativeUrl { get; set; }
        }

        private sealed class FixtureHost
        {
            public string InstanceId { get; set; }
            public string Title { get; set; }
            public int ZoneIndex { get; set; }
            public string EmbeddedCompanionJSLink { get; set; }
        }

        private sealed class FixtureIngredient
        {
            public string Kind { get; set; }
            public string Subtype { get; set; }
            public string SemanticRole { get; set; }
            public string RawLocator { get; set; }
            public string NormalizedLocator { get; set; }
            public string SourceRelativePath { get; set; }
            public int PersistedReferenceOrder { get; set; }
            public FixturePayload Payload { get; set; }
        }

        private sealed class FixturePayload
        {
            public string File { get; set; }
            public long Bytes { get; set; }
            public string Sha256 { get; set; }
            public string ContentType { get; set; }
            public string Encoding { get; set; }
        }

        private sealed class ForeignEvidence
        {
            public string NodeId { get; set; }

            public string Value { get; set; }
        }

        private sealed class ForeignIdentityHandler : PublishingPageIngredientHandler<ForeignEvidence>
        {
            public const string EvidenceSchema = "pnp-resource-script-coexistence-foreign/v1";

            public override PageIngredientHandlerDescriptor Descriptor { get; } = new PageIngredientHandlerDescriptor(
                "pnp.resource-script.coexistence-foreign/v1",
                new PageIngredientLaneDescriptor("dynamic.region", new[] { "publishing" }),
                new[] { EvidenceSchema },
                PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                100,
                new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix, "dynamic-region:") });

            protected override void ProjectGraph(
                PublishingPageIngredientGraphProjectionContext context,
                PublishingPageIngredientEvidenceEnvelope envelope,
                ForeignEvidence evidence)
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
                    SourceAuthority = "Coexistence test envelope",
                    EvidenceDigest = envelope.EvidenceDigest,
                    EvidenceReferences = envelope.EvidenceReferences.ToList()
                });
            }

            protected override void ProjectActions(
                PublishingPageIngredientActionProjectionContext context,
                PublishingPageIngredientEvidenceEnvelope envelope,
                ForeignEvidence evidence)
            {
                context.AddAction(new PageIngredientAction
                {
                    ActionId = "action:" + evidence.NodeId,
                    IngredientId = evidence.NodeId,
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Preserve,
                    Realization = "coexistence-test-handler",
                    PolicyId = "policy.coexistence-test-handler",
                    PolicyVersion = "1",
                    Reason = "Foreign handler evidence remains in the composed plan."
                });
            }
        }

        private sealed class OverlappingResourceHandler : PublishingPageIngredientHandler<ForeignEvidence>
        {
            public override PageIngredientHandlerDescriptor Descriptor { get; } = new PageIngredientHandlerDescriptor(
                "pnp.resource-script.overlap-test/v1",
                new PageIngredientLaneDescriptor("dynamic.region", new[] { "publishing" }),
                new[] { ForeignIdentityHandler.EvidenceSchema },
                PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
                200,
                new[]
                {
                    new PageIngredientIdOwnership(
                        PageIngredientIdOwnershipKind.Prefix,
                        PublishingPageJsLinkReferenceEvidence.IngredientIdPrefix)
                });

            protected override void ProjectGraph(
                PublishingPageIngredientGraphProjectionContext context,
                PublishingPageIngredientEvidenceEnvelope envelope,
                ForeignEvidence evidence)
            {
                throw new AssertFailedException("The overlapping handler must be rejected before projection.");
            }
        }
    }
}
