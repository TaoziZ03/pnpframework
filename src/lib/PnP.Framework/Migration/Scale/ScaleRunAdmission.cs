using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleRunAdmission
    {
        public static void Validate(
            ScaleRunManifest manifest,
            ScaleRunControllerOptions options,
            IReadOnlyDictionary<ScaleRunStage, IScaleRunStageExecutor> executors)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.OutputRoot))
            {
                throw new ArgumentException("Scale-run controller options require an output root.", nameof(options));
            }
            var fullOutputRoot = Path.GetFullPath(options.OutputRoot);
            if (string.Equals(
                fullOutputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetPathRoot(fullOutputRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A filesystem root is not a valid scale-run output directory.", nameof(options));
            }
            if (!string.IsNullOrWhiteSpace(options.ImprovementReference))
            {
                ScaleRunManifestValidator.ValidateOpaqueKey(
                    options.ImprovementReference,
                    nameof(options.ImprovementReference));
            }
            var explicitlyApproved = manifest.MutationMode == ScaleRunMutationMode.ExplicitApproved;
            if (explicitlyApproved
                && !string.Equals(
                    options.ExplicitMutationConfirmationDigest,
                    manifest.ManifestDigest,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "ExplicitApproved requires a command-time confirmation equal to the sealed manifest digest.");
            }
            if (!explicitlyApproved
                && !string.IsNullOrWhiteSpace(options.ExplicitMutationConfirmationDigest))
            {
                throw new InvalidOperationException(
                    "A live-mutation confirmation is valid only for an ExplicitApproved manifest.");
            }

            foreach (var stage in ScaleRunManifestValidator.Stages)
            {
                if (!executors.TryGetValue(stage, out var executor)
                    || executor.Stage != stage
                    || !MigrationActionSignature.IsSha256(executor.ContractDigest)
                    || executor.AllowsLiveMutation && !executor.MutatesTarget
                    || executor.AllowsLiveMutation && !explicitlyApproved
                    || stage == ScaleRunStage.Repro
                        && (!executor.MutatesTarget || executor.ResumePolicy != ScaleStageResumePolicy.FreshProbe)
                    || stage != ScaleRunStage.Repro && executor.MutatesTarget
                    || stage == ScaleRunStage.TargetRecapture
                        && executor.ResumePolicy != ScaleStageResumePolicy.AlwaysExecute
                    || stage != ScaleRunStage.Repro
                        && stage != ScaleRunStage.TargetRecapture
                        && executor.ResumePolicy != ScaleStageResumePolicy.ArtifactCheckpoint)
                {
                    throw new InvalidDataException(
                        "Every scale stage requires a canonical executor contract, safe resume policy, and fail-closed live-mutation capability.");
                }
            }
        }
    }
}
