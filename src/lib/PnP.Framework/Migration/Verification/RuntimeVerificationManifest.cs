using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Verification
{
    public sealed class RuntimeVerificationManifest
    {
        public string SchemaVersion { get; set; } = "pnp-migration-runtime-verification/v1";

        public IList<RuntimeVerificationRequirement> Requirements { get; set; } = new List<RuntimeVerificationRequirement>();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IList<RuntimeVerificationAssertion> Assertions { get; set; }
    }
}
