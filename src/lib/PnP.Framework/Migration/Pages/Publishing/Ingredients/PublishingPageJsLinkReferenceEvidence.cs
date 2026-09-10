using PnP.Framework.Migration.Pages.References;
using System;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    // Shared identity payload for the existing v8 evidence-envelope seam. This
    // does not collect scripts, produce actions, compare results, or award maturity.
    internal sealed class PublishingPageJsLinkReferenceEvidence
    {
        public const string SchemaVersion = "pnp-publishing-page-jslink-reference-evidence/v1";
        public const string SubtypeId = "reference.jslink";
        public const string SourcePredicateId = "reference.persisted-webpart-jslink/v1";
        public const string IngredientIdPrefix = "ccd.ingredient.resource.script/v1:";

        public string IngredientId { get; set; }
        public string Subtype { get; set; }
        public string SemanticRole { get; set; }
        public Guid SourcePageFileUniqueId { get; set; }
        public int SourceListItemId { get; set; }
        public string SourcePageServerRelativeUrl { get; set; }
        public string SourcePageETag { get; set; }
        public Guid HostWebPartId { get; set; }
        // The captured ZoneIndex, not the order of a normalized evidence array.
        public int? HostWebPartOrder { get; set; }
        public string HostEvidenceFormat { get; set; }
        public string HostEvidenceSha256 { get; set; }
        public string ReferenceForm { get; set; }
        public string PersistedPropertyValue { get; set; }
        public int? PersistedReferenceOrder { get; set; }

        // Reuse the existing locator, payload digest/length, capture status and
        // optional artifact bytes contract instead of creating a script contract.
        public PageReferenceSnapshot Reference { get; set; }
    }
}
