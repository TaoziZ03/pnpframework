using PnP.Framework.Migration.Evidence.ProducerBuild;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public sealed class NativePageRuntimeAcceptanceReceipt
    {
        public string SchemaVersion { get; set; } = NativePageRuntimeContract.AcceptanceReceiptSchemaVersion;

        public DateTimeOffset DecidedAtUtc { get; set; }

        public string AcceptanceAuthority { get; set; } = NativePageRuntimeContract.NativeAuthority;

        public NativePageRuntimeBinding Binding { get; set; }

        public string BindingDigestSha256 { get; set; }

        public RuntimeVerificationStatus RuntimeVerificationStatus { get; set; } = RuntimeVerificationStatus.Pending;

        public MigrationAcceptanceStatus AcceptanceStatus { get; set; } = MigrationAcceptanceStatus.Pending;

        public string DecisionCode { get; set; }

        public string ProducerBuildProvenanceStatus { get; set; } = ProducerBuildProvenanceContract.Unverified;

        public IList<string> Diagnostics { get; set; } = new List<string>();
    }
}
