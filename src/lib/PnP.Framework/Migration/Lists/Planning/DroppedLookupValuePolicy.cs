using System;
using System.IO;

namespace PnP.Framework.Migration.Lists.Planning
{
    /// <summary>
    /// Reviewed handling for a captured lookup value whose referenced source
    /// item is intentionally excluded from target materialization.
    /// </summary>
    public enum DroppedLookupValueDisposition
    {
        NeedsPolicyDecision = 0,
        ClearValue = 1,
        DropDependentItem = 2
    }

    public sealed class DroppedLookupValuePolicy
    {
        public const string ContractVersion = "pnp-dropped-lookup-value-policy/v1";

        public string SchemaVersion { get; set; } = ContractVersion;

        public string PolicyId { get; set; }

        public DroppedLookupValueDisposition Disposition { get; set; }

        public static DroppedLookupValuePolicy Clear(string policyId)
        {
            return Create(policyId, DroppedLookupValueDisposition.ClearValue);
        }

        public static DroppedLookupValuePolicy DropDependent(string policyId)
        {
            return Create(policyId, DroppedLookupValueDisposition.DropDependentItem);
        }

        public static DroppedLookupValuePolicy NeedsDecision(string policyId)
        {
            return Create(policyId, DroppedLookupValueDisposition.NeedsPolicyDecision);
        }

        internal static void Validate(DroppedLookupValuePolicy policy)
        {
            if (policy == null)
            {
                return;
            }
            if (!string.Equals(policy.SchemaVersion, ContractVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(policy.PolicyId)
                || !Enum.IsDefined(typeof(DroppedLookupValueDisposition), policy.Disposition))
            {
                throw new InvalidDataException(
                    "The dropped-lookup-value policy has an unsupported schema, missing policy ID, or invalid disposition.");
            }
        }

        private static DroppedLookupValuePolicy Create(
            string policyId,
            DroppedLookupValueDisposition disposition)
        {
            return new DroppedLookupValuePolicy
            {
                PolicyId = policyId,
                Disposition = disposition
            };
        }
    }
}
