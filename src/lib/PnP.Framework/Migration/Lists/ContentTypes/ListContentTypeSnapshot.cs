using PnP.Framework.Migration.Evidence;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Lists.ContentTypes
{
    public sealed class ListContentTypeFieldLinkSnapshot
    {
        public Guid FieldId { get; set; }

        public string InternalName { get; set; }

        public string DisplayName { get; set; }

        public bool Required { get; set; }

        public bool Hidden { get; set; }

        public bool ReadOnly { get; set; }
    }

    public sealed class ListContentTypeSnapshot
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string Group { get; set; }

        public string ParentId { get; set; }

        public bool Hidden { get; set; }

        public bool ReadOnly { get; set; }

        public bool Sealed { get; set; }

        public IList<ListContentTypeFieldLinkSnapshot> FieldLinks { get; set; } = new List<ListContentTypeFieldLinkSnapshot>();

        /// <summary>
        /// Null preserves the v1 contract's historical meaning: the member was
        /// captured. Only non-captured member evidence is emitted so existing
        /// canonical snapshot digests remain stable.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EvidenceAvailability? Availability { get; set; }
    }

    internal static class ListContentTypeEvidence
    {
        public static bool IsCaptured(ListContentTypeSnapshot value)
        {
            return value != null
                && (!value.Availability.HasValue
                    || value.Availability.Value == EvidenceAvailability.Captured);
        }
    }
}
