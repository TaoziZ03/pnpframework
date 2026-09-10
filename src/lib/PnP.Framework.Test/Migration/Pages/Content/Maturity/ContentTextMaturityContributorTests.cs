using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Content.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Pages.Content.Maturity
{
    [TestClass]
    public class ContentTextMaturityContributorTests
    {
        [TestMethod]
        public void PublishingFixturePassesM0AndM2ButMissingTargetReadbackStopsAtM0()
        {
            var fixture = Fixture.CreatePublishing();
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
        public void FreshSemanticallyEquivalentPublishingReadbackClosesM1AndM2()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var contribution = fixture.Contribute();
            var assessment = fixture.Evaluate();

            Assert.IsFalse(contribution.GetType().GetProperties().Any(value =>
                string.Equals(value.Name, "AttainedMaturity", StringComparison.Ordinal)));
            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual(fixture.Data.SemanticSlice.Canonical, fixture.Normalized.NormalizedBodyValue);
            Assert.AreEqual(fixture.Data.SemanticSlice.SemanticSha256,
                fixture.Normalized.TargetObservationDigests["content.semantic"]);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Passed);
            AssertGate(assessment, IngredientMaturityGateCatalog.PerValueObservation, IngredientMaturityGateStatus.Passed);
        }

        [TestMethod]
        public void TargetReadbackOlderThanSourceFailsFreshness()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations(fixture.SourceObservedAt.AddSeconds(-1));

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void MaterialSemanticDifferenceFailsFreshReadback()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();
            var targetSemantic = fixture.Evidence.Live.Observations.Single(value =>
                value.Origin == IngredientObservationOrigin.CupCollectFreshReadback
                && value.ValuePath == "content.semantic");
            targetSemantic.ValueDigest = MigrationDigest.ComputeSha256("materially different content");

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M0, assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.CupCollectFreshReadback, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void WrongPublishingFieldIdentityFailsPrimaryOwnerGate()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.Evidence.Source.FieldId = "11111111-1111-1111-1111-111111111111";
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void PartialCapturedValueFailsClosedInsteadOfLookingEmpty()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.Evidence.Source.Availability = ContentTextAvailability.Partial;
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void MissingListSchemaDependencyFailsPrimaryOwnerGate()
        {
            var fixture = Fixture.CreatePublishing();
            fixture.Evidence.Source.DependencyIngredientIds = fixture.Evidence.Source.DependencyIngredientIds
                .Where(value => !value.StartsWith("list-schema:", StringComparison.Ordinal))
                .ToList();
            fixture.RefreshNormalization();
            fixture.AddSourceObservations();

            var assessment = fixture.Evaluate();

            Assert.IsNull(assessment.AttainedMaturity);
            AssertGate(assessment, IngredientMaturityGateCatalog.PrimaryOwner, IngredientMaturityGateStatus.Failed);
        }

        [TestMethod]
        public void WikiFieldUsesOrdinalBodySemanticsAndKeepsEscapedMarkup()
        {
            var fixture = Fixture.CreateWiki("<p>&lt;script&gt;safe text&lt;/script&gt;</p>");
            fixture.AddSourceObservations();
            fixture.AddTargetObservations();

            var assessment = fixture.Evaluate();

            Assert.AreEqual(IngredientMaturityLevel.M2, assessment.AttainedMaturity);
            Assert.AreEqual("<p>&lt;script&gt;safe text&lt;/script&gt;</p>", fixture.Normalized.NormalizedBodyValue);
            Assert.IsFalse(fixture.Normalized.NormalizedBodyValue.Contains("<script>", StringComparison.Ordinal));
            Assert.AreEqual(ContentTextEvidenceNormalizer.WikiSubtype, fixture.Normalized.Node.Subtype);
            Assert.AreEqual(ContentTextEvidenceNormalizer.WikiSourcePredicateId, fixture.Normalized.Node.SourcePredicateId);
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
            private static readonly DateTimeOffset FixedSourceObservedAt =
                new DateTimeOffset(2026, 9, 9, 19, 29, 35, TimeSpan.Zero);

            private Fixture()
            {
            }

            public FixtureData Data { get; private set; }

            public ContentTextMaturityEvidence Evidence { get; private set; }

            public IngredientMaturityEvaluationContext Context { get; private set; }

            public ContentTextNormalization Normalized { get; private set; }

            public DateTimeOffset SourceObservedAt => FixedSourceObservedAt;

            public static Fixture CreatePublishing()
            {
                var data = LoadFixture();
                var rawBytes = Encoding.UTF8.GetBytes(data.SemanticSlice.Raw);
                var source = new ContentTextSourceEvidence
                {
                    PageUrl = data.Source.PageUrl,
                    WebUrl = data.Source.WebUrl,
                    FileServerRelativeUrl = data.Source.FileServerRelativeUrl,
                    ListId = data.Source.ListId,
                    ListTitle = data.Source.ListTitle,
                    ListBaseTemplate = data.Source.ListBaseTemplate,
                    ItemId = data.Source.ItemId,
                    FileUniqueId = data.Source.FileUniqueId,
                    SourceVersion = data.Source.SourceVersion,
                    ContentTypeId = data.Source.ContentTypeId,
                    ContentTypeName = data.Source.ContentTypeName,
                    FieldId = data.Field.Id,
                    FieldInternalName = data.Field.InternalName,
                    FieldType = data.Field.Type,
                    FieldRequired = data.Field.Required,
                    FieldReadOnly = data.Field.ReadOnly,
                    FieldHidden = data.Field.Hidden,
                    FieldSealed = data.Field.Sealed,
                    Availability = ContentTextAvailability.Captured,
                    RawArtifact = new ArtifactReference
                    {
                        Sha256 = data.SemanticSlice.RawSha256,
                        Length = data.SemanticSlice.RawBytes,
                        MediaType = "text/html",
                        ContentEncoding = "utf-8"
                    },
                    RawArtifactBase64 = Convert.ToBase64String(rawBytes),
                    RawValueSha256 = data.SemanticSlice.RawSha256,
                    SemanticValueSha256 = data.SemanticSlice.SemanticSha256,
                    DependencyIngredientIds = new List<string>
                    {
                        "content-type:page",
                        "field:PublishingPageContent",
                        "list:pages",
                        "list-schema:pages",
                        "list-item:240"
                    },
                    EvidenceReferences = References("source-artifact.bin", "source-recheck.json")
                };
                return Create(data, source, data.IngredientId,
                    ContentTextEvidenceNormalizer.PublishingSubtype,
                    ContentTextEvidenceNormalizer.PublishingSourcePredicateId);
            }

            public static Fixture CreateWiki(string raw)
            {
                var data = LoadFixture();
                var bytes = Encoding.UTF8.GetBytes(raw);
                var digest = MigrationDigest.ComputeSha256(bytes);
                var source = new ContentTextSourceEvidence
                {
                    PageUrl = "https://microsoft.sharepoint.com/sites/ccd-redacted/SitePages/wiki-canary.aspx",
                    WebUrl = "https://microsoft.sharepoint.com/sites/ccd-redacted",
                    FileServerRelativeUrl = "/sites/ccd-redacted/SitePages/wiki-canary.aspx",
                    ListId = "22222222-2222-2222-2222-222222222222",
                    ListTitle = "Site Pages",
                    ListBaseTemplate = 119,
                    ItemId = 17,
                    FileUniqueId = "33333333-3333-3333-3333-333333333333",
                    SourceVersion = "\"{33333333-3333-3333-3333-333333333333},4\"",
                    ContentTypeId = "0x010108",
                    ContentTypeName = "Wiki Page",
                    FieldId = "c33527b4-d920-4587-b791-45024d00068a",
                    FieldInternalName = ContentTextEvidenceNormalizer.WikiFieldInternalName,
                    FieldType = "Note",
                    Availability = ContentTextAvailability.Captured,
                    RawArtifact = new ArtifactReference
                    {
                        Sha256 = digest,
                        Length = bytes.LongLength,
                        MediaType = "text/html",
                        ContentEncoding = "utf-8"
                    },
                    RawArtifactBase64 = Convert.ToBase64String(bytes),
                    RawValueSha256 = digest,
                    SemanticValueSha256 = digest,
                    DependencyIngredientIds = new List<string>
                    {
                        "content-type:wiki-page",
                        "field:WikiField",
                        "list:site-pages",
                        "list-schema:site-pages",
                        "list-item:17"
                    },
                    EvidenceReferences = References("wiki-source-artifact.bin", "wiki-source-recheck.json")
                };
                return Create(data, source,
                    "ccd.ingredient.content.text/v1:33333333-3333-3333-3333-333333333333:WikiField",
                    ContentTextEvidenceNormalizer.WikiSubtype,
                    ContentTextEvidenceNormalizer.WikiSourcePredicateId);
            }

            public void RefreshNormalization()
            {
                Context.Source.PageOrListItemIdentity = ContentTextEvidenceNormalizer.CreateSourceIdentity(Evidence.Source);
                Context.Source.SourceVersion = Evidence.Source.SourceVersion;
                Context.Source.SourceArtifactDigest = Evidence.Source.RawValueSha256;
                Normalized = ContentTextEvidenceNormalizer.Normalize(Context, Evidence.Source);
                Evidence.Live = new IngredientLiveEvidence
                {
                    SourceAuthenticated = true,
                    TargetFreshReadback = false,
                    SourceEvidenceReferences = References("source-live.json"),
                    TargetEvidenceReferences = References("target-live.json")
                };
            }

            public void AddSourceObservations()
            {
                AddObservations(
                    IngredientObservationOrigin.AuthenticatedSource,
                    Normalized.SourceObservationDigests,
                    SourceObservedAt,
                    "source");
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
                return new ContentTextMaturityContributor(Evidence).Contribute(Context);
            }

            public IngredientMaturityAssessment Evaluate()
            {
                return IngredientMaturityEvaluator.Evaluate(
                    Context,
                    new IngredientMaturityContributorCatalog(new[]
                    {
                        (IIngredientMaturityContributor)new ContentTextMaturityContributor(Evidence)
                    }));
            }

            private static Fixture Create(
                FixtureData data,
                ContentTextSourceEvidence source,
                string ingredientId,
                string subtype,
                string sourcePredicateId)
            {
                var fixture = new Fixture
                {
                    Data = data,
                    Evidence = new ContentTextMaturityEvidence { Source = source }
                };
                fixture.Context = new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = data.ClaimId,
                        WorkItemType = IngredientMaturityContract.CanonicalIngredientWorkItem,
                        Lane = ContentTextEvidenceNormalizer.Lane,
                        IngredientId = ingredientId,
                        Kind = PageIngredientKind.Content,
                        Subtype = subtype,
                        SemanticRole = ContentTextEvidenceNormalizer.SemanticRole,
                        SourcePredicateId = sourcePredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = ContentTextEvidenceNormalizer.CreateSourceIdentity(source),
                        SourceVersion = source.SourceVersion,
                        SourceArtifactDigest = source.RawValueSha256,
                        SourceSnapshotDigest = data.SourceClaim.SourceSnapshotSha256
                    },
                    Target = new IngredientMaturityTargetBinding
                    {
                        TargetProfile = "cupcollect-classic-page/v1",
                        TargetIdentity = "cupcollect:ccd-owned:content-text-canary"
                    },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "pnp-framework",
                        ProducerVersion = ContentTextEvidenceNormalizer.ContributorId,
                        ImplementationCommit = "aff892f119a20dd764af6b005087e7af5dad3a1c"
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        PnPMigrationOutcome = PageMigrationOutcome.ExecutableWithLoss,
                        Status = IngredientTechnicalStatus.Conditional,
                        ReasonCode = "TARGET_NATIVE_FIELD_CAPABILITY_PENDING"
                    }
                };
                fixture.RefreshNormalization();
                return fixture;
            }

            private void AddObservations(
                IngredientObservationOrigin origin,
                IReadOnlyDictionary<string, string> digests,
                DateTimeOffset observedAt,
                string prefix)
            {
                foreach (var pair in digests.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    Evidence.Live.Observations.Add(new IngredientValueObservation
                    {
                        ValuePath = pair.Key,
                        ValueDigest = pair.Value,
                        ObservedAtUtc = observedAt,
                        Origin = origin,
                        EvidenceReference = $"evidence/content-text/{prefix}-{pair.Key}.json"
                    });
                }
            }

            private static FixtureData LoadFixture([CallerFilePath] string callerFile = null)
            {
                var path = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(callerFile),
                    "../../../../Resources/Migration/Ingredients/ContentText/row-00029-semantic-slice.fixture.v1.json"));
                return JsonSerializer.Deserialize<FixtureData>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            private static List<string> References(params string[] names)
            {
                return names.Select(value => "evidence/content-text/" + value)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
            }
        }

        private sealed class FixtureData
        {
            public string Schema { get; set; }

            public string ClaimId { get; set; }

            public string IngredientId { get; set; }

            public SourceClaimData SourceClaim { get; set; }

            public SourceData Source { get; set; }

            public FieldData Field { get; set; }

            public SemanticSliceData SemanticSlice { get; set; }
        }

        private sealed class SourceClaimData
        {
            public string RawSha256 { get; set; }

            public string SemanticSha256 { get; set; }

            public string SourceSnapshotSha256 { get; set; }
        }

        private sealed class SourceData
        {
            public string PageUrl { get; set; }

            public string WebUrl { get; set; }

            public string FileServerRelativeUrl { get; set; }

            public string ListId { get; set; }

            public string ListTitle { get; set; }

            public int ListBaseTemplate { get; set; }

            public int ItemId { get; set; }

            public string FileUniqueId { get; set; }

            public string SourceVersion { get; set; }

            public string ContentTypeId { get; set; }

            public string ContentTypeName { get; set; }
        }

        private sealed class FieldData
        {
            public string Id { get; set; }

            public string InternalName { get; set; }

            public string Type { get; set; }

            public bool Required { get; set; }

            public bool ReadOnly { get; set; }

            public bool Hidden { get; set; }

            public bool Sealed { get; set; }
        }

        private sealed class SemanticSliceData
        {
            public string Provenance { get; set; }

            public string Raw { get; set; }

            public string Canonical { get; set; }

            public int RawBytes { get; set; }

            public string RawSha256 { get; set; }

            public string SemanticSha256 { get; set; }
        }
    }
}
