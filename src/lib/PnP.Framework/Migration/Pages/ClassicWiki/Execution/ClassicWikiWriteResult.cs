using Microsoft.SharePoint.Client;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal sealed class ClassicWikiWriteResult
    {
        public List TargetLibrary { get; set; }

        public Microsoft.SharePoint.Client.File TargetFile { get; set; }

        public ListItem TargetItem { get; set; }

        public bool ResumedExistingOwnedPage { get; set; }

        public string PersistedWikiFieldSha256 { get; set; }

        public int ImportedWebPartCount { get; set; }
    }
}
