using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static PnP.Framework.Migration.Pages.Assessment.Maturity.External.ExternalEvidenceJson;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.External
{
    // Reads original CCD-143/v2 artifacts; never runs a lifecycle, rewrites an
    // operation/action ID, or changes native acceptance/Compare. Domain value
    // fidelity remains in the contributor and its existing domain validators.
    internal sealed class SharedTargetLifecycleEvidenceAdapter
    {
        private static readonly string[] Phases =
        {
            "admission", "provision", "native-web-readiness", "capability-readiness",
            "native-create", "fresh-readback", "cleanup", "post-cleanup"
        };
        private static readonly string[] Before =
        {
            "absent", "admitted", "native-web-created", "native-web-ready",
            "capability-ready", "created", "retained-or-partial", "cleaned"
        };
        private static readonly string[] After =
        {
            "admitted", "provisioned", "native-web-ready", "capability-ready",
            "created", "retained", "cleaned", "absent"
        };

        private readonly Dictionary<string, JsonElement> receipts = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, ArtifactReference> artifacts = new Dictionary<string, ArtifactReference>(StringComparer.Ordinal);
        private ValidatedExternalIngredientPlan plan;
        private IngredientExternalEvidenceAdmission admission;

        public IngredientExternalPlanBinding Binding => plan.Binding;
        private IngredientExternalTargetIdentity Target => admission.Target;

        public static SharedTargetLifecycleEvidenceAdapter Read(
            IngredientMaturityEvaluationContext context, IngredientExternalOperationalEvidence evidence)
        {
            Require(evidence != null && evidence.SchemaVersion == IngredientExternalEvidenceContract.EnvelopeSchema,
                "Unsupported external operational envelope.");
            var result = new SharedTargetLifecycleEvidenceAdapter
            {
                plan = Batch1SealedPlanEvidenceAdapter.Validate(context, evidence.PlanEvidence),
                admission = context.ExternalAdmission
            };
            var expected = result.admission;
            Require(expected.ExecutionWindowStartUtc != default && expected.ExecutionWindowEndUtc != default
                && expected.ExecutionWindowStartUtc.Offset == TimeSpan.Zero && expected.ExecutionWindowEndUtc.Offset == TimeSpan.Zero
                && expected.ExecutionWindowStartUtc <= expected.ExecutionWindowEndUtc
                && IngredientMaturityEvaluator.IsSha256(expected.ReceiptSetArtifactSha256),
                "Lifecycle evidence requires independent receipt-set and UTC execution-window pins.");
            Equal(evidence.ReceiptSetArtifact?.Sha256, expected.ReceiptSetArtifactSha256, "receipt-set artifact");
            var set = ReadCanonical<IngredientExternalReceiptSet>(evidence.ReceiptSetArtifact, evidence.PlanEvidence.ArtifactStore);
            Equal(set.SchemaVersion, IngredientExternalEvidenceContract.ReceiptSetSchema, "receipt-set schema");
            Equal(set.BindingArtifactSha256, expected.BindingArtifactSha256, "receipt-set annex");
            Require(set.Receipts != null && set.Receipts.Count == Phases.Length
                && set.Receipts.All(value => value != null)
                && set.Receipts.Select(value => value.Sha256).Distinct(StringComparer.Ordinal).Count() == Phases.Length,
                "Every lifecycle phase, including cleanup and post-cleanup, needs one original artifact.");
            var previous = result.Binding.AdmittedAtUtc;
            Require(previous >= expected.ExecutionWindowStartUtc && previous <= expected.ExecutionWindowEndUtc,
                "The annex admission is outside the consumer's execution window.");
            for (var index = 0; index < Phases.Length; index++)
            {
                var receipt = ExternalEvidenceJson.Read(set.Receipts[index], evidence.PlanEvidence.ArtifactStore);
                var phase = Phases[index];
                result.ValidateHeader(receipt, phase);
                Equal(Text(receipt, "stateBefore"), Before[index], "producer state before " + phase);
                Equal(Text(receipt, "stateAfter"), After[index], "producer state after " + phase);
                var started = Time(receipt, "startedAtUtc");
                var finished = Time(receipt, "finishedAtUtc");
                Require(started >= previous && finished >= started && finished <= expected.ExecutionWindowEndUtc,
                    "A lifecycle receipt is stale, future, overlapping or out of order: " + phase);
                previous = finished;
                result.receipts.Add(phase, receipt);
                result.artifacts.Add(phase, set.Receipts[index]);
            }
            Require(expected.Target != null && expected.Target.ItemId > 0 && !string.IsNullOrWhiteSpace(expected.Target.ETag),
                "An independently observed target storage tuple is required.");
            GuidIdentity(expected.Target.SiteId, "target Site");
            GuidIdentity(expected.Target.WebId, "target Web");
            GuidIdentity(expected.Target.ListId, "target List");
            GuidIdentity(expected.Target.FileUniqueId, "target File");
            return result;
        }

        private void ValidateHeader(JsonElement receipt, string phase)
        {
            Equal(Text(receipt, "schema"), IngredientExternalEvidenceContract.LifecycleReceiptSchema, "receipt schema");
            Equal(Text(receipt, "issue"), "CCD-143", "receipt producer protocol");
            Equal(Text(receipt, "phase"), phase, "phase ordering");
            Equal(Text(receipt, "runId"), Binding.RunId, "receipt run");
            Equal(Text(receipt, "operationId"), "ccd143-" + phase + "-" + Binding.LifecycleDigest.Substring(0, 16),
                "original phase operation ID");
            Equal(Text(receipt, "actionId"), "ccd143-action-" + phase + "-" + Binding.LifecycleDigest.Substring(0, 16),
                "original phase action ID");
            Equal(Text(receipt, "planDigest"), Binding.PlanDigest, "receipt plan");
            Equal(Text(receipt, "lifecycleDigest"), Binding.LifecycleDigest, "receipt lifecycle");
            Equal(Text(receipt, "targetMappingDigest"), Binding.TargetMappingDigest, "receipt target mapping");
            Equal(Text(receipt, "targetOrigin"), Binding.TargetOrigin, "receipt target origin");
            Equal(Text(receipt, "ownershipMarker"), Binding.OwnershipMarker, "receipt ownership");
            Require(Number(receipt, "sourceRequests") == 0 && Number(receipt, "sourceMutations") == 0,
                "A target-only lifecycle cannot claim source collection or mutate the source.");
            Equal(Text(receipt, "verdict"), "pass", "producer phase verdict");
            Batch1SealedPlanEvidenceAdapter.ValidateProducer(Property(receipt, "producerRef"), Binding);
            var lineage = Property(receipt, "digestLineage");
            Equal(Text(lineage, "parentPlanDigest"), Binding.PlanDigest, "parent plan lineage");
            Equal(Text(lineage, "lifecycleDigest"), Binding.LifecycleDigest, "lifecycle lineage");
            Equal(Text(lineage, "targetMappingDigest"), Binding.TargetMappingDigest, "target mapping lineage");
            Batch1SealedPlanEvidenceAdapter.ValidateProducer(Property(lineage, "producer"), Binding);
            var execution = Property(receipt, "executionBinding");
            Equal(Text(execution, "mode"), "single-claim", "receipt execution mode");
            Require(Number(execution, "expectedPageCount") == 1, "Receipt claim coverage mismatch.");
            Equal(Text(execution, "claimId"), Binding.Identity.ClaimId, "receipt claim");
            Equal(Text(execution, "consumerIssue"), Text(Property(plan.Input, "execution"), "consumerIssue"), "receipt consumer");
            Equal(Text(execution, "rowId"), Binding.RowId, "receipt row");
            Equal(Text(execution, "admittedPlanDigest"), Binding.PlanDigest, "receipt execution plan");
            Equal(Text(execution, "pnpCommit"), Binding.Producer.ImplementationCommit, "receipt implementation commit");
            Equal(Text(execution, "pnpBranch"), Text(Property(Property(plan.Input, "execution"), "pnpImplementation"), "branch"),
                "receipt implementation branch");
            Equal(Text(execution, "targetProfile"), Binding.Target.TargetProfile, "receipt target profile");
            Batch1SealedPlanEvidenceAdapter.ValidateSource(Property(execution, "sourceVersion"), Binding, true);
        }

        public void ValidateAdmission()
        {
            var frontier = PageIngredientPlanEvaluator.Evaluate(Binding.Graph, Binding.Actions).ExecutionFrontier;
            Require(frontier != null && frontier.IsExecutable(Binding.Identity.IngredientId),
                "A native mutation receipt cannot verify an excluded, deferred or dependency-skipped action.");
            var receipt = receipts["admission"];
            var site = SingleBy(receipt, "sites", "path", Binding.TargetSitePath);
            Require(Number(site, "status") == 404, "The admitted target Site was not absent.");
            Text(site, "requestGuid");
            var marker = Property(receipt, "marker");
            Equal(Text(marker, "path"), Text(plan.Input, "markerPath"), "admission marker path");
            Require(Number(marker, "status") == 404, "The ownership marker was not absent at admission.");
            var pageAdmissions = Array(receipts["capability-readiness"], "pageAdmissions");
            Require(pageAdmissions.Length == 1, "Exactly one original page admission fence is required.");
            var pageAdmission = pageAdmissions[0];
            Require(Number(pageAdmission, "rowNumber") == Binding.RowNumber
                && Number(pageAdmission, "preferredStatus") == 404 && Number(pageAdmission, "mappedStatus") == 404,
                "The assessed page did not pass its pre-write absent-path admission.");
            Equal(Text(pageAdmission, "claimId"), Binding.Identity.ClaimId, "page admission claim");
            Equal(Text(pageAdmission, "operationId"), Binding.NativeOperationId, "page admission operation");
            Equal(Text(pageAdmission, "actionId"), "ccd143-action-admission-" + Binding.NativeOperationId, "page admission action");
            Equal(Text(pageAdmission, "targetPath"), Binding.TargetPath, "page admission target");
            Equal(Text(pageAdmission, "preferredPath"), Text(plan.Page, "preferredPath"), "page admission preferred path");
            Equal(Text(pageAdmission, "mappingReason"), Text(plan.Page, "mappingReason"), "page admission mapping policy");
            Equal(Text(pageAdmission, "verdict"), "pass", "page admission verdict");
            Text(pageAdmission, "preferredRequestGuid");
            Text(pageAdmission, "mappedRequestGuid");
            InPhase("capability-readiness", Time(pageAdmission, "timestampUtc"));
            // CCD-143's provision receipt retains stateAfter=provisioned while
            // containing native Web creation. Validate that evidence explicitly;
            // do not rewrite the historical state to manufacture continuity.
            ValidateTargetDependencies();
        }

        public void ValidateOperations()
        {
            ValidateAdmission();
            foreach (var phase in new[] { "native-create", "fresh-readback", "cleanup", "post-cleanup" })
                Page(phase);
            var created = Page("native-create");
            Equal(Text(created, "verdict"), "pass", "native create verdict");
            Equal(Text(created, "planDigest"), Binding.PlanDigest, "native page plan");
            Equal(Text(created, "lifecycleDigest"), Binding.LifecycleDigest, "native page lifecycle");
            Equal(Text(created, "targetUrl"), Binding.TargetOrigin + Binding.TargetPath, "created target URL");
            Equal(Text(created, "sourceUrl"), Binding.SourcePage.PageUrl, "created source URL");
            Equal(Text(Property(created, "identity"), "itemFileRef"), Binding.TargetPath, "created item File path");
            Equal(Text(Property(created, "pnpImplementation"), "commit"), Binding.Producer.ImplementationCommit,
                "native implementation producer");
            var preflight = Property(created, "preflight");
            Require(Number(preflight, "status") == 404, "Native create has no absent-path fence.");
            Text(preflight, "requestGuid");
            InPhase("native-create", Time(preflight, "timestampUtc"));
            var mutation = Property(created, "nativeCreate");
            Success(mutation, "status", "requestGuid");
            Equal(Text(mutation, "mode"), Text(Property(Property(plan.Input, "execution"), "ingredient"), "nativeCreateMode"),
                "native API mode");
            Batch1SealedPlanEvidenceAdapter.ValidateProducer(Property(mutation, "producerRef"), Binding);
            var applied = Time(mutation, "timestampUtc");
            InPhase("native-create", applied);
            Require(applied >= Time(preflight, "timestampUtc"), "Native mutation predates its absent-path fence.");
        }

        public void ValidateOwnership()
        {
            ValidateTargetDependencies();
            var provisionMarker = Property(receipts["provision"], "ownership");
            Equal(Text(provisionMarker, "path"), Text(plan.Input, "markerPath"), "created marker path");
            Success(provisionMarker, "createStatus", "requestGuid");
            foreach (var phase in new[] { "native-web-readiness", "capability-readiness", "native-create", "fresh-readback", "cleanup" })
            {
                var ownership = Property(receipts[phase], "ownership");
                Require(Number(ownership, "status") == 200 && Flag(ownership, "valid"),
                    "The producer did not verify run/claim/target ownership: " + phase);
            }
            ValidateTargetIdentity(Property(Page("native-create"), "identity"));
            ValidateTargetIdentity(Property(Page("fresh-readback"), "identity"));
        }

        private void ValidateTargetDependencies()
        {
            ValidateSiteWebIdentity();
            ValidateCapabilityListIdentity();
        }

        private void ValidateSiteWebIdentity()
        {
            var site = SingleBy(receipts["provision"], "sites", "path", Binding.TargetSitePath);
            Equal(Text(site, "siteId"), Target.SiteId, "provision Site ID");
            Require(Number(site, "preStatus") == 404, "Site creation would overwrite an existing target.");
            Success(Property(site, "create"), "status", "requestGuid");
            Equal(Text(Property(site, "create"), "siteId"), Target.SiteId, "created Site ID");
            var readySite = SingleBy(receipts["native-web-readiness"], "sites", "path", Binding.TargetSitePath);
            Equal(Text(readySite, "siteId"), Target.SiteId, "readiness Site ID");
            Equal(Text(readySite, "ownedSiteId"), Target.SiteId, "owned Site ID");
            Require(Number(readySite, "siteStatus") == 200 && Number(readySite, "webStatus") == 200
                && Flag(readySite, "identityMatches") && Flag(readySite, "ownershipMatches")
                && Flag(readySite, "isProvisioningComplete"), "Site readiness/ownership is incomplete.");
            Text(readySite, "requestGuid");
            var createdWeb = SingleBy(receipts["provision"], "webs", "path", Binding.TargetWebPath);
            var readyWeb = SingleBy(receipts["native-web-readiness"], "webs", "path", Binding.TargetWebPath);
            Equal(Text(createdWeb, "parentPath"), Binding.TargetWebPath.Substring(0, Binding.TargetWebPath.LastIndexOf('/')),
                "created Web parent path");
            Require(Number(createdWeb, "preStatus") == 404, "Native Web creation has no absent-path fence.");
            InPhase("provision", Time(createdWeb, "timestampUtc"));
            Equal(Text(createdWeb, "operationId"), "ccd143-native-web-create-" +
                MigrationDigest.ComputeSha256(Binding.TargetWebPath).Substring(0, 12), "original native Web operation");
            var creation = Property(createdWeb, "create");
            Success(creation, "status", "requestGuid");
            Require(Number(creation, "status") == 200 && Property(creation, "errorInfo").ValueKind == JsonValueKind.Null,
                "Native Web CSOM creation is missing, denied or contains a semantic error.");
            var createdIdentity = Property(createdWeb, "identity");
            Equal(Text(readyWeb, "ownedObjectIdentity"), Text(createdIdentity, "objectIdentity"), "accepted Web object identity");
            Equal(Text(createdIdentity, "ownershipFingerprint"), Binding.OwnershipMarker + " " + Binding.TargetWebPath,
                "created Web ownership fingerprint");
            Equal(Text(readyWeb, "ownershipFingerprint"), Text(createdIdentity, "ownershipFingerprint"),
                "readiness Web ownership fingerprint");
            Equal(Text(readyWeb, "sitePath"), Binding.TargetSitePath, "ready Web parent Site");
            Equal(Text(createdWeb, "operationId"), Text(readyWeb, "operationId"), "native Web operation");
            Equal(Text(readyWeb, "ownedWebId"), Target.WebId, "owned Web ID");
            foreach (var web in new[] { createdWeb, readyWeb })
            {
                Equal(Text(web, "verdict"), "pass", "native Web verdict");
                var identity = Property(web, "identity");
                Equal(Text(identity, "webId"), Target.WebId, "native Web ID");
                Equal(Text(identity, "serverRelativeUrl"), Binding.TargetWebPath, "native Web path");
                Equal(Text(identity, "url"), Binding.TargetOrigin + Binding.TargetWebPath, "native Web URL");
                Equal(Text(identity, "description"), Binding.OwnershipMarker + " " + Binding.TargetWebPath, "native Web ownership");
                // Native create may be accepted before provisioning completes.
                // Only the following fresh OpenWebById observation proves ready.
                ProvisioningComplete(identity);
            }
            Require(ProvisioningComplete(Property(readyWeb, "identity")), "Native Web provisioning is not complete.");
            ValidateNativeWebPoll(readyWeb);
        }

        private void ValidateNativeWebPoll(JsonElement poll)
        {
            var limits = Property(Property(plan.Input, "pollPolicy"), "web");
            var attempts = Number(poll, "attempts");
            var samples = Array(poll, "samples");
            var started = Time(poll, "startedAtUtc");
            var finished = Time(poll, "finishedAtUtc");
            var timeout = Number(limits, "timeoutMs");
            InPhase("native-web-readiness", started);
            InPhase("native-web-readiness", finished);
            Require(attempts > 0 && attempts <= Number(limits, "attempts") && samples.Length == attempts
                && timeout > 0 && Number(poll, "timeoutMs") == timeout
                && finished >= started && (finished - started).TotalMilliseconds <= timeout
                && Number(poll, "elapsedMs") >= 0 && Number(poll, "elapsedMs") <= timeout,
                "Missing, unbounded or stale native Web readiness evidence.");
            var previous = started;
            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                var observed = Time(sample, "atUtc");
                Require(Number(sample, "attempt") == index + 1 && observed >= previous && observed <= finished,
                    "Native Web readiness samples are missing, reordered or outside their time fence.");
                previous = observed;
                if (Property(sample, "status").ValueKind == JsonValueKind.Null)
                {
                    // The producer records a caught request exception as a
                    // non-ready attempt. Retain it; it cannot be terminal success.
                    var error = Text(sample, "error");
                    Require(sample.EnumerateObject().Count() == 5
                        && (error == "AbortError" || error == "TimeoutError" || error == "TypeError"),
                        "Unknown or mixed native Web request-exception evidence.");
                    Require(!Flag(sample, "ready") && index < samples.Length - 1,
                        "A failed native Web request cannot establish readiness.");
                    continue;
                }
                Require(!sample.TryGetProperty("error", out _), "A native Web HTTP observation cannot hide an exception.");
                var status = Number(sample, "status");
                Require(status >= 100 && status <= 599 && status != 401 && status != 403,
                    "Native Web readiness contains an unavailable/access-denied observation.");
                Text(sample, "requestGuid");
                Require(Property(sample, "errorInfo").ValueKind == JsonValueKind.Null,
                    "Native Web readiness contains a CSOM semantic error.");
                var web = Property(sample, "web");
                var sameId = ObservedIdentity(web, "webId", Target.WebId);
                var samePath = ObservedIdentity(web, "serverRelativeUrl", Binding.TargetWebPath);
                var sameUrl = ObservedIdentity(web, "url", Binding.TargetOrigin + Binding.TargetWebPath);
                var sameOwner = ObservedIdentity(web, "description", Binding.OwnershipMarker + " " + Binding.TargetWebPath);
                Require(Flag(sample, "identityMatches") == (sameId && samePath)
                    && Flag(sample, "ownershipMatches") == sameOwner,
                    "Native Web readiness flags contradict the observed identity/ownership.");
                var complete = ProvisioningComplete(web);
                var ready = status == 200 && sameId && samePath && sameOwner && complete;
                // CCD-143 stops at the FIRST successful native sample. It does
                // not use the file/list/cleanup three-observation streak rule.
                Require(Flag(sample, "ready") == ready && ready == (index == samples.Length - 1)
                    && (!ready || sameUrl), "Native Web readiness has no exact first-success terminal observation.");
            }
            EqualObject(Property(poll, "identity"), Property(samples[samples.Length - 1], "web"),
                "native Web terminal sample / summary");
        }

        private static bool ObservedIdentity(JsonElement observed, string field, string expected)
        {
            var value = Property(observed, field);
            // An unsuccessful query may explicitly report no identity. An
            // observed foreign identity is never repaired by a later summary.
            if (value.ValueKind == JsonValueKind.Null) return false;
            Equal(Text(observed, field), expected, "dependency sample " + field);
            return true;
        }

        private static bool ProvisioningComplete(JsonElement identity)
        {
            var value = Property(identity, "isProvisioningComplete");
            Require(value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.False
                || value.ValueKind == JsonValueKind.True, "Invalid native Web provisioning state.");
            return value.ValueKind == JsonValueKind.True;
        }

        private void ValidateCapabilityListIdentity()
        {
            var library = SingleBy(receipts["capability-readiness"], "libraries", "rootPath", Binding.TargetListPath);
            Equal(Text(library, "webPath"), Binding.TargetWebPath, "capability List parent Web");
            Equal(Text(library, "listId"), Target.ListId, "capability List ID");
            Equal(Text(library, "verdict"), "pass", "capability List verdict");
            Require(Number(library, "readStatus") == 200, "The required capability List was not readable.");
            var preStatus = Number(library, "preStatus");
            Require(preStatus == 200 || preStatus == 404, "Capability List preflight was unavailable.");
            if (preStatus == 404)
            {
                Success(Property(library, "create"), "status", "requestGuid");
                ValidateCapabilityListPoll(Property(library, "readiness"));
            }
            else
            {
                Require(!library.TryGetProperty("create", out _) && !library.TryGetProperty("readiness", out _),
                    "An existing capability List cannot carry an unfenced creation branch.");
            }
            // The original existing-list receipt has no raw samples or request
            // ID. Do not invent them, nor interpret baseTemplate/content types:
            // domain readiness/fidelity remains with its existing lane validator.
        }

        private void ValidateCapabilityListPoll(JsonElement poll)
        {
            Equal(Text(poll, "kind"), "list-provision", "capability List poll protocol");
            Require(Number(poll, "elapsedMs") >= 0 && Number(poll, "elapsedMs") <= Number(poll, "timeoutMs"),
                "Capability List polling elapsed time is outside its bound.");
            var previousStreak = 0;
            var terminal = ValidatePoll(poll, "capability-readiness", 200, "list", (sample, isTerminal) =>
            {
                // The pinned native producer emits these six fields per GET.
                // Missing identity is not captured null; mixed error/HTTP or
                // unknown observation shapes cannot impersonate that protocol.
                Require(sample.EnumerateObject().Count() == 6, "Unknown or mixed capability List sample evidence.");
                var status = Number(sample, "status");
                Require(status >= 100 && status <= 599 && status != 401 && status != 403,
                    "Capability List readiness contains an unavailable/access-denied observation.");
                var sameIdentity = ObservedIdentity(sample, "objectId", Target.ListId);
                var streak = Number(sample, "readyStreak");
                Require(streak >= 0 && streak <= 3 && (streak == 0
                    || (status == 200 && sameIdentity && streak == previousStreak + 1)),
                    "Capability List stability contradicts its observed identity/status or preceding streak.");
                // Unlike native Web readiness, this producer stops at the
                // FIRST run of three ready observations. Earlier non-ready
                // samples may have null identity or incomplete path evidence.
                Require((streak == 3) == isTerminal,
                    "Capability List polling has no exact first stable terminal observation.");
                previousStreak = streak;
            });
            var body = Property(poll, "lastBody");
            Equal(Text(body, "Id"), Target.ListId, "created capability List ID");
            Equal(Text(terminal, "objectId"), Text(body, "Id"), "capability List terminal sample / body");
            Equal(Text(Property(body, "RootFolder"), "ServerRelativeUrl"), Binding.TargetListPath,
                "created capability List root");
        }

        public void ValidateFreshStorage()
        {
            ValidateOperations();
            ValidateOwnership();
            var created = Page("native-create");
            ValidateTargetIdentity(Property(created, "identity"));
            var page = Page("fresh-readback");
            Batch1SealedPlanEvidenceAdapter.ValidateProducer(Property(page, "producerRef"), Binding);
            Equal(Text(page, "verdict"), "pass", "fresh readback verdict");
            Equal(Text(page, "targetUrl"), Binding.TargetOrigin + Binding.TargetPath, "readback URL");
            Equal(Text(page, "sourceUrl"), Binding.SourcePage.PageUrl, "readback source URL");
            Equal(Text(Property(page, "pnpImplementation"), "commit"), Binding.Producer.ImplementationCommit, "readback producer");
            var identity = Property(page, "identity");
            Require(Flag(identity, "valid"), "The fresh File/ListItem identity did not verify.");
            ValidateTargetIdentity(identity);
            Text(identity, "requestGuid");
            var file = Property(identity, "fileStability");
            var item = Property(identity, "itemStability");
            ValidatePoll(file, "fresh-readback", 200, "list");
            ValidatePoll(item, "fresh-readback", 200, "list");
            var fileBody = Property(file, "lastBody");
            Equal(Text(fileBody, "ETag"), Target.ETag, "fresh target ETag");
            Equal(Text(fileBody, "UniqueId"), Target.FileUniqueId, "fresh File body ID");
            Equal(Text(fileBody, "ServerRelativeUrl"), Binding.TargetPath, "fresh File body path");
            var itemBody = Property(item, "lastBody");
            Require(Number(itemBody, "Id") == Target.ItemId, "Fresh item body ID mismatch.");
            Equal(Text(itemBody, "UniqueId"), Target.FileUniqueId, "fresh item body unique ID");
            Equal(Text(itemBody, "FileRef"), Binding.TargetPath, "fresh item body path");
            Equal(Text(Property(itemBody, "ParentList"), "Id"), Target.ListId, "fresh item parent List");
            Equal(Text(Property(itemBody, "File"), "UniqueId"), Target.FileUniqueId, "fresh item File");
            var observed = Time(page, "timestampUtc");
            InPhase("fresh-readback", observed);
            Require(observed >= Time(file, "finishedAtUtc") && observed >= Time(item, "finishedAtUtc"),
                "The readback predates its fresh storage observations.");
        }

        private void ValidateTargetIdentity(JsonElement identity)
        {
            Require(Number(identity, "fileStatus") == 200 && Number(identity, "itemStatus") == 200
                && Number(identity, "itemId") == Target.ItemId, "Target storage readback is incomplete.");
            Equal(Text(identity, "fileUniqueId"), Target.FileUniqueId, "target File ID");
            Equal(Text(identity, "itemUniqueId"), Target.FileUniqueId, "target item unique ID");
            Equal(Text(identity, "itemFileUniqueId"), Target.FileUniqueId, "target item File ID");
            Equal(Text(identity, "parentListId"), Target.ListId, "target parent List");
            Equal(Text(identity, "fileRef"), Binding.TargetPath, "target File path");
        }

        public void ValidateRuntimeCleanup()
        {
            ValidateFreshStorage();
            var page = Page("fresh-readback");
            var runtime = Property(page, "runtime");
            var observed = Time(runtime, "timestampUtc");
            InPhase("fresh-readback", observed);
            Require(observed >= Time(Property(Property(page, "identity"), "fileStability"), "finishedAtUtc")
                && observed >= Time(Property(Property(page, "identity"), "itemStability"), "finishedAtUtc")
                && observed <= Time(page, "timestampUtc"),
                "Runtime observation predates fresh storage identity.");
            Text(runtime, "requestGuid");
            var reachable = Number(runtime, "status") == 200
                && Text(runtime, "finalUrl") == Binding.TargetOrigin + Binding.TargetPath;
            var clear = !Flag(runtime, "containsAccessDenied") && !Flag(runtime, "containsServerError");
            var adapted = new RuntimeVerificationReceipt
            {
                PlanDigest = Binding.PlanDigest,
                TargetIdentity = Binding.Target.TargetIdentity,
                CompletedAtUtc = observed,
                Status = reachable && clear ? RuntimeVerificationStatus.Passed : RuntimeVerificationStatus.Failed,
                Results = Binding.RuntimeManifest.Requirements.Select(requirement => new RuntimeVerificationResult
                {
                    RequirementId = requirement.Id,
                    Passed = requirement.Kind == RuntimeVerificationRequirementKind.PageReachability ? reachable : clear,
                    EvidenceArtifactSha256 = artifacts["fresh-readback"].Sha256,
                    Message = "Original CCD-143 lifecycle observation; not native or visual acceptance."
                }).ToList()
            };
            RuntimeVerificationContractValidator.ValidateReceipt(Binding.RuntimeManifest, adapted,
                Binding.PlanDigest, Binding.Target.TargetIdentity, Time(receipts["fresh-readback"], "startedAtUtc"));
            RuntimeVerificationContractValidator.ValidateAggregateStatus(Binding.RuntimeManifest, adapted);
            Require(adapted.Status == RuntimeVerificationStatus.Passed,
                "EXTERNAL_LIFECYCLE_RUNTIME_UNAVAILABLE: denied, failed, redirected or error-shell observation.");
            ValidateCleanup();
        }

        private void ValidateCleanup()
        {
            var page = Page("cleanup");
            Equal(Text(page, "verdict"), "pass", "cleanup verdict");
            Require(Number(page, "preStatus") == 200 && Number(page, "itemStatus") == 200 && Flag(page, "identityMatches"),
                "Cleanup needs the original ownership-fenced delete, not an already-absent substitute.");
            foreach (var name in new[] { "ownedFileUniqueId", "observedFileUniqueId", "ownedItemUniqueId", "observedItemUniqueId" })
                Equal(Text(page, name), Target.FileUniqueId, "cleanup " + name);
            foreach (var name in new[] { "ownedParentListId", "observedParentListId" })
                Equal(Text(page, name), Target.ListId, "cleanup " + name);
            Require(Number(page, "ownedItemId") == Target.ItemId && Number(page, "observedItemId") == Target.ItemId,
                "Cleanup item identity mismatch.");
            Text(page, "preflightRequestGuid");
            Text(page, "itemRequestGuid");
            InPhase("cleanup", Time(page, "timestampUtc"));
            Success(page, "deleteStatus", "deleteRequestGuid");
            Require(Number(page, "postDeleteStatus") == 404, "Fresh post-delete page absence was not proved.");
            Text(page, "postDeleteRequestGuid");
            var site = SingleBy(receipts["cleanup"], "sites", "path", Binding.TargetSitePath);
            Equal(Text(site, "ownedSiteId"), Target.SiteId, "cleanup owned Site");
            Equal(Text(site, "observedSiteId"), Target.SiteId, "cleanup observed Site");
            Require(Flag(site, "identityMatches"), "Site cleanup ownership mismatch.");
            Success(site, "deleteStatus", "deleteRequestGuid");
            ValidatePoll(Property(site, "readiness"), "cleanup", 404, "cleanup");
            Require(Number(site, "postDeleteStatus") == 404, "Site cleanup is not complete.");
            var ownership = Property(receipts["cleanup"], "ownership");
            Success(ownership, "deleteStatus", "deleteRequestGuid");
            Require(Number(ownership, "postDeleteStatus") == 404, "Ownership marker cleanup is not complete.");
            var post = Page("post-cleanup");
            Require(Number(post, "status") == 404, "Fresh post-cleanup page absence was not proved.");
            Text(post, "requestGuid");
            InPhase("post-cleanup", Time(post, "timestampUtc"));
            var postSite = SingleBy(receipts["post-cleanup"], "sites", "path", Binding.TargetSitePath);
            Require(Number(postSite, "status") == 404, "Fresh post-cleanup Site absence was not proved.");
            Text(postSite, "requestGuid");
            var marker = Property(receipts["post-cleanup"], "ownership");
            Require(Number(marker, "status") == 404, "The post-cleanup marker is still present.");
            Equal(Text(marker, "path"), Text(plan.Input, "markerPath"), "post-cleanup marker");
        }

        private JsonElement Page(string phase)
        {
            var pages = Array(receipts[phase], "pages");
            Require(pages.Length == 1, "A lifecycle receipt must contain exactly the assessed page.");
            var page = pages[0];
            Require(Number(page, "rowNumber") == Binding.RowNumber, "Receipt page row mismatch.");
            Equal(Text(page, "claimId"), Binding.Identity.ClaimId, "receipt page claim");
            Equal(Text(page, "operationId"), Binding.NativeOperationId, "original page operation ID");
            Equal(Text(page, "actionId"), "ccd143-action-" + phase + "-" + Binding.NativeOperationId,
                "original page action ID");
            Equal(Text(page, "targetPath"), Binding.TargetPath, "receipt page target");
            if (phase == "native-create" || phase == "fresh-readback")
            {
                Equal(Text(page, "planDigest"), Binding.PlanDigest, "page plan digest");
                Equal(Text(page, "lifecycleDigest"), Binding.LifecycleDigest, "page lifecycle digest");
                Equal(Text(page, "preferredPath"), Text(plan.Page, "preferredPath"), "page preferred path");
                Equal(Text(page, "mappingReason"), Text(plan.Page, "mappingReason"), "page mapping policy");
            }
            if (phase != "post-cleanup")
            {
                Equal(Text(page, "ownershipMarker"), Binding.OwnershipMarker, "page ownership");
                Equal(Text(page, "targetMappingDigest"), Binding.TargetMappingDigest, "page mapping");
                Batch1SealedPlanEvidenceAdapter.ValidateSource(Property(page, "sourceVersion"), Binding, false);
            }
            return page;
        }

        private JsonElement ValidatePoll(JsonElement poll, string phase, int status, string policy,
            Action<JsonElement, bool> validateSample = null)
        {
            Equal(Text(poll, "state"), "ready", "bounded readback state");
            var limits = Property(Property(plan.Input, "pollPolicy"), policy);
            var attempts = Number(poll, "attempts");
            var samples = Array(poll, "samples");
            var started = Time(poll, "startedAtUtc");
            var finished = Time(poll, "finishedAtUtc");
            InPhase(phase, started);
            InPhase(phase, finished);
            Require(attempts > 0 && attempts <= Number(limits, "attempts") && samples.Length == attempts
                && finished >= started && (finished - started).TotalMilliseconds <= Number(limits, "timeoutMs")
                && Number(poll, "timeoutMs") == Number(limits, "timeoutMs"),
                "Missing, unbounded or stale retry/readback evidence.");
            var previous = started;
            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                var at = Time(sample, "atUtc");
                Require(Number(sample, "attempt") == index + 1 && at >= previous && at <= finished,
                    "Retry attempts are missing, reordered or outside their time fence.");
                Text(sample, "requestGuid");
                validateSample?.Invoke(sample, index == samples.Length - 1);
                previous = at;
            }
            Require(samples.Length >= 3 && samples.Skip(samples.Length - 3).All(sample => Number(sample, "status") == status),
                "The producer's three-observation stability fence is missing.");
            return samples[samples.Length - 1];
        }

        private void InPhase(string phase, DateTimeOffset observed)
        {
            Require(observed >= Time(receipts[phase], "startedAtUtc")
                && observed <= Time(receipts[phase], "finishedAtUtc"),
                "An observation is outside its original phase: " + phase);
        }

        private static JsonElement SingleBy(JsonElement receipt, string collection, string key, string value)
        {
            var matches = Array(receipt, collection).Where(item => Text(item, key) == value).ToArray();
            Require(matches.Length == 1, "Missing or duplicate target dependency: " + collection);
            return matches[0];
        }

        private static void Success(JsonElement value, string statusField, string requestField)
        {
            var status = Number(value, statusField);
            Require(status == 200 || status == 201 || status == 202 || status == 204,
                "A native lifecycle operation did not succeed: " + statusField);
            Text(value, requestField);
        }
    }
}
