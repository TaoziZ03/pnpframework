using System;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Capture
{
    public sealed class SourcePageFence
    {
        public Guid FileUniqueId { get; set; }

        // Optional opaque REST/HTTP version evidence. Do not infer an ETag from
        // VersionLabel: they are different SharePoint version identities.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ETag { get; set; }

        public string VersionLabel { get; set; }

        public long Length { get; set; }

        public DateTime ModifiedUtc { get; set; }
    }
}
