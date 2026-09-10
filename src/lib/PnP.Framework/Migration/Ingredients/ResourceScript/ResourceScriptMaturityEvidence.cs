using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Ingredients.ResourceScript
{
    internal sealed class ResourceScriptMaturityEvidence
    {
        public PublishingPageCaptureBundle Snapshot { get; set; }

        public PublishingPageJsLinkReferenceEvidence Source { get; set; }

        public string ScriptContentBase64 { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }
}
