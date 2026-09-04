using PnP.Framework.Migration.Packaging;
using System;
using System.IO;

namespace PnP.Framework.Migration.Taxonomy.Assets
{
    /// <summary>
    /// Optional, separately approved reference stamp for an external taxonomy
    /// object. This property is intentionally distinct from the ownership marker
    /// and is not consumed by owned-object inspection.
    /// </summary>
    public sealed class TaxonomyExternalReferenceStamp
    {
        public string SchemaVersion { get; set; } = "pnp-taxonomy-external-reference-stamp/v1";

        public string ActionId { get; set; }

        public TaxonomyAssetKind Kind { get; set; }

        public string SourceIdentity { get; set; }

        public string TargetIdentity { get; set; }

        public string PropertyName { get; set; } = TaxonomyAssetIdentity.ExternalReferencePropertyName;

        public string PropertyValue { get; set; }

        public string ReviewPlanDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public bool ExplicitPerObjectApproval { get; set; }

        public string StampDigest { get; set; }
    }

    public static class TaxonomyExternalReferenceStampPolicy
    {
        public static void Seal(TaxonomyExternalReferenceStamp stamp)
        {
            if (stamp == null)
            {
                throw new ArgumentNullException(nameof(stamp));
            }
            stamp.StampDigest = null;
            Validate(stamp, false);
            stamp.StampDigest = ComputeDigest(stamp);
            Validate(stamp, true);
        }

        public static void Validate(TaxonomyExternalReferenceStamp stamp, bool requireDigest = true)
        {
            if (stamp == null)
            {
                throw new ArgumentNullException(nameof(stamp));
            }
            if (!string.Equals(stamp.SchemaVersion, "pnp-taxonomy-external-reference-stamp/v1", StringComparison.Ordinal)
                || !stamp.ExplicitPerObjectApproval
                || string.IsNullOrWhiteSpace(stamp.ActionId)
                || string.IsNullOrWhiteSpace(stamp.SourceIdentity)
                || string.IsNullOrWhiteSpace(stamp.TargetIdentity)
                || !string.Equals(stamp.PropertyName, TaxonomyAssetIdentity.ExternalReferencePropertyName, StringComparison.Ordinal)
                || string.Equals(stamp.PropertyName, TaxonomyAssetIdentity.OriginalIdentifierPropertyName, StringComparison.Ordinal)
                || string.Equals(stamp.PropertyName, TaxonomyAssetIdentity.MappingDigestPropertyName, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(stamp.PropertyValue)
                || !IsSha256(stamp.ReviewPlanDigest)
                || !IsSha256(stamp.ApprovalDigest)
                || requireDigest && (!IsSha256(stamp.StampDigest)
                    || !string.Equals(stamp.StampDigest, ComputeDigest(stamp), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "External taxonomy provenance stamping requires exact per-object, plan-digest, and approval-digest authorization and may not use an ownership property.");
            }
        }

        public static string ComputeDigest(TaxonomyExternalReferenceStamp stamp)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    stamp,
                    nameof(TaxonomyExternalReferenceStamp.StampDigest)));
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }
            foreach (var character in value)
            {
                if (!(character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
