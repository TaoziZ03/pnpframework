#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}

namespace System.Linq
{
    internal static class NetStandardLinqExtensions
    {
        public static Collections.Generic.HashSet<T> ToHashSet<T>(this Collections.Generic.IEnumerable<T> source)
        {
            return new Collections.Generic.HashSet<T>(source);
        }

        public static Collections.Generic.HashSet<T> ToHashSet<T>(
            this Collections.Generic.IEnumerable<T> source,
            Collections.Generic.IEqualityComparer<T> comparer)
        {
            return new Collections.Generic.HashSet<T>(source, comparer);
        }
    }
}
#endif
