using System;

namespace PnP.Framework.Migration.Scale
{
    internal sealed class ScaleControllerFailureEvidence
    {
        public const string CurrentSchemaVersion = "pnp-scale-controller-failure-evidence/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public ScaleRunStage Stage { get; set; }

        public int Attempt { get; set; }

        public string ActionSignature { get; set; }

        public string ExceptionType { get; set; }

        public DateTimeOffset CapturedAtUtc { get; set; }
    }
}
