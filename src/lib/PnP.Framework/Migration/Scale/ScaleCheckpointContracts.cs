using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Scale
{
    public enum ScaleStageArtifactKind
    {
        Output = 1,
        Evidence = 2,
        HttpAuthorizationEvidence = 3
    }

    public sealed class ScaleStageArtifact
    {
        public ScaleStageArtifactKind Kind { get; set; } = ScaleStageArtifactKind.Output;

        public string RelativePath { get; set; }

        public string Sha256 { get; set; }

        public long Length { get; set; }

        public string MediaType { get; set; }

        public string SchemaVersion { get; set; }
    }

    public sealed class ScaleRequestMetric
    {
        public string Operation { get; set; }

        public double DurationMilliseconds { get; set; }

        public int? HttpStatusCode { get; set; }

        public long ResponseBytes { get; set; }

        public double RetryAfterWaitMilliseconds { get; set; }
    }

    /// <summary>
    /// Sanitized, content-addressed evidence for a literal HTTP authorization
    /// response. It deliberately stores no URI, headers, body, token, or cookie.
    /// </summary>
    public sealed class ScaleHttpAuthorizationEvidence
    {
        public const string CurrentSchemaVersion = "pnp-scale-http-authorization-evidence/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string ActionSignature { get; set; }

        public string TargetIdentityDigest { get; set; }

        public string Operation { get; set; }

        public int HttpStatusCode { get; set; }

        public DateTimeOffset CapturedAtUtc { get; set; }
    }

    public sealed class ScaleStageCheckpoint
    {
        public const string CurrentSchemaVersion = "pnp-scale-stage-checkpoint/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string PageKey { get; set; }

        public ScaleRunStage Stage { get; set; }

        public string ActionSignature { get; set; }

        public string ArtifactSetDigest { get; set; }

        public ScaleStageOutcome Outcome { get; set; }

        public bool Verified { get; set; }

        public bool MutationAttempted { get; set; }

        public string ObservedStateDigest { get; set; }

        public string TargetIdentityDigest { get; set; }

        public string DiagnosticCode { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public IList<ScaleStageArtifact> Artifacts { get; set; } = new List<ScaleStageArtifact>();

        public IList<ScaleRequestMetric> Requests { get; set; } = new List<ScaleRequestMetric>();

        public string CheckpointDigest { get; set; }
    }
}
