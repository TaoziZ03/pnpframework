using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Assessment.Maturity.External;
using PnP.Framework.Migration.Pages.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static PnP.Framework.Migration.Pages.Assessment.Maturity.External.ExternalEvidenceJson;

namespace PnP.Framework.Migration.Pages.Publishing.Comparison
{
    public static partial class PublishingPageCompareReconciler
    {
        // This bounded v3 mode reports a terminal observation, NOT a successful
        // page import, value-fidelity comparison, plan admission or maturity.
        // The absent native cleanup receipt is an input fact, never synthesized.
        internal static PublishingPageCompareReport ReconcileExternalTerminal(
            PublishingPageCompareRequest request,
            ExternalTerminalCompareAdmission admission,
            IMigrationArtifactStore artifactStore)
        {
            ValidateExternalAdmission(request, admission);
            var evidence = request.ExternalEvidence;
            var binding = admission.Binding;
            var manifest = ReadExternal(evidence.Manifest, admission.ManifestSha256,
                ExternalTerminalCompareContract.ManifestSchema, artifactStore);
            var readback = ReadExternal(evidence.FreshReadback, admission.FreshReadbackSha256,
                IngredientExternalEvidenceContract.LifecycleReceiptSchema, artifactStore);
            var browser = ReadExternal(evidence.BrowserReview, admission.BrowserReviewSha256,
                ExternalTerminalCompareContract.BrowserSchema, artifactStore);
            var unavailable = ReadExternal(evidence.CleanupUnavailable, admission.CleanupUnavailableSha256,
                ExternalTerminalCompareContract.CleanupUnavailableSchema, artifactStore);
            var postCleanup = ReadExternal(evidence.PostCleanup, admission.PostCleanupSha256,
                IngredientExternalEvidenceContract.LifecycleReceiptSchema, artifactStore);

            ValidateExternalManifest(manifest, admission);
            var execution = Property(manifest, "execution");
            SharedTargetLifecycleEvidenceAdapter.ValidateReceiptBinding(readback, "fresh-readback", binding, execution);
            SharedTargetLifecycleEvidenceAdapter.ValidateReceiptBinding(postCleanup, "post-cleanup", binding, execution);
            var readbackAt = ValidateExternalReadback(readback, admission);
            var failedGates = ValidateExternalBrowser(browser, admission, readbackAt);
            var browserAt = Time(browser, "observedAtUtc");
            ValidateExternalCleanup(postCleanup, unavailable, admission, browserAt);

            var failed = failedGates.Count > 0;
            var runtimeStatus = failed ? "failed" : "passed";
            var artifactBindings = new[]
            {
                ExternalArtifactBinding("manifest", evidence.Manifest, ExternalTerminalCompareContract.ManifestSchema),
                ExternalArtifactBinding("fresh-readback", evidence.FreshReadback, IngredientExternalEvidenceContract.LifecycleReceiptSchema),
                ExternalArtifactBinding("browser-runtime", evidence.BrowserReview, ExternalTerminalCompareContract.BrowserSchema),
                ExternalArtifactBinding("cleanup-unavailable", evidence.CleanupUnavailable, ExternalTerminalCompareContract.CleanupUnavailableSchema),
                ExternalArtifactBinding("post-cleanup", evidence.PostCleanup, IngredientExternalEvidenceContract.LifecycleReceiptSchema)
            }.OrderBy(value => value.Role, StringComparer.Ordinal).ToList();
            var reasons = new SortedSet<string>(StringComparer.Ordinal)
            {
                "NATIVE_CLEANUP_RECEIPT_UNAVAILABLE",
                "compare.external.single-ingredient-terminal",
                "compare.external.plan.reference-only",
                "compare.external.domain-comparison.not-run"
            };
            if (failed)
            {
                reasons.Add("BROWSER_RUNTIME_FAIL");
                reasons.Add(PublishingPageCompareContract.ReasonCodes.RuntimeFailed);
            }
            var report = new PublishingPageCompareReport
            {
                SchemaVersion = PublishingPageCompareContract.ExternalTerminalSchemaVersion,
                GeneratedAtUtc = request.GeneratedAtUtc,
                Producer = request.Producer,
                // No Assessment/export/package/source-snapshot bytes were
                // supplied here. Null means unavailable, not an invented pin.
                Bindings = new CompareBindings
                {
                    ManifestDigestSha256 = evidence.Manifest.Sha256,
                    PlanDigestSha256 = binding.PlanDigest,
                    ImportReceiptSchemaVersion = IngredientExternalEvidenceContract.LifecycleReceiptSchema,
                    ImportReceiptDigestSha256 = evidence.FreshReadback.Sha256,
                    RuntimeReceiptSchemaVersion = ExternalTerminalCompareContract.BrowserSchema,
                    RuntimeReceiptDigestSha256 = evidence.BrowserReview.Sha256
                },
                SourceVersionComparison = new SourceVersionComparison { Status = "not-checked" },
                TargetIdentity = new CompareTargetIdentity
                {
                    WebUrlHashSha256 = MigrationDigest.ComputeSha256(binding.TargetOrigin + binding.TargetWebPath),
                    PageServerRelativeUrlHashSha256 = MigrationDigest.ComputeSha256(binding.TargetPath),
                    FileUniqueId = Guid.Parse(admission.Target.FileUniqueId),
                    ListItemId = admission.Target.ItemId,
                    CanonicalIdentity = binding.TargetOrigin + binding.TargetPath
                },
                // Identity readback is verified below, but does not certify
                // any lane's content/layout/value comparison. Keep that layer out.
                Storage = new CompareStorageSummary { Status = "not-run", FreshReadback = true },
                Runtime = new CompareRuntimeSummary { Status = runtimeStatus },
                Ingredients = new List<IngredientCompareResult>
                {
                    new IngredientCompareResult
                    {
                        IngredientId = binding.Identity.IngredientId,
                        Kind = binding.Identity.Kind.Value.ToString(),
                        Material = true,
                        Lineage = new IngredientCompareLineage
                        {
                            RequestedUrlHashSha256 = MigrationDigest.ComputeSha256(binding.SourcePage.PageUrl),
                            SourceIngredientId = binding.Identity.IngredientId,
                            ActionId = "ccd143-action-fresh-readback-" + binding.NativeOperationId,
                            TargetIdentity = binding.TargetOrigin + binding.TargetPath,
                            PlanDigestSha256 = binding.PlanDigest,
                            TargetEvidenceDigestSha256 = evidence.FreshReadback.Sha256,
                            EvidenceRefs = artifactBindings.Select(value => value.Role + "#sha256=" + value.RawDigestSha256).ToList()
                        },
                        TargetEvidenceState = IngredientTargetEvidenceStates.Partial,
                        ObservedAtUtc = browserAt,
                        // External failed gate names are recorded in the
                        // summary, not relabeled as native runtime requirement IDs.
                        ResultClass = failed ? PublishingPageCompareContract.ResultClasses.Mismatch
                            : PublishingPageCompareContract.ResultClasses.Unknown,
                        ReasonCode = failed ? PublishingPageCompareContract.ReasonCodes.RuntimeRequirementFailed
                            : PublishingPageCompareContract.ReasonCodes.EvidenceIncomplete,
                        Message = "Terminal runtime observation only; domain fidelity and native cleanup are not certified."
                    }
                },
                Acceptance = new CompareAcceptance
                {
                    // An observed runtime failure outranks missing domain
                    // evidence. This new mode can never emit pass/unverified.
                    Verdict = failed ? "fail" : "conditional",
                    ReasonCodes = reasons.ToList(),
                    StorageStatus = "not-run",
                    RuntimeStatus = runtimeStatus
                },
                ExternalEvidence = new CompareExternalEvidenceSummary
                {
                    Scope = ExternalTerminalCompareContract.Scope,
                    AdmissionDigestSha256 = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(admission)),
                    ClaimId = binding.Identity.ClaimId,
                    Subtype = binding.Identity.Subtype,
                    SemanticRole = binding.Identity.SemanticRole,
                    PrimaryOwnerLane = binding.Identity.Lane,
                    SourcePageUrl = binding.SourcePage.PageUrl,
                    SourceListId = binding.SourcePage.ListId,
                    SourceItemId = binding.SourcePage.ItemId,
                    SourceFileUniqueId = binding.SourcePage.FileUniqueId,
                    SourceETag = binding.SourcePage.ETag,
                    TargetUrl = binding.TargetOrigin + binding.TargetPath,
                    TargetListId = admission.Target.ListId,
                    TargetETag = admission.Target.ETag,
                    ImplementationCommit = binding.Producer.ImplementationCommit,
                    ImplementationTree = admission.ImplementationTree,
                    LifecycleRunId = binding.RunId,
                    LifecycleDigestSha256 = binding.LifecycleDigest,
                    TargetMappingDigestSha256 = binding.TargetMappingDigest,
                    NativeOperationId = binding.NativeOperationId,
                    ExecutionWindowStartUtc = admission.ExecutionWindowStartUtc,
                    ExecutionWindowEndUtc = admission.ExecutionWindowEndUtc,
                    ReadbackAtUtc = readbackAt,
                    BrowserObservedAtUtc = browserAt,
                    BrowserReviewIssue = admission.BrowserReviewIssue,
                    BrowserReviewRunId = admission.BrowserReviewRunId,
                    BrowserVerdict = Text(browser, "verdict"),
                    BrowserSurfaceClassification = Text(Property(browser, "observation"), "surfaceClassification"),
                    BrowserFailedGates = failedGates,
                    StorageIdentityStatus = "verified",
                    PlanEvidenceStatus = "admitted-digest-reference-only",
                    PlanSchemaVersion = binding.PlanSchema,
                    DomainComparisonStatus = "not-run",
                    NativeCleanupReceiptStatus = "unavailable",
                    NativeCleanupReceiptDigestSha256 = null,
                    PostCleanupStatus = "FRESH_POST_CLEANUP_ABSENCE_PASS",
                    Artifacts = artifactBindings
                }
            };
            report.ReportDigestSha256 = ComputeReportDigest(report);
            // Do not expose aliases to the caller's mutable admission/request.
            return MigrationContractSerializer.Deserialize<PublishingPageCompareReport>(
                MigrationContractSerializer.SerializeCanonical(report));
        }

        internal static void ValidateExternalTerminalReport(
            PublishingPageCompareReport report, PublishingPageCompareRequest request,
            ExternalTerminalCompareAdmission admission, IMigrationArtifactStore artifactStore)
        {
            Require(report != null && report.SchemaVersion == PublishingPageCompareContract.ExternalTerminalSchemaVersion,
                "External terminal report validation requires the exact v3 schema.");
            ValidateDigest(report.ReportDigestSha256, "external Compare report");
            Equal(ComputeReportDigest(report), report.ReportDigestSha256, "external Compare report digest");
            // A caller can recompute a hash over invented success. Validation
            // therefore reopens the independently pinned inputs and rederives
            // the entire result, just as context-bound maturity validation does.
            var expected = ReconcileExternalTerminal(request, admission, artifactStore);
            Equal(MigrationContractSerializer.SerializeCanonical(report),
                MigrationContractSerializer.SerializeCanonical(expected), "rederived external terminal report");
        }

        private static void ValidateExternalAdmission(
            PublishingPageCompareRequest request, ExternalTerminalCompareAdmission admission)
        {
            Require(request != null && request.ExternalEvidence?.SchemaVersion == ExternalTerminalCompareContract.IntakeSchema
                && admission?.SchemaVersion == ExternalTerminalCompareContract.AdmissionSchema,
                "External Compare requires versioned evidence and independent admission pins.");
            Require(request.Package == null && request.ImportReceipt == null && request.RuntimeReceipt == null
                && request.Bindings == null && request.PlannedTargetIdentity == null
                && request.AssessmentHandoff == null && request.SourceVersionComparison == null
                && request.ImportReceiptDigestSha256 == null && request.RuntimeReceiptDigestSha256 == null
                && request.AssessmentHandoffDigestSha256 == null && request.AssessmentProducerRevisionId == null
                && request.SourceAuthState == "valid" && request.Ingredients != null && request.Ingredients.Count == 0,
                "Native and external Compare representations cannot be mixed or silently discarded.");
            Require(request.Producer != null && !string.IsNullOrWhiteSpace(request.Producer.Id)
                && !string.IsNullOrWhiteSpace(request.Producer.Version) && !string.IsNullOrWhiteSpace(request.Producer.ImplementationRef),
                "A Compare producer identity is required.");
            var binding = admission.Binding;
            Require(binding?.Identity != null && binding.SourcePage != null && binding.Producer != null
                && binding.Target != null && binding.LifecycleProducer != null && admission.Target != null
                && admission.BrowserPolicy != null,
                "The external claim, source, target, producer and browser policy are required.");
            Equal(binding.SchemaVersion, IngredientExternalEvidenceContract.BindingSchema, "shared identity binding version");
            Equal(binding.PlanSchema, IngredientExternalEvidenceContract.BatchPlanSchema, "referenced plan schema");
            Require(binding.Graph == null && binding.Actions != null && binding.Actions.Count == 0
                && binding.ActionSignature == null && binding.RuntimeManifest == null
                && binding.SourceSnapshotArtifact == null && binding.LifecycleInputArtifact == null,
                "Terminal Compare cannot silently consume a full plan/domain/maturity envelope.");
            Require(IngredientMaturityEvaluator.IsSha256(binding.Identity.ClaimId)
                && binding.Identity.Kind.HasValue && Enum.IsDefined(typeof(PageIngredientKind), binding.Identity.Kind.Value)
                && binding.Identity.WorkItemType == IngredientMaturityContract.CanonicalIngredientWorkItem
                && !string.IsNullOrWhiteSpace(binding.Identity.IngredientId)
                && !string.IsNullOrWhiteSpace(binding.Identity.Lane)
                && !string.IsNullOrWhiteSpace(binding.Identity.Subtype)
                && !string.IsNullOrWhiteSpace(binding.Identity.SemanticRole)
                && !string.IsNullOrWhiteSpace(binding.Identity.SourcePredicateId),
                "A concrete canonical ingredient needs claim, kind, subtype, role, predicate and primary lane.");
            foreach (var digest in new[] { binding.PlanDigest, binding.LifecycleDigest, binding.TargetMappingDigest,
                binding.LifecycleProducer.Sha256, admission.ManifestSha256, admission.FreshReadbackSha256,
                admission.BrowserReviewSha256, admission.CleanupUnavailableSha256, admission.PostCleanupSha256 })
                ValidateDigest(digest, "external admission");
            Require(IngredientMaturityEvaluator.IsCommit(binding.Producer.ImplementationCommit)
                && IngredientMaturityEvaluator.IsCommit(admission.ImplementationTree)
                && !string.IsNullOrWhiteSpace(admission.ImplementationBranch)
                && !string.IsNullOrWhiteSpace(admission.ConsumerIssue)
                && !string.IsNullOrWhiteSpace(admission.BrowserReviewIssue)
                && !string.IsNullOrWhiteSpace(binding.Target.TargetProfile)
                && !string.IsNullOrWhiteSpace(binding.RowId) && binding.RowNumber > 0
                && binding.LifecycleProducer.Kind == "workspace-file"
                && !string.IsNullOrWhiteSpace(binding.LifecycleProducer.Path),
                "The independently admitted implementation, producer and consumer binding is incomplete.");
            GuidIdentity(binding.RunId, "lifecycle run");
            GuidIdentity(admission.BrowserReviewRunId, "independent browser run");
            Require(binding.RunId != admission.BrowserReviewRunId, "Browser review must have a distinct run identity.");
            GuidIdentity(binding.SourcePage.ListId, "source List");
            GuidIdentity(binding.SourcePage.FileUniqueId, "source File");
            GuidIdentity(admission.Target.ListId, "target List");
            GuidIdentity(admission.Target.FileUniqueId, "target File");
            Require(binding.SourcePage.ItemId > 0 && admission.Target.ItemId > 0
                && !string.IsNullOrWhiteSpace(binding.SourcePage.ETag) && !string.IsNullOrWhiteSpace(admission.Target.ETag),
                "Version-bound source and target storage identities are required.");
            var source = ExactHttpsUrl(binding.SourcePage.PageUrl);
            Equal(Uri.UnescapeDataString(source.AbsolutePath), binding.SourcePage.FileServerRelativeUrl, "source path");
            var target = ExactHttpsUrl(binding.TargetOrigin + binding.TargetPath);
            Equal(target.GetLeftPart(UriPartial.Authority), binding.TargetOrigin, "target origin");
            Equal(binding.Target.TargetIdentity, target.AbsoluteUri, "target identity");
            Require(Batch1SealedPlanEvidenceAdapter.ValidPath(binding.TargetSitePath)
                && Batch1SealedPlanEvidenceAdapter.ValidPath(binding.TargetWebPath)
                && Batch1SealedPlanEvidenceAdapter.ValidPath(binding.TargetListPath)
                && Batch1SealedPlanEvidenceAdapter.ValidPath(binding.TargetPath)
                && Batch1SealedPlanEvidenceAdapter.Under(binding.TargetWebPath, binding.TargetSitePath)
                && Batch1SealedPlanEvidenceAdapter.Under(binding.TargetListPath, binding.TargetWebPath)
                && Batch1SealedPlanEvidenceAdapter.Under(binding.TargetPath, binding.TargetListPath),
                "The target hierarchy must be exact and contained.");
            Equal(binding.NativeOperationId, "ccd143-native-page-" + MigrationDigest.ComputeSha256(binding.TargetPath).Substring(0, 16),
                "native operation derivation");
            Equal(binding.NativeActionId, "ccd143-action-native-create-" + binding.NativeOperationId, "native action");
            Equal(binding.OwnershipMarker, "[CCD-143 " + binding.RunId.Substring(0, 8) + "]", "run ownership marker");
            Require(admission.ExecutionWindowStartUtc != default && admission.ExecutionWindowEndUtc != default
                && admission.ExecutionWindowStartUtc.Offset == TimeSpan.Zero && admission.ExecutionWindowEndUtc.Offset == TimeSpan.Zero
                && admission.ExecutionWindowStartUtc <= admission.ExecutionWindowEndUtc
                && request.GeneratedAtUtc != default && request.GeneratedAtUtc.Offset == TimeSpan.Zero
                && request.GeneratedAtUtc >= admission.ExecutionWindowEndUtc
                && admission.BrowserPolicy.MinimumBodyTextLength > 0
                && admission.ReadbackMaximumAttempts >= 3 && admission.ReadbackTimeoutMilliseconds > 0,
                "The report needs an independent UTC observation window and explicit browser policy.");
        }

        private static Uri ExactHttpsUrl(string value)
        {
            Require(Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
                && uri.AbsoluteUri == value, "External evidence needs an exact HTTPS URL without credentials, query or fragment.");
            return uri;
        }

        private static JsonElement ReadExternal(ArtifactReference artifact, string expectedDigest, string schema, IMigrationArtifactStore store)
        {
            Equal(artifact?.Sha256, expectedDigest, "independently pinned artifact");
            try
            {
                var root = ExternalEvidenceJson.Read(artifact, store);
                Equal(Text(root, "schema"), schema, "external schema");
                return root;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The external artifact is not valid JSON.", exception);
            }
        }

        private static void ValidateExternalManifest(JsonElement manifest, ExternalTerminalCompareAdmission admission)
        {
            var binding = admission.Binding;
            Equal(Text(manifest, "issue"), "CCD-143", "manifest producer protocol");
            Equal(Text(manifest, "runId"), binding.RunId, "manifest run");
            Equal(Text(manifest, "planDigest"), binding.PlanDigest, "manifest plan");
            Equal(Text(manifest, "sourcePlanDigest"), binding.PlanDigest, "manifest source plan");
            Equal(Text(manifest, "lifecycleDigest"), binding.LifecycleDigest, "manifest lifecycle");
            Equal(Text(manifest, "targetMappingDigest"), binding.TargetMappingDigest, "manifest mapping");
            Equal(Text(manifest, "targetOrigin"), binding.TargetOrigin, "manifest target");
            Batch1SealedPlanEvidenceAdapter.ValidateProducer(Property(manifest, "producerRef"), binding);
            Require(Number(manifest, "pageCount") == 1 && Number(manifest, "expectedPageCount") == 1,
                "External terminal Compare covers one concrete ingredient, not a page/batch-wide verdict.");
            var execution = Property(manifest, "execution");
            Equal(Text(execution, "mode"), "single-claim", "manifest execution mode");
            Require(Number(execution, "expectedPageCount") == 1 && Number(execution, "rowNumber") == binding.RowNumber,
                "Manifest claim/row coverage mismatch.");
            Equal(Text(execution, "claimId"), binding.Identity.ClaimId, "manifest claim");
            Equal(Text(execution, "rowId"), binding.RowId, "manifest row");
            Equal(Text(execution, "consumerIssue"), admission.ConsumerIssue, "manifest consumer");
            Equal(Text(execution, "admittedPlanDigest"), binding.PlanDigest, "manifest execution plan");
            Equal(Text(execution, "targetProfile"), binding.Target.TargetProfile, "manifest target profile");
            var implementation = Property(execution, "pnpImplementation");
            Equal(Text(implementation, "commit"), binding.Producer.ImplementationCommit, "manifest implementation commit");
            Equal(Text(implementation, "branch"), admission.ImplementationBranch, "manifest implementation branch");
            Batch1SealedPlanEvidenceAdapter.ValidateSource(Property(execution, "source"), binding, true);
            var ingredient = Property(execution, "ingredient");
            Equal(Text(ingredient, "id"), binding.Identity.IngredientId, "manifest ingredient");
            Equal(Text(ingredient, "kind"), binding.Identity.Kind.Value.ToString(), "manifest ingredient kind");
            Equal(Text(ingredient, "subtype"), binding.Identity.Subtype, "manifest ingredient subtype");
            // These are referenced seals, not reconstructed plan/input bytes.
            // In particular do not turn a valid manifest into a plan admission.
            Require(Time(manifest, "generatedAtUtc") <= admission.ExecutionWindowStartUtc,
                "The execution manifest must predate the independent observation window.");
        }

        private static DateTimeOffset ValidateExternalReadback(JsonElement receipt, ExternalTerminalCompareAdmission admission)
        {
            var binding = admission.Binding;
            Equal(Text(receipt, "stateBefore"), "created", "readback before state");
            Equal(Text(receipt, "stateAfter"), "retained", "readback after state");
            ValidateExternalPhaseTime(receipt, admission, admission.ExecutionWindowStartUtc);
            var page = ExternalPage(receipt, binding, "fresh-readback");
            Batch1SealedPlanEvidenceAdapter.ValidateSource(Property(page, "sourceVersion"), binding, false);
            Equal(Text(page, "sourceUrl"), binding.SourcePage.PageUrl, "readback source URL");
            Equal(Text(page, "targetUrl"), binding.TargetOrigin + binding.TargetPath, "readback target URL");
            Equal(Text(page, "planDigest"), binding.PlanDigest, "readback page plan");
            Equal(Text(page, "lifecycleDigest"), binding.LifecycleDigest, "readback page lifecycle");
            Equal(Text(page, "targetMappingDigest"), binding.TargetMappingDigest, "readback page mapping");
            Equal(Text(page, "ownershipMarker"), binding.OwnershipMarker, "readback page owner");
            Batch1SealedPlanEvidenceAdapter.ValidateProducer(Property(page, "producerRef"), binding);
            Equal(Text(Property(page, "pnpImplementation"), "commit"), binding.Producer.ImplementationCommit, "readback implementation");
            Equal(Text(Property(page, "pnpImplementation"), "branch"), admission.ImplementationBranch, "readback implementation branch");
            var identity = Property(page, "identity");
            Require(Flag(identity, "valid") && Number(identity, "fileStatus") == 200 && Number(identity, "itemStatus") == 200,
                "Fresh target identity is missing, denied or invalid.");
            foreach (var name in new[] { "fileUniqueId", "itemUniqueId", "itemFileUniqueId" })
                Equal(Text(identity, name), admission.Target.FileUniqueId, "readback " + name);
            Equal(Text(identity, "parentListId"), admission.Target.ListId, "readback List");
            Require(Number(identity, "itemId") == admission.Target.ItemId, "Readback target item mismatch.");
            Equal(Text(identity, "fileRef"), binding.TargetPath, "readback FileRef");
            Text(identity, "requestGuid");
            foreach (var name in new[] { "fileStability", "itemStability" })
            {
                SharedTargetLifecycleEvidenceAdapter.ValidateBoundedPoll(Property(identity, name), 200,
                    admission.ReadbackMaximumAttempts, admission.ReadbackTimeoutMilliseconds,
                    Time(receipt, "startedAtUtc"), Time(receipt, "finishedAtUtc"), (sample, terminal) =>
                    {
                        Require(Number(sample, "status") != 401 && Number(sample, "status") != 403,
                            "An ingredient denial cannot be erased by a later successful readback.");
                        if (name == "itemStability" && Property(sample, "objectId").ValueKind != JsonValueKind.Null)
                            Require(Number(sample, "objectId") == admission.Target.ItemId,
                                "A foreign item poll cannot verify the admitted target.");
                    });
            }
            var fileBody = Property(Property(identity, "fileStability"), "lastBody");
            Equal(Text(fileBody, "ETag"), admission.Target.ETag, "readback target version");
            Equal(Text(fileBody, "UniqueId"), admission.Target.FileUniqueId, "readback terminal File ID");
            Equal(Text(fileBody, "ServerRelativeUrl"), binding.TargetPath, "readback terminal path");
            var itemBody = Property(Property(identity, "itemStability"), "lastBody");
            Require(Number(itemBody, "Id") == admission.Target.ItemId, "Readback terminal item mismatch.");
            Equal(Text(Property(itemBody, "File"), "UniqueId"), admission.Target.FileUniqueId, "readback item File");
            Equal(Text(Property(itemBody, "ParentList"), "Id"), admission.Target.ListId, "readback item List");
            Equal(Text(itemBody, "UniqueId"), admission.Target.FileUniqueId, "readback terminal item identity");
            Equal(Text(itemBody, "FileRef"), binding.TargetPath, "readback terminal item path");
            var observed = Time(page, "timestampUtc");
            Require(observed >= Time(receipt, "startedAtUtc") && observed <= Time(receipt, "finishedAtUtc")
                && observed >= Time(Property(identity, "fileStability"), "finishedAtUtc")
                && observed >= Time(Property(identity, "itemStability"), "finishedAtUtc"),
                "Readback observation is outside its receipt phase.");
            // Do not read layoutEvidence/content/ingredientAssessment here:
            // their normalization and outcome remain with the domain owner.
            return observed;
        }

        private static IList<string> ValidateExternalBrowser(
            JsonElement receipt, ExternalTerminalCompareAdmission admission, DateTimeOffset readbackAt)
        {
            var binding = admission.Binding;
            Equal(Text(receipt, "issue"), admission.BrowserReviewIssue, "browser review issue");
            Equal(Text(receipt, "parentIssue"), admission.ConsumerIssue, "browser consumer");
            Equal(Text(receipt, "reviewRunId"), admission.BrowserReviewRunId, "independent browser run");
            var frozen = Property(receipt, "frozenBinding");
            Equal(Text(frozen, "claimId"), binding.Identity.ClaimId, "browser claim");
            Equal(Text(frozen, "ingredientId"), binding.Identity.IngredientId, "browser ingredient");
            Equal(Text(frozen, "planDigest"), binding.PlanDigest, "browser plan");
            Equal(Text(frozen, "lifecycleRunId"), binding.RunId, "browser lifecycle run");
            Equal(Text(frozen, "lifecycleDigest"), binding.LifecycleDigest, "browser lifecycle");
            Equal(Text(frozen, "targetMappingDigest"), binding.TargetMappingDigest, "browser target mapping");
            Equal(Text(frozen, "operationId"), binding.NativeOperationId, "browser native operation");
            Equal(Text(frozen, "targetFileUniqueId"), admission.Target.FileUniqueId, "browser File");
            Equal(Text(frozen, "targetListId"), admission.Target.ListId, "browser List");
            Require(Number(frozen, "targetItemId") == admission.Target.ItemId, "Browser target item mismatch.");
            var requested = Property(receipt, "requested");
            Equal(Text(requested, "origin"), binding.TargetOrigin, "browser requested origin");
            Equal(Text(requested, "path"), binding.TargetPath, "browser requested path");
            Equal(Text(requested, "url"), binding.TargetOrigin + binding.TargetPath, "browser requested URL");
            var browser = Property(receipt, "browser");
            Require(Flag(browser, "freshTab") && Flag(browser, "cacheDisabled") && Flag(browser, "bypassServiceWorker"),
                "Browser evidence must be a fresh, uncached navigation.");
            Text(browser, "product");
            Text(browser, "revision");
            Text(browser, "protocolVersion");
            var observation = Property(receipt, "observation");
            // The browser producer seals the observation with JSON.stringify,
            // retaining insertion order. The shared serializer uses the same
            // representation for this JSON value; original outer bytes stay pinned.
            Equal(MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(observation)),
                Text(receipt, "observationSha256"), "browser observation digest");
            var location = Property(observation, "location");
            Equal(Text(location, "href"), binding.TargetOrigin + binding.TargetPath, "observed browser URL");
            Equal(Text(location, "origin"), binding.TargetOrigin, "observed browser origin");
            Equal(Text(location, "pathname"), binding.TargetPath, "observed browser path");
            Require(!Flag(location, "searchPresent") && !Flag(location, "hashPresent"), "Browser URL was redirected or decorated.");
            var response = Property(observation, "sameOriginRequest");
            Equal(Text(response, "finalUrl"), binding.TargetOrigin + binding.TargetPath, "browser response URL");
            Require(!Flag(response, "redirected"), "Browser response redirect cannot be accepted as the target.");
            Text(Property(response, "requestIds"), "spRequestGuid");
            var at = Time(receipt, "observedAtUtc");
            var observationAt = Time(observation, "observedAtUtc");
            Require(observationAt >= readbackAt && at >= observationAt && at <= admission.ExecutionWindowEndUtc,
                "Browser observation is stale, future, or earlier than fresh readback.");
            Require(Number(receipt, "sourceMutationCount") == 0 && Number(receipt, "targetMutationCount") == 0,
                "Independent browser review must not mutate either tenant.");
            var gates = Property(receipt, "gates");
            var policy = admission.BrowserPolicy;
            Require(Number(gates, "expectedHttpStatus") == 200
                && Number(gates, "minimumBodyTextLength") == policy.MinimumBodyTextLength
                && Flag(gates, "requireSpPageContextInfo") && Flag(gates, "rejectLogin")
                && Flag(gates, "rejectAccessDenied") && Flag(gates, "rejectSpoError")
                && Flag(gates, "requireAspNetForm") == policy.RequireAspNetForm
                && Flag(gates, "requireClassicSurface") == policy.RequireClassicSurface
                && Flag(gates, "requireWikiRuntimeSignal") == policy.RequireWikiRuntimeSignal,
                "Browser requirements were weakened or differ from independent admission.");
            Equal(Text(gates, "expectedOrigin"), binding.TargetOrigin, "browser policy origin");
            Equal(Text(gates, "expectedPath"), binding.TargetPath, "browser policy path");
            var failures = new SortedSet<string>(StringComparer.Ordinal);
            if (Number(response, "status") != 200 || Number(Property(observation, "navigationTiming"), "responseStatus") != 200)
                failures.Add("HTTP_STATUS_MISMATCH");
            var semantic = Property(observation, "semantic");
            if (Flag(semantic, "login")) failures.Add("LOGIN_SURFACE_DETECTED");
            if (Flag(semantic, "accessDenied")) failures.Add("ACCESS_DENIED_SURFACE_DETECTED");
            if (Flag(semantic, "spoError") || Flag(semantic, "errorRedirect")) failures.Add("SPO_ERROR_SURFACE_DETECTED");
            if (Text(observation, "readyState") != "complete") failures.Add("DOCUMENT_NOT_READY");
            if (Number(observation, "bodyTextLength") < policy.MinimumBodyTextLength) failures.Add("BODY_TEXT_TOO_SHORT");
            if (!Flag(Property(observation, "pageContext"), "present")) failures.Add("SP_PAGE_CONTEXT_MISSING");
            if (policy.RequireAspNetForm && !Flag(observation, "aspNetFormSurface")) failures.Add("ASPNET_FORM_MISSING");
            if (policy.RequireClassicSurface && !Flag(observation, "classicSurface")) failures.Add("CLASSIC_SHAREPOINT_SURFACE_MISSING");
            if (policy.RequireWikiRuntimeSignal && !Flag(observation, "wikiRuntimeSignal")) failures.Add("WIKI_RUNTIME_SIGNAL_MISSING");
            var declared = Array(receipt, "failedGates").Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToList();
            Require(declared.All(value => !string.IsNullOrWhiteSpace(value))
                && declared.Distinct(StringComparer.Ordinal).Count() == declared.Count
                && failures.SetEquals(declared), "Browser failed gates contradict the original observation or use an unsupported gate.");
            Equal(Text(receipt, "verdict"), failures.Count == 0 ? "BROWSER_RUNTIME_PASS" : "BROWSER_RUNTIME_FAIL", "browser verdict");
            return failures.ToList();
        }

        private static void ValidateExternalCleanup(JsonElement post, JsonElement unavailable,
            ExternalTerminalCompareAdmission admission, DateTimeOffset browserAt)
        {
            var binding = admission.Binding;
            Equal(Text(post, "stateBefore"), "cleaned", "post-cleanup producer before state");
            Equal(Text(post, "stateAfter"), "absent", "post-cleanup producer after state");
            ValidateExternalPhaseTime(post, admission, browserAt);
            Equal(Text(post, "reasonCode"), "FRESH_POST_CLEANUP_ABSENCE_PASS", "post-cleanup reason");
            var page = ExternalPage(post, binding, "post-cleanup");
            Require(Number(page, "status") == 404, "Post-cleanup page absence is not proven.");
            Text(page, "requestGuid");
            var at = Time(page, "timestampUtc");
            Require(at >= Time(post, "startedAtUtc") && at <= Time(post, "finishedAtUtc"), "Stale post-cleanup page observation.");
            var sites = Array(post, "sites");
            Require(sites.Length == 1, "Post-cleanup site coverage mismatch.");
            Equal(Text(sites[0], "path"), binding.TargetSitePath, "post-cleanup site");
            Require(Number(sites[0], "status") == 404, "Post-cleanup site absence is not proven.");
            Text(sites[0], "requestGuid");
            var ownership = Property(post, "ownership");
            Equal(Text(ownership, "path"), "/SiteAssets/ccd143-ownership-" + binding.RunId.Substring(0, 8) + ".json", "post-cleanup marker");
            Require(Number(ownership, "status") == 404, "Post-cleanup ownership marker absence is not proven.");

            Equal(Text(unavailable, "issue"), admission.ConsumerIssue, "cleanup observer");
            Equal(Text(unavailable, "phase"), "cleanup", "unavailable phase");
            Equal(Text(unavailable, "runId"), binding.RunId, "unavailable cleanup run");
            Equal(Text(unavailable, "claimId"), binding.Identity.ClaimId, "unavailable cleanup claim");
            Equal(Text(unavailable, "planDigest"), binding.PlanDigest, "unavailable cleanup plan");
            Equal(Text(unavailable, "lifecycleDigest"), binding.LifecycleDigest, "unavailable cleanup lifecycle");
            Equal(Text(unavailable, "targetMappingDigest"), binding.TargetMappingDigest, "unavailable cleanup mapping");
            Equal(Text(unavailable, "producerSha256"), binding.LifecycleProducer.Sha256, "unavailable cleanup producer");
            Equal(Text(unavailable, "freshPostCleanupReceiptSha256"), admission.PostCleanupSha256, "unavailable observation to exact target/operation absence");
            Equal(Text(unavailable, "freshPostCleanupVerdict"), "FRESH_POST_CLEANUP_ABSENCE_PASS", "unavailable post-cleanup verdict");
            Require(!Flag(unavailable, "nativeReceiptPersisted") && Number(unavailable, "sourceRequests") == 0
                && Number(unavailable, "sourceMutations") == 0 && Number(unavailable, "pageStatus") == 404
                && Number(unavailable, "siteStatus") == 404 && Number(unavailable, "ownershipMarkerStatus") == 404,
                "A missing native cleanup receipt cannot become successful cleanup evidence.");
            // CDP completion is retained as an observation, not used as proof
            // of native operation success, count, ownership or retry safety.
            Flag(unavailable, "cdpEvaluationCompleted");
            Text(unavailable, "transportError");
            Equal(Text(unavailable, "m4Disposition"), "fail_closed_missing_native_cleanup_receipt", "missing cleanup disposition");
            var recorded = Time(unavailable, "recordedAtUtc");
            Require(recorded >= Time(post, "finishedAtUtc") && recorded <= admission.ExecutionWindowEndUtc,
                "Cleanup-unavailable observation is stale, future or before its referenced readback.");
        }

        private static JsonElement ExternalPage(JsonElement receipt, IngredientExternalPlanBinding binding, string phase)
        {
            var pages = Array(receipt, "pages");
            Require(pages.Length == 1 && Number(pages[0], "rowNumber") == binding.RowNumber,
                "External receipt must contain exactly the admitted page instance.");
            var page = pages[0];
            Equal(Text(page, "claimId"), binding.Identity.ClaimId, phase + " claim");
            Equal(Text(page, "operationId"), binding.NativeOperationId, phase + " native operation");
            Equal(Text(page, "actionId"), "ccd143-action-" + phase + "-" + binding.NativeOperationId, phase + " action");
            Equal(Text(page, "targetPath"), binding.TargetPath, phase + " target path");
            return page;
        }

        private static void ValidateExternalPhaseTime(JsonElement receipt, ExternalTerminalCompareAdmission admission, DateTimeOffset previous)
        {
            var start = Time(receipt, "startedAtUtc");
            var finish = Time(receipt, "finishedAtUtc");
            Require(start >= previous && start >= admission.ExecutionWindowStartUtc && finish >= start
                && finish <= admission.ExecutionWindowEndUtc, "External receipt is stale, future or out of phase order.");
        }

        private static CompareExternalArtifactBinding ExternalArtifactBinding(string role, ArtifactReference artifact, string schema)
        {
            return new CompareExternalArtifactBinding
            {
                Role = role, SchemaVersion = schema, RawDigestSha256 = artifact.Sha256, Length = artifact.Length
            };
        }
    }
}
