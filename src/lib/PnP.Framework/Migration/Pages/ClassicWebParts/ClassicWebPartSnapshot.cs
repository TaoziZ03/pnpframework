using System;
using PnP.Framework.Migration.Packaging;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.ClassicWebParts
{
    public sealed class ClassicWebPartSnapshot
    {
        public Guid Id { get; set; }

        public string Title { get; set; }

        public string TypeName { get; set; }

        public string ZoneId { get; set; }

        public int ZoneIndex { get; set; }

        public bool Hidden { get; set; }

        public string ExportXml { get; set; }

        public string ExportSha256 { get; set; }

        // A REST property observation is not native ExportWebPart XML. Preserve
        // its exact artifact/format separately; it does not authorize replay.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string PropertyEvidenceFormat { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string PropertyEvidenceJson { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ArtifactReference PropertyEvidenceArtifact { get; set; }
    }
}
