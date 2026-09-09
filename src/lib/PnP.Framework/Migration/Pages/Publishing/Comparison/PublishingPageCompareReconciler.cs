using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Comparison
{
    public static class PublishingPageCompareReconciler
    {
        public static PublishingPageCompareReport Reconcile(
            PublishingPageCompareRequest request,
            IMigrationArtifactStore artifactStore = null)
        {
            ValidateRequest(request, artifactStore);

            var runtimeStatus = DeriveRuntimeStatus(request);
            var results = request.Ingredients
                .OrderBy(value => value.IngredientId, StringComparer.Ordinal)
                .Select(value => Classify(request, value, runtimeStatus))
                .ToList();
            var storageStatus = StorageStatus(request.ImportReceipt.StorageVerificationStatus);
            var acceptance = DeriveAcceptance(storageStatus, runtimeStatus, results);
            var report = new PublishingPageCompareReport
            {
                GeneratedAtUtc = request.GeneratedAtUtc,
                Producer = request.Producer,
                AssessmentHandoff = ProjectAssessmentHandoff(request),
                Bindings = request.Bindings,
                SourceVersionComparison = request.SourceVersionComparison,
                TargetIdentity = request.PlannedTargetIdentity,
                Storage = new CompareStorageSummary
                {
                    Status = storageStatus,
                    FreshReadback = request.ImportReceipt.FreshReadbackPassed
                },
                Runtime = new CompareRuntimeSummary { Status = runtimeStatus },
                Ingredients = results,
                Acceptance = acceptance
            };
            report.ReportDigestSha256 = ComputeReportDigest(report);
            return report;
        }

        public static string ComputeReportDigest(PublishingPageCompareReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var semantic = new
            {
                report.SchemaVersion,
                report.Producer,
                report.AssessmentHandoff,
                report.Bindings,
                report.SourceVersionComparison,
                report.TargetIdentity,
                report.Storage,
                report.Runtime,
                ingredients = (report.Ingredients ?? new List<IngredientCompareResult>())
                    .OrderBy(value => value.IngredientId, StringComparer.Ordinal)
                    .Select(value => new
                    {
                        value.IngredientId,
                        value.Kind,
                        value.Material,
                        lineage = new
                        {
                            value.Lineage.RequestedUrlHashSha256,
                            value.Lineage.SourceArtifactDigestSha256,
                            value.Lineage.SourceIngredientId,
                            value.Lineage.ActionId,
                            value.Lineage.TargetIdentity,
                            evidenceRefs = (value.Lineage.EvidenceRefs ?? new List<string>()).OrderBy(item => item, StringComparer.Ordinal).ToList(),
                            causeIngredientIds = (value.Lineage.CauseIngredientIds ?? new List<string>()).OrderBy(item => item, StringComparer.Ordinal).ToList()
                        },
                        value.Expected,
                        value.Actual,
                        value.ResultClass,
                        value.ReasonCode
                    })
                    .ToList(),
                report.Acceptance
            };
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(semantic));
        }

        private static void ValidateRequest(
            PublishingPageCompareRequest request,
            IMigrationArtifactStore artifactStore)
        {
            if (request == null || request.Package == null || request.ImportReceipt == null)
            {
                throw new InvalidDataException("Compare reconciliation requires a migration package and import receipt.");
            }
            PublishingPagePackageValidator.ValidateMigration(request.Package, artifactStore);
            Require(request.Producer != null
                && !string.IsNullOrWhiteSpace(request.Producer.Id)
                && !string.IsNullOrWhiteSpace(request.Producer.Version)
                && !string.IsNullOrWhiteSpace(request.Producer.ImplementationRef),
                "Compare producer identity is incomplete.");
            ValidateAssessmentHandoff(request);
            Require(request.Bindings != null && request.SourceVersionComparison != null
                && request.PlannedTargetIdentity != null && request.Ingredients != null,
                "Compare bindings, source version, target identity, and ingredients are required.");
            ValidateDigest(request.Bindings.ManifestDigestSha256, "manifest digest");
            ValidateDigest(request.Bindings.SourceCaptureReceiptDigestSha256, "source capture receipt digest");
            ValidateDigest(request.Bindings.SnapshotDigestSha256, "snapshot digest");
            ValidateDigest(request.Bindings.PlanDigestSha256, "plan digest");
            ValidateDigest(request.Bindings.AdmittedPlanDigestSha256, "admitted plan digest");
            ValidateDigest(request.Bindings.SourceVersionDigestSha256, "source version digest");
            ValidateDigest(request.ImportReceiptDigestSha256, "import receipt digest");
            Require(string.Equals(request.Bindings.ExportSchemaVersion, request.Package.ExportSchemaVersion, StringComparison.Ordinal)
                && string.Equals(request.Bindings.MigrationPackageSchemaVersion, request.Package.SchemaVersion, StringComparison.Ordinal)
                && string.Equals(request.Bindings.IngredientGraphSchemaVersion, request.Package.Plan.IngredientGraph.SchemaVersion, StringComparison.Ordinal)
                && string.Equals(request.Bindings.IngredientProjectionVersion, request.Package.Plan.IngredientGraph.ProjectionVersion, StringComparison.Ordinal),
                "Compare package or ingredient-graph bindings are inconsistent.");
            Require(string.Equals(request.Bindings.PlanDigestSha256, request.Package.PlanDigest, StringComparison.OrdinalIgnoreCase),
                "Compare plan binding does not match the migration package.");
            Require(string.Equals(request.Bindings.SnapshotDigestSha256, request.Package.SnapshotDigest, StringComparison.OrdinalIgnoreCase),
                "Compare snapshot binding does not match the migration package.");
            var admittedPlanDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                request.AdmittedPlan,
                request.Package.PlanDigest,
                request.PlannedTargetIdentity.CanonicalIdentity);
            Require(string.Equals(request.AdmittedPlanDigestSha256, admittedPlanDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.Bindings.AdmittedPlanDigestSha256, admittedPlanDigest, StringComparison.OrdinalIgnoreCase),
                "The compare chain is not bound to the exact target-admitted repro plan.");
            Require(AdmittedReproExecutionPlanValidator.SameOperations(
                    request.Bindings.Operations,
                    request.AdmittedPlan.Operations),
                "The compare operation binding is missing, foreign, or stale.");
            Require(string.Equals(request.ImportReceipt.SchemaVersion, PublishingPagePackageContract.ReceiptSchemaVersion, StringComparison.Ordinal),
                "The import receipt schema is unsupported.");
            Require(string.Equals(request.Bindings.ImportReceiptSchemaVersion, request.ImportReceipt.SchemaVersion, StringComparison.Ordinal),
                "The import receipt schema binding is inconsistent.");
            Require(string.Equals(request.ImportReceipt.ApprovedPlanDigest, request.Package.PlanDigest, StringComparison.OrdinalIgnoreCase),
                "The import receipt is not bound to the approved plan.");
            Require(string.Equals(request.ImportReceipt.AdmittedPlanDigestSha256, admittedPlanDigest, StringComparison.OrdinalIgnoreCase)
                && request.ImportReceipt.OperationId == request.AdmittedPlan.Operations.MutationOperationId
                && AdmittedReproExecutionPlanValidator.SameOperations(request.ImportReceipt.Operations, request.AdmittedPlan.Operations)
                && AdmittedReproExecutionPlanValidator.SameSourceVersion(request.ImportReceipt.SourceVersion, request.AdmittedPlan.SourceVersion),
                "The import receipt is missing, foreign, or stale for the admitted execution plan.");
            Require(!request.ImportReceipt.PartialExecution
                && request.ImportReceipt.ExecutionStatus == PnP.Framework.Migration.Execution.MigrationExecutionStatus.Succeeded,
                "A partial or incomplete import receipt cannot enter admitted compare.");
            Require(request.ImportReceipt.StorageVerificationStatus != StorageVerificationStatus.Passed
                || request.ImportReceipt.FreshReadbackPassed,
                "Storage cannot pass without a successful fresh readback.");
            ValidateContractDigest(request.ImportReceipt, request.ImportReceiptDigestSha256, "import receipt");
            Require(string.Equals(request.Bindings.ImportReceiptDigestSha256, request.ImportReceiptDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The import receipt digest binding is inconsistent.");
            ValidateTargetIdentity(request);
            ValidateSourceVersion(request.SourceVersionComparison);
            Require(string.Equals(request.SourceVersionComparison.IdentityDigestSha256,
                    request.AdmittedPlan.SourceVersion.IdentityDigestSha256,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.SourceVersionComparison.ObservedCompositeDigestSha256,
                    request.AdmittedPlan.SourceVersion.VersionDigestSha256,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.Bindings.SourceVersionDigestSha256,
                    request.AdmittedPlan.SourceVersion.VersionDigestSha256,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.SourceVersionComparison.ObservedETag,
                    request.AdmittedPlan.SourceVersion.ETag,
                    StringComparison.Ordinal)
                && Nullable.Equals(request.SourceVersionComparison.ObservedLastModifiedUtc,
                    request.AdmittedPlan.SourceVersion.LastModifiedUtc)
                && string.Equals(request.SourceVersionComparison.ObservedVersionLabel,
                    request.AdmittedPlan.SourceVersion.VersionLabel,
                    StringComparison.Ordinal)
                && request.SourceVersionComparison.ObservedAtUtc.Equals(
                    request.AdmittedPlan.SourceVersion.ObservedAtUtc),
                "The compare source-version evidence is missing, foreign, or stale.");
            Require(string.Equals(request.SourceAuthState, "valid", StringComparison.Ordinal)
                || string.Equals(request.SourceAuthState, "expired", StringComparison.Ordinal),
                "The source authentication state is unsupported.");
            ValidateIngredients(request);
            ValidateRuntime(request, artifactStore);
        }

        private static void ValidateAssessmentHandoff(PublishingPageCompareRequest request)
        {
            var handoff = request.AssessmentHandoff;
            Require(handoff != null, "The Assessment capture handoff is required.");
            Require(string.Equals(handoff.Schema, PublishingPageCompareContract.AssessmentHandoffSchemaVersion, StringComparison.Ordinal),
                "The Assessment capture handoff schema is unsupported.");
            Require(string.Equals(request.AssessmentProducerRevisionId, PublishingPageCompareContract.AssessmentProducerRevisionId, StringComparison.Ordinal),
                "The Assessment producer revision is not the accepted revision.");
            Require(!string.IsNullOrWhiteSpace(handoff.ManifestRevisionId)
                && !string.IsNullOrWhiteSpace(handoff.AllowlistRevisionId)
                && !string.IsNullOrWhiteSpace(handoff.SampleId)
                && !string.IsNullOrWhiteSpace(handoff.CanonicalLocatorRef)
                && !string.IsNullOrWhiteSpace(handoff.StratumId),
                "The Assessment capture handoff identity is incomplete.");
            ValidatePrefixedDigest(handoff.ManifestRevisionId, "sha256:", "Assessment manifest revision");
            ValidatePrefixedDigest(handoff.AllowlistRevisionId, "sha256:", "Assessment allowlist revision");
            ValidatePrefixedDigest(handoff.CanonicalLocatorHash, "sha256:", "Assessment canonical locator hash");
            ValidatePrefixedDigest(handoff.ResourceIdentityHash, "sha256:", "Assessment resource identity hash");
            ValidatePrefixedDigest(handoff.ApprovedHostHash, "hmac-sha256:", "Assessment approved host hash");
            Require(handoff.StageMembership != null && handoff.StageMembership.Count > 0
                && handoff.StageMembership.All(value => string.Equals(value, "A0", StringComparison.Ordinal)
                    || string.Equals(value, "A1", StringComparison.Ordinal))
                && handoff.StageMembership.Distinct(StringComparer.Ordinal).Count() == handoff.StageMembership.Count,
                "The Assessment stage membership is unsupported.");
            Require(new[] { "publishing_page", "enterprise_wiki", "web_part_page", "aspx_unknown" }
                    .Contains(handoff.ExpectedProfile, StringComparer.Ordinal),
                "The Assessment expected profile is unsupported.");
            Require(new[] { "known_readable", "known_forbidden", "auth_required", "inherited_unknown", "unknown" }
                    .Contains(handoff.PermissionSignal, StringComparer.Ordinal),
                "The Assessment permission signal is unsupported.");
            Require(new[] { "none_observed", "same_host", "cross_host", "login_or_challenge", "loop", "unknown" }
                    .Contains(handoff.RedirectSignal, StringComparer.Ordinal),
                "The Assessment redirect signal is unsupported.");
            Require(handoff.UnknownSignals != null
                && handoff.UnknownSignals.All(value => !string.IsNullOrWhiteSpace(value)),
                "The Assessment unknown signals are invalid.");
            var policy = handoff.RequestPolicy;
            Require(policy != null
                && policy.Methods != null
                && policy.Methods.Count > 0
                && policy.Methods.All(value => string.Equals(value, "HEAD", StringComparison.Ordinal)
                    || string.Equals(value, "GET", StringComparison.Ordinal))
                && policy.Methods.Distinct(StringComparer.Ordinal).Count() == policy.Methods.Count
                && !policy.AutoDiscovery
                && !policy.MutationAllowed
                && string.Equals(policy.FollowRedirects, "same-approved-host-only", StringComparison.Ordinal)
                && policy.MaxRedirects >= 0
                && policy.MaxRedirects <= 3,
                "The Assessment request policy expands the accepted read-only boundary.");
            ValidateDigest(request.AssessmentHandoffDigestSha256, "Assessment handoff digest");
            ValidateContractDigest(handoff, request.AssessmentHandoffDigestSha256, "Assessment handoff");
        }

        private static void ValidateSourceVersion(SourceVersionComparison sourceVersion)
        {
            ValidateDigest(sourceVersion.IdentityDigestSha256, "source version identity digest");
            ValidateDigest(sourceVersion.ExpectedCompositeDigestSha256, "expected source version digest");
            var allowed = new[] { "matched", "changed", "not-checked", "unknown" };
            Require(allowed.Contains(sourceVersion.Status, StringComparer.Ordinal), "The source-version status is unsupported.");
            if (string.Equals(sourceVersion.Status, "matched", StringComparison.Ordinal)
                || string.Equals(sourceVersion.Status, "changed", StringComparison.Ordinal))
            {
                ValidateDigest(sourceVersion.ObservedCompositeDigestSha256, "observed source version digest");
                var equal = string.Equals(sourceVersion.ExpectedCompositeDigestSha256, sourceVersion.ObservedCompositeDigestSha256, StringComparison.OrdinalIgnoreCase);
                Require(equal == string.Equals(sourceVersion.Status, "matched", StringComparison.Ordinal),
                    "The source-version status does not agree with the bound digests.");
            }
            Require(sourceVersion.ObservedAtUtc != default,
                "The current source-version recheck time is required.");
            Require(!string.IsNullOrWhiteSpace(sourceVersion.ObservedETag)
                || sourceVersion.ObservedLastModifiedUtc.HasValue
                || !string.IsNullOrWhiteSpace(sourceVersion.ObservedVersionLabel),
                "The current source-version recheck requires an ETag, Last-Modified value, or version label.");
        }

        private static void ValidateTargetIdentity(PublishingPageCompareRequest request)
        {
            var target = request.PlannedTargetIdentity;
            ValidateDigest(target.WebUrlHashSha256, "target Web URL hash");
            ValidateDigest(target.PageServerRelativeUrlHashSha256, "target page URL hash");
            Require(!string.IsNullOrWhiteSpace(target.CanonicalIdentity), "A canonical planned target identity is required.");
            var receiptIdentity = CanonicalTargetIdentity(
                request.ImportReceipt.TargetWebUrl,
                request.ImportReceipt.TargetPageServerRelativeUrl);
            Require(string.Equals(receiptIdentity, target.CanonicalIdentity, StringComparison.Ordinal),
                "The import receipt target identity is not the planned target identity.");
            Require(target.ListItemId == request.ImportReceipt.TargetListItemId
                && target.FileUniqueId.GetValueOrDefault() == request.ImportReceipt.TargetFileUniqueId,
                "The import receipt item identity is not the planned target identity.");
        }

        private static void ValidateIngredients(PublishingPageCompareRequest request)
        {
            var duplicate = request.Ingredients
                .GroupBy(value => value?.IngredientId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            Require(duplicate == null && request.Ingredients.All(value => value != null),
                "Every ingredient must have exactly one compare observation.");
            var sourceIngredientIds = new HashSet<string>(
                request.Package.Plan.IngredientGraph.Nodes.Select(value => value.Id),
                StringComparer.Ordinal);
            var observedSourceIds = new HashSet<string>(
                request.Ingredients.Where(value => !value.UnexpectedExtra).Select(value => value.IngredientId),
                StringComparer.Ordinal);
            Require(sourceIngredientIds.SetEquals(observedSourceIds),
                "Every source ingredient must have exactly one result; only explicit unexpected-extra rows may extend the set.");
            foreach (var ingredient in request.Ingredients)
            {
                Require(ingredient.Lineage != null
                    && ingredient.Expected != null
                    && ingredient.Actual != null
                    && !string.IsNullOrWhiteSpace(ingredient.Kind)
                    && !string.IsNullOrWhiteSpace(ingredient.Lineage.SourceIngredientId),
                    "Every ingredient requires typed source lineage.");
                ValidateDigest(ingredient.Lineage.RequestedUrlHashSha256, "ingredient requested URL hash");
                ValidateDigest(ingredient.Lineage.SourceArtifactDigestSha256, "ingredient source artifact digest");
                ValidateOptionalDigest(ingredient.Expected?.RawDigestSha256, "expected raw ingredient digest");
                ValidateOptionalDigest(ingredient.Expected?.CanonicalDigestSha256, "expected canonical ingredient digest");
                ValidateOptionalDigest(ingredient.Actual?.RawDigestSha256, "actual raw ingredient digest");
                ValidateOptionalDigest(ingredient.Actual?.CanonicalDigestSha256, "actual canonical ingredient digest");
                ValidateOptionalDigest(ingredient.ApprovedTransformedCanonicalDigestSha256, "approved transformed ingredient digest");
                if (ingredient.Material)
                {
                    Require(ingredient.Lineage.EvidenceRefs != null && ingredient.Lineage.EvidenceRefs.Count > 0,
                        "Every material ingredient requires evidence lineage.");
                }
                Require(ingredient.Lineage.EvidenceRefs == null
                    || ingredient.Lineage.EvidenceRefs.All(IsDigestBoundReference),
                    "Ingredient evidence references must be digest-bound.");
                if (!ingredient.UnexpectedExtra)
                {
                    Require(string.Equals(ingredient.Lineage.SourceIngredientId, ingredient.IngredientId, StringComparison.Ordinal),
                        "A source ingredient result must retain its stable source ingredient ID.");
                    var action = request.Package.Plan.IngredientActions
                        .SingleOrDefault(value => string.Equals(value.IngredientId, ingredient.IngredientId, StringComparison.Ordinal));
                    Require(action == null
                            ? string.IsNullOrWhiteSpace(ingredient.Lineage.ActionId)
                            : string.Equals(action.ActionId, ingredient.Lineage.ActionId, StringComparison.Ordinal)
                                && string.Equals(action.TargetIdentity, ingredient.Lineage.TargetIdentity, StringComparison.Ordinal),
                        "Ingredient action lineage does not match the sealed repro plan.");
                }
            }
        }

        private static void ValidateRuntime(
            PublishingPageCompareRequest request,
            IMigrationArtifactStore artifactStore)
        {
            var manifest = request.Package.Plan.RuntimeVerification;
            Require(manifest != null
                && string.Equals(manifest.SchemaVersion, PublishingPageCompareContract.RuntimeManifestSchemaVersion, StringComparison.Ordinal),
                "The runtime verification manifest schema is unsupported.");
            var required = manifest.Requirements.Where(value => value.Required).ToList();
            if (request.RuntimeReceipt == null)
            {
                Require(string.IsNullOrWhiteSpace(request.RuntimeReceiptDigestSha256)
                    && string.IsNullOrWhiteSpace(request.Bindings.RuntimeReceiptDigestSha256)
                    && string.IsNullOrWhiteSpace(request.Bindings.RuntimeReceiptSchemaVersion),
                    "An absent runtime receipt cannot carry receipt bindings.");
                return;
            }

            ValidateDigest(request.RuntimeReceiptDigestSha256, "runtime receipt digest");
            Require(string.Equals(request.RuntimeReceipt.SchemaVersion, PublishingPageCompareContract.RuntimeReceiptSchemaVersion, StringComparison.Ordinal),
                "The runtime receipt schema is unsupported.");
            Require(string.Equals(request.Bindings.RuntimeReceiptSchemaVersion, request.RuntimeReceipt.SchemaVersion, StringComparison.Ordinal)
                && string.Equals(request.Bindings.RuntimeReceiptDigestSha256, request.RuntimeReceiptDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The runtime receipt binding is inconsistent.");
            ValidateContractDigest(request.RuntimeReceipt, request.RuntimeReceiptDigestSha256, "runtime receipt");
            Require(string.Equals(request.RuntimeReceipt.PlanDigest, request.Package.PlanDigest, StringComparison.OrdinalIgnoreCase),
                "The runtime receipt is not bound to the approved plan.");
            Require(string.Equals(request.RuntimeReceipt.AdmittedPlanDigestSha256, request.AdmittedPlanDigestSha256, StringComparison.OrdinalIgnoreCase)
                && request.RuntimeReceipt.OperationId == request.AdmittedPlan.Operations.RuntimeOperationId
                && string.Equals(request.RuntimeReceipt.ImportReceiptDigestSha256, request.ImportReceiptDigestSha256, StringComparison.OrdinalIgnoreCase)
                && AdmittedReproExecutionPlanValidator.SameOperations(request.RuntimeReceipt.Operations, request.AdmittedPlan.Operations)
                && AdmittedReproExecutionPlanValidator.SameSourceVersion(request.RuntimeReceipt.SourceVersion, request.AdmittedPlan.SourceVersion),
                "The runtime receipt is missing, foreign, or stale for the admitted execution plan.");
            Require(string.Equals(request.RuntimeReceipt.TargetIdentity, request.PlannedTargetIdentity.CanonicalIdentity, StringComparison.Ordinal),
                "The runtime receipt is not bound to the planned target identity.");

            RuntimeVerificationReceiptValidator.ValidateEvidence(
                request.RuntimeReceipt,
                manifest,
                request.Producer.ImplementationRef,
                artifactStore);

            var results = request.RuntimeReceipt.Results ?? new List<RuntimeVerificationResult>();
            var duplicate = results.GroupBy(value => value?.RequirementId, StringComparer.Ordinal)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
            Require(duplicate == null && results.All(value => value != null),
                "Runtime results contain a missing or duplicate requirement ID.");
            var known = new HashSet<string>(manifest.Requirements.Select(value => value.Id), StringComparer.Ordinal);
            Require(results.All(value => known.Contains(value.RequirementId)),
                "Runtime results contain an unknown requirement ID.");
            Require(required.All(value => results.Count(result => string.Equals(result.RequirementId, value.Id, StringComparison.Ordinal)) == 1),
                "The runtime receipt does not cover every required requirement exactly once.");
            foreach (var result in results)
            {
                ValidateDigest(result.EvidenceArtifactSha256, "runtime evidence digest");
            }
            var allRequiredPassed = required.All(requirement =>
                results.Single(result => string.Equals(result.RequirementId, requirement.Id, StringComparison.Ordinal)).Passed);
            Require((allRequiredPassed && request.RuntimeReceipt.Status == RuntimeVerificationStatus.Passed)
                || (!allRequiredPassed && request.RuntimeReceipt.Status == RuntimeVerificationStatus.Failed)
                || (required.Count == 0 && request.RuntimeReceipt.Status == RuntimeVerificationStatus.NotRequired),
                "The runtime receipt status does not agree with required results.");
        }

        internal static IngredientCompareResult Classify(
            PublishingPageCompareRequest request,
            IngredientCompareObservation ingredient,
            string runtimeStatus)
        {
            var result = Project(ingredient);
            if (!string.IsNullOrWhiteSpace(ingredient.UnknownReasonCode))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.Unknown, ingredient.UnknownReasonCode);
                return result;
            }
            if (string.Equals(request.SourceAuthState, "expired", StringComparison.Ordinal))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.AuthExpired, PublishingPageCompareContract.ReasonCodes.SourceAuthExpired);
                return result;
            }
            if (string.Equals(request.AssessmentHandoff.PermissionSignal, "known_forbidden", StringComparison.Ordinal))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.AuthorizationBlocked, PublishingPageCompareContract.ReasonCodes.SourcePermissionDenied);
                return result;
            }
            if (string.Equals(request.AssessmentHandoff.PermissionSignal, "auth_required", StringComparison.Ordinal))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.AuthExpired, PublishingPageCompareContract.ReasonCodes.SourceAuthExpired);
                return result;
            }
            if (string.Equals(request.AssessmentHandoff.PermissionSignal, "inherited_unknown", StringComparison.Ordinal)
                || string.Equals(request.AssessmentHandoff.PermissionSignal, "unknown", StringComparison.Ordinal))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.Unknown, PublishingPageCompareContract.ReasonCodes.SourcePermissionUnknown);
                return result;
            }
            if (ingredient.SourceVersionSensitive
                && string.Equals(request.SourceVersionComparison.Status, "changed", StringComparison.Ordinal))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.SourceVersionChanged, PublishingPageCompareContract.ReasonCodes.SourceVersionDrift);
                return result;
            }

            var frontier = request.Package.Plan.ExecutionFrontier.Decisions.SingleOrDefault(value =>
                string.Equals(value.IngredientId, ingredient.IngredientId, StringComparison.Ordinal));
            if (frontier != null)
            {
                result.Lineage.CauseIngredientIds = (frontier.CauseIngredientIds ?? new List<string>())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
            }
            if (frontier?.State == PageIngredientExecutionState.Deferred
                || frontier?.State == PageIngredientExecutionState.SkippedByDeferredDependency)
            {
                Set(result, PublishingPageCompareContract.ResultClasses.Deferred, PublishingPageCompareContract.ReasonCodes.FrontierDeferred);
                return result;
            }
            if (frontier?.State == PageIngredientExecutionState.AuthorizationBlocked
                || frontier?.State == PageIngredientExecutionState.SkippedByAuthorizationDependency)
            {
                Set(result, PublishingPageCompareContract.ResultClasses.AuthorizationBlocked, PublishingPageCompareContract.ReasonCodes.FrontierAuthorizationBlocked);
                return result;
            }
            if (!string.IsNullOrWhiteSpace(ingredient.RuntimeRequirementId)
                && string.Equals(runtimeStatus, "pending", StringComparison.Ordinal))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.RuntimePending, PublishingPageCompareContract.ReasonCodes.RuntimeReceiptPending);
                return result;
            }
            if (!ingredient.TargetEvidenceComplete)
            {
                Set(result, PublishingPageCompareContract.ResultClasses.Unknown, PublishingPageCompareContract.ReasonCodes.EvidenceIncomplete);
                return result;
            }
            if (ingredient.UnexpectedExtra)
            {
                Set(result, PublishingPageCompareContract.ResultClasses.UnexpectedExtra, PublishingPageCompareContract.ReasonCodes.TargetUnexpectedExtra);
                return result;
            }
            if (!ingredient.TargetPresent)
            {
                Set(result, PublishingPageCompareContract.ResultClasses.Missing, PublishingPageCompareContract.ReasonCodes.TargetMissing);
                return result;
            }
            if (DigestEquals(ingredient.Expected.RawDigestSha256, ingredient.Actual.RawDigestSha256))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.Exact, PublishingPageCompareContract.ReasonCodes.ExactRawDigest);
                return result;
            }
            if (DigestEquals(ingredient.Expected.CanonicalDigestSha256, ingredient.Actual.CanonicalDigestSha256))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.CanonicalEquivalent, PublishingPageCompareContract.ReasonCodes.CanonicalRulesEquivalent);
                return result;
            }
            if (DigestEquals(ingredient.ApprovedTransformedCanonicalDigestSha256, ingredient.Actual.CanonicalDigestSha256))
            {
                Set(result, PublishingPageCompareContract.ResultClasses.TransformedAsPlanned, PublishingPageCompareContract.ReasonCodes.ApprovedTransformMatched);
                return result;
            }
            if (!ingredient.AssertionApplicable)
            {
                Set(result, PublishingPageCompareContract.ResultClasses.NotApplicable, PublishingPageCompareContract.ReasonCodes.AssertionNotApplicable);
                return result;
            }
            Set(result, PublishingPageCompareContract.ResultClasses.Mismatch, PublishingPageCompareContract.ReasonCodes.TargetMismatch);
            return result;
        }

        private static CompareAcceptance DeriveAcceptance(
            string storageStatus,
            string runtimeStatus,
            IList<IngredientCompareResult> ingredients)
        {
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            string verdict;
            if (string.Equals(storageStatus, "failed", StringComparison.Ordinal))
            {
                verdict = "fail";
                reasons.Add(PublishingPageCompareContract.ReasonCodes.StorageFailed);
            }
            else if (string.Equals(storageStatus, "not-run", StringComparison.Ordinal))
            {
                verdict = "unverified";
                reasons.Add(PublishingPageCompareContract.ReasonCodes.StorageNotRun);
            }
            else if (string.Equals(runtimeStatus, "failed", StringComparison.Ordinal))
            {
                verdict = "fail";
                reasons.Add(PublishingPageCompareContract.ReasonCodes.RuntimeFailed);
            }
            else if (string.Equals(runtimeStatus, "pending", StringComparison.Ordinal)
                || string.Equals(runtimeStatus, "not-run", StringComparison.Ordinal))
            {
                verdict = "unverified";
                reasons.Add(PublishingPageCompareContract.ReasonCodes.RuntimeReceiptPending);
            }
            else if (ingredients.Any(value => value.ResultClass == PublishingPageCompareContract.ResultClasses.SourceVersionChanged
                || value.ResultClass == PublishingPageCompareContract.ResultClasses.AuthExpired
                || value.ResultClass == PublishingPageCompareContract.ResultClasses.Unknown
                || value.ResultClass == PublishingPageCompareContract.ResultClasses.RuntimePending))
            {
                verdict = "unverified";
                reasons.Add(PublishingPageCompareContract.ReasonCodes.MaterialUnknown);
            }
            else if (ingredients.Any(value => value.Material
                && (value.ResultClass == PublishingPageCompareContract.ResultClasses.Missing
                    || value.ResultClass == PublishingPageCompareContract.ResultClasses.UnexpectedExtra
                    || value.ResultClass == PublishingPageCompareContract.ResultClasses.Mismatch)))
            {
                verdict = "fail";
                reasons.Add(PublishingPageCompareContract.ReasonCodes.MaterialGap);
            }
            else if (ingredients.Any(value => value.Material
                && (value.ResultClass == PublishingPageCompareContract.ResultClasses.Deferred
                    || value.ResultClass == PublishingPageCompareContract.ResultClasses.AuthorizationBlocked)))
            {
                verdict = "conditional";
                reasons.Add(PublishingPageCompareContract.ReasonCodes.MaterialGap);
            }
            else
            {
                verdict = "pass";
            }
            return new CompareAcceptance
            {
                Verdict = verdict,
                ReasonCodes = reasons.ToList(),
                StorageStatus = storageStatus,
                RuntimeStatus = runtimeStatus
            };
        }

        private static string DeriveRuntimeStatus(PublishingPageCompareRequest request)
        {
            var required = request.Package.Plan.RuntimeVerification.Requirements.Any(value => value.Required);
            if (!required)
            {
                return "not-required";
            }
            if (request.RuntimeReceipt == null)
            {
                return "pending";
            }
            return request.RuntimeReceipt.Status == RuntimeVerificationStatus.Passed ? "passed"
                : request.RuntimeReceipt.Status == RuntimeVerificationStatus.Failed ? "failed"
                : request.RuntimeReceipt.Status == RuntimeVerificationStatus.Pending ? "pending"
                : "not-run";
        }

        private static string StorageStatus(StorageVerificationStatus status)
        {
            return status == StorageVerificationStatus.Passed ? "passed"
                : status == StorageVerificationStatus.Failed ? "failed"
                : "not-run";
        }

        private static IngredientCompareResult Project(IngredientCompareObservation value)
        {
            return new IngredientCompareResult
            {
                IngredientId = value.IngredientId,
                Kind = value.Kind,
                Material = value.Material,
                Lineage = new IngredientCompareLineage
                {
                    RequestedUrlHashSha256 = value.Lineage.RequestedUrlHashSha256,
                    SourceArtifactDigestSha256 = value.Lineage.SourceArtifactDigestSha256,
                    SourceIngredientId = value.Lineage.SourceIngredientId,
                    ActionId = value.Lineage.ActionId,
                    TargetIdentity = value.Lineage.TargetIdentity,
                    EvidenceRefs = (value.Lineage.EvidenceRefs ?? new List<string>()).OrderBy(item => item, StringComparer.Ordinal).ToList(),
                    CauseIngredientIds = (value.Lineage.CauseIngredientIds ?? new List<string>()).OrderBy(item => item, StringComparer.Ordinal).ToList()
                },
                Expected = value.Expected,
                Actual = value.Actual,
                Message = value.Message
            };
        }

        private static AssessmentHandoffProjection ProjectAssessmentHandoff(PublishingPageCompareRequest request)
        {
            return new AssessmentHandoffProjection
            {
                SchemaVersion = request.AssessmentHandoff.Schema,
                ProducerRevisionId = request.AssessmentProducerRevisionId,
                ConformanceRevisionId = PublishingPageCompareContract.AssessmentConformanceRevisionId,
                HandoffDigestSha256 = request.AssessmentHandoffDigestSha256,
                ManifestRevisionId = request.AssessmentHandoff.ManifestRevisionId,
                AllowlistRevisionId = request.AssessmentHandoff.AllowlistRevisionId,
                SelectionId = request.AssessmentHandoff.SampleId,
                CanonicalLocatorHash = request.AssessmentHandoff.CanonicalLocatorHash,
                ResourceIdentityHash = request.AssessmentHandoff.ResourceIdentityHash,
                ApprovedHostHash = request.AssessmentHandoff.ApprovedHostHash,
                PermissionSignal = request.AssessmentHandoff.PermissionSignal,
                DiscoveryObservationStatus = PublishingPageCompareContract.UnavailableByProducerContract,
                ProducerAttestationStatus = PublishingPageCompareContract.UnavailableByProducerContract,
                CoverageStatus = PublishingPageCompareContract.UnavailableByProducerContract
            };
        }

        private static void Set(IngredientCompareResult result, string resultClass, string reasonCode)
        {
            result.ResultClass = resultClass;
            result.ReasonCode = reasonCode;
        }

        private static bool DigestEquals(string expected, string actual)
        {
            return !string.IsNullOrWhiteSpace(expected)
                && !string.IsNullOrWhiteSpace(actual)
                && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateContractDigest<T>(T value, string expected, string name)
        {
            var actual = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value));
            Require(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                "The " + name + " digest does not match its payload.");
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex), "The " + name + " must be a SHA-256 digest.");
        }

        private static void ValidateOptionalDigest(string value, string name)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidateDigest(value, name);
            }
        }

        private static void ValidatePrefixedDigest(string value, string prefix, string name)
        {
            Require(value != null
                && value.StartsWith(prefix, StringComparison.Ordinal)
                && value.Length == prefix.Length + 64
                && value.Substring(prefix.Length).All(IsHex),
                "The " + name + " must be a " + prefix.TrimEnd(':') + " digest.");
        }

        private static bool IsDigestBoundReference(string value)
        {
            const string prefix = "sha256:";
            return value != null
                && value.StartsWith(prefix, StringComparison.Ordinal)
                && value.Length == prefix.Length + 64
                && value.Substring(prefix.Length).All(IsHex);
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }

        private static string CanonicalTargetIdentity(string webUrl, string pageServerRelativeUrl)
        {
            Require(Uri.TryCreate(webUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(pageServerRelativeUrl),
                "The import receipt target URL identity is incomplete.");
            return uri.Scheme.ToLowerInvariant() + "://" + uri.Authority.ToLowerInvariant()
                + "/" + pageServerRelativeUrl.Trim().TrimStart('/').ToLowerInvariant();
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
