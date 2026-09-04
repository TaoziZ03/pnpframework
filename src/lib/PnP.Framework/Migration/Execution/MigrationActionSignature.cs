using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Execution
{
    /// <summary>
    /// Stable identity for one independently executable action node. It is
    /// deliberately independent from a page or closure plan digest so changes to
    /// unrelated sibling actions do not invalidate completed work.
    /// </summary>
    public sealed class MigrationActionSignature
    {
        public const string CurrentSchemaVersion = "pnp-migration-action-signature/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string ActionId { get; set; }

        public string ActionKind { get; set; }

        public string SourceEvidenceDigest { get; set; }

        public string SelectionReceiptDigest { get; set; }

        public string TargetIdentity { get; set; }

        public string TargetIdentityDigest { get; set; }

        public string SemanticDigest { get; set; }

        public IList<string> DependencySignatures { get; set; } = new List<string>();

        public string Signature { get; set; }

        public static MigrationActionSignature Create(
            string actionId,
            string actionKind,
            string sourceEvidenceDigest,
            string selectionReceiptDigest,
            string targetIdentity,
            string semanticDigest,
            IEnumerable<string> dependencySignatures = null)
        {
            var result = new MigrationActionSignature
            {
                ActionId = actionId?.Trim(),
                ActionKind = actionKind?.Trim(),
                SourceEvidenceDigest = NormalizeOptionalDigest(sourceEvidenceDigest),
                SelectionReceiptDigest = NormalizeOptionalDigest(selectionReceiptDigest),
                TargetIdentity = targetIdentity?.Trim(),
                SemanticDigest = semanticDigest?.Trim().ToLowerInvariant(),
                DependencySignatures = (dependencySignatures ?? Enumerable.Empty<string>())
                    .Select(value => value?.Trim().ToLowerInvariant())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList()
            };
            result.TargetIdentityDigest = string.IsNullOrWhiteSpace(result.TargetIdentity)
                ? null
                : MigrationDigest.ComputeSha256(result.TargetIdentity);
            result.Signature = ComputeSignature(result);
            Validate(result);
            return result;
        }

        public static void Validate(MigrationActionSignature value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var dependencies = value.DependencySignatures ?? new List<string>();
            var ordered = dependencies.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            if (!string.Equals(value.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(value.ActionId)
                || string.IsNullOrWhiteSpace(value.ActionKind)
                || !IsOptionalSha256(value.SourceEvidenceDigest)
                || !IsOptionalSha256(value.SelectionReceiptDigest)
                || string.IsNullOrWhiteSpace(value.TargetIdentity)
                || !IsSha256(value.TargetIdentityDigest)
                || !string.Equals(
                    value.TargetIdentityDigest,
                    MigrationDigest.ComputeSha256(value.TargetIdentity.Trim()),
                    StringComparison.OrdinalIgnoreCase)
                || !IsSha256(value.SemanticDigest)
                || dependencies.Any(item => !IsSha256(item))
                || dependencies.Count != dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                || !dependencies.SequenceEqual(ordered, StringComparer.Ordinal)
                || !IsSha256(value.Signature)
                || !string.Equals(value.Signature, ComputeSignature(value), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "A migration action signature requires canonical action, target, semantic, selection, source-evidence, and dependency identities with a matching SHA-256 signature.");
            }
        }

        public static string ComputeSignature(MigrationActionSignature value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    value,
                    nameof(Signature)));
        }

        private static string NormalizeOptionalDigest(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        internal static bool IsOptionalSha256(string value)
        {
            return string.IsNullOrWhiteSpace(value) || IsSha256(value);
        }

        internal static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 64
                && value.All(character => character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F');
        }
    }
}
