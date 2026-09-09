using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Comparison
{
    public static class PageCompareTerminalContract
    {
        public const string SchemaVersion = "pnp-page-compare-terminal-envelope/v1";
        public const string ConsumerProjectionSchemaVersion = "pnp-page-compare-consumer-projection/v1";

        public static class TerminalKinds
        {
            public const string WikiNativeExecuted = "wiki-native-executed";
            public const string PreWriteDenied = "pre-write-denied";
            public const string UnsupportedNoExecution = "unsupported-no-execution";
        }

        public static class PageFamilies
        {
            public const string ClassicWiki = "classic-wiki";
            public const string Publishing = "publishing";
        }

        public static class Statuses
        {
            public const string Succeeded = "succeeded";
            public const string Passed = "passed";
            public const string Partial = "partial";
            public const string NotExecuted = "not-executed";
        }

        public static class Availability
        {
            public const string Captured = "captured";
            public const string AccessDenied = "unavailable.access_denied";
            public const string Unsupported = "unsupported";
        }

        public static class Dispositions
        {
            public const string Preserve = "Preserve";
            public const string Transform = "Transform";
            public const string Drop = "Drop";
            public const string Delegate = "Delegate";
        }

        public static class TerminalResults
        {
            public const string AccessDenied = "access-denied";
            public const string Unsupported = "unsupported";
        }

        public static class ReasonCodes
        {
            public const string NativeExecuted = "NATIVE_EXECUTED";
            public const string AccessDeniedSkipped = "ACCESS_DENIED_SKIPPED";
            public const string CapabilityUnsupported = "CAPABILITY_UNSUPPORTED";
        }

        public static class ObligationStatuses
        {
            public const string Satisfied = "satisfied";
            public const string NotExecuted = "not-executed";
            public const string Unverified = "unverified";
        }
    }

    /// <summary>
    /// Versioned aggregate for canonical v1 reports and terminal cases that cannot
    /// honestly be renamed as pnp-page-compare-report/v1.
    /// </summary>
    public sealed class PageCompareTerminalEnvelope
    {
        public string SchemaVersion { get; set; } = PageCompareTerminalContract.SchemaVersion;

        public DateTimeOffset GeneratedAtUtc { get; set; }

        public IList<PublishingPageCompareReport> CanonicalReports { get; set; } = new List<PublishingPageCompareReport>();

        public IList<PageCompareTerminalCase> Cases { get; set; } = new List<PageCompareTerminalCase>();

        public string EnvelopeDigestSha256 { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareTerminalCase
    {
        public string CaseId { get; set; }

        public string TerminalKind { get; set; }

        public string PageFamily { get; set; }

        public string MigrationPackageSchemaVersion { get; set; }

        public string MigrationPackageArtifactLocator { get; set; }

        public string MigrationPackageArtifactDigestSha256 { get; set; }

        public long MigrationPackageArtifactLength { get; set; }

        public PageCompareSourceIdentity Source { get; set; }

        public string PlanDigestSha256 { get; set; }

        public string ImplementationRef { get; set; }

        public string EvidenceDigestSha256 { get; set; }

        public AdmittedReproExecutionPlan AdmittedPlan { get; set; }

        public string AdmittedPlanDigestSha256 { get; set; }

        public ReproOperationIds Operations { get; set; }

        public NativePageImportReceiptAggregate NativeReceipt { get; set; }

        public RuntimeVerificationManifest RuntimeManifest { get; set; }

        public RuntimeVerificationReceipt RuntimeReceipt { get; set; }

        public string RuntimeReceiptDigestSha256 { get; set; }

        public string MutationStatus { get; set; }

        public string ReadbackStatus { get; set; }

        public string RuntimeStatus { get; set; }

        public string ReconcileStatus { get; set; }

        public string ReasonCode { get; set; }

        public IList<PageCompareTerminalIngredient> Ingredients { get; set; } = new List<PageCompareTerminalIngredient>();

        public IList<PageCompareDependentObligation> DependentObligations { get; set; } = new List<PageCompareDependentObligation>();

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareSourceIdentity
    {
        public string OriginalIdentifier { get; set; }

        public string PageServerRelativeUrl { get; set; }

        public string IdentityDigestSha256 { get; set; }

        public string VersionDigestSha256 { get; set; }

        public string ETag { get; set; }

        public DateTimeOffset? LastModifiedUtc { get; set; }

        public string VersionLabel { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareTerminalIngredient
    {
        public string IngredientId { get; set; }

        public string Kind { get; set; }

        public string Subtype { get; set; }

        public bool Material { get; set; }

        public bool Required { get; set; }

        public bool FidelitySignificant { get; set; }

        public string SourceIdentityDigestSha256 { get; set; }

        public string SourceVersionDigestSha256 { get; set; }

        public string PlanDigestSha256 { get; set; }

        public string ImplementationRef { get; set; }

        public string SourceEvidenceDigestSha256 { get; set; }

        public string ActualEvidenceDigestSha256 { get; set; }

        public string ActionId { get; set; }

        public string Disposition { get; set; }

        public string Result { get; set; }

        public string Availability { get; set; }

        public string ExecutionStatus { get; set; }

        public string ReasonCode { get; set; }

        public int? HttpStatus { get; set; }

        public bool SemanticAccessDenied { get; set; }

        public int AttemptCount { get; set; }

        public IngredientCompareLineage Lineage { get; set; }

        public CompareDigestPair Expected { get; set; } = new CompareDigestPair();

        public CompareDigestPair Actual { get; set; } = new CompareDigestPair();

        public IList<string> DependentObligationIds { get; set; } = new List<string>();

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareDependentObligation
    {
        public string ObligationId { get; set; }

        public string IngredientId { get; set; }

        public bool Required { get; set; }

        public string Status { get; set; }

        public string ReasonCode { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class PageCompareConsumerProjection
    {
        public string SchemaVersion { get; set; } = PageCompareTerminalContract.ConsumerProjectionSchemaVersion;

        public string SourceEnvelopeSchemaVersion { get; set; }

        public string SourceEnvelopeDigestSha256 { get; set; }

        public DateTimeOffset GeneratedAtUtc { get; set; }

        public IList<PublishingPageCompareReport> CanonicalReports { get; set; } = new List<PublishingPageCompareReport>();

        public IList<PageCompareConsumerCase> Cases { get; set; } = new List<PageCompareConsumerCase>();
    }

    public sealed class PageCompareConsumerCase
    {
        public string CaseId { get; set; }

        public string TerminalKind { get; set; }

        public string PageFamily { get; set; }

        public string AcceptanceVerdict { get; set; }

        public IList<IngredientCompareResult> CanonicalMaterialRows { get; set; } = new List<IngredientCompareResult>();

        public PageCompareTerminalCase Terminal { get; set; }
    }

    public static class PageCompareTerminalEnvelopeFactory
    {
        public static PageCompareTerminalEnvelope Create(
            DateTimeOffset generatedAtUtc,
            IEnumerable<PublishingPageCompareReport> canonicalReports,
            IEnumerable<PageCompareTerminalCase> cases)
        {
            var envelope = new PageCompareTerminalEnvelope
            {
                GeneratedAtUtc = generatedAtUtc,
                CanonicalReports = (canonicalReports ?? Enumerable.Empty<PublishingPageCompareReport>()).ToList(),
                Cases = (cases ?? Enumerable.Empty<PageCompareTerminalCase>()).ToList()
            };
            envelope.EnvelopeDigestSha256 = ComputeDigest(envelope);
            return envelope;
        }

        public static string ComputeDigest(PageCompareTerminalEnvelope envelope)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    envelope,
                    nameof(PageCompareTerminalEnvelope.EnvelopeDigestSha256)));
        }
    }

    public static class PageCompareTerminalEnvelopeSerializer
    {
        public static string SerializeCanonical(
            PageCompareTerminalEnvelope envelope,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            PageCompareTerminalEnvelopeValidator.Validate(envelope, expectedImplementationRef, artifactStore);
            return MigrationContractSerializer.SerializeCanonical(envelope);
        }

        public static PageCompareTerminalEnvelope DeserializeStrict(
            string json,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            var envelope = MigrationContractSerializer.Deserialize<PageCompareTerminalEnvelope>(json);
            if (!PageCompareJsonShape.HasSameJsonShape(json, envelope))
            {
                throw new InvalidDataException(
                    "The page compare terminal envelope contains an unknown or omitted field.");
            }
            PageCompareTerminalEnvelopeValidator.Validate(envelope, expectedImplementationRef, artifactStore);
            return envelope;
        }
    }

    public static class PageCompareTerminalEnvelopeValidator
    {
        private static readonly HashSet<string> CanonicalResults = new HashSet<string>(new[]
        {
            PublishingPageCompareContract.ResultClasses.Exact,
            PublishingPageCompareContract.ResultClasses.CanonicalEquivalent,
            PublishingPageCompareContract.ResultClasses.TransformedAsPlanned,
            PublishingPageCompareContract.ResultClasses.Missing,
            PublishingPageCompareContract.ResultClasses.UnexpectedExtra,
            PublishingPageCompareContract.ResultClasses.Mismatch,
            PublishingPageCompareContract.ResultClasses.Deferred,
            PublishingPageCompareContract.ResultClasses.AuthorizationBlocked,
            PublishingPageCompareContract.ResultClasses.RuntimePending,
            PublishingPageCompareContract.ResultClasses.AuthExpired,
            PublishingPageCompareContract.ResultClasses.Unknown,
            PublishingPageCompareContract.ResultClasses.NotApplicable,
            PublishingPageCompareContract.ResultClasses.SourceVersionChanged
        }, StringComparer.Ordinal);

        public static void Validate(
            PageCompareTerminalEnvelope envelope,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            Require(envelope != null, "A page compare terminal envelope is required.");
            RequireNoExtensions(envelope.ExtensionData, "terminal envelope");
            Require(string.Equals(envelope.SchemaVersion, PageCompareTerminalContract.SchemaVersion, StringComparison.Ordinal),
                "The page compare terminal envelope schema is unsupported.");
            Require(envelope.GeneratedAtUtc != default, "The terminal envelope generation time is required.");
            ValidateImplementationRef(expectedImplementationRef, "expected implementation ref");
            Require(envelope.CanonicalReports != null && envelope.Cases != null && envelope.Cases.Count > 0,
                "The terminal envelope requires a case ledger.");

            foreach (var report in envelope.CanonicalReports)
            {
                PublishingPageCompareReportValidator.Validate(report, expectedImplementationRef);
            }

            var duplicateCase = envelope.Cases.GroupBy(value => value?.CaseId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            Require(duplicateCase == null && envelope.Cases.All(value => value != null),
                "Terminal case IDs must be present and unique.");
            foreach (var terminalCase in envelope.Cases)
            {
                ValidateCase(terminalCase, expectedImplementationRef, artifactStore);
            }

            ValidateDigest(envelope.EnvelopeDigestSha256, "terminal envelope digest");
            Require(string.Equals(
                    PageCompareTerminalEnvelopeFactory.ComputeDigest(envelope),
                    envelope.EnvelopeDigestSha256,
                    StringComparison.OrdinalIgnoreCase),
                "The terminal envelope digest is stale or corrupt.");
        }

        private static void ValidateCase(
            PageCompareTerminalCase terminalCase,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            ValidateCommonCase(terminalCase, expectedImplementationRef);

            if (string.Equals(terminalCase.TerminalKind, PageCompareTerminalContract.TerminalKinds.WikiNativeExecuted, StringComparison.Ordinal))
            {
                ValidateExecutedWiki(terminalCase, artifactStore);
            }
            else if (string.Equals(terminalCase.TerminalKind, PageCompareTerminalContract.TerminalKinds.PreWriteDenied, StringComparison.Ordinal))
            {
                ValidatePreWriteDenied(terminalCase);
            }
            else if (string.Equals(terminalCase.TerminalKind, PageCompareTerminalContract.TerminalKinds.UnsupportedNoExecution, StringComparison.Ordinal))
            {
                ValidateUnsupported(terminalCase);
            }
            else
            {
                throw new InvalidDataException("The terminal case kind is unsupported.");
            }
        }

        internal static void ValidateCommonCase(
            PageCompareTerminalCase terminalCase,
            string expectedImplementationRef)
        {
            RequireNoExtensions(terminalCase.ExtensionData, "terminal case");
            Require(terminalCase.Source != null, "The terminal source identity is required.");
            RequireNoExtensions(terminalCase.Source.ExtensionData, "terminal source identity");
            Require(!string.IsNullOrWhiteSpace(terminalCase.Source.OriginalIdentifier)
                && !string.IsNullOrWhiteSpace(terminalCase.Source.PageServerRelativeUrl),
                "The exact source identifier and page path are required.");
            ValidateDigest(terminalCase.Source.IdentityDigestSha256, "source identity digest");
            ValidateDigest(terminalCase.Source.VersionDigestSha256, "source version digest");
            Require(terminalCase.Source.ObservedAtUtc != default
                && (!string.IsNullOrWhiteSpace(terminalCase.Source.ETag)
                    || terminalCase.Source.LastModifiedUtc.HasValue
                    || !string.IsNullOrWhiteSpace(terminalCase.Source.VersionLabel)),
                "The terminal source version is incomplete.");
            ValidateDigest(terminalCase.PlanDigestSha256, "terminal plan digest");
            ValidateDigest(terminalCase.EvidenceDigestSha256, "terminal evidence digest");
            ValidateImplementationRef(terminalCase.ImplementationRef, "terminal implementation ref");
            Require(string.Equals(terminalCase.ImplementationRef, expectedImplementationRef, StringComparison.OrdinalIgnoreCase),
                "The terminal case implementation ref is foreign.");
            ValidatePackageSchema(terminalCase);
            ValidateIngredientLedger(terminalCase);
        }

        private static void ValidatePackageSchema(PageCompareTerminalCase terminalCase)
        {
            if (string.Equals(terminalCase.PageFamily, PageCompareTerminalContract.PageFamilies.ClassicWiki, StringComparison.Ordinal))
            {
                Require(string.Equals(
                        terminalCase.MigrationPackageSchemaVersion,
                        ClassicWikiPackageContract.MigrationSchemaVersion,
                        StringComparison.Ordinal),
                    "The classic Wiki package schema binding is not exact.");
            }
            else if (string.Equals(terminalCase.PageFamily, PageCompareTerminalContract.PageFamilies.Publishing, StringComparison.Ordinal))
            {
                Require(string.Equals(
                        terminalCase.MigrationPackageSchemaVersion,
                        PublishingPagePackageContract.MigrationSchemaVersion,
                        StringComparison.Ordinal),
                    "The Publishing package schema binding is not exact.");
            }
            else
            {
                throw new InvalidDataException("The terminal page family is unsupported.");
            }
        }

        private static void ValidateIngredientLedger(PageCompareTerminalCase terminalCase)
        {
            Require(terminalCase.Ingredients != null && terminalCase.Ingredients.Count > 0
                && terminalCase.Ingredients.All(value => value != null),
                "Every terminal case requires ingredient evidence.");
            var duplicate = terminalCase.Ingredients.GroupBy(value => value.IngredientId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            Require(duplicate == null, "Terminal ingredient IDs must be present and unique.");
            var ids = new HashSet<string>(terminalCase.Ingredients.Select(value => value.IngredientId), StringComparer.Ordinal);
            var obligationIds = new HashSet<string>(StringComparer.Ordinal);
            Require(terminalCase.DependentObligations != null, "The dependent obligation ledger is required.");
            foreach (var obligation in terminalCase.DependentObligations)
            {
                Require(obligation != null, "A dependent obligation entry is required.");
                RequireNoExtensions(obligation.ExtensionData, "dependent obligation");
                Require(!string.IsNullOrWhiteSpace(obligation.ObligationId)
                    && obligationIds.Add(obligation.ObligationId)
                    && ids.Contains(obligation.IngredientId),
                    "Dependent obligations must be unique and reference a known ingredient.");
                Require(new[]
                    {
                        PageCompareTerminalContract.ObligationStatuses.Satisfied,
                        PageCompareTerminalContract.ObligationStatuses.NotExecuted,
                        PageCompareTerminalContract.ObligationStatuses.Unverified
                    }.Contains(obligation.Status, StringComparer.Ordinal),
                    "The dependent obligation status is unsupported.");
                Require(!string.IsNullOrWhiteSpace(obligation.ReasonCode),
                    "The dependent obligation reason is required.");
            }

            foreach (var ingredient in terminalCase.Ingredients)
            {
                RequireNoExtensions(ingredient.ExtensionData, "terminal ingredient");
                Require(!string.IsNullOrWhiteSpace(ingredient.Kind)
                    && !string.IsNullOrWhiteSpace(ingredient.Subtype)
                    && ingredient.Lineage != null
                    && ingredient.Expected != null
                    && ingredient.Actual != null,
                    "The terminal ingredient shape is incomplete.");
                RequireNoExtensions(ingredient.Lineage.ExtensionData, "terminal ingredient lineage");
                RequireNoExtensions(ingredient.Expected.ExtensionData, "terminal expected digest");
                RequireNoExtensions(ingredient.Actual.ExtensionData, "terminal actual digest");
                Require(string.Equals(ingredient.SourceIdentityDigestSha256, terminalCase.Source.IdentityDigestSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ingredient.SourceVersionDigestSha256, terminalCase.Source.VersionDigestSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ingredient.PlanDigestSha256, terminalCase.PlanDigestSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ingredient.ImplementationRef, terminalCase.ImplementationRef, StringComparison.OrdinalIgnoreCase),
                    "The terminal ingredient source, plan, or implementation binding is foreign.");
                ValidateDigest(ingredient.SourceEvidenceDigestSha256, "ingredient source evidence digest");
                ValidateOptionalDigest(ingredient.ActualEvidenceDigestSha256, "ingredient actual evidence digest");
                ValidateDigest(ingredient.Lineage.RequestedUrlHashSha256, "ingredient requested URL hash");
                ValidateDigest(ingredient.Lineage.SourceArtifactDigestSha256, "ingredient source artifact digest");
                Require(string.Equals(ingredient.Lineage.SourceIngredientId, ingredient.IngredientId, StringComparison.Ordinal),
                    "The terminal ingredient source lineage is foreign.");
                Require(ingredient.Lineage.EvidenceRefs != null
                    && ingredient.Lineage.EvidenceRefs.All(IsDigestBoundReference)
                    && ingredient.Lineage.CauseIngredientIds != null
                    && ingredient.Lineage.CauseIngredientIds.All(ids.Contains),
                    "Terminal ingredient evidence contains a non-digest reference or orphan cause.");
                ValidateOptionalDigest(ingredient.Expected.RawDigestSha256, "expected raw ingredient digest");
                ValidateOptionalDigest(ingredient.Expected.CanonicalDigestSha256, "expected canonical ingredient digest");
                ValidateOptionalDigest(ingredient.Actual.RawDigestSha256, "actual raw ingredient digest");
                ValidateOptionalDigest(ingredient.Actual.CanonicalDigestSha256, "actual canonical ingredient digest");
                Require(ingredient.DependentObligationIds != null
                    && ingredient.DependentObligationIds.All(obligationIds.Contains),
                    "The terminal ingredient references an unknown dependent obligation.");
            }
        }

        private static void ValidateExecutedWiki(
            PageCompareTerminalCase terminalCase,
            IMigrationArtifactStore artifactStore)
        {
            Require(string.Equals(terminalCase.PageFamily, PageCompareTerminalContract.PageFamilies.ClassicWiki, StringComparison.Ordinal),
                "The Wiki native terminal kind requires a classic Wiki case.");
            Require(terminalCase.AdmittedPlan != null
                && terminalCase.Operations != null
                && terminalCase.NativeReceipt != null
                && terminalCase.RuntimeManifest != null
                && terminalCase.RuntimeReceipt != null,
                "Wiki native execution requires admitted plan, operations, native receipt, and runtime evidence.");
            ValidateDigest(terminalCase.AdmittedPlanDigestSha256, "admitted plan digest");
            ValidateDigest(terminalCase.RuntimeReceiptDigestSha256, "runtime receipt digest");
            var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                terminalCase.AdmittedPlan,
                terminalCase.PlanDigestSha256,
                terminalCase.AdmittedPlan.TargetIdentity);
            Require(string.Equals(admittedDigest, terminalCase.AdmittedPlanDigestSha256, StringComparison.OrdinalIgnoreCase)
                && AdmittedReproExecutionPlanValidator.SameOperations(terminalCase.Operations, terminalCase.AdmittedPlan.Operations)
                && string.Equals(terminalCase.Source.IdentityDigestSha256, terminalCase.AdmittedPlan.SourceVersion.IdentityDigestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(terminalCase.Source.VersionDigestSha256, terminalCase.AdmittedPlan.SourceVersion.VersionDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The Wiki native admitted plan binding is stale or foreign.");
            var native = NativePageImportReceiptAggregateValidator.ValidateForCompare(
                terminalCase.NativeReceipt,
                terminalCase.AdmittedPlan,
                admittedDigest);
            ValidateClassicWikiPackageArtifact(terminalCase, artifactStore);
            Require(string.Equals(native.PageFamily, NativePageImportReceiptContract.ClassicWikiFamily, StringComparison.Ordinal)
                && native.ExecutionStatus == Execution.MigrationExecutionStatus.Succeeded
                && !native.PartialExecution
                && native.MutationStarted
                && native.NativeStepCount > 0
                && native.FreshReadbackPassed
                && native.StorageVerificationStatus == StorageVerificationStatus.Passed,
                "The Wiki native receipt did not prove successful mutation and fresh readback.");
            Require(string.Equals(terminalCase.RuntimeReceipt.PlanDigest, terminalCase.PlanDigestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(terminalCase.RuntimeReceipt.AdmittedPlanDigestSha256, admittedDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(terminalCase.RuntimeReceipt.ImportReceiptDigestSha256, native.ReceiptDigestSha256, StringComparison.OrdinalIgnoreCase)
                && terminalCase.RuntimeReceipt.OperationId == terminalCase.Operations.RuntimeOperationId
                && AdmittedReproExecutionPlanValidator.SameOperations(terminalCase.RuntimeReceipt.Operations, terminalCase.Operations)
                && AdmittedReproExecutionPlanValidator.SameSourceVersion(terminalCase.RuntimeReceipt.SourceVersion, terminalCase.AdmittedPlan.SourceVersion)
                && string.Equals(terminalCase.RuntimeReceipt.TargetIdentity, terminalCase.AdmittedPlan.TargetIdentity, StringComparison.Ordinal)
                && string.Equals(terminalCase.RuntimeReceipt.ImplementationRef, terminalCase.ImplementationRef, StringComparison.OrdinalIgnoreCase),
                "The Wiki runtime receipt is stale or foreign.");
            Require(string.Equals(
                    MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(terminalCase.RuntimeReceipt)),
                    terminalCase.RuntimeReceiptDigestSha256,
                    StringComparison.OrdinalIgnoreCase),
                "The Wiki runtime receipt digest is stale or corrupt.");
            RuntimeVerificationReceiptValidator.ValidateEvidence(
                terminalCase.RuntimeReceipt,
                terminalCase.RuntimeManifest,
                terminalCase.ImplementationRef,
                artifactStore);
            Require(terminalCase.RuntimeReceipt.Status == RuntimeVerificationStatus.Passed
                && string.Equals(terminalCase.MutationStatus, PageCompareTerminalContract.Statuses.Succeeded, StringComparison.Ordinal)
                && string.Equals(terminalCase.ReadbackStatus, PageCompareTerminalContract.Statuses.Passed, StringComparison.Ordinal)
                && string.Equals(terminalCase.RuntimeStatus, PageCompareTerminalContract.Statuses.Passed, StringComparison.Ordinal)
                && (string.Equals(terminalCase.ReconcileStatus, PageCompareTerminalContract.Statuses.Passed, StringComparison.Ordinal)
                    || string.Equals(terminalCase.ReconcileStatus, PageCompareTerminalContract.Statuses.Partial, StringComparison.Ordinal))
                && string.Equals(terminalCase.ReasonCode, PageCompareTerminalContract.ReasonCodes.NativeExecuted, StringComparison.Ordinal),
                "The Wiki native terminal statuses are incomplete.");

            foreach (var ingredient in terminalCase.Ingredients)
            {
                if (IsDeniedIngredient(ingredient))
                {
                    ValidateDeniedIngredient(ingredient);
                    continue;
                }
                Require(string.Equals(ingredient.Availability, PageCompareTerminalContract.Availability.Captured, StringComparison.Ordinal)
                    && string.Equals(ingredient.ExecutionStatus, PageCompareTerminalContract.Statuses.Succeeded, StringComparison.Ordinal)
                    && (string.Equals(ingredient.Disposition, PageCompareTerminalContract.Dispositions.Preserve, StringComparison.Ordinal)
                        || string.Equals(ingredient.Disposition, PageCompareTerminalContract.Dispositions.Transform, StringComparison.Ordinal))
                    && !string.IsNullOrWhiteSpace(ingredient.ActionId)
                    && CanonicalResults.Contains(ingredient.Result)
                    && !string.IsNullOrWhiteSpace(ingredient.ActualEvidenceDigestSha256)
                    && !string.Equals(ingredient.SourceEvidenceDigestSha256, ingredient.ActualEvidenceDigestSha256, StringComparison.OrdinalIgnoreCase)
                    && (!string.IsNullOrWhiteSpace(ingredient.Actual.RawDigestSha256)
                        || !string.IsNullOrWhiteSpace(ingredient.Actual.CanonicalDigestSha256))
                    && ingredient.Lineage.EvidenceRefs.Distinct(StringComparer.Ordinal).Count() >= 2,
                    "A Wiki executed ingredient lacks distinct actual target evidence.");
            }
            var hasRequiredDenial = terminalCase.Ingredients.Any(value =>
                IsDeniedIngredient(value) && (value.Required || value.FidelitySignificant));
            Require(hasRequiredDenial
                    ? string.Equals(terminalCase.ReconcileStatus, PageCompareTerminalContract.Statuses.Partial, StringComparison.Ordinal)
                    : string.Equals(terminalCase.ReconcileStatus, PageCompareTerminalContract.Statuses.Passed, StringComparison.Ordinal),
                "The Wiki reconcile status does not preserve required denied ingredients.");
            Require(terminalCase.DependentObligations.Where(value => value.Required).All(value =>
                {
                    var ingredient = terminalCase.Ingredients.Single(item =>
                        string.Equals(item.IngredientId, value.IngredientId, StringComparison.Ordinal));
                    return IsDeniedIngredient(ingredient)
                        ? !string.Equals(value.Status, PageCompareTerminalContract.ObligationStatuses.Satisfied, StringComparison.Ordinal)
                        : string.Equals(value.Status, PageCompareTerminalContract.ObligationStatuses.Satisfied, StringComparison.Ordinal);
                }),
                "The Wiki dependent obligation state is inconsistent.");
        }

        private static void ValidatePreWriteDenied(PageCompareTerminalCase terminalCase)
        {
            ValidateNoExecutionShape(terminalCase, PageCompareTerminalContract.ReasonCodes.AccessDeniedSkipped);
            Require(terminalCase.Ingredients.Any(IsDeniedIngredient),
                "A pre-write denied case requires an access-denied ingredient.");
            foreach (var ingredient in terminalCase.Ingredients)
            {
                ValidateDeniedIngredient(ingredient);
            }
            Require(terminalCase.DependentObligations.All(value =>
                    !string.Equals(value.Status, PageCompareTerminalContract.ObligationStatuses.Satisfied, StringComparison.Ordinal)),
                "A denied branch cannot satisfy dependent obligations.");
        }

        private static void ValidateUnsupported(PageCompareTerminalCase terminalCase)
        {
            ValidateNoExecutionShape(terminalCase, PageCompareTerminalContract.ReasonCodes.CapabilityUnsupported);
            foreach (var ingredient in terminalCase.Ingredients)
            {
                Require(string.Equals(ingredient.Availability, PageCompareTerminalContract.Availability.Unsupported, StringComparison.Ordinal)
                    && string.Equals(ingredient.ExecutionStatus, PageCompareTerminalContract.Statuses.NotExecuted, StringComparison.Ordinal)
                    && string.Equals(ingredient.Result, PageCompareTerminalContract.TerminalResults.Unsupported, StringComparison.Ordinal)
                    && string.Equals(ingredient.ReasonCode, PageCompareTerminalContract.ReasonCodes.CapabilityUnsupported, StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(ingredient.ActualEvidenceDigestSha256)
                    && string.IsNullOrWhiteSpace(ingredient.Actual.RawDigestSha256)
                    && string.IsNullOrWhiteSpace(ingredient.Actual.CanonicalDigestSha256)
                    && string.IsNullOrWhiteSpace(ingredient.Lineage.TargetIdentity)
                    && !ingredient.SemanticAccessDenied
                    && !ingredient.HttpStatus.HasValue
                    && ingredient.AttemptCount == 0,
                    "An unsupported ingredient cannot carry execution or target evidence.");
            }
            Require(terminalCase.DependentObligations.All(value =>
                    !string.Equals(value.Status, PageCompareTerminalContract.ObligationStatuses.Satisfied, StringComparison.Ordinal)),
                "An unsupported branch cannot satisfy dependent obligations.");
        }

        private static void ValidateNoExecutionShape(PageCompareTerminalCase terminalCase, string reasonCode)
        {
            Require(terminalCase.AdmittedPlan == null
                && terminalCase.Operations == null
                && terminalCase.NativeReceipt == null
                && terminalCase.RuntimeManifest == null
                && terminalCase.RuntimeReceipt == null
                && string.IsNullOrWhiteSpace(terminalCase.AdmittedPlanDigestSha256)
                && string.IsNullOrWhiteSpace(terminalCase.RuntimeReceiptDigestSha256)
                && string.IsNullOrWhiteSpace(terminalCase.MigrationPackageArtifactLocator)
                && string.IsNullOrWhiteSpace(terminalCase.MigrationPackageArtifactDigestSha256)
                && terminalCase.MigrationPackageArtifactLength == 0,
                "A no-execution terminal case cannot carry receipts or operation IDs.");
            Require(string.Equals(terminalCase.MutationStatus, PageCompareTerminalContract.Statuses.NotExecuted, StringComparison.Ordinal)
                && string.Equals(terminalCase.ReadbackStatus, PageCompareTerminalContract.Statuses.NotExecuted, StringComparison.Ordinal)
                && string.Equals(terminalCase.RuntimeStatus, PageCompareTerminalContract.Statuses.NotExecuted, StringComparison.Ordinal)
                && string.Equals(terminalCase.ReconcileStatus, PageCompareTerminalContract.Statuses.NotExecuted, StringComparison.Ordinal)
                && string.Equals(terminalCase.ReasonCode, reasonCode, StringComparison.Ordinal),
                "The no-execution terminal statuses are inconsistent.");
        }

        private static void ValidateDeniedIngredient(PageCompareTerminalIngredient ingredient)
        {
            Require(string.Equals(ingredient.Availability, PageCompareTerminalContract.Availability.AccessDenied, StringComparison.Ordinal)
                && string.Equals(ingredient.ExecutionStatus, PageCompareTerminalContract.Statuses.NotExecuted, StringComparison.Ordinal)
                && string.Equals(ingredient.Result, PageCompareTerminalContract.TerminalResults.AccessDenied, StringComparison.Ordinal)
                && string.Equals(ingredient.ReasonCode, PageCompareTerminalContract.ReasonCodes.AccessDeniedSkipped, StringComparison.Ordinal)
                && (string.Equals(ingredient.Disposition, PageCompareTerminalContract.Dispositions.Drop, StringComparison.Ordinal)
                    || string.Equals(ingredient.Disposition, PageCompareTerminalContract.Dispositions.Delegate, StringComparison.Ordinal))
                && ingredient.AttemptCount > 0
                && ingredient.AttemptCount <= 3
                && (ingredient.HttpStatus == 401 || ingredient.HttpStatus == 403 || ingredient.SemanticAccessDenied)
                && string.IsNullOrWhiteSpace(ingredient.ActualEvidenceDigestSha256)
                && string.IsNullOrWhiteSpace(ingredient.Actual.RawDigestSha256)
                && string.IsNullOrWhiteSpace(ingredient.Actual.CanonicalDigestSha256)
                && string.IsNullOrWhiteSpace(ingredient.Lineage.TargetIdentity),
                "The access-denied ingredient is incomplete or success-shaped.");
        }

        private static bool IsDeniedIngredient(PageCompareTerminalIngredient ingredient)
        {
            return string.Equals(ingredient.Availability, PageCompareTerminalContract.Availability.AccessDenied, StringComparison.Ordinal)
                || string.Equals(ingredient.ReasonCode, PageCompareTerminalContract.ReasonCodes.AccessDeniedSkipped, StringComparison.Ordinal);
        }

        internal static void ValidateClassicWikiPackageArtifact(
            PageCompareTerminalCase terminalCase,
            IMigrationArtifactStore artifactStore)
        {
            Require(IsSafeRelativeLocator(terminalCase.MigrationPackageArtifactLocator),
                "The Classic Wiki package locator is missing or unsafe.");
            ValidateDigest(terminalCase.MigrationPackageArtifactDigestSha256, "Classic Wiki package artifact digest");
            Require(terminalCase.MigrationPackageArtifactLength > 0 && artifactStore != null,
                "The Classic Wiki package artifact resolver and positive byte length are required.");
            Require(artifactStore.Contains(terminalCase.MigrationPackageArtifactDigestSha256),
                "The Classic Wiki package artifact is missing.");
            byte[] bytes;
            using (var input = artifactStore.OpenRead(terminalCase.MigrationPackageArtifactDigestSha256))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                bytes = copy.ToArray();
            }
            Require(bytes.LongLength == terminalCase.MigrationPackageArtifactLength
                && string.Equals(
                    MigrationDigest.ComputeSha256(bytes),
                    terminalCase.MigrationPackageArtifactDigestSha256,
                    StringComparison.OrdinalIgnoreCase),
                "The Classic Wiki package path, digest, and bytes are inconsistent.");
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var package = MigrationContractSerializer.Deserialize<ClassicWikiMigrationPackage>(json);
            Require(PageCompareJsonShape.HasSameJsonShape(json, package),
                "The Classic Wiki package contains an unknown or omitted field.");
            ClassicWikiPackageValidator.ValidateMigration(package, artifactStore);
            Require(string.Equals(package.SchemaVersion, ClassicWikiPackageContract.MigrationSchemaVersion, StringComparison.Ordinal)
                && string.Equals(package.SchemaVersion, terminalCase.MigrationPackageSchemaVersion, StringComparison.Ordinal)
                && string.Equals(package.PlanDigest, terminalCase.PlanDigestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(MigrationContractSerializer.SerializeCanonical(package), json, StringComparison.Ordinal),
                "The Classic Wiki package artifact is foreign, non-canonical, or bound to another plan.");
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

        private static void ValidateOptionalDigest(string value, string name)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidateDigest(value, name);
            }
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

    public static class PageCompareTerminalConsumerAdapter
    {
        public static PageCompareConsumerProjection Consume(
            PageCompareTerminalEnvelope envelope,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            PageCompareTerminalEnvelopeValidator.Validate(envelope, expectedImplementationRef, artifactStore);
            return new PageCompareConsumerProjection
            {
                SourceEnvelopeSchemaVersion = envelope.SchemaVersion,
                SourceEnvelopeDigestSha256 = envelope.EnvelopeDigestSha256,
                GeneratedAtUtc = envelope.GeneratedAtUtc,
                CanonicalReports = Clone(envelope.CanonicalReports),
                Cases = envelope.Cases.Select(value => new PageCompareConsumerCase
                {
                    CaseId = value.CaseId,
                    TerminalKind = value.TerminalKind,
                    PageFamily = value.PageFamily,
                    AcceptanceVerdict = Acceptance(value),
                    CanonicalMaterialRows = ProjectCanonicalRows(value),
                    Terminal = Clone(value)
                }).ToList()
            };
        }

        public static PageCompareTerminalEnvelope Reconstruct(
            PageCompareConsumerProjection projection,
            string expectedImplementationRef,
            IMigrationArtifactStore artifactStore)
        {
            if (projection == null
                || !string.Equals(projection.SchemaVersion, PageCompareTerminalContract.ConsumerProjectionSchemaVersion, StringComparison.Ordinal)
                || !string.Equals(projection.SourceEnvelopeSchemaVersion, PageCompareTerminalContract.SchemaVersion, StringComparison.Ordinal)
                || projection.Cases == null
                || projection.CanonicalReports == null)
            {
                throw new InvalidDataException("The page compare consumer projection is unsupported or incomplete.");
            }
            var envelope = PageCompareTerminalEnvelopeFactory.Create(
                projection.GeneratedAtUtc,
                Clone(projection.CanonicalReports),
                projection.Cases.Select(value => Clone(value.Terminal)));
            Require(string.Equals(envelope.EnvelopeDigestSha256, projection.SourceEnvelopeDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The consumer projection lost or changed terminal information.");
            PageCompareTerminalEnvelopeValidator.Validate(envelope, expectedImplementationRef, artifactStore);
            return envelope;
        }

        private static IList<IngredientCompareResult> ProjectCanonicalRows(PageCompareTerminalCase terminalCase)
        {
            if (!string.Equals(terminalCase.TerminalKind, PageCompareTerminalContract.TerminalKinds.WikiNativeExecuted, StringComparison.Ordinal))
            {
                return new List<IngredientCompareResult>();
            }
            return terminalCase.Ingredients
                .Where(value => value.Material
                    && string.Equals(value.ExecutionStatus, PageCompareTerminalContract.Statuses.Succeeded, StringComparison.Ordinal)
                    && PageCompareTerminalEnvelopeValidatorResultIsCanonical(value.Result))
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

        private static bool PageCompareTerminalEnvelopeValidatorResultIsCanonical(string value)
        {
            return !string.Equals(value, PageCompareTerminalContract.TerminalResults.AccessDenied, StringComparison.Ordinal)
                && !string.Equals(value, PageCompareTerminalContract.TerminalResults.Unsupported, StringComparison.Ordinal);
        }

        private static string Acceptance(PageCompareTerminalCase terminalCase)
        {
            if (string.Equals(terminalCase.TerminalKind, PageCompareTerminalContract.TerminalKinds.PreWriteDenied, StringComparison.Ordinal))
            {
                return terminalCase.Ingredients.Any(value => value.Required || value.FidelitySignificant)
                    ? "conditional"
                    : "unverified";
            }
            if (string.Equals(terminalCase.TerminalKind, PageCompareTerminalContract.TerminalKinds.UnsupportedNoExecution, StringComparison.Ordinal))
            {
                return "unverified";
            }
            if (string.Equals(terminalCase.ReconcileStatus, PageCompareTerminalContract.Statuses.Partial, StringComparison.Ordinal))
            {
                return "conditional";
            }
            return terminalCase.Ingredients.Any(value => value.Material
                && (string.Equals(value.Result, PublishingPageCompareContract.ResultClasses.Missing, StringComparison.Ordinal)
                    || string.Equals(value.Result, PublishingPageCompareContract.ResultClasses.UnexpectedExtra, StringComparison.Ordinal)
                    || string.Equals(value.Result, PublishingPageCompareContract.ResultClasses.Mismatch, StringComparison.Ordinal)))
                ? "fail"
                : "pass";
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
            return value == null
                ? new List<T>()
                : value.Select(Clone).ToList();
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
