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

    private static readonly JsonSerializerOptions StrictOptions = CreateOptions(false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(true);

    public static BrowserRuntimeAdapterResult EmitFromFile(string requestPath)
    {
        Require(File.Exists(requestPath), "request_missing");
        var requestJson = File.ReadAllText(requestPath);
        RejectDuplicateKeys(requestJson, requestPath);
        var request = JsonSerializer.Deserialize<BrowserRuntimeAdapterRequest>(requestJson, StrictOptions);
        Require(request != null, "request_invalid");
        return Emit(request, Path.GetDirectoryName(requestPath));
    }

    public static BrowserRuntimeAdapterResult Emit(BrowserRuntimeAdapterRequest request, string requestDirectory)
    {
        Require(request != null, "request_required");
        Require(string.Equals(request.SchemaVersion, RequestSchemaVersion, StringComparison.Ordinal),
            "request_schema_unsupported");
        Require(request.Expected != null, "expected_identity_required");
        var bindingPath = Resolve(requestDirectory, request.BindingPath);
        var artifactStorePath = Resolve(requestDirectory, request.ArtifactStorePath);
        var outputPath = Resolve(requestDirectory, request.OutputPath);
        Require(File.Exists(bindingPath), "binding_missing");
        Require(Directory.Exists(artifactStorePath), "artifact_store_missing");

        var bindingJson = File.ReadAllText(bindingPath);
        RejectDuplicateKeys(bindingJson, bindingPath);
        var binding = JsonSerializer.Deserialize<NativePageRuntimeBinding>(bindingJson, StrictOptions);
        Require(binding != null, "binding_invalid");
        ValidateBindingEnvelope(binding, request.Expected);
        ValidateProducer(request.CaptureProducer);

        var artifactStore = new DirectoryMigrationArtifactStore(artifactStorePath);
        var hasResults = request.Results != null && request.Results.Count > 0;
        var hasTerminal = request.TerminalObservation != null;
        Require(hasResults != hasTerminal, "request_result_one_of_required");

        RuntimeVerificationReceipt runtimeReceipt = null;
        string runtimeReceiptDigest = null;
        string resultKind;
        if (hasResults)
        {
            ValidatePositiveObservation(request, binding);
            runtimeReceipt = CreateRuntimeReceipt(request, binding);
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
            new ClassicWikiRuntimeEvidencePolicy(),
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

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        var temporaryPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(evidence, IndentedOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        File.Move(temporaryPath, outputPath, true);

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
        BrowserRuntimeExpectedIdentity expected)
    {
        Require(string.Equals(binding.SchemaVersion, NativePageRuntimeContract.BindingSchemaVersion, StringComparison.Ordinal),
            "binding_schema_unsupported");
        Require(string.Equals(binding.ProfileId, NativePageRuntimeContract.ClassicWikiProfile, StringComparison.Ordinal),
            "binding_profile_unsupported");
        Require(string.Equals(binding.ClaimId, NativePageRuntimeContract.ClaimId, StringComparison.Ordinal),
            "binding_claim_foreign");
        var declaredSeal = binding.ContentSha256;
        var computedSeal = NativePageRuntimeBindingValidator.SealBinding(binding);
        Require(DigestEquals(declaredSeal, computedSeal), "binding_content_seal_stale");
        Require(DigestEquals(declaredSeal, expected.BindingDigestSha256), "binding_digest_mismatch");
        Require(DigestEquals(binding.SourceVersion?.VersionDigestSha256, expected.SourceVersionDigestSha256),
            "source_version_digest_mismatch");
        Require(DigestEquals(binding.AdmittedPlanDigestSha256, expected.AdmittedPlanDigestSha256),
            "admitted_plan_digest_mismatch");
        Require(DigestEquals(binding.ImportReceiptDigestSha256, expected.ImportReceiptDigestSha256),
            "import_receipt_digest_mismatch");
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

    private static RuntimeVerificationReceipt CreateRuntimeReceipt(
        BrowserRuntimeAdapterRequest request,
        NativePageRuntimeBinding binding)
    {
        var manifestIds = binding.RequirementsManifest.Requirements
            .Select(value => value.Id)
            .ToHashSet(StringComparer.Ordinal);
        var resultsById = request.Results.ToDictionary(value => value.RequirementId, StringComparer.Ordinal);
        Require(manifestIds.SetEquals(resultsById.Keys), "runtime_result_coverage_mismatch");
        var status = manifestIds.All(id => resultsById[id].Passed)
            ? RuntimeVerificationStatus.Passed
            : RuntimeVerificationStatus.Failed;
        return new RuntimeVerificationReceipt
        {
            PlanDigest = binding.PlanDigest,
            AdmittedPlanDigestSha256 = binding.AdmittedPlanDigestSha256,
            OperationId = binding.Operations.RuntimeOperationId,
            ImportReceiptDigestSha256 = binding.ImportReceiptDigestSha256,
            RequirementsManifestDigestSha256 = binding.RequirementsManifestDigestSha256,
            ImplementationRef = binding.ContractProducerRef,
            SourceVersion = binding.SourceVersion,
            Operations = binding.Operations,
            TargetIdentity = binding.TargetStorageIdentity.CanonicalUrl,
            BrowserContext = request.BrowserContext,
            CompletedAtUtc = request.CompletedAtUtc,
            Results = request.Results,
            Status = status
        };
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

    private static bool DigestEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
