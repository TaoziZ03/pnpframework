using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Evidence;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.References
{
    public sealed class PageReferenceSnapshot
    {
        public string Id { get; set; }

        public string OriginalValue { get; set; }

        public string SourceAbsoluteUrl { get; set; }

        public string SourceServerRelativeUrl { get; set; }

        public string Consumer { get; set; }

        public PageReferenceKind Kind { get; set; }

        public bool IsRenderableResource { get; set; }

        public string ContentBase64 { get; set; }

        public string ContentSha256 { get; set; }

        public long ContentLength { get; set; }

        public PageCaptureStatus CaptureStatus { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LiteralHttpAuthorizationEvidence AuthorizationEvidence { get; set; }

        public IList<string> Diagnostics { get; set; } = new List<string>();
    }
}
