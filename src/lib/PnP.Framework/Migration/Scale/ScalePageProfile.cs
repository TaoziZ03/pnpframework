using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Execution;
using System;
using System.IO;

namespace PnP.Framework.Migration.Scale
{
    public sealed class ScalePageProfile
    {
        public const string CurrentSchemaVersion = "pnp-scale-page-profile/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string PageFamily { get; set; }

        public string TargetReferenceKey { get; set; }

        public string SupportCohortSignature { get; set; }

        public string ExecutionCohortSignature { get; set; }

        public string LoadBucket { get; set; }

        public string ProfileDigest { get; set; }

        public static string ComputeProfileDigest(ScalePageProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    profile,
                    nameof(ScalePageProfile.ProfileDigest)));
        }

        public static ScalePageProfile Seal(ScalePageProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            profile.ProfileDigest = ComputeProfileDigest(profile);
            Validate(profile);
            return profile;
        }

        public static void Validate(ScalePageProfile profile)
        {
            if (profile == null
                || !string.Equals(profile.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(profile.PageFamily)
                || string.IsNullOrWhiteSpace(profile.TargetReferenceKey)
                || !MigrationActionSignature.IsSha256(profile.SupportCohortSignature)
                || !MigrationActionSignature.IsSha256(profile.ExecutionCohortSignature)
                || string.IsNullOrWhiteSpace(profile.LoadBucket)
                || !MigrationActionSignature.IsSha256(profile.ProfileDigest)
                || !string.Equals(profile.ProfileDigest, ComputeProfileDigest(profile), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A scale page profile is incomplete, invalid, or has a stale digest.");
            }
            ScaleRunManifestValidator.ValidateOpaqueKey(profile.PageFamily, nameof(profile.PageFamily));
            ScaleRunManifestValidator.ValidateOpaqueKey(profile.TargetReferenceKey, nameof(profile.TargetReferenceKey));
            ScaleRunManifestValidator.ValidateOpaqueKey(profile.LoadBucket, nameof(profile.LoadBucket));
        }

        public static ScalePageProfile Clone(ScalePageProfile profile)
        {
            if (profile == null)
            {
                return null;
            }
            return new ScalePageProfile
            {
                SchemaVersion = profile.SchemaVersion,
                PageFamily = profile.PageFamily,
                TargetReferenceKey = profile.TargetReferenceKey,
                SupportCohortSignature = profile.SupportCohortSignature,
                ExecutionCohortSignature = profile.ExecutionCohortSignature,
                LoadBucket = profile.LoadBucket,
                ProfileDigest = profile.ProfileDigest
            };
        }

        public static void ValidateCompatibility(
            ScaleRunPage expectedPage,
            ScalePageProfile discoveredProfile)
        {
            if (expectedPage == null || discoveredProfile == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(expectedPage.TargetReferenceKey)
                && !string.IsNullOrWhiteSpace(discoveredProfile.TargetReferenceKey)
                && !string.Equals(expectedPage.TargetReferenceKey, discoveredProfile.TargetReferenceKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Scale page profile mismatch on target reference key: expected '{expectedPage.TargetReferenceKey}', but Plan discovered '{discoveredProfile.TargetReferenceKey}'.");
            }

            if (!string.IsNullOrWhiteSpace(expectedPage.SupportCohortSignature)
                && !string.IsNullOrWhiteSpace(discoveredProfile.SupportCohortSignature)
                && !string.Equals(expectedPage.SupportCohortSignature, discoveredProfile.SupportCohortSignature, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Scale page profile mismatch on support cohort signature: expected '{expectedPage.SupportCohortSignature}', but Plan discovered '{discoveredProfile.SupportCohortSignature}'.");
            }

            if (!string.IsNullOrWhiteSpace(expectedPage.ExecutionCohortSignature)
                && !string.IsNullOrWhiteSpace(discoveredProfile.ExecutionCohortSignature)
                && !string.Equals(expectedPage.ExecutionCohortSignature, discoveredProfile.ExecutionCohortSignature, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Scale page profile mismatch on execution cohort signature: expected '{expectedPage.ExecutionCohortSignature}', but Plan discovered '{discoveredProfile.ExecutionCohortSignature}'.");
            }
        }
    }
}
