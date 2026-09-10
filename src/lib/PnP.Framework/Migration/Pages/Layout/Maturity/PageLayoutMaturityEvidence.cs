using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using System;
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

    internal sealed class PageLayoutWikiTargetReadbackEvidence
    {
        public string ClaimId { get; set; }

        public string IngredientId { get; set; }

        public string SourceListId { get; set; }

        public int SourceItemId { get; set; }

        public string SourceFileUniqueId { get; set; }

        public string SourceVersion { get; set; }

        public string ImplementationCommit { get; set; }

        public string PlanDigest { get; set; }

        public string TargetProfile { get; set; }

        public string TargetPath { get; set; }

        public DateTimeOffset ReadbackStartedAtUtc { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public int ListBaseTemplate { get; set; }

        public string ContentTypeId { get; set; }

        public string ContentTypeName { get; set; }

        public string PublishingPageLayout { get; set; }

        public string RuntimeAdapterId { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class PageLayoutMaturityEvidence
    {
        public PageLayoutWikiSourceEvidence WikiSource { get; set; }

        public PageLayoutWikiTargetReadbackEvidence WikiTargetReadback { get; set; }

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }
}
