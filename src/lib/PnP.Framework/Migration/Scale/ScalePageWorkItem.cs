using PnP.Framework.Migration.Execution;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Scale
{
    internal sealed class ScalePageWorkItem
    {
        public ScalePageWorkItem(ScaleRunPage page)
        {
            Page = page ?? throw new ArgumentNullException(nameof(page));
            if (!string.IsNullOrWhiteSpace(page.TargetReferenceKey)
                && MigrationActionSignature.IsSha256(page.SupportCohortSignature)
                && MigrationActionSignature.IsSha256(page.ExecutionCohortSignature))
            {
                EffectiveProfile = ScalePageProfile.Seal(new ScalePageProfile
                {
                    PageFamily = page.PageFamily,
                    TargetReferenceKey = page.TargetReferenceKey,
                    SupportCohortSignature = page.SupportCohortSignature,
                    ExecutionCohortSignature = page.ExecutionCohortSignature,
                    LoadBucket = page.LoadBucket
                });
            }
        }

        public ScaleRunPage Page { get; }

        public ScalePageProfile EffectiveProfile { get; set; }

        public ScalePageDisposition Disposition { get; set; } = ScalePageDisposition.Pending;

        public string NextAction { get; set; } = "Continue";

        public IList<ScaleStageRunSummary> Stages { get; } = new List<ScaleStageRunSummary>();

        public IList<ScaleStageArtifact> InputArtifacts { get; set; } = new List<ScaleStageArtifact>();

        public IDictionary<ScaleRunStage, IList<ScaleStageArtifact>> StageArtifacts { get; } =
            new Dictionary<ScaleRunStage, IList<ScaleStageArtifact>>();

        public string DependencySignature { get; set; }

        public bool UnverifiedSlotHeld { get; set; }

        public double PendingBackpressureWaitMilliseconds { get; set; }
    }
}
