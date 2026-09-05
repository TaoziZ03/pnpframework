using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    internal sealed class ClassicWikiFreshTargetEvidence
    {
        public ClassicWikiExportPackage Recapture { get; set; }

        public IDictionary<string, object> FileProperties { get; set; } = new Dictionary<string, object>();

        public bool IndependentContext { get; set; }
    }
}
