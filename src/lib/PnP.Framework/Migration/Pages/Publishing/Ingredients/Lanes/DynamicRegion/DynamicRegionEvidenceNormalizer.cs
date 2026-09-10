using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.DynamicRegion
{
    internal sealed class DynamicRegionNormalizedEvidence
    {
        public PageIngredientNode Node { get; set; }

        public string SemanticCanonicalJson { get; set; }

        public IReadOnlyDictionary<string, string> ValueDigests { get; set; }

        public bool SourcePredicateMatched { get; set; }
    }

    internal static class DynamicRegionEvidenceNormalizer
    {
        public const string ContributorId = "pnp.dynamic-region-maturity/v1";
        public const string Lane = "dynamic.region";
        public const string Subtype = "runtime.dynamic-region";
        public const string SemanticRole = "rendered-dynamic-region";
        public const string SourcePredicateId = "dynamic-region.result-script-webpart/v1";
        public const string ResultScriptWebPartType = "Microsoft.Office.Server.Search.WebControls.ResultScriptWebPart";

        public static DynamicRegionNormalizedEvidence Normalize(
            IngredientMaturityEvaluationContext context,
            DynamicRegionSourceEvidence source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var canonical = MigrationContractSerializer.SerializeCanonical(source.RawRegion);
            var canonicalBytes = Encoding.UTF8.GetBytes(canonical);
            var semanticDigest = MigrationDigest.ComputeSha256(canonicalBytes);
            var sourceIdentity = CreateSourceIdentity(source);
            var ingredientId = context?.Identity?.IngredientId;
            var sourcePredicateMatched = IsComplete(source, canonicalBytes, semanticDigest)
                && !string.IsNullOrWhiteSpace(ingredientId)
                && RegionString(source.RawRegion, "regionId")?.EndsWith(source.ProviderInstanceId, StringComparison.OrdinalIgnoreCase) == true
                && string.Equals(RegionString(source.RawRegion, "owner", "instanceId"), source.ProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(RegionString(source.RawRegion, "owner", "inferredControlType"), source.ControlType, StringComparison.Ordinal)
                && string.Equals(RegionString(source.RawRegion, "placement", "domLocator"), source.Boundary, StringComparison.Ordinal)
                && string.Equals(RegionString(source.RawRegion, "placement", "zone"), source.Zone, StringComparison.Ordinal)
                && RegionInt32(source.RawRegion, "placement", "order") == source.Order
                && string.Equals(RegionString(source.RawRegion, "definition", "configDigest"), source.ConfigSha256, StringComparison.OrdinalIgnoreCase);

            return new DynamicRegionNormalizedEvidence
            {
                Node = new PageIngredientNode
                {
                    Id = ingredientId,
                    Kind = PageIngredientKind.Runtime,
                    KindId = "Runtime",
                    Subtype = Subtype,
                    SemanticRole = SemanticRole,
                    SourcePredicateId = SourcePredicateId,
                    SourcePageOrListItemIdentity = sourceIdentity,
                    SourceVersionIdentity = source.SourceVersion,
                    PrimaryOwnerLane = Lane,
                    Label = "Search Results dynamic region",
                    HasContent = true,
                    Ownership = PageIngredientOwnership.TargetRuntime,
                    SourceAuthority = "authenticated-source-derived-typed-projection",
                    EvidenceDigest = source.SourceArtifactSha256,
                    RuntimeRequirement = "fresh-native-runtime-reconciliation",
                    EvidenceReferences = source.EvidenceReferences?.ToList() ?? new List<string>()
                },
                SemanticCanonicalJson = canonical,
                ValueDigests = CreateValueDigests(source.RawRegion),
                SourcePredicateMatched = sourcePredicateMatched
            };
        }

        public static string CreateSourceIdentity(DynamicRegionSourceEvidence source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return MigrationContractSerializer.SerializeCanonical(new
            {
                fileServerRelativeUrl = source.FileServerRelativeUrl,
                itemId = source.ItemId,
                listId = source.ListId,
                uniqueId = source.UniqueId,
                version = source.SourceVersion,
                webUrl = source.WebUrl
            });
        }

        public static IngredientLiveEvidence ProjectLiveEvidence(
            IngredientLiveEvidence evidence,
            DynamicRegionNormalizedEvidence normalized)
        {
            if (evidence == null || normalized == null)
            {
                return evidence;
            }

            var observations = (evidence.Observations ?? new List<IngredientValueObservation>())
                .Where(value => value != null)
                .ToList();
            var sources = observations.Where(value => value.Origin == IngredientObservationOrigin.AuthenticatedSource).ToArray();
            var targets = observations.Where(value => value.Origin == IngredientObservationOrigin.CupCollectFreshReadback).ToArray();
            var expected = normalized.ValueDigests;
            var sourceMatches = MatchesExpected(expected, sources);
            var targetMatches = MatchesExpected(expected, targets);
            var newestSource = sources.Length == 0 ? default : sources.Max(value => value.ObservedAtUtc);
            var oldestTarget = targets.Length == 0 ? default : targets.Min(value => value.ObservedAtUtc);
            var substituted = evidence.HistoricalOrSyntheticSubstitution
                || observations.Any(value => value.Origin == IngredientObservationOrigin.Historical
                    || value.Origin == IngredientObservationOrigin.Synthetic);

            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated && sourceMatches && !substituted,
                TargetFreshReadback = evidence.TargetFreshReadback
                    && targetMatches
                    && !substituted
                    && oldestTarget != default
                    && oldestTarget >= newestSource,
                HistoricalOrSyntheticSubstitution = substituted,
                Observations = observations,
                SourceEvidenceReferences = evidence.SourceEvidenceReferences?.ToList() ?? new List<string>(),
                TargetEvidenceReferences = evidence.TargetEvidenceReferences?.ToList() ?? new List<string>()
            };
        }

        private static bool IsComplete(DynamicRegionSourceEvidence source, byte[] canonicalBytes, string semanticDigest)
        {
            return Uri.TryCreate(source.PageUrl, UriKind.Absolute, out var pageUrl)
                && string.Equals(pageUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(pageUrl.Host, @"^microsoft[a-z0-9-]*\.sharepoint\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && !string.IsNullOrWhiteSpace(source.WebUrl)
                && !string.IsNullOrWhiteSpace(source.FileServerRelativeUrl)
                && Guid.TryParse(source.ListId, out _)
                && source.ItemId > 0
                && Guid.TryParse(source.UniqueId, out _)
                && !string.IsNullOrWhiteSpace(source.SourceVersion)
                && IsSha256(source.SourceArtifactSha256)
                && !string.IsNullOrWhiteSpace(source.ProviderIngredientId)
                && Guid.TryParse(source.ProviderInstanceId, out _)
                && string.Equals(source.ControlType, ResultScriptWebPartType, StringComparison.Ordinal)
                && IsSha256(source.ConfigSha256)
                && !string.IsNullOrWhiteSpace(source.Boundary)
                && !string.IsNullOrWhiteSpace(source.Zone)
                && source.Order >= 0
                && source.ObservedAtUtc != default
                && source.RawRegion.ValueKind == JsonValueKind.Object
                && source.RawArtifactLength == canonicalBytes.LongLength
                && string.Equals(MigrationDigest.ComputeSha256(canonicalBytes), source.RawArtifactSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(semanticDigest, source.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(source.LegacyLineTerminatedSemanticDigest, source.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                && source.Dependencies?.Contains(source.ProviderIngredientId, StringComparer.Ordinal) == true;
        }

        private static IReadOnlyDictionary<string, string> CreateValueDigests(JsonElement region)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["region.regionId"] = Digest(region.GetProperty("regionId")),
                ["region.owner.instanceId"] = Digest(region.GetProperty("owner").GetProperty("instanceId")),
                ["region.owner.controlType"] = Digest(region.GetProperty("owner").GetProperty("inferredControlType")),
                ["region.placement.zone"] = Digest(region.GetProperty("placement").GetProperty("zone")),
                ["region.placement.order"] = Digest(region.GetProperty("placement").GetProperty("order")),
                ["region.placement.boundary"] = Digest(region.GetProperty("placement").GetProperty("domLocator")),
                ["region.definition.config"] = Digest(region.GetProperty("definition").GetProperty("configDigest")),
                ["region.definition.providerBinding"] = Digest(region.GetProperty("definition").GetProperty("providerBinding")),
                ["region.definition.pagination"] = Digest(region.GetProperty("definition").GetProperty("pagination")),
                ["region.observation.state"] = Digest(region.GetProperty("observation").GetProperty("state")),
                ["region.observation.observable"] = Digest(region.GetProperty("observation").GetProperty("observableDigest"))
            };
        }

        private static bool MatchesExpected(
            IReadOnlyDictionary<string, string> expected,
            IEnumerable<IngredientValueObservation> observations)
        {
            var values = observations.ToArray();
            if (values.Length != expected.Count
                || values.GroupBy(value => value.ValuePath, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                return false;
            }

            return expected.All(pair => values.Any(value =>
                string.Equals(value.ValuePath, pair.Key, StringComparison.Ordinal)
                && string.Equals(value.ValueDigest, pair.Value, StringComparison.OrdinalIgnoreCase)
                && value.ObservedAtUtc != default
                && !string.IsNullOrWhiteSpace(value.EvidenceReference)));
        }

        private static string Digest(JsonElement value)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value));
        }

        private static string RegionString(JsonElement root, params string[] path)
        {
            var current = root;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    return null;
                }
            }
            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }

        private static int? RegionInt32(JsonElement root, params string[] path)
        {
            var current = root;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    return null;
                }
            }
            return current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value) ? value : (int?)null;
        }

        private static bool IsSha256(string value)
        {
            return value?.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
        }
    }
}
