using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.References;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.EmbedIframe
{
    internal sealed class EmbedIframeSourceIdentity
    {
        public string WebUrl { get; set; }

        public string PageUrl { get; set; }

        public string FileServerRelativeUrl { get; set; }

        public string ListId { get; set; }

        public int ItemId { get; set; }

        public string UniqueId { get; set; }

        public string Version { get; set; }

        public string Modified { get; set; }
    }

    internal sealed class EmbedIframeHostBinding
    {
        public string IngredientId { get; set; }

        public string WebPartId { get; set; }

        public string PropertyName { get; set; }

        public int ZoneIndex { get; set; }
    }

    internal sealed class EmbedIframeSourceEvidence
    {
        public EmbedIframeSourceIdentity Source { get; set; }

        public EmbedIframeHostBinding Host { get; set; }

        public PageReferenceSnapshot Reference { get; set; }

        public ArtifactReference RawArtifact { get; set; }

        public string RawArtifactBase64 { get; set; }

        public string SemanticDigest { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class EmbedIframeMaturityEvidence
    {
        public EmbedIframeSourceEvidence Source { get; set; }

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }
}
