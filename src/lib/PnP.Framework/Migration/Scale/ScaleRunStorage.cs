using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleRunStorage
    {
        private const string ContractNamespace = "contracts-v2";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static string JournalPath(string outputRoot)
        {
            return Path.Combine(FullRoot(outputRoot), "scale-run-journal.jsonl");
        }

        public static string StageJournalPath(string outputRoot)
        {
            return Path.Combine(FullRoot(outputRoot), ContractNamespace, "scale-stage-journal.jsonl");
        }

        public static string StageRoot(string outputRoot, ScaleRunPage page, ScaleRunStage stage)
        {
            return Path.Combine(
                FullRoot(outputRoot),
                ContractNamespace,
                "items",
                MigrationDigest.ComputeSha256(page.PageKey).Substring(0, 24),
                "stages",
                stage.ToString().ToLowerInvariant());
        }

        public static string CheckpointPath(string outputRoot, ScaleRunPage page, ScaleRunStage stage)
        {
            return Path.Combine(StageRoot(outputRoot, page, stage), "stage-checkpoint.json");
        }

        public static void WriteCheckpointAtomic(
            string outputRoot,
            ScaleRunPage page,
            ScaleStageCheckpoint checkpoint)
        {
            if (page == null || checkpoint == null)
            {
                throw new ArgumentNullException(page == null ? nameof(page) : nameof(checkpoint));
            }
            checkpoint.CheckpointDigest = ComputeCheckpointDigest(checkpoint);
            var path = CheckpointPath(outputRoot, page, checkpoint.Stage);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WriteAtomic(path, MigrationContractSerializer.SerializeCanonical(checkpoint) + "\n");
        }

        public static bool TryReadValidatedCheckpoint(
            string outputRoot,
            ScaleRunPage page,
            ScaleRunStage stage,
            MigrationActionSignature action,
            ScaleStageExecutionJournalReadResult journal,
            out ScaleStageCheckpoint checkpoint)
        {
            checkpoint = null;
            var path = CheckpointPath(outputRoot, page, stage);
            if (!File.Exists(path))
            {
                return false;
            }
            var raw = File.ReadAllText(path, Utf8).TrimEnd('\r', '\n');
            var value = MigrationContractSerializer.Deserialize<ScaleStageCheckpoint>(raw);
            if (!string.Equals(
                raw,
                MigrationContractSerializer.SerializeCanonical(value),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("A scale stage checkpoint is not canonical typed JSON.");
            }
            ScaleCheckpointValidator.ValidateCheckpoint(outputRoot, page, stage, action, journal, value);
            checkpoint = value;
            return true;
        }

        public static void WriteSummaryAtomic(string outputRoot, ScaleRunSummary summary)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }
            summary.SummaryDigest = ComputeSummaryDigest(summary);
            if (summary.CatalogProjection != null)
            {
                summary.CatalogProjection.SummaryDigest = summary.SummaryDigest;
            }
            var root = FullRoot(outputRoot);
            Directory.CreateDirectory(root);
            var attemptRoot = Path.Combine(root, "attempts", summary.RunAttemptId);
            Directory.CreateDirectory(attemptRoot);
            var serialized = MigrationContractSerializer.SerializeCanonical(summary) + "\n";
            WriteAtomic(Path.Combine(attemptRoot, "run-summary.json"), serialized);
            WriteAtomic(Path.Combine(root, "run-summary.json"), serialized);
        }

        public static string ComputeCheckpointDigest(ScaleStageCheckpoint checkpoint)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    checkpoint ?? throw new ArgumentNullException(nameof(checkpoint)),
                    nameof(ScaleStageCheckpoint.CheckpointDigest)));
        }

        public static string ComputeArtifactReferenceSetDigest(
            IEnumerable<ScaleStageArtifact> artifacts)
        {
            var canonical = (artifacts ?? Enumerable.Empty<ScaleStageArtifact>())
                .OrderBy(value => value.RelativePath, StringComparer.Ordinal)
                .Select(value => new
                {
                    value.Kind,
                    value.RelativePath,
                    value.Sha256,
                    value.Length,
                    value.MediaType,
                    value.SchemaVersion
                })
                .ToArray();
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(new
            {
                schemaVersion = "pnp-scale-artifact-reference-set/v1",
                artifacts = canonical
            }));
        }

        public static string ComputeSummaryDigest(ScaleRunSummary summary)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }
            var rootDigest = summary.SummaryDigest;
            var catalogDigest = summary.CatalogProjection?.SummaryDigest;
            summary.SummaryDigest = null;
            if (summary.CatalogProjection != null)
            {
                summary.CatalogProjection.SummaryDigest = null;
            }
            try
            {
                return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(summary));
            }
            finally
            {
                summary.SummaryDigest = rootDigest;
                if (summary.CatalogProjection != null)
                {
                    summary.CatalogProjection.SummaryDigest = catalogDigest;
                }
            }
        }

        public static string ComputeFileSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return MigrationDigest.ComputeSha256(stream);
            }
        }

        public static string ResolveArtifactPath(string outputRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath)
                || relativePath.IndexOf(':') >= 0
                || relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => value == ".." || value == "."))
            {
                throw new InvalidDataException("Scale stage artifacts must use safe paths relative to the run output root.");
            }
            var root = FullRoot(outputRoot);
            var resolved = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!resolved.StartsWith(RootPrefix(root), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A scale stage artifact escaped the run output root.");
            }
            return resolved;
        }

        public static string ToRelativeArtifactPath(string outputRoot, string path)
        {
            var root = FullRoot(outputRoot);
            var resolved = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            var prefix = RootPrefix(root);
            if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A scale stage artifact escaped the run output root.");
            }
            return resolved.Substring(prefix.Length).Replace('\\', '/');
        }


        private static string FullRoot(string outputRoot)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                throw new ArgumentException("A scale-run output root is required.", nameof(outputRoot));
            }
            return Path.GetFullPath(outputRoot);
        }

        private static string RootPrefix(string root)
        {
            return root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? root
                    : root + Path.DirectorySeparatorChar;
        }

        private static void WriteAtomic(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, content, Utf8);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
    }
}
