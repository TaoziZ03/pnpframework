using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Comparison
{
    /// <summary>
    /// Versioned successor that leaves the shipped v1 terminal and projection
    /// schemas unchanged while admitting mutation-started adverse native evidence.
    /// </summary>
    public static class PageCompareTerminalContractV2
    {
        public const string SchemaVersion = "pnp-page-compare-terminal-envelope/v2";
        public const string ConsumerProjectionSchemaVersion = "pnp-page-compare-consumer-projection/v2";

        public static class TerminalKinds
        {
            public const string NativeExecutionAdverse = "native-execution-adverse";
        }

        public static class Statuses
        {
            public const string Failed = "failed";
            public const string Pending = "pending";
        }

        public static class ReasonCodes
        {
            public const string NativeExecutionFailed = "NATIVE_EXECUTION_FAILED";
        }
    }

    public sealed class PageCompareTerminalEnvelopeV2
    {
        public string SchemaVersion { get; set; } = PageCompareTerminalContractV2.SchemaVersion;

        public DateTimeOffset GeneratedAtUtc { get; set; }

        public IList<PublishingPageCompareReport> CanonicalReports { get; set; } = new List<PublishingPageCompareReport>();

        public IList<PageCompareTerminalCaseV2> Cases { get; set; } = new List<PageCompareTerminalCaseV2>();

        public string EnvelopeDigestSha256 { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareTerminalCaseV2
    {
        public PageCompareTerminalCase Terminal { get; set; }

        public PageCompareCleanupReceiptEvidence CleanupReceipt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    /// <summary>
    /// Lossless carrier for an external cleanup receipt. ReceiptJson is the exact
    /// UTF-8 text whose digest is recorded; it is evidence, not a replacement
    /// cleanup outcome engine or a new native receipt.
    /// </summary>
    public sealed class PageCompareCleanupReceiptEvidence
    {
        public string SchemaVersion { get; set; }

        public Guid OperationId { get; set; }

        public string ArtifactLocator { get; set; }

        public string ReceiptDigestSha256 { get; set; }

        public string ReceiptJson { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareConsumerProjectionV2
    {
        public string SchemaVersion { get; set; } = PageCompareTerminalContractV2.ConsumerProjectionSchemaVersion;

        public string SourceEnvelopeSchemaVersion { get; set; }

        public string SourceEnvelopeDigestSha256 { get; set; }

        public DateTimeOffset GeneratedAtUtc { get; set; }

        public IList<PublishingPageCompareReport> CanonicalReports { get; set; } = new List<PublishingPageCompareReport>();

        public IList<PageCompareConsumerCaseV2> Cases { get; set; } = new List<PageCompareConsumerCaseV2>();

        public string ProjectionDigestSha256 { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareConsumerCaseV2
    {
        public string CaseId { get; set; }

        public string TerminalKind { get; set; }

        public string PageFamily { get; set; }

        public string AcceptanceVerdict { get; set; }

        public IList<IngredientCompareResult> CanonicalMaterialRows { get; set; } = new List<IngredientCompareResult>();

        public PageCompareTerminalCaseV2 Terminal { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public static class PageCompareTerminalEnvelopeFactoryV2
    {
        public static PageCompareTerminalEnvelopeV2 Create(
            DateTimeOffset generatedAtUtc,
            IEnumerable<PublishingPageCompareReport> canonicalReports,
            IEnumerable<PageCompareTerminalCaseV2> cases)
        {
            var envelope = new PageCompareTerminalEnvelopeV2
            {
                GeneratedAtUtc = generatedAtUtc,
                CanonicalReports = (canonicalReports ?? Enumerable.Empty<PublishingPageCompareReport>()).ToList(),
                Cases = (cases ?? Enumerable.Empty<PageCompareTerminalCaseV2>()).ToList()
            };
            envelope.EnvelopeDigestSha256 = ComputeDigest(envelope);
            return envelope;
        }

        public static string ComputeDigest(PageCompareTerminalEnvelopeV2 envelope)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    envelope,
                    nameof(PageCompareTerminalEnvelopeV2.EnvelopeDigestSha256)));
        }
    }

    public static class PageCompareTerminalEnvelopeSerializerV2
    {
        public static string SerializeCanonical(
            PageCompareTerminalEnvelopeV2 envelope,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            PageCompareTerminalEnvelopeValidatorV2.Validate(envelope, expectedImplementationRef, artifactStore);
            return MigrationContractSerializer.SerializeCanonical(envelope);
        }

        public static PageCompareTerminalEnvelopeV2 DeserializeStrict(
            string json,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            var envelope = MigrationContractSerializer.Deserialize<PageCompareTerminalEnvelopeV2>(json);
            if (!PageCompareJsonShape.HasSameJsonShape(json, envelope))
            {
                throw new InvalidDataException("The page compare terminal envelope v2 contains an unknown or omitted field.");
            }
            PageCompareTerminalEnvelopeValidatorV2.Validate(envelope, expectedImplementationRef, artifactStore);
            return envelope;
        }
    }

    public static class PageCompareTerminalEnvelopeValidatorV2
    {
        public static void Validate(
            PageCompareTerminalEnvelopeV2 envelope,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            Require(envelope != null, "A page compare terminal envelope v2 is required.");
            RequireNoExtensions(envelope.ExtensionData, "terminal envelope v2");
            Require(string.Equals(envelope.SchemaVersion, PageCompareTerminalContractV2.SchemaVersion, StringComparison.Ordinal),
                "The page compare terminal envelope v2 schema is unsupported.");
            Require(envelope.GeneratedAtUtc != default, "The terminal envelope v2 generation time is required.");
            ValidateImplementationRef(expectedImplementationRef, "expected implementation ref");
            Require(envelope.CanonicalReports != null && envelope.Cases != null && envelope.Cases.Count > 0,
                "The terminal envelope v2 requires a case ledger.");
            foreach (var report in envelope.CanonicalReports)
            {
                PublishingPageCompareReportValidator.Validate(report, expectedImplementationRef);
            }

            var duplicateCase = envelope.Cases.GroupBy(value => value?.Terminal?.CaseId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            Require(duplicateCase == null && envelope.Cases.All(value => value?.Terminal != null),
                "Terminal v2 case IDs must be present and unique.");
            foreach (var terminalCase in envelope.Cases)
            {
                ValidateCase(terminalCase, envelope.GeneratedAtUtc, expectedImplementationRef, artifactStore);
            }

            ValidateDigest(envelope.EnvelopeDigestSha256, "terminal envelope v2 digest");
            Require(string.Equals(PageCompareTerminalEnvelopeFactoryV2.ComputeDigest(envelope), envelope.EnvelopeDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The terminal envelope v2 digest is stale or corrupt.");
        }

        private static void ValidateCase(
            PageCompareTerminalCaseV2 value,
            DateTimeOffset generatedAtUtc,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            RequireNoExtensions(value.ExtensionData, "terminal case v2");
            var terminal = value.Terminal;
            if (string.Equals(terminal.TerminalKind, PageCompareTerminalContractV2.TerminalKinds.NativeExecutionAdverse, StringComparison.Ordinal))
            {
                ValidateNativeExecutionAdverse(terminal, value.CleanupReceipt, expectedImplementationRef, artifactStore);
            }
            else
            {
                Require(value.CleanupReceipt == null,
                    "A non-adverse terminal case cannot carry adverse cleanup evidence.");
                var v1 = PageCompareTerminalEnvelopeFactory.Create(generatedAtUtc, null, new[] { terminal });
                PageCompareTerminalEnvelopeValidator.Validate(v1, expectedImplementationRef, artifactStore);
            }
            ValidateMaterialResultSemantics(terminal);
        }

        private static void ValidateNativeExecutionAdverse(
            PageCompareTerminalCase terminal,
            PageCompareCleanupReceiptEvidence cleanup,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            PageCompareTerminalEnvelopeValidator.ValidateCommonCase(terminal, expectedImplementationRef);
            Require(string.Equals(terminal.PageFamily, PageCompareTerminalContract.PageFamilies.ClassicWiki, StringComparison.Ordinal),
                "The current native adverse terminal kind requires a classic Wiki case.");
            Require(terminal.AdmittedPlan != null && terminal.Operations != null && terminal.NativeReceipt != null,
                "Native adverse evidence requires admitted plan, operations, and a typed native receipt.");
            Require(terminal.RuntimeReceipt == null && string.IsNullOrWhiteSpace(terminal.RuntimeReceiptDigestSha256),
                "Native adverse evidence cannot invent a runtime receipt.");
            ValidateDigest(terminal.AdmittedPlanDigestSha256, "admitted plan digest");
            var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                terminal.AdmittedPlan,
                terminal.PlanDigestSha256,
                terminal.AdmittedPlan.TargetIdentity);
            Require(string.Equals(admittedDigest, terminal.AdmittedPlanDigestSha256, StringComparison.OrdinalIgnoreCase)
                && AdmittedReproExecutionPlanValidator.SameOperations(terminal.Operations, terminal.AdmittedPlan.Operations)
                && string.Equals(terminal.Source.IdentityDigestSha256, terminal.AdmittedPlan.SourceVersion.IdentityDigestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(terminal.Source.VersionDigestSha256, terminal.AdmittedPlan.SourceVersion.VersionDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The native adverse admitted plan binding is stale or foreign.");
            var native = NativePageImportReceiptAggregateValidator.ValidateForTerminalEvidence(
                terminal.NativeReceipt,
                terminal.AdmittedPlan,
                admittedDigest);
            PageCompareTerminalEnvelopeValidator.ValidateClassicWikiPackageArtifact(terminal, artifactStore);
            Require(native.ExecutionStatus == MigrationExecutionStatus.FailedUnexpectedly
                    || native.ExecutionStatus == MigrationExecutionStatus.PartiallySucceeded,
                "Native adverse evidence requires a failed or partially-succeeded execution status.");
            Require(native.MutationStarted && native.PartialExecution && native.NativeStepCount > 0,
                "Native adverse evidence must preserve mutation-started partial execution.");
            Require(native.StorageVerificationStatus != StorageVerificationStatus.Passed
                && native.RuntimeVerificationStatus != RuntimeVerificationStatus.Passed,
                "Native adverse evidence cannot be storage/runtime success-shaped.");
            Require(string.Equals(terminal.MutationStatus, PageCompareTerminalContract.Statuses.Partial, StringComparison.Ordinal)
                && string.Equals(terminal.ReadbackStatus, PageCompareTerminalContractV2.Statuses.Failed, StringComparison.Ordinal)
                && (string.Equals(terminal.RuntimeStatus, PageCompareTerminalContractV2.Statuses.Pending, StringComparison.Ordinal)
                    || string.Equals(terminal.RuntimeStatus, PageCompareTerminalContract.Statuses.NotExecuted, StringComparison.Ordinal))
                && string.Equals(terminal.ReconcileStatus, PageCompareTerminalContract.Statuses.Partial, StringComparison.Ordinal)
                && string.Equals(terminal.ReasonCode, PageCompareTerminalContractV2.ReasonCodes.NativeExecutionFailed, StringComparison.Ordinal),
                "The native adverse terminal statuses are inconsistent.");
            Require(terminal.Ingredients.Where(item => item.Material).All(item =>
                    !IsSuccessResult(item.Result)
                    && !string.Equals(item.ExecutionStatus, PageCompareTerminalContract.Statuses.Succeeded, StringComparison.Ordinal)),
                "A native adverse case cannot emit success-shaped material equality.");
            Require(terminal.DependentObligations.Any(item => item.Required
                    && !string.Equals(item.Status, PageCompareTerminalContract.ObligationStatuses.Satisfied, StringComparison.Ordinal)),
                "A native adverse case must retain an unsatisfied required obligation.");
            ValidateCleanup(cleanup, terminal.Operations.CleanupOperationId);
        }

        private static void ValidateCleanup(PageCompareCleanupReceiptEvidence cleanup, Guid expectedOperationId)
        {
            Require(cleanup != null, "Native adverse evidence requires cleanup evidence.");
            RequireNoExtensions(cleanup.ExtensionData, "cleanup evidence");
            Require(!string.IsNullOrWhiteSpace(cleanup.SchemaVersion)
                && cleanup.OperationId != Guid.Empty
                && cleanup.OperationId == expectedOperationId
                && IsSafeRelativeLocator(cleanup.ArtifactLocator)
                && !string.IsNullOrWhiteSpace(cleanup.ReceiptJson),
                "The cleanup evidence identity is incomplete or foreign.");
            ValidateDigest(cleanup.ReceiptDigestSha256, "cleanup receipt digest");
            Require(string.Equals(MigrationDigest.ComputeSha256(Encoding.UTF8.GetBytes(cleanup.ReceiptJson)), cleanup.ReceiptDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The cleanup receipt text and digest are inconsistent.");
            using (var document = JsonDocument.Parse(cleanup.ReceiptJson))
            {
                Require(document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("schema", out var schema)
                    && string.Equals(schema.GetString(), cleanup.SchemaVersion, StringComparison.Ordinal)
                    && document.RootElement.TryGetProperty("operationId", out var operation)
                    && Guid.TryParse(operation.GetString(), out var operationId)
                    && operationId == cleanup.OperationId,
                    "The cleanup receipt payload is foreign to its declared schema or operation.");
            }
        }

        private static void ValidateMaterialResultSemantics(PageCompareTerminalCase terminal)
        {
            foreach (var ingredient in terminal.Ingredients.Where(value => value.Material))
            {
                if (string.Equals(ingredient.Result, PublishingPageCompareContract.ResultClasses.Exact, StringComparison.Ordinal))
                {
                    Require(DigestEquals(ingredient.Expected.RawDigestSha256, ingredient.Actual.RawDigestSha256),
                        "An exact material result requires equal expected and actual raw digests.");
                }
                else if (string.Equals(ingredient.Result, PublishingPageCompareContract.ResultClasses.CanonicalEquivalent, StringComparison.Ordinal))
                {
                    Require(DigestEquals(ingredient.Expected.CanonicalDigestSha256, ingredient.Actual.CanonicalDigestSha256),
                        "A canonical-equivalent material result requires equal canonical digests.");
                }
            }
        }

        private static bool IsSuccessResult(string result)
        {
            return string.Equals(result, PublishingPageCompareContract.ResultClasses.Exact, StringComparison.Ordinal)
                || string.Equals(result, PublishingPageCompareContract.ResultClasses.CanonicalEquivalent, StringComparison.Ordinal)
                || string.Equals(result, PublishingPageCompareContract.ResultClasses.TransformedAsPlanned, StringComparison.Ordinal);
        }

        private static bool DigestEquals(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeRelativeLocator(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !Path.IsPathRooted(value)
                && !value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => string.Equals(part, "..", StringComparison.Ordinal));
        }

        private static void RequireNoExtensions(IDictionary<string, JsonElement> value, string name)
        {
            Require(value == null || value.Count == 0, "The " + name + " contains an unknown field.");
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

    public static class PageCompareTerminalConsumerAdapterV2
    {
        public static PageCompareConsumerProjectionV2 Consume(
            PageCompareTerminalEnvelopeV2 envelope,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            PageCompareTerminalEnvelopeValidatorV2.Validate(envelope, expectedImplementationRef, artifactStore);
            var projection = new PageCompareConsumerProjectionV2
            {
                SourceEnvelopeSchemaVersion = envelope.SchemaVersion,
                SourceEnvelopeDigestSha256 = envelope.EnvelopeDigestSha256,
                GeneratedAtUtc = envelope.GeneratedAtUtc,
                CanonicalReports = Clone(envelope.CanonicalReports),
                Cases = envelope.Cases.Select(value => new PageCompareConsumerCaseV2
                {
                    CaseId = value.Terminal.CaseId,
                    TerminalKind = value.Terminal.TerminalKind,
                    PageFamily = value.Terminal.PageFamily,
                    AcceptanceVerdict = Acceptance(value.Terminal),
                    CanonicalMaterialRows = ProjectCanonicalRows(value.Terminal),
                    Terminal = Clone(value)
                }).ToList()
            };
            projection.ProjectionDigestSha256 = ComputeProjectionDigest(projection);
            return projection;
        }

        public static PageCompareTerminalEnvelopeV2 Reconstruct(
            PageCompareConsumerProjectionV2 projection,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            ValidateProjectionShape(projection);
            var envelope = PageCompareTerminalEnvelopeFactoryV2.Create(
                projection.GeneratedAtUtc,
                Clone(projection.CanonicalReports),
                projection.Cases.Select(value => Clone(value.Terminal)));
            Require(string.Equals(envelope.EnvelopeDigestSha256, projection.SourceEnvelopeDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The consumer projection v2 lost or changed terminal information.");
            PageCompareTerminalEnvelopeValidatorV2.Validate(envelope, expectedImplementationRef, artifactStore);

            var expected = Consume(envelope, expectedImplementationRef, artifactStore);
            Require(string.Equals(
                    MigrationContractSerializer.SerializeCanonical(expected),
                    MigrationContractSerializer.SerializeCanonical(projection),
                    StringComparison.Ordinal),
                "The consumer projection v2 public fields do not match the retained terminal payload.");
            return envelope;
        }

        public static string SerializeProjectionCanonical(
            PageCompareConsumerProjectionV2 projection,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            Reconstruct(projection, expectedImplementationRef, artifactStore);
            return MigrationContractSerializer.SerializeCanonical(projection);
        }

        public static PageCompareConsumerProjectionV2 DeserializeProjectionStrict(
            string json,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            var projection = MigrationContractSerializer.Deserialize<PageCompareConsumerProjectionV2>(json);
            if (!PageCompareJsonShape.HasSameJsonShape(json, projection))
            {
                throw new InvalidDataException("The page compare consumer projection v2 contains an unknown or omitted field.");
            }
            Reconstruct(projection, expectedImplementationRef, artifactStore);
            return projection;
        }

        private static void ValidateProjectionShape(PageCompareConsumerProjectionV2 projection)
        {
            Require(projection != null
                && string.Equals(projection.SchemaVersion, PageCompareTerminalContractV2.ConsumerProjectionSchemaVersion, StringComparison.Ordinal)
                && string.Equals(projection.SourceEnvelopeSchemaVersion, PageCompareTerminalContractV2.SchemaVersion, StringComparison.Ordinal)
                && projection.GeneratedAtUtc != default
                && projection.Cases != null
                && projection.CanonicalReports != null,
                "The page compare consumer projection v2 is unsupported or incomplete.");
            Require(projection.ExtensionData == null || projection.ExtensionData.Count == 0,
                "The page compare consumer projection v2 contains an unknown field.");
            Require(projection.Cases.All(value => value != null
                    && value.Terminal?.Terminal != null
                    && (value.ExtensionData == null || value.ExtensionData.Count == 0)),
                "The page compare consumer projection v2 contains an incomplete or extended case.");
            Require(projection.Cases.Select(value => value.CaseId).Distinct(StringComparer.Ordinal).Count() == projection.Cases.Count,
                "The page compare consumer projection v2 contains duplicate case IDs.");
            Require(!string.IsNullOrWhiteSpace(projection.ProjectionDigestSha256)
                && string.Equals(ComputeProjectionDigest(projection), projection.ProjectionDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The page compare consumer projection v2 digest is stale or corrupt.");
        }

        private static string ComputeProjectionDigest(PageCompareConsumerProjectionV2 projection)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    projection,
                    nameof(PageCompareConsumerProjectionV2.ProjectionDigestSha256)));
        }

        private static IList<IngredientCompareResult> ProjectCanonicalRows(PageCompareTerminalCase terminal)
        {
            if (!string.Equals(terminal.TerminalKind, PageCompareTerminalContract.TerminalKinds.WikiNativeExecuted, StringComparison.Ordinal))
            {
                return new List<IngredientCompareResult>();
            }
            return terminal.Ingredients
                .Where(value => value.Material
                    && string.Equals(value.ExecutionStatus, PageCompareTerminalContract.Statuses.Succeeded, StringComparison.Ordinal)
                    && !string.Equals(value.Result, PageCompareTerminalContract.TerminalResults.AccessDenied, StringComparison.Ordinal)
                    && !string.Equals(value.Result, PageCompareTerminalContract.TerminalResults.Unsupported, StringComparison.Ordinal))
                .Select(value => new IngredientCompareResult
                {
                    IngredientId = value.IngredientId,
                    Kind = value.Kind,
                    Material = value.Material,
                    Lineage = Clone(value.Lineage),
                    Expected = Clone(value.Expected),
                    Actual = Clone(value.Actual),
                    ResultClass = value.Result,
                    ReasonCode = value.ReasonCode
                })
                .ToList();
        }

        private static string Acceptance(PageCompareTerminalCase terminal)
        {
            if (string.Equals(terminal.TerminalKind, PageCompareTerminalContract.TerminalKinds.PreWriteDenied, StringComparison.Ordinal))
            {
                return terminal.Ingredients.Any(value => value.Required || value.FidelitySignificant) ? "conditional" : "unverified";
            }
            if (string.Equals(terminal.TerminalKind, PageCompareTerminalContract.TerminalKinds.UnsupportedNoExecution, StringComparison.Ordinal))
            {
                return "unverified";
            }
            if (string.Equals(terminal.TerminalKind, PageCompareTerminalContractV2.TerminalKinds.NativeExecutionAdverse, StringComparison.Ordinal))
            {
                return PublishingPageCompareReconciler.DeriveAcceptance(
                    PageCompareTerminalContractV2.Statuses.Failed,
                    NormalizeRuntimeStatus(terminal.RuntimeStatus),
                    new List<IngredientCompareResult>()).Verdict;
            }
            if (terminal.Ingredients.Any(value => (value.Required || value.FidelitySignificant)
                && string.Equals(value.Availability, PageCompareTerminalContract.Availability.AccessDenied, StringComparison.Ordinal)))
            {
                return "conditional";
            }
            return PublishingPageCompareReconciler.DeriveAcceptance(
                NormalizeStorageStatus(terminal.ReadbackStatus),
                NormalizeRuntimeStatus(terminal.RuntimeStatus),
                ProjectCanonicalRows(terminal)).Verdict;
        }

        private static string NormalizeStorageStatus(string value)
        {
            if (string.Equals(value, PageCompareTerminalContract.Statuses.Passed, StringComparison.Ordinal))
            {
                return "passed";
            }
            if (string.Equals(value, PageCompareTerminalContractV2.Statuses.Failed, StringComparison.Ordinal))
            {
                return "failed";
            }
            return "not-run";
        }

        private static string NormalizeRuntimeStatus(string value)
        {
            if (string.Equals(value, PageCompareTerminalContract.Statuses.Passed, StringComparison.Ordinal))
            {
                return "passed";
            }
            if (string.Equals(value, PageCompareTerminalContractV2.Statuses.Failed, StringComparison.Ordinal))
            {
                return "failed";
            }
            if (string.Equals(value, PageCompareTerminalContractV2.Statuses.Pending, StringComparison.Ordinal))
            {
                return "pending";
            }
            return "not-run";
        }

        private static T Clone<T>(T value)
        {
            if (value == null)
            {
                return default;
            }
            return MigrationContractSerializer.Deserialize<T>(MigrationContractSerializer.SerializeCanonical(value));
        }

        private static IList<T> Clone<T>(IList<T> value)
        {
            return value == null ? new List<T>() : value.Select(Clone).ToList();
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
