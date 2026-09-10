using PnP.Framework.Migration.Pages.Assessment.Maturity;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients.Lanes.DynamicRegion
{
    internal sealed class DynamicRegionMaturityEvidence
    {
        public DynamicRegionSourceEvidence Source { get; set; }

        public DynamicRegionTargetEvidence Target { get; set; }

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

    internal sealed class DynamicRegionTargetEvidence
    {
        public string TargetProfile { get; set; }

        public string TargetIdentity { get; set; }

        public string PageUrl { get; set; }

        public string WebUrl { get; set; }

        public string FileServerRelativeUrl { get; set; }

        public string ListId { get; set; }

        public int ItemId { get; set; }

        public string UniqueId { get; set; }

        public string TargetVersion { get; set; }

        public string SourceProviderIngredientId { get; set; }

        public string SourceProviderInstanceId { get; set; }

        public string TargetProviderInstanceId { get; set; }

        public string ProviderMappingDigest { get; set; }

        public string TargetEvidenceBindingDigest { get; set; }

        public string ReviewedProviderPlanDigest { get; set; }

        public string ReviewedProviderActionId { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }
}
