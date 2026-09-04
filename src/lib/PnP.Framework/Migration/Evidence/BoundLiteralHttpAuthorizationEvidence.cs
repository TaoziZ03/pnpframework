using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Execution;
using System;
using System.IO;

namespace PnP.Framework.Migration.Evidence
{
    /// <summary>
    /// Binds literal wire authorization evidence to the exact operation, request,
    /// authority, and migration action that may classify a result as blocked.
    /// </summary>
    public sealed class BoundLiteralHttpAuthorizationEvidence
    {
        public const string CurrentSchemaVersion = "pnp-bound-literal-http-authorization/v1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string ActionId { get; set; }

        public string ExpectedOperation { get; set; }

        public string ExpectedAuthority { get; set; }

        public string ExpectedRequestUri { get; set; }

        public LiteralHttpAuthorizationEvidence LiteralEvidence { get; set; }

        public string EvidenceSha256 { get; set; }

        public static BoundLiteralHttpAuthorizationEvidence Create(
            string actionId,
            string expectedOperation,
            string expectedRequestUri,
            LiteralHttpAuthorizationEvidence literalEvidence)
        {
            var request = NormalizeUri(expectedRequestUri);
            var result = new BoundLiteralHttpAuthorizationEvidence
            {
                ActionId = actionId?.Trim(),
                ExpectedOperation = expectedOperation?.Trim(),
                ExpectedAuthority = new Uri(request).Authority.ToLowerInvariant(),
                ExpectedRequestUri = request,
                LiteralEvidence = literalEvidence
            };
            result.EvidenceSha256 = ComputeDigest(result);
            Validate(result, result.ActionId, result.ExpectedOperation, result.ExpectedAuthority, result.ExpectedRequestUri);
            return result;
        }

        public static void Validate(
            BoundLiteralHttpAuthorizationEvidence evidence,
            string expectedActionId,
            string expectedOperation,
            string expectedAuthority,
            string expectedRequestUri)
        {
            if (evidence == null)
            {
                throw new InvalidDataException("Bound literal authorization evidence is missing.");
            }
            LiteralHttpAuthorizationEvidence.Validate(evidence.LiteralEvidence);
            var externallyExpectedUri = NormalizeUri(expectedRequestUri);
            var expectedUri = NormalizeUri(evidence.ExpectedRequestUri);
            var literalUri = NormalizeUri(evidence.LiteralEvidence.RequestUri);
            var authority = new Uri(externallyExpectedUri).Authority.ToLowerInvariant();
            if (!string.Equals(evidence.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(evidence.ActionId)
                || !string.Equals(evidence.ActionId, expectedActionId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(evidence.ExpectedOperation)
                || !string.Equals(evidence.ExpectedOperation, expectedOperation, StringComparison.Ordinal)
                || !string.Equals(evidence.ExpectedOperation, evidence.LiteralEvidence.Operation, StringComparison.Ordinal)
                || !string.Equals(expectedAuthority, authority, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(evidence.ExpectedAuthority, authority, StringComparison.Ordinal)
                || !string.Equals(expectedUri, externallyExpectedUri, StringComparison.Ordinal)
                || !string.Equals(expectedUri, literalUri, StringComparison.Ordinal)
                || !MigrationActionSignature.IsSha256(evidence.EvidenceSha256)
                || !string.Equals(evidence.EvidenceSha256, ComputeDigest(evidence), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Literal authorization evidence does not match its expected action, operation, authority, request URI, or digest.");
            }
        }

        public static string ComputeDigest(BoundLiteralHttpAuthorizationEvidence evidence)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    evidence ?? throw new ArgumentNullException(nameof(evidence)),
                    nameof(EvidenceSha256)));
        }

        private static string NormalizeUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Authorization evidence requires an absolute HTTP request URI.");
            }
            return uri.AbsoluteUri;
        }
    }
}
