using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    public static class ScaleRunManifestValidator
    {
        private static readonly ScaleRunStage[] OrderedStages =
        {
            ScaleRunStage.Collect,
            ScaleRunStage.Plan,
            ScaleRunStage.Repro,
            ScaleRunStage.TargetRecapture,
            ScaleRunStage.PackageCompare,
            ScaleRunStage.BrowserAcceptance
        };

        public static ScaleRunManifest Seal(ScaleRunManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            manifest.Pages = (manifest.Pages ?? new List<ScaleRunPage>())
                .OrderBy(value => value.Ordinal)
                .ThenBy(value => value.PageKey, StringComparer.Ordinal)
                .ToList();
            if (manifest.Policy != null)
            {
                manifest.Policy.StageConcurrency = (manifest.Policy.StageConcurrency
                        ?? new List<ScaleRunStageConcurrency>())
                    .OrderBy(value => value.Stage)
                    .ToList();
            }
            manifest.ManifestDigest = ComputeDigest(manifest);
            Validate(manifest);
            return manifest;
        }

        public static void Validate(ScaleRunManifest manifest)
        {
            if (manifest == null
                || !string.Equals(manifest.SchemaVersion, ScaleRunManifest.CurrentSchemaVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(manifest.LoopId)
                || string.IsNullOrWhiteSpace(manifest.RunKey)
                || !Enum.IsDefined(typeof(ScaleRunMutationMode), manifest.MutationMode)
                || manifest.Policy == null
                || manifest.Pages == null
                || manifest.Pages.Count == 0
                || !MigrationActionSignature.IsSha256(manifest.ManifestDigest)
                || !string.Equals(manifest.ManifestDigest, ComputeDigest(manifest), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The scale-run manifest is incomplete, unsupported, or has a stale digest.");
            }
            ValidateOpaqueKey(manifest.LoopId, nameof(manifest.LoopId));
            ValidateOpaqueKey(manifest.RunKey, nameof(manifest.RunKey));
            ValidatePolicy(manifest.Policy);

            var pageKeys = new HashSet<string>(StringComparer.Ordinal);
            var ordinals = new HashSet<int>();
            ScaleRunPage previous = null;
            foreach (var page in manifest.Pages)
            {
                if (page == null
                    || page.Ordinal < 0
                    || !pageKeys.Add(page.PageKey ?? string.Empty)
                    || !ordinals.Add(page.Ordinal)
                    || string.IsNullOrWhiteSpace(page.PageFamily)
                    || string.IsNullOrWhiteSpace(page.LoadBucket)
                    || (!string.IsNullOrWhiteSpace(page.SupportCohortSignature) && !MigrationActionSignature.IsSha256(page.SupportCohortSignature))
                    || (!string.IsNullOrWhiteSpace(page.ExecutionCohortSignature) && !MigrationActionSignature.IsSha256(page.ExecutionCohortSignature)))
                {
                    throw new InvalidDataException("Every scale-run page requires unique identity/order and valid cohort signatures.");
                }
                ValidateOpaqueKey(page.PageKey, nameof(page.PageKey));
                ValidateOpaqueKey(page.PageFamily, nameof(page.PageFamily));
                ValidateOpaqueKey(page.SourceReferenceKey, nameof(page.SourceReferenceKey));
                if (!string.IsNullOrWhiteSpace(page.TargetReferenceKey))
                {
                    ValidateOpaqueKey(page.TargetReferenceKey, nameof(page.TargetReferenceKey));
                }
                ValidateOpaqueKey(page.LoadBucket, nameof(page.LoadBucket));
                if (previous != null
                    && (page.Ordinal < previous.Ordinal
                        || page.Ordinal == previous.Ordinal
                            && string.CompareOrdinal(page.PageKey, previous.PageKey) <= 0))
                {
                    throw new InvalidDataException("Scale-run pages must be in canonical ordinal/page-key order.");
                }
                previous = page;
            }
        }

        public static string ComputeDigest(ScaleRunManifest manifest)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    manifest ?? throw new ArgumentNullException(nameof(manifest)),
                    nameof(ScaleRunManifest.ManifestDigest)));
        }

        internal static IReadOnlyList<ScaleRunStage> Stages => OrderedStages;

        internal static void ValidateOpaqueKey(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > 256
                || value.StartsWith("/", StringComparison.Ordinal)
                || value.StartsWith("\\", StringComparison.Ordinal)
                || value.IndexOf("://", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf(':') >= 0
                || value.IndexOf('\\') >= 0
                || value.Split('/').Any(segment => segment == ".." || segment == ".")
                || value.Any(character => !(char.IsLetterOrDigit(character)
                    || character == '-'
                    || character == '_'
                    || character == '.'
                    || character == '/')))
            {
                throw new InvalidDataException(
                    parameterName + " must be an opaque non-URL key; URLs, credentials, rooted paths, and traversal are not manifest data.");
            }
        }

        private static void ValidatePolicy(ScaleRunPolicy policy)
        {
            if (policy.QueueCapacity <= 0
                || policy.QueueCapacity > 10000
                || policy.MaximumAttemptsPerStage <= 0
                || policy.MaximumAttemptsPerStage > 10
                || policy.RetryBaseDelayMilliseconds < 0
                || policy.RetryBaseDelayMilliseconds > 600000
                || policy.MaximumUnverifiedTargets <= 0
                || policy.MaximumUnverifiedTargets > 1000
                || policy.StageConcurrency == null
                || policy.StageConcurrency.Count != OrderedStages.Length)
            {
                throw new InvalidDataException("The scale-run policy has invalid queue, retry, backlog, or concurrency limits.");
            }
            var ordered = policy.StageConcurrency.OrderBy(value => value.Stage).ToArray();
            if (!ordered.Select(value => value.Stage).SequenceEqual(OrderedStages)
                || !policy.StageConcurrency.Select(value => value.Stage).SequenceEqual(OrderedStages)
                || ordered.Any(value => value.Maximum <= 0 || value.Maximum > 64))
            {
                throw new InvalidDataException("Scale-run stage concurrency must name every stage exactly once in canonical order with a bound of 1-64.");
            }
        }
    }

}
