using System;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public enum PageIngredientIdOwnershipKind
    {
        Exact = 1,
        Prefix = 2
    }

    public sealed class PageIngredientIdOwnership
    {
        public PageIngredientIdOwnership(PageIngredientIdOwnershipKind kind, string value)
        {
            if (kind != PageIngredientIdOwnershipKind.Exact && kind != PageIngredientIdOwnershipKind.Prefix)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("An ingredient ID ownership value is required.", nameof(value));
            }

            Kind = kind;
            Value = value;
        }

        public PageIngredientIdOwnershipKind Kind { get; }

        public string Value { get; }

        public bool Matches(string ingredientId)
        {
            if (string.IsNullOrEmpty(ingredientId))
            {
                return false;
            }
            return Kind == PageIngredientIdOwnershipKind.Exact
                ? string.Equals(ingredientId, Value, StringComparison.Ordinal)
                : ingredientId.StartsWith(Value, StringComparison.Ordinal);
        }

        internal bool Overlaps(PageIngredientIdOwnership other)
        {
            if (Kind == PageIngredientIdOwnershipKind.Exact && other.Kind == PageIngredientIdOwnershipKind.Exact)
            {
                return string.Equals(Value, other.Value, StringComparison.Ordinal);
            }
            if (Kind == PageIngredientIdOwnershipKind.Exact)
            {
                return other.Matches(Value);
            }
            if (other.Kind == PageIngredientIdOwnershipKind.Exact)
            {
                return Matches(other.Value);
            }
            return Value.StartsWith(other.Value, StringComparison.Ordinal)
                || other.Value.StartsWith(Value, StringComparison.Ordinal);
        }
    }
}
