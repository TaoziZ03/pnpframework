using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public sealed class CanonicalPageIngredientGraph
    {
        public const string SchemaVersionV1 = "pnp-page-ingredient-graph/v1";

        public const string SchemaVersionV2 = "pnp-page-ingredient-graph/v2";

        public string SchemaVersion { get; set; } = SchemaVersionV1;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ProjectionVersion { get; set; }

        public IList<PageIngredientNode> Nodes { get; set; } = new List<PageIngredientNode>();

        public IList<PageIngredientEdge> Edges { get; set; } = new List<PageIngredientEdge>();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IList<PageIngredientExternalReference> ExternalReferences { get; set; }
    }
}
