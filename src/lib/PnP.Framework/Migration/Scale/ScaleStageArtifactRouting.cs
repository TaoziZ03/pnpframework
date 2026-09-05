using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleStageArtifactRouting
    {
        public static IList<ScaleStageArtifact> GetInputArtifactsForStage(
            ScalePageWorkItem item,
            ScaleRunStage stage)
        {
            if (item == null)
            {
                return new List<ScaleStageArtifact>();
            }

            switch (stage)
            {
                case ScaleRunStage.Collect:
                    return new List<ScaleStageArtifact>();

                case ScaleRunStage.Plan:
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.Collect, out var collectArtifacts))
                    {
                        return collectArtifacts.ToList();
                    }
                    return item.InputArtifacts?.ToList() ?? new List<ScaleStageArtifact>();

                case ScaleRunStage.Repro:
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.Plan, out var planArtifacts))
                    {
                        return planArtifacts.ToList();
                    }
                    return item.InputArtifacts?.ToList() ?? new List<ScaleStageArtifact>();

                case ScaleRunStage.TargetRecapture:
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.Repro, out var reproArtifacts))
                    {
                        return reproArtifacts.ToList();
                    }
                    return item.InputArtifacts?.ToList() ?? new List<ScaleStageArtifact>();

                case ScaleRunStage.PackageCompare:
                    var combined = new List<ScaleStageArtifact>();
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.Collect, out var srcArtifacts))
                    {
                        combined.AddRange(srcArtifacts);
                    }
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.Plan, out var planCmpArtifacts))
                    {
                        combined.AddRange(planCmpArtifacts);
                    }
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.TargetRecapture, out var trArtifacts))
                    {
                        combined.AddRange(trArtifacts);
                    }
                    else if (item.InputArtifacts != null)
                    {
                        combined.AddRange(item.InputArtifacts);
                    }
                    return MergeArtifactsWithoutConflict(combined);

                case ScaleRunStage.BrowserAcceptance:
                    var acceptCombined = new List<ScaleStageArtifact>();
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.TargetRecapture, out var trAcceptArtifacts))
                    {
                        acceptCombined.AddRange(trAcceptArtifacts);
                    }
                    if (item.StageArtifacts.TryGetValue(ScaleRunStage.PackageCompare, out var cmpArtifacts))
                    {
                        acceptCombined.AddRange(cmpArtifacts);
                    }
                    else if (item.InputArtifacts != null)
                    {
                        acceptCombined.AddRange(item.InputArtifacts);
                    }
                    return MergeArtifactsWithoutConflict(acceptCombined);

                default:
                    return item.InputArtifacts?.ToList() ?? new List<ScaleStageArtifact>();
            }
        }

        internal static IList<ScaleStageArtifact> MergeArtifactsWithoutConflict(
            IEnumerable<ScaleStageArtifact> artifacts)
        {
            var byPath = new Dictionary<string, ScaleStageArtifact>(StringComparer.Ordinal);
            foreach (var artifact in artifacts ?? Enumerable.Empty<ScaleStageArtifact>())
            {
                if (artifact == null || string.IsNullOrWhiteSpace(artifact.RelativePath))
                {
                    continue;
                }
                if (byPath.TryGetValue(artifact.RelativePath, out var existing))
                {
                    if (!string.Equals(existing.Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Scale stage artifact conflict on relative path '{artifact.RelativePath}': differing content hashes '{existing.Sha256}' and '{artifact.Sha256}'.");
                    }
                    continue;
                }
                byPath.Add(artifact.RelativePath, artifact);
            }
            return byPath.Values.ToList();
        }
    }
}
