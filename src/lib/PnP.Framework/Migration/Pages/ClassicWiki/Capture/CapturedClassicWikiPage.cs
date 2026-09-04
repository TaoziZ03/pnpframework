using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.Security;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Capture
{
    internal sealed class CapturedClassicWikiPage
    {
        public PageIdentity Identity { get; set; }

        public PageArtifactSnapshot PageArtifact { get; set; }

        public string WikiField { get; set; }

        public int LibraryBaseTemplate { get; set; }

        public string LibraryTitle { get; set; }

        public string LibraryServerRelativeUrl { get; set; }

        public List<PageFieldValueSnapshot> Fields { get; set; } = new List<PageFieldValueSnapshot>();

        public List<ClassicWebPartSnapshot> WebParts { get; set; } = new List<ClassicWebPartSnapshot>();

        public PageSecuritySnapshot Security { get; set; }

        public PageLifecycleSnapshot Lifecycle { get; set; }

        public SourcePageFence SourceFence { get; set; }
    }
}
