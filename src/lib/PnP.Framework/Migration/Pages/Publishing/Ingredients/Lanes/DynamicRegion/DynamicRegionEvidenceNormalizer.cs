using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        public string CanonicalIngredientId { get; set; }

        public string CanonicalProviderIngredientId { get; set; }
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
            var ingredientId = CreateCanonicalIngredientId(source);
            var providerIngredientId = CreateCanonicalProviderIngredientId(source);
            var sourcePredicateMatched = IsComplete(source, canonicalBytes, semanticDigest)
                && string.Equals(context?.Identity?.IngredientId, ingredientId, StringComparison.Ordinal)
                && string.Equals(source.ProviderIngredientId, providerIngredientId, StringComparison.Ordinal)
                && RegionString(source.RawRegion, "regionId")?.EndsWith(source.ProviderInstanceId, StringComparison.OrdinalIgnoreCase) == true
                && string.Equals(RegionString(source.RawRegion, "owner", "instanceId"), source.ProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(RegionString(source.RawRegion, "owner", "inferredControlType"), source.ControlType, StringComparison.Ordinal)
                && string.Equals(RegionString(source.RawRegion, "placement", "domLocator"), source.Boundary, StringComparison.Ordinal)
                && string.Equals(RegionString(source.RawRegion, "placement", "zone"), source.Zone, StringComparison.Ordinal)
                && RegionInt32(source.RawRegion, "placement", "order") == source.Order
                && string.Equals(RegionString(source.RawRegion, "definition", "configDigest"), source.ConfigSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(RegionString(source.RawRegion, "definition", "providerBinding", "contextualListId"), source.ListId, StringComparison.OrdinalIgnoreCase)
                && RegionInt32(source.RawRegion, "definition", "providerBinding", "contextualItemId") == source.ItemId;

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
                SourcePredicateMatched = sourcePredicateMatched,
                CanonicalIngredientId = ingredientId,
                CanonicalProviderIngredientId = providerIngredientId
            };
        }

        public static string CreateCanonicalIngredientId(DynamicRegionSourceEvidence source)
        {
            if (source == null || !Guid.TryParse(source.UniqueId, out var pageId))
            {
                return null;
            }
            var regionId = RegionString(source.RawRegion, "regionId");
            if (string.IsNullOrWhiteSpace(regionId)
                || !Regex.IsMatch(regionId, @"^\d{5}:[0-9a-fA-F-]{36}$", RegexOptions.CultureInvariant)
                || !regionId.EndsWith(source.ProviderInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return $"ccd.ingredient.dynamic.region/v1:{pageId:D}:{regionId}".ToLowerInvariant();
        }

        public static string CreateCanonicalProviderIngredientId(DynamicRegionSourceEvidence source)
        {
            if (source == null
                || !Guid.TryParse(source.UniqueId, out var pageId)
                || !Guid.TryParse(source.ProviderInstanceId, out var providerId))
            {
                return null;
            }
            return $"ccd.ingredient.webpart.instance/v1:{pageId:D}:{providerId:D}".ToLowerInvariant();
        }

        public static string CreateSourceIdentity(DynamicRegionSourceEvidence source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return MigrationContractSerializer.SerializeCanonical(new
            {
                pageUrl = source.PageUrl,
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
            DynamicRegionNormalizedEvidence normalized,
            IngredientMaturityEvaluationContext context,
            DynamicRegionSourceEvidence source,
            DynamicRegionTargetEvidence target)
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
            var targetBound = IsTargetBound(context, source, target);
            var substituted = evidence.HistoricalOrSyntheticSubstitution
                || observations.Any(value => value.Origin == IngredientObservationOrigin.Historical
                    || value.Origin == IngredientObservationOrigin.Synthetic);

            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated && sourceMatches && !substituted,
                TargetFreshReadback = evidence.TargetFreshReadback
                    && targetMatches
                    && targetBound
                    && !substituted
                    && oldestTarget != default
                    && oldestTarget >= newestSource
                    && oldestTarget >= target.ObservedAtUtc,
                HistoricalOrSyntheticSubstitution = substituted,
                Observations = observations,
                SourceEvidenceReferences = evidence.SourceEvidenceReferences?.ToList() ?? new List<string>(),
                TargetEvidenceReferences = evidence.TargetEvidenceReferences?.ToList() ?? new List<string>()
            };
        }

        public static bool IsPlanBound(
            IngredientMaturityEvaluationContext context,
            DynamicRegionNormalizedEvidence normalized,
            DynamicRegionSourceEvidence source,
            DynamicRegionTargetEvidence target,
            IngredientPlanEvidence evidence,
            out string failureReason)
        {
            failureReason = null;
            var plan = evidence?.Plan;
            var actions = (plan?.IngredientActions ?? Array.Empty<PageIngredientAction>()).Where(value =>
                string.Equals(value?.IngredientId, normalized?.CanonicalIngredientId, StringComparison.Ordinal)).ToArray();
            var nodes = (plan?.IngredientGraph?.Nodes ?? Array.Empty<PageIngredientNode>()).Where(value =>
                string.Equals(value?.Id, normalized?.CanonicalIngredientId, StringComparison.Ordinal)).ToArray();
            var providers = (plan?.IngredientGraph?.ExternalReferences ?? Array.Empty<PageIngredientExternalReference>()).Where(value =>
                string.Equals(value?.IngredientId, normalized?.CanonicalProviderIngredientId, StringComparison.Ordinal)).ToArray();
            var edges = (plan?.IngredientGraph?.Edges ?? Array.Empty<PageIngredientEdge>()).Where(value =>
                string.Equals(value?.FromIngredientId, normalized?.CanonicalIngredientId, StringComparison.Ordinal)
                && string.Equals(value?.ToIngredientId, normalized?.CanonicalProviderIngredientId, StringComparison.Ordinal)).ToArray();
            var action = actions.Length == 1 ? actions[0] : null;
            var node = nodes.Length == 1 ? nodes[0] : null;
            var provider = providers.Length == 1 ? providers[0] : null;
            var edge = edges.Length == 1 ? edges[0] : null;
            var valid = normalized != null
                && normalized.SourcePredicateMatched
                && IsTargetBound(context, source, target)
                && evidence != null
                && string.Equals(evidence.IngredientId, normalized.CanonicalIngredientId, StringComparison.Ordinal)
                && string.Equals(context?.Identity?.IngredientId, normalized.CanonicalIngredientId, StringComparison.Ordinal)
                && string.Equals(evidence.ExpectedSourceSnapshotDigest, context?.Source?.SourceSnapshotDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan?.SourceSnapshotDigest, context?.Source?.SourceSnapshotDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan?.SourceWebUrl, source?.WebUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan?.SourcePageServerRelativeUrl, source?.FileServerRelativeUrl, StringComparison.Ordinal)
                && node != null
                && string.Equals(node.SourcePageOrListItemIdentity, context?.Source?.PageOrListItemIdentity, StringComparison.Ordinal)
                && string.Equals(node.SourceVersionIdentity, context?.Source?.SourceVersion, StringComparison.Ordinal)
                && string.Equals(node.EvidenceDigest, context?.Source?.SourceArtifactDigest, StringComparison.OrdinalIgnoreCase)
                && action != null
                && string.Equals(action.TargetIdentity, context?.Target?.TargetIdentity, StringComparison.Ordinal)
                && (action.VerificationAssertions?.Contains("provider-mapping:" + target?.ProviderMappingDigest, StringComparer.Ordinal) == true)
                && provider != null
                && string.Equals(provider.SharedPlanDigest, target?.ReviewedProviderPlanDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(provider.LogicalActionKey, target?.ReviewedProviderActionId, StringComparison.Ordinal)
                && string.Equals(provider.TargetIdentity, target?.TargetProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(provider.EvidenceDigest, target?.ProviderMappingDigest, StringComparison.OrdinalIgnoreCase)
                && edge != null
                && (edge.Relationship == PageIngredientRelationship.DependsOn
                    || edge.Relationship == PageIngredientRelationship.RendersThrough)
                && edge.Requirement != PageIngredientRequirement.Optional;
            if (!valid)
            {
                failureReason = "The dynamic region plan is not bound to the current source, canonical provider handoff, target, action, and dependency edge.";
            }
            return valid;
        }

        public static bool IsOperationalBound(
            IngredientMaturityEvaluationContext context,
            DynamicRegionNormalizedEvidence normalized,
            DynamicRegionSourceEvidence source,
            DynamicRegionTargetEvidence target,
            IngredientPlanEvidence plan,
            IngredientOperationalEvidence operational,
            out string failureReason)
        {
            if (!IsPlanBound(context, normalized, source, target, plan, out failureReason))
            {
                return false;
            }
            var signature = operational?.ActionSignature;
            var plannedActions = (plan.Plan?.IngredientActions ?? Array.Empty<PageIngredientAction>()).Where(value =>
                string.Equals(value?.IngredientId, normalized.CanonicalIngredientId, StringComparison.Ordinal)).ToArray();
            var plannedAction = plannedActions.Length == 1 ? plannedActions[0] : null;
            var valid = operational != null
                && operational.Plan != null
                && string.Equals(operational.IngredientId, normalized.CanonicalIngredientId, StringComparison.Ordinal)
                && string.Equals(operational.AdmittedPlanDigest, plan.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(PnP.Framework.Migration.Pages.Publishing.Packaging.PublishingPageDigest.ComputePlanDigest(operational.Plan), plan.ExpectedPlanDigest, StringComparison.OrdinalIgnoreCase)
                && signature != null
                && string.Equals(signature.ActionId, plannedAction?.ActionId, StringComparison.Ordinal)
                && string.Equals(signature.TargetIdentity, context?.Target?.TargetIdentity, StringComparison.Ordinal)
                && string.Equals(signature.SourceEvidenceDigest, context?.Source?.SourceArtifactDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(signature.SemanticDigest, source?.SemanticDigest, StringComparison.OrdinalIgnoreCase)
                && (signature.DependencySignatures?.Contains(target?.ProviderMappingDigest, StringComparer.OrdinalIgnoreCase) == true)
                && (signature.DependencySignatures?.Contains(target?.ReviewedProviderPlanDigest, StringComparer.OrdinalIgnoreCase) == true)
                && operational.RuntimeRequired
                && operational.RuntimeReceipt != null;
            if (!valid)
            {
                failureReason = "The dynamic region operation is not bound to the current plan, action, source evidence, provider handoff, target, and required runtime receipt.";
            }
            return valid;
        }

        public static string ComputeTargetMappingDigest(
            DynamicRegionSourceEvidence source,
            DynamicRegionTargetEvidence target)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                sourceUrl = source?.PageUrl,
                targetUrl = target?.PageUrl,
                sourceInstanceId = target?.SourceProviderInstanceId,
                sourceVersion = source?.SourceVersion,
                targetListId = target?.ListId,
                targetItemId = target?.ItemId
            }));
        }

        public static string CreateTargetPageIdentity(DynamicRegionTargetEvidence target)
        {
            if (target == null)
            {
                return null;
            }
            return MigrationContractSerializer.SerializeCanonical(new
            {
                fileServerRelativeUrl = target.FileServerRelativeUrl,
                itemId = target.ItemId,
                listId = target.ListId,
                pageUrl = target.PageUrl,
                uniqueId = target.UniqueId,
                version = target.TargetVersion,
                webUrl = target.WebUrl
            });
        }

        private static bool IsTargetBound(
            IngredientMaturityEvaluationContext context,
            DynamicRegionSourceEvidence source,
            DynamicRegionTargetEvidence target)
        {
            return target != null
                && string.Equals(target.TargetProfile, context?.Target?.TargetProfile, StringComparison.Ordinal)
                && string.Equals(target.TargetIdentity, context?.Target?.TargetIdentity, StringComparison.Ordinal)
                && PageLocatorMatches(target.PageUrl, target.WebUrl, target.FileServerRelativeUrl, false)
                && Guid.TryParse(target.ListId, out _)
                && target.ItemId > 0
                && Guid.TryParse(target.UniqueId, out _)
                && !string.IsNullOrWhiteSpace(target.TargetVersion)
                && string.Equals(target.SourceProviderIngredientId, source?.ProviderIngredientId, StringComparison.Ordinal)
                && string.Equals(target.SourceProviderInstanceId, source?.ProviderInstanceId, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(target.TargetProviderInstanceId, out _)
                && IsSha256(target.ReviewedProviderPlanDigest)
                && !string.IsNullOrWhiteSpace(target.ReviewedProviderActionId)
                && target.ObservedAtUtc != default
                && source != null
                && target.ObservedAtUtc >= source.ObservedAtUtc
                && string.Equals(target.ProviderMappingDigest, ComputeTargetMappingDigest(source, target), StringComparison.OrdinalIgnoreCase)
                && target.EvidenceReferences?.Any(value => !string.IsNullOrWhiteSpace(value)) == true;
        }

        private static bool IsComplete(DynamicRegionSourceEvidence source, byte[] canonicalBytes, string semanticDigest)
        {
            return PageLocatorMatches(source.PageUrl, source.WebUrl, source.FileServerRelativeUrl, true)
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
                ["region.regionId"] = Digest(RequiredProperty(region, "regionId")),
                ["region.owner.instanceId"] = Digest(RequiredProperty(region, "owner", "instanceId")),
                ["region.owner.controlType"] = Digest(RequiredProperty(region, "owner", "inferredControlType")),
                ["region.placement.zone"] = Digest(RequiredProperty(region, "placement", "zone")),
                ["region.placement.order"] = Digest(RequiredProperty(region, "placement", "order")),
                ["region.placement.boundary"] = Digest(RequiredProperty(region, "placement", "domLocator")),
                ["region.definition.config"] = Digest(RequiredProperty(region, "definition", "configDigest")),
                ["region.definition.providerBinding"] = Digest(RequiredProperty(region, "definition", "providerBinding")),
                ["region.definition.pagination"] = Digest(RequiredProperty(region, "definition", "pagination")),
                ["region.observation.state"] = Digest(RequiredProperty(region, "observation", "state")),
                ["region.observation.observable"] = Digest(RequiredProperty(region, "observation", "observableDigest"))
            };
        }

        private static JsonElement RequiredProperty(JsonElement root, params string[] path)
        {
            var current = root;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    throw new InvalidDataException("The dynamic region evidence is missing required property '" + string.Join(".", path) + "'.");
                }
            }
            return current;
        }

        private static bool PageLocatorMatches(string pageUrlValue, string webUrlValue, string fileServerRelativeUrl, bool requireMicrosoftSource)
        {
            if (!Uri.TryCreate(pageUrlValue, UriKind.Absolute, out var pageUrl)
                || !Uri.TryCreate(webUrlValue, UriKind.Absolute, out var webUrl)
                || !string.Equals(pageUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(webUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(pageUrl.Host, webUrl.Host, StringComparison.OrdinalIgnoreCase)
                || !Regex.IsMatch(pageUrl.Host, requireMicrosoftSource
                    ? @"^microsoft[a-z0-9-]*\.sharepoint\.com$"
                    : @"^[a-z0-9-]+\.sharepoint\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || string.IsNullOrWhiteSpace(fileServerRelativeUrl)
                || !fileServerRelativeUrl.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }
            var pagePath = Uri.UnescapeDataString(pageUrl.AbsolutePath).TrimEnd('/');
            var filePath = Uri.UnescapeDataString(fileServerRelativeUrl).TrimEnd('/');
            var webPath = Uri.UnescapeDataString(webUrl.AbsolutePath).TrimEnd('/');
            return string.Equals(pagePath, filePath, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(webPath)
                    || filePath.StartsWith(webPath + "/", StringComparison.OrdinalIgnoreCase));
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
