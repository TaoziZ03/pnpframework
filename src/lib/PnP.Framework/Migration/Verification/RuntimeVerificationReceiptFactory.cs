using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PnP.Framework.Migration.Packaging;

namespace PnP.Framework.Migration.Verification
{
    public static class RuntimeVerificationReceiptFactory
    {
        public static RuntimeVerificationReceipt Create(
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            string importReceiptDigestSha256,
            string targetIdentity,
            RuntimeVerificationManifest manifest,
            string implementationRef,
            RuntimeBrowserContextIdentity browserContext,
            IEnumerable<RuntimeVerificationResult> results,
            DateTimeOffset completedAtUtc)
        {
            var computedAdmissionDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                admittedPlan?.PlanDigest,
                targetIdentity);
            Require(DigestEquals(computedAdmissionDigest, admittedPlanDigestSha256),
                "The runtime producer received a stale or corrupt admitted-plan digest.");
            ValidateDigest(importReceiptDigestSha256, "import receipt digest");
            Require(manifest != null && manifest.Requirements != null,
                "A runtime verification manifest is required.");
            ValidateImplementationRef(implementationRef);
            ValidateBrowserContext(browserContext, completedAtUtc);
            var requirementsManifestDigest = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonical(manifest));

            var materializedResults = (results ?? Enumerable.Empty<RuntimeVerificationResult>()).ToList();
            var duplicate = materializedResults.GroupBy(value => value?.RequirementId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            Require(duplicate == null && materializedResults.All(value => value != null),
                "Runtime results contain a missing or duplicate requirement ID.");
            var required = manifest.Requirements.Where(value => value.Required).ToList();
            Require(required.All(requirement => materializedResults.Count(value =>
                    string.Equals(value.RequirementId, requirement.Id, StringComparison.Ordinal)) == 1),
                "The runtime producer did not cover every required requirement exactly once.");
            Require(materializedResults.All(value => manifest.Requirements.Any(requirement =>
                    string.Equals(requirement.Id, value.RequirementId, StringComparison.Ordinal))),
                "The runtime producer emitted an unknown requirement ID.");

            foreach (var result in materializedResults)
            {
                ValidateDigest(result.EvidenceArtifactSha256, "runtime evidence digest");
                Require(result.EvidenceArtifactLength > 0
                    && !string.IsNullOrWhiteSpace(result.EvidenceArtifactLocator),
                    "Runtime evidence bytes require a positive length and locator.");
                Require(string.Equals(result.ImplementationRef, implementationRef, StringComparison.OrdinalIgnoreCase),
                    "Runtime result implementation ref is missing or foreign.");
                Require(string.Equals(result.BrowserContextId, browserContext.BrowserContextId, StringComparison.Ordinal),
                    "Runtime result browser context is missing or foreign.");
            }

            var status = required.Count == 0
                ? RuntimeVerificationStatus.NotRequired
                : required.All(requirement => materializedResults.Single(value =>
                    string.Equals(value.RequirementId, requirement.Id, StringComparison.Ordinal)).Passed)
                    ? RuntimeVerificationStatus.Passed
                    : RuntimeVerificationStatus.Failed;
            return new RuntimeVerificationReceipt
            {
                PlanDigest = admittedPlan.PlanDigest,
                AdmittedPlanDigestSha256 = admittedPlanDigestSha256.ToLowerInvariant(),
                OperationId = admittedPlan.Operations.RuntimeOperationId,
                ImportReceiptDigestSha256 = importReceiptDigestSha256.ToLowerInvariant(),
                RequirementsManifestDigestSha256 = requirementsManifestDigest,
                ImplementationRef = implementationRef.ToLowerInvariant(),
                SourceVersion = admittedPlan.SourceVersion,
                Operations = admittedPlan.Operations,
                TargetIdentity = targetIdentity,
                BrowserContext = browserContext,
                CompletedAtUtc = completedAtUtc,
                Results = materializedResults,
                Status = status
            };
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex),
                "The " + name + " must be a SHA-256 digest.");
        }

        private static void ValidateImplementationRef(string value)
        {
            Require(value != null && value.Length == 40 && value.All(IsHex),
                "The runtime implementation ref must be a full Git SHA.");
        }

        private static void ValidateBrowserContext(
            RuntimeBrowserContextIdentity browserContext,
            DateTimeOffset completedAtUtc)
        {
            Require(browserContext != null
                && browserContext.FreshContext
                && browserContext.IsIncognito
                && !string.IsNullOrWhiteSpace(browserContext.BrowserProduct)
                && !string.IsNullOrWhiteSpace(browserContext.BrowserVersion)
                && !string.IsNullOrWhiteSpace(browserContext.ProtocolVersion)
                && !string.IsNullOrWhiteSpace(browserContext.BrowserContextId)
                && !string.IsNullOrWhiteSpace(browserContext.TargetId),
                "A fresh isolated browser context identity is required.");
            ValidateDigest(browserContext.ProfileIdentitySha256, "browser profile identity digest");
            Require(browserContext.CreatedAtUtc != default
                && browserContext.FirstNavigationAtUtc >= browserContext.CreatedAtUtc
                && completedAtUtc >= browserContext.FirstNavigationAtUtc,
                "The fresh browser-context timeline is invalid.");
        }

        private static bool DigestEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
