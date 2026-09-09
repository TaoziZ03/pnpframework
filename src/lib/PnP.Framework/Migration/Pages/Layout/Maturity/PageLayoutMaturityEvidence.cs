using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.PageLayout
{
    internal sealed class PageLayoutWikiSourceEvidence
    {
        public ClassicWikiCaptureBundle Snapshot { get; set; }

        public string ListId { get; set; }

        public string SourceVersion { get; set; }

        public string PublishingPageLayout { get; set; }

        public ArtifactReference RawArtifact { get; set; }

        public string RawArtifactBase64 { get; set; }

        public string SemanticDigest { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class PageLayoutMaturityEvidence
    {
        public PageLayoutWikiSourceEvidence WikiSource { get; set; }

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }
}
