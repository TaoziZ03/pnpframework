using Ccd263.RuntimeBrowser;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures");
var fixturePaths = Directory.GetFiles(fixtureDirectory, "*.json")
    .Where(value => Regex.IsMatch(Path.GetFileName(value), "^[0-9]{2}-"))
    .OrderBy(value => value, StringComparer.Ordinal)
    .ToList();
if (fixturePaths.Count == 0) throw new InvalidDataException("No CCD-272 fixtures were found.");
var printProvenance = args.Length == 1 && string.Equals(args[0], "--print-provenance", StringComparison.Ordinal);
var provenancePath = Path.Combine(fixtureDirectory, "fixture-provenance-manifest.json");
var provenance = printProvenance
    ? null
    : JsonSerializer.Deserialize<FixtureProvenanceManifest>(File.ReadAllText(provenancePath), TestJson.Options())
        ?? throw new InvalidDataException("Fixture provenance manifest is invalid.");
var provenanceByFile = provenance?.Fixtures.ToDictionary(value => value.File, StringComparer.Ordinal)
    ?? new Dictionary<string, FixtureProvenanceEntry>(StringComparer.Ordinal);
if (!printProvenance && (!string.Equals(provenance.SchemaVersion, "ccd272.browser-runtime-fixture-provenance/v1", StringComparison.Ordinal)
    || !string.Equals(provenance.SharedContractCommit, "93451dc5188cdf8e495102456d4195fdbb62c6c9", StringComparison.Ordinal)))
    throw new InvalidDataException("Fixture provenance manifest authority is stale.");
if (!printProvenance && provenanceByFile.Count != fixturePaths.Count)
    throw new InvalidDataException("Fixture provenance manifest coverage is stale.");

var passed = 0;
foreach (var fixturePath in fixturePaths)
{
    var fixtureFile = Path.GetFileName(fixturePath);
    var fixture = JsonSerializer.Deserialize<FixtureCase>(File.ReadAllText(fixturePath), TestJson.Options())
        ?? throw new InvalidDataException("Fixture is invalid: " + fixturePath);
    using var scenario = Scenario.Create(fixture);
    var actualProvenance = new FixtureProvenanceEntry
    {
        File = fixtureFile,
        RecipeDigestSha256 = TestJson.HashBytes(File.ReadAllBytes(fixturePath)),
        OriginDigestSha256 = scenario.OriginDigestSha256,
        InputDigestSha256 = scenario.InputDigestSha256
    };
    if (printProvenance)
    {
        Console.WriteLine(JsonSerializer.Serialize(actualProvenance, TestJson.Options()));
        continue;
    }
    if (!provenanceByFile.TryGetValue(fixtureFile, out var expectedProvenance)
        || !expectedProvenance.Equals(actualProvenance))
        throw new InvalidDataException("Fixture provenance is stale: " + fixtureFile);

    var inputState = scenario.CaptureInputState();
    Exception failure = null;
    try
    {
        var result = BrowserRuntimeEvidenceAdapter.EmitFromFile(scenario.RequestPath);
        if (!fixture.ExpectSuccess) throw new InvalidDataException("Expected rejection but emission succeeded.");
        if (!string.Equals(result.ResultKind, fixture.ExpectedResultKind, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected result kind: " + result.ResultKind);
        using var emitted = JsonDocument.Parse(File.ReadAllText(scenario.OutputPath));
        if (emitted.RootElement.TryGetProperty("runtimeVerificationStatus", out _)
            || emitted.RootElement.TryGetProperty("acceptanceStatus", out _))
            throw new InvalidDataException("External evidence asserted native authority fields.");
        scenario.ReopenPublishedEvidence();
    }
    catch (Exception exception)
    {
        failure = exception;
    }
    scenario.AssertInputState(inputState);

    if (fixture.ExpectSuccess && failure != null)
        throw new InvalidDataException(fixture.CaseId + " unexpectedly failed: " + failure.Message, failure);
    if (!fixture.ExpectSuccess && failure == null)
        throw new InvalidDataException(fixture.CaseId + " unexpectedly succeeded.");
    if (!fixture.ExpectSuccess && !string.IsNullOrWhiteSpace(fixture.ExpectedError)
        && failure.Message.IndexOf(fixture.ExpectedError, StringComparison.OrdinalIgnoreCase) < 0)
        throw new InvalidDataException(fixture.CaseId + " rejected for the wrong reason: " + failure.Message, failure);

    Console.WriteLine("PASS " + fixture.CaseId + (failure == null ? " emitted" : " rejected:" + failure.Message));
    passed++;
}

if (printProvenance) return;
Console.WriteLine($"CCD-272 fixtures passed: {passed}/{fixturePaths.Count}");

sealed class FixtureCase
{
    public string SchemaVersion { get; set; }
    public string CaseId { get; set; }
    public string Mode { get; set; }
    public string Mutation { get; set; }
    public bool ExpectSuccess { get; set; }
    public string ExpectedResultKind { get; set; }
    public string ExpectedError { get; set; }
}

sealed class FixtureProvenanceManifest
{
    public string SchemaVersion { get; set; }
    public string SharedContractCommit { get; set; }
    public IList<FixtureProvenanceEntry> Fixtures { get; set; } = new List<FixtureProvenanceEntry>();
}

sealed class FixtureProvenanceEntry : IEquatable<FixtureProvenanceEntry>
{
    public string File { get; set; }
    public string RecipeDigestSha256 { get; set; }
    public string OriginDigestSha256 { get; set; }
    public string InputDigestSha256 { get; set; }

    public bool Equals(FixtureProvenanceEntry other) => other != null
        && string.Equals(File, other.File, StringComparison.Ordinal)
        && string.Equals(RecipeDigestSha256, other.RecipeDigestSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(OriginDigestSha256, other.OriginDigestSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(InputDigestSha256, other.InputDigestSha256, StringComparison.OrdinalIgnoreCase);
}

sealed class Scenario : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("c19f82d4-a098-4f88-9e01-c608f0b68b6b");
    private static readonly Guid ReadbackOperationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RuntimeOperationId = Guid.Parse("77fa33e6-9814-434d-bd7f-919747fe3f60");
    private static readonly string ContractRef = "60f2e50e8d60670a75d4258afbb2934b7bb4d4c0";
    private const string EvidencePackageDigest = "40aef6bc9c8b443a93839cdc99b8f06d2e0980e2883625766ac3b81bdf032283";
    private readonly string root;
    private readonly string bindingPath;
    private readonly string admittedPlanPath;
    private readonly string artifactStorePath;

    private Scenario(
        string root,
        string requestPath,
        string outputPath,
        string bindingPath,
        string admittedPlanPath,
        string artifactStorePath,
        string originDigestSha256,
        string inputDigestSha256)
    {
        this.root = root;
        this.bindingPath = bindingPath;
        this.admittedPlanPath = admittedPlanPath;
        this.artifactStorePath = artifactStorePath;
        RequestPath = requestPath;
        OutputPath = outputPath;
        OriginDigestSha256 = originDigestSha256;
        InputDigestSha256 = inputDigestSha256;
    }

    public string RequestPath { get; }
    public string OutputPath { get; }
    public string OriginDigestSha256 { get; }
    public string InputDigestSha256 { get; }

    public static Scenario Create(FixtureCase fixture)
    {
        if (!string.Equals(fixture.SchemaVersion, "ccd272.browser-runtime-fixture/v1", StringComparison.Ordinal))
            throw new InvalidDataException("Fixture schema unsupported: " + fixture.CaseId);
        var root = Path.Combine(Path.GetTempPath(), "ccd272-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var storePath = Path.Combine(root, "artifacts");
        var store = new DirectoryMigrationArtifactStore(storePath);
        var target = CreateTarget();
        var sourceVersion = CreateSourceVersion();
        var operations = CreateOperations();
        var admittedPlan = new AdmittedReproExecutionPlan
        {
            PlanDigest = Hash("plan"),
            TargetIdentity = target.CanonicalUrl,
            SourceVersion = CopySourceVersion(sourceVersion),
            Operations = CopyOperations(operations)
        };
        var admittedPlanDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
            admittedPlan,
            admittedPlan.PlanDigest,
            admittedPlan.TargetIdentity);
        var importReceiptDigest = Hash("import-receipt");
        const string authoredContent = "synthetic wiki";
        var bindingTargetEvidence = CreateTargetEvidence(
            store,
            target,
            "binding-readback",
            BaseTime.AddMinutes(2),
            ReadbackOperationId,
            importReceiptDigest);
        var manifest = NativePageRuntimeBindingValidator.CreateClassicWikiManifest();
        var binding = new NativePageRuntimeBinding
        {
            RunId = RunId,
            ClaimId = NativePageRuntimeContract.ClaimId,
            Subject = new NativePageRuntimeSubject
            {
                IngredientId = "node:runtime",
                PageIngredientKind = "Runtime",
                Subtype = "runtime.page",
                SemanticRole = "runtime.wiki acceptance binding",
                PrimaryOwnerLane = "shared integration"
            },
            ProfileId = NativePageRuntimeContract.ClassicWikiProfile,
            PolicyVersion = NativePageRuntimeContract.ClassicWikiPolicyVersion,
            SourceIdentity = new NativePageRuntimeSourceIdentity
            {
                SiteId = Guid.Parse("c37b3679-0000-0000-0000-000000000001"),
                WebId = Guid.Parse("041e70b3-0000-0000-0000-000000000001"),
                ListId = Guid.Parse("3fed0145-0000-0000-0000-000000000001"),
                ListItemId = 2,
                FileUniqueId = Guid.Parse("c3b2c2bb-663d-47ed-8562-840c9fd685fb"),
                PageServerRelativeUrl = "/sites/ccd/source/SitePages/wiki.aspx"
            },
            SourceVersion = CopySourceVersion(sourceVersion),
            SnapshotDigestSha256 = "518fb815a78d760eeb709ad4ba8008657ce8319e41d3baff0096debb5c56b76a",
            PlanDigest = admittedPlan.PlanDigest,
            AdmittedPlanDigestSha256 = admittedPlanDigest,
            ImportReceiptDigestSha256 = importReceiptDigest,
            Operations = CopyOperations(operations),
            TargetStorageIdentity = target,
            TargetIdentityEvidence = bindingTargetEvidence,
            RequirementsManifest = manifest,
            RequirementsManifestDigestSha256 = MigrationDigest.ComputeSha256(ClassicWikiPackageSerializer.SerializeCanonical(manifest)),
            ExpectedAuthoredContentSha256 = Hash(authoredContent),
            IssuedAtUtc = BaseTime.AddMinutes(2),
            CaptureNotBeforeUtc = BaseTime.AddMinutes(2),
            CaptureExpiresAtUtc = BaseTime.AddHours(1),
            ImportProducerRef = ContractRef,
            ContractProducerRef = ContractRef,
            ContractProducerProvenanceManifestDigestSha256 = Hash("provenance"),
            ContractProducerProvenanceStatus = "Unverified"
        };
        NativePageRuntimeBindingValidator.SealBinding(binding);
        var pre = CreateTargetEvidence(
            store,
            target,
            "pre",
            BaseTime.AddMinutes(3),
            RuntimeOperationId,
            binding.ContentSha256);
        var post = CreateTargetEvidence(
            store,
            target,
            "post",
            BaseTime.AddMinutes(5),
            RuntimeOperationId,
            binding.ContentSha256);

        var html = Put(store, Encoding.UTF8.GetBytes("<html><body class=classic-wiki>" + authoredContent + "</body></html>"), "text/html", "page.html");
        var dom = Put(store, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = "pnp-classic-wiki-runtime-dom/v1",
            surface = "classic-wiki",
            readyState = "complete",
            errorShell = false,
            observedUrl = target.CanonicalUrl,
            authoredContent
        })), "application/json", "dom.json");
        var screenshot = Put(store, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="), "image/png", "page.png");

        var request = new BrowserRuntimeAdapterRequest
        {
            SchemaVersion = BrowserRuntimeEvidenceAdapter.RequestSchemaVersion,
            BindingPath = "binding.json",
            AdmittedPlanPath = "admitted-plan.json",
            ArtifactStorePath = "artifacts",
            OutputPath = "external-runtime-evidence.json",
            Expected = new BrowserRuntimeExpectedIdentity
            {
                BindingDigestSha256 = binding.ContentSha256,
                SourceVersionDigestSha256 = binding.SourceVersion.VersionDigestSha256,
                AdmittedPlanDigestSha256 = binding.AdmittedPlanDigestSha256,
                ImportReceiptDigestSha256 = binding.ImportReceiptDigestSha256,
                RuntimeOperationId = RuntimeOperationId,
                TargetFileUniqueId = target.FileUniqueId,
                TargetListItemId = target.ListItemId,
                TargetListItemVersion = target.ListItemVersion,
                TargetCanonicalUrl = target.CanonicalUrl
            },
            CaptureProducer = new NativePageRuntimeCaptureProducer
            {
                AdapterId = BrowserRuntimeEvidenceAdapter.AdapterId,
                Version = BrowserRuntimeEvidenceAdapter.AdapterVersion,
                ImplementationRef = AdapterImplementationRef(),
                ToolVersion = "Microsoft Edge 140.0.3485.54",
                ProtocolVersion = "CDP 1.3"
            },
            StartedAtUtc = BaseTime.AddMinutes(4),
            CompletedAtUtc = BaseTime.AddMinutes(4).AddSeconds(5),
            ObservedTargetIdentity = CopyTarget(target),
            PreCaptureTargetReadback = pre,
            PostCaptureTargetReadback = post,
            Attempts = new List<NativePageRuntimeAttempt> { CreateAttempt(target, html, dom, screenshot) },
            BrowserContext = new RuntimeBrowserContextIdentity
            {
                BrowserProduct = "Microsoft Edge",
                BrowserVersion = "140.0.3485.54",
                ProtocolVersion = "1.3",
                ProfileIdentitySha256 = Hash("ephemeral-profile"),
                BrowserContextId = "context-ccd272-1",
                TargetId = "target-ccd272-1",
                IsIncognito = true,
                FreshContext = true,
                CreatedAtUtc = BaseTime.AddMinutes(3).AddSeconds(55),
                FirstNavigationAtUtc = BaseTime.AddMinutes(4).AddSeconds(1)
            },
            Results = manifest.Requirements.Select(requirement => CreateResult(requirement.Id, target, html, dom, screenshot)).ToList(),
            Extensions = new Dictionary<string, string>
            {
                ["ccd272.originEvidencePackageDigest"] = EvidencePackageDigest
            }
        };

        ApplyMode(request, fixture.Mode, store, html, dom, screenshot);
        if (string.Equals(fixture.Mode, "terminal-access-200", StringComparison.Ordinal))
        {
            using var denialStream = store.OpenRead(request.Attempts[0].RawEvidence[0].Sha256);
            using var denialReader = new StreamReader(denialStream, Encoding.UTF8);
            if (denialReader.ReadToEnd().IndexOf("Access Denied", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidDataException("The semantic HTTP-200 denial fixture lacks denial-body evidence.");
        }
        ApplyMutation(fixture.Mutation, request, binding, admittedPlan, root, storePath, html, dom, screenshot);
        var options = TestJson.Options(true);
        var bindingPath = Path.Combine(root, "binding.json");
        File.WriteAllText(bindingPath, JsonSerializer.Serialize(binding, options));
        var admittedPlanPath = Path.Combine(root, "admitted-plan.json");
        File.WriteAllText(admittedPlanPath, JsonSerializer.Serialize(admittedPlan, options));
        var requestPath = Path.Combine(root, "request.json");
        var requestNode = JsonNode.Parse(JsonSerializer.Serialize(request, options)).AsObject();
        if (string.Equals(fixture.Mutation, "native-authority-field", StringComparison.Ordinal))
            requestNode["acceptanceStatus"] = "Accepted";
        File.WriteAllText(requestPath, requestNode.ToJsonString(options));
        var originDigest = TestJson.HashObject(new
        {
            runId = RunId,
            claimId = NativePageRuntimeContract.ClaimId,
            sourceFileUniqueId = BrowserRuntimeEvidenceAdapter.ClaimSourceFileUniqueId,
            sourceVersionDigestSha256 = "7c0ed8c29e1a81c925f93bca41affa5035116f444af278a35c62ca807045f8d5",
            snapshotDigestSha256 = "518fb815a78d760eeb709ad4ba8008657ce8319e41d3baff0096debb5c56b76a",
            evidencePackageDigestSha256 = EvidencePackageDigest,
            runtimeOperationId = RuntimeOperationId,
            targetFileUniqueId = target.FileUniqueId,
            targetCanonicalUrl = target.CanonicalUrl
        });
        var stableRequestNode = requestNode.DeepClone().AsObject();
        stableRequestNode["captureProducer"]["implementationRef"] = "<adapter-commit-bound-at-build>";
        var inputDigest = TestJson.HashObject(new
        {
            binding = JsonNode.Parse(File.ReadAllText(bindingPath)),
            admittedPlan = JsonNode.Parse(File.ReadAllText(admittedPlanPath)),
            request = stableRequestNode,
            artifacts = ArtifactState(storePath)
        });
        return new Scenario(
            root,
            requestPath,
            Path.GetFullPath(Path.Combine(root, request.OutputPath)),
            bindingPath,
            admittedPlanPath,
            storePath,
            originDigest,
            inputDigest);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    public IReadOnlyDictionary<string, string> CaptureInputState()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in new[] { RequestPath, bindingPath, admittedPlanPath })
            result[Path.GetRelativePath(root, path)] = TestJson.HashBytes(File.ReadAllBytes(path));
        foreach (var path in Directory.GetFiles(artifactStorePath, "*", SearchOption.AllDirectories)
                     .OrderBy(value => value, StringComparer.Ordinal))
            result[Path.GetRelativePath(root, path)] = TestJson.HashBytes(File.ReadAllBytes(path));
        return result;
    }

    public void AssertInputState(IReadOnlyDictionary<string, string> expected)
    {
        var actual = CaptureInputState();
        if (expected.Count != actual.Count || expected.Any(pair =>
                !actual.TryGetValue(pair.Key, out var digest)
                || !string.Equals(pair.Value, digest, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Adapter mutated a read-only input or artifact-store object.");
    }

    public void ReopenPublishedEvidence()
    {
        var binding = JsonSerializer.Deserialize<NativePageRuntimeBinding>(File.ReadAllText(bindingPath), TestJson.Options())
            ?? throw new InvalidDataException("Published evidence binding reopen failed.");
        var evidence = JsonSerializer.Deserialize<ExternalPageRuntimeEvidence>(File.ReadAllText(OutputPath), TestJson.Options())
            ?? throw new InvalidDataException("Published external evidence reopen failed.");
        var store = new DirectoryMigrationArtifactStore(artifactStorePath);
        var policy = new ClassicWikiRuntimeEvidencePolicy();
        policy.ValidateBinding(binding, store);
        NativePageRuntimeBindingValidator.ValidateExternalEvidenceAndComputeDigest(
            evidence,
            binding,
            policy,
            store,
            out var semanticStatus);
        if (evidence.RuntimeReceipt != null && semanticStatus != evidence.RuntimeReceipt.Status)
            throw new InvalidDataException("Published runtime receipt semantic status changed on reopen.");
        if (evidence.TerminalObservation != null && semanticStatus != RuntimeVerificationStatus.Failed)
            throw new InvalidDataException("Published terminal observation was not degraded on reopen.");
    }

    private static void ApplyMode(
        BrowserRuntimeAdapterRequest request,
        string mode,
        IMigrationArtifactStore store,
        ArtifactReference html,
        ArtifactReference dom,
        ArtifactReference screenshot)
    {
        if (string.Equals(mode, "positive", StringComparison.Ordinal)) return;
        request.Results = new List<RuntimeVerificationResult>();
        request.BrowserContext = null;
        if (string.Equals(mode, "terminal-access", StringComparison.Ordinal))
        {
            request.Attempts[0].HttpStatusCode = 403;
            request.Attempts[0].SemanticResult = "access_denied";
            request.TerminalObservation = new NativePageRuntimeTerminalObservation
            {
                Kind = "access-denied",
                HttpStatusCode = 403,
                SemanticDetectorResult = "access_denied",
                ReasonCode = "ACCESS_DENIED_SKIPPED",
                Message = "Synthetic per-instance denial"
            };
            return;
        }
        if (string.Equals(mode, "terminal-access-401", StringComparison.Ordinal))
        {
            request.Attempts[0].HttpStatusCode = 401;
            request.Attempts[0].SemanticResult = "unauthorized";
            request.TerminalObservation = new NativePageRuntimeTerminalObservation
            {
                Kind = "access-denied",
                HttpStatusCode = 401,
                SemanticDetectorResult = "unauthorized",
                ReasonCode = "ACCESS_DENIED_SKIPPED",
                Message = "Synthetic per-instance 401 denial"
            };
            return;
        }
        if (string.Equals(mode, "terminal-access-200", StringComparison.Ordinal))
        {
            var denial = Put(
                store,
                Encoding.UTF8.GetBytes("<html><body><main>Access Denied</main></body></html>"),
                "text/html",
                "access-denied.html");
            request.Attempts[0].RawEvidence[0] = Native(denial, "artifacts/access-denied.html");
            request.Attempts[0].HttpStatusCode = 200;
            request.Attempts[0].SemanticResult = "access_denied";
            request.TerminalObservation = new NativePageRuntimeTerminalObservation
            {
                Kind = "access-denied",
                HttpStatusCode = 200,
                SemanticDetectorResult = "access_denied",
                ReasonCode = "ACCESS_DENIED_SKIPPED",
                Message = "Synthetic HTTP 200 denial body"
            };
            return;
        }
        if (string.Equals(mode, "terminal-transport", StringComparison.Ordinal))
        {
            request.Attempts[0].HttpStatusCode = null;
            request.Attempts[0].TransportUnavailable = true;
            request.Attempts[0].SemanticResult = "transport_unavailable";
            request.TerminalObservation = new NativePageRuntimeTerminalObservation
            {
                Kind = "transport-unavailable",
                SemanticDetectorResult = "transport_unavailable",
                ReasonCode = "TRANSPORT_UNAVAILABLE",
                Message = "Synthetic transport failure"
            };
            return;
        }
        if (string.Equals(mode, "terminal-detector", StringComparison.Ordinal))
        {
            request.Attempts[0].SemanticResult = "http_error";
            request.TerminalObservation = new NativePageRuntimeTerminalObservation
            {
                Kind = "semantic-detector-failure",
                HttpStatusCode = 200,
                SemanticDetectorResult = "http_error",
                ReasonCode = "SEMANTIC_DETECTOR_FAILED",
                Message = "Synthetic detector failure"
            };
            return;
        }
        throw new InvalidDataException("Unsupported fixture mode: " + mode);
    }

    private static void ApplyMutation(
        string mutation,
        BrowserRuntimeAdapterRequest request,
        NativePageRuntimeBinding binding,
        AdmittedReproExecutionPlan admittedPlan,
        string root,
        string storePath,
        ArtifactReference html,
        ArtifactReference dom,
        ArtifactReference screenshot)
    {
        switch (mutation)
        {
            case "none": return;
            case "stale-binding": binding.SourceVersion.VersionLabel = "4.0"; return;
            case "source-version": request.Expected.SourceVersionDigestSha256 = Hash("stale-source"); return;
            case "admitted-plan": request.Expected.AdmittedPlanDigestSha256 = Hash("wrong-plan"); return;
            case "import-receipt": request.Expected.ImportReceiptDigestSha256 = Hash("wrong-import"); return;
            case "operation": request.Expected.RuntimeOperationId = Guid.Parse("99999999-9999-9999-9999-999999999999"); return;
            case "target": request.Expected.TargetFileUniqueId = Guid.Parse("4881af85-37b6-4ae7-b307-df93c52994a8"); return;
            case "url-mismatch":
                request.Attempts[0].FinalUrl += "?redirected=1";
                foreach (var result in request.Results) result.Http.FinalUrl = request.Attempts[0].FinalUrl;
                return;
            case "cache": foreach (var result in request.Results) result.Cache.RequestMode = "default"; return;
            case "service-worker": foreach (var result in request.Results) result.Cache.FromServiceWorker = true; return;
            case "fresh-context": request.BrowserContext.FreshContext = false; return;
            case "html-bytes": Corrupt(storePath, html.Sha256); return;
            case "dom-bytes": Corrupt(storePath, dom.Sha256); return;
            case "screenshot-bytes": Corrupt(storePath, screenshot.Sha256); return;
            case "semantic-detector": request.Attempts[0].SemanticResult = "http_error"; return;
            case "request-id-with-reason":
                request.Attempts[0].RequestId = null;
                request.Attempts[0].RequestIdAvailability = "unavailable";
                request.Attempts[0].RequestIdUnavailableReason = "response_header_absent";
                foreach (var result in request.Results) result.Http.RequestId = null;
                return;
            case "request-id-without-reason":
                request.Attempts[0].RequestId = null;
                request.Attempts[0].RequestIdAvailability = "unavailable";
                request.Attempts[0].RequestIdUnavailableReason = null;
                return;
            case "unknown-schema": request.SchemaVersion = "ccd272.browser-runtime-evidence-request/v2"; return;
            case "unknown-profile":
                binding.ProfileId = "classic-wiki.unknown/v2";
                NativePageRuntimeBindingValidator.SealBinding(binding);
                request.Expected.BindingDigestSha256 = binding.ContentSha256;
                return;
            case "unsupported-policy-terminal":
                binding.PolicyVersion = "unsupported-policy/v999";
                NativePageRuntimeBindingValidator.SealBinding(binding);
                request.Expected.BindingDigestSha256 = binding.ContentSha256;
                return;
            case "missing-admitted-plan-digest":
                binding.AdmittedPlanDigestSha256 = null;
                request.Expected.AdmittedPlanDigestSha256 = null;
                NativePageRuntimeBindingValidator.SealBinding(binding);
                request.Expected.BindingDigestSha256 = binding.ContentSha256;
                return;
            case "missing-snapshot-digest":
                binding.SnapshotDigestSha256 = null;
                NativePageRuntimeBindingValidator.SealBinding(binding);
                request.Expected.BindingDigestSha256 = binding.ContentSha256;
                return;
            case "foreign-snapshot-digest":
                binding.SnapshotDigestSha256 = Hash("foreign-snapshot");
                NativePageRuntimeBindingValidator.SealBinding(binding);
                request.Expected.BindingDigestSha256 = binding.ContentSha256;
                return;
            case "missing-origin-package": request.Extensions.Remove("ccd272.originEvidencePackageDigest"); return;
            case "foreign-origin-package":
                request.Extensions["ccd272.originEvidencePackageDigest"] = Hash("foreign-origin-package");
                return;
            case "foreign-source-file":
                binding.SourceIdentity.FileUniqueId = Guid.Parse("c3b2c2bb-663d-47ed-8562-840c9fd685fa");
                NativePageRuntimeBindingValidator.SealBinding(binding);
                request.Expected.BindingDigestSha256 = binding.ContentSha256;
                return;
            case "admitted-plan-source-lineage":
                admittedPlan.SourceVersion.VersionLabel = "4.0";
                var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                    admittedPlan,
                    admittedPlan.PlanDigest,
                    admittedPlan.TargetIdentity);
                binding.AdmittedPlanDigestSha256 = admittedDigest;
                request.Expected.AdmittedPlanDigestSha256 = admittedDigest;
                NativePageRuntimeBindingValidator.SealBinding(binding);
                request.Expected.BindingDigestSha256 = binding.ContentSha256;
                return;
            case "output-alias-binding": request.OutputPath = request.BindingPath; return;
            case "output-alias-admitted-plan": request.OutputPath = request.AdmittedPlanPath; return;
            case "output-alias-request": request.OutputPath = "request.json"; return;
            case "output-alias-artifact":
                request.OutputPath = Path.GetRelativePath(root, Path.Combine(storePath, html.Sha256[..2], html.Sha256));
                return;
            case "output-inside-artifact-store": request.OutputPath = "artifacts/external-runtime-evidence.json"; return;
            case "missing-admitted-plan-file": request.AdmittedPlanPath = "missing-admitted-plan.json"; return;
            case "native-authority-field": return;
            case "too-many-attempts":
                var original = request.Attempts[0];
                request.Attempts = Enumerable.Range(1, 4).Select(index => new NativePageRuntimeAttempt
                {
                    Sequence = index,
                    MaximumAttempts = 4,
                    ObservedAtUtc = original.ObservedAtUtc,
                    RunId = original.RunId,
                    RuntimeOperationId = original.RuntimeOperationId,
                    RequestedUrl = original.RequestedUrl,
                    FinalUrl = original.FinalUrl,
                    HttpStatusCode = original.HttpStatusCode,
                    TransportUnavailable = original.TransportUnavailable,
                    RequestId = original.RequestId,
                    RequestIdAvailability = original.RequestIdAvailability,
                    ProviderId = original.ProviderId,
                    ProviderVersion = original.ProviderVersion,
                    SemanticDetectorId = original.SemanticDetectorId,
                    SemanticDetectorVersion = original.SemanticDetectorVersion,
                    SemanticDetectorDigestSha256 = original.SemanticDetectorDigestSha256,
                    SemanticResult = original.SemanticResult,
                    BrowserContextId = original.BrowserContextId,
                    RawEvidence = original.RawEvidence
                }).ToList();
                return;
            default: throw new InvalidDataException("Unsupported fixture mutation: " + mutation);
        }
    }

    private static NativePageRuntimeAttempt CreateAttempt(
        NativePageRuntimeTargetIdentity target,
        ArtifactReference html,
        ArtifactReference dom,
        ArtifactReference screenshot) => new()
    {
        Sequence = 1,
        MaximumAttempts = 1,
        ObservedAtUtc = BaseTime.AddMinutes(4).AddSeconds(2),
        RunId = RunId,
        RuntimeOperationId = RuntimeOperationId,
        RequestedUrl = target.CanonicalUrl,
        FinalUrl = target.CanonicalUrl,
        HttpStatusCode = 200,
        TransportUnavailable = false,
        RequestId = "request-ccd272-1",
        RequestIdAvailability = "available",
        ProviderId = "edge-cdp",
        ProviderVersion = "140.0.3485.54",
        SemanticDetectorId = "classic-wiki-surface-detector",
        SemanticDetectorVersion = "1.0.0",
        SemanticDetectorDigestSha256 = Hash("detector-v1"),
        SemanticResult = "surface_present",
        BrowserContextId = "context-ccd272-1",
        RawEvidence = new List<NativePageRuntimeArtifactReference>
        {
            Native(html, "artifacts/page.html"),
            Native(dom, "artifacts/dom.json"),
            Native(screenshot, "artifacts/page.png")
        }
    };

    private static RuntimeVerificationResult CreateResult(
        string requirementId,
        NativePageRuntimeTargetIdentity target,
        ArtifactReference html,
        ArtifactReference dom,
        ArtifactReference screenshot) => new()
    {
        RequirementId = requirementId,
        Passed = true,
        EvidenceArtifactSha256 = html.Sha256,
        EvidenceArtifactLength = html.Length,
        EvidenceArtifactLocator = "artifacts/page.html",
        ImplementationRef = ContractRef,
        BrowserContextId = "context-ccd272-1",
        Http = new RuntimeHttpEvidence
        {
            RequestedUrl = target.CanonicalUrl,
            FinalUrl = target.CanonicalUrl,
            Method = "GET",
            StatusCode = 200,
            ContentType = "text/html; charset=utf-8",
            RequestId = "request-ccd272-1",
            SharePointRequestGuid = "sprequestguid-ccd272-1",
            ResponseHeadersDigestSha256 = Hash("headers"),
            EncodedDataLength = html.Length,
            CapturedAtUtc = BaseTime.AddMinutes(4).AddSeconds(2)
        },
        Cache = new RuntimeCacheEvidence
        {
            RequestMode = "no-store",
            CacheDisabled = true,
            RequestCacheControl = "no-store",
            RequestPragma = "no-cache",
            ResponseCacheControl = "private",
            FromDiskCache = false,
            FromServiceWorker = false
        },
        DomProbeArtifactSha256 = dom.Sha256,
        DomProbeArtifactLength = dom.Length,
        DomProbeArtifactLocator = "artifacts/dom.json",
        ScreenshotArtifactSha256 = screenshot.Sha256,
        ScreenshotArtifactLength = screenshot.Length,
        ScreenshotArtifactLocator = "artifacts/page.png",
        Message = "Synthetic exact binding"
    };

    private static CurrentSourceVersionIdentity CreateSourceVersion() => new()
    {
        IdentityDigestSha256 = Hash("source-identity"),
        VersionDigestSha256 = "7c0ed8c29e1a81c925f93bca41affa5035116f444af278a35c62ca807045f8d5",
        ETag = "\"source,3\"",
        LastModifiedUtc = BaseTime,
        VersionLabel = "3.0",
        ObservedAtUtc = BaseTime.AddMinutes(1)
    };

    private static ReproOperationIds CreateOperations() => new()
    {
        MutationOperationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ReadbackOperationId = ReadbackOperationId,
        RuntimeOperationId = RuntimeOperationId,
        CleanupOperationId = Guid.Parse("44444444-4444-4444-4444-444444444444")
    };

    private static CurrentSourceVersionIdentity CopySourceVersion(CurrentSourceVersionIdentity value) => new()
    {
        IdentityDigestSha256 = value.IdentityDigestSha256,
        VersionDigestSha256 = value.VersionDigestSha256,
        ETag = value.ETag,
        LastModifiedUtc = value.LastModifiedUtc,
        VersionLabel = value.VersionLabel,
        ObservedAtUtc = value.ObservedAtUtc
    };

    private static ReproOperationIds CopyOperations(ReproOperationIds value) => new()
    {
        MutationOperationId = value.MutationOperationId,
        ReadbackOperationId = value.ReadbackOperationId,
        RuntimeOperationId = value.RuntimeOperationId,
        CleanupOperationId = value.CleanupOperationId
    };

    private static object ArtifactState(string storePath) => Directory.GetFiles(storePath, "*", SearchOption.AllDirectories)
        .OrderBy(value => value, StringComparer.Ordinal)
        .Select(value => new
        {
            locator = Path.GetRelativePath(storePath, value).Replace(Path.DirectorySeparatorChar, '/'),
            length = new FileInfo(value).Length,
            actualDigestSha256 = TestJson.HashBytes(File.ReadAllBytes(value))
        })
        .ToList();

    private static NativePageRuntimeTargetIdentity CreateTarget()
    {
        const string path = "/sites/ccd272/SitePages/wiki.aspx";
        return new NativePageRuntimeTargetIdentity
        {
            SiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WebId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ListId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            WebUrl = "https://a830edad9050849cupcollect.sharepoint.com/sites/ccd272",
            PageServerRelativeUrl = path,
            FileUniqueId = Guid.Parse("4881af85-37b6-4ae7-b307-df93c52994a7"),
            ListItemId = 2,
            ListItemVersion = "3.0",
            ListItemETag = "\"target,3\"",
            CanonicalUrl = "https://a830edad9050849cupcollect.sharepoint.com" + path
        };
    }

    private static NativePageRuntimeTargetIdentityEvidence CreateTargetEvidence(
        IMigrationArtifactStore store,
        NativePageRuntimeTargetIdentity target,
        string id,
        DateTimeOffset observedAt,
        Guid operationId,
        string sourceArtifactSha256) => new()
    {
        ObservationId = id,
        ObservedAtUtc = observedAt,
        OperationId = operationId,
        ProviderId = "synthetic-storage-readback",
        ProviderVersion = "1.0.0",
        SourceArtifactSha256 = sourceArtifactSha256,
        Identity = CopyTarget(target),
        Artifact = NativePageRuntimeBindingValidator.PutCanonicalArtifact(
            target,
            store,
            "application/vnd.pnp.target-identity+json")
    };

    private static NativePageRuntimeTargetIdentity CopyTarget(NativePageRuntimeTargetIdentity value) =>
        NativePageRuntimeBindingValidator.CopyTarget(value);

    private static ArtifactReference Put(
        IMigrationArtifactStore store,
        byte[] bytes,
        string mediaType,
        string name)
    {
        using var content = new MemoryStream(bytes, writable: false);
        return store.Put(content, mediaType, name);
    }

    private static NativePageRuntimeArtifactReference Native(ArtifactReference value, string locator) => new()
    {
        Sha256 = value.Sha256,
        Length = value.Length,
        MediaType = value.MediaType,
        Locator = locator
    };

    private static void Corrupt(string storePath, string digest)
    {
        var path = Path.Combine(storePath, digest[..2], digest);
        File.AppendAllText(path, "altered");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string AdapterImplementationRef()
    {
        var informational = typeof(BrowserRuntimeEvidenceAdapter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty;
        var candidates = Regex.Matches(informational, "(?<![0-9a-fA-F])[0-9a-fA-F]{40}(?![0-9a-fA-F])")
            .Select(value => value.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count != 1)
            throw new InvalidDataException("The adapter test binary is not bound to a full Git SHA.");
        return candidates[0].ToLowerInvariant();
    }
}

static class TestJson
{
    public static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string HashObject<T>(T value) =>
        HashBytes(JsonSerializer.SerializeToUtf8Bytes(value, Options()));

    public static JsonSerializerOptions Options(bool indented = false)
    {
        var result = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = indented
        };
        result.Converters.Add(new JsonStringEnumConverter());
        return result;
    }
}
