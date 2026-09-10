using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Pages.Content.Maturity
{
    internal sealed class ContentTextNormalization
    {
        public PageIngredientNode Node { get; set; }

        public string SemanticCanonicalJson { get; set; }

        public string SemanticProjectionSha256 { get; set; }

        public string NormalizedBodyValue { get; set; }

        public bool SourcePredicateMatched { get; set; }

        public IReadOnlyDictionary<string, string> SourceObservationDigests { get; set; }

        public IReadOnlyDictionary<string, string> TargetObservationDigests { get; set; }
    }

    internal static class ContentTextEvidenceNormalizer
    {
        public const string ContributorId = "pnp-content-text-maturity/v1";
        public const string Lane = "content.text";
        public const string PublishingSubtype = "content.publishing-page-content";
        public const string WikiSubtype = "content.wiki-field";
        public const string SemanticRole = "page-body";
        public const string PublishingSourcePredicateId = "content.text.publishing-page-field/v1";
        public const string WikiSourcePredicateId = "content.text.wiki-field/v1";
        public const string PublishingFieldInternalName = "PublishingPageContent";
        public const string WikiFieldInternalName = "WikiField";
        public const string PublishingFieldId = "f55c4d88-1f2e-4ad9-aaa8-819af4ee7ee8";
        public const string SemanticSchema = "pnp-content-text-semantic/v1";
        private const string CanonicalIngredientPrefix = "ccd.ingredient.content.text/v1:";

        public static ContentTextNormalization Normalize(
            IngredientMaturityEvaluationContext context,
            ContentTextSourceEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            var field = ClassifyField(evidence.FieldInternalName);
            var rawBytes = DecodeBase64(evidence.RawArtifactBase64);
            var utf8Valid = TryDecodeUtf8(rawBytes, out var rawValue);
            var normalizedBody = field.IsPublishing
                ? PublishingPageContentStorageCanonicalizer.Canonicalize(rawValue)
                : rawValue;
            var rawDigest = rawBytes == null ? null : MigrationDigest.ComputeSha256(rawBytes);
            var semanticValueDigest = rawBytes == null
                ? null
                : MigrationDigest.ComputeSha256(Encoding.UTF8.GetBytes(normalizedBody));
            var sourceIdentity = CreateSourceIdentity(evidence);
            var semantic = new ContentTextSemanticProjection
            {
                Schema = SemanticSchema,
                Binding = new ContentTextBindingProjection
                {
                    ContentTypeId = evidence.ContentTypeId,
                    ContentTypeName = evidence.ContentTypeName,
                    FieldId = NormalizeGuid(evidence.FieldId),
                    FieldInternalName = evidence.FieldInternalName,
                    FieldType = evidence.FieldType,
                    FileServerRelativeUrl = evidence.FileServerRelativeUrl,
                    FileUniqueId = NormalizeGuid(evidence.FileUniqueId),
                    ItemId = evidence.ItemId,
                    ListBaseTemplate = evidence.ListBaseTemplate,
                    ListId = NormalizeGuid(evidence.ListId),
                    PageUrl = evidence.PageUrl,
                    SourceVersion = evidence.SourceVersion,
                    Subtype = field.Subtype
                },
                Value = new ContentTextValueProjection
                {
                    Body = normalizedBody,
                    FieldHidden = evidence.FieldHidden,
                    FieldReadOnly = evidence.FieldReadOnly,
                    FieldRequired = evidence.FieldRequired,
                    FieldSealed = evidence.FieldSealed,
                    RawSha256 = rawDigest,
                    SemanticSha256 = semanticValueDigest
                }
            };
            var semanticCanonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var semanticProjectionDigest = MigrationDigest.ComputeSha256(semanticCanonical);
            var canonicalIngredientId = CreateCanonicalIngredientId(evidence);
            var valid = field.IsSupported
                && string.Equals(context?.Identity?.IngredientId, canonicalIngredientId, StringComparison.Ordinal)
                && IsCompleteBinding(evidence)
                && evidence.Availability == ContentTextAvailability.Captured
                && rawBytes != null
                && rawBytes.Length > 0
                && utf8Valid
                && evidence.RawArtifact != null
                && evidence.RawArtifact.Availability == EvidenceAvailability.Captured
                && evidence.RawArtifact.Length == rawBytes.LongLength
                && string.Equals(evidence.RawArtifact.Sha256, rawDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.RawValueSha256, rawDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(evidence.SemanticValueSha256, semanticValueDigest, StringComparison.OrdinalIgnoreCase)
                && HasRequiredDependencyClosure(evidence)
                && FieldContractMatches(evidence, field.IsPublishing);

            return new ContentTextNormalization
            {
                Node = new PageIngredientNode
                {
                    Id = canonicalIngredientId,
                    Kind = PageIngredientKind.Content,
                    KindId = "Content",
                    Subtype = field.Subtype,
                    SemanticRole = SemanticRole,
                    SourcePredicateId = field.SourcePredicateId,
                    SourcePageOrListItemIdentity = sourceIdentity,
                    SourceVersionIdentity = evidence.SourceVersion,
                    PrimaryOwnerLane = Lane,
                    Label = evidence.FieldInternalName,
                    HasContent = rawBytes != null && rawBytes.Length > 0,
                    Ownership = PageIngredientOwnership.SourceOwned,
                    SourceAuthority = "authenticated-list-item-field-value",
                    EvidenceDigest = rawDigest,
                    RuntimeRequirement = "fresh-persisted-field-readback",
                    EvidenceReferences = (evidence.EvidenceReferences ?? Array.Empty<string>()).ToList()
                },
                SemanticCanonicalJson = semanticCanonical,
                SemanticProjectionSha256 = semanticProjectionDigest,
                NormalizedBodyValue = normalizedBody,
                SourcePredicateMatched = valid,
                SourceObservationDigests = CreateSourceObservationDigests(context, evidence, rawDigest, semanticValueDigest),
                TargetObservationDigests = CreateTargetObservationDigests(context, evidence, semanticValueDigest)
            };
        }

        public static string CreateSourceIdentity(ContentTextSourceEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            return MigrationContractSerializer.SerializeCanonical(new ContentTextSourceIdentityProjection
            {
                FieldInternalName = evidence.FieldInternalName,
                FileServerRelativeUrl = evidence.FileServerRelativeUrl,
                FileUniqueId = NormalizeGuid(evidence.FileUniqueId),
                ItemId = evidence.ItemId,
                ListId = NormalizeGuid(evidence.ListId),
                PageUrl = evidence.PageUrl
            });
        }

        public static string CreateCanonicalIngredientId(ContentTextSourceEvidence evidence)
        {
            if (evidence == null || !Guid.TryParse(evidence.FileUniqueId, out var fileUniqueId)
                || string.IsNullOrWhiteSpace(evidence.FieldInternalName))
            {
                return null;
            }

            return CanonicalIngredientPrefix
                + fileUniqueId.ToString("D")
                + ":"
                + evidence.FieldInternalName;
        }

        public static IngredientLiveEvidence ProjectLiveEvidence(
            IngredientLiveEvidence evidence,
            ContentTextNormalization normalized)
        {
            if (evidence == null || normalized == null)
            {
                return evidence;
            }

            var observations = (evidence.Observations ?? Array.Empty<IngredientValueObservation>()).ToList();
            var nonNullObservations = observations.Where(value => value != null).ToArray();
            var sources = nonNullObservations.Where(value => value.Origin == IngredientObservationOrigin.AuthenticatedSource).ToArray();
            var targets = nonNullObservations.Where(value => value.Origin == IngredientObservationOrigin.CupCollectFreshReadback).ToArray();
            var substituted = evidence.HistoricalOrSyntheticSubstitution
                || nonNullObservations.Any(value => value.Origin == IngredientObservationOrigin.Historical
                    || value.Origin == IngredientObservationOrigin.Synthetic);
            var sourceMatches = MatchesExpected(normalized.SourceObservationDigests, sources);
            var targetMatches = MatchesExpected(normalized.TargetObservationDigests, targets);
            var newestSource = sources.Length == 0 ? default : sources.Max(value => value.ObservedAtUtc);
            var oldestTarget = targets.Length == 0 ? default : targets.Min(value => value.ObservedAtUtc);

            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated && sourceMatches && !substituted,
                TargetFreshReadback = evidence.TargetFreshReadback
                    && targetMatches
                    && !substituted
                    && oldestTarget != default
                    && oldestTarget >= newestSource,
                HistoricalOrSyntheticSubstitution = substituted,
                ReadbackStartedAtUtc = evidence.ReadbackStartedAtUtc,
                Observations = observations,
                SourceEvidenceReferences = (evidence.SourceEvidenceReferences ?? Array.Empty<string>()).ToList(),
                TargetEvidenceReferences = (evidence.TargetEvidenceReferences ?? Array.Empty<string>()).ToList()
            };
        }

        private static IReadOnlyDictionary<string, string> CreateSourceObservationDigests(
            IngredientMaturityEvaluationContext context,
            ContentTextSourceEvidence evidence,
            string rawDigest,
            string semanticDigest)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["binding.fieldId"] = ScalarDigest(NormalizeGuid(evidence.FieldId)),
                ["binding.fieldInternalName"] = ScalarDigest(evidence.FieldInternalName),
                ["binding.fieldSchema"] = CreateFieldSchemaDigest(evidence),
                ["content.semantic"] = semanticDigest
            };
        }

        private static IReadOnlyDictionary<string, string> CreateTargetObservationDigests(
            IngredientMaturityEvaluationContext context,
            ContentTextSourceEvidence evidence,
            string semanticDigest)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["binding.fieldId"] = ScalarDigest(NormalizeGuid(evidence.FieldId)),
                ["binding.fieldInternalName"] = ScalarDigest(evidence.FieldInternalName),
                ["binding.fieldSchema"] = CreateFieldSchemaDigest(evidence),
                ["content.semantic"] = semanticDigest
            };
        }

        private static bool MatchesExpected(
            IReadOnlyDictionary<string, string> expected,
            IEnumerable<IngredientValueObservation> observations)
        {
            if (expected == null)
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

        private static bool IsCompleteBinding(ContentTextSourceEvidence evidence)
        {
            return Uri.TryCreate(evidence.PageUrl, UriKind.Absolute, out var pageUrl)
                && string.Equals(pageUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && pageUrl.Host.StartsWith("microsoft", StringComparison.OrdinalIgnoreCase)
                && pageUrl.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(evidence.WebUrl, UriKind.Absolute, out var webUrl)
                && string.Equals(webUrl.Host, pageUrl.Host, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(evidence.FileServerRelativeUrl)
                && string.Equals(pageUrl.AbsolutePath, evidence.FileServerRelativeUrl, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(evidence.ListId, out _)
                && !string.IsNullOrWhiteSpace(evidence.ListTitle)
                && evidence.ItemId > 0
                && Guid.TryParse(evidence.FileUniqueId, out _)
                && Guid.TryParse(evidence.FieldId, out _)
                && !string.IsNullOrWhiteSpace(evidence.SourceVersion)
                && !string.IsNullOrWhiteSpace(evidence.ContentTypeId)
                && !string.IsNullOrWhiteSpace(evidence.ContentTypeName)
                && evidence.ListBaseTemplate > 0;
        }

        private static bool FieldContractMatches(ContentTextSourceEvidence evidence, bool publishing)
        {
            if (!string.Equals(evidence.FieldType, publishing ? "HTML" : "Note", StringComparison.OrdinalIgnoreCase)
                || evidence.FieldReadOnly
                || evidence.FieldHidden)
            {
                return false;
            }

            var expectedFieldId = publishing
                ? PublishingFieldId
                : BuiltInFieldId.WikiField.ToString("D");
            return string.Equals(NormalizeGuid(evidence.FieldId), expectedFieldId, StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] DecodeBase64(string value)
        {
            try
            {
                return string.IsNullOrWhiteSpace(value) ? null : Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static bool TryDecodeUtf8(byte[] bytes, out string value)
        {
            if (bytes == null)
            {
                value = string.Empty;
                return false;
            }

            try
            {
                value = new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                value = string.Empty;
                return false;
            }
        }

        private static bool HasRequiredDependencyClosure(ContentTextSourceEvidence evidence)
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["list"] = NormalizeGuid(evidence.ListId),
                ["list-schema"] = NormalizeGuid(evidence.ListId),
                ["content-type"] = evidence.ContentTypeId,
                ["field"] = NormalizeGuid(evidence.FieldId)
            };
            var dependencies = (evidence.Dependencies ?? Array.Empty<ContentTextDependencyEvidence>()).ToArray();
            var legacyIds = (evidence.DependencyIngredientIds ?? Array.Empty<string>()).ToArray();
            var evidenceReferences = (evidence.EvidenceReferences ?? Array.Empty<string>()).ToArray();

            return dependencies.Length == expected.Count
                && dependencies.All(value => value != null)
                && expected.All(pair =>
                {
                    var matches = dependencies.Where(value => string.Equals(value.Kind, pair.Key, StringComparison.Ordinal)).ToArray();
                    var canonicalId = pair.Key + ":" + pair.Value;
                    var dependency = matches.SingleOrDefault();
                    var expectedProviderSchema = CreateDependencySchemaCanonicalJson(evidence, pair.Key, pair.Value);
                    var providerBytes = DecodeBase64(dependency?.ProviderArtifactBase64);
                    var providerDigest = providerBytes == null ? null : MigrationDigest.ComputeSha256(providerBytes);
                    return matches.Length == 1
                        && string.Equals(dependency.ProviderIdentity, pair.Value, StringComparison.OrdinalIgnoreCase)
                        && dependency.ProviderArtifact != null
                        && dependency.ProviderArtifact.Availability == EvidenceAvailability.Captured
                        && providerBytes != null
                        && string.Equals(Encoding.UTF8.GetString(providerBytes), expectedProviderSchema, StringComparison.Ordinal)
                        && dependency.ProviderArtifact.Length == providerBytes.LongLength
                        && string.Equals(dependency.ProviderArtifact.Sha256, providerDigest, StringComparison.OrdinalIgnoreCase)
                        && IngredientMaturityEvaluator.IsSha256(dependency.EvidenceDigest)
                        && string.Equals(
                            dependency.EvidenceDigest,
                            CreateDependencyEvidenceDigest(
                                evidence,
                                pair.Key,
                                pair.Value,
                                dependency.EvidenceReference,
                                providerDigest),
                            StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(dependency.EvidenceReference)
                        && evidenceReferences.Count(value => string.Equals(
                            value,
                            dependency.EvidenceReference,
                            StringComparison.Ordinal)) == 1
                        && legacyIds.Count(value => string.Equals(value, canonicalId, StringComparison.OrdinalIgnoreCase)) == 1;
                });
        }

        public static string CreateDependencyEvidenceDigest(
            ContentTextSourceEvidence evidence,
            string kind,
            string providerIdentity)
        {
            var schema = CreateDependencySchemaCanonicalJson(evidence, kind, providerIdentity);
            if (schema == null)
            {
                return null;
            }

            return CreateDependencyEvidenceDigest(
                evidence,
                kind,
                providerIdentity,
                "evidence/content-text/dependency-" + kind + ".json",
                MigrationDigest.ComputeSha256(Encoding.UTF8.GetBytes(schema)));
        }

        public static string CreateDependencyEvidenceDigest(
            ContentTextSourceEvidence evidence,
            string kind,
            string providerIdentity,
            string evidenceReference,
            string providerArtifactDigest)
        {
            if (evidence == null || string.IsNullOrWhiteSpace(evidenceReference)
                || !IngredientMaturityEvaluator.IsSha256(providerArtifactDigest))
            {
                return null;
            }

            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                new ContentTextDependencyBindingProjection
                {
                    ContentTypeId = evidence.ContentTypeId,
                    EvidenceReference = evidenceReference,
                    FieldId = NormalizeGuid(evidence.FieldId),
                    Kind = kind,
                    ListId = NormalizeGuid(evidence.ListId),
                    ProviderArtifactDigest = providerArtifactDigest,
                    ProviderIdentity = providerIdentity,
                    SourceVersion = evidence.SourceVersion
                }));
        }

        public static string CreateDependencySchemaCanonicalJson(
            ContentTextSourceEvidence evidence,
            string kind,
            string providerIdentity)
        {
            if (evidence == null || string.IsNullOrWhiteSpace(kind)
                || string.IsNullOrWhiteSpace(providerIdentity))
            {
                return null;
            }

            switch (kind)
            {
                case "list":
                    return MigrationContractSerializer.SerializeCanonical(new ContentTextListProviderSchemaProjection
                    {
                        ListBaseTemplate = evidence.ListBaseTemplate,
                        ListId = NormalizeGuid(evidence.ListId),
                        ListTitle = evidence.ListTitle,
                        ProviderIdentity = providerIdentity
                    });
                case "list-schema":
                    return MigrationContractSerializer.SerializeCanonical(new ContentTextListSchemaProviderProjection
                    {
                        ContentTypeId = evidence.ContentTypeId,
                        FieldSchemaDigest = CreateFieldSchemaDigest(evidence),
                        ListBaseTemplate = evidence.ListBaseTemplate,
                        ListId = NormalizeGuid(evidence.ListId),
                        ProviderIdentity = providerIdentity
                    });
                case "content-type":
                    return MigrationContractSerializer.SerializeCanonical(new ContentTextContentTypeProviderSchemaProjection
                    {
                        ContentTypeId = evidence.ContentTypeId,
                        ContentTypeName = evidence.ContentTypeName,
                        ListId = NormalizeGuid(evidence.ListId),
                        ProviderIdentity = providerIdentity
                    });
                case "field":
                    return MigrationContractSerializer.SerializeCanonical(new ContentTextFieldProviderSchemaProjection
                    {
                        ContentTypeId = evidence.ContentTypeId,
                        FieldSchemaDigest = CreateFieldSchemaDigest(evidence),
                        ListId = NormalizeGuid(evidence.ListId),
                        ProviderIdentity = providerIdentity
                    });
                default:
                    return null;
            }
        }

        private static string CreateFieldSchemaDigest(ContentTextSourceEvidence evidence)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                new ContentTextFieldSchemaProjection
                {
                    FieldHidden = evidence.FieldHidden,
                    FieldId = NormalizeGuid(evidence.FieldId),
                    FieldInternalName = evidence.FieldInternalName,
                    FieldReadOnly = evidence.FieldReadOnly,
                    FieldRequired = evidence.FieldRequired,
                    FieldSealed = evidence.FieldSealed,
                    FieldType = evidence.FieldType
                }));
        }

        private static string ScalarDigest(object value)
        {
            var canonical = value == null ? "null" : MigrationContractSerializer.SerializeCanonical(value);
            return MigrationDigest.ComputeSha256(canonical);
        }

        private static string NormalizeGuid(string value)
        {
            return Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : value;
        }

        private static (bool IsSupported, bool IsPublishing, string Subtype, string SourcePredicateId) ClassifyField(string internalName)
        {
            if (string.Equals(internalName, PublishingFieldInternalName, StringComparison.Ordinal))
            {
                return (true, true, PublishingSubtype, PublishingSourcePredicateId);
            }
            if (string.Equals(internalName, WikiFieldInternalName, StringComparison.Ordinal))
            {
                return (true, false, WikiSubtype, WikiSourcePredicateId);
            }
            return (false, false, "content.unsupported-body-field", "content.text.unsupported-field/v1");
        }

        private sealed class ContentTextSemanticProjection
        {
            public string Schema { get; set; }

            public ContentTextBindingProjection Binding { get; set; }

            public ContentTextValueProjection Value { get; set; }
        }

        private sealed class ContentTextBindingProjection
        {
            public string ContentTypeId { get; set; }

            public string ContentTypeName { get; set; }

            public string FieldId { get; set; }

            public string FieldInternalName { get; set; }

            public string FieldType { get; set; }

            public string FileServerRelativeUrl { get; set; }

            public string FileUniqueId { get; set; }

            public int ItemId { get; set; }

            public int ListBaseTemplate { get; set; }

            public string ListId { get; set; }

            public string PageUrl { get; set; }

            public string SourceVersion { get; set; }

            public string Subtype { get; set; }
        }

        private sealed class ContentTextValueProjection
        {
            public string Body { get; set; }

            public bool FieldHidden { get; set; }

            public bool FieldReadOnly { get; set; }

            public bool FieldRequired { get; set; }

            public bool FieldSealed { get; set; }

            public string RawSha256 { get; set; }

            public string SemanticSha256 { get; set; }
        }

        private sealed class ContentTextFieldSchemaProjection
        {
            public bool FieldHidden { get; set; }

            public string FieldId { get; set; }

            public string FieldInternalName { get; set; }

            public bool FieldReadOnly { get; set; }

            public bool FieldRequired { get; set; }

            public bool FieldSealed { get; set; }

            public string FieldType { get; set; }
        }

        private sealed class ContentTextDependencyBindingProjection
        {
            public string ContentTypeId { get; set; }

            public string EvidenceReference { get; set; }

            public string FieldId { get; set; }

            public string Kind { get; set; }

            public string ListId { get; set; }

            public string ProviderArtifactDigest { get; set; }

            public string ProviderIdentity { get; set; }

            public string SourceVersion { get; set; }
        }

        private sealed class ContentTextListProviderSchemaProjection
        {
            public int ListBaseTemplate { get; set; }

            public string ListId { get; set; }

            public string ListTitle { get; set; }

            public string ProviderIdentity { get; set; }
        }

        private sealed class ContentTextListSchemaProviderProjection
        {
            public string ContentTypeId { get; set; }

            public string FieldSchemaDigest { get; set; }

            public int ListBaseTemplate { get; set; }

            public string ListId { get; set; }

            public string ProviderIdentity { get; set; }
        }

        private sealed class ContentTextContentTypeProviderSchemaProjection
        {
            public string ContentTypeId { get; set; }

            public string ContentTypeName { get; set; }

            public string ListId { get; set; }

            public string ProviderIdentity { get; set; }
        }

        private sealed class ContentTextFieldProviderSchemaProjection
        {
            public string ContentTypeId { get; set; }

            public string FieldSchemaDigest { get; set; }

            public string ListId { get; set; }

            public string ProviderIdentity { get; set; }
        }

        private sealed class ContentTextSourceIdentityProjection
        {
            public string FieldInternalName { get; set; }

            public string FileServerRelativeUrl { get; set; }

            public string FileUniqueId { get; set; }

            public int ItemId { get; set; }

            public string ListId { get; set; }

            public string PageUrl { get; set; }
        }
    }
}
