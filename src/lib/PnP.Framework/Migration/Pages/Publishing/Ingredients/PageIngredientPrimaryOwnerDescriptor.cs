using PnP.Framework.Migration.Pages.Ingredients;
using System;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PageIngredientPrimaryOwnerDescriptor
    {
        public PageIngredientPrimaryOwnerDescriptor(
            string id,
            PageIngredientKind kind,
            string subtype,
            string semanticRole,
            string sourcePredicateId,
            string primaryOwnerLane)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A stable owner-registry entry ID is required.", nameof(id));
            }
            if (string.IsNullOrWhiteSpace(subtype))
            {
                throw new ArgumentException("An ingredient subtype is required.", nameof(subtype));
            }
            if (string.IsNullOrWhiteSpace(semanticRole))
            {
                throw new ArgumentException("An ingredient semantic role is required.", nameof(semanticRole));
            }
            if (string.IsNullOrWhiteSpace(sourcePredicateId))
            {
                throw new ArgumentException("An executable source-predicate ID is required.", nameof(sourcePredicateId));
            }
            if (string.IsNullOrWhiteSpace(primaryOwnerLane))
            {
                throw new ArgumentException("A primary owner lane is required.", nameof(primaryOwnerLane));
            }

            Id = id;
            Kind = kind;
            Subtype = subtype;
            SemanticRole = semanticRole;
            SourcePredicateId = sourcePredicateId;
            PrimaryOwnerLane = primaryOwnerLane;
        }

        public string Id { get; }

        public PageIngredientKind Kind { get; }

        public string Subtype { get; }

        public string SemanticRole { get; }

        public string SourcePredicateId { get; }

        public string PrimaryOwnerLane { get; }

        internal bool MatchesTuple(PageIngredientNode node)
        {
            return node != null
                && node.Kind == Kind
                && MatchesSubtype(node.Subtype)
                && string.Equals(node.SemanticRole, SemanticRole, StringComparison.Ordinal)
                && string.Equals(node.SourcePredicateId, SourcePredicateId, StringComparison.Ordinal);
        }

        private bool MatchesSubtype(string value)
        {
            if (Subtype.EndsWith(".*", StringComparison.Ordinal))
            {
                return value != null
                    && value.StartsWith(Subtype.Substring(0, Subtype.Length - 1), StringComparison.Ordinal);
            }
            return string.Equals(value, Subtype, StringComparison.Ordinal);
        }
    }
}
