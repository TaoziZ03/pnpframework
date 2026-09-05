using System.Collections.Generic;

namespace PnP.Framework.Migration.Scale
{
    public enum ScaleRunStage
    {
        Collect = 1,
        Plan = 2,
        Repro = 3,
        TargetRecapture = 4,
        PackageCompare = 5,
        BrowserAcceptance = 6
    }

    public enum ScaleRunMutationMode
    {
        Disabled = 1,
        Simulation = 2,
        ExplicitApproved = 3
    }

    public sealed class ScaleRunStageConcurrency
    {
        public ScaleRunStage Stage { get; set; }

        public int Maximum { get; set; }
    }

    public sealed class ScaleRunPolicy
    {
        public int QueueCapacity { get; set; } = 16;

        public int MaximumAttemptsPerStage { get; set; } = 3;

        public int RetryBaseDelayMilliseconds { get; set; } = 1000;

        public int MaximumUnverifiedTargets { get; set; } = 2;

        public IList<ScaleRunStageConcurrency> StageConcurrency { get; set; } =
            new List<ScaleRunStageConcurrency>
            {
                new ScaleRunStageConcurrency { Stage = ScaleRunStage.Collect, Maximum = 4 },
                new ScaleRunStageConcurrency { Stage = ScaleRunStage.Plan, Maximum = 8 },
                new ScaleRunStageConcurrency { Stage = ScaleRunStage.Repro, Maximum = 1 },
                new ScaleRunStageConcurrency { Stage = ScaleRunStage.TargetRecapture, Maximum = 4 },
                new ScaleRunStageConcurrency { Stage = ScaleRunStage.PackageCompare, Maximum = 8 },
                new ScaleRunStageConcurrency { Stage = ScaleRunStage.BrowserAcceptance, Maximum = 3 }
            };
    }

    public sealed class ScaleRunPage
    {
        public string PageKey { get; set; }

        public int Ordinal { get; set; }

        public string PageFamily { get; set; }

        public string SourceReferenceKey { get; set; }

        public string TargetReferenceKey { get; set; }

        public string SupportCohortSignature { get; set; }

        public string ExecutionCohortSignature { get; set; }

        public string LoadBucket { get; set; }
    }

    public sealed class ScaleRunManifest
    {
        public const string CurrentSchemaVersion = "pnp-scale-run-manifest/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string LoopId { get; set; }

        /// <summary>
        /// Stable campaign identity shared by nested gates and repeated loop attempts.
        /// It is intentionally not an attempt or batch identifier.
        /// </summary>
        public string RunKey { get; set; }

        public ScaleRunMutationMode MutationMode { get; set; } = ScaleRunMutationMode.Disabled;

        public ScaleRunPolicy Policy { get; set; } = new ScaleRunPolicy();

        public IList<ScaleRunPage> Pages { get; set; } = new List<ScaleRunPage>();

        public string ManifestDigest { get; set; }
    }

    public sealed class ScaleRunControllerOptions
    {
        public string OutputRoot { get; set; }

        public bool Resume { get; set; } = true;

        public string ImprovementReference { get; set; }

        /// <summary>
        /// Required only for ExplicitApproved runs and must exactly match the
        /// sealed manifest digest. This is the host's second, command-time
        /// confirmation; the journal never grants mutation authority.
        /// </summary>
        public string ExplicitMutationConfirmationDigest { get; set; }
    }
}
