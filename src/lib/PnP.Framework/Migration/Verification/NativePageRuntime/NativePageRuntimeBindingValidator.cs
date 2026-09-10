using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Migration.Verification.NativePageRuntime
{
    public static class NativePageRuntimeBindingValidator
    {
        public static string SealBinding(NativePageRuntimeBinding binding)
        {
            Require(binding != null, "A native page runtime binding is required.");
            binding.ContentSha256 = ComputeSeal(binding, nameof(NativePageRuntimeBinding.ContentSha256));
            return binding.ContentSha256;
        }

        public static string ValidateBindingAndComputeDigest(
            NativePageRuntimeBinding binding,
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256,
            NativePageImportReceiptAggregate importAggregate,
            ProducerBuildProvenanceManifest provenanceManifest,
            IMigrationArtifactStore artifactStore,
            INativePageRuntimeIdentityEvidenceVerifier identityEvidenceVerifier)
        {
            Require(binding != null, "A native page runtime binding is required.");
            Require(string.Equals(binding.SchemaVersion, NativePageRuntimeContract.BindingSchemaVersion, StringComparison.Ordinal),
                "The native page runtime binding schema is unsupported.");
            ValidateDigest(binding.ContentSha256, "native runtime binding content seal");
            Require(DigestEquals(binding.ContentSha256, ComputeSeal(binding, nameof(NativePageRuntimeBinding.ContentSha256))),
                "The native runtime binding content seal is stale or corrupt.");
            Require(binding.RunId != Guid.Empty && string.Equals(binding.ClaimId, NativePageRuntimeContract.ClaimId, StringComparison.Ordinal),
                "The native runtime binding run or reviewed claim identity is missing or foreign.");
            ValidateSubject(binding.Subject);
            Require(string.Equals(binding.ProfileId, NativePageRuntimeContract.ClassicWikiProfile, StringComparison.Ordinal)
                && string.Equals(binding.PolicyVersion, NativePageRuntimeContract.ClassicWikiPolicyVersion, StringComparison.Ordinal),
                "The native page runtime profile or policy version is unsupported.");
            ValidateExtensions(binding.Extensions);

            Require(artifactStore != null, "An exact native runtime artifact resolver is required.");
            ClassicWikiPackageValidator.ValidateMigration(package, artifactStore);
            ValidateSourcePredicate(package);
            ValidateCanonicalArtifact(binding.PackageEvidence, package, artifactStore, "classic Wiki package evidence");
            Require(DigestEquals(binding.SnapshotDigestSha256, package.SnapshotDigest)
                && DigestEquals(binding.PlanDigest, package.PlanDigest),
                "The runtime binding package snapshot or plan digest is stale or foreign.");
            ValidateSourceIdentity(binding.SourceIdentity, package);
            ValidateSourceIdentityEvidence(
                binding.SourceIdentityEvidence,
                binding.SourceIdentity,
                admittedPlan?.SourceVersion?.VersionDigestSha256,
                artifactStore);
            Require(AdmittedReproExecutionPlanValidator.SameSourceVersion(binding.SourceVersion, admittedPlan?.SourceVersion),
                "The runtime binding source version is missing or stale.");
            Require(binding.SourceIdentityEvidence.ObservedAtUtc <= binding.SourceVersion.ObservedAtUtc,
                "The source identity observation postdates the admitted source-version fence.");

            var computedAdmittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                admittedPlan?.TargetIdentity);
            Require(DigestEquals(computedAdmittedDigest, admittedPlanDigestSha256)
                && DigestEquals(binding.AdmittedPlanDigestSha256, computedAdmittedDigest),
                "The runtime binding admitted-plan digest is stale or foreign.");
            Require(AdmittedReproExecutionPlanValidator.SameOperations(binding.Operations, admittedPlan.Operations),
                "The runtime binding operation set is missing or foreign.");

            ValidateCanonicalArtifact(binding.NativeImportEvidence, importAggregate, artifactStore, "native import evidence");
            var importBinding = NativePageImportReceiptAggregateValidator.ValidateForCompare(
                importAggregate,
                admittedPlan,
                computedAdmittedDigest);
            Require(string.Equals(importBinding.PageFamily, NativePageRuntimeContract.ClassicWikiFamily, StringComparison.Ordinal),
                "Only a native Classic Wiki receipt can enter this runtime profile.");
            Require(importBinding.FreshReadbackPassed
                && importBinding.StorageVerificationStatus == StorageVerificationStatus.Passed,
                "Native runtime acceptance requires a passed fresh storage readback.");
            Require(importBinding.RuntimeVerificationStatus == RuntimeVerificationStatus.Pending
                && importAggregate.ClassicWikiReceipt.AcceptanceStatus == MigrationAcceptanceStatus.Pending,
                "The native import receipt must remain Pending before runtime evaluation.");
            Require(DigestEquals(binding.ImportReceiptDigestSha256, importBinding.ReceiptDigestSha256),
                "The runtime binding import receipt digest is stale or foreign.");

            ValidateTarget(binding.TargetStorageIdentity, "sealed target storage identity");
            Require(UriEquals(binding.TargetStorageIdentity.WebUrl, importBinding.TargetWebUrl)
                && binding.TargetStorageIdentity.WebId == package.Plan.TargetLocation.TargetWebId
                && UriEquals(binding.TargetStorageIdentity.WebUrl, package.Plan.TargetLocation.TargetWebUrl)
                && PathEquals(binding.TargetStorageIdentity.PageServerRelativeUrl, importBinding.TargetPageServerRelativeUrl)
                && binding.TargetStorageIdentity.FileUniqueId == importBinding.TargetFileUniqueId
                && binding.TargetStorageIdentity.ListItemId == importBinding.TargetListItemId
                && string.Equals(binding.TargetStorageIdentity.ListItemVersion, importBinding.TargetVersionLabel, StringComparison.Ordinal)
                && string.Equals(binding.TargetStorageIdentity.CanonicalUrl, admittedPlan.TargetIdentity, StringComparison.Ordinal),
                "The sealed target identity does not match the admitted native import.");
            ValidateTargetEvidence(
                binding.TargetIdentityEvidence,
                binding.TargetStorageIdentity,
                admittedPlan.Operations.ReadbackOperationId,
                binding.ImportReceiptDigestSha256,
                artifactStore,
                "pre-capture target identity");
            Require(identityEvidenceVerifier != null
                && !string.IsNullOrWhiteSpace(binding.IdentityEvidenceVerifierId)
                && string.Equals(binding.IdentityEvidenceVerifierId, identityEvidenceVerifier.VerifierId, StringComparison.Ordinal)
                && string.Equals(
                    binding.IdentityEvidenceVerifierImplementationRef,
                    identityEvidenceVerifier.ImplementationRef,
                    StringComparison.OrdinalIgnoreCase),
                "The independent identity evidence verifier is missing or foreign.");
            ValidateImplementationRef(binding.IdentityEvidenceVerifierImplementationRef, "identity evidence verifier ref");
            identityEvidenceVerifier.Verify(
                binding.SourceIdentityEvidence,
                binding.TargetIdentityEvidence,
                package,
                admittedPlan,
                importAggregate);

            ValidateFixedManifest(binding.RequirementsManifest);
            var manifestDigest = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonical(binding.RequirementsManifest));
            Require(DigestEquals(binding.RequirementsManifestDigestSha256, manifestDigest),
                "The pre-capture runtime requirements manifest digest is stale or foreign.");
            ValidateCanonicalArtifact(binding.PolicyArtifact, binding.RequirementsManifest, artifactStore, "runtime policy artifact");
            Require(DigestEquals(binding.PolicyArtifact.Sha256, manifestDigest),
                "The runtime policy artifact is not the sealed requirements manifest.");
            ValidateDigest(binding.ExpectedAuthoredContentSha256, "expected authored content digest");
            Require(DigestEquals(binding.ExpectedAuthoredContentSha256, package.Plan.WikiFieldPlan.ExpectedStoredSha256),
                "The runtime authored-content expectation is foreign to the admitted Wiki plan.");

            var importCompleted = importAggregate.ClassicWikiReceipt.CompletedAtUtc;
            Require(binding.TargetIdentityEvidence.ObservedAtUtc >= importCompleted
                && binding.IssuedAtUtc >= binding.TargetIdentityEvidence.ObservedAtUtc
                && binding.CaptureNotBeforeUtc >= binding.IssuedAtUtc
                && binding.CaptureNotBeforeUtc >= admittedPlan.SourceVersion.ObservedAtUtc
                && binding.CaptureExpiresAtUtc > binding.CaptureNotBeforeUtc,
                "The native pre-capture policy timeline is invalid or predates import/readback.");

            ValidateImplementationRef(binding.ImportProducerRef, "import producer ref");
            ValidateImplementationRef(binding.ContractProducerRef, "contract producer ref");
            var provenanceDigest = ProducerBuildProvenanceContract.ValidateManifestAndComputeDigest(provenanceManifest);
            Require(DigestEquals(binding.ContractProducerProvenanceManifestDigestSha256, provenanceDigest)
                && string.Equals(binding.ContractProducerRef, provenanceManifest.SubjectImplementationRef, StringComparison.OrdinalIgnoreCase),
                "The contract producer provenance manifest is foreign to the binding.");
            Require(string.Equals(binding.ContractProducerProvenanceStatus, ProducerBuildProvenanceContract.Unverified, StringComparison.Ordinal)
                || string.Equals(binding.ContractProducerProvenanceStatus, ProducerBuildProvenanceContract.Verified, StringComparison.Ordinal)
                || string.Equals(binding.ContractProducerProvenanceStatus, ProducerBuildProvenanceContract.Rejected, StringComparison.Ordinal),
                "The bound contract producer provenance status is unsupported.");
            return binding.ContentSha256;
        }

        public static string SealExternalEvidence(ExternalPageRuntimeEvidence evidence)
        {
            Require(evidence != null, "External runtime evidence is required.");
            evidence.ContentSha256 = ComputeSeal(evidence, nameof(ExternalPageRuntimeEvidence.ContentSha256));
            return evidence.ContentSha256;
        }

        public static string ValidateExternalEvidenceAndComputeDigest(
            ExternalPageRuntimeEvidence evidence,
            NativePageRuntimeBinding binding,
            INativePageRuntimeEvidencePolicy policy,
            IMigrationArtifactStore artifactStore,
            out RuntimeVerificationStatus semanticStatus)
        {
            Require(evidence != null, "External runtime evidence is required.");
            Require(string.Equals(evidence.SchemaVersion, NativePageRuntimeContract.ExternalEvidenceSchemaVersion, StringComparison.Ordinal),
                "The external runtime evidence schema is unsupported.");
            ValidateDigest(evidence.ContentSha256, "external runtime evidence content seal");
            Require(DigestEquals(evidence.ContentSha256, ComputeSeal(evidence, nameof(ExternalPageRuntimeEvidence.ContentSha256))),
                "The external runtime evidence content seal is stale or corrupt.");
            Require(evidence.RunId == binding.RunId
                && string.Equals(evidence.ClaimId, binding.ClaimId, StringComparison.Ordinal)
                && DigestEquals(evidence.BindingDigestSha256, binding.ContentSha256),
                "The external runtime evidence is foreign to the sealed binding.");
            Require(string.Equals(evidence.ProfileClass, binding.ProfileId, StringComparison.Ordinal),
                "The external runtime evidence profile is foreign.");
            ValidateExtensions(evidence.Extensions);
            ValidateCaptureProducer(evidence.CaptureProducer);
            Require(evidence.StartedAtUtc >= binding.CaptureNotBeforeUtc
                && evidence.CompletedAtUtc >= evidence.StartedAtUtc
                && evidence.CompletedAtUtc <= binding.CaptureExpiresAtUtc,
                "The external runtime capture is outside the sealed policy window.");
            ValidateTarget(evidence.ObservedTargetIdentity, "observed runtime target identity");
            Require(SameTarget(evidence.ObservedTargetIdentity, binding.TargetStorageIdentity),
                "The observed runtime target identity is foreign or stale.");
            ValidateTargetEvidence(
                evidence.PreCaptureTargetReadback,
                binding.TargetStorageIdentity,
                binding.Operations.RuntimeOperationId,
                binding.ContentSha256,
                artifactStore,
                "pre-capture target readback");
            ValidateTargetEvidence(
                evidence.PostCaptureTargetReadback,
                binding.TargetStorageIdentity,
                binding.Operations.RuntimeOperationId,
                binding.ContentSha256,
                artifactStore,
                "post-capture target readback");
            Require(evidence.PreCaptureTargetReadback.ObservedAtUtc >= binding.TargetIdentityEvidence.ObservedAtUtc
                && evidence.PreCaptureTargetReadback.ObservedAtUtc >= binding.CaptureNotBeforeUtc
                && evidence.PreCaptureTargetReadback.ObservedAtUtc <= evidence.StartedAtUtc
                && evidence.PostCaptureTargetReadback.ObservedAtUtc >= evidence.CompletedAtUtc
                && evidence.PostCaptureTargetReadback.ObservedAtUtc <= binding.CaptureExpiresAtUtc,
                "The target identity readbacks do not bracket the runtime capture.");

            var hasRuntime = evidence.RuntimeReceipt != null;
            var hasTerminal = evidence.TerminalObservation != null;
            Require(hasRuntime != hasTerminal, "External runtime evidence must contain exactly one result kind.");
            if (hasRuntime)
            {
                Require(string.Equals(evidence.ResultKind, NativePageRuntimeContract.RuntimeResultKind, StringComparison.Ordinal),
                    "The external runtime evidence result kind is inconsistent.");
                var receipt = evidence.RuntimeReceipt;
                Require(string.Equals(receipt.SchemaVersion, "pnp-migration-runtime-verification-receipt/v1", StringComparison.Ordinal)
                    && Enum.IsDefined(typeof(RuntimeVerificationStatus), receipt.Status),
                    "The nested runtime receipt schema or status is unsupported.");
                RuntimeVerificationReceiptValidator.ValidateEvidence(
                    receipt,
                    binding.RequirementsManifest,
                    binding.ContractProducerRef,
                    artifactStore);
                Require(receipt.OperationId == binding.Operations.RuntimeOperationId
                    && AdmittedReproExecutionPlanValidator.SameOperations(receipt.Operations, binding.Operations)
                    && AdmittedReproExecutionPlanValidator.SameSourceVersion(receipt.SourceVersion, binding.SourceVersion)
                    && DigestEquals(receipt.AdmittedPlanDigestSha256, binding.AdmittedPlanDigestSha256)
                    && DigestEquals(receipt.ImportReceiptDigestSha256, binding.ImportReceiptDigestSha256)
                    && DigestEquals(receipt.RequirementsManifestDigestSha256, binding.RequirementsManifestDigestSha256)
                    && string.Equals(receipt.PlanDigest, binding.PlanDigest, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(receipt.TargetIdentity, binding.TargetStorageIdentity.CanonicalUrl, StringComparison.Ordinal),
                    "The canonical runtime receipt lineage is foreign to the binding.");
                Require(receipt.BrowserContext.CreatedAtUtc >= binding.CaptureNotBeforeUtc
                    && receipt.BrowserContext.FirstNavigationAtUtc >= evidence.StartedAtUtc
                    && receipt.CompletedAtUtc <= evidence.CompletedAtUtc,
                    "The runtime receipt/browser timeline is stale or predates the admitted import.");
                var digest = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(receipt));
                Require(DigestEquals(evidence.RuntimeReceiptDigestSha256, digest),
                    "The external envelope runtime receipt digest is missing or altered.");
                ValidateResultCoverage(receipt.Results, binding.RequirementsManifest);
                var expectedReceiptStatus = binding.RequirementsManifest.Requirements.All(requirement =>
                    receipt.Results.Single(value => string.Equals(value.RequirementId, requirement.Id, StringComparison.Ordinal)).Passed)
                    ? RuntimeVerificationStatus.Passed
                    : RuntimeVerificationStatus.Failed;
                Require(receipt.Status == expectedReceiptStatus,
                    "The nested runtime receipt status contradicts its required results.");
                ValidateAttempts(evidence.Attempts, binding, evidence, receipt, null, artifactStore);
                ValidateArtifactManifest(
                    evidence.ArtifactManifest,
                    RequiredArtifacts(evidence, receipt),
                    artifactStore);
                semanticStatus = receipt.Status == RuntimeVerificationStatus.Passed
                    && string.Equals(
                        evidence.Attempts[evidence.Attempts.Count - 1].SemanticResult,
                        "surface_present",
                        StringComparison.Ordinal)
                    && binding.RequirementsManifest.Requirements.All(requirement =>
                    {
                        var result = receipt.Results.Single(value => string.Equals(value.RequirementId, requirement.Id, StringComparison.Ordinal));
                        return result.Passed && policy.VerifyResult(binding, result, artifactStore);
                    })
                    ? RuntimeVerificationStatus.Passed
                    : RuntimeVerificationStatus.Failed;
            }
            else
            {
                Require(string.Equals(evidence.ResultKind, NativePageRuntimeContract.TerminalResultKind, StringComparison.Ordinal),
                    "The external terminal observation result kind is inconsistent.");
                Require(string.IsNullOrWhiteSpace(evidence.RuntimeReceiptDigestSha256),
                    "A terminal observation cannot carry a runtime receipt digest.");
                Require(!string.IsNullOrWhiteSpace(evidence.TerminalObservation.Kind)
                    && !string.IsNullOrWhiteSpace(evidence.TerminalObservation.ReasonCode)
                    && IsSupportedSemanticResult(evidence.TerminalObservation.SemanticDetectorResult)
                    && !string.Equals(
                        evidence.TerminalObservation.SemanticDetectorResult,
                        "surface_present",
                        StringComparison.Ordinal),
                    "The terminal runtime observation is incomplete.");
                ValidateAttempts(evidence.Attempts, binding, evidence, null, evidence.TerminalObservation, artifactStore);
                ValidateArtifactManifest(
                    evidence.ArtifactManifest,
                    RequiredArtifacts(evidence, null),
                    artifactStore);
                semanticStatus = RuntimeVerificationStatus.Failed;
            }
            return evidence.ContentSha256;
        }

        public static string SealAcceptanceReceipt(NativePageRuntimeAcceptanceReceipt receipt)
        {
            Require(receipt != null, "A native runtime acceptance receipt is required.");
            receipt.ContentSha256 = ComputeSeal(receipt, nameof(NativePageRuntimeAcceptanceReceipt.ContentSha256));
            return receipt.ContentSha256;
        }

        public static NativePageRuntimeArtifactReference PutCanonicalArtifact<T>(
            T value,
            IMigrationArtifactStore artifactStore,
            string mediaType)
        {
            Require(value != null, "A canonical artifact value is required.");
            Require(artifactStore != null, "An exact artifact store is required.");
            var bytes = Encoding.UTF8.GetBytes(MigrationContractSerializer.SerializeCanonical(value));
            using (var stream = new MemoryStream(bytes, writable: false))
            {
                var stored = artifactStore.Put(stream, mediaType);
                return new NativePageRuntimeArtifactReference
                {
                    Sha256 = stored.Sha256,
                    Length = stored.Length,
                    MediaType = mediaType,
                    Locator = stored.Sha256.Substring(0, 2).ToLowerInvariant() + "/" + stored.Sha256.ToLowerInvariant()
                };
            }
        }

        public static NativePageRuntimeTargetIdentity CopyTarget(NativePageRuntimeTargetIdentity value)
        {
            return value == null ? null : new NativePageRuntimeTargetIdentity
            {
                SiteId = value.SiteId,
                WebId = value.WebId,
                ListId = value.ListId,
                WebUrl = value.WebUrl,
                PageServerRelativeUrl = value.PageServerRelativeUrl,
                FileUniqueId = value.FileUniqueId,
                ListItemId = value.ListItemId,
                ListItemVersion = value.ListItemVersion,
                ListItemETag = value.ListItemETag,
                CanonicalUrl = value.CanonicalUrl
            };
        }

        public static RuntimeVerificationManifest CreateClassicWikiManifest()
        {
            return new RuntimeVerificationManifest
            {
                Requirements = new List<RuntimeVerificationRequirement>
                {
                    new RuntimeVerificationRequirement
                    {
                        Id = NativePageRuntimeContract.RuntimeRequirementId,
                        Kind = RuntimeVerificationRequirementKind.PageReachability,
                        Required = true,
                        Description = "Exact admitted Wiki surface"
                    },
                    new RuntimeVerificationRequirement
                    {
                        Id = NativePageRuntimeContract.ErrorShellRequirementId,
                        Kind = RuntimeVerificationRequirementKind.ErrorShellAbsence,
                        Required = true,
                        Description = "No login, access-denied or error shell"
                    },
                    new RuntimeVerificationRequirement
                    {
                        Id = NativePageRuntimeContract.ScreenshotRequirementId,
                        Kind = RuntimeVerificationRequirementKind.ScreenshotCapture,
                        Required = true,
                        Description = "Bound screenshot captured"
                    }
                }
            };
        }

        private static void ValidateSourcePredicate(ClassicWikiMigrationPackage package)
        {
            var snapshot = package.Snapshot;
            var runtimeNode = snapshot?.IngredientGraph?.Nodes?.SingleOrDefault(value =>
                value != null && string.Equals(value.Id, "node:runtime", StringComparison.Ordinal));
            Require(snapshot != null
                && snapshot.Runtime?.ResolutionState == PageRuntimeResolutionState.Resolved
                && string.Equals(snapshot.Runtime.AdapterId, PageRuntimeAdapterIds.Wiki, StringComparison.Ordinal)
                && snapshot.LibraryBaseTemplate == 119
                && snapshot.PageArtifact?.PageDirective?.Inherits?.IndexOf(
                    "Microsoft.SharePoint.WebPartPages.WikiEditPage",
                    StringComparison.Ordinal) >= 0
                && runtimeNode?.Kind == PageIngredientKind.Runtime
                && string.Equals(runtimeNode.RuntimeRequirement, PageRuntimeAdapterIds.Wiki, StringComparison.Ordinal),
                "The package does not satisfy the sealed Classic Wiki runtime source predicate.");
        }

        private static void ValidateSourceIdentity(NativePageRuntimeSourceIdentity source, ClassicWikiMigrationPackage package)
        {
            var expected = package.Snapshot.Source;
            Require(source != null && expected != null
                && source.SiteId == expected.SiteId
                && source.WebId == expected.WebId
                && source.ListId != Guid.Empty
                && source.ListItemId == expected.ListItemId
                && source.FileUniqueId == expected.FileUniqueId
                && PathEquals(source.PageServerRelativeUrl, expected.PageServerRelativeUrl),
                "The runtime binding source Site/Web/List/item/File/path identity is missing or foreign.");
        }

        private static void ValidateSubject(NativePageRuntimeSubject subject)
        {
            Require(subject != null
                && string.Equals(subject.IngredientId, "node:runtime", StringComparison.Ordinal)
                && string.Equals(subject.PageIngredientKind, "Runtime", StringComparison.Ordinal)
                && string.Equals(subject.Subtype, "runtime.page", StringComparison.Ordinal)
                && string.Equals(subject.SemanticRole, "runtime.wiki acceptance binding", StringComparison.Ordinal)
                && string.Equals(subject.PrimaryOwnerLane, "shared integration", StringComparison.Ordinal),
                "The runtime binding subject/owner claim is missing or foreign.");
        }

        private static void ValidateFixedManifest(RuntimeVerificationManifest manifest)
        {
            var expected = CreateClassicWikiManifest().Requirements;
            Require(manifest != null
                && string.Equals(manifest.SchemaVersion, "pnp-migration-runtime-verification/v1", StringComparison.Ordinal)
                && manifest.Requirements != null
                && manifest.Requirements.Count == expected.Count,
                "The Classic Wiki pre-capture runtime manifest is missing or incomplete.");
            for (var index = 0; index < expected.Count; index++)
            {
                var actual = manifest.Requirements[index];
                Require(actual != null
                    && string.Equals(actual.Id, expected[index].Id, StringComparison.Ordinal)
                    && actual.Kind == expected[index].Kind
                    && actual.Required
                    && string.Equals(actual.Description, expected[index].Description, StringComparison.Ordinal),
                    "The Classic Wiki runtime manifest was reordered, weakened, or altered.");
            }
        }

        private static void ValidateResultCoverage(
            IList<RuntimeVerificationResult> results,
            RuntimeVerificationManifest manifest)
        {
            Require(results != null && results.Count == manifest.Requirements.Count,
                "The runtime receipt must cover the sealed requirement set exactly once.");
            Require(results.All(value => value != null)
                && results.Select(value => value.RequirementId).Distinct(StringComparer.Ordinal).Count() == results.Count,
                "The runtime receipt contains missing or duplicate result IDs.");
            Require(manifest.Requirements.All(requirement => results.Count(result =>
                    string.Equals(result.RequirementId, requirement.Id, StringComparison.Ordinal)) == 1),
                "The runtime receipt is missing a sealed required result.");
        }

        private static void ValidateAttempts(
            IList<NativePageRuntimeAttempt> attempts,
            NativePageRuntimeBinding binding,
            ExternalPageRuntimeEvidence evidence,
            RuntimeVerificationReceipt receipt,
            NativePageRuntimeTerminalObservation terminal,
            IMigrationArtifactStore store)
        {
            Require(attempts != null && attempts.Count > 0 && attempts.Count <= 3,
                "The external runtime evidence requires one to three bounded attempts.");
            for (var index = 0; index < attempts.Count; index++)
            {
                var attempt = attempts[index];
                Require(attempt != null
                    && attempt.Sequence == index + 1
                    && attempt.MaximumAttempts == attempts.Count
                    && attempt.RunId == binding.RunId
                    && attempt.RuntimeOperationId == binding.Operations.RuntimeOperationId
                    && attempt.ObservedAtUtc >= evidence.StartedAtUtc
                    && attempt.ObservedAtUtc <= evidence.CompletedAtUtc
                    && (index == 0 || attempt.ObservedAtUtc >= attempts[index - 1].ObservedAtUtc)
                    && string.Equals(attempt.RequestedUrl, binding.TargetStorageIdentity.CanonicalUrl, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(attempt.FinalUrl)
                    && !string.IsNullOrWhiteSpace(attempt.ProviderId)
                    && !string.IsNullOrWhiteSpace(attempt.ProviderVersion)
                    && !string.IsNullOrWhiteSpace(attempt.SemanticDetectorId)
                    && !string.IsNullOrWhiteSpace(attempt.SemanticDetectorVersion)
                    && IsSupportedSemanticResult(attempt.SemanticResult)
                    && !string.IsNullOrWhiteSpace(attempt.BrowserContextId),
                    "A runtime attempt is incomplete, foreign, or out of order.");
                ValidateDigest(attempt.SemanticDetectorDigestSha256, "semantic detector digest");
                Require(attempt.HttpStatusCode.HasValue != attempt.TransportUnavailable,
                    "A runtime attempt must contain either HTTP status or transport-unavailable evidence.");
                Require(!string.IsNullOrWhiteSpace(attempt.RequestId)
                        && string.Equals(attempt.RequestIdAvailability, "available", StringComparison.Ordinal)
                        && string.IsNullOrWhiteSpace(attempt.RequestIdUnavailableReason)
                    || string.IsNullOrWhiteSpace(attempt.RequestId)
                        && string.Equals(attempt.RequestIdAvailability, "unavailable", StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(attempt.RequestIdUnavailableReason),
                    "A runtime attempt must preserve request ID or explicit unavailability.");
                Require(attempt.RawEvidence != null && attempt.RawEvidence.Count > 0,
                    "A runtime attempt must preserve reopenable raw evidence.");
                foreach (var artifact in attempt.RawEvidence)
                {
                    ValidateRuntimeArtifactReference(artifact, store, "runtime attempt raw evidence");
                }
            }
            var finalAttempt = attempts[attempts.Count - 1];
            if (receipt != null)
            {
                var http = receipt.Results.Select(value => value.Http).FirstOrDefault();
                Require(http != null
                    && receipt.Results.All(value => SameHttpObservation(value.Http, http))
                    && finalAttempt.HttpStatusCode == http.StatusCode
                    && !finalAttempt.TransportUnavailable
                    && string.Equals(finalAttempt.FinalUrl, http.FinalUrl, StringComparison.Ordinal)
                    && string.Equals(finalAttempt.BrowserContextId, receipt.BrowserContext.BrowserContextId, StringComparison.Ordinal)
                    && (string.Equals(finalAttempt.RequestId, http.RequestId, StringComparison.Ordinal)
                        || string.Equals(finalAttempt.RequestIdAvailability, "unavailable", StringComparison.Ordinal)
                            && string.IsNullOrWhiteSpace(http.RequestId))
                    && finalAttempt.ObservedAtUtc == http.CapturedAtUtc,
                    "The terminal runtime attempt does not identify the consumed runtime result.");
                ValidateTerminalAttemptArtifacts(finalAttempt, receipt.Results);
            }
            else
            {
                Require(terminal != null
                    && (!terminal.HttpStatusCode.HasValue || finalAttempt.HttpStatusCode == terminal.HttpStatusCode)
                    && string.Equals(finalAttempt.SemanticResult, terminal.SemanticDetectorResult, StringComparison.OrdinalIgnoreCase),
                    "The terminal attempt does not match the terminal observation.");
            }
        }

        private static void ValidateCaptureProducer(NativePageRuntimeCaptureProducer producer)
        {
            Require(producer != null
                && !string.IsNullOrWhiteSpace(producer.AdapterId)
                && !string.IsNullOrWhiteSpace(producer.Version)
                && !string.IsNullOrWhiteSpace(producer.ToolVersion)
                && !string.IsNullOrWhiteSpace(producer.ProtocolVersion),
                "The runtime capture producer identity is incomplete.");
            ValidateImplementationRef(producer.ImplementationRef, "capture producer ref");
        }

        private static void ValidateTargetEvidence(
            NativePageRuntimeTargetIdentityEvidence evidence,
            NativePageRuntimeTargetIdentity expected,
            Guid expectedOperationId,
            string expectedSourceDigest,
            IMigrationArtifactStore store,
            string name)
        {
            Require(evidence != null
                && !string.IsNullOrWhiteSpace(evidence.ObservationId)
                && evidence.ObservedAtUtc != default
                && evidence.OperationId == expectedOperationId
                && !string.IsNullOrWhiteSpace(evidence.ProviderId)
                && !string.IsNullOrWhiteSpace(evidence.ProviderVersion)
                && DigestEquals(evidence.SourceArtifactSha256, expectedSourceDigest)
                && SameTarget(evidence.Identity, expected),
                "The " + name + " is incomplete or foreign.");
            ValidateCanonicalArtifact(evidence.Artifact, evidence.Identity, store, name + " artifact");
        }

        private static void ValidateArtifactManifest(
            MigrationArtifactManifest manifest,
            IEnumerable<NativePageRuntimeArtifactReference> requiredArtifacts,
            IMigrationArtifactStore store)
        {
            Require(manifest != null
                && string.Equals(manifest.SchemaVersion, "pnp-migration-artifacts/v1", StringComparison.Ordinal)
                && (manifest.Artifacts?.Count ?? 0) > 0,
                "The external runtime artifact manifest is missing.");
            var seal = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    manifest,
                    nameof(MigrationArtifactManifest.ContentSha256)));
            Require(DigestEquals(manifest.ContentSha256, seal),
                "The external runtime artifact manifest seal is stale or corrupt.");
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in manifest.Artifacts)
            {
                Require(artifact != null
                    && artifact.Length > 0
                    && !string.IsNullOrWhiteSpace(artifact.MediaType)
                    && observed.Add(artifact.Sha256)
                    && store.Contains(artifact.Sha256),
                    "The external runtime artifact manifest contains a missing artifact.");
                ValidateDigest(artifact.Sha256, "external runtime artifact digest");
                using (var input = store.OpenRead(artifact.Sha256))
                using (var copy = new MemoryStream())
                {
                    input.CopyTo(copy);
                    Require(copy.Length == artifact.Length
                        && DigestEquals(MigrationDigest.ComputeSha256(copy.ToArray()), artifact.Sha256),
                        "An external runtime artifact is corrupt or stale.");
                }
            }
            foreach (var required in requiredArtifacts ?? Enumerable.Empty<NativePageRuntimeArtifactReference>())
            {
                ValidateRuntimeArtifactReference(required, store, "required external runtime evidence");
                Require(manifest.Artifacts.Any(value => value != null
                        && DigestEquals(value.Sha256, required.Sha256)
                        && value.Length == required.Length
                        && MediaTypeEquals(value.MediaType, required.MediaType)),
                    "The external runtime artifact manifest omits required evidence.");
            }
        }

        private static void ValidateSourceIdentityEvidence(
            NativePageRuntimeSourceIdentityEvidence evidence,
            NativePageRuntimeSourceIdentity expected,
            string expectedSourceVersionDigest,
            IMigrationArtifactStore store)
        {
            Require(evidence != null
                && !string.IsNullOrWhiteSpace(evidence.ObservationId)
                && evidence.ObservedAtUtc != default
                && evidence.OperationId != Guid.Empty
                && !string.IsNullOrWhiteSpace(evidence.AcquisitionMethod)
                && !string.IsNullOrWhiteSpace(evidence.ProviderId)
                && !string.IsNullOrWhiteSpace(evidence.ProviderVersion)
                && DigestEquals(evidence.SourceVersionDigestSha256, expectedSourceVersionDigest)
                && SameSource(evidence.Identity, expected),
                "The source identity evidence is incomplete or foreign.");
            ValidateCanonicalArtifact(evidence.Artifact, evidence.Identity, store, "source identity evidence artifact");
        }

        private static IList<NativePageRuntimeArtifactReference> RequiredArtifacts(
            ExternalPageRuntimeEvidence evidence,
            RuntimeVerificationReceipt receipt)
        {
            var result = new List<NativePageRuntimeArtifactReference>
            {
                evidence.PreCaptureTargetReadback.Artifact,
                evidence.PostCaptureTargetReadback.Artifact
            };
            foreach (var attempt in evidence.Attempts ?? new List<NativePageRuntimeAttempt>())
            {
                result.AddRange(attempt?.RawEvidence ?? new List<NativePageRuntimeArtifactReference>());
            }
            foreach (var item in receipt?.Results ?? new List<RuntimeVerificationResult>())
            {
                result.Add(new NativePageRuntimeArtifactReference
                {
                    Sha256 = item.EvidenceArtifactSha256,
                    Length = item.EvidenceArtifactLength,
                    MediaType = item.Http?.ContentType,
                    Locator = item.EvidenceArtifactLocator
                });
                result.Add(new NativePageRuntimeArtifactReference
                {
                    Sha256 = item.DomProbeArtifactSha256,
                    Length = item.DomProbeArtifactLength,
                    MediaType = "application/json",
                    Locator = item.DomProbeArtifactLocator
                });
                if (!string.IsNullOrWhiteSpace(item.ScreenshotArtifactSha256))
                {
                    result.Add(new NativePageRuntimeArtifactReference
                    {
                        Sha256 = item.ScreenshotArtifactSha256,
                        Length = item.ScreenshotArtifactLength.GetValueOrDefault(),
                        MediaType = "image/png",
                        Locator = item.ScreenshotArtifactLocator
                    });
                }
            }
            return result
                .Where(value => value != null)
                .GroupBy(value => value.Sha256, StringComparer.OrdinalIgnoreCase)
                .Select(value => value.First())
                .ToList();
        }

        private static void ValidateRuntimeArtifactReference(
            NativePageRuntimeArtifactReference artifact,
            IMigrationArtifactStore store,
            string name)
        {
            Require(artifact != null
                && artifact.Length > 0
                && !string.IsNullOrWhiteSpace(artifact.MediaType)
                && IsSafeRelativeLocator(artifact.Locator),
                "The " + name + " reference is incomplete or unsafe.");
            ValidateDigest(artifact.Sha256, name + " digest");
            Require(store != null && store.Contains(artifact.Sha256), "The " + name + " is missing.");
            using (var input = store.OpenRead(artifact.Sha256))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                Require(copy.Length == artifact.Length
                    && DigestEquals(MigrationDigest.ComputeSha256(copy.ToArray()), artifact.Sha256),
                    "The " + name + " bytes are stale or corrupt.");
            }
        }

        private static void ValidateCanonicalArtifact<T>(
            NativePageRuntimeArtifactReference artifact,
            T expected,
            IMigrationArtifactStore store,
            string name)
        {
            Require(artifact != null && artifact.Length > 0 && !string.IsNullOrWhiteSpace(artifact.MediaType),
                "The " + name + " reference is incomplete.");
            ValidateDigest(artifact.Sha256, name + " digest");
            var expectedLocator = artifact.Sha256.Substring(0, 2).ToLowerInvariant()
                + "/"
                + artifact.Sha256.ToLowerInvariant();
            Require(string.Equals(artifact.Locator, expectedLocator, StringComparison.Ordinal),
                "The " + name + " locator is unsafe or foreign.");
            Require(store != null && store.Contains(artifact.Sha256), "The " + name + " is missing.");
            using (var input = store.OpenRead(artifact.Sha256))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                var bytes = copy.ToArray();
                var expectedBytes = Encoding.UTF8.GetBytes(MigrationContractSerializer.SerializeCanonical(expected));
                Require(bytes.LongLength == artifact.Length
                    && bytes.SequenceEqual(expectedBytes)
                    && DigestEquals(MigrationDigest.ComputeSha256(bytes), artifact.Sha256),
                    "The " + name + " bytes are foreign, stale, or corrupt.");
            }
        }

        private static void ValidateTarget(NativePageRuntimeTargetIdentity target, string name)
        {
            Require(target != null
                && target.SiteId != Guid.Empty
                && target.WebId != Guid.Empty
                && target.ListId != Guid.Empty
                && target.FileUniqueId != Guid.Empty
                && target.ListItemId > 0
                && Uri.TryCreate(target.WebUrl, UriKind.Absolute, out _)
                && !string.IsNullOrWhiteSpace(target.PageServerRelativeUrl)
                && target.PageServerRelativeUrl.StartsWith("/", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(target.ListItemVersion)
                && !string.IsNullOrWhiteSpace(target.ListItemETag)
                && string.Equals(target.CanonicalUrl, CanonicalTargetIdentity(target), StringComparison.Ordinal),
                "The " + name + " is incomplete.");
        }

        private static bool SameTarget(NativePageRuntimeTargetIdentity left, NativePageRuntimeTargetIdentity right)
        {
            return left != null && right != null
                && left.SiteId == right.SiteId
                && left.WebId == right.WebId
                && left.ListId == right.ListId
                && UriEquals(left.WebUrl, right.WebUrl)
                && PathEquals(left.PageServerRelativeUrl, right.PageServerRelativeUrl)
                && left.FileUniqueId == right.FileUniqueId
                && left.ListItemId == right.ListItemId
                && string.Equals(left.ListItemVersion, right.ListItemVersion, StringComparison.Ordinal)
                && string.Equals(left.ListItemETag, right.ListItemETag, StringComparison.Ordinal)
                && string.Equals(left.CanonicalUrl, right.CanonicalUrl, StringComparison.Ordinal);
        }

        private static bool SameSource(NativePageRuntimeSourceIdentity left, NativePageRuntimeSourceIdentity right)
        {
            return left != null && right != null
                && left.SiteId == right.SiteId
                && left.WebId == right.WebId
                && left.ListId == right.ListId
                && left.ListItemId == right.ListItemId
                && left.FileUniqueId == right.FileUniqueId
                && PathEquals(left.PageServerRelativeUrl, right.PageServerRelativeUrl);
        }

        private static bool SameHttpObservation(RuntimeHttpEvidence left, RuntimeHttpEvidence right)
        {
            return left != null && right != null
                && left.StatusCode == right.StatusCode
                && string.Equals(left.Method, right.Method, StringComparison.Ordinal)
                && string.Equals(left.RequestedUrl, right.RequestedUrl, StringComparison.Ordinal)
                && string.Equals(left.FinalUrl, right.FinalUrl, StringComparison.Ordinal)
                && string.Equals(left.RequestId, right.RequestId, StringComparison.Ordinal)
                && string.Equals(left.SharePointRequestGuid, right.SharePointRequestGuid, StringComparison.Ordinal)
                && string.Equals(left.ContentType, right.ContentType, StringComparison.OrdinalIgnoreCase)
                && DigestEquals(left.ResponseHeadersDigestSha256, right.ResponseHeadersDigestSha256)
                && left.EncodedDataLength == right.EncodedDataLength
                && left.CapturedAtUtc == right.CapturedAtUtc;
        }

        private static void ValidateTerminalAttemptArtifacts(
            NativePageRuntimeAttempt finalAttempt,
            IList<RuntimeVerificationResult> results)
        {
            var expected = ResultArtifacts(results)
                .GroupBy(ArtifactKey, StringComparer.OrdinalIgnoreCase)
                .Select(value => value.First())
                .ToList();
            var actual = finalAttempt.RawEvidence
                .GroupBy(ArtifactKey, StringComparer.OrdinalIgnoreCase)
                .Select(value => value.First())
                .ToList();
            Require(actual.Count == finalAttempt.RawEvidence.Count
                && actual.Count == expected.Count
                && expected.All(item => actual.Any(value => string.Equals(
                    ArtifactKey(value),
                    ArtifactKey(item),
                    StringComparison.OrdinalIgnoreCase))),
                "The terminal runtime attempt raw evidence is not the consumed runtime result evidence.");
        }

        private static IEnumerable<NativePageRuntimeArtifactReference> ResultArtifacts(
            IEnumerable<RuntimeVerificationResult> results)
        {
            foreach (var result in results ?? Enumerable.Empty<RuntimeVerificationResult>())
            {
                yield return new NativePageRuntimeArtifactReference
                {
                    Sha256 = result.EvidenceArtifactSha256,
                    Length = result.EvidenceArtifactLength,
                    MediaType = result.Http?.ContentType,
                    Locator = result.EvidenceArtifactLocator
                };
                yield return new NativePageRuntimeArtifactReference
                {
                    Sha256 = result.DomProbeArtifactSha256,
                    Length = result.DomProbeArtifactLength,
                    MediaType = "application/json",
                    Locator = result.DomProbeArtifactLocator
                };
                if (!string.IsNullOrWhiteSpace(result.ScreenshotArtifactSha256))
                {
                    yield return new NativePageRuntimeArtifactReference
                    {
                        Sha256 = result.ScreenshotArtifactSha256,
                        Length = result.ScreenshotArtifactLength.GetValueOrDefault(),
                        MediaType = "image/png",
                        Locator = result.ScreenshotArtifactLocator
                    };
                }
            }
        }

        private static string ArtifactKey(NativePageRuntimeArtifactReference artifact)
        {
            return (artifact?.Sha256 ?? string.Empty) + ":" + artifact?.Length + ":"
                + (artifact?.MediaType ?? string.Empty).Split(';')[0].Trim() + ":" + artifact?.Locator;
        }

        private static bool IsSupportedSemanticResult(string value)
        {
            return string.Equals(value, "surface_present", StringComparison.Ordinal)
                || string.Equals(value, "access_denied", StringComparison.Ordinal)
                || string.Equals(value, "unauthorized", StringComparison.Ordinal)
                || string.Equals(value, "forbidden", StringComparison.Ordinal)
                || string.Equals(value, "error_shell", StringComparison.Ordinal)
                || string.Equals(value, "http_error", StringComparison.Ordinal)
                || string.Equals(value, "transport_unavailable", StringComparison.Ordinal);
        }

        private static bool IsSafeRelativeLocator(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !Path.IsPathRooted(value)
                && !value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => string.Equals(part, "..", StringComparison.Ordinal));
        }

        private static bool MediaTypeEquals(string left, string right)
        {
            var leftBase = (left ?? string.Empty).Split(';')[0].Trim();
            var rightBase = (right ?? string.Empty).Split(';')[0].Trim();
            return !string.IsNullOrWhiteSpace(leftBase)
                && string.Equals(leftBase, rightBase, StringComparison.OrdinalIgnoreCase);
        }

        private static string CanonicalTargetIdentity(NativePageRuntimeTargetIdentity target)
        {
            var web = new Uri(target.WebUrl, UriKind.Absolute);
            return web.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/" + target.PageServerRelativeUrl.TrimStart('/');
        }

        private static void ValidateExtensions(IDictionary<string, string> extensions)
        {
            Require(extensions != null && extensions.All(value =>
                    !string.IsNullOrWhiteSpace(value.Key)
                    && value.Key.IndexOf(".", StringComparison.Ordinal) > 0
                    && value.Value != null),
                "Runtime extensions must be lossless namespaced values.");
        }

        private static string ComputeSeal<T>(T value, string propertyName)
        {
            return MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(value, propertyName));
        }

        private static bool UriEquals(string left, string right)
        {
            return Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
                && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
                && string.Equals(leftUri.AbsoluteUri.TrimEnd('/'), rightUri.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathEquals(string left, string right)
        {
            return string.Equals((left ?? string.Empty).TrimEnd('/'), (right ?? string.Empty).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
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

        private static bool DigestEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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
}
