using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Publishing.Comparison
{
    /// <summary>
    /// Strict semantic reader for the frozen pnp-page-compare-report/v1 contract.
    /// It does not widen v1 for another page family or terminal outcome.
    /// </summary>
    public static class PublishingPageCompareReportValidator
    {
        private static readonly HashSet<string> ResultClasses = new HashSet<string>(new[]
        {
            PublishingPageCompareContract.ResultClasses.Exact,
            PublishingPageCompareContract.ResultClasses.CanonicalEquivalent,
            PublishingPageCompareContract.ResultClasses.TransformedAsPlanned,
            PublishingPageCompareContract.ResultClasses.SourceVersionChanged,
            PublishingPageCompareContract.ResultClasses.Missing,
            PublishingPageCompareContract.ResultClasses.UnexpectedExtra,
            PublishingPageCompareContract.ResultClasses.Mismatch,
            PublishingPageCompareContract.ResultClasses.Deferred,
            PublishingPageCompareContract.ResultClasses.AuthorizationBlocked,
            PublishingPageCompareContract.ResultClasses.RuntimePending,
            PublishingPageCompareContract.ResultClasses.AuthExpired,
            PublishingPageCompareContract.ResultClasses.Unknown,
            PublishingPageCompareContract.ResultClasses.NotApplicable
        }, StringComparer.Ordinal);

        private static readonly HashSet<string> RuntimeStatuses = new HashSet<string>(new[]
        {
            "not-run", "not-required", "pending", "passed", "failed"
        }, StringComparer.Ordinal);

        private static readonly HashSet<string> StorageStatuses = new HashSet<string>(new[]
        {
            "not-run", "passed", "failed"
        }, StringComparer.Ordinal);

        private static readonly HashSet<string> Verdicts = new HashSet<string>(new[]
        {
            "pass", "conditional", "fail", "unverified"
        }, StringComparer.Ordinal);

        public static string SerializeCanonical(
            PublishingPageCompareReport report,
            string expectedImplementationRef)
        {
            Validate(report, expectedImplementationRef);
            return MigrationContractSerializer.SerializeCanonical(report);
        }

        public static PublishingPageCompareReport DeserializeStrict(
            string json,
            string expectedImplementationRef)
        {
            var report = MigrationContractSerializer.Deserialize<PublishingPageCompareReport>(json);
            Require(PageCompareJsonShape.HasSameJsonShape(json, report),
                "The canonical page compare report contains an unknown or omitted field.");
            Validate(report, expectedImplementationRef);
            return report;
        }

        public static void Validate(
            PublishingPageCompareReport report,
            string expectedImplementationRef)
        {
            Require(report != null, "A canonical page compare report is required.");
            RequireNoExtensions(report.ExtensionData, "compare report");
            Require(string.Equals(report.SchemaVersion, PublishingPageCompareContract.SchemaVersion, StringComparison.Ordinal),
                "The canonical page compare report schema is unsupported.");
            Require(report.GeneratedAtUtc != default, "The compare report generation time is required.");
            ValidateImplementationRef(expectedImplementationRef, "expected compare implementation ref");

            var producer = report.Producer;
            Require(producer != null
                && !string.IsNullOrWhiteSpace(producer.Id)
                && !string.IsNullOrWhiteSpace(producer.Version),
                "The compare producer identity is incomplete.");
            RequireNoExtensions(producer.ExtensionData, "compare producer");
            ValidateImplementationRef(producer.ImplementationRef, "compare producer implementation ref");
            Require(string.Equals(producer.ImplementationRef, expectedImplementationRef, StringComparison.OrdinalIgnoreCase),
                "The compare report implementation ref is foreign.");

            ValidateAssessment(report.AssessmentHandoff);
            ValidateBindings(report.Bindings);
            ValidateSourceVersion(report.SourceVersionComparison);
            ValidateTarget(report.TargetIdentity);
            ValidateStatus(report.Storage, report.Runtime, report.Acceptance);
            ValidateIngredients(report.Ingredients, report.Storage, report.Runtime, report.Acceptance);

            ValidateDigest(report.ReportDigestSha256, "compare report digest");
            var computed = PublishingPageCompareReconciler.ComputeReportDigest(report);
            Require(string.Equals(computed, report.ReportDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The canonical page compare report digest is stale or corrupt.");
        }

        private static void ValidateAssessment(AssessmentHandoffProjection value)
        {
            Require(value != null, "The compare Assessment handoff projection is required.");
            RequireNoExtensions(value.ExtensionData, "compare Assessment handoff");
            Require(string.Equals(value.SchemaVersion, PublishingPageCompareContract.AssessmentHandoffSchemaVersion, StringComparison.Ordinal)
                && string.Equals(value.ProducerRevisionId, PublishingPageCompareContract.AssessmentProducerRevisionId, StringComparison.Ordinal)
                && string.Equals(value.ConformanceRevisionId, PublishingPageCompareContract.AssessmentConformanceRevisionId, StringComparison.Ordinal),
                "The compare Assessment handoff version binding is unsupported.");
            ValidateDigest(value.HandoffDigestSha256, "Assessment handoff digest");
            ValidatePrefixedDigest(value.ManifestRevisionId, "sha256:", "Assessment manifest revision");
            ValidatePrefixedDigest(value.AllowlistRevisionId, "sha256:", "Assessment allowlist revision");
            ValidatePrefixedDigest(value.CanonicalLocatorHash, "sha256:", "Assessment locator hash");
            ValidatePrefixedDigest(value.ResourceIdentityHash, "sha256:", "Assessment resource identity hash");
            ValidatePrefixedDigest(value.ApprovedHostHash, "hmac-sha256:", "Assessment approved host hash");
            Require(!string.IsNullOrWhiteSpace(value.SelectionId), "The Assessment selection ID is required.");
            Require(new[] { "known_readable", "known_forbidden", "auth_required", "inherited_unknown", "unknown" }
                    .Contains(value.PermissionSignal, StringComparer.Ordinal),
                "The Assessment permission signal is unsupported.");
            Require(string.Equals(value.DiscoveryObservationStatus, PublishingPageCompareContract.UnavailableByProducerContract, StringComparison.Ordinal)
                && string.Equals(value.ProducerAttestationStatus, PublishingPageCompareContract.UnavailableByProducerContract, StringComparison.Ordinal)
                && string.Equals(value.CoverageStatus, PublishingPageCompareContract.UnavailableByProducerContract, StringComparison.Ordinal),
                "The compare report invents unsupported Assessment provenance.");
        }

        private static void ValidateBindings(CompareBindings value)
        {
            Require(value != null, "Canonical compare bindings are required.");
            RequireNoExtensions(value.ExtensionData, "compare bindings");
            ValidateDigest(value.ManifestDigestSha256, "manifest digest");
            ValidateDigest(value.SourceCaptureReceiptDigestSha256, "source capture receipt digest");
            ValidateDigest(value.SnapshotDigestSha256, "snapshot digest");
            ValidateDigest(value.PlanDigestSha256, "plan digest");
            ValidateDigest(value.AdmittedPlanDigestSha256, "admitted plan digest");
            ValidateDigest(value.SourceVersionDigestSha256, "source version digest");
            ValidateDigest(value.ImportReceiptDigestSha256, "import receipt digest");
            Require(string.Equals(value.ExportSchemaVersion, PublishingPagePackageContract.ExportSchemaVersion, StringComparison.Ordinal)
                && string.Equals(value.MigrationPackageSchemaVersion, PublishingPagePackageContract.MigrationSchemaVersion, StringComparison.Ordinal)
                && string.Equals(value.ImportReceiptSchemaVersion, PublishingPagePackageContract.ReceiptSchemaVersion, StringComparison.Ordinal),
                "Canonical v1 cannot carry a foreign page package or native receipt schema.");
            Require(string.Equals(value.IngredientGraphSchemaVersion, "pnp-page-ingredient-graph/v1", StringComparison.Ordinal)
                || string.Equals(value.IngredientGraphSchemaVersion, "pnp-page-ingredient-graph/v2", StringComparison.Ordinal),
                "The canonical ingredient graph schema is unsupported.");
            Require(!string.IsNullOrWhiteSpace(value.IngredientProjectionVersion),
                "The ingredient projection version is required.");
            PnP.Framework.Migration.Verification.AdmittedReproExecutionPlanValidator.ValidateOperations(value.Operations);
            if (string.IsNullOrWhiteSpace(value.RuntimeReceiptSchemaVersion))
            {
                Require(string.IsNullOrWhiteSpace(value.RuntimeReceiptDigestSha256),
                    "A missing runtime receipt cannot carry a runtime digest.");
            }
            else
            {
                Require(string.Equals(value.RuntimeReceiptSchemaVersion, PublishingPageCompareContract.RuntimeReceiptSchemaVersion, StringComparison.Ordinal),
                    "The canonical runtime receipt schema is unsupported.");
                ValidateDigest(value.RuntimeReceiptDigestSha256, "runtime receipt digest");
            }
        }

        private static void ValidateSourceVersion(SourceVersionComparison value)
        {
            Require(value != null, "The compare source-version observation is required.");
            RequireNoExtensions(value.ExtensionData, "source-version observation");
            ValidateDigest(value.IdentityDigestSha256, "source identity digest");
            ValidateDigest(value.ExpectedCompositeDigestSha256, "expected source version digest");
            Require(new[] { "matched", "changed", "not-checked", "unknown" }.Contains(value.Status, StringComparer.Ordinal),
                "The source-version status is unsupported.");
            if (string.Equals(value.Status, "matched", StringComparison.Ordinal)
                || string.Equals(value.Status, "changed", StringComparison.Ordinal))
            {
                ValidateDigest(value.ObservedCompositeDigestSha256, "observed source version digest");
            }
            Require(value.ObservedAtUtc != default, "The source-version observation time is required.");
        }

        private static void ValidateTarget(CompareTargetIdentity value)
        {
            Require(value != null, "The canonical target identity is required.");
            RequireNoExtensions(value.ExtensionData, "target identity");
            ValidateDigest(value.WebUrlHashSha256, "target Web URL hash");
            ValidateDigest(value.PageServerRelativeUrlHashSha256, "target page URL hash");
            Require(value.ListItemId >= 0, "The target list item ID is invalid.");
        }

        private static void ValidateStatus(
            CompareStorageSummary storage,
            CompareRuntimeSummary runtime,
            CompareAcceptance acceptance)
        {
            Require(storage != null && runtime != null && acceptance != null,
                "Canonical storage, runtime, and acceptance summaries are required.");
            RequireNoExtensions(storage.ExtensionData, "storage summary");
            RequireNoExtensions(runtime.ExtensionData, "runtime summary");
            RequireNoExtensions(acceptance.ExtensionData, "acceptance summary");
            Require(StorageStatuses.Contains(storage.Status), "The storage status is unsupported.");
            Require(RuntimeStatuses.Contains(runtime.Status), "The runtime status is unsupported.");
            Require(Verdicts.Contains(acceptance.Verdict), "The compare acceptance verdict is unsupported.");
            Require(StorageStatuses.Contains(acceptance.StorageStatus), "The acceptance storage status is unsupported.");
            Require(RuntimeStatuses.Contains(acceptance.RuntimeStatus), "The acceptance runtime status is unsupported.");
            Require(string.Equals(storage.Status, acceptance.StorageStatus, StringComparison.Ordinal)
                && string.Equals(runtime.Status, acceptance.RuntimeStatus, StringComparison.Ordinal),
                "The acceptance status does not match the canonical summaries.");
            Require(acceptance.ReasonCodes != null
                && acceptance.ReasonCodes.All(value => !string.IsNullOrWhiteSpace(value))
                && acceptance.ReasonCodes.Distinct(StringComparer.Ordinal).Count() == acceptance.ReasonCodes.Count,
                "The acceptance reason codes are missing or duplicated.");
            Require(storage.Status != "passed" || storage.FreshReadback,
                "Storage cannot pass without fresh target readback.");
            if (string.Equals(acceptance.Verdict, "pass", StringComparison.Ordinal))
            {
                Require(string.Equals(storage.Status, "passed", StringComparison.Ordinal)
                    && (string.Equals(runtime.Status, "passed", StringComparison.Ordinal)
                        || string.Equals(runtime.Status, "not-required", StringComparison.Ordinal)),
                    "A canonical pass cannot be storage/runtime false-complete.");
            }
        }

        private static void ValidateIngredients(
            IList<IngredientCompareResult> ingredients,
            CompareStorageSummary storage,
            CompareRuntimeSummary runtime,
            CompareAcceptance acceptance)
        {
            Require(ingredients != null && ingredients.Count > 0 && ingredients.All(value => value != null),
                "Canonical per-page compare requires ingredient rows.");
            var duplicate = ingredients.GroupBy(value => value.IngredientId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            Require(duplicate == null, "Canonical compare ingredient IDs must be unique.");
            var ids = new HashSet<string>(ingredients.Select(value => value.IngredientId), StringComparer.Ordinal);
            foreach (var ingredient in ingredients)
            {
                RequireNoExtensions(ingredient.ExtensionData, "ingredient result");
                Require(!string.IsNullOrWhiteSpace(ingredient.Kind)
                    && ingredient.Lineage != null
                    && ingredient.Expected != null
                    && ingredient.Actual != null,
                    "Every canonical ingredient requires kind, lineage, expected, and actual evidence.");
                RequireNoExtensions(ingredient.Lineage.ExtensionData, "ingredient lineage");
                RequireNoExtensions(ingredient.Expected.ExtensionData, "expected ingredient digest");
                RequireNoExtensions(ingredient.Actual.ExtensionData, "actual ingredient digest");
                ValidateDigest(ingredient.Lineage.RequestedUrlHashSha256, "ingredient requested URL hash");
                ValidateDigest(ingredient.Lineage.SourceArtifactDigestSha256, "ingredient source artifact digest");
                Require(string.Equals(ingredient.Lineage.SourceIngredientId, ingredient.IngredientId, StringComparison.Ordinal),
                    "Canonical source ingredient lineage is foreign.");
                Require(ingredient.Lineage.EvidenceRefs != null
                    && (!ingredient.Material || ingredient.Lineage.EvidenceRefs.Count > 0)
                    && ingredient.Lineage.EvidenceRefs.All(IsDigestBoundReference),
                    "Ingredient evidence references must be digest-bound.");
                Require(ingredient.Lineage.CauseIngredientIds != null
                    && ingredient.Lineage.CauseIngredientIds.All(ids.Contains),
                    "Ingredient cause lineage contains an orphan ID.");
                Require(ResultClasses.Contains(ingredient.ResultClass),
                    "The ingredient result class is unsupported.");
                Require(!string.IsNullOrWhiteSpace(ingredient.ReasonCode),
                    "The ingredient result reason code is required.");
                ValidateOptionalDigest(ingredient.Expected.RawDigestSha256, "expected raw digest");
                ValidateOptionalDigest(ingredient.Expected.CanonicalDigestSha256, "expected canonical digest");
                ValidateOptionalDigest(ingredient.Actual.RawDigestSha256, "actual raw digest");
                ValidateOptionalDigest(ingredient.Actual.CanonicalDigestSha256, "actual canonical digest");

                var successShaped = string.Equals(ingredient.ResultClass, PublishingPageCompareContract.ResultClasses.Exact, StringComparison.Ordinal)
                    || string.Equals(ingredient.ResultClass, PublishingPageCompareContract.ResultClasses.CanonicalEquivalent, StringComparison.Ordinal)
                    || string.Equals(ingredient.ResultClass, PublishingPageCompareContract.ResultClasses.TransformedAsPlanned, StringComparison.Ordinal);
                if (ingredient.Material && successShaped)
                {
                    Require(!string.IsNullOrWhiteSpace(ingredient.Actual.RawDigestSha256)
                        || !string.IsNullOrWhiteSpace(ingredient.Actual.CanonicalDigestSha256),
                        "A successful material ingredient requires actual target evidence.");
                    Require(ingredient.Lineage.EvidenceRefs.Distinct(StringComparer.Ordinal).Count() >= 2,
                        "A successful material ingredient requires distinct source and actual evidence references.");
                }
            }

            if (string.Equals(acceptance.Verdict, "pass", StringComparison.Ordinal))
            {
                Require(ingredients.Where(value => value.Material).All(value =>
                        string.Equals(value.ResultClass, PublishingPageCompareContract.ResultClasses.Exact, StringComparison.Ordinal)
                        || string.Equals(value.ResultClass, PublishingPageCompareContract.ResultClasses.CanonicalEquivalent, StringComparison.Ordinal)
                        || string.Equals(value.ResultClass, PublishingPageCompareContract.ResultClasses.TransformedAsPlanned, StringComparison.Ordinal))
                    && string.Equals(storage.Status, "passed", StringComparison.Ordinal)
                    && !string.Equals(runtime.Status, "pending", StringComparison.Ordinal)
                    && !string.Equals(runtime.Status, "not-run", StringComparison.Ordinal),
                    "A canonical pass contains incomplete material, storage, or runtime evidence.");
            }
        }

        private static void RequireNoExtensions(IDictionary<string, JsonElement> value, string name)
        {
            Require(value == null || value.Count == 0,
                "The " + name + " contains an unknown field.");
        }

        private static void ValidateImplementationRef(string value, string name)
        {
            Require(value != null && value.Length == 40 && value.All(IsHex),
                "The " + name + " must be a full Git SHA.");
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex),
                "The " + name + " must be a SHA-256 digest.");
        }

        private static void ValidateOptionalDigest(string value, string name)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidateDigest(value, name);
            }
        }

        private static void ValidatePrefixedDigest(string value, string prefix, string name)
        {
            Require(value != null
                && value.StartsWith(prefix, StringComparison.Ordinal)
                && value.Length == prefix.Length + 64
                && value.Substring(prefix.Length).All(IsHex),
                "The " + name + " must be a " + prefix.TrimEnd(':') + " digest.");
        }

        private static bool IsDigestBoundReference(string value)
        {
            return value != null
                && value.StartsWith("sha256:", StringComparison.Ordinal)
                && value.Length == 71
                && value.Substring(7).All(IsHex);
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
