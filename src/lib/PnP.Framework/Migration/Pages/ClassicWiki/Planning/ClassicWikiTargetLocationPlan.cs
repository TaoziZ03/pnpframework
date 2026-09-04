namespace PnP.Framework.Migration.Pages.ClassicWiki.Planning
{
    public sealed class ClassicWikiTargetLocationPlan
    {
        public string TargetWebUrl { get; set; }

        public string TargetLibraryServerRelativeUrl { get; set; }

        public string TargetLibraryTitle { get; set; }

        public int TargetLibraryTemplate { get; set; } = 119;

        public string TargetFolderServerRelativeUrl { get; set; }

        public string FileName { get; set; }
    }
}
