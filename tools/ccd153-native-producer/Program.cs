using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using IOFile = System.IO.File;

if (args.Length != 2 || (args[0] != "import" && args[0] != "reconcile" && args[0] != "validate-import"))
{
    throw new ArgumentException(
        "usage: ccd153-native-producer <import|reconcile|validate-import> <request.json>");
}

var options = CreateJsonOptions(writeIndented: false);
var indented = CreateJsonOptions(writeIndented: true);
if (args[0] == "import")
{
    RunImport(Path.GetFullPath(args[1]), options, indented);
}
else if (args[0] == "reconcile")
{
    RunReconcile(Path.GetFullPath(args[1]), options, indented);
}
else
{
    RunValidateImport(Path.GetFullPath(args[1]), options, indented);
}

static void RunValidateImport(string requestPath, JsonSerializerOptions options, JsonSerializerOptions indented)
{
    var request = Read<NativeImportValidationRequest>(requestPath, options);
    Require(request.Schema == "ccd153.native-import-validation-request/v1", "native_import_validation_request_schema_unsupported");
    ValidateImplementationRef(request.ImplementationRef);
    var admittedPlan = Read<AdmittedReproExecutionPlan>(Resolve(requestPath, request.AdmittedPlanPath), options);
    var importReceipt = Read<PublishingPageImportReceipt>(Resolve(requestPath, request.ImportReceiptPath), options);
    var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
        admittedPlan,
        admittedPlan.PlanDigest,
        admittedPlan.TargetIdentity);
    string verdict;
    string reason = null;
    try
    {
        PublishingPageImportReceiptValidator.ValidateAdmittedExecution(
            importReceipt,
            admittedPlan,
            admittedDigest);
        verdict = "admitted";
    }
    catch (InvalidDataException exception)
    {
        verdict = "rejected";
        reason = exception.Message;
        Environment.ExitCode = 2;
    }
    var result = new
    {
        schema = "ccd153.native-import-validation-result/v1",
        request.CaseId,
        producer = new { id = ProducerContract.Id, version = ProducerContract.Version, implementationRef = request.ImplementationRef },
        binarySha256 = CurrentBinarySha256(),
        admittedPlanDigestSha256 = admittedDigest,
        importReceiptDigestSha256 = ContractDigest(importReceipt, options),
        stepCount = importReceipt.Steps?.Count ?? 0,
        verdict,
        reason
    };
    Console.WriteLine(JsonSerializer.Serialize(result, indented));
}

static void RunImport(string requestPath, JsonSerializerOptions options, JsonSerializerOptions indented)
{
    var request = Read<NativeImportRequest>(requestPath, options);
    Require(request.Schema == "ccd153.native-import-request/v1", "native_import_request_schema_unsupported");
    ValidateImplementationRef(request.ImplementationRef);
    ValidateTargetWeb(request.TargetWebUrl);

    var package = Read<PublishingPageMigrationPackage>(Resolve(requestPath, request.PackagePath), options);
    var admittedPlan = Read<AdmittedReproExecutionPlan>(Resolve(requestPath, request.AdmittedPlanPath), options);
    Require(UriEquals(package.Plan?.TargetWebUrl, request.TargetWebUrl), "target_web_mismatch");
    Require(string.Equals(package.PlanDigest, admittedPlan.PlanDigest, StringComparison.OrdinalIgnoreCase), "admitted_plan_package_mismatch");

    var cookieEnvironmentVariable = string.IsNullOrWhiteSpace(request.TargetCookieEnvironmentVariable)
        ? "CCD153_TARGET_COOKIE_HEADER"
        : request.TargetCookieEnvironmentVariable;
    var cookieHeader = Environment.GetEnvironmentVariable(cookieEnvironmentVariable);
    Require(!string.IsNullOrWhiteSpace(cookieHeader), "target_provider_session_unavailable");

    var outputDirectory = Resolve(requestPath, request.OutputDirectory);
    Directory.CreateDirectory(outputDirectory);
    var artifactStore = new DirectoryMigrationArtifactStore(Resolve(requestPath, request.ArtifactStorePath));
    var journal = new NativeExecutionJournal();
    PublishingPageImportReceipt receipt;
    using (var context = new ClientContext(package.Plan.TargetWebUrl))
    {
        context.RequestTimeout = 180000;
        context.ExecutingWebRequest += (_, eventArgs) =>
        {
            eventArgs.WebRequestExecutor.RequestHeaders["Cookie"] = cookieHeader;
            eventArgs.WebRequestExecutor.RequestHeaders["Cache-Control"] = "no-cache, no-store";
            eventArgs.WebRequestExecutor.RequestHeaders["Pragma"] = "no-cache";
        };
        receipt = new PublishingPageMigrationImporter().ImportAdmitted(
            context,
            package,
            package.PlanDigest,
            admittedPlan,
            journal: journal,
            artifactStore: artifactStore);
    }

    var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
        admittedPlan,
        package.PlanDigest,
        admittedPlan.TargetIdentity);
    if (receipt.ExecutionStatus == MigrationExecutionStatus.Succeeded)
    {
        PublishingPageImportReceiptValidator.ValidateAdmittedExecution(
            receipt,
            admittedPlan,
            admittedDigest);
    }

    var receiptPath = Path.Combine(outputDirectory, "publishing-page-import-receipt-v5.json");
    var ledgerPath = Path.Combine(outputDirectory, "operation-ledger.json");
    Write(receiptPath, receipt, indented);
    Write(ledgerPath, journal.ToDocument(request.CaseId, request.ImplementationRef), indented);
    var manifest = new
    {
        schema = "ccd153.native-import-result/v1",
        request.CaseId,
        producer = new { id = ProducerContract.Id, version = ProducerContract.Version, implementationRef = request.ImplementationRef },
        binarySha256 = CurrentBinarySha256(),
        admittedPlanDigestSha256 = admittedDigest,
        importReceiptDigestSha256 = ContractDigest(receipt, options),
        receipt.ExecutionStatus,
        receipt.MutationStarted,
        stepCount = receipt.Steps?.Count ?? 0,
        receipt.OperationId,
        operations = admittedPlan.Operations,
        receiptPath = Path.GetFileName(receiptPath),
        ledgerPath = Path.GetFileName(ledgerPath)
    };
    Write(Path.Combine(outputDirectory, "import-manifest.json"), manifest, indented);
    Console.WriteLine(JsonSerializer.Serialize(manifest, indented));
    Environment.ExitCode = receipt.ExecutionStatus == MigrationExecutionStatus.Succeeded ? 0 : 2;
}

static void RunReconcile(string requestPath, JsonSerializerOptions options, JsonSerializerOptions indented)
{
    var request = Read<NativeReconcileRequest>(requestPath, options);
    Require(request.Schema == "ccd153.native-reconcile-request/v1", "native_reconcile_request_schema_unsupported");
    ValidateImplementationRef(request.ImplementationRef);
    var package = Read<PublishingPageMigrationPackage>(Resolve(requestPath, request.PackagePath), options);
    ValidateTargetWeb(package.Plan?.TargetWebUrl);
    var admittedPlan = Read<AdmittedReproExecutionPlan>(Resolve(requestPath, request.AdmittedPlanPath), options);
    var importReceipt = Read<PublishingPageImportReceipt>(Resolve(requestPath, request.ImportReceiptPath), options);
    var runtimeReceipt = Read<RuntimeVerificationReceipt>(Resolve(requestPath, request.RuntimeReceiptPath), options);
    var handoff = Read<AssessmentCaptureHandoff>(Resolve(requestPath, request.AssessmentHandoffPath), options);
    var sourceVersion = Read<SourceVersionComparison>(Resolve(requestPath, request.SourceVersionComparisonPath), options);
    var measurements = Read<ActualIngredientMeasurementSet>(Resolve(requestPath, request.ActualMeasurementsPath), options);
    Require(measurements.Schema == "ccd153.actual-ingredient-measurements/v1", "actual_measurement_schema_unsupported");
    Require(measurements.CaseId == request.CaseId, "actual_measurement_case_mismatch");
    Require(measurements.ReadbackOperationId == admittedPlan.Operations.ReadbackOperationId, "actual_measurement_operation_mismatch");
    Require(measurements.CapturedAtUtc != default && measurements.CapturedAtUtc >= importReceipt.CompletedAtUtc,
        "actual_measurement_not_fresh");

    var artifactStore = new DirectoryMigrationArtifactStore(Resolve(requestPath, request.ArtifactStorePath));
    var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
        admittedPlan,
        package.PlanDigest,
        admittedPlan.TargetIdentity);
    PublishingPageImportReceiptValidator.ValidateAdmittedExecution(importReceipt, admittedPlan, admittedDigest);
    var importDigest = ContractDigest(importReceipt, options);
    var runtimeDigest = ContractDigest(runtimeReceipt, options);
    var observations = measurements.Ingredients.Select(value => ToObservation(value, artifactStore)).ToList();

    var compareRequest = new PublishingPageCompareRequest
    {
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        Producer = new CompareProducer
        {
            Id = ProducerContract.Id,
            Version = ProducerContract.Version,
            ImplementationRef = request.ImplementationRef
        },
        AssessmentHandoff = handoff,
        AssessmentProducerRevisionId = PublishingPageCompareContract.AssessmentProducerRevisionId,
        AssessmentHandoffDigestSha256 = ContractDigest(handoff, options),
        Package = package,
        AdmittedPlan = admittedPlan,
        AdmittedPlanDigestSha256 = admittedDigest,
        ImportReceipt = importReceipt,
        ImportReceiptDigestSha256 = importDigest,
        RuntimeReceipt = runtimeReceipt,
        RuntimeReceiptDigestSha256 = runtimeDigest,
        Bindings = new CompareBindings
        {
            ManifestDigestSha256 = request.ManifestDigestSha256,
            SourceCaptureReceiptDigestSha256 = request.SourceCaptureReceiptDigestSha256,
            ExportSchemaVersion = package.ExportSchemaVersion,
            SnapshotDigestSha256 = package.SnapshotDigest,
            IngredientGraphSchemaVersion = package.Plan.IngredientGraph.SchemaVersion,
            IngredientProjectionVersion = package.Plan.IngredientGraph.ProjectionVersion,
            MigrationPackageSchemaVersion = package.SchemaVersion,
            PlanDigestSha256 = package.PlanDigest,
            AdmittedPlanDigestSha256 = admittedDigest,
            SourceVersionDigestSha256 = admittedPlan.SourceVersion.VersionDigestSha256,
            Operations = admittedPlan.Operations,
            ImportReceiptSchemaVersion = importReceipt.SchemaVersion,
            ImportReceiptDigestSha256 = importDigest,
            RuntimeReceiptSchemaVersion = runtimeReceipt.SchemaVersion,
            RuntimeReceiptDigestSha256 = runtimeDigest
        },
        SourceVersionComparison = sourceVersion,
        SourceAuthState = request.SourceAuthState ?? "valid",
        PlannedTargetIdentity = new CompareTargetIdentity
        {
            WebUrlHashSha256 = Sha256(importReceipt.TargetWebUrl.ToLowerInvariant()),
            PageServerRelativeUrlHashSha256 = Sha256(importReceipt.TargetPageServerRelativeUrl.ToLowerInvariant()),
            FileUniqueId = importReceipt.TargetFileUniqueId,
            ListItemId = importReceipt.TargetListItemId,
            VersionLabel = importReceipt.TargetVersionLabel,
            CanonicalIdentity = admittedPlan.TargetIdentity
        },
        Ingredients = observations
    };

    var report = PublishingPageCompareReconciler.Reconcile(compareRequest, artifactStore);
    var outputDirectory = Resolve(requestPath, request.OutputDirectory);
    Directory.CreateDirectory(outputDirectory);
    Write(Path.Combine(outputDirectory, "pnp-page-compare-report-v1.json"), report, indented);
    var manifest = new
    {
        schema = "ccd153.native-reconcile-result/v1",
        request.CaseId,
        producer = compareRequest.Producer,
        binarySha256 = CurrentBinarySha256(),
        admittedPlanDigestSha256 = admittedDigest,
        importReceiptDigestSha256 = importDigest,
        runtimeReceiptDigestSha256 = runtimeDigest,
        reportDigestSha256 = report.ReportDigestSha256,
        report.Acceptance.Verdict,
        report.Storage.Status,
        runtimeStatus = report.Runtime.Status,
        operations = admittedPlan.Operations,
        actualMeasurementCapturedAtUtc = measurements.CapturedAtUtc
    };
    Write(Path.Combine(outputDirectory, "reconcile-manifest.json"), manifest, indented);
    Console.WriteLine(JsonSerializer.Serialize(manifest, indented));
}

static IngredientCompareObservation ToObservation(
    ActualIngredientMeasurement value,
    IMigrationArtifactStore artifactStore)
{
    Require(value != null, "actual_measurement_missing");
    var rawDigest = RecomputeOptional(value.ActualRawArtifactSha256, artifactStore);
    var canonicalDigest = RecomputeOptional(value.ActualCanonicalArtifactSha256, artifactStore);
    if (value.Material)
    {
        Require(rawDigest != null || canonicalDigest != null, "material_actual_measurement_missing:" + value.IngredientId);
    }
    var evidenceRefs = new List<string>(value.EvidenceRefs ?? new List<string>());
    foreach (var digest in new[] { rawDigest, canonicalDigest }.Where(item => item != null))
    {
        var reference = "sha256:" + digest;
        if (!evidenceRefs.Contains(reference, StringComparer.OrdinalIgnoreCase))
        {
            evidenceRefs.Add(reference);
        }
    }
    return new IngredientCompareObservation
    {
        IngredientId = value.IngredientId,
        Kind = value.Kind,
        Material = value.Material,
        AssertionApplicable = value.AssertionApplicable,
        SourceVersionSensitive = value.SourceVersionSensitive,
        TargetEvidenceComplete = value.TargetEvidenceComplete,
        TargetPresent = value.TargetPresent,
        UnexpectedExtra = value.UnexpectedExtra,
        RuntimeRequirementId = value.RuntimeRequirementId,
        UnknownReasonCode = value.UnknownReasonCode,
        Lineage = new IngredientCompareLineage
        {
            RequestedUrlHashSha256 = value.RequestedUrlHashSha256,
            SourceArtifactDigestSha256 = value.SourceArtifactDigestSha256,
            SourceIngredientId = value.SourceIngredientId,
            ActionId = value.ActionId,
            TargetIdentity = value.TargetIdentity,
            EvidenceRefs = evidenceRefs,
            CauseIngredientIds = value.CauseIngredientIds ?? new List<string>()
        },
        Expected = value.Expected ?? new CompareDigestPair(),
        Actual = new CompareDigestPair
        {
            RawDigestSha256 = rawDigest,
            CanonicalDigestSha256 = canonicalDigest
        },
        ApprovedTransformedCanonicalDigestSha256 = value.ApprovedTransformedCanonicalDigestSha256,
        Message = value.Message
    };
}

static string RecomputeOptional(string digest, IMigrationArtifactStore artifactStore)
{
    if (string.IsNullOrWhiteSpace(digest)) return null;
    Require(artifactStore.Contains(digest), "actual_artifact_missing:" + digest);
    using var content = artifactStore.OpenRead(digest);
    var computed = MigrationDigest.ComputeSha256(content);
    Require(string.Equals(computed, digest, StringComparison.OrdinalIgnoreCase), "actual_artifact_corrupt:" + digest);
    return computed;
}

static void ValidateImplementationRef(string implementationRef)
{
    Require(IsSha1(implementationRef), "implementation_ref_invalid");
    var informational = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    var embedded = informational?.Split('+').LastOrDefault();
    Require(string.Equals(embedded, implementationRef, StringComparison.OrdinalIgnoreCase),
        "binary_implementation_ref_mismatch");
}

static void ValidateTargetWeb(string targetWebUrl)
{
    Require(Uri.TryCreate(targetWebUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Authority, ProducerContract.TargetAuthority, StringComparison.OrdinalIgnoreCase),
        "target_tenant_not_authorized");
}

static bool UriEquals(string left, string right) =>
    Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
    && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
    && string.Equals(leftUri.AbsoluteUri.TrimEnd('/'), rightUri.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

static T Read<T>(string path, JsonSerializerOptions options)
{
    Require(IOFile.Exists(path), "input_missing:" + path);
    var value = JsonSerializer.Deserialize<T>(IOFile.ReadAllText(path), options);
    Require(value != null, "input_invalid:" + path);
    return value;
}

static void Write<T>(string path, T value, JsonSerializerOptions options)
{
    IOFile.WriteAllText(path, JsonSerializer.Serialize(value, options) + Environment.NewLine, new UTF8Encoding(false));
}

static string Resolve(string requestPath, string path)
{
    Require(!string.IsNullOrWhiteSpace(path), "path_required");
    return Path.GetFullPath(Path.IsPathRooted(path)
        ? path
        : Path.Combine(Path.GetDirectoryName(requestPath)!, path));
}

static string ContractDigest<T>(T value, JsonSerializerOptions options) =>
    Sha256(JsonSerializer.Serialize(value, options));

static string Sha256(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static string CurrentBinarySha256()
{
    var path = Assembly.GetExecutingAssembly().Location;
    if (string.IsNullOrWhiteSpace(path) || !IOFile.Exists(path)) return null;
    using var content = IOFile.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}

static bool IsSha1(string value) => value?.Length == 40 && value.All(Uri.IsHexDigit);

static JsonSerializerOptions CreateJsonOptions(bool writeIndented)
{
    var result = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = writeIndented
    };
    result.Converters.Add(new JsonStringEnumConverter());
    return result;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

sealed class NativeImportRequest
{
    public string Schema { get; set; }
    public string CaseId { get; set; }
    public string ImplementationRef { get; set; }
    public string TargetWebUrl { get; set; }
    public string PackagePath { get; set; }
    public string AdmittedPlanPath { get; set; }
    public string ArtifactStorePath { get; set; }
    public string OutputDirectory { get; set; }
    public string TargetCookieEnvironmentVariable { get; set; }
}

sealed class NativeImportValidationRequest
{
    public string Schema { get; set; }
    public string CaseId { get; set; }
    public string ImplementationRef { get; set; }
    public string AdmittedPlanPath { get; set; }
    public string ImportReceiptPath { get; set; }
}

sealed class NativeReconcileRequest
{
    public string Schema { get; set; }
    public string CaseId { get; set; }
    public string ImplementationRef { get; set; }
    public string PackagePath { get; set; }
    public string AdmittedPlanPath { get; set; }
    public string ImportReceiptPath { get; set; }
    public string RuntimeReceiptPath { get; set; }
    public string AssessmentHandoffPath { get; set; }
    public string SourceVersionComparisonPath { get; set; }
    public string ActualMeasurementsPath { get; set; }
    public string ArtifactStorePath { get; set; }
    public string OutputDirectory { get; set; }
    public string ManifestDigestSha256 { get; set; }
    public string SourceCaptureReceiptDigestSha256 { get; set; }
    public string SourceAuthState { get; set; }
}

sealed class ActualIngredientMeasurementSet
{
    public string Schema { get; set; }
    public string CaseId { get; set; }
    public Guid ReadbackOperationId { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public IList<ActualIngredientMeasurement> Ingredients { get; set; } = new List<ActualIngredientMeasurement>();
}

sealed class ActualIngredientMeasurement
{
    public string IngredientId { get; set; }
    public string Kind { get; set; }
    public bool Material { get; set; } = true;
    public bool AssertionApplicable { get; set; } = true;
    public bool SourceVersionSensitive { get; set; } = true;
    public bool TargetEvidenceComplete { get; set; } = true;
    public bool TargetPresent { get; set; } = true;
    public bool UnexpectedExtra { get; set; }
    public string RuntimeRequirementId { get; set; }
    public string UnknownReasonCode { get; set; }
    public string RequestedUrlHashSha256 { get; set; }
    public string SourceArtifactDigestSha256 { get; set; }
    public string SourceIngredientId { get; set; }
    public string ActionId { get; set; }
    public string TargetIdentity { get; set; }
    public IList<string> EvidenceRefs { get; set; } = new List<string>();
    public IList<string> CauseIngredientIds { get; set; } = new List<string>();
    public CompareDigestPair Expected { get; set; }
    public string ActualRawArtifactSha256 { get; set; }
    public string ActualCanonicalArtifactSha256 { get; set; }
    public string ApprovedTransformedCanonicalDigestSha256 { get; set; }
    public string Message { get; set; }
}

sealed class NativeExecutionJournal : IMigrationExecutionJournal
{
    private readonly List<MigrationExecutionStateReceipt> states = new();
    private readonly List<MigrationMutationIntent> intents = new();
    private readonly List<MigrationMutationReceipt> receipts = new();

    public void WriteExecutionState(MigrationExecutionStateReceipt state) => states.Add(state);
    public void WriteIntent(MigrationMutationIntent intent) => intents.Add(intent);
    public void WriteReceipt(MigrationMutationReceipt receipt) => receipts.Add(receipt);

    public object ToDocument(string caseId, string implementationRef) => new
    {
        schema = "ccd153.native-operation-ledger/v1",
        caseId,
        producer = new { id = ProducerContract.Id, version = ProducerContract.Version, implementationRef },
        states,
        intents,
        receipts
    };
}

static class ProducerContract
{
    public const string Id = "ccd153-native-producer";
    public const string Version = "1.0.0";
    public const string TargetAuthority = "a830edad9050849cupcollect.sharepoint.com";
}
