using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ccd263.RuntimeBrowser;

public static class BrowserRuntimeEvidenceAdapter
{
    public const string RequestSchemaVersion = "ccd272.browser-runtime-evidence-request/v1";
    public const string ResultSchemaVersion = "ccd272.browser-runtime-evidence-result/v1";
    public const string AdapterId = "ccd272.browser-runtime-adapter";
    public const string AdapterVersion = "1.0.0";
    public const string AuthorizedTargetHost = "a830edad9050849cupcollect.sharepoint.com";
    public const string OriginEvidencePackageDigest = "40aef6bc9c8b443a93839cdc99b8f06d2e0980e2883625766ac3b81bdf032283";
    public const string ClaimSnapshotDigest = "518fb815a78d760eeb709ad4ba8008657ce8319e41d3baff0096debb5c56b76a";
    public const string ClaimSourceVersionDigest = "7c0ed8c29e1a81c925f93bca41affa5035116f444af278a35c62ca807045f8d5";
    public static readonly Guid ClaimSourceFileUniqueId = Guid.Parse("c3b2c2bb-663d-47ed-8562-840c9fd685fb");
    public static readonly Guid ClaimRuntimeOperationId = Guid.Parse("77fa33e6-9814-434d-bd7f-919747fe3f60");
    public static readonly Guid ClaimTargetFileUniqueId = Guid.Parse("4881af85-37b6-4ae7-b307-df93c52994a7");

    private static readonly JsonSerializerOptions StrictOptions = CreateOptions(false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(true);

    public static BrowserRuntimeAdapterResult EmitFromFile(string requestPath)
    {
        Require(File.Exists(requestPath), "request_missing");
        var requestJson = File.ReadAllText(requestPath);
        RejectDuplicateKeys(requestJson, requestPath);
        var request = JsonSerializer.Deserialize<BrowserRuntimeAdapterRequest>(requestJson, StrictOptions);
        Require(request != null, "request_invalid");
        return Emit(request, Path.GetDirectoryName(requestPath), requestPath);
    }

    public static BrowserRuntimeAdapterResult Emit(BrowserRuntimeAdapterRequest request, string requestDirectory)
        => Emit(request, requestDirectory, null);

    private static BrowserRuntimeAdapterResult Emit(
        BrowserRuntimeAdapterRequest request,
        string requestDirectory,
        string requestPath)
    {
        Require(request != null, "request_required");
        Require(string.Equals(request.SchemaVersion, RequestSchemaVersion, StringComparison.Ordinal),
            "request_schema_unsupported");
        Require(request.Expected != null, "expected_identity_required");
        var bindingPath = Resolve(requestDirectory, request.BindingPath);
        var admittedPlanPath = Resolve(requestDirectory, request.AdmittedPlanPath);
        var artifactStorePath = Resolve(requestDirectory, request.ArtifactStorePath);
        var outputPath = Resolve(requestDirectory, request.OutputPath);
        Require(File.Exists(bindingPath), "binding_missing");
        Require(File.Exists(admittedPlanPath), "admitted_plan_missing");
        Require(Directory.Exists(artifactStorePath), "artifact_store_missing");
        ValidateOutputFence(outputPath, requestPath, bindingPath, admittedPlanPath, artifactStorePath);

        var bindingJson = File.ReadAllText(bindingPath);
        RejectDuplicateKeys(bindingJson, bindingPath);
        var binding = JsonSerializer.Deserialize<NativePageRuntimeBinding>(bindingJson, StrictOptions);
        Require(binding != null, "binding_invalid");
        var admittedPlanJson = File.ReadAllText(admittedPlanPath);
        RejectDuplicateKeys(admittedPlanJson, admittedPlanPath);
        var admittedPlan = JsonSerializer.Deserialize<AdmittedReproExecutionPlan>(admittedPlanJson, StrictOptions);
        Require(admittedPlan != null, "admitted_plan_invalid");
        ValidateBindingEnvelope(binding, admittedPlan, request.Expected);
        ValidateProducer(request.CaptureProducer);
        ValidateOriginEvidencePackage(request.Extensions);

        var artifactStore = new DirectoryMigrationArtifactStore(artifactStorePath);
        var policy = new ClassicWikiRuntimeEvidencePolicy();
        policy.ValidateBinding(binding, artifactStore);
        var hasResults = request.Results != null && request.Results.Count > 0;
        var hasTerminal = request.TerminalObservation != null;
        Require(hasResults != hasTerminal, "request_result_one_of_required");

        RuntimeVerificationReceipt runtimeReceipt = null;
        string runtimeReceiptDigest = null;
        string resultKind;
        if (hasResults)
        {
            ValidatePositiveObservation(request, binding);
            runtimeReceipt = RuntimeVerificationReceiptFactory.Create(
                admittedPlan,
                binding.AdmittedPlanDigestSha256,
                binding.ImportReceiptDigestSha256,
                binding.TargetStorageIdentity.CanonicalUrl,
                binding.RequirementsManifest,
                binding.ContractProducerRef,
                request.BrowserContext,
                request.Results,
                request.CompletedAtUtc);
            runtimeReceiptDigest = MigrationDigest.ComputeSha256(
                ClassicWikiPackageSerializer.SerializeCanonical(runtimeReceipt));
            resultKind = NativePageRuntimeContract.RuntimeResultKind;
        }
        else
        {
            ValidateTerminalObservation(request);
            resultKind = NativePageRuntimeContract.TerminalResultKind;
        }

        var manifest = CreateArtifactManifest(request, artifactStore);
        var evidence = new ExternalPageRuntimeEvidence
        {
            RunId = binding.RunId,
            ClaimId = binding.ClaimId,
            BindingDigestSha256 = binding.ContentSha256,
            CaptureProducer = request.CaptureProducer,
            ProfileClass = binding.ProfileId,
            StartedAtUtc = request.StartedAtUtc,
            CompletedAtUtc = request.CompletedAtUtc,
            ResultKind = resultKind,
            ObservedTargetIdentity = request.ObservedTargetIdentity,
            PreCaptureTargetReadback = request.PreCaptureTargetReadback,
            PostCaptureTargetReadback = request.PostCaptureTargetReadback,
            Attempts = request.Attempts,
            ArtifactManifest = manifest,
            RuntimeReceipt = runtimeReceipt,
            RuntimeReceiptDigestSha256 = runtimeReceiptDigest,
            TerminalObservation = request.TerminalObservation,
            Extensions = request.Extensions ?? new Dictionary<string, string>()
        };

        NativePageRuntimeBindingValidator.SealExternalEvidence(evidence);
        var validatedDigest = NativePageRuntimeBindingValidator.ValidateExternalEvidenceAndComputeDigest(
            evidence,
            binding,
            policy,
            artifactStore,
            out var semanticStatus);
        if (runtimeReceipt != null)
        {
            Require(semanticStatus == runtimeReceipt.Status,
                "runtime_claim_does_not_match_shared_semantic_policy");
        }
        else
        {
            Require(semanticStatus == RuntimeVerificationStatus.Failed,
                "terminal_observation_must_remain_degraded");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(evidence, IndentedOptions) + Environment.NewLine,
                new UTF8Encoding(false));
            File.Move(temporaryPath, outputPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        var publishedJson = File.ReadAllText(outputPath);
        RejectDuplicateKeys(publishedJson, outputPath);
        var published = JsonSerializer.Deserialize<ExternalPageRuntimeEvidence>(publishedJson, StrictOptions);
        Require(published != null, "published_evidence_invalid");
        var publishedDigest = NativePageRuntimeBindingValidator.ValidateExternalEvidenceAndComputeDigest(
            published,
            binding,
            policy,
            artifactStore,
            out var publishedSemanticStatus);
        Require(DigestEquals(publishedDigest, validatedDigest)
            && publishedSemanticStatus == semanticStatus,
            "published_evidence_reopen_mismatch");

        return new BrowserRuntimeAdapterResult
        {
            ExternalEvidenceSchemaVersion = evidence.SchemaVersion,
            ExternalEvidenceDigestSha256 = validatedDigest,
            BindingDigestSha256 = binding.ContentSha256,
            ResultKind = resultKind,
            OutputPath = outputPath
        };
    }

    public static string SerializeIndented<T>(T value) =>
        JsonSerializer.Serialize(value, IndentedOptions);

    private static void ValidateBindingEnvelope(
        NativePageRuntimeBinding binding,
        AdmittedReproExecutionPlan admittedPlan,
        BrowserRuntimeExpectedIdentity expected)
    {
        Require(string.Equals(binding.SchemaVersion, NativePageRuntimeContract.BindingSchemaVersion, StringComparison.Ordinal),
            "binding_schema_unsupported");
        Require(string.Equals(binding.ProfileId, NativePageRuntimeContract.ClassicWikiProfile, StringComparison.Ordinal),
            "binding_profile_unsupported");
        Require(string.Equals(binding.ClaimId, NativePageRuntimeContract.ClaimId, StringComparison.Ordinal),
            "binding_claim_foreign");
        Require(binding.RunId != Guid.Empty, "binding_run_id_required");
        Require(binding.SourceIdentity?.FileUniqueId == ClaimSourceFileUniqueId,
            "claim_source_file_identity_mismatch");
        ValidateDigest(binding.ContentSha256, "binding_content_digest_required");
        ValidateDigest(binding.SnapshotDigestSha256, "snapshot_digest_required");
        ValidateDigest(binding.PlanDigest, "plan_digest_required");
        ValidateDigest(binding.AdmittedPlanDigestSha256, "admitted_plan_digest_required");
        ValidateDigest(binding.ImportReceiptDigestSha256, "import_receipt_digest_required");
        ValidateDigest(binding.SourceVersion?.IdentityDigestSha256, "source_identity_digest_required");
        ValidateDigest(binding.SourceVersion?.VersionDigestSha256, "source_version_digest_required");
        ValidateDigest(binding.RequirementsManifestDigestSha256, "requirements_manifest_digest_required");
        ValidateDigest(binding.ExpectedAuthoredContentSha256, "expected_authored_content_digest_required");
        RequireDigestEquals(binding.SnapshotDigestSha256, ClaimSnapshotDigest, "claim_snapshot_digest_mismatch");
        RequireDigestEquals(binding.SourceVersion?.VersionDigestSha256, ClaimSourceVersionDigest,
            "claim_source_version_digest_mismatch");
        ValidateImplementationRef(binding.ContractProducerRef, "contract_producer_ref_invalid");
        Require(binding.RequirementsManifest?.Requirements != null
            && binding.RequirementsManifest.Requirements.Count > 0,
            "requirements_manifest_required");
        Require(binding.TargetStorageIdentity != null, "target_identity_required");
        Require(binding.TargetStorageIdentity.FileUniqueId == ClaimTargetFileUniqueId
            && binding.TargetStorageIdentity.ListItemId == 2
            && string.Equals(binding.TargetStorageIdentity.ListItemVersion, "3.0", StringComparison.Ordinal),
            "claim_target_identity_mismatch");
        Require(binding.Operations?.RuntimeOperationId == ClaimRuntimeOperationId,
            "claim_runtime_operation_id_mismatch");
        AdmittedReproExecutionPlanValidator.ValidateSourceVersion(binding.SourceVersion);
        AdmittedReproExecutionPlanValidator.ValidateOperations(binding.Operations);
        var declaredSeal = binding.ContentSha256;
        var computedSeal = NativePageRuntimeBindingValidator.SealBinding(binding);
        RequireDigestEquals(declaredSeal, computedSeal, "binding_content_seal_stale");
        RequireDigestEquals(declaredSeal, expected.BindingDigestSha256, "binding_digest_mismatch");
        RequireDigestEquals(binding.SourceVersion.VersionDigestSha256, expected.SourceVersionDigestSha256,
            "source_version_digest_mismatch");
        var computedAdmissionDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
            admittedPlan,
            binding.PlanDigest,
            binding.TargetStorageIdentity.CanonicalUrl);
        Require(AdmittedReproExecutionPlanValidator.SameSourceVersion(binding.SourceVersion, admittedPlan.SourceVersion)
            && AdmittedReproExecutionPlanValidator.SameOperations(binding.Operations, admittedPlan.Operations),
            "admitted_plan_lineage_mismatch");
        RequireDigestEquals(binding.AdmittedPlanDigestSha256, computedAdmissionDigest,
            "admitted_plan_digest_stale");
        RequireDigestEquals(binding.AdmittedPlanDigestSha256, expected.AdmittedPlanDigestSha256,
            "admitted_plan_digest_mismatch");
        RequireDigestEquals(binding.ImportReceiptDigestSha256, expected.ImportReceiptDigestSha256,
            "import_receipt_digest_mismatch");
        Require(expected.RuntimeOperationId != Guid.Empty, "expected_runtime_operation_id_required");
        Require(binding.Operations?.RuntimeOperationId == expected.RuntimeOperationId,
            "runtime_operation_id_mismatch");
        Require(binding.TargetStorageIdentity?.FileUniqueId == expected.TargetFileUniqueId
            && binding.TargetStorageIdentity.ListItemId == expected.TargetListItemId
            && string.Equals(binding.TargetStorageIdentity.ListItemVersion, expected.TargetListItemVersion, StringComparison.Ordinal)
            && string.Equals(binding.TargetStorageIdentity.CanonicalUrl, expected.TargetCanonicalUrl, StringComparison.Ordinal),
            "target_identity_mismatch");
        Require(Uri.TryCreate(binding.TargetStorageIdentity.CanonicalUrl, UriKind.Absolute, out var targetUri)
            && string.Equals(targetUri.Host, AuthorizedTargetHost, StringComparison.OrdinalIgnoreCase),
            "target_host_unauthorized");
    }

    private static void ValidateProducer(NativePageRuntimeCaptureProducer producer)
    {
        Require(producer != null
            && string.Equals(producer.AdapterId, AdapterId, StringComparison.Ordinal)
            && string.Equals(producer.Version, AdapterVersion, StringComparison.Ordinal),
            "capture_producer_unsupported");
        ValidateImplementationRef(producer.ImplementationRef, "capture_producer_implementation_ref_invalid");
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Require((informationalVersion ?? string.Empty).IndexOf(
                producer.ImplementationRef,
                StringComparison.OrdinalIgnoreCase) >= 0,
            "capture_producer_binary_ref_mismatch");
        Require(!string.IsNullOrWhiteSpace(producer.ToolVersion)
            && !string.IsNullOrWhiteSpace(producer.ProtocolVersion),
            "capture_producer_version_incomplete");
    }

    private static void ValidateOriginEvidencePackage(IDictionary<string, string> extensions)
    {
        string packageDigest = null;
        Require(extensions != null
            && extensions.TryGetValue("ccd272.originEvidencePackageDigest", out packageDigest),
            "origin_evidence_package_digest_required");
        RequireDigestEquals(packageDigest, OriginEvidencePackageDigest,
            "origin_evidence_package_digest_mismatch");
    }

    private static void ValidatePositiveObservation(
        BrowserRuntimeAdapterRequest request,
        NativePageRuntimeBinding binding)
    {
        Require(request.BrowserContext != null && request.BrowserContext.FreshContext,
            "fresh_browser_context_required");
        Require(request.Attempts != null && request.Attempts.Count > 0 && request.Attempts.All(value =>
                value != null
                && !value.TransportUnavailable
                && string.Equals(value.RequestedUrl, binding.TargetStorageIdentity.CanonicalUrl, StringComparison.Ordinal)
                && string.Equals(value.FinalUrl, binding.TargetStorageIdentity.CanonicalUrl, StringComparison.Ordinal)
                && string.Equals(value.SemanticResult, "surface_present", StringComparison.Ordinal)),
            "positive_attempt_is_redirected_unavailable_or_semantically_invalid");
        Require(request.Results.Count == binding.RequirementsManifest.Requirements.Count,
            "runtime_result_coverage_mismatch");
        Require(request.Results.All(value => value != null
                && string.Equals(value.ImplementationRef, binding.ContractProducerRef, StringComparison.OrdinalIgnoreCase)
                && string.Equals(value.BrowserContextId, request.BrowserContext.BrowserContextId, StringComparison.Ordinal)),
            "runtime_result_producer_or_context_mismatch");
    }

    private static void ValidateTerminalObservation(BrowserRuntimeAdapterRequest request)
    {
        Require(request.BrowserContext == null, "terminal_observation_cannot_claim_browser_receipt_context");
        Require(request.Results == null || request.Results.Count == 0,
            "terminal_observation_cannot_claim_runtime_results");
        Require(request.Attempts != null && request.Attempts.Count > 0 && request.Attempts.Count <= 3,
            "terminal_attempt_bounds_invalid");
        var terminal = request.TerminalObservation;
        var last = request.Attempts[^1];
        if (string.Equals(terminal.Kind, "access-denied", StringComparison.Ordinal))
        {
            var semantic = (terminal.SemanticDetectorResult ?? string.Empty).ToLowerInvariant();
            Require(last.HttpStatusCode is 401 or 403
                || semantic.Contains("access-denied", StringComparison.Ordinal)
                || semantic.Contains("access_denied", StringComparison.Ordinal)
                || semantic.Contains("unauthorized", StringComparison.Ordinal)
                || semantic.Contains("forbidden", StringComparison.Ordinal)
                || semantic.Contains("sign-in", StringComparison.Ordinal),
                "access_denied_terminal_observation_unproven");
        }
        else if (string.Equals(terminal.Kind, "transport-unavailable", StringComparison.Ordinal))
        {
            Require(last.TransportUnavailable && !last.HttpStatusCode.HasValue,
                "transport_terminal_observation_unproven");
        }
        else if (string.Equals(terminal.Kind, "semantic-detector-failure", StringComparison.Ordinal))
        {
            Require(!string.IsNullOrWhiteSpace(terminal.SemanticDetectorResult),
                "semantic_detector_terminal_observation_unproven");
        }
        else
        {
            throw new InvalidDataException("terminal_observation_kind_unsupported");
        }
    }

    private static MigrationArtifactManifest CreateArtifactManifest(
        BrowserRuntimeAdapterRequest request,
        IMigrationArtifactStore store)
    {
        var references = new Dictionary<string, ArtifactReference>(StringComparer.OrdinalIgnoreCase);
        AddNative(request.PreCaptureTargetReadback?.Artifact);
        AddNative(request.PostCaptureTargetReadback?.Artifact);
        foreach (var attempt in request.Attempts ?? Array.Empty<NativePageRuntimeAttempt>())
        {
            foreach (var artifact in attempt?.RawEvidence ?? Array.Empty<NativePageRuntimeArtifactReference>()) AddNative(artifact);
        }
        foreach (var result in request.Results ?? Array.Empty<RuntimeVerificationResult>())
        {
            AddArtifact(result?.EvidenceArtifactSha256, result?.EvidenceArtifactLength ?? 0, result?.Http?.ContentType ?? "text/html");
            AddArtifact(result?.DomProbeArtifactSha256, result?.DomProbeArtifactLength ?? 0, "application/json");
            AddArtifact(result?.ScreenshotArtifactSha256, result?.ScreenshotArtifactLength ?? 0, "image/png");
        }
        Require(references.Count > 0, "artifact_manifest_empty");
        var manifest = new MigrationArtifactManifest { Artifacts = references.Values.OrderBy(value => value.Sha256, StringComparer.Ordinal).ToList() };
        manifest.ContentSha256 = MigrationDigest.ComputeSha256(ClassicWikiPackageSerializer.SerializeCanonical(manifest));
        return manifest;

        void AddNative(NativePageRuntimeArtifactReference value)
        {
            if (value != null) AddArtifact(value.Sha256, value.Length, value.MediaType);
        }

        void AddArtifact(string digest, long length, string mediaType)
        {
            if (string.IsNullOrWhiteSpace(digest)) return;
            Require(length > 0 && store.Contains(digest), "artifact_missing_or_empty:" + digest);
            references[digest] = new ArtifactReference
            {
                Sha256 = digest.ToLowerInvariant(),
                Length = length,
                MediaType = mediaType,
                Availability = EvidenceAvailability.Captured
            };
        }
    }

    private static void RejectDuplicateKeys(string json, string path)
    {
        using var document = JsonDocument.Parse(json);
        Walk(document.RootElement, "$", path);

        static void Walk(JsonElement element, string locator, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    Require(names.Add(property.Name), "duplicate_json_key:" + path + ":" + locator + "." + property.Name);
                    Walk(property.Value, locator + "." + property.Name, path);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray()) Walk(item, locator + "[" + index++ + "]", path);
            }
        }
    }

    private static string Resolve(string root, string value)
    {
        Require(!string.IsNullOrWhiteSpace(value), "path_required");
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root ?? Directory.GetCurrentDirectory(), value));
    }

    private static void ValidateOutputFence(
        string outputPath,
        string requestPath,
        string bindingPath,
        string admittedPlanPath,
        string artifactStorePath)
    {
        var output = NormalizePath(outputPath);
        foreach (var input in new[] { requestPath, bindingPath, admittedPlanPath })
        {
            if (!string.IsNullOrWhiteSpace(input))
                Require(!PathEquals(output, NormalizePath(input)), "output_input_alias_forbidden");
        }
        var store = NormalizePath(artifactStorePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Require(!PathEquals(output, store)
            && !output.StartsWith(store + Path.DirectorySeparatorChar, PathComparison),
            "output_artifact_store_alias_forbidden");
        Require(!File.Exists(outputPath) && !Directory.Exists(outputPath), "output_path_must_be_new");
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, fullPath)
                     .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            FileSystemInfo target = null;
            if (info.Exists || !string.IsNullOrWhiteSpace(info.LinkTarget))
                target = info.ResolveLinkTarget(true);
            current = target?.FullName ?? candidate;
        }
        return Path.GetFullPath(current)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(left, right, PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = indented
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void ValidateImplementationRef(string value, string error)
    {
        Require(value != null && value.Length == 40 && value.All(Uri.IsHexDigit), error);
    }

    private static void ValidateDigest(string value, string error)
    {
        Require(value != null && value.Length == 64 && value.All(Uri.IsHexDigit), error);
    }

    private static void RequireDigestEquals(string left, string right, string error)
    {
        ValidateDigest(left, error);
        ValidateDigest(right, error);
        Require(DigestEquals(left, right), error);
    }

    private static bool DigestEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
