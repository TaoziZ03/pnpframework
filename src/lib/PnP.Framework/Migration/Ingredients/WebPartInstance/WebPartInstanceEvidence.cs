using PnP.Framework.Migration.Pages.Assessment.Maturity;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Ingredients.WebPartInstance
{
    internal enum WebPartInstanceAvailability
    {
        Captured = 1,
        AccessDenied = 2,
        Partial = 3
    }

    internal sealed class WebPartInstanceSourceEvidence
    {
        public string PageListId { get; set; }

        public int PageItemId { get; set; }

        public string PageUniqueId { get; set; }

        public string PageServerRelativeUrl { get; set; }

        public string SourceVersion { get; set; }

        public string InstanceId { get; set; }

        public string Title { get; set; }

        public string RestTypeName { get; set; }

        public string DefinitionTypeName { get; set; }

        public string AssemblyIdentity { get; set; }

        public string Family { get; set; }

        public string Scope { get; set; }

        public string ZoneId { get; set; }

        public int ZoneIndex { get; set; }

        public string NormalizedPropertiesCanonicalJson { get; set; }

        public string NormalizedRestPayloadSha256 { get; set; }

        public string BoundListId { get; set; }

        public string BoundViewId { get; set; }

        public string ViewType { get; set; }

        public int BaseViewId { get; set; }

        public WebPartInstanceAvailability Availability { get; set; }

        public IList<string> DependencyIngredientIds { get; set; } = new List<string>();

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class WebPartInstanceTargetExpectation
    {
        public string TargetInstanceId { get; set; }

        public string TargetListId { get; set; }

        public string TargetViewId { get; set; }
    }

    internal sealed class WebPartInstanceMaturityEvidence
    {
        public WebPartInstanceSourceEvidence Source { get; set; }

        public WebPartInstanceTargetExpectation TargetExpectation { get; set; }

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }
}
