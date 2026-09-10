using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public static class NativePageRuntimeBindingValidator
    {
        public static string ValidateCoreAndComputeDigest(
            NativePageRuntimeBinding binding,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            NativePageRuntimeTargetIdentity expectedTarget)
        {
            Require(binding != null, "A native page runtime binding is required.");
            Require(string.Equals(binding.SchemaVersion, NativePageRuntimeContract.BindingSchemaVersion, StringComparison.Ordinal),
                "The native page runtime binding schema is unsupported.");
            Require(string.Equals(binding.ProfileId, NativePageRuntimeContract.ClassicWikiProfile, StringComparison.Ordinal),
                "The native page runtime profile is unsupported.");
            Require(string.Equals(binding.PageFamily, NativePageRuntimeContract.ClassicWikiFamily, StringComparison.Ordinal),
                "The native page runtime family is unsupported.");

            var computedAdmittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                admittedPlan?.PlanDigest,
                admittedPlan?.TargetIdentity);
            Require(DigestEquals(computedAdmittedDigest, admittedPlanDigestSha256),
                "The supplied admitted-plan digest is stale or corrupt.");
            var importBinding = NativePageImportReceiptAggregateValidator.ValidateForCompare(
                importAggregate,
                admittedPlan,
                computedAdmittedDigest);
            Require(string.Equals(importBinding.PageFamily, NativePageRuntimeContract.ClassicWikiFamily, StringComparison.Ordinal),
                "Only a native Classic Wiki import receipt can enter this runtime profile.");
            Require(importBinding.FreshReadbackPassed
                && importBinding.StorageVerificationStatus == StorageVerificationStatus.Passed,
                "Native runtime acceptance requires a passed fresh storage readback.");
            Require(importBinding.RuntimeVerificationStatus == RuntimeVerificationStatus.Pending
                && importAggregate.ClassicWikiReceipt.AcceptanceStatus == MigrationAcceptanceStatus.Pending,
                "The native import receipt must enter runtime acceptance in Pending state.");
            Require(binding.RuntimeOperationId == admittedPlan.Operations.RuntimeOperationId,
                "The runtime operation ID is missing or foreign.");
            Require(DigestEquals(binding.SourceIdentityDigestSha256, admittedPlan.SourceVersion.IdentityDigestSha256),
                "The runtime binding source identity is missing or stale.");
            Require(DigestEquals(binding.SourceVersionDigestSha256, admittedPlan.SourceVersion.VersionDigestSha256),
                "The runtime binding source version is missing or stale.");
            Require(DigestEquals(binding.AdmittedPlanDigestSha256, computedAdmittedDigest),
                "The runtime binding admitted-plan digest is missing or foreign.");
            Require(DigestEquals(binding.ImportReceiptDigestSha256, importBinding.ReceiptDigestSha256),
                "The runtime binding import receipt digest is missing or foreign.");
            ValidateTarget(expectedTarget, "expected native target");
            ValidateTarget(binding.Target, "runtime binding target");
            Require(SameTarget(binding.Target, expectedTarget),
                "The runtime binding target Site/Web/File/item/version/ETag identity is foreign or stale.");
            Require(UriEquals(binding.Target.WebUrl, importBinding.TargetWebUrl)
                && PathEquals(binding.Target.PageServerRelativeUrl, importBinding.TargetPageServerRelativeUrl)
                && binding.Target.FileUniqueId == importBinding.TargetFileUniqueId
                && binding.Target.ListItemId == importBinding.TargetListItemId
                && string.Equals(binding.Target.ListItemVersion, importBinding.TargetVersionLabel, StringComparison.Ordinal),
                "The runtime binding target identity does not match the native import receipt.");
            Require(string.Equals(CanonicalTargetIdentity(binding.Target), admittedPlan.TargetIdentity, StringComparison.Ordinal),
                "The runtime binding target is foreign to the admitted plan.");
            ValidateAuthorityShape(binding);
            return ComputeBindingDigest(binding);
        }

        public static void ValidateNativeEvidence(
            NativePageRuntimeBinding binding,
            RuntimeVerificationReceipt runtimeReceipt,
            RuntimeVerificationManifest requirementsManifest,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore,
            AdmittedReproExecutionPlan admittedPlan)
        {
            Require(binding != null, "A native page runtime binding is required.");
            Require(string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.NativeAuthority, StringComparison.Ordinal),
                "Native runtime acceptance requires native producer authority.");
            Require(runtimeReceipt != null, "A canonical native runtime receipt is required.");
            RuntimeVerificationReceiptValidator.ValidateEvidence(
                runtimeReceipt,
                requirementsManifest,
                expectedImplementationRef,
                artifactStore);
            Require(string.Equals(runtimeReceipt.SchemaVersion, "pnp-migration-runtime-verification-receipt/v1", StringComparison.Ordinal),
                "The canonical runtime receipt schema is unsupported.");
            Require(runtimeReceipt.OperationId == binding.RuntimeOperationId
                && runtimeReceipt.OperationId == admittedPlan.Operations.RuntimeOperationId,
                "The canonical runtime receipt operation is missing or foreign.");
            Require(DigestEquals(runtimeReceipt.AdmittedPlanDigestSha256, binding.AdmittedPlanDigestSha256),
                "The canonical runtime receipt admitted-plan binding is missing or foreign.");
            Require(DigestEquals(runtimeReceipt.ImportReceiptDigestSha256, binding.ImportReceiptDigestSha256),
                "The canonical runtime receipt import binding is missing or foreign.");
            Require(string.Equals(runtimeReceipt.PlanDigest, admittedPlan.PlanDigest, StringComparison.OrdinalIgnoreCase),
                "The canonical runtime receipt plan binding is missing or foreign.");
            Require(AdmittedReproExecutionPlanValidator.SameSourceVersion(runtimeReceipt.SourceVersion, admittedPlan.SourceVersion),
                "The canonical runtime receipt source version is missing or stale.");
            Require(AdmittedReproExecutionPlanValidator.SameOperations(runtimeReceipt.Operations, admittedPlan.Operations),
                "The canonical runtime receipt operation set is missing or foreign.");
            Require(string.Equals(runtimeReceipt.TargetIdentity, admittedPlan.TargetIdentity, StringComparison.Ordinal),
                "The canonical runtime receipt target is foreign to the admitted plan.");

            var results = runtimeReceipt.Results ?? new List<RuntimeVerificationResult>();
            var requirements = requirementsManifest?.Requirements ?? new List<RuntimeVerificationRequirement>();
            Require(requirements.All(value => value != null && !string.IsNullOrWhiteSpace(value.Id)),
                "The runtime requirements manifest contains a missing requirement.");
            Require(requirements.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() == requirements.Count,
                "The runtime requirements manifest contains duplicate requirement IDs.");
            Require(requirements.All(value => Enum.IsDefined(typeof(RuntimeVerificationRequirementKind), value.Kind)),
                "The runtime requirements manifest contains an unsupported requirement kind.");
            Require(results.All(value => value != null && !string.IsNullOrWhiteSpace(value.RequirementId)),
                "The runtime receipt contains a missing result.");
            Require(results.Select(value => value.RequirementId).Distinct(StringComparer.Ordinal).Count() == results.Count,
                "The runtime receipt contains duplicate result IDs.");
            Require(results.All(result => requirements.Any(requirement =>
                    string.Equals(requirement.Id, result.RequirementId, StringComparison.Ordinal))),
                "The runtime receipt contains an unknown result ID.");
            var required = requirements.Where(value => value.Required).ToList();
            Require(required.All(requirement => results.Count(result =>
                    string.Equals(requirement.Id, result.RequirementId, StringComparison.Ordinal)) == 1),
                "The runtime receipt does not cover every required result exactly once.");
            var derivedStatus = required.Count == 0
                ? RuntimeVerificationStatus.NotRequired
                : required.All(requirement => results.Single(result =>
                    string.Equals(requirement.Id, result.RequirementId, StringComparison.Ordinal)).Passed)
                    ? RuntimeVerificationStatus.Passed
                    : RuntimeVerificationStatus.Failed;
            Require(runtimeReceipt.Status == derivedStatus && binding.RuntimeVerificationStatus == derivedStatus,
                "The runtime status is not the status derived from canonical native evidence.");
            Require(derivedStatus == RuntimeVerificationStatus.Passed
                || derivedStatus == RuntimeVerificationStatus.Failed
                || derivedStatus == RuntimeVerificationStatus.NotRequired,
                "A nonterminal canonical runtime status cannot decide acceptance.");

            Require(results.Count > 0 || derivedStatus == RuntimeVerificationStatus.NotRequired,
                "Canonical runtime evidence is missing.");
            if (results.Count > 0)
            {
                Require(results.All(result => result.Http != null
                    && string.Equals(result.Http.RequestedUrl, binding.RequestedUrl, StringComparison.Ordinal)
                    && string.Equals(result.Http.FinalUrl, binding.FinalUrl, StringComparison.Ordinal)),
                    "The runtime requested/final URL binding is missing or inconsistent.");
            }
            Require(string.Equals(binding.RuntimeReceiptSchemaVersion, runtimeReceipt.SchemaVersion, StringComparison.Ordinal),
                "The runtime receipt schema binding is missing or foreign.");
            var runtimeDigest = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonical(runtimeReceipt));
            Require(DigestEquals(binding.RuntimeEvidenceDigestSha256, runtimeDigest),
                "The canonical runtime evidence digest is missing, altered, or foreign.");
        }

        public static string SealExternalEvidence(ExternalPageRuntimeEvidence evidence)
        {
            Require(evidence != null, "External runtime evidence is required.");
            evidence.ReportDigestSha256 = ComputeExternalEvidenceDigest(evidence);
            return evidence.ReportDigestSha256;
        }

        public static void ValidateExternalEvidence(
            ExternalPageRuntimeEvidence evidence,
            NativePageRuntimeBinding binding)
        {
            Require(evidence != null, "External runtime evidence is required.");
            Require(string.Equals(evidence.SchemaVersion, NativePageRuntimeContract.ExternalEvidenceSchemaVersion, StringComparison.Ordinal),
                "The external runtime evidence schema is unsupported.");
            Require(string.Equals(evidence.ProfileId, NativePageRuntimeContract.ClassicWikiProfile, StringComparison.Ordinal),
                "The external runtime evidence profile is unsupported.");
            Require(!string.IsNullOrWhiteSpace(evidence.ReportId) && evidence.ObservedAtUtc != default,
                "External runtime report identity and observation time are required.");
            Require(evidence.RuntimeOperationId == binding.RuntimeOperationId
                && DigestEquals(evidence.SourceVersionDigestSha256, binding.SourceVersionDigestSha256)
                && DigestEquals(evidence.AdmittedPlanDigestSha256, binding.AdmittedPlanDigestSha256)
                && DigestEquals(evidence.ImportReceiptDigestSha256, binding.ImportReceiptDigestSha256),
                "The external runtime report is foreign to the native operation or inputs.");
            Require(SameTarget(evidence.Target, binding.Target),
                "The external runtime report target is foreign or stale.");
            Require(string.Equals(evidence.RequestedUrl, binding.RequestedUrl, StringComparison.Ordinal)
                && string.Equals(evidence.FinalUrl, binding.FinalUrl, StringComparison.Ordinal),
                "The external runtime report URL binding is foreign.");
            ValidateDigest(evidence.EvidenceDigestSha256, "external runtime evidence digest");
            Require(Enum.IsDefined(typeof(RuntimeVerificationStatus), evidence.ClaimedStatus),
                "The external runtime report status is unsupported.");
            var computedDigest = ComputeExternalEvidenceDigest(evidence);
            Require(DigestEquals(evidence.ReportDigestSha256, computedDigest)
                && DigestEquals(binding.ExternalEvidenceDigestSha256, computedDigest),
                "The external runtime report digest is missing, altered, or foreign.");
        }

        public static string ComputeBindingDigest(NativePageRuntimeBinding binding)
        {
            Require(binding != null, "A native page runtime binding is required.");
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(binding));
        }

        public static string ComputeExternalEvidenceDigest(ExternalPageRuntimeEvidence evidence)
        {
            Require(evidence != null, "External runtime evidence is required.");
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    evidence,
                    nameof(ExternalPageRuntimeEvidence.ReportDigestSha256)));
        }

        public static NativePageRuntimeTargetIdentity CopyTarget(NativePageRuntimeTargetIdentity value)
        {
            if (value == null)
            {
                return null;
            }
            return new NativePageRuntimeTargetIdentity
            {
                SiteId = value.SiteId,
                WebId = value.WebId,
                WebUrl = value.WebUrl,
                PageServerRelativeUrl = value.PageServerRelativeUrl,
                FileUniqueId = value.FileUniqueId,
                ListItemId = value.ListItemId,
                ListItemVersion = value.ListItemVersion,
                ListItemETag = value.ListItemETag
            };
        }

        private static void ValidateAuthorityShape(NativePageRuntimeBinding binding)
        {
            Require(Enum.IsDefined(typeof(RuntimeVerificationStatus), binding.RuntimeVerificationStatus),
                "The runtime verification status is unsupported.");
            if (string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.NativeAuthority, StringComparison.Ordinal))
            {
                Require(!string.IsNullOrWhiteSpace(binding.RequestedUrl)
                    && !string.IsNullOrWhiteSpace(binding.FinalUrl)
                    && !string.IsNullOrWhiteSpace(binding.RuntimeReceiptSchemaVersion),
                    "Native runtime URL and receipt schema bindings are required.");
                ValidateDigest(binding.RuntimeEvidenceDigestSha256, "native runtime evidence digest");
                Require(binding.RuntimeVerificationStatus != RuntimeVerificationStatus.Pending
                    && binding.RuntimeVerificationStatus != RuntimeVerificationStatus.NotRun,
                    "Native runtime authority cannot publish a nonterminal status.");
            }
            else if (string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.ExternalAuthority, StringComparison.Ordinal))
            {
                Require(binding.RuntimeVerificationStatus == RuntimeVerificationStatus.Pending,
                    "External runtime evidence cannot change native Pending status.");
                Require(!string.IsNullOrWhiteSpace(binding.RequestedUrl)
                    && !string.IsNullOrWhiteSpace(binding.FinalUrl),
                    "External runtime URL bindings are required when an external report is retained.");
                ValidateDigest(binding.RuntimeEvidenceDigestSha256, "external runtime evidence digest");
                ValidateDigest(binding.ExternalEvidenceDigestSha256, "external runtime report digest");
            }
            else
            {
                Require(string.Equals(binding.RuntimeEvidenceAuthority, NativePageRuntimeContract.NoAuthority, StringComparison.Ordinal)
                    && binding.RuntimeVerificationStatus == RuntimeVerificationStatus.Pending
                    && string.IsNullOrWhiteSpace(binding.RequestedUrl)
                    && string.IsNullOrWhiteSpace(binding.FinalUrl)
                    && string.IsNullOrWhiteSpace(binding.RuntimeEvidenceDigestSha256)
                    && string.IsNullOrWhiteSpace(binding.ExternalEvidenceDigestSha256),
                    "A missing runtime authority must preserve a clean Pending binding.");
            }
        }

        private static void ValidateTarget(NativePageRuntimeTargetIdentity target, string name)
        {
            Require(target != null
                && target.SiteId != Guid.Empty
                && target.WebId != Guid.Empty
                && target.FileUniqueId != Guid.Empty
                && target.ListItemId > 0
                && Uri.TryCreate(target.WebUrl, UriKind.Absolute, out _)
                && !string.IsNullOrWhiteSpace(target.PageServerRelativeUrl)
                && target.PageServerRelativeUrl.StartsWith("/", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(target.ListItemVersion)
                && !string.IsNullOrWhiteSpace(target.ListItemETag),
                "The " + name + " is incomplete.");
        }

        private static bool SameTarget(NativePageRuntimeTargetIdentity left, NativePageRuntimeTargetIdentity right)
        {
            return left != null
                && right != null
                && left.SiteId == right.SiteId
                && left.WebId == right.WebId
                && UriEquals(left.WebUrl, right.WebUrl)
                && PathEquals(left.PageServerRelativeUrl, right.PageServerRelativeUrl)
                && left.FileUniqueId == right.FileUniqueId
                && left.ListItemId == right.ListItemId
                && string.Equals(left.ListItemVersion, right.ListItemVersion, StringComparison.Ordinal)
                && string.Equals(left.ListItemETag, right.ListItemETag, StringComparison.Ordinal);
        }

        private static string CanonicalTargetIdentity(NativePageRuntimeTargetIdentity target)
        {
            var web = new Uri(target.WebUrl, UriKind.Absolute);
            return web.GetLeftPart(UriPartial.Authority).TrimEnd('/')
                + "/"
                + target.PageServerRelativeUrl.TrimStart('/');
        }

        private static bool UriEquals(string left, string right)
        {
            return Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
                && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
                && string.Equals(
                    leftUri.AbsoluteUri.TrimEnd('/'),
                    rightUri.AbsoluteUri.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathEquals(string left, string right)
        {
            return string.Equals(
                (left ?? string.Empty).TrimEnd('/'),
                (right ?? string.Empty).TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool DigestEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex),
                "The " + name + " must be a SHA-256 digest.");
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
