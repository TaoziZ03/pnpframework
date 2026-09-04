using PnP.Framework.Migration.Lists.Items.Protection;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Capture
{
    public sealed class PageCaptureOptions
    {
        public string SourcePageServerRelativeUrl { get; set; }

        public bool IncludeWebParts { get; set; } = true;

        public long MaximumDependencyBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Optional explicit policy that can omit protected document bytes before
        /// the binary request is issued. Null preserves historical capture-all behavior.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ProtectedAssetCapturePolicy ProtectedAssets { get; set; }
    }
}
