using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public sealed class PageIngredientNode
    {
        public string Id { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public PageIngredientKind Kind { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string KindId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Subtype { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string SemanticRole { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string SourcePredicateId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string SourcePageOrListItemIdentity { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string SourceVersionIdentity { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string PrimaryOwnerLane { get; set; }

        public string Label { get; set; }

        public bool HasContent { get; set; }

        public PageIngredientOwnership Ownership { get; set; }

        public string SourceAuthority { get; set; }

        public string EvidenceDigest { get; set; }

        public string RuntimeRequirement { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }
}
