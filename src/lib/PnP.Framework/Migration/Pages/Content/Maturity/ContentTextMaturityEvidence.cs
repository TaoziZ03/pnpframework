using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Content.Maturity
{
    internal enum ContentTextAvailability
    {
        Captured = 1,
        Empty = 2,
        Absent = 3,
        AccessDenied = 4,
        Partial = 5
    }

    internal sealed class ContentTextSourceEvidence
    {
        public string PageUrl { get; set; }

        public string WebUrl { get; set; }

        public string FileServerRelativeUrl { get; set; }

        public string ListId { get; set; }

        public string ListTitle { get; set; }

        public int ListBaseTemplate { get; set; }

        public int ItemId { get; set; }

        public string FileUniqueId { get; set; }

        public string SourceVersion { get; set; }

        public string ContentTypeId { get; set; }

        public string ContentTypeName { get; set; }

        public string FieldId { get; set; }

        public string FieldInternalName { get; set; }

        public string FieldType { get; set; }

        public bool FieldRequired { get; set; }

        public bool FieldReadOnly { get; set; }

        public bool FieldHidden { get; set; }

        public bool FieldSealed { get; set; }

        public ContentTextAvailability Availability { get; set; }

        public ArtifactReference RawArtifact { get; set; }

        public string RawArtifactBase64 { get; set; }

        public string RawValueSha256 { get; set; }

        public string SemanticValueSha256 { get; set; }

        public IList<string> DependencyIngredientIds { get; set; } = new List<string>();

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class ContentTextMaturityEvidence
    {
        public ContentTextSourceEvidence Source { get; set; }

        public IngredientLiveEvidence Live { get; set; }

        public IngredientPlanEvidence Plan { get; set; }

        public IngredientOperationalEvidence Operational { get; set; }

        public IngredientProductizationEvidence Productization { get; set; }
    }
}
