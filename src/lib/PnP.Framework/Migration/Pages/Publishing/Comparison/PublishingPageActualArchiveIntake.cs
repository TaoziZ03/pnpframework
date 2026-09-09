using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.Publishing.Comparison
{
    /// <summary>
    /// Admits the page-centric capture projection used by the first live thin slice
    /// without treating that transport projection as a native migration package.
    /// The returned evidence is intended to populate the existing compare request.
    /// </summary>
    public static class PublishingPageActualArchiveIntake
    {
        public const string BundleSchemaVersion = "ccd.page-centric-canonical-bundle/v1";
        public const string CanonicalizationVersion = "recursive-key-sort-json/1";
        public const string ThinSliceReceiptSchemaVersion = "ccd35.live-thin-slice-final-receipt/v1";
        public const string ReconciliationProjectionSchemaVersion = "pnp-page-compare-actual-archive-intake/v1";

        public static ActualArchiveCaptureProjection AdmitCanonicalBundle(
            byte[] rawBundle,
            ActualArchiveCaptureBindings bindings)
        {
            if (rawBundle == null || rawBundle.Length == 0)
            {
                throw new InvalidDataException("The actual capture bundle is empty.");
            }
            if (bindings == null)
            {
                throw new InvalidDataException("Actual capture bindings are required.");
            }

            ValidateDigest(bindings.RawBundleDigestSha256, "raw bundle digest");
            ValidateDigest(bindings.RawLocatorDigestSha256, "raw locator digest");
            ValidateDigest(bindings.TransportLocatorDigestSha256, "transport locator digest");
            ValidateDigest(bindings.ExpectedCaptureReportDigestSha256, "capture report digest");
            Require(bindings.RawBundleLength >= 0, "The raw bundle length is invalid.");
            Require(bindings.RawBundleLength == rawBundle.LongLength,
                "The raw bundle length does not match its binding.");
            Require(DigestEquals(bindings.RawBundleDigestSha256, MigrationDigest.ComputeSha256(rawBundle)),
                "The raw bundle digest does not match its bytes.");

            using (var document = JsonDocument.Parse(rawBundle))
            {
                var root = document.RootElement;
                Require(root.ValueKind == JsonValueKind.Object, "The actual capture bundle must be a JSON object.");
                ValidateNoDuplicateProperties(root);

                Require(String(root, "schema") == BundleSchemaVersion,
                    "The actual capture bundle schema is unsupported.");
                var caseId = RequiredString(root, "caseId", "case ID");
                Require(string.IsNullOrWhiteSpace(bindings.ExpectedCaseId)
                    || string.Equals(caseId, bindings.ExpectedCaseId, StringComparison.Ordinal),
                    "The actual capture bundle case ID does not match its binding.");

                var integrity = RequiredObject(root, "integrity");
                Require(String(integrity, "algorithm") == "sha256",
                    "The actual capture bundle integrity algorithm is unsupported.");
                Require(String(integrity, "canonicalization") == CanonicalizationVersion,
                    "The actual capture bundle canonicalization is unsupported.");
                var declaredCanonicalLength = RequiredInt64(integrity, "byteLength");
                var declaredCanonicalDigest = RequiredString(integrity, "digest", "canonical digest");
                ValidateDigest(declaredCanonicalDigest, "canonical digest");

                var canonicalBytes = CanonicalizeWithoutRootIntegrity(root);
                Require(declaredCanonicalLength == canonicalBytes.LongLength,
                    "The canonical bundle length does not match the declared integrity.");
                Require(DigestEquals(declaredCanonicalDigest, MigrationDigest.ComputeSha256(canonicalBytes)),
                    "The canonical bundle digest does not match the declared integrity.");

                var source = RequiredObject(root, "source");
                var transportLocatorDigest = RequiredString(source, "urlHash", "transport locator digest");
                ValidateDigest(transportLocatorDigest, "transport locator digest");
                Require(DigestEquals(bindings.TransportLocatorDigestSha256, transportLocatorDigest),
                    "The transport locator digest does not match the bundle.");
                var captureReportDigest = RequiredString(source, "captureReportSha256", "capture report digest");
                ValidateDigest(captureReportDigest, "capture report digest");
                Require(DigestEquals(bindings.ExpectedCaptureReportDigestSha256, captureReportDigest),
                    "The capture report digest does not match its binding.");

                var graph = RequiredObject(root, "ingredientGraph");
                var nodes = RequiredArray(graph, "nodes").EnumerateArray().ToList();
                Require(nodes.Count > 0, "The actual capture bundle contains no ingredient nodes.");
                Require(nodes.All(value => value.ValueKind == JsonValueKind.Object),
                    "Every actual capture ingredient node must be an object.");
                var duplicateNode = nodes.GroupBy(value => RequiredString(value, "id", "ingredient ID"), StringComparer.Ordinal)
                    .FirstOrDefault(group => group.Count() != 1);
                Require(duplicateNode == null, "The actual capture bundle contains duplicate ingredient IDs.");

                var ingredients = nodes
                    .Select(ProjectIngredient)
                    .OrderBy(value => value.IngredientId, StringComparer.Ordinal)
                    .ToList();

                return new ActualArchiveCaptureProjection
                {
                    SchemaVersion = BundleSchemaVersion,
                    CaseId = caseId,
                    Family = RequiredString(root, "family", "page family"),
                    SourceManifestRevision = RequiredString(source, "manifestRevision", "source manifest revision"),
                    CaptureReportDigestSha256 = captureReportDigest,
                    CaptureBindingRevision = RequiredString(source, "captureBindingRevision", "capture binding revision"),
                    RawBundleDigestSha256 = bindings.RawBundleDigestSha256.ToLowerInvariant(),
                    RawBundleLength = bindings.RawBundleLength,
                    CanonicalDigestSha256 = declaredCanonicalDigest.ToLowerInvariant(),
                    CanonicalLength = declaredCanonicalLength,
                    RawLocatorDigestSha256 = bindings.RawLocatorDigestSha256.ToLowerInvariant(),
                    TransportLocatorDigestSha256 = transportLocatorDigest.ToLowerInvariant(),
                    IngredientGraphSchemaVersion = RequiredString(graph, "schemaVersion", "ingredient graph schema"),
                    IngredientProjectionVersion = RequiredString(graph, "projectionVersion", "ingredient projection version"),
                    DiscoveryObservationStatus = PublishingPageCompareContract.UnavailableByProducerContract,
                    ProducerAttestationStatus = PublishingPageCompareContract.UnavailableByProducerContract,
                    CoverageStatus = PublishingPageCompareContract.UnavailableByProducerContract,
                    Ingredients = ingredients
                };
            }
        }

        public static ActualArchiveReconciliationProjection AdmitThinSliceReceipt(
            byte[] rawReceipt,
            ActualArchiveReceiptBindings bindings,
            IEnumerable<ActualArchiveCaptureProjection> captures,
            IEnumerable<ActualArchiveNativeReceiptSet> nativeReceipts)
        {
            if (rawReceipt == null || rawReceipt.Length == 0)
            {
                throw new InvalidDataException("The actual thin-slice receipt is empty.");
            }
            if (bindings == null)
            {
                throw new InvalidDataException("Actual thin-slice receipt bindings are required.");
            }

            ValidateDigest(bindings.RawReceiptDigestSha256, "raw thin-slice receipt digest");
            ValidateDigest(bindings.SourceArchiveDigestSha256, "source archive digest");
            Require(bindings.RawReceiptLength == rawReceipt.LongLength,
                "The raw thin-slice receipt length does not match its binding.");
            Require(DigestEquals(bindings.RawReceiptDigestSha256, MigrationDigest.ComputeSha256(rawReceipt)),
                "The raw thin-slice receipt digest does not match its bytes.");
            Require(bindings.SourceArchiveLength >= 0, "The source archive length is invalid.");
            Require(Uri.TryCreate(bindings.ExpectedTargetOrigin, UriKind.Absolute, out var targetOrigin)
                && targetOrigin.Scheme == Uri.UriSchemeHttps
                && targetOrigin.AbsolutePath == "/",
                "The expected target origin is invalid.");

            var capturesByCase = (captures ?? Enumerable.Empty<ActualArchiveCaptureProjection>())
                .ToDictionary(value => value.CaseId, StringComparer.Ordinal);
            Require(capturesByCase.Count > 0, "At least one admitted capture projection is required.");
            var nativeReceiptsByCase = (nativeReceipts ?? Enumerable.Empty<ActualArchiveNativeReceiptSet>())
                .ToDictionary(value => value.CaseId, StringComparer.Ordinal);
            Require(nativeReceiptsByCase.Count == capturesByCase.Count
                && capturesByCase.Keys.All(nativeReceiptsByCase.ContainsKey),
                "Every actual page case requires exactly one native import/runtime receipt set.");

            using (var document = JsonDocument.Parse(rawReceipt))
            {
                var root = document.RootElement;
                Require(root.ValueKind == JsonValueKind.Object, "The actual thin-slice receipt must be a JSON object.");
                ValidateNoDuplicateProperties(root);
                Require(String(root, "schema") == ThinSliceReceiptSchemaVersion,
                    "The actual thin-slice receipt schema is unsupported.");

                var sourceEvidence = RequiredObject(root, "sourceEvidence");
                ValidateArchiveBinding(RequiredObject(sourceEvidence, "captureArchive"), bindings);
                var selectedInput = RequiredObject(root, "selectedInput");
                ValidateArchiveBinding(RequiredObject(selectedInput, "sourceArchive"), bindings);
                Require(string.Equals(String(selectedInput, "targetOrigin"), bindings.ExpectedTargetOrigin.TrimEnd('/'), StringComparison.Ordinal),
                    "The selected target origin does not match its binding.");

                var boundaries = RequiredObject(root, "boundaries");
                Require(string.Equals(String(boundaries, "targetOrigin"), bindings.ExpectedTargetOrigin.TrimEnd('/'), StringComparison.Ordinal),
                    "The boundary target origin does not match its binding.");
                Require(RequiredInt64(boundaries, "sourceMutationCount") == 0,
                    "The actual receipt reports source mutation.");
                Require(RequiredInt64(boundaries, "allowlistEscapeCount") == 0,
                    "The actual receipt reports an allowlist escape.");

                var apply = RequiredObject(root, "apply");
                var outcomes = RequiredObject(root, "independentOutcomes");
                var storage = RequiredObject(outcomes, "storage");
                var listItem = RequiredObject(outcomes, "listItem");
                var runtime = RequiredObject(outcomes, "runtime");
                Require(String(apply, "verdict") == "pass"
                    && String(storage, "verdict") == "pass"
                    && String(listItem, "verdict") == "pass"
                    && String(runtime, "verdict") == "pass",
                    "The actual receipt does not contain successful apply, storage, list-item, and runtime outcomes.");

                var selectedCases = RequiredArray(selectedInput, "cases");
                var applyCases = RequiredArray(apply, "cases");
                var storageCases = RequiredArray(storage, "cases");
                var listCases = RequiredArray(listItem, "cases");
                var runtimeCases = RequiredArray(runtime, "cases");
                var assessments = RequiredArray(outcomes, "ingredientAssessment");

                var results = new List<ActualArchiveCaseReconciliation>();
                foreach (var capture in capturesByCase.Values.OrderBy(value => value.CaseId, StringComparer.Ordinal))
                {
                    var selected = FindCase(selectedCases, capture.CaseId, "selected input");
                    var applied = FindCase(applyCases, capture.CaseId, "apply");
                    var stored = FindCase(storageCases, capture.CaseId, "storage");
                    var listed = FindCase(listCases, capture.CaseId, "list item");
                    var rendered = FindCase(runtimeCases, capture.CaseId, "runtime");
                    var assessment = FindCase(assessments, capture.CaseId, "ingredient assessment");
                    var nativeReceipt = ValidateNativeReceipts(
                        nativeReceiptsByCase[capture.CaseId],
                        bindings.ExpectedTargetOrigin.TrimEnd('/')
                            + RequiredString(applied, "targetFilePath", "target file path"));

                    var source = RequiredObject(selected, "source");
                    var pageArtifact = capture.Ingredients.Single(value => value.IngredientId == "node:page-artifact");
                    var content = capture.Ingredients.Single(value => value.Kind == "Content");
                    Require(DigestEquals(RequiredString(source, "fileSha256", "source file digest"), pageArtifact.EvidenceDigestSha256),
                        "The selected source page artifact is not bound to the admitted capture.");
                    Require(DigestEquals(RequiredString(source, "contentSha256", "source content digest"), content.EvidenceDigestSha256),
                        "The selected source content is not bound to the admitted capture.");

                    Require(RequiredInt64(applied, "createStatus") == 200
                        && RequiredInt64(applied, "updateStatus") == 200,
                        "The actual apply receipt is incomplete.");
                    Require(DigestEquals(RequiredString(applied, "sourceFileSha256", "applied source file digest"), pageArtifact.EvidenceDigestSha256)
                        && DigestEquals(RequiredString(applied, "sourceContentSha256", "applied source content digest"), content.EvidenceDigestSha256),
                        "The actual apply receipt is not bound to the admitted capture.");

                    Require(RequiredBoolean(stored, "exact")
                        && RequiredInt64(stored, "status") == 200
                        && DigestEquals(RequiredString(stored, "expectedSha256", "expected storage digest"), pageArtifact.EvidenceDigestSha256)
                        && DigestEquals(RequiredString(stored, "actualSha256", "actual storage digest"), pageArtifact.EvidenceDigestSha256),
                        "The fresh storage readback is not exact.");
                    Require(RequiredBoolean(listed, "exact")
                        && RequiredInt64(listed, "status") == 200
                        && DigestEquals(RequiredString(listed, "expectedContentSha256", "expected list content digest"), content.EvidenceDigestSha256)
                        && DigestEquals(RequiredString(listed, "actualContentSha256", "actual list content digest"), content.EvidenceDigestSha256),
                        "The fresh list-item readback is not exact.");

                    var runtimeEvidenceDigest = RequiredString(rendered, "htmlSha256", "runtime evidence digest");
                    ValidateDigest(runtimeEvidenceDigest, "runtime evidence digest");
                    Require(RequiredInt64(rendered, "status") == 200
                        && !RequiredBoolean(rendered, "containsAccessDenied")
                        && !RequiredBoolean(rendered, "containsBaselineSecurityMessage")
                        && !RequiredBoolean(rendered, "containsServerError"),
                        "The runtime evidence is not an admitted successful response.");

                    var verdict = RequiredString(assessment, "verdict", "ingredient verdict");
                    Require(verdict == "pass" || verdict == "conditional",
                        "The actual ingredient verdict is unsupported.");
                    if (verdict == "pass")
                    {
                        Require(RequiredBoolean(rendered, "containsExpectedProbe"),
                            "A passing actual ingredient result must contain its runtime probe.");
                    }

                    results.Add(new ActualArchiveCaseReconciliation
                    {
                        CaseId = capture.CaseId,
                        Family = capture.Family,
                        TargetIdentity = bindings.ExpectedTargetOrigin.TrimEnd('/')
                            + RequiredString(applied, "targetFilePath", "target file path"),
                        StorageStatus = "passed",
                        RuntimeStatus = "passed",
                        IngredientVerdict = verdict,
                        Reason = RequiredString(assessment, "reason", "ingredient verdict reason"),
                        SourcePageArtifactDigestSha256 = pageArtifact.EvidenceDigestSha256,
                        SourceContentDigestSha256 = content.EvidenceDigestSha256,
                        RuntimeEvidenceDigestSha256 = runtimeEvidenceDigest,
                        ImportReceiptDigestSha256 = nativeReceipt.ImportReceiptDigestSha256,
                        RuntimeReceiptDigestSha256 = nativeReceipt.RuntimeReceiptDigestSha256
                    });
                }

                Require(selectedCases.GetArrayLength() == results.Count
                    && applyCases.GetArrayLength() == results.Count
                    && storageCases.GetArrayLength() == results.Count
                    && listCases.GetArrayLength() == results.Count
                    && runtimeCases.GetArrayLength() == results.Count
                    && assessments.GetArrayLength() == results.Count,
                    "The actual receipt contains unbound or missing page cases.");

                var expectedVerdict = results.Any(value => value.IngredientVerdict == "conditional") ? "conditional" : "pass";
                Require(string.Equals(String(root, "verdict"), expectedVerdict, StringComparison.Ordinal),
                    "The actual receipt verdict does not agree with its page results.");
                ValidateCleanup(root, results.Count);

                return new ActualArchiveReconciliationProjection
                {
                    SchemaVersion = ReconciliationProjectionSchemaVersion,
                    ReceiptDigestSha256 = bindings.RawReceiptDigestSha256.ToLowerInvariant(),
                    SourceArchiveDigestSha256 = bindings.SourceArchiveDigestSha256.ToLowerInvariant(),
                    TargetOrigin = bindings.ExpectedTargetOrigin.TrimEnd('/'),
                    Verdict = expectedVerdict,
                    NativeImportReceiptStatus = "native_deserialized",
                    NativeRuntimeReceiptStatus = "native_deserialized",
                    Cases = results
                };
            }
        }

        private static ActualArchiveNativeReceiptProjection ValidateNativeReceipts(
            ActualArchiveNativeReceiptSet nativeReceipts,
            string expectedTargetIdentity)
        {
            Require(nativeReceipts != null
                && nativeReceipts.ImportReceiptJson != null
                && nativeReceipts.RuntimeReceiptJson != null,
                "The actual native receipt set is incomplete.");
            ValidateDigest(nativeReceipts.AdmittedPlanDigestSha256, "native admitted plan digest");
            ValidateDigest(nativeReceipts.ImportReceiptDigestSha256, "native import receipt digest");
            ValidateDigest(nativeReceipts.RuntimeReceiptDigestSha256, "native runtime receipt digest");

            var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                nativeReceipts.AdmittedPlan,
                nativeReceipts.AdmittedPlan?.PlanDigest,
                expectedTargetIdentity);
            Require(DigestEquals(admittedDigest, nativeReceipts.AdmittedPlanDigestSha256),
                "The native receipt set is not bound to its admitted plan.");

            PublishingPageImportReceipt importReceipt;
            RuntimeVerificationReceipt runtimeReceipt;
            try
            {
                using (var importStream = new MemoryStream(nativeReceipts.ImportReceiptJson, writable: false))
                using (var runtimeStream = new MemoryStream(nativeReceipts.RuntimeReceiptJson, writable: false))
                {
                    importReceipt = MigrationContractSerializer.Deserialize<PublishingPageImportReceipt>(importStream);
                    runtimeReceipt = MigrationContractSerializer.Deserialize<RuntimeVerificationReceipt>(runtimeStream);
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is JsonException
                || exception is NotSupportedException)
            {
                throw new InvalidDataException("The native receipt set cannot be independently deserialized.", exception);
            }

            Require(DigestEquals(nativeReceipts.ImportReceiptDigestSha256,
                    MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(importReceipt)))
                && DigestEquals(nativeReceipts.RuntimeReceiptDigestSha256,
                    MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(runtimeReceipt))),
                "A native receipt payload is corrupt or does not match its digest.");
            Require(string.Equals(importReceipt.SchemaVersion, PublishingPagePackageContract.ReceiptSchemaVersion, StringComparison.Ordinal)
                && importReceipt.ExecutionStatus == MigrationExecutionStatus.Succeeded
                && !importReceipt.PartialExecution
                && importReceipt.FreshReadbackPassed
                && importReceipt.StorageVerificationStatus == StorageVerificationStatus.Passed,
                "The native import receipt is partial or unsuccessful.");
            Require(string.Equals(runtimeReceipt.SchemaVersion, PublishingPageCompareContract.RuntimeReceiptSchemaVersion, StringComparison.Ordinal)
                && runtimeReceipt.Status == RuntimeVerificationStatus.Passed,
                "The native runtime receipt is partial or unsuccessful.");
            Require(string.Equals(importReceipt.AdmittedPlanDigestSha256, admittedDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(runtimeReceipt.AdmittedPlanDigestSha256, admittedDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(importReceipt.ApprovedPlanDigest, nativeReceipts.AdmittedPlan.PlanDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(runtimeReceipt.PlanDigest, nativeReceipts.AdmittedPlan.PlanDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(runtimeReceipt.ImportReceiptDigestSha256, nativeReceipts.ImportReceiptDigestSha256, StringComparison.OrdinalIgnoreCase)
                && importReceipt.OperationId == nativeReceipts.AdmittedPlan.Operations.MutationOperationId
                && runtimeReceipt.OperationId == nativeReceipts.AdmittedPlan.Operations.RuntimeOperationId
                && AdmittedReproExecutionPlanValidator.SameOperations(importReceipt.Operations, nativeReceipts.AdmittedPlan.Operations)
                && AdmittedReproExecutionPlanValidator.SameOperations(runtimeReceipt.Operations, nativeReceipts.AdmittedPlan.Operations)
                && AdmittedReproExecutionPlanValidator.SameSourceVersion(importReceipt.SourceVersion, nativeReceipts.AdmittedPlan.SourceVersion)
                && AdmittedReproExecutionPlanValidator.SameSourceVersion(runtimeReceipt.SourceVersion, nativeReceipts.AdmittedPlan.SourceVersion),
                "A native receipt is missing, foreign, or stale for the admitted plan.");
            Require(string.Equals(CanonicalTargetIdentity(importReceipt.TargetWebUrl, importReceipt.TargetPageServerRelativeUrl), expectedTargetIdentity, StringComparison.Ordinal)
                && string.Equals(runtimeReceipt.TargetIdentity, expectedTargetIdentity, StringComparison.Ordinal),
                "A native receipt is not bound to the actual target identity.");

            return new ActualArchiveNativeReceiptProjection
            {
                ImportReceiptDigestSha256 = nativeReceipts.ImportReceiptDigestSha256.ToLowerInvariant(),
                RuntimeReceiptDigestSha256 = nativeReceipts.RuntimeReceiptDigestSha256.ToLowerInvariant()
            };
        }

        private static string CanonicalTargetIdentity(string webUrl, string pageServerRelativeUrl)
        {
            Require(Uri.TryCreate(webUrl, UriKind.Absolute, out var web),
                "The native import receipt target Web URL is invalid.");
            return web.GetLeftPart(UriPartial.Authority).TrimEnd('/')
                + "/"
                + (pageServerRelativeUrl ?? string.Empty).TrimStart('/');
        }

        private static ActualArchiveIngredientEvidence ProjectIngredient(JsonElement node)
        {
            var evidenceDigest = String(node, "evidenceDigest");
            if (!string.IsNullOrWhiteSpace(evidenceDigest))
            {
                ValidateDigest(evidenceDigest, "ingredient evidence digest");
            }

            var references = RequiredArray(node, "evidenceReferences")
                .EnumerateArray()
                .Select(value =>
                {
                    Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
                        "Ingredient evidence references must be non-empty strings.");
                    return value.GetString();
                })
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

            return new ActualArchiveIngredientEvidence
            {
                IngredientId = RequiredString(node, "id", "ingredient ID"),
                Kind = RequiredString(node, "kind", "ingredient kind"),
                EvidenceDigestSha256 = evidenceDigest?.ToLowerInvariant(),
                RuntimeRequirementId = String(node, "runtimeRequirement"),
                EvidenceReferences = references
            };
        }

        private static void ValidateArchiveBinding(JsonElement archive, ActualArchiveReceiptBindings bindings)
        {
            Require(DigestEquals(RequiredString(archive, "sha256", "source archive digest"), bindings.SourceArchiveDigestSha256)
                && RequiredInt64(archive, "bytes") == bindings.SourceArchiveLength,
                "The source archive binding is inconsistent.");
        }

        private static JsonElement FindCase(JsonElement array, string caseId, string label)
        {
            var matches = array.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.Object
                    && string.Equals(String(value, "caseId"), caseId, StringComparison.Ordinal))
                .ToList();
            Require(matches.Count == 1, "The actual " + label + " evidence must contain case '" + caseId + "' exactly once.");
            return matches[0];
        }

        private static void ValidateCleanup(JsonElement root, int caseCount)
        {
            var cleanup = RequiredObject(root, "cleanup");
            var pages = RequiredObject(cleanup, "pages");
            var structures = RequiredObject(cleanup, "structures");
            Require(String(pages, "verdict") == "pass" && String(structures, "verdict") == "pass",
                "The actual receipt cleanup is incomplete.");
            var cleanupCases = RequiredArray(pages, "cases");
            Require(cleanupCases.GetArrayLength() == caseCount
                && cleanupCases.EnumerateArray().All(value =>
                    RequiredInt64(value, "deleteStatus") == 200
                    && RequiredInt64(value, "postDeleteStatus") == 404),
                "The actual receipt page cleanup is incomplete.");
        }

        private static byte[] CanonicalizeWithoutRootIntegrity(JsonElement root)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    Indented = false
                }))
                {
                    WriteCanonical(root, writer, true);
                    writer.Flush();
                }
                return stream.ToArray();
            }
        }

        private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer, bool omitRootIntegrity)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject()
                        .Where(value => !omitRootIntegrity || !string.Equals(value.Name, "integrity", StringComparison.Ordinal))
                        .OrderBy(value => value.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        WriteCanonical(property.Value, writer, false);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteCanonical(item, writer, false);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static void ValidateNoDuplicateProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    Require(names.Add(property.Name), "The actual capture bundle contains duplicate JSON properties.");
                    ValidateNoDuplicateProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    ValidateNoDuplicateProperties(item);
                }
            }
        }

        private static JsonElement RequiredObject(JsonElement parent, string name)
        {
            Require(parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object,
                "The actual capture bundle is missing object '" + name + "'.");
            return value;
        }

        private static JsonElement RequiredArray(JsonElement parent, string name)
        {
            Require(parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array,
                "The actual capture bundle is missing array '" + name + "'.");
            return value;
        }

        private static string RequiredString(JsonElement parent, string name, string label)
        {
            var value = String(parent, name);
            Require(!string.IsNullOrWhiteSpace(value), "The actual capture bundle " + label + " is missing.");
            return value;
        }

        private static string String(JsonElement parent, string name)
        {
            return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static long RequiredInt64(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt64(out var result)
                || result < 0)
            {
                throw new InvalidDataException(
                    "The actual capture bundle integer '" + name + "' is invalid.");
            }
            return result;
        }

        private static bool RequiredBoolean(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value)
                || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            {
                throw new InvalidDataException(
                    "The actual capture boolean '" + name + "' is invalid.");
            }
            return value.GetBoolean();
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

    public sealed class ActualArchiveCaptureBindings
    {
        public string ExpectedCaseId { get; set; }
        public string RawBundleDigestSha256 { get; set; }
        public long RawBundleLength { get; set; }
        public string RawLocatorDigestSha256 { get; set; }
        public string TransportLocatorDigestSha256 { get; set; }
        public string ExpectedCaptureReportDigestSha256 { get; set; }
    }

    public sealed class ActualArchiveCaptureProjection
    {
        public string SchemaVersion { get; set; }
        public string CaseId { get; set; }
        public string Family { get; set; }
        public string SourceManifestRevision { get; set; }
        public string CaptureReportDigestSha256 { get; set; }
        public string CaptureBindingRevision { get; set; }
        public string RawBundleDigestSha256 { get; set; }
        public long RawBundleLength { get; set; }
        public string CanonicalDigestSha256 { get; set; }
        public long CanonicalLength { get; set; }
        public string RawLocatorDigestSha256 { get; set; }
        public string TransportLocatorDigestSha256 { get; set; }
        public string IngredientGraphSchemaVersion { get; set; }
        public string IngredientProjectionVersion { get; set; }
        public string DiscoveryObservationStatus { get; set; }
        public string ProducerAttestationStatus { get; set; }
        public string CoverageStatus { get; set; }
        public IList<ActualArchiveIngredientEvidence> Ingredients { get; set; } = new List<ActualArchiveIngredientEvidence>();
    }

    public sealed class ActualArchiveIngredientEvidence
    {
        public string IngredientId { get; set; }
        public string Kind { get; set; }
        public string EvidenceDigestSha256 { get; set; }
        public string RuntimeRequirementId { get; set; }
        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    public sealed class ActualArchiveReceiptBindings
    {
        public string RawReceiptDigestSha256 { get; set; }
        public long RawReceiptLength { get; set; }
        public string SourceArchiveDigestSha256 { get; set; }
        public long SourceArchiveLength { get; set; }
        public string ExpectedTargetOrigin { get; set; }
    }

    public sealed class ActualArchiveReconciliationProjection
    {
        public string SchemaVersion { get; set; }
        public string ReceiptDigestSha256 { get; set; }
        public string SourceArchiveDigestSha256 { get; set; }
        public string TargetOrigin { get; set; }
        public string Verdict { get; set; }
        public string NativeImportReceiptStatus { get; set; }
        public string NativeRuntimeReceiptStatus { get; set; }
        public IList<ActualArchiveCaseReconciliation> Cases { get; set; } = new List<ActualArchiveCaseReconciliation>();
    }

    public sealed class ActualArchiveCaseReconciliation
    {
        public string CaseId { get; set; }
        public string Family { get; set; }
        public string TargetIdentity { get; set; }
        public string StorageStatus { get; set; }
        public string RuntimeStatus { get; set; }
        public string IngredientVerdict { get; set; }
        public string Reason { get; set; }
        public string SourcePageArtifactDigestSha256 { get; set; }
        public string SourceContentDigestSha256 { get; set; }
        public string RuntimeEvidenceDigestSha256 { get; set; }
        public string ImportReceiptDigestSha256 { get; set; }
        public string RuntimeReceiptDigestSha256 { get; set; }
    }

    public sealed class ActualArchiveNativeReceiptSet
    {
        public string CaseId { get; set; }
        public AdmittedReproExecutionPlan AdmittedPlan { get; set; }
        public string AdmittedPlanDigestSha256 { get; set; }
        public byte[] ImportReceiptJson { get; set; }
        public string ImportReceiptDigestSha256 { get; set; }
        public byte[] RuntimeReceiptJson { get; set; }
        public string RuntimeReceiptDigestSha256 { get; set; }
    }

    internal sealed class ActualArchiveNativeReceiptProjection
    {
        public string ImportReceiptDigestSha256 { get; set; }
        public string RuntimeReceiptDigestSha256 { get; set; }
    }
}
