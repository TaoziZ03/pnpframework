using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.EmbedIframe;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Test.IngredientLanes.EmbedIframe
{
    [TestClass]
    public class EmbedIframeMaturityContributorTests
    {
        [TestMethod]
        public void PositiveFixturePassesM0AndM2WhileFreshTargetReadbackRemainsClosed()
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
        public void MatchingFreshTargetValuesCloseM1WithoutContributorAssigningMaturity()
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
        public void NormalizerPreservesLocatorGeometryPoliciesAndExternalBoundary()
        {
            var fixture = Fixture.Create();
            var normalized = fixture.Normalize();

            Assert.AreEqual("https://sway.com/s/BatsRizUdfGTTaJS/embed", normalized.RawLocator);
            Assert.AreEqual(normalized.RawLocator, normalized.ResolvedLocator);
            Assert.AreEqual(normalized.RawLocator, normalized.NormalizedLocator);
            Assert.AreEqual("external", normalized.Classification);
            Assert.AreEqual(fixture.Contract.SemanticSha256,
                MigrationDigest.ComputeSha256(normalized.SemanticCanonicalJson));
            Assert.IsTrue(normalized.ValueDigests.ContainsKey("geometry.width"));
            Assert.IsTrue(normalized.ValueDigests.ContainsKey("geometry.height"));
            Assert.IsTrue(normalized.ValueDigests.ContainsKey("policy.sandbox"));
            Assert.IsTrue(normalized.ValueDigests.ContainsKey("policy.allow"));
            Assert.IsTrue(normalized.ValueDigests.ContainsKey("policy.referrerpolicy"));
            Assert.IsTrue(normalized.ValueDigests.ContainsKey("policy.loading"));
            Assert.IsTrue(normalized.ValueDigests.ContainsKey("policy.title"));
            Assert.AreEqual(EmbedIframeEvidenceNormalizer.RuntimeRequirement, normalized.Node.RuntimeRequirement);
            Assert.IsNull(fixture.Evidence.Source.Reference.ContentBase64);
            Assert.IsNull(fixture.Evidence.Source.Reference.ContentSha256);
        }

        [DataTestMethod]
        [DataRow("missing-src")]
        [DataRow("hyperlink-is-not-iframe")]
        [DataRow("stale-version")]
        [DataRow("missing-host-binding")]
        [DataRow("wrong-consumer-binding")]
        [DataRow("wrong-reference-kind")]
        [DataRow("unobservable-content-claimed")]
        [DataRow("corrupt-raw-artifact")]
        [DataRow("semantic-digest-mismatch")]
        public void WrongBindingPolicyAndIntegrityCasesFailClosed(string mutation)
        {
            var fixture = Fixture.Create();
            fixture.Mutate(mutation);

            var assessment = fixture.Evaluate();
            var failed = assessment.Levels.SelectMany(value => value.Gates)
                .Where(value => value.Status == IngredientMaturityGateStatus.Failed)
                .Select(value => value.GateId)
                .ToArray();

            Assert.IsTrue(failed.Contains(IngredientMaturityGateCatalog.PrimaryOwner, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.SourceBinding, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.RawArtifactIntegrity, StringComparer.Ordinal)
                || failed.Contains(IngredientMaturityGateCatalog.SemanticIntegrity, StringComparer.Ordinal));
        }

        private static IngredientMaturityGateResult Gate(IngredientMaturityAssessment assessment, string gateId)
        {
            return assessment.Levels.SelectMany(value => value.Gates).Single(value => value.GateId == gateId);
        }

        private sealed class Fixture
        {
            private readonly string expectedVersion;
            private readonly string expectedSourceIdentity;

            private Fixture(FixtureContract contract)
            {
                Contract = contract;
                Evidence = CreateEvidence(contract);
                expectedVersion = contract.Source.Version;
                expectedSourceIdentity = CreateSourceIdentity(contract.Source);
                Context = CreateContext(contract, expectedSourceIdentity);
            }

            public FixtureContract Contract { get; }

            public IngredientMaturityEvaluationContext Context { get; }

            public EmbedIframeMaturityEvidence Evidence { get; }

            public static Fixture Create()
            {
                return new Fixture(LoadContract());
            }

            public EmbedIframeNormalization Normalize()
            {
                return EmbedIframeEvidenceNormalizer.Normalize(Context, Evidence.Source);
            }

            public IngredientMaturityContribution Contribute()
            {
                return new EmbedIframeMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                var contributor = new EmbedIframeMaturityContributor(Evidence);
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[] { contributor }));
            }

            public void AddMatchingTargetObservations()
            {
                var normalized = Normalize();
                var targetTime = Contract.SourceObservedAtUtc.AddMinutes(1);
                foreach (var value in normalized.ValueDigests)
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ValuePath = value.Key,
                        ValueDigest = value.Value,
                        ObservedAtUtc = targetTime,
                        Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                        EvidenceReference = "cupcollect-readback.json#" + value.Key
                    });
                }
                Evidence.Live.TargetFreshReadback = true;
                Evidence.Live.TargetEvidenceReferences.Add("cupcollect-readback.json");
            }

            public void Mutate(string mutation)
            {
                switch (mutation)
                {
                    case "missing-src":
                        ReplaceRaw("<iframe width=\"560px\" height=\"250px\"></iframe>");
                        break;
                    case "hyperlink-is-not-iframe":
                        ReplaceRaw("<a href=\"https://sway.com/s/BatsRizUdfGTTaJS/embed\">Sway</a>");
                        break;
                    case "stale-version":
                        Evidence.Source.Source.Version = expectedVersion + "-stale";
                        break;
                    case "missing-host-binding":
                        Evidence.Source.Host.PropertyName = null;
                        break;
                    case "wrong-consumer-binding":
                        Evidence.Source.Reference.Consumer = "webpart:00000000-0000-0000-0000-000000000000";
                        break;
                    case "wrong-reference-kind":
                        Evidence.Source.Reference.Kind = PageReferenceKind.Anchor;
                        break;
                    case "unobservable-content-claimed":
                        Evidence.Source.Reference.ContentBase64 = Evidence.Source.RawArtifactBase64;
                        Evidence.Source.Reference.ContentSha256 = Evidence.Source.RawArtifact.Sha256;
                        break;
                    case "corrupt-raw-artifact":
                        Evidence.Source.RawArtifact.Sha256 = new string('0', 64);
                        break;
                    case "semantic-digest-mismatch":
                        Evidence.Source.SemanticDigest = new string('0', 64);
                        break;
                    default:
                        Assert.Fail("Unknown mutation " + mutation);
                        break;
                }

                Assert.AreEqual(expectedSourceIdentity, Context.Source.PageOrListItemIdentity);
                Assert.AreEqual(expectedVersion, Context.Source.SourceVersion);
            }

            private void ReplaceRaw(string raw)
            {
                var bytes = Encoding.UTF8.GetBytes(raw);
                Evidence.Source.RawArtifactBase64 = Convert.ToBase64String(bytes);
                Evidence.Source.RawArtifact.Length = bytes.LongLength;
                Evidence.Source.RawArtifact.Sha256 = MigrationDigest.ComputeSha256(bytes);
            }

            private static EmbedIframeMaturityEvidence CreateEvidence(FixtureContract fixture)
            {
                var sourceEvidence = new EmbedIframeSourceEvidence
                {
                    Source = new EmbedIframeSourceIdentity
                    {
                        WebUrl = fixture.Source.WebUrl,
                        PageUrl = fixture.Source.PageUrl,
                        FileServerRelativeUrl = fixture.Source.FileServerRelativeUrl,
                        ListId = fixture.Source.ListId,
                        ItemId = fixture.Source.ItemId,
                        UniqueId = fixture.Source.UniqueId,
                        Version = fixture.Source.Version,
                        Modified = fixture.Source.Modified
                    },
                    Host = new EmbedIframeHostBinding
                    {
                        IngredientId = fixture.Host.IngredientId,
                        WebPartId = fixture.Host.WebPartId,
                        PropertyName = fixture.Host.PropertyName,
                        ZoneIndex = fixture.Host.ZoneIndex
                    },
                    Reference = new PageReferenceSnapshot
                    {
                        Id = fixture.Reference.Id,
                        OriginalValue = fixture.Reference.OriginalValue,
                        SourceAbsoluteUrl = fixture.Reference.SourceAbsoluteUrl,
                        Consumer = fixture.Reference.Consumer,
                        Kind = Enum.Parse<PageReferenceKind>(fixture.Reference.Kind),
                        IsRenderableResource = fixture.Reference.IsRenderableResource,
                        CaptureStatus = Enum.Parse<PageCaptureStatus>(fixture.Reference.CaptureStatus)
                    },
                    RawArtifact = new ArtifactReference
                    {
                        Sha256 = fixture.RawArtifact.Sha256,
                        Length = fixture.RawArtifact.Length,
                        MediaType = fixture.RawArtifact.MediaType,
                        OriginalName = fixture.RawArtifact.OriginalName
                    },
                    RawArtifactBase64 = fixture.RawArtifact.Base64,
                    SemanticDigest = fixture.SemanticSha256,
                    EvidenceReferences = new List<string>
                    {
                        "ccd110-r00871-v83.fixture.json",
                        "source-collect-receipt.json"
                    }
                };
                var normalized = EmbedIframeEvidenceNormalizer.Normalize(null, sourceEvidence);
                var observations = normalized.ValueDigests.Select(value => new IngredientValueObservation
                {
                    ValuePath = value.Key,
                    ValueDigest = value.Value,
                    ObservedAtUtc = fixture.SourceObservedAtUtc,
                    Origin = IngredientObservationOrigin.AuthenticatedSource,
                    EvidenceReference = "source-collect-receipt.json#" + value.Key
                }).ToList();
                return new EmbedIframeMaturityEvidence
                {
                    Source = sourceEvidence,
                    Live = new IngredientLiveEvidence
                    {
                        SourceAuthenticated = true,
                        TargetFreshReadback = false,
                        Observations = observations,
                        SourceEvidenceReferences = new List<string> { "source-collect-receipt.json" }
                    }
                };
            }

            private static IngredientMaturityEvaluationContext CreateContext(
                FixtureContract fixture,
                string sourceIdentity)
            {
                return new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = fixture.ClaimId,
                        Lane = EmbedIframeEvidenceNormalizer.Lane,
                        IngredientId = fixture.IngredientId,
                        Kind = PageIngredientKind.Reference,
                        Subtype = EmbedIframeEvidenceNormalizer.Subtype,
                        SemanticRole = EmbedIframeEvidenceNormalizer.SemanticRole,
                        SourcePredicateId = EmbedIframeEvidenceNormalizer.SourcePredicateId
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
                        TargetProfile = fixture.TargetProfile,
                        TargetIdentity = fixture.TargetIdentity
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
                        ReasonCode = "fresh-cupcollect-policy-runtime-readback-required"
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
                    "../../Resources/IngredientLanes/embed.iframe/v1/ccd110-r00871-v83.fixture.json"));
                return JsonSerializer.Deserialize<FixtureContract>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }

        internal sealed class FixtureContract
        {
            public string ClaimId { get; set; }

            public string IngredientId { get; set; }

            public SourceContract Source { get; set; }

            public HostContract Host { get; set; }

            public ReferenceContract Reference { get; set; }

            public RawArtifactContract RawArtifact { get; set; }

            public string SemanticSha256 { get; set; }

            public DateTimeOffset SourceObservedAtUtc { get; set; }

            public string TargetProfile { get; set; }

            public string TargetIdentity { get; set; }
        }

        internal sealed class SourceContract
        {
            public string WebUrl { get; set; }
            public string PageUrl { get; set; }
            public string FileServerRelativeUrl { get; set; }
            public string ListId { get; set; }
            public int ItemId { get; set; }
            public string UniqueId { get; set; }
            public string Version { get; set; }
            public string Modified { get; set; }
        }

        internal sealed class HostContract
        {
            public string IngredientId { get; set; }
            public string WebPartId { get; set; }
            public string PropertyName { get; set; }
            public int ZoneIndex { get; set; }
        }

        internal sealed class ReferenceContract
        {
            public string Id { get; set; }
            public string OriginalValue { get; set; }
            public string SourceAbsoluteUrl { get; set; }
            public string Consumer { get; set; }
            public string Kind { get; set; }
            public bool IsRenderableResource { get; set; }
            public string CaptureStatus { get; set; }
        }

        internal sealed class RawArtifactContract
        {
            public string Sha256 { get; set; }
            public long Length { get; set; }
            public string MediaType { get; set; }
            public string OriginalName { get; set; }
            public string Base64 { get; set; }
        }
    }
}
