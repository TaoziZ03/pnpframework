using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Ingredients.ResourceScript
{
    internal sealed class ResourceScriptNormalization
    {
        public PublishingPageCaptureBundle Snapshot { get; set; }

        public PageIngredientNode Node { get; set; }

        public string SemanticCanonicalJson { get; set; }

        public string SemanticDigest { get; set; }

        public IReadOnlyDictionary<string, string> SourceObservationDigests { get; set; }

        public IReadOnlyDictionary<string, string> TargetObservationDigests { get; set; }
    }

    internal static class ResourceScriptEvidenceNormalizer
    {
        public const string ContributorId = "pnp-resource-script-maturity/v1";
        public const string Lane = "resource.script";
        public const string SemanticSchema = "pnp-resource-script-semantic/v1";

        public static ResourceScriptNormalization Normalize(
            IngredientMaturityEvaluationContext context,
            ResourceScriptMaturityEvidence evidence)
        {
            if (evidence?.Snapshot == null)
            {
                throw new ArgumentNullException(nameof(evidence), "The resource.script source snapshot is required.");
            }
            if (evidence.Source == null)
            {
                throw new ArgumentNullException(nameof(evidence), "The typed JSLink source evidence is required.");
            }

            var snapshot = Clone(evidence.Snapshot);
            var source = Clone(evidence.Source);
            var handler = new ResourceScriptIdentityHandler();
            var catalog = ResourceScriptIngredientCatalog.Create();
            var envelope = PublishingPageIngredientEvidenceEnvelope.Create(
                handler,
                PublishingPageJsLinkReferenceEvidence.SchemaVersion,
                source.IngredientId,
                source,
                evidence.EvidenceReferences);
            snapshot.IngredientEvidence = catalog.OrderEvidence(new[] { envelope }).ToList();

            var graph = PublishingPageIngredientGraphProjector.Project(snapshot, catalog);
            var node = graph.Nodes.Single(value => string.Equals(value?.Id, source.IngredientId, StringComparison.Ordinal));
            var semantic = new ResourceScriptSemanticProjection
            {
                Schema = SemanticSchema,
                IngredientId = source.IngredientId,
                Subtype = source.Subtype,
                SemanticRole = source.SemanticRole,
                ReferenceForm = source.ReferenceForm,
                RawLocator = source.Reference.OriginalValue,
                NormalizedLocator = source.Reference.SourceAbsoluteUrl,
                SourceServerRelativeUrl = source.Reference.SourceServerRelativeUrl,
                HostWebPartId = source.HostWebPartId.ToString("D"),
                HostWebPartOrder = source.HostWebPartOrder,
                PersistedReferenceOrder = source.PersistedReferenceOrder,
                ContentSha256 = source.Reference.ContentSha256,
                ContentLength = source.Reference.ContentLength
            };
            var semanticCanonical = MigrationContractSerializer.SerializeCanonical(semantic);
            var semanticDigest = MigrationDigest.ComputeSha256(semanticCanonical);

            return new ResourceScriptNormalization
            {
                Snapshot = snapshot,
                Node = node,
                SemanticCanonicalJson = semanticCanonical,
                SemanticDigest = semanticDigest,
                SourceObservationDigests = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["binding.sourceIdentity"] = ScalarDigest(node.SourcePageOrListItemIdentity),
                    ["binding.sourceVersion"] = ScalarDigest(node.SourceVersionIdentity),
                    ["binding.hostWebPart"] = ScalarDigest(source.HostWebPartId.ToString("D")),
                    ["binding.referenceForm"] = ScalarDigest(source.ReferenceForm),
                    ["binding.rawLocator"] = ScalarDigest(source.Reference.OriginalValue),
                    ["binding.persistedOrder"] = ScalarDigest(source.PersistedReferenceOrder?.ToString()),
                    ["content.raw"] = source.Reference.ContentSha256,
                    ["content.semantic"] = semanticDigest
                },
                TargetObservationDigests = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["binding.targetIdentity"] = ScalarDigest(context?.Target?.TargetIdentity),
                    ["binding.targetProfile"] = ScalarDigest(context?.Target?.TargetProfile),
                    ["binding.referenceForm"] = ScalarDigest(source.ReferenceForm),
                    ["binding.persistedOrder"] = ScalarDigest(source.PersistedReferenceOrder?.ToString()),
                    ["content.raw"] = source.Reference.ContentSha256,
                    ["content.semantic"] = semanticDigest
                }
            };
        }

        public static IngredientLiveEvidence ProjectLiveEvidence(
            IngredientLiveEvidence evidence,
            ResourceScriptNormalization normalized)
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

            return new IngredientLiveEvidence
            {
                SourceAuthenticated = evidence.SourceAuthenticated
                    && MatchesExpected(normalized.SourceObservationDigests, sources)
                    && !substituted,
                TargetFreshReadback = evidence.TargetFreshReadback
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

        private static bool MatchesExpected(
            IReadOnlyDictionary<string, string> expected,
            IEnumerable<IngredientValueObservation> observations)
        {
            var values = observations.ToArray();
            return expected.All(pair =>
            {
                var matches = values.Where(value => string.Equals(value.ValuePath, pair.Key, StringComparison.Ordinal)).ToArray();
                return matches.Length == 1
                    && string.Equals(matches[0].ValueDigest, pair.Value, StringComparison.OrdinalIgnoreCase)
                    && matches[0].ObservedAtUtc != default
                    && !string.IsNullOrWhiteSpace(matches[0].EvidenceReference);
            });
        }

        private static string ScalarDigest(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : MigrationDigest.ComputeSha256(value);
        }

        private static T Clone<T>(T value) where T : class
        {
            return MigrationContractSerializer.Deserialize<T>(MigrationContractSerializer.SerializeCanonical(value));
        }

        private sealed class ResourceScriptSemanticProjection
        {
            public string Schema { get; set; }
            public string IngredientId { get; set; }
            public string Subtype { get; set; }
            public string SemanticRole { get; set; }
            public string ReferenceForm { get; set; }
            public string RawLocator { get; set; }
            public string NormalizedLocator { get; set; }
            public string SourceServerRelativeUrl { get; set; }
            public string HostWebPartId { get; set; }
            public int? HostWebPartOrder { get; set; }
            public int? PersistedReferenceOrder { get; set; }
            public string ContentSha256 { get; set; }
            public long ContentLength { get; set; }
        }
    }
}
