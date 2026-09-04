using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Lists.Planning
{
    public enum DroppedItemDependencyDisposition
    {
        NeedsPolicyDecision = 0,
        ClearValue = 1,
        DropDependentItem = 2
    }

    /// <summary>
    /// One reviewed decision for one exact lookup value edge. Folder hierarchy
    /// edges are structural and are always projected as DropDependentItem.
    /// </summary>
    public sealed class DroppedLookupValueDecision
    {
        public const string ContractVersion = "pnp-dropped-lookup-value-decision/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public Guid ConsumerSourceListId { get; set; }

        public int ConsumerSourceItemId { get; set; }

        public string ConsumerFieldInternalName { get; set; }

        public Guid ProviderSourceListId { get; set; }

        public int ProviderSourceItemId { get; set; }

        public DroppedItemDependencyDisposition Disposition { get; set; }

        public string PolicyId { get; set; }

        public static DroppedLookupValueDecision Create(
            Guid consumerSourceListId,
            int consumerSourceItemId,
            string consumerFieldInternalName,
            Guid providerSourceListId,
            int providerSourceItemId,
            DroppedItemDependencyDisposition disposition,
            string policyId)
        {
            return new DroppedLookupValueDecision
            {
                ConsumerSourceListId = consumerSourceListId,
                ConsumerSourceItemId = consumerSourceItemId,
                ConsumerFieldInternalName = consumerFieldInternalName,
                ProviderSourceListId = providerSourceListId,
                ProviderSourceItemId = providerSourceItemId,
                Disposition = disposition,
                PolicyId = policyId
            };
        }

        public static IList<DroppedLookupValueDecision> ForProvider(
            IEnumerable<(Guid ConsumerListId, int ConsumerItemId, string FieldInternalName)> consumers,
            Guid providerSourceListId,
            int providerSourceItemId,
            DroppedItemDependencyDisposition disposition,
            string policyId)
        {
            return (consumers ?? Array.Empty<(Guid, int, string)>())
                .Select(value => Create(
                    value.ConsumerListId,
                    value.ConsumerItemId,
                    value.FieldInternalName,
                    providerSourceListId,
                    providerSourceItemId,
                    disposition,
                    policyId))
                .ToList();
        }

        internal static IReadOnlyDictionary<string, DroppedLookupValueDecision> ValidateAndIndex(
            IEnumerable<DroppedLookupValueDecision> decisions)
        {
            var result = new Dictionary<string, DroppedLookupValueDecision>(StringComparer.Ordinal);
            foreach (var decision in decisions ?? Array.Empty<DroppedLookupValueDecision>())
            {
                if (decision == null
                    || !string.Equals(decision.SchemaVersion, ContractVersion, StringComparison.Ordinal)
                    || decision.ConsumerSourceListId == Guid.Empty
                    || decision.ConsumerSourceItemId <= 0
                    || string.IsNullOrWhiteSpace(decision.ConsumerFieldInternalName)
                    || decision.ProviderSourceListId == Guid.Empty
                    || decision.ProviderSourceItemId <= 0
                    || !Enum.IsDefined(typeof(DroppedItemDependencyDisposition), decision.Disposition)
                    || string.IsNullOrWhiteSpace(decision.PolicyId))
                {
                    throw new InvalidDataException(
                        "A dropped lookup-value decision is null, incomplete, or uses an unsupported schema/disposition.");
                }
                var key = ExactEdgeKey(decision);
                if (result.ContainsKey(key))
                {
                    throw new InvalidDataException(
                        "Dropped lookup-value decisions must be unique per exact consumer/field/provider edge: " + key);
                }
                result.Add(key, decision);
            }
            return result;
        }

        internal static IList<DroppedLookupValueDecision> Canonicalize(
            IEnumerable<DroppedLookupValueDecision> decisions)
        {
            var indexed = ValidateAndIndex(decisions);
            if (indexed.Count == 0)
            {
                return null;
            }
            return indexed
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => Clone(value.Value))
                .ToList();
        }

        internal static void ValidateCanonicalOrder(
            IList<DroppedLookupValueDecision> decisions)
        {
            if (decisions == null)
            {
                return;
            }
            if (decisions.Count == 0)
            {
                throw new InvalidDataException(
                    "A sealed planning policy must represent an empty dropped lookup-value decision set as null.");
            }
            ValidateAndIndex(decisions);
            var actual = decisions.Select(ExactEdgeKey).ToArray();
            var expected = actual.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Dropped lookup-value decisions in a sealed planning policy are not in canonical exact-edge order.");
            }
        }

        internal static string ExactEdgeKey(DroppedLookupValueDecision decision)
        {
            return ListItemDependencyEdge.KeyFor(
                ListItemDependencyKind.LookupValue,
                decision.ConsumerSourceListId,
                decision.ConsumerSourceItemId,
                decision.ConsumerFieldInternalName,
                decision.ProviderSourceListId,
                decision.ProviderSourceItemId);
        }

        private static DroppedLookupValueDecision Clone(DroppedLookupValueDecision value)
        {
            return new DroppedLookupValueDecision
            {
                SchemaVersion = value.SchemaVersion,
                ConsumerSourceListId = value.ConsumerSourceListId,
                ConsumerSourceItemId = value.ConsumerSourceItemId,
                ConsumerFieldInternalName = value.ConsumerFieldInternalName,
                ProviderSourceListId = value.ProviderSourceListId,
                ProviderSourceItemId = value.ProviderSourceItemId,
                Disposition = value.Disposition,
                PolicyId = value.PolicyId
            };
        }
    }
}
