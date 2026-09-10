using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace PnP.Framework.Migration.Ingredients.WebPartInstance
{
    internal sealed class WebPartInstanceNormalization
    {
        public PageIngredientNode Node { get; set; }

        public string RawPayloadCanonicalJson { get; set; }

        public string RawPayloadSha256 { get; set; }

        public string NormalizedPropertiesCanonicalJson { get; set; }

        public string NormalizedPropertiesSha256 { get; set; }

        public string SemanticCanonicalJson { get; set; }

        public string SemanticSha256 { get; set; }

        public bool SourcePredicateMatched { get; set; }

        public IReadOnlyDictionary<string, string> SourceObservationDigests { get; set; }

        public IReadOnlyDictionary<string, string> TargetObservationDigests { get; set; }
    }

    internal static class WebPartInstanceEvidenceNormalizer
    {
        public const string ContributorId = "pnp-webpart-instance-maturity/v1";
        public const string Lane = "webpart.instance";
        public const string Subtype = "classic.webpart.instance";
        public const string SemanticRole = "persisted-instance";
        public const string SourcePredicateId = "webpart.instance.authenticated-persisted-export/v1";
        public const string SemanticSchema = "pnp-webpart-instance-semantic/v1";
        public const string ListViewFamily = "list-view";
        public const string ListViewTypeName = "Microsoft.SharePoint.WebPartPages.ListViewWebPart";
        public const string ListViewAssemblyIdentity = "Microsoft.SharePoint.Core, Version=16.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c";

        private static readonly string[] RequiredBooleanProperties =
        {
            "AllowClose", "AllowConnect", "AllowEdit", "AllowHide", "AllowMinimize",
            "AllowZoneChange", "Hidden"
        };

        private static readonly string[] RequiredNumberProperties =
        {
            "ChromeState", "ChromeType", "Direction", "ExportMode", "HelpMode", "PageType"
        };

        private static readonly string[] RequiredStringProperties =
        {
            "AuthorizationFilter", "CatalogIconImageUrl", "Description", "Height", "HelpUrl",
            "ImportErrorMessage", "ListId", "ListName", "ListViewXml", "MissingAssembly",
            "Title", "TitleIconImageUrl", "TitleUrl", "ViewContentTypeId", "ViewFlag",
            "ViewFlags", "WebId", "Width"
        };

        public static WebPartInstanceNormalization Normalize(
            IngredientMaturityEvaluationContext context,
            WebPartInstanceSourceEvidence evidence,
            WebPartInstanceTargetExpectation targetExpectation)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            var propertiesValid = TryCanonicalProperties(
                evidence.NormalizedPropertiesCanonicalJson,
                out var properties,
                out var canonicalProperties);
            var rawPayload = CreateNormalizedRestPayloadCanonical(evidence, canonicalProperties);
            var rawPayloadSha256 = MigrationDigest.ComputeSha256(rawPayload);
            var propertiesSha256 = MigrationDigest.ComputeSha256(canonicalProperties);
            var dependencies = (evidence.DependencyIngredientIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var semantic = new WebPartInstanceSemanticProjection
            {
                Schema = SemanticSchema,
                Instance = new WebPartInstanceIdentityProjection
                {
                    InstanceId = NormalizeGuid(evidence.InstanceId),
                    Scope = evidence.Scope,
                    Title = evidence.Title
                },
                Definition = new WebPartDefinitionProjection
                {
                    AssemblyIdentity = evidence.AssemblyIdentity,
                    Family = evidence.Family,
                    TypeName = evidence.DefinitionTypeName
                },
                Placement = new WebPartPlacementProjection
                {
                    ZoneId = evidence.ZoneId,
                    ZoneIndex = evidence.ZoneIndex
                },
                PersistedPayload = new WebPartPayloadProjection
                {
                    NormalizedProperties = properties,
                    NormalizedPropertiesSha256 = propertiesSha256,
                    NormalizedRestPayloadSha256 = rawPayloadSha256
                },
                Bindings = new WebPartBindingProjection
                {
                    BaseViewId = evidence.BaseViewId,
                    ListId = NormalizeGuid(evidence.BoundListId),
                    ViewId = NormalizeGuid(evidence.BoundViewId),
                    ViewType = evidence.ViewType
                },
                Dependencies = dependencies
            };
            var semanticCanonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var semanticSha256 = MigrationDigest.ComputeSha256(semanticCanonical);
            var sourceIdentity = CreateSourceIdentity(evidence);
            var sourcePredicateMatched = propertiesValid
                && evidence.Availability == WebPartInstanceAvailability.Captured
                && HasCompleteSourceBinding(evidence)
                && HasAuthoritativeDefinition(evidence)
                && HasTypedCompleteProperties(properties)
                && HasExactListAndViewBinding(evidence, properties)
                && HasRequiredDependencyClosure(dependencies)
                && IngredientIdMatchesInstance(context?.Identity?.IngredientId, evidence.InstanceId)
                && string.Equals(evidence.NormalizedRestPayloadSha256, rawPayloadSha256, StringComparison.OrdinalIgnoreCase);

            return new WebPartInstanceNormalization
            {
                Node = new PageIngredientNode
                {
                    Id = context?.Identity?.IngredientId,
                    Kind = PageIngredientKind.WebPart,
                    KindId = "WebPart",
                    Subtype = Subtype,
                    SemanticRole = SemanticRole,
                    SourcePredicateId = SourcePredicateId,
                    SourcePageOrListItemIdentity = sourceIdentity,
                    SourceVersionIdentity = evidence.SourceVersion,
                    PrimaryOwnerLane = Lane,
                    Label = evidence.Title,
                    HasContent = !string.IsNullOrWhiteSpace(canonicalProperties),
                    Ownership = PageIngredientOwnership.SourceOwned,
                    SourceAuthority = "authenticated-shared-webpart-definition-and-export",
                    EvidenceDigest = rawPayloadSha256,
                    RuntimeRequirement = "fresh-persisted-instance-readback-and-runtime-handoff",
                    EvidenceReferences = (evidence.EvidenceReferences ?? Array.Empty<string>()).ToList()
                },
                RawPayloadCanonicalJson = rawPayload,
                RawPayloadSha256 = rawPayloadSha256,
                NormalizedPropertiesCanonicalJson = canonicalProperties,
                NormalizedPropertiesSha256 = propertiesSha256,
                SemanticCanonicalJson = semanticCanonical,
                SemanticSha256 = semanticSha256,
                SourcePredicateMatched = sourcePredicateMatched,
                SourceObservationDigests = CreateSourceObservationDigests(evidence, rawPayloadSha256, propertiesSha256),
                TargetObservationDigests = CreateTargetObservationDigests(context, evidence, targetExpectation, propertiesSha256)
            };
        }

        public static string CreateSourceIdentity(WebPartInstanceSourceEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            return MigrationContractSerializer.SerializeCanonical(new WebPartSourceIdentityProjection
            {
                InstanceId = NormalizeGuid(evidence.InstanceId),
                PageItemId = evidence.PageItemId,
                PageListId = NormalizeGuid(evidence.PageListId),
                PageServerRelativeUrl = evidence.PageServerRelativeUrl,
                PageUniqueId = NormalizeGuid(evidence.PageUniqueId)
            });
        }

        public static string CreateNormalizedRestPayloadCanonical(
            WebPartInstanceSourceEvidence evidence,
            string canonicalProperties)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            return "{\"id\":" + JsonSerializer.Serialize(NormalizeGuid(evidence.InstanceId))
                + ",\"properties\":" + (string.IsNullOrWhiteSpace(canonicalProperties) ? "{}" : canonicalProperties)
                + ",\"title\":" + JsonSerializer.Serialize(evidence.Title)
                + ",\"type\":" + (evidence.RestTypeName == null ? "null" : JsonSerializer.Serialize(evidence.RestTypeName))
                + ",\"zoneIndex\":" + evidence.ZoneIndex.ToString(CultureInfo.InvariantCulture)
                + "}";
        }

        public static IngredientLiveEvidence ProjectLiveEvidence(
            IngredientLiveEvidence evidence,
            WebPartInstanceNormalization normalized,
            WebPartInstanceTargetExpectation targetExpectation)
        {
            if (evidence == null || normalized == null)
            {
                return evidence;
            }

            var observations = (evidence.Observations ?? Array.Empty<IngredientValueObservation>())
                .Where(value => value != null)
                .ToList();
            var sources = observations.Where(value => value.Origin == IngredientObservationOrigin.AuthenticatedSource).ToArray();
            var targets = observations.Where(value => value.Origin == IngredientObservationOrigin.CupCollectFreshReadback).ToArray();
            var substituted = evidence.HistoricalOrSyntheticSubstitution
                || observations.Any(value => value.Origin == IngredientObservationOrigin.Historical
                    || value.Origin == IngredientObservationOrigin.Synthetic);
            var newestSource = sources.Length == 0 ? default : sources.Max(value => value.ObservedAtUtc);
            var oldestTarget = targets.Length == 0 ? default : targets.Min(value => value.ObservedAtUtc);
            var targetBound = targetExpectation != null
                && Guid.TryParse(targetExpectation.TargetInstanceId, out _)
                && Guid.TryParse(targetExpectation.TargetListId, out _)
                && Guid.TryParse(targetExpectation.TargetViewId, out _);

            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated
                    && normalized.SourcePredicateMatched
                    && MatchesExpected(normalized.SourceObservationDigests, sources)
                    && !substituted,
                TargetFreshReadback = evidence.TargetFreshReadback
                    && targetBound
                    && MatchesExpected(normalized.TargetObservationDigests, targets)
                    && !substituted
                    && oldestTarget != default
                    && oldestTarget >= newestSource,
                HistoricalOrSyntheticSubstitution = substituted,
                Observations = observations,
                SourceEvidenceReferences = (evidence.SourceEvidenceReferences ?? Array.Empty<string>()).ToList(),
                TargetEvidenceReferences = (evidence.TargetEvidenceReferences ?? Array.Empty<string>()).ToList()
            };
        }

        private static IReadOnlyDictionary<string, string> CreateSourceObservationDigests(
            WebPartInstanceSourceEvidence evidence,
            string rawPayloadSha256,
            string propertiesSha256)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["binding.listId"] = ScalarDigest(NormalizeGuid(evidence.BoundListId)),
                ["binding.viewId"] = ScalarDigest(NormalizeGuid(evidence.BoundViewId)),
                ["definition.assembly"] = ScalarDigest(evidence.AssemblyIdentity),
                ["definition.type"] = ScalarDigest(evidence.DefinitionTypeName),
                ["instance.id"] = ScalarDigest(NormalizeGuid(evidence.InstanceId)),
                ["payload.raw"] = rawPayloadSha256,
                ["placement.scope"] = ScalarDigest(evidence.Scope),
                ["placement.zoneId"] = ScalarDigest(evidence.ZoneId),
                ["placement.zoneIndex"] = ScalarDigest(evidence.ZoneIndex),
                ["properties.normalized"] = propertiesSha256,
                ["source.version"] = ScalarDigest(evidence.SourceVersion)
            });
        }

        private static IReadOnlyDictionary<string, string> CreateTargetObservationDigests(
            IngredientMaturityEvaluationContext context,
            WebPartInstanceSourceEvidence evidence,
            WebPartInstanceTargetExpectation target,
            string propertiesSha256)
        {
            if (target == null)
            {
                return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
            }

            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["binding.listId"] = ScalarDigest(NormalizeGuid(target.TargetListId)),
                ["binding.targetIdentity"] = ScalarDigest(context?.Target?.TargetIdentity),
                ["binding.targetProfile"] = ScalarDigest(context?.Target?.TargetProfile),
                ["binding.viewId"] = ScalarDigest(NormalizeGuid(target.TargetViewId)),
                ["definition.assembly"] = ScalarDigest(evidence.AssemblyIdentity),
                ["definition.type"] = ScalarDigest(evidence.DefinitionTypeName),
                ["instance.id"] = ScalarDigest(NormalizeGuid(target.TargetInstanceId)),
                ["placement.scope"] = ScalarDigest(evidence.Scope),
                ["placement.zoneId"] = ScalarDigest(evidence.ZoneId),
                ["placement.zoneIndex"] = ScalarDigest(evidence.ZoneIndex),
                ["properties.normalized"] = propertiesSha256
            });
        }

        private static bool TryCanonicalProperties(
            string value,
            out JsonElement properties,
            out string canonical)
        {
            try
            {
                using (var document = JsonDocument.Parse(value ?? string.Empty))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        properties = EmptyProperties();
                        canonical = "{}";
                        return false;
                    }
                    properties = document.RootElement.Clone();
                    canonical = MigrationContractSerializer.SerializeCanonical(properties);
                    return string.Equals(value, canonical, StringComparison.Ordinal);
                }
            }
            catch (JsonException)
            {
                properties = EmptyProperties();
                canonical = "{}";
                return false;
            }
        }

        private static JsonElement EmptyProperties()
        {
            using (var document = JsonDocument.Parse("{}"))
            {
                return document.RootElement.Clone();
            }
        }

        private static bool HasCompleteSourceBinding(WebPartInstanceSourceEvidence evidence)
        {
            return Guid.TryParse(evidence.PageListId, out _)
                && evidence.PageItemId > 0
                && Guid.TryParse(evidence.PageUniqueId, out _)
                && !string.IsNullOrWhiteSpace(evidence.PageServerRelativeUrl)
                && evidence.PageServerRelativeUrl.StartsWith("/", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(evidence.SourceVersion)
                && Guid.TryParse(evidence.InstanceId, out _)
                && !string.IsNullOrWhiteSpace(evidence.Title)
                && (string.Equals(evidence.Scope, "shared", StringComparison.Ordinal)
                    || string.Equals(evidence.Scope, "personal", StringComparison.Ordinal))
                && !string.IsNullOrWhiteSpace(evidence.ZoneId)
                && evidence.ZoneIndex >= 0;
        }

        private static bool HasAuthoritativeDefinition(WebPartInstanceSourceEvidence evidence)
        {
            return string.Equals(evidence.Family, ListViewFamily, StringComparison.Ordinal)
                && string.Equals(evidence.DefinitionTypeName, ListViewTypeName, StringComparison.Ordinal)
                && string.Equals(evidence.AssemblyIdentity, ListViewAssemblyIdentity, StringComparison.Ordinal);
        }

        private static bool HasTypedCompleteProperties(JsonElement properties)
        {
            return RequiredBooleanProperties.All(name => HasKind(properties, name, JsonValueKind.True, JsonValueKind.False))
                && RequiredNumberProperties.All(name => HasKind(properties, name, JsonValueKind.Number))
                && RequiredStringProperties.All(name => HasKind(properties, name, JsonValueKind.String));
        }

        private static bool HasKind(JsonElement properties, string name, params JsonValueKind[] kinds)
        {
            return properties.TryGetProperty(name, out var value) && kinds.Contains(value.ValueKind);
        }

        private static bool HasExactListAndViewBinding(
            WebPartInstanceSourceEvidence evidence,
            JsonElement properties)
        {
            if (!Guid.TryParse(evidence.BoundListId, out var listId)
                || !Guid.TryParse(evidence.BoundViewId, out var viewId)
                || evidence.BaseViewId <= 0
                || string.IsNullOrWhiteSpace(evidence.ViewType)
                || !properties.TryGetProperty("ListId", out var listIdProperty)
                || !properties.TryGetProperty("ListName", out var listNameProperty)
                || !Guid.TryParse(listIdProperty.GetString(), out var propertyListId)
                || !Guid.TryParse((listNameProperty.GetString() ?? string.Empty).Trim().Trim('{', '}'), out var propertyListName)
                || propertyListId != listId
                || propertyListName != listId
                || !properties.TryGetProperty("ListViewXml", out var viewXmlProperty))
            {
                return false;
            }

            try
            {
                var root = XDocument.Parse(viewXmlProperty.GetString() ?? string.Empty).Root;
                return root != null
                    && Guid.TryParse(((string)root.Attribute("Name") ?? string.Empty).Trim().Trim('{', '}'), out var propertyViewId)
                    && propertyViewId == viewId
                    && string.Equals((string)root.Attribute("Type"), evidence.ViewType, StringComparison.Ordinal)
                    && int.TryParse((string)root.Attribute("BaseViewID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baseViewId)
                    && baseViewId == evidence.BaseViewId;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        private static bool HasRequiredDependencyClosure(IEnumerable<string> values)
        {
            var dependencies = values?.ToArray() ?? Array.Empty<string>();
            return HasPrefix(dependencies, "webpart-definition:")
                && HasPrefix(dependencies, "list:")
                && HasPrefix(dependencies, "list-schema:")
                && HasPrefix(dependencies, "view:")
                && HasPrefix(dependencies, "feature:")
                && HasPrefix(dependencies, "runtime-capability:");
        }

        private static bool HasPrefix(IEnumerable<string> values, string prefix)
        {
            return values.Any(value => value?.StartsWith(prefix, StringComparison.Ordinal) == true);
        }

        private static bool IngredientIdMatchesInstance(string ingredientId, string instanceId)
        {
            return Guid.TryParse(instanceId, out var parsed)
                && ingredientId?.EndsWith(":" + parsed.ToString("D"), StringComparison.Ordinal) == true;
        }

        private static bool MatchesExpected(
            IReadOnlyDictionary<string, string> expected,
            IEnumerable<IngredientValueObservation> observations)
        {
            if (expected == null || expected.Count == 0)
            {
                return false;
            }

            var values = observations.ToArray();
            return values.Length == expected.Count
                && values.All(value => expected.ContainsKey(value.ValuePath))
                && expected.All(pair =>
            {
                var matches = values.Where(value => string.Equals(value.ValuePath, pair.Key, StringComparison.Ordinal)).ToArray();
                return matches.Length == 1
                    && string.Equals(matches[0].ValueDigest, pair.Value, StringComparison.OrdinalIgnoreCase)
                    && matches[0].ObservedAtUtc != default
                    && !string.IsNullOrWhiteSpace(matches[0].EvidenceReference);
            });
        }

        private static string ScalarDigest(object value)
        {
            return MigrationDigest.ComputeSha256(value == null
                ? "null"
                : MigrationContractSerializer.SerializeCanonical(value));
        }

        private static string NormalizeGuid(string value)
        {
            return Guid.TryParse((value ?? string.Empty).Trim().Trim('{', '}'), out var parsed)
                ? parsed.ToString("D")
                : value;
        }

        private sealed class WebPartInstanceSemanticProjection
        {
            public string Schema { get; set; }

            public WebPartInstanceIdentityProjection Instance { get; set; }

            public WebPartDefinitionProjection Definition { get; set; }

            public WebPartPlacementProjection Placement { get; set; }

            public WebPartPayloadProjection PersistedPayload { get; set; }

            public WebPartBindingProjection Bindings { get; set; }

            public IEnumerable<string> Dependencies { get; set; }
        }

        private sealed class WebPartInstanceIdentityProjection
        {
            public string InstanceId { get; set; }

            public string Scope { get; set; }

            public string Title { get; set; }
        }

        private sealed class WebPartDefinitionProjection
        {
            public string AssemblyIdentity { get; set; }

            public string Family { get; set; }

            public string TypeName { get; set; }
        }

        private sealed class WebPartPlacementProjection
        {
            public string ZoneId { get; set; }

            public int ZoneIndex { get; set; }
        }

        private sealed class WebPartPayloadProjection
        {
            public JsonElement NormalizedProperties { get; set; }

            public string NormalizedPropertiesSha256 { get; set; }

            public string NormalizedRestPayloadSha256 { get; set; }
        }

        private sealed class WebPartBindingProjection
        {
            public int BaseViewId { get; set; }

            public string ListId { get; set; }

            public string ViewId { get; set; }

            public string ViewType { get; set; }
        }

        private sealed class WebPartSourceIdentityProjection
        {
            public string InstanceId { get; set; }

            public int PageItemId { get; set; }

            public string PageListId { get; set; }

            public string PageServerRelativeUrl { get; set; }

            public string PageUniqueId { get; set; }
        }
    }
}
