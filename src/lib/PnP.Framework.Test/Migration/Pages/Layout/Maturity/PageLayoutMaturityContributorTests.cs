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
        private const string AdmittedPlanDigest = "8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f";

        [TestMethod]
        public void WikiFixturePassesM0AndM2WhileFreshTargetReadbackRemainsClosed()
        {
            var fixture = Fixture.Create();
            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CanonicalIdentity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.PrimaryOwner).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SourceBinding).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.RawArtifactIntegrity).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.SemanticIntegrity).Status);
        }

        [TestMethod]
        public void FreshCcd255WikiReadbackClosesM1ByContentTypeLineageWithoutAssigningMaturityInContributor()
        {
            var fixture = Fixture.Create();
            fixture.AddCcd255TargetReadback();
            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();

            Assert.AreNotEqual(fixture.SourceContentTypeId, fixture.TargetContentTypeId);
            Assert.IsFalse(contribution.GetType().GetProperties().Any(value =>
                string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Passed, Gate(assessment, IngredientMaturityGateCatalog.PerValueObservation).Status);
            Assert.AreEqual("v2", Gate(assessment, IngredientMaturityGateCatalog.PerValueObservation).ValidatorVersion);
        }

        [TestMethod]
        public void MismatchedFreshTargetLayoutValueFailsM1Closed()
        {
            var fixture = Fixture.Create();
            fixture.AddCcd255TargetReadback();
            fixture.Evidence.WikiTargetReadback.ListBaseTemplate = 101;

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [DataTestMethod]
        [DataRow("wrong-target-claim")]
        [DataRow("wrong-target-ingredient")]
        [DataRow("wrong-target-source-version")]
        [DataRow("wrong-target-source-file")]
        [DataRow("wrong-target-implementation")]
        [DataRow("wrong-target-profile")]
        [DataRow("wrong-target-path")]
        [DataRow("wrong-target-content-type-lineage")]
        [DataRow("stale-target-observation")]
        [DataRow("invalid-target-plan-digest")]
        [DataRow("wrong-valid-target-plan-digest")]
        [DataRow("missing-admitted-plan-digest")]
        [DataRow("blank-admitted-plan-digest")]
        [DataRow("missing-target-reference")]
        public void WrongTargetBindingAndLineageCasesFailM1Closed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.AddCcd255TargetReadback();
            fixture.MutateTarget(mutation);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [TestMethod]
        public void NullSourceObservationFailsM1ClosedWithoutThrowing()
        {
            var fixture = Fixture.Create();
            fixture.AddCcd255TargetReadback();
            fixture.ReplaceRequiredSourceObservationWithNull();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
        }

        [TestMethod]
        public void MissingConsumerObservationWindowFailsM1Closed()
        {
            var fixture = Fixture.Create();
            fixture.AddCcd255TargetReadback();
            fixture.Context.ObservationWindowEndUtc = null;

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback).Status);
        }

        [TestMethod]
        public void WrongObservationClaimBindingFailsM1Closed()
        {
            var fixture = Fixture.Create();
            fixture.AddCcd255TargetReadback();
            fixture.Evidence.Live.Observations[0].ClaimId = new string('a', 64);

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.AuthenticatedSourceCollect).Status);
            Assert.AreEqual(IngredientMaturityGateStatus.Failed, Gate(assessment, IngredientMaturityGateCatalog.PerValueObservation).Status);
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
                    IngredientMaturityGateCatalog.PerValueObservation,
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
                expectedIdentity = CreateSourceIdentity(contract.Source);
                expectedVersion = contract.Source.Version;
                Context = CreateContext(contract, expectedIdentity);
                Evidence = CreateEvidence(contract, Context);
            }

            public IngredientMaturityEvaluationContext Context { get; }

            public PageLayoutMaturityEvidence Evidence { get; }

            public string SourceContentTypeId => contract.Source.ContentTypeId;

            public string TargetContentTypeId => Evidence.WikiTargetReadback?.ContentTypeId;

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

            public void AddCcd255TargetReadback()
            {
                var target = LoadTargetContract();
                Evidence.WikiTargetReadback = new PageLayoutWikiTargetReadbackEvidence
                {
                    ClaimId = target.ClaimId,
                    IngredientId = target.IngredientId,
                    SourceListId = target.SourceListId,
                    SourceItemId = target.SourceItemId,
                    SourceFileUniqueId = target.SourceFileUniqueId,
                    SourceVersion = target.SourceVersion,
                    ImplementationCommit = target.ImplementationCommit,
                    PlanDigest = target.PlanDigest,
                    TargetProfile = target.TargetProfile,
                    TargetPath = target.TargetPath,
                    ReadbackStartedAtUtc = target.ReadbackStartedAtUtc,
                    ObservedAtUtc = target.ObservedAtUtc,
                    ListBaseTemplate = target.ListBaseTemplate,
                    ContentTypeId = target.ContentTypeId,
                    ContentTypeName = target.ContentTypeName,
                    PublishingPageLayout = target.PublishingPageLayout,
                    RuntimeAdapterId = target.RuntimeAdapterId,
                    EvidenceReferences = target.EvidenceReferences
                };
            }

            public void MutateTarget(string mutation)
            {
                switch (mutation)
                {
                    case "wrong-target-claim":
                        Evidence.WikiTargetReadback.ClaimId = new string('a', 64);
                        break;
                    case "wrong-target-ingredient":
                        Evidence.WikiTargetReadback.IngredientId += ":wrong";
                        break;
                    case "wrong-target-source-version":
                        Evidence.WikiTargetReadback.SourceVersion += "-stale";
                        break;
                    case "wrong-target-source-file":
                        Evidence.WikiTargetReadback.SourceFileUniqueId = "11111111-1111-1111-1111-111111111111";
                        break;
                    case "wrong-target-implementation":
                        Evidence.WikiTargetReadback.ImplementationCommit = new string('0', 40);
                        break;
                    case "wrong-target-profile":
                        Evidence.WikiTargetReadback.TargetProfile = "wrong-profile/v1";
                        break;
                    case "wrong-target-path":
                        Evidence.WikiTargetReadback.TargetPath += ".wrong";
                        break;
                    case "wrong-target-content-type-lineage":
                        Evidence.WikiTargetReadback.ContentTypeId = "0x0101";
                        break;
                    case "stale-target-observation":
                        Evidence.WikiTargetReadback.ObservedAtUtc = contract.SourceObservedAtUtc.AddSeconds(-1);
                        break;
                    case "invalid-target-plan-digest":
                        Evidence.WikiTargetReadback.PlanDigest = "not-a-digest";
                        break;
                    case "wrong-valid-target-plan-digest":
                        Evidence.WikiTargetReadback.PlanDigest = new string('f', 64);
                        break;
                    case "missing-admitted-plan-digest":
                        Evidence.AdmittedPlanDigest = null;
                        break;
                    case "blank-admitted-plan-digest":
                        Evidence.AdmittedPlanDigest = " ";
                        break;
                    case "missing-target-reference":
                        Evidence.WikiTargetReadback.EvidenceReferences.Clear();
                        break;
                    default:
                        Assert.Fail("Unknown target mutation " + mutation);
                        break;
                }
            }

            public void ReplaceRequiredSourceObservationWithNull()
            {
                var observations = Evidence.Live.Observations.ToList();
                observations[0] = null;
                Evidence.Live.Observations = observations;
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

            private static PageLayoutMaturityEvidence CreateEvidence(
                FixtureContract fixture,
                IngredientMaturityEvaluationContext context)
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
                var observations = normalized.ValueDigests.Select(value =>
                {
                    var evidenceReference = "wiki-row-00003.fixture.json#" + value.Key;
                    return new IngredientValueObservation
                    {
                        ClaimId = context.Identity.ClaimId,
                        IngredientId = context.Identity.IngredientId,
                        Source = Clone(context.Source),
                        Target = Clone(context.Target),
                        ValuePath = value.Key,
                        ValueDigest = value.Value,
                        ObservedAtUtc = fixture.SourceObservedAtUtc,
                        Origin = IngredientObservationOrigin.AuthenticatedSource,
                        EvidenceReference = evidenceReference
                    };
                }).ToList();
                return new PageLayoutMaturityEvidence
                {
                    WikiSource = source,
                    AdmittedPlanDigest = AdmittedPlanDigest,
                    Live = new IngredientLiveEvidence
                    {
                        SourceAuthenticated = true,
                        TargetFreshReadback = false,
                        Observations = observations,
                        SourceEvidenceReferences = observations.Select(value => value.EvidenceReference).ToList()
                    }
                };
            }

            private static IngredientMaturityEvaluationContext CreateContext(FixtureContract fixture, string sourceIdentity)
            {
                var target = LoadTargetContract();
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
                        TargetProfile = target.TargetProfile,
                        TargetIdentity = target.TargetPath
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = "ccd-182-v1",
                        ImplementationCommit = "5a9f634da422a92b2493967e32dc74edacac9777",
                        BinaryDigest = "dfbe85ab6b0e6c51a0efa1b3c09bc8ad8e222cd16a0f6647cc14766e29f5717a"
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.MitigationPending,
                        Status = IngredientTechnicalStatus.Unverified,
                        ReasonCode = "fresh-cupcollect-readback-required"
                    },
                    ObservationWindowStartUtc = fixture.SourceObservedAtUtc,
                    ObservationWindowEndUtc = target.ObservedAtUtc
                };
            }

            private static IngredientMaturitySourceBinding Clone(IngredientMaturitySourceBinding value)
            {
                return new IngredientMaturitySourceBinding
                {
                    PageOrListItemIdentity = value.PageOrListItemIdentity,
                    SourceVersion = value.SourceVersion,
                    SourceArtifactDigest = value.SourceArtifactDigest,
                    SourceSnapshotDigest = value.SourceSnapshotDigest
                };
            }

            private static IngredientMaturityTargetBinding Clone(IngredientMaturityTargetBinding value)
            {
                return new IngredientMaturityTargetBinding
                {
                    TargetProfile = value.TargetProfile,
                    TargetIdentity = value.TargetIdentity
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

            private static TargetReadbackContract LoadTargetContract([CallerFilePath] string callerPath = null)
            {
                var path = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(callerPath),
                    "../../../../Resources/Migration/Pages/Layout/wiki-row-00003.cupcollect-readback.fixture.json"));
                return JsonSerializer.Deserialize<TargetReadbackContract>(
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

        private sealed class TargetReadbackContract
        {
            public string ClaimId { get; set; }

            public string IngredientId { get; set; }

            public string SourceListId { get; set; }

            public int SourceItemId { get; set; }

            public string SourceFileUniqueId { get; set; }

            public string SourceVersion { get; set; }

            public string ImplementationCommit { get; set; }

            public string PlanDigest { get; set; }

            public string TargetProfile { get; set; }

            public string TargetPath { get; set; }

            public DateTimeOffset ObservedAtUtc { get; set; }

            public DateTimeOffset ReadbackStartedAtUtc { get; set; }

            public int ListBaseTemplate { get; set; }

            public string ContentTypeId { get; set; }

            public string ContentTypeName { get; set; }

            public string PublishingPageLayout { get; set; }

            public string RuntimeAdapterId { get; set; }

            public List<string> EvidenceReferences { get; set; }
        }
    }
}
