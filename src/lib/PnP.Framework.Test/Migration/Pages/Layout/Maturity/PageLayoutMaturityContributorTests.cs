using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Assessment.Maturity.PageLayout;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Pages.Assessment.Maturity.PageLayout
{
    [TestClass]
    public class PageLayoutMaturityContributorTests
    {
        [TestMethod]
        public void WikiFixturePassesM0AndM2WhileFreshTargetReadbackRemainsClosed()
        {
            var fixture = Fixture.Create();
            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.PrimaryOwner).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SourceBinding).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity).Status);
        }

        [TestMethod]
        public void MatchingFreshTargetValuesCloseM1WithoutAssigningMaturityInContributor()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();
            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();

            Assert.IsFalse(contribution.GetType().GetProperties().Any(value =>
                string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.PerValueObservation).Status);
        }

        [TestMethod]
        public void MismatchedFreshTargetLayoutValueFailsM1Closed()
        {
            var fixture = Fixture.Create();
            fixture.AddMatchingTargetObservations();
            fixture.Evidence.Live.Observations.Single(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                && value.ValuePath == "layout.listBaseTemplate").ValueDigest = new string('0', 64);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [DataTestMethod]
        [DataRow("wrong-list")]
        [DataRow("wrong-item")]
        [DataRow("wrong-unique-id")]
        [DataRow("stale-version")]
        [DataRow("wrong-template")]
        [DataRow("wrong-content-type")]
        [DataRow("unexpected-publishing-layout")]
        [DataRow("corrupt-raw-artifact")]
        public void WrongBindingAndCorruptEvidenceCasesFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.Mutate(mutation);

            var assessment = fixture.Evaluate();
            var failed = assessment.Levels.SelectMany(value => value.Gates)
                .Where(value => value.Status == IngredientMaturityGateStatus.Failed)
                .Select(value => value.GateId)
                .ToArray();

            CollectionAssert.IsSubsetOf(
                failed,
                new[]
                {
                    IngredientMaturityGateCatalog.SourceBinding,
                    IngredientMaturityGateCatalog.PrimaryOwner,
                    IngredientMaturityGateCatalog.AuthenticatedSourceCollect,
                    IngredientMaturityGateCatalog.SemanticIntegrity,
                    IngredientMaturityGateCatalog.RawArtifactIntegrity,
                    IngredientMaturityGateCatalog.CupCollectFreshReadback
                });
            Assert.IsTrue(failed.Contains(IngredientMaturityGateCatalog.SourceBinding, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.PrimaryOwner, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.SemanticIntegrity, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.RawArtifactIntegrity, StringComparer.Ordinal));
        }

        private static IngredientMaturityGateResult Gate(IngredientMaturityAssessment assessment, string gateId)
        {
            return assessment.Levels.SelectMany(value => value.Gates).Single(value => value.GateId == gateId);
        }

        private sealed class Fixture
        {
            private readonly FixtureContract contract;
            private readonly string expectedIdentity;
            private readonly string expectedVersion;

            private Fixture(FixtureContract contract)
            {
                this.contract = contract;
                Evidence = CreateEvidence(contract);
                expectedIdentity = CreateSourceIdentity(contract.Source);
                expectedVersion = contract.Source.Version;
                Context = CreateContext(contract, expectedIdentity);
            }

            public IngredientMaturityEvaluationContext Context { get; }

            public PageLayoutMaturityEvidence Evidence { get; }

            public static Fixture Create()
            {
                return new Fixture(LoadContract());
            }

            public IngredientMaturityContribution Contribute()
            {
                return new PageLayoutMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                var contributor = new PageLayoutMaturityContributor(Evidence);
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[] { contributor }));
            }

            public void AddMatchingTargetObservations()
            {
                var targetTime = contract.SourceObservedAtUtc.AddMinutes(1);
                foreach (var source in Evidence.Live.Observations.ToArray())
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ValuePath = source.ValuePath,
                        ValueDigest = source.ValueDigest,
                        ObservedAtUtc = targetTime,
                        Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                        EvidenceReference = "cupcollect-readback.json#" + source.ValuePath
                    });
                }
                Evidence.Live.TargetFreshReadback = true;
                Evidence.Live.TargetEvidenceReferences.Add("cupcollect-readback.json");
            }

            public void Mutate(string mutation)
            {
                switch (mutation)
                {
                    case "wrong-list":
                        Evidence.WikiSource.ListId = "11111111-1111-1111-1111-111111111111";
                        break;
                    case "wrong-item":
                        Evidence.WikiSource.Snapshot.Source.ListItemId++;
                        break;
                    case "wrong-unique-id":
                        Evidence.WikiSource.Snapshot.Source.FileUniqueId = Guid.Parse("22222222-2222-2222-2222-222222222222");
                        break;
                    case "stale-version":
                        Evidence.WikiSource.SourceVersion = expectedVersion + "-stale";
                        break;
                    case "wrong-template":
                        Evidence.WikiSource.Snapshot.LibraryBaseTemplate = 101;
                        break;
                    case "wrong-content-type":
                        Evidence.WikiSource.Snapshot.Source.ContentTypeId = "0x0101";
                        break;
                    case "unexpected-publishing-layout":
                        Evidence.WikiSource.PublishingPageLayout = "/_catalogs/masterpage/ArticleLeft.aspx";
                        break;
                    case "corrupt-raw-artifact":
                        Evidence.WikiSource.RawArtifact.Sha256 = new string('0', 64);
                        break;
                    default:
                        Assert.Fail("Unknown mutation " + mutation);
                        break;
                }

                Assert.AreEqual(expectedIdentity, Context.Source.PageOrListItemIdentity);
                Assert.AreEqual(expectedVersion, Context.Source.SourceVersion);
            }

            private static PageLayoutMaturityEvidence CreateEvidence(FixtureContract fixture)
            {
                var snapshot = new ClassicWikiCaptureBundle
                {
                    Source = new PageIdentity
                    {
                        WebUrl = fixture.Source.WebUrl,
                        PageServerRelativeUrl = fixture.Source.FileServerRelativeUrl,
                        ListItemId = fixture.Source.ItemId,
                        FileUniqueId = Guid.Parse(fixture.Source.UniqueId),
                        ContentTypeId = fixture.Source.ContentTypeId,
                        ContentTypeName = fixture.Source.ContentTypeName
                    },
                    LibraryBaseTemplate = fixture.Source.ListBaseTemplate,
                    Runtime = new PageRuntimeSnapshot
                    {
                        AdapterId = fixture.Source.RuntimeAdapterId,
                        ResolutionState = PageRuntimeResolutionState.Resolved
                    }
                };
                var source = new PageLayoutWikiSourceEvidence
                {
                    Snapshot = snapshot,
                    ListId = fixture.Source.ListId,
                    SourceVersion = fixture.Source.Version,
                    PublishingPageLayout = fixture.Source.PublishingPageLayout,
                    RawArtifact = new ArtifactReference
                    {
                        Sha256 = fixture.RawArtifact.Sha256,
                        Length = fixture.RawArtifact.Length
                    },
                    RawArtifactBase64 = fixture.RawArtifact.Base64,
                    SemanticDigest = fixture.SemanticSha256,
                    EvidenceReferences = new List<string> { "wiki-row-00003.fixture.json" }
                };
                var normalized = PageLayoutWikiEvidenceNormalizer.Normalize(null, source);
                var observations = normalized.ValueDigests.Select(value => new IngredientValueObservation
                {
                    ValuePath = value.Key,
                    ValueDigest = value.Value,
                    ObservedAtUtc = fixture.SourceObservedAtUtc,
                    Origin = IngredientObservationOrigin.AuthenticatedSource,
                    EvidenceReference = "wiki-row-00003.fixture.json#" + value.Key
                }).ToList();
                return new PageLayoutMaturityEvidence
                {
                    WikiSource = source,
                    Live = new IngredientLiveEvidence
                    {
                        SourceAuthenticated = true,
                        TargetFreshReadback = false,
                        Observations = observations,
                        SourceEvidenceReferences = new List<string> { "wiki-row-00003.fixture.json" }
                    }
                };
            }

            private static IngredientMaturityEvaluationContext CreateContext(FixtureContract fixture, string sourceIdentity)
            {
                return new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = fixture.ClaimId,
                        Lane = PageLayoutWikiEvidenceNormalizer.Lane,
                        IngredientId = fixture.IngredientId,
                        Kind = PageIngredientKind.Layout,
                        Subtype = PageLayoutWikiEvidenceNormalizer.Subtype,
                        SemanticRole = PageLayoutWikiEvidenceNormalizer.SemanticRole,
                        SourcePredicateId = PageLayoutWikiEvidenceNormalizer.SourcePredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = sourceIdentity,
                        SourceVersion = fixture.Source.Version,
                        SourceArtifactDigest = fixture.RawArtifact.Sha256,
                        SourceSnapshotDigest = fixture.SemanticSha256
                    },
                    Target = new IngredientMaturityTargetBinding
                    {
                        TargetProfile = "cupcollect-classic-page/v1",
                        TargetIdentity = "cupcollect:SitePages/rss.aspx"
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = "ccd-182-v1",
                        ImplementationCommit = "aff892f119a20dd764af6b005087e7af5dad3a1c"
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.MitigationPending,
                        Status = IngredientTechnicalStatus.Unverified,
                        ReasonCode = "fresh-cupcollect-readback-required"
                    }
                };
            }

            private static string CreateSourceIdentity(SourceContract source)
            {
                return MigrationContractSerializer.SerializeCanonical(new
                {
                    fileServerRelativeUrl = source.FileServerRelativeUrl,
                    itemId = source.ItemId,
                    listId = source.ListId,
                    uniqueId = source.UniqueId,
                    version = source.Version,
                    webUrl = source.WebUrl
                });
            }

            private static FixtureContract LoadContract([CallerFilePath] string callerPath = null)
            {
                var path = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(callerPath),
                    "../../../../Resources/Migration/Pages/Layout/wiki-row-00003.fixture.json"));
                return JsonSerializer.Deserialize<FixtureContract>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }

        private sealed class FixtureContract
        {
            public string ClaimId { get; set; }

            public string IngredientId { get; set; }

            public SourceContract Source { get; set; }

            public RawArtifactContract RawArtifact { get; set; }

            public string SemanticSha256 { get; set; }

            public DateTimeOffset SourceObservedAtUtc { get; set; }
        }

        private sealed class SourceContract
        {
            public string WebUrl { get; set; }

            public string FileServerRelativeUrl { get; set; }

            public string ListId { get; set; }

            public int ItemId { get; set; }

            public string UniqueId { get; set; }

            public string Version { get; set; }

            public string ContentTypeId { get; set; }

            public string ContentTypeName { get; set; }

            public int ListBaseTemplate { get; set; }

            public string PublishingPageLayout { get; set; }

            public string RuntimeAdapterId { get; set; }
        }

        private sealed class RawArtifactContract
        {
            public string Sha256 { get; set; }

            public long Length { get; set; }

            public string Base64 { get; set; }
        }
    }
}
