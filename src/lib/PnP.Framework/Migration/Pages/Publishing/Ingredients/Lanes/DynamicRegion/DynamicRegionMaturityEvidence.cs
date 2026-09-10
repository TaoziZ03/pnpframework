using PnP.Framework.Migration.Pages.Assessment.Maturity;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.DynamicRegion
{
    internal sealed class DynamicRegionMaturityEvidence
    {
        public DynamicRegionSourceEvidence Source { get; set; }

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }

    internal sealed class DynamicRegionSourceEvidence
    {
        public string PageUrl { get; set; }

        public string WebUrl { get; set; }

        public string FileServerRelativeUrl { get; set; }

        public string ListId { get; set; }

        public int ItemId { get; set; }

        public string UniqueId { get; set; }

        public string SourceVersion { get; set; }

        public string SourceArtifactSha256 { get; set; }

        public string ProviderIngredientId { get; set; }

        public string ProviderInstanceId { get; set; }

        public string ControlType { get; set; }

        public string ConfigSha256 { get; set; }

        public string Boundary { get; set; }

        public string Zone { get; set; }

        public int Order { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public JsonElement RawRegion { get; set; }

        public string RawArtifactSha256 { get; set; }

        public long RawArtifactLength { get; set; }

        public string SemanticDigest { get; set; }

        public string LegacyLineTerminatedSemanticDigest { get; set; }

        public IList<string> Dependencies { get; set; } = new List<string>();

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }
}
