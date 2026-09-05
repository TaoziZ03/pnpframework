using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleRunIdentity
    {
        public static MigrationActionSignature CreateAction(
            ScaleRunManifest manifest,
            ScaleRunPage page,
            ScaleRunStage stage,
            IScaleRunStageExecutor executor,
            IEnumerable<ScaleStageArtifact> inputArtifacts,
            string dependencySignature)
        {
            return CreateAction(
                manifest,
                page,
                null,
                stage,
                executor,
                inputArtifacts,
                dependencySignature);
        }

        public static MigrationActionSignature CreateAction(
            ScaleRunManifest manifest,
            ScaleRunPage page,
            ScalePageProfile effectiveProfile,
            ScaleRunStage stage,
            IScaleRunStageExecutor executor,
            IEnumerable<ScaleStageArtifact> inputArtifacts,
            string dependencySignature)
        {
            var profile = effectiveProfile ?? new ScalePageProfile
            {
                PageFamily = page?.PageFamily,
                TargetReferenceKey = page?.TargetReferenceKey,
                SupportCohortSignature = page?.SupportCohortSignature,
                ExecutionCohortSignature = page?.ExecutionCohortSignature,
                LoadBucket = page?.LoadBucket
            };
            var inputDigest = ComputeArtifactSetDigest(inputArtifacts);
            var isCollect = stage == ScaleRunStage.Collect;
            var hasTargetKey = !string.IsNullOrWhiteSpace(profile.TargetReferenceKey);
            var pageSelectionObject = isCollect || !hasTargetKey
                ? (object)new
                {
                    schemaVersion = "pnp-scale-source-selection/v1",
                    campaignKey = manifest.RunKey,
                    page.PageKey,
                    page.SourceReferenceKey
                }
                : new
                {
                    schemaVersion = "pnp-scale-page-selection/v1",
                    campaignKey = manifest.RunKey,
                    page.PageKey,
                    page.SourceReferenceKey,
                    TargetReferenceKey = profile.TargetReferenceKey
                };
            var pageSelectionDigest = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonical(pageSelectionObject));
            var actionId = "scale." + MigrationDigest.ComputeSha256(page.PageKey).Substring(0, 16)
                + "." + stage.ToString().ToLowerInvariant();
            var targetIdentity = isCollect || !hasTargetKey
                ? "scale-source-slot/v1/" + page.SourceReferenceKey + "/" + stage.ToString().ToLowerInvariant()
                : "scale-slot/v1/" + profile.TargetReferenceKey + "/" + stage.ToString().ToLowerInvariant();
            var hasCohorts = !string.IsNullOrWhiteSpace(profile.SupportCohortSignature) && !string.IsNullOrWhiteSpace(profile.ExecutionCohortSignature);
            var semanticObject = isCollect || !hasCohorts
                ? (object)new
                {
                    schemaVersion = "pnp-scale-stage-semantic-source/v1",
                    stage,
                    executor.ContractDigest,
                    executor.AllowsLiveMutation,
                    profile.PageFamily,
                    inputDigest
                }
                : new
                {
                    schemaVersion = "pnp-scale-stage-semantic/v1",
                    stage,
                    executor.ContractDigest,
                    executor.AllowsLiveMutation,
                    profile.PageFamily,
                    profile.SupportCohortSignature,
                    profile.ExecutionCohortSignature,
                    inputDigest
                };
            var semanticDigest = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonical(semanticObject));
            return MigrationActionSignature.Create(
                actionId,
                "Scale." + stage,
                inputDigest,
                pageSelectionDigest,
                targetIdentity,
                semanticDigest,
                string.IsNullOrWhiteSpace(dependencySignature) ? null : new[] { dependencySignature });
        }

        public static string ComputeArtifactSetDigest(IEnumerable<ScaleStageArtifact> artifacts)
        {
            var canonical = (artifacts ?? Enumerable.Empty<ScaleStageArtifact>())
                .OrderBy(value => value.RelativePath, StringComparer.Ordinal)
                .Select(value => new
                {
                    value.Kind,
                    value.Sha256,
                    value.Length,
                    value.MediaType,
                    value.SchemaVersion
                })
                .ToArray();
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-scale-artifact-set/v1",
                artifacts = canonical
            }));
        }
    }
}
