using System;

namespace PnP.Framework.Migration.Verification
{
    public sealed class RuntimeBrowserContextIdentity
    {
        public string BrowserProduct { get; set; }

        public string BrowserVersion { get; set; }

        public string ProtocolVersion { get; set; }

        public string ProfileIdentitySha256 { get; set; }

        public string BrowserContextId { get; set; }

        public string TargetId { get; set; }

        public bool IsIncognito { get; set; }

        public bool FreshContext { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public DateTimeOffset FirstNavigationAtUtc { get; set; }
    }
}
