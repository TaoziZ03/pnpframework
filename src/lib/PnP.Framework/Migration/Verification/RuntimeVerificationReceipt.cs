using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Verification
{
    public sealed class RuntimeVerificationReceipt
    {
        public string SchemaVersion { get; set; } = "pnp-migration-runtime-verification-receipt/v1";

        public string PlanDigest { get; set; }

        public string TargetIdentity { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public IList<RuntimeVerificationResult> Results { get; set; } = new List<RuntimeVerificationResult>();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IList<RuntimeVerificationAssertionResult> AssertionResults { get; set; }

        public RuntimeVerificationStatus Status { get; set; }
    }
}
