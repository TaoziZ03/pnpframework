using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public sealed class ClassicWikiComparisonResult
    {
        public bool Passed { get; set; }

        public bool WikiContentMatched { get; set; }

        public bool BracketNormalizationMatched { get; set; }

        public bool WebPartsMatched { get; set; }

        public bool NestedFoldersMatched { get; set; }

        public bool EmptyContentPreserved { get; set; }

        public bool DependenciesMatched { get; set; }

        public bool LifecycleMatched { get; set; }

        public bool SecurityMatched { get; set; }

        public IList<string> Differences { get; set; } = new List<string>();

        public IList<string> CanariesPassed { get; set; } = new List<string>();
    }
}
