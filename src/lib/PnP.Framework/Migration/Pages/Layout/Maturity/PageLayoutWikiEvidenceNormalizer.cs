using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Discovery;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.PageLayout
{
    internal sealed class PageLayoutWikiNormalization
    {
        public PageIngredientNode Node { get; set; }

        public string SemanticCanonicalJson { get; set; }

        public bool SourcePredicateMatched { get; set; }

        public IReadOnlyDictionary<string, string> ValueDigests { get; set; }
    }

    internal static class PageLayoutWikiEvidenceNormalizer
    {
        public const string Lane = "page.layout";
        public const string Subtype = "layout.wiki";
        public const string SemanticRole = "wiki-page-family-layout-binding";
        public const string SourcePredicateId = "layout.wiki.source-binding/v1";
        public const string ContributorId = "pnp-page-layout-maturity/v1";
        public const string SemanticSchema = "pnp-page-layout-wiki-semantic/v1";
        public const string WikiContentTypeLineage = "0x010108";

        private static readonly string[] RequiredValuePaths =
        {
            "layout.listBaseTemplate",
            "layout.contentTypeLineage",
            "layout.contentTypeName",
            "layout.publishingPageLayout",
            "runtime.adapterId"
        };

        public static PageLayoutWikiNormalization Normalize(
            IngredientMaturityEvaluationContext context,
            PageLayoutWikiSourceEvidence evidence)
        {
            var snapshot = evidence?.Snapshot;
            var source = snapshot?.Source;
            var semantic = new WikiSemanticProjection
            {
                Schema = SemanticSchema,
                Source = new WikiSourceProjection
                {
                    FileServerRelativeUrl = source?.PageServerRelativeUrl,
                    ItemId = source?.ListItemId ?? 0,
                    ListId = evidence?.ListId,
                    UniqueId = source?.FileUniqueId.ToString("D"),
                    Version = evidence?.SourceVersion,
                    WebUrl = source?.WebUrl
                },
                Layout = new WikiLayoutProjection
                {
                    ContentTypeId = source?.ContentTypeId,
                    ContentTypeName = source?.ContentTypeName,
                    Kind = PageIngredientKind.Layout.ToString(),
                    ListBaseTemplate = snapshot?.LibraryBaseTemplate ?? 0,
                    PublishingPageLayout = evidence?.PublishingPageLayout,
                    Subtype = Subtype
                }
            };
            var canonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var actualIdentity = MigrationContractSerializer.SerializeCanonical(semantic.Source);
            var valid = source != null
                && !string.IsNullOrWhiteSpace(source.WebUrl)
                && !string.IsNullOrWhiteSpace(source.PageServerRelativeUrl)
                && !string.IsNullOrWhiteSpace(evidence?.ListId)
                && source.ListItemId > 0
                && source.FileUniqueId != Guid.Empty
                && !string.IsNullOrWhiteSpace(evidence.SourceVersion)
                && ClassicWikiPageDiscovery.IsClassicWikiLibrary(snapshot.LibraryBaseTemplate)
                && ClassicWikiPageDiscovery.IsClassicWikiContentType(source.ContentTypeId)
                && string.Equals(source.ContentTypeName, "Wiki Page", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(evidence.PublishingPageLayout)
                && snapshot.Runtime?.ResolutionState == PageRuntimeResolutionState.Resolved
                && string.Equals(snapshot.Runtime.AdapterId, PageRuntimeAdapterIds.Wiki, StringComparison.Ordinal);

            return new PageLayoutWikiNormalization
            {
                Node = new PageIngredientNode
                {
                    Id = context?.Identity?.IngredientId,
                    Kind = PageIngredientKind.Layout,
                    Subtype = Subtype,
                    SemanticRole = SemanticRole,
                    SourcePredicateId = SourcePredicateId,
                    SourcePageOrListItemIdentity = actualIdentity,
                    SourceVersionIdentity = evidence?.SourceVersion,
                    PrimaryOwnerLane = Lane,
                    HasContent = true,
                    Ownership = PageIngredientOwnership.SourceOwned,
                    SourceAuthority = "classic-wiki-capture/v1",
                    EvidenceDigest = context?.Source?.SourceArtifactDigest,
                    RuntimeRequirement = PageRuntimeAdapterIds.Wiki,
                    EvidenceReferences = (evidence?.EvidenceReferences ?? Array.Empty<string>()).ToList()
                },
                SemanticCanonicalJson = canonical,
                SourcePredicateMatched = valid,
                ValueDigests = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["layout.listBaseTemplate"] = ScalarDigest(snapshot?.LibraryBaseTemplate ?? 0),
                    ["layout.contentTypeLineage"] = ScalarDigest(NormalizeWikiContentTypeLineage(source?.ContentTypeId)),
                    ["layout.contentTypeName"] = ScalarDigest(source?.ContentTypeName),
                    ["layout.publishingPageLayout"] = ScalarDigest(evidence?.PublishingPageLayout),
                    ["runtime.adapterId"] = ScalarDigest(snapshot?.Runtime?.AdapterId)
                }
            };
        }

        public static IngredientLiveEvidence ProjectTargetReadback(
            IngredientMaturityEvaluationContext context,
            PageLayoutWikiSourceEvidence source,
            IngredientLiveEvidence live,
            PageLayoutWikiTargetReadbackEvidence target)
        {
            if (target == null)
            {
                return live;
            }

            var observations = (live?.Observations ?? Array.Empty<IngredientValueObservation>())
                .Where(value => value != null
                    && value.Origin != IngredientObservationOrigin.CupCollectFreshReadback)
                .ToList();
            var sourceObservedAtUtc = observations
                .Where(value => value.Origin == IngredientObservationOrigin.AuthenticatedSource)
                .Select(value => value.ObservedAtUtc)
                .DefaultIfEmpty()
                .Max();
            var targetReferences = (target.EvidenceReferences ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var sourceIdentity = source?.Snapshot?.Source;
            var bindingMatched = context?.Identity != null
                && context.Source != null
                && context.Target != null
                && context.Producer != null
                && string.Equals(target.ClaimId, context.Identity.ClaimId, StringComparison.Ordinal)
                && string.Equals(target.IngredientId, context.Identity.IngredientId, StringComparison.Ordinal)
                && string.Equals(target.SourceListId, source?.ListId, StringComparison.OrdinalIgnoreCase)
                && target.SourceItemId == sourceIdentity?.ListItemId
                && string.Equals(target.SourceFileUniqueId, sourceIdentity?.FileUniqueId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                && string.Equals(target.SourceVersion, source?.SourceVersion, StringComparison.Ordinal)
                && string.Equals(target.ImplementationCommit, context.Producer.ImplementationCommit, StringComparison.Ordinal)
                && string.Equals(target.TargetProfile, context.Target.TargetProfile, StringComparison.Ordinal)
                && IngredientMaturityEvaluator.IsSha256(target.PlanDigest)
                && !string.IsNullOrWhiteSpace(context.Target.TargetIdentity)
                && string.Equals(target.TargetPath, context.Target.TargetIdentity, StringComparison.OrdinalIgnoreCase)
                && target.ObservedAtUtc != default
                && (sourceObservedAtUtc == default || target.ObservedAtUtc >= sourceObservedAtUtc)
                && targetReferences.Count > 0;

            var targetValues = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["layout.listBaseTemplate"] = target.ListBaseTemplate,
                ["layout.contentTypeLineage"] = NormalizeWikiContentTypeLineage(target.ContentTypeId),
                ["layout.contentTypeName"] = target.ContentTypeName,
                ["layout.publishingPageLayout"] = target.PublishingPageLayout,
                ["runtime.adapterId"] = target.RuntimeAdapterId
            };
            foreach (var value in targetValues.Where(value => targetReferences.Count > 0))
            {
                observations.Add(new IngredientValueObservation
                {
                    ValuePath = value.Key,
                    ValueDigest = ScalarDigest(value.Value),
                    ObservedAtUtc = target.ObservedAtUtc,
                    Origin = IngredientObservationOrigin.CupCollectFreshReadback,
                    EvidenceReference = targetReferences[0] + "#" + value.Key
                });
            }

            return new IngredientLiveEvidence
            {
                SourceAuthenticated = live?.SourceAuthenticated == true,
                TargetFreshReadback = bindingMatched,
                HistoricalOrSyntheticSubstitution = live?.HistoricalOrSyntheticSubstitution == true,
                Observations = observations,
                SourceEvidenceReferences = (live?.SourceEvidenceReferences ?? Array.Empty<string>()).ToList(),
                TargetEvidenceReferences = targetReferences
            };
        }

        public static IngredientLiveEvidence ProjectLiveEvidence(
            IngredientLiveEvidence evidence,
            PageLayoutWikiNormalization normalized)
        {
            if (evidence == null)
            {
                return null;
            }

            var observations = (evidence.Observations ?? Array.Empty<IngredientValueObservation>())
                .Where(value => value != null)
                .ToList();
            var sourceComplete = ValuesMatch(
                observations,
                IngredientObservationOrigin.AuthenticatedSource,
                normalized?.ValueDigests);
            var targetComplete = ValuesMatch(
                observations,
                IngredientObservationOrigin.CupCollectFreshReadback,
                normalized?.ValueDigests);

            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated && sourceComplete,
                TargetFreshReadback = evidence.TargetFreshReadback && targetComplete,
                HistoricalOrSyntheticSubstitution = evidence.HistoricalOrSyntheticSubstitution,
                Observations = observations,
                SourceEvidenceReferences = (evidence.SourceEvidenceReferences ?? Array.Empty<string>()).ToList(),
                TargetEvidenceReferences = (evidence.TargetEvidenceReferences ?? Array.Empty<string>()).ToList()
            };
        }

        private static bool ValuesMatch(
            IEnumerable<IngredientValueObservation> observations,
            IngredientObservationOrigin origin,
            IReadOnlyDictionary<string, string> expected)
        {
            if (expected == null)
            {
                return false;
            }

            foreach (var path in RequiredValuePaths)
            {
                var candidates = observations.Where(value => value.Origin == origin
                    && string.Equals(value.ValuePath, path, StringComparison.Ordinal)).ToArray();
                if (candidates.Length != 1
                    || !expected.TryGetValue(path, out var digest)
                    || !string.Equals(candidates[0].ValueDigest, digest, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static string ScalarDigest(object value)
        {
            var canonical = value == null ? "null" : MigrationContractSerializer.SerializeCanonical(value);
            return MigrationDigest.ComputeSha256(canonical);
        }

        private static string NormalizeWikiContentTypeLineage(string contentTypeId)
        {
            return ClassicWikiPageDiscovery.IsClassicWikiContentType(contentTypeId)
                ? WikiContentTypeLineage
                : contentTypeId;
        }

        private sealed class WikiSemanticProjection
        {
            public string Schema { get; set; }

            public WikiSourceProjection Source { get; set; }

            public WikiLayoutProjection Layout { get; set; }
        }

        private sealed class WikiSourceProjection
        {
            public string FileServerRelativeUrl { get; set; }

            public int ItemId { get; set; }

            public string ListId { get; set; }

            public string UniqueId { get; set; }

            public string Version { get; set; }

            public string WebUrl { get; set; }
        }

        private sealed class WikiLayoutProjection
        {
            public string ContentTypeId { get; set; }

            public string ContentTypeName { get; set; }

            public string Kind { get; set; }

            public int ListBaseTemplate { get; set; }

            public string PublishingPageLayout { get; set; }

            public string Subtype { get; set; }
        }
    }
}
