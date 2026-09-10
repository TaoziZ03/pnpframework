using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class NativeRuntimeCommands
{
    public static void Run(string requestPath)
    {
        var options = CreateOptions(false);
        var indented = CreateOptions(true);
        var request = ReadStrict<NativeRuntimeRequest>(requestPath, options);
        Require(string.Equals(request.Schema, "ccd153.native-runtime-request/v1", StringComparison.Ordinal),
            "native_runtime_request_schema_unsupported");
        Require(string.Equals(request.ProfileId, NativePageRuntimeContract.ClassicWikiProfile, StringComparison.Ordinal),
            "native_runtime_profile_unsupported");
        ValidateImplementationRef(request.ImplementationRef);

        var package = ReadStrict<ClassicWikiMigrationPackage>(Resolve(requestPath, request.PackagePath), options);
        var admittedPlan = ReadStrict<AdmittedReproExecutionPlan>(Resolve(requestPath, request.AdmittedPlanPath), options);
        var aggregate = ReadStrict<NativePageImportReceiptAggregate>(Resolve(requestPath, request.ReceiptAggregatePath), options);
        var binding = ReadStrict<NativePageRuntimeBinding>(Resolve(requestPath, request.BindingPath), options);
        var external = ReadOptional<ExternalPageRuntimeEvidence>(requestPath, request.ExternalEvidencePath, options);
        var provenanceManifest = ReadStrict<ProducerBuildProvenanceManifest>(
            Resolve(requestPath, request.ProducerBuildManifestPath), options);
        var artifactStorePath = Resolve(requestPath, request.ArtifactStorePath);
        Require(Directory.Exists(artifactStorePath), "runtime_artifact_store_missing");
        var artifactStore = new DirectoryMigrationArtifactStore(artifactStorePath);
        var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
            admittedPlan,
            package.PlanDigest,
            admittedPlan.TargetIdentity);
        Require(string.Equals(binding.ContractProducerRef, request.ImplementationRef, StringComparison.OrdinalIgnoreCase),
            "runtime_binding_contract_producer_ref_mismatch");

        var acceptance = ClassicWikiNativeRuntimeAcceptance.Evaluate(
            package,
            admittedPlan,
            admittedDigest,
            aggregate,
            binding,
            external,
            artifactStore,
            new ClassicWikiRuntimeEvidencePolicy(),
            provenanceManifest,
            new UnverifiedProducerBuildProvenanceVerifier(),
            "ccd153-native-producer.runtime-evaluator",
            request.ImplementationRef,
            request.EvaluatedAtUtc == default ? DateTimeOffset.UtcNow : request.EvaluatedAtUtc);

        var acceptanceBytes = Encoding.UTF8.GetBytes(ClassicWikiPackageSerializer.SerializeCanonical(acceptance));
        ArtifactReference acceptanceArtifact;
        using (var acceptanceStream = new MemoryStream(acceptanceBytes, writable: false))
        {
            acceptanceArtifact = artifactStore.Put(
                acceptanceStream,
                "application/vnd.pnp.native-runtime-acceptance+json",
                "native-page-runtime-acceptance-receipt-v1.json");
        }
        var action = MigrationActionSignature.Create(
            "classic-wiki.runtime:" + binding.Operations.RuntimeOperationId.ToString("D"),
            "RuntimeVerification",
            binding.ContentSha256,
            external?.ContentSha256,
            binding.TargetStorageIdentity.CanonicalUrl,
            acceptance.ContentSha256);
        var journalReference = new MigrationExecutionArtifactReference
        {
            OperationId = binding.Operations.RuntimeOperationId,
            PlanDigest = binding.PlanDigest,
            ActionId = action.ActionId,
            ActionSignature = action.Signature,
            WrittenAtUtc = acceptance.EvaluatedAtUtc,
            ArtifactKind = MigrationExecutionArtifactKind.VerificationEvidence,
            ArtifactSchemaVersion = acceptance.SchemaVersion,
            Sha256 = acceptanceArtifact.Sha256,
            Length = acceptanceArtifact.Length,
            MediaType = acceptanceArtifact.MediaType
        };
        AppendJournalReference(Resolve(requestPath, request.VerificationJournalPath), journalReference);

        var outputDirectory = Resolve(requestPath, request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var acceptancePath = Path.Combine(outputDirectory, "native-page-runtime-acceptance-receipt-v1.json");
        Write(acceptancePath, acceptance, indented);
        var result = new
        {
            schema = "ccd153.native-runtime-result/v1",
            request.CaseId,
            request.ProfileId,
            request.ImplementationRef,
            binarySha256 = CurrentBinarySha256(),
            admittedPlanDigestSha256 = admittedDigest,
            importReceiptDigestSha256 = aggregate.ReceiptDigestSha256,
            bindingDigestSha256 = binding.ContentSha256,
            externalEvidenceDigestSha256 = external?.ContentSha256,
            acceptance.BindingValidationStatus,
            acceptance.RuntimeVerificationStatus,
            acceptance.AcceptanceStatus,
            acceptance.ProvenanceStatus,
            verificationEvidenceSha256 = acceptanceArtifact.Sha256,
            verificationJournalPath = request.VerificationJournalPath,
            acceptancePath = Path.GetFileName(acceptancePath)
        };
        Write(Path.Combine(outputDirectory, "native-runtime-manifest.json"), result, indented);
        Console.WriteLine(JsonSerializer.Serialize(result, indented));
        Environment.ExitCode = acceptance.AcceptanceStatus == MigrationAcceptanceStatus.Rejected ? 2 : 0;
    }

    private static T ReadOptional<T>(string requestPath, string path, JsonSerializerOptions options) where T : class
    {
        return string.IsNullOrWhiteSpace(path) ? null : ReadStrict<T>(Resolve(requestPath, path), options);
    }

    private static T ReadStrict<T>(string path, JsonSerializerOptions options)
    {
        Require(File.Exists(path), "input_missing:" + path);
        var json = File.ReadAllText(path);
        using (var document = JsonDocument.Parse(json))
        {
            RejectDuplicateKeys(document.RootElement, "$", path);
        }
        var value = JsonSerializer.Deserialize<T>(json, options);
        Require(value != null, "input_invalid:" + path);
        return value;
    }

    private static void RejectDuplicateKeys(JsonElement element, string locator, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), "duplicate_json_key:" + path + ":" + locator + "." + property.Name);
                RejectDuplicateKeys(property.Value, locator + "." + property.Name, path);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateKeys(item, locator + "[" + index + "]", path);
                index++;
            }
        }
    }

    private static string Resolve(string requestPath, string path)
    {
        Require(!string.IsNullOrWhiteSpace(path), "path_required");
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(requestPath), path));
    }

    private static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, options) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void AppendJournalReference(string path, MigrationExecutionArtifactReference reference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var bytes = Encoding.UTF8.GetBytes(ClassicWikiPackageSerializer.SerializeCanonical(reference) + Environment.NewLine);
        using (var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            output.Write(bytes, 0, bytes.Length);
            output.Flush(true);
        }
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var result = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = writeIndented
        };
        result.Converters.Add(new JsonStringEnumConverter());
        return result;
    }

    private static void ValidateImplementationRef(string implementationRef)
    {
        Require(implementationRef != null && implementationRef.Length == 40 && implementationRef.All(Uri.IsHexDigit),
            "implementation_ref_invalid");
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Require((informational ?? string.Empty).IndexOf(implementationRef, StringComparison.OrdinalIgnoreCase) >= 0,
            "binary_implementation_ref_mismatch");
    }

    private static string CurrentBinarySha256()
    {
        var path = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }
        using (var content = File.OpenRead(path))
        {
            return MigrationDigest.ComputeSha256(content);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}

internal sealed class NativeRuntimeRequest
{
    public string Schema { get; set; }
    public string CaseId { get; set; }
    public string ProfileId { get; set; }
    public string ImplementationRef { get; set; }
    public string PackagePath { get; set; }
    public string AdmittedPlanPath { get; set; }
    public string ReceiptAggregatePath { get; set; }
    public string BindingPath { get; set; }
    public string ExternalEvidencePath { get; set; }
    public string ArtifactStorePath { get; set; }
    public string ProducerBuildManifestPath { get; set; }
    public string VerificationJournalPath { get; set; }
    public string OutputDirectory { get; set; }
    public DateTimeOffset EvaluatedAtUtc { get; set; }
}
