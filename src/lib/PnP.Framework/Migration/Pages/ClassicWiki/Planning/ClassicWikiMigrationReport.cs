using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Planning
{
    public sealed class ClassicWikiMigrationReport
    {
        public string Status { get; set; } = "Ready";

        public IList<string> Dispositions { get; set; } = new List<string>();

        public IList<string> Warnings { get; set; } = new List<string>();

        public IList<string> Blockers { get; set; } = new List<string>();
    }
}
