using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Linq;
using System.Text.Json;
using static PnP.Framework.Migration.Pages.Assessment.Maturity.External.ExternalEvidenceJson;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity.External
{
    internal sealed class ValidatedExternalIngredientPlan
    {
        public IngredientExternalPlanBinding Binding { get; set; }
        public JsonElement Input { get; set; }
        public JsonElement Page { get; set; }
        public IngredientExternalPlanEvidence Evidence { get; set; }
    }

    // Format adapter, not a lane projector. No Wiki/Publishing field, layout,
    // content, feature or outcome inference belongs here.
    internal static class Batch1SealedPlanEvidenceAdapter
    {
        public static ValidatedExternalIngredientPlan Validate(
            IngredientMaturityEvaluationContext context,
            IngredientExternalPlanEvidence evidence)
        {
            IngredientMaturityEvaluator.ValidateContext(context);
            var admission = context.ExternalAdmission;
            Require(admission != null && evidence != null
                && admission.SchemaVersion == IngredientExternalEvidenceContract.AdmissionSchema
                && evidence.SchemaVersion == IngredientExternalEvidenceContract.EnvelopeSchema
                && IngredientMaturityEvaluator.IsSha256(admission.PlanArtifactSha256)
                && IngredientMaturityEvaluator.IsSha256(admission.PlanDigest)
                && IngredientMaturityEvaluator.IsSha256(admission.BindingArtifactSha256)
                && IngredientMaturityEvaluator.IsCommit(admission.PlanProducerCommit),
                "External plans require versioned, independently obtained admission pins.");
            Equal(evidence.PlanArtifact?.Sha256, admission.PlanArtifactSha256, "raw plan artifact");
            Equal(evidence.BindingArtifact?.Sha256, admission.BindingArtifactSha256, "admitted binding annex");
            var plan = Read(evidence.PlanArtifact, evidence.ArtifactStore);
            Equal(Text(plan, "schema"), IngredientExternalEvidenceContract.BatchPlanSchema, "plan schema");
            Equal(Text(plan, "planDigest"), admission.PlanDigest, "admitted plan digest");
            Equal(BatchPlanDigest(plan), admission.PlanDigest, "recomputed external plan digest");
            Equal(Text(Property(plan, "framework"), "fullSha"), admission.PlanProducerCommit, "plan producer");

            var binding = ReadCanonical<IngredientExternalPlanBinding>(evidence.BindingArtifact, evidence.ArtifactStore);
            Equal(binding.SchemaVersion, IngredientExternalEvidenceContract.BindingSchema, "annex schema");
            Equal(binding.PlanSchema, IngredientExternalEvidenceContract.BatchPlanSchema, "annex plan schema");
            Equal(binding.PlanArtifactSha256, admission.PlanArtifactSha256, "annex raw plan");
            Equal(binding.PlanDigest, admission.PlanDigest, "annex admitted plan");
            Equal(binding.PlanProducerCommit, admission.PlanProducerCommit, "annex plan producer");
            EqualObject(binding.Identity, context.Identity, "claim / kind / subtype / role / owner");
            EqualObject(binding.Source, context.Source, "source identity / version / evidence");
            EqualObject(binding.Target, context.Target, "target identity / profile");
            EqualObject(binding.Producer, context.Producer, "implementation producer");
            Require(binding.SourceSnapshotArtifact != null
                && binding.SourceSnapshotArtifact.Sha256 == context.Source.SourceSnapshotDigest,
                "The annex must bind reopenable source snapshot bytes.");
            Read(binding.SourceSnapshotArtifact, evidence.ArtifactStore);
            ValidateSourcePage(binding.SourcePage, context);
            Require(binding.AdmittedAtUtc != default && binding.AdmittedAtUtc.Offset == TimeSpan.Zero,
                "The annex requires a pre-execution UTC admission time.");

            var operation = Array(plan, "operations").SingleOrDefault(value =>
                Text(value, "operationId") == binding.ExternalPlanOperationId);
            Equal(Text(operation, "type"), "target.repro", "external plan operation type");
            Equal(Text(operation, "sourceUrl"), binding.SourcePage.PageUrl, "plan source page");
            Require(Array(plan, "operations").Select(value => Text(value, "operationId"))
                .Distinct(StringComparer.Ordinal).Count() == Array(plan, "operations").Length,
                "External plan operations must be unique.");
            var target = Property(plan, "target");
            Equal(Text(target, "origin"), binding.TargetOrigin, "plan target origin");
            var mapping = Array(Property(target, "admittedCandidates"), "sourceToTargetPageMappings")
                .SingleOrDefault(value => Text(value, "sourceUrl") == binding.SourcePage.PageUrl);
            Equal(Text(mapping, "targetUrl"), binding.PlannedTargetUrl, "original admitted target mapping");
            Require(Uri.TryCreate(binding.TargetOrigin, UriKind.Absolute, out var origin)
                && origin.Scheme == Uri.UriSchemeHttps && origin.UserInfo.Length == 0
                && origin.GetLeftPart(UriPartial.Authority) == binding.TargetOrigin,
                "The target origin must be an exact HTTPS authority.");
            Require(ValidPath(binding.TargetSitePath) && ValidPath(binding.TargetWebPath)
                && ValidPath(binding.TargetListPath) && ValidPath(binding.TargetPath)
                && Under(binding.TargetWebPath, binding.TargetSitePath)
                && Under(binding.TargetListPath, binding.TargetWebPath)
                && Under(binding.TargetPath, binding.TargetListPath)
                && (context.Target.TargetIdentity == binding.TargetPath
                    || context.Target.TargetIdentity == binding.TargetOrigin + binding.TargetPath),
                "The admitted target hierarchy or context identity is inconsistent.");

            var input = Read(binding.LifecycleInputArtifact, evidence.ArtifactStore);
            ValidateInput(input, binding);
            ValidateGraph(binding);
            return new ValidatedExternalIngredientPlan
            {
                Binding = binding, Input = input, Page = Array(input, "pages").Single(), Evidence = evidence
            };
        }

        private static void ValidateSourcePage(
            IngredientExternalPageIdentity source, IngredientMaturityEvaluationContext context)
        {
            Require(source != null && source.ItemId > 0
                && Uri.TryCreate(source.PageUrl, UriKind.Absolute, out var page)
                && page.Scheme == Uri.UriSchemeHttps && page.UserInfo.Length == 0
                && page.Query.Length == 0 && page.Fragment.Length == 0
                && Uri.UnescapeDataString(page.AbsolutePath) == source.FileServerRelativeUrl
                && ValidPath(source.FileServerRelativeUrl),
                "The source page/list-item/file tuple is incomplete.");
            GuidIdentity(source.ListId, "source List");
            GuidIdentity(source.FileUniqueId, "source File");
            Equal(source.ETag, context.Source.SourceVersion, "source version");
        }

        private static void ValidateInput(JsonElement input, IngredientExternalPlanBinding binding)
        {
            Equal(Text(input, "schema"), IngredientExternalEvidenceContract.LifecycleInputSchema, "lifecycle input schema");
            Equal(Text(input, "issue"), "CCD-143", "lifecycle producer protocol");
            Equal(Text(input, "runId"), binding.RunId, "run");
            Require(!string.IsNullOrWhiteSpace(binding.RunId) && !string.IsNullOrWhiteSpace(binding.OwnershipMarker)
                && IngredientMaturityEvaluator.IsSha256(binding.LifecycleDigest)
                && IngredientMaturityEvaluator.IsSha256(binding.TargetMappingDigest)
                && binding.LifecycleProducer != null
                && binding.LifecycleProducer.Kind == "workspace-file"
                && IngredientMaturityEvaluator.IsSha256(binding.LifecycleProducer.Sha256)
                && !string.IsNullOrWhiteSpace(binding.LifecycleProducer.Path),
                "The lifecycle, producer and ownership pins are incomplete.");
            Equal(Text(input, "planDigest"), binding.PlanDigest, "lifecycle plan");
            Equal(Text(input, "lifecycleDigest"), binding.LifecycleDigest, "lifecycle digest");
            Equal(Text(input, "targetMappingDigest"), binding.TargetMappingDigest, "target mapping digest");
            Equal(Text(input, "targetOrigin"), binding.TargetOrigin, "lifecycle target origin");
            Equal(Text(input, "ownershipMarker"), binding.OwnershipMarker, "ownership marker");
            ValidateProducer(Property(input, "producerRef"), binding);
            var sourceBundle = Property(input, "sourceBundle");
            Equal(Text(sourceBundle, "planDigest"), binding.PlanDigest, "source bundle plan");
            Equal(Text(sourceBundle, "immutableRef"), binding.PlanProducerCommit, "source bundle producer");
            var execution = Property(input, "execution");
            Equal(Text(execution, "mode"), "single-claim", "execution mode");
            Require(Number(input, "expectedPageCount") == 1 && Number(execution, "expectedPageCount") == 1
                && Array(input, "pages").Length == 1,
                "Lifecycle/v2 maturity intake requires exactly one assessed claim.");
            Equal(Text(execution, "claimId"), binding.Identity.ClaimId, "execution claim");
            Equal(Text(execution, "rowId"), binding.RowId, "execution row");
            Require(binding.RowNumber > 0 && Number(execution, "rowNumber") == binding.RowNumber,
                "Execution row number mismatch.");
            Equal(Text(execution, "admittedPlanDigest"), binding.PlanDigest, "execution plan");
            Equal(Text(execution, "targetProfile"), binding.Target.TargetProfile, "execution target profile");
            Equal(Text(Property(execution, "pnpImplementation"), "commit"), binding.Producer.ImplementationCommit,
                "execution implementation commit");
            ValidateSource(Property(execution, "source"), binding, true);
            var ingredient = Property(execution, "ingredient");
            Equal(Text(ingredient, "id"), binding.Identity.IngredientId, "execution ingredient");
            Equal(Text(ingredient, "kind"), binding.Identity.Kind?.ToString(), "execution kind");
            Equal(Text(ingredient, "subtype"), binding.Identity.Subtype, "execution subtype");
            var page = Array(input, "pages").Single();
            Require(Number(page, "rowNumber") == binding.RowNumber, "Input page row mismatch.");
            Equal(Text(page, "operationId"), binding.NativeOperationId, "native page operation");
            Equal(binding.NativeOperationId, "ccd143-native-page-" +
                MigrationDigest.ComputeSha256(binding.TargetPath).Substring(0, 16), "native operation derivation");
            Equal(binding.NativeActionId, "ccd143-action-native-create-" + binding.NativeOperationId,
                "native action (unmodified string identity)");
            Equal(Text(page, "sourceOperationId"), binding.ExternalPlanOperationId, "original plan operation");
            Equal(Text(page, "sourceUrl"), binding.SourcePage.PageUrl, "input source URL");
            Equal(Text(page, "sourcePath"), binding.SourcePage.FileServerRelativeUrl, "input source path");
            Equal(Text(page, "targetPath"), binding.TargetPath, "input target path");
            Equal(Text(page, "targetUrl"), binding.TargetOrigin + binding.TargetPath, "input target URL");
            Equal(Text(page, "webPath"), binding.TargetWebPath, "input target Web");
            Equal(Text(page, "listRootPath"), binding.TargetListPath, "input target List");
            Equal(Text(page, "sourceListId"), binding.SourcePage.ListId, "input source List");
            var site = Array(input, "sites").SingleOrDefault(value => Text(value, "targetPath") == binding.TargetSitePath);
            Equal(Text(site, "targetUrl"), binding.TargetOrigin + binding.TargetSitePath, "input target Site URL");
            var web = Array(input, "webs").SingleOrDefault(value => Text(value, "targetPath") == binding.TargetWebPath);
            Equal(Text(web, "sitePath"), binding.TargetSitePath, "input Web/Site mapping");
            var library = Array(input, "libraries").SingleOrDefault(value => Text(value, "rootPath") == binding.TargetListPath);
            Equal(Text(library, "webPath"), binding.TargetWebPath, "input List/Web mapping");
            ValidateSource(Property(page, "sourceVersion"), binding, false);
            Require(Time(input, "generatedAtUtc") <= binding.AdmittedAtUtc,
                "The annex predates the execution input it claims to admit.");

            // Recompute the exact CCD-143 wire representations, using the
            // existing serializer/hash, not a PublishingPageDigest translation.
            var targetMapping = new
            {
                schema = "ccd.shared-target-plan/v2",
                sourcePlanDigest = binding.PlanDigest,
                targetOrigin = binding.TargetOrigin,
                sitePathMappings = Property(input, "sitePathMappings"),
                pageMappings = Array(input, "pages").Select(value => new
                {
                    rowNumber = Number(value, "rowNumber"),
                    preferredPath = Text(value, "preferredPath"),
                    targetPath = Text(value, "targetPath"),
                    operationId = Text(value, "operationId")
                }).ToArray()
            };
            Equal(MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(targetMapping)),
                binding.TargetMappingDigest, "recomputed target mapping");
            var lifecycle = new
            {
                issue = "CCD-143", runId = binding.RunId, planDigest = binding.PlanDigest,
                targetMappingDigest = binding.TargetMappingDigest, producerSha256 = binding.LifecycleProducer.Sha256,
                targetOrigin = binding.TargetOrigin, claimId = binding.Identity.ClaimId
            };
            Equal(MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(lifecycle)),
                binding.LifecycleDigest, "recomputed lifecycle digest");
        }

        internal static void ValidateSource(JsonElement source, IngredientExternalPlanBinding binding, bool withPage)
        {
            Equal(Text(source, "listId"), binding.SourcePage.ListId, "source List");
            Require(Number(source, "itemId") == binding.SourcePage.ItemId, "Source item mismatch.");
            Equal(Text(source, "fileUniqueId"), binding.SourcePage.FileUniqueId, "source File");
            Equal(Text(source, "etag"), binding.SourcePage.ETag, "source ETag");
            if (withPage)
            {
                Equal(Text(source, "pageUrl"), binding.SourcePage.PageUrl, "source URL");
                Equal(Text(source, "fileServerRelativeUrl"), binding.SourcePage.FileServerRelativeUrl, "source path");
            }
        }

        internal static void ValidateProducer(JsonElement producer, IngredientExternalPlanBinding binding)
        {
            Equal(Text(producer, "kind"), binding.LifecycleProducer.Kind, "producer kind");
            Equal(Text(producer, "path"), binding.LifecycleProducer.Path, "producer locator");
            Equal(Text(producer, "sha256"), binding.LifecycleProducer.Sha256, "producer bytes");
        }

        public static string ActionSemanticDigest(IngredientExternalPlanBinding binding)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                new { graph = binding.Graph, actions = binding.Actions }));
        }

        private static void ValidateGraph(IngredientExternalPlanBinding binding)
        {
            Require(binding.Graph != null && binding.Graph.Nodes != null && binding.Graph.Edges != null
                && binding.Actions != null
                && (binding.Graph.SchemaVersion == CanonicalPageIngredientGraph.SchemaVersionV1
                    || binding.Graph.SchemaVersion == CanonicalPageIngredientGraph.SchemaVersionV2),
                "The annex requires an existing, supported canonical PnP graph.");
            Require(binding.Graph.Nodes.All(node => node != null && Enum.IsDefined(typeof(PageIngredientKind), node.Kind))
                && (binding.Graph.ExternalReferences == null || binding.Graph.ExternalReferences.All(reference =>
                    reference != null && Enum.IsDefined(typeof(PageIngredientKind), reference.Kind)
                    && Enum.IsDefined(typeof(PageExternalIngredientState), reference.State)
                    && Enum.IsDefined(typeof(PageIngredientOwnership), reference.Ownership)))
                && binding.Graph.Edges.All(edge => edge != null
                    && Enum.IsDefined(typeof(PageIngredientRelationship), edge.Relationship)
                    && Enum.IsDefined(typeof(PageIngredientRequirement), edge.Requirement))
                && binding.Actions.All(action => action != null && !string.IsNullOrWhiteSpace(action.ActionId)
                    && Enum.IsDefined(typeof(IngredientDisposition), action.Disposition)
                    && action.Disposition != IngredientDisposition.Undefined
                    && Enum.IsDefined(typeof(IngredientCapability), action.Capability)
                    && !string.IsNullOrWhiteSpace(action.PolicyId) && !string.IsNullOrWhiteSpace(action.PolicyVersion)
                    && !string.IsNullOrWhiteSpace(action.Reason))
                && binding.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() == binding.Actions.Count,
                "Unknown graph/action enum, missing policy or duplicate action identity.");
            var node = binding.Graph.Nodes.SingleOrDefault(value => value.Id == binding.Identity.IngredientId);
            Require(node != null && node.HasContent && node.Kind == binding.Identity.Kind
                && node.Subtype == binding.Identity.Subtype && node.SemanticRole == binding.Identity.SemanticRole
                && node.SourcePredicateId == binding.Identity.SourcePredicateId
                && node.PrimaryOwnerLane == binding.Identity.Lane
                && node.SourcePageOrListItemIdentity == binding.Source.PageOrListItemIdentity
                && node.SourceVersionIdentity == binding.Source.SourceVersion
                && node.EvidenceDigest == binding.Source.SourceArtifactDigest,
                "The assessed graph node differs from the independently bound claim.");
            var evaluation = PageIngredientPlanEvaluator.Evaluate(binding.Graph, binding.Actions);
            Require(evaluation.Outcome != PageMigrationOutcome.Invalid
                && !evaluation.Issues.Any(issue => issue.Severity == MigrationIssueSeverity.Error),
                "PnP ingredient action/dependency/policy validation failed.");
            var action = binding.Actions.SingleOrDefault(value => value.IngredientId == binding.Identity.IngredientId);
            Require(action != null, "The assessed ingredient requires exactly one legal action.");
            Equal(action.ActionId, binding.NativeActionId, "annex/native action mapping");
            Equal(action.TargetIdentity, binding.Target.TargetIdentity, "action target");
            MigrationActionSignature.Validate(binding.ActionSignature);
            Equal(binding.ActionSignature.ActionId, action.ActionId, "action signature identity");
            Equal(binding.ActionSignature.ActionKind, IngredientExternalEvidenceContract.LifecycleReceiptSchema, "action protocol");
            Equal(binding.ActionSignature.TargetIdentity, action.TargetIdentity, "signature target");
            Equal(binding.ActionSignature.SourceEvidenceDigest, binding.Source.SourceArtifactDigest, "signature source");
            Equal(binding.ActionSignature.SelectionReceiptDigest, binding.PlanDigest, "signature admitted selection");
            Equal(binding.ActionSignature.SemanticDigest, ActionSemanticDigest(binding), "signature graph/action closure");
            Require(binding.ActionSignature.DependencySignatures != null && binding.ActionSignature.DependencySignatures.Count == 0,
                "The external action signature seals the complete scoped graph/action closure.");
            ValidateRuntimeManifest(binding);
        }

        private static void ValidateRuntimeManifest(IngredientExternalPlanBinding binding)
        {
            var manifest = binding.RuntimeManifest;
            Require(manifest != null && manifest.SchemaVersion == RuntimeVerificationContractValidator.ManifestSchemaV1
                && manifest.Requirements != null && manifest.Requirements.Count == 2
                && manifest.Requirements.All(value => value != null && value.Required)
                && manifest.Requirements.Count(value => value.Kind == RuntimeVerificationRequirementKind.PageReachability) == 1
                && manifest.Requirements.Count(value => value.Kind == RuntimeVerificationRequirementKind.ErrorShellAbsence) == 1,
                "Lifecycle/v2 supports only required reachability and error-shell observations. Native/DOM acceptance belongs to its existing authority.");
            RuntimeVerificationContractValidator.ValidateManifest(manifest, binding.Graph, binding.Actions);
        }

        internal static bool Under(string child, string parent)
        {
            return child == parent || child.StartsWith(parent + "/", StringComparison.Ordinal);
        }

        internal static bool ValidPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.StartsWith("/", StringComparison.Ordinal)
                && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains("\\")
                && !path.Contains("?") && !path.Contains("#")
                && !path.Any(char.IsControl) && !Uri.UnescapeDataString(path).Contains("\\")
                && !Uri.UnescapeDataString(path).StartsWith("//", StringComparison.Ordinal)
                && !Uri.UnescapeDataString(path).Split('/').Any(segment => segment == "." || segment == "..");
        }
    }
}
