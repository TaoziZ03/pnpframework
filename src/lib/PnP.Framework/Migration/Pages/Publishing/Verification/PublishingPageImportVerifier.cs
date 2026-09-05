using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Lifecycle;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Topology.Ingredients;
using PnP.Framework.Migration.Pages.Fields.Taxonomy;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Layouts;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Content;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Verification
{
    internal static class PublishingPageImportVerifier
    {
        public static PublishingPageImportReceipt Verify(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            PublishingPageExecutionScope executionScope,
            string approvedPlanDigest,
            Guid operationId,
            DateTimeOffset startedAt,
            int materializedDependencyCount,
            TopologyMaterializationReceipt topologyReceipt,
            IList<ListMaterializationReceipt> listReceipts,
            IList<PageFieldImportResult> fieldResults,
            IList<MigrationMutationReceipt> steps,
            IEnumerable<string> warnings,
            string expectedContentTypeId,
            SharedTopologyExecutionProof sharedTopologyProof = null,
            PublishingPageImportExecutionSeam executionSeam = null)
        {
            if (executionSeam != null)
            {
                return VerifyControlledStorage(
                    package,
                    executionScope,
                    approvedPlanDigest,
                    operationId,
                    startedAt,
                    materializedDependencyCount,
                    topologyReceipt,
                    listReceipts,
                    fieldResults,
                    steps,
                    warnings,
                    expectedContentTypeId,
                    sharedTopologyProof,
                    executionSeam);
            }
            if (!executionScope.PageArtifact)
            {
                return VerifyComponents(
                    targetContext,
                    package,
                    executionScope,
                    approvedPlanDigest,
                    operationId,
                    startedAt,
                    materializedDependencyCount,
                    topologyReceipt,
                    listReceipts,
                    steps,
                    warnings,
                    sharedTopologyProof);
            }
            using (var verificationContext = targetContext.Clone(package.Plan.TargetWebUrl))
            {
                var pages = verificationContext.Web.GetList(
                    package.Plan.TargetProbe?.PagesLibraryServerRelativeUrl
                    ?? PagePath.GetDirectoryName(package.Plan.TargetPageServerRelativeUrl));

                var file = verificationContext.Web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(package.Plan.TargetPageServerRelativeUrl));
                var executableTaxonomyActions = executionScope.TaxonomyActions(package).ToArray();
                var executableFieldActions = executionScope.PageFieldActions(package)
                    .Where(value => value.WillApply)
                    .ToArray();
                var ordinaryViewFields = string.Join(
                    Environment.NewLine,
                    executableFieldActions
                        .Where(value => value.Disposition != PageFieldDisposition.ApplyTaxonomyRelationships)
                        .Select(value => value.TargetInternalName)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Concat(new[] { "PublishingPageLayout" })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .Select(value => "    <FieldRef Name='" + System.Security.SecurityElement.Escape(value) + "' />"));
                var taxonomyViewFields = string.Join(
                    Environment.NewLine,
                    executableTaxonomyActions
                        .Select(value => value.SourceFieldInternalName)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .Select(value => "    <FieldRef Name='" + System.Security.SecurityElement.Escape(value) + "' />"));
                var taxCatchAllViewField = executableTaxonomyActions.Length > 0
                    ? "    <FieldRef Name='TaxCatchAll' />"
                    : string.Empty;
                var items = pages.GetItems(new CamlQuery
                {
                    ViewXml = $@"<View Scope='RecursiveAll'>
  <Query>
    <Where>
      <Eq>
        <FieldRef Name='FileRef' />
        <Value Type='Text'>{System.Security.SecurityElement.Escape(package.Plan.TargetPageServerRelativeUrl)}</Value>
      </Eq>
    </Where>
  </Query>
  <ViewFields>
    <FieldRef Name='ID' />
    <FieldRef Name='ContentTypeId' />
    <FieldRef Name='PublishingPageContent' />
    <FieldRef Name='_ModerationStatus' />
{ordinaryViewFields}
{taxonomyViewFields}
{taxCatchAllViewField}
  </ViewFields>
  <RowLimit>1</RowLimit>
</View>"
                });
                verificationContext.Load(file,
                    value => value.Exists,
                    value => value.UniqueId,
                    value => value.UIVersionLabel,
                    value => value.Level,
                    value => value.CheckOutType,
                    value => value.Properties);
                verificationContext.Load(pages, value => value.EnableModeration);
                verificationContext.Load(items);
                verificationContext.ExecuteQueryRetry();
                if (!file.Exists)
                {
                    throw new InvalidOperationException("Fresh target readback could not find the imported page.");
                }

                var item = items.SingleOrDefault();
                if (item == null)
                {
                    throw new InvalidOperationException("Fresh target readback could not find the imported page list item.");
                }

                verificationContext.Load(item, value => value.HasUniqueRoleAssignments);
                verificationContext.ExecuteQueryRetry();

                var content = PublishingPageCaptureReader.GetFieldString(item, "PublishingPageContent") ?? string.Empty;
                var contentTypeId = PublishingPageCaptureReader.GetFieldString(item, "ContentTypeId") ?? string.Empty;
                var executableWebPartActions = executionScope.WebPartActions(package);
                var executableWebPartIds = new HashSet<Guid>(
                    executableWebPartActions.Select(value => value.SourceWebPartId));
                var executableWebParts = package.Snapshot.WebParts
                    .Where(value => executableWebPartIds.Contains(value.Id))
                    .ToArray();
                var executionReplacements = PublishingPageExecutionReplacementProjector.Project(
                    package,
                    executionScope);
                var webPartResults = PublishingPageWebPartVerifier.Verify(
                    verificationContext,
                    package.Plan.TargetPageServerRelativeUrl,
                    executableWebParts,
                    package.Snapshot.ListWebPartBindings,
                    executableWebPartActions,
                    listReceipts,
                    executionReplacements,
                    executableWebParts.Length == package.Snapshot.WebParts.Count);
                var persistedDigest = PublishingPageDigest.ComputeSha256(content);
                var expectedExecutionContent = PageTextTransformer.Rewrite(
                    package.Snapshot.PublishingPageContent,
                    executionReplacements);
                var expectedExecutionContentDigest = PublishingPageDigest.ComputeSha256(expectedExecutionContent);
                var receiptWarnings = warnings
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var storageContentEqual = !executionScope.PublishingContent
                    || PublishingPageContentStorageCanonicalizer.AreEquivalent(
                        expectedExecutionContent,
                        content);
                if (!storageContentEqual)
                {
                    receiptWarnings.Add("PublishingPageContent storage bytes differ from the approved digest. Storage verification failed; runtime verification cannot override this mismatch.");
                }
                else if (executionScope.PublishingContent
                    && !string.Equals(persistedDigest, expectedExecutionContentDigest, StringComparison.OrdinalIgnoreCase))
                {
                    receiptWarnings.Add(
                        "SharePoint normalized equivalent HTML character references while persisting PublishingPageContent; canonical authored content matches the approved plan.");
                }

                var expectedContentPresent = executionScope.PublishingContent
                    && !string.IsNullOrWhiteSpace(package.Snapshot.PublishingPageContent);
                var persistedContentPresent = !string.IsNullOrWhiteSpace(content);
                if (expectedContentPresent && !persistedContentPresent)
                {
                    receiptWarnings.Add("Fresh target readback found empty PublishingPageContent even though the approved source snapshot was non-empty.");
                }

                var actualLevel = file.Level.ToString();
                var actualCheckOutType = file.CheckOutType.ToString();
                var actualModerationStatus = PublishingPageCaptureReader.TryGetInt32(item, "_ModerationStatus");
                var effectiveLifecycle = executionScope.Lifecycle
                    ? package.Plan.TargetLifecycle
                    : PublishingPageTargetLifecycle.Draft;
                var lifecycleVerification = PublishingPageLifecycleVerifier.Verify(
                    effectiveLifecycle,
                    pages.EnableModeration,
                    file.Level,
                    file.CheckOutType,
                    actualModerationStatus);
                var moderationContractMatched = package.Plan.TargetProbe != null
                    && package.Plan.TargetProbe.EnableModeration == pages.EnableModeration;
                var lifecycleMatched = moderationContractMatched && lifecycleVerification.Matched;
                if (!lifecycleMatched)
                {
                    receiptWarnings.Add(
                        $"Target lifecycle mismatch. Expected {effectiveLifecycle}; actual level is {actualLevel}, checkout state is {actualCheckOutType}, moderation-enabled is {pages.EnableModeration}, moderation is {(actualModerationStatus.HasValue ? actualModerationStatus.Value.ToString() : "unknown")}. "
                        + (moderationContractMatched ? lifecycleVerification.Message : "The target Pages-library moderation setting changed after approval."));
                }

                var securityMatched = !executionScope.Security
                    || package.Snapshot.Security.HasUniqueRoleAssignments
                    || !item.HasUniqueRoleAssignments;
                if (!securityMatched)
                {
                    receiptWarnings.Add("Target security mismatch. The source page inherited permissions, but the target page has unique role assignments.");
                }
                var ownershipMatched = string.Equals(
                        Property(file.Properties, PublishingPageTargetOwnership.OriginalIdentifierPropertyName),
                        package.Plan.OriginalIdentifier,
                        StringComparison.Ordinal)
                    && string.Equals(
                        Property(file.Properties, PublishingPageTargetOwnership.SourceSnapshotDigestPropertyName),
                        package.SnapshotDigest,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        Property(file.Properties, PublishingPageTargetOwnership.PlanDigestPropertyName),
                        package.PlanDigest,
                        StringComparison.OrdinalIgnoreCase);
                if (!ownershipMatched)
                {
                    receiptWarnings.Add("Target Page ownership provenance differs from the approved source identity, snapshot digest, or plan digest.");
                }

                var freshFieldResults = PublishingPageFieldFreshReadbackVerifier.Verify(
                    item,
                    package.Snapshot.Fields,
                    executableFieldActions,
                    executionReplacements,
                    fieldResults);
                var plannedFieldsPassed = freshFieldResults.Count == executableFieldActions.Length
                    && freshFieldResults.All(result => result.Attempted && result.Succeeded);
                if (!plannedFieldsPassed)
                {
                    receiptWarnings.Add("Fresh page field readback did not verify every executable field value.");
                    receiptWarnings.AddRange(freshFieldResults
                        .Where(value => !value.Succeeded)
                        .Select(value => value.InternalName + ": " + value.Message));
                }
                var layoutMatched = PublishingPageFieldFreshReadbackVerifier.LayoutMatches(
                    item,
                    package.Plan.LayoutMaterialization?.TargetServerRelativeUrl);
                if (!layoutMatched)
                {
                    receiptWarnings.Add("Fresh PublishingPageLayout readback differs from the approved target Page Layout path.");
                }
                var taxonomyRelationshipResults = PageTaxonomyRelationshipVerifier.Verify(
                    verificationContext,
                    pages,
                    item,
                    package.Snapshot.Fields,
                    executableTaxonomyActions,
                    freshFieldResults);
                var taxonomyRelationshipsMatched = taxonomyRelationshipResults.All(value => value.Passed)
                    && taxonomyRelationshipResults.Count == executableTaxonomyActions.Length;
                if (!taxonomyRelationshipsMatched)
                {
                    receiptWarnings.Add("Fresh taxonomy readback did not reproduce every sealed relationship exactly.");
                    receiptWarnings.AddRange(taxonomyRelationshipResults
                        .Where(value => !value.Passed)
                        .Select(value => value.SourceFieldInternalName + ":" + value.SourceTermId.ToString("D") + ": " + value.Message));
                }
                var webPartsMatched = webPartResults.All(result => result.Passed)
                    && webPartResults.Count == executableWebParts.Length;
                var topologyMatched = TopologyMatched(package, executionScope, topologyReceipt, sharedTopologyProof);
                var listsMatched = ListsMatched(executionScope, listReceipts);
                if (!topologyMatched)
                {
                    receiptWarnings.Add("Fresh topology readback did not verify every approved Site/Web mapping.");
                }
                foreach (var listReceipt in listReceipts.Where(value => !value.FreshReadbackPassed))
                {
                    receiptWarnings.AddRange(listReceipt.Diagnostics.Select(value => "List " + listReceipt.SourceListId.ToString("D") + ": " + value));
                }
                if (!listsMatched)
                {
                    receiptWarnings.Add("Fresh List readback did not verify every captured List dependency.");
                }
                var contentTypeMatched = !executionScope.ContentType
                    || PublishingPageContentTypeIdentity.MatchesSiteContentType(
                        contentTypeId,
                        expectedContentTypeId);
                if (!contentTypeMatched)
                {
                    receiptWarnings.Add($"Target Content Type mismatch. Expected '{expectedContentTypeId ?? "unavailable"}'; actual '{contentTypeId}'.");
                }
                var expectedMaterializedDependencies = executionScope.ReferenceActions(package)
                    .Count(value => value.Disposition == PageReferenceDisposition.MaterializeAtTarget);
                var dependenciesMatched = materializedDependencyCount == expectedMaterializedDependencies;
                if (!dependenciesMatched)
                {
                    receiptWarnings.Add(
                        $"Materialized dependency count differs. Expected {expectedMaterializedDependencies}; observed {materializedDependencyCount}.");
                }
                var readbackPassed = contentTypeMatched
                    && storageContentEqual
                    && webPartsMatched
                    && lifecycleMatched
                    && securityMatched
                    && ownershipMatched
                    && layoutMatched
                    && plannedFieldsPassed
                    && taxonomyRelationshipsMatched
                    && topologyMatched
                    && listsMatched
                    && dependenciesMatched;
                AddFrontierWarnings(receiptWarnings, executionScope);
                var sourceFidelityLimited = SourceFidelityLimited(package);
                if (sourceFidelityLimited)
                {
                    receiptWarnings.Add("Source Web fidelity remains authorization-limited; target path and storage verification do not claim source topology parity.");
                }
                var runtimeVerificationRequired = executionScope.Runtime
                    && package.Plan.RuntimeVerification.Requirements.Any(item => item.Required);
                var completedIngredientIds = executionScope.ExecutableIngredientIds;
                var ingredientVerification = PublishingPageIngredientVerificationProjector.Project(
                    package,
                    executionScope,
                    new PublishingPageIngredientVerificationEvidence
                    {
                        StructuralMaterializersPassed = true,
                        PageArtifactMatched = ownershipMatched,
                        ContentTypeMatched = contentTypeMatched,
                        PublishingContentMatched = storageContentEqual,
                        SecurityMatched = securityMatched,
                        LifecycleMatched = lifecycleMatched,
                        TaxonomyRelationshipsMatched = taxonomyRelationshipsMatched,
                        TopologyMatched = topologyMatched,
                        DependenciesMatched = dependenciesMatched,
                        RuntimeVerificationRequired = runtimeVerificationRequired,
                        FieldResults = freshFieldResults,
                        WebPartResults = webPartResults,
                        ListReceipts = listReceipts
                    });
                return new PublishingPageImportReceipt
                {
                    StartedAtUtc = startedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    OperationId = operationId,
                    ExecutionStatus = readbackPassed
                        ? SuccessStatus(executionScope)
                        : MigrationExecutionStatus.FailedUnexpectedly,
                    PartialExecution = executionScope.IsPartial,
                    ExecutionFrontier = executionScope.Frontier,
                    CompletedIngredientIds = completedIngredientIds,
                    VerifiedIngredientIds = ingredientVerification.VerifiedIngredientIds,
                    PendingVerificationIngredientIds = ingredientVerification.PendingIngredientIds,
                    FailedVerificationIngredientIds = ingredientVerification.FailedIngredientIds,
                    DeferredIngredientCount = DeferredCount(executionScope),
                    AuthorizationBlockedIngredientCount = AuthorizationBlockedCount(executionScope),
                    MutationStarted = true,
                    Steps = steps,
                    ApprovedPlanDigest = approvedPlanDigest,
                    TargetWebUrl = package.Plan.TargetWebUrl,
                    TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                    TargetFileUniqueId = file.UniqueId,
                    TargetListItemId = item.Id,
                    TargetContentTypeId = contentTypeId,
                    TargetVersionLabel = file.UIVersionLabel,
                    ExpectedLifecycle = effectiveLifecycle,
                    ApprovedLifecycle = package.Plan.TargetLifecycle,
                    ActualFileLevel = actualLevel,
                    ActualCheckOutType = actualCheckOutType,
                    ActualModerationStatus = actualModerationStatus,
                    LifecycleMatched = lifecycleMatched,
                    SecurityMatched = securityMatched,
                    OwnershipMatched = ownershipMatched,
                    PageArtifactMatched = ownershipMatched,
                    LayoutMatched = layoutMatched,
                    ContentTypeMatched = contentTypeMatched,
                    PageFieldsMatched = plannedFieldsPassed,
                    DependenciesMatched = dependenciesMatched,
                    ApprovedPublishingPageContentSha256 = package.Plan.ExpectedPublishingPageContentSha256,
                    ExpectedPublishingPageContentSha256 = expectedExecutionContentDigest,
                    PersistedPublishingPageContentSha256 = persistedDigest,
                    StorageContentEqual = storageContentEqual,
                    ImportedWebPartCount = webPartResults.Count(result => result.TargetWebPartId.HasValue),
                    WebPartsMatched = webPartsMatched,
                    WebPartResults = webPartResults,
                    MaterializedDependencyCount = materializedDependencyCount,
                    TopologyMaterialization = topologyReceipt,
                    SharedTopologyMaterialization = sharedTopologyProof?.Receipt,
                    TopologyMatched = topologyMatched,
                    ListMaterializations = listReceipts,
                    ApprovedProtectedDocumentExclusionCount = ApprovedProtectedDocumentExclusionCount(listReceipts),
                    DroppedDependentItemAbsentCount = DroppedDependentItemCount(
                        listReceipts,
                        DroppedDependentTargetIdentityStatus.Absent),
                    DroppedDependentItemPresentCount = DroppedDependentItemCount(
                        listReceipts,
                        DroppedDependentTargetIdentityStatus.Present),
                    ListsMatched = listsMatched,
                    FieldResults = freshFieldResults,
                    TaxonomyRelationshipsMatched = taxonomyRelationshipsMatched,
                    TaxonomyRelationshipResults = taxonomyRelationshipResults,
                    FreshReadbackPassed = readbackPassed,
                    StorageVerificationStatus = readbackPassed
                        ? StorageVerificationStatus.Passed
                        : StorageVerificationStatus.Failed,
                    RuntimeVerificationStatus = runtimeVerificationRequired
                        ? RuntimeVerificationStatus.Pending
                        : RuntimeVerificationStatus.NotRequired,
                    AcceptanceStatus = !readbackPassed
                        ? MigrationAcceptanceStatus.Rejected
                        : runtimeVerificationRequired
                            ? MigrationAcceptanceStatus.Pending
                            : executionScope.IsPartial || sourceFidelityLimited
                                ? MigrationAcceptanceStatus.PartiallyAccepted
                                : MigrationAcceptanceStatus.Accepted,
                    Warnings = receiptWarnings.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList(),
                };
            }
        }

        private static PublishingPageImportReceipt VerifyControlledStorage(
            PublishingPageMigrationPackage package,
            PublishingPageExecutionScope executionScope,
            string approvedPlanDigest,
            Guid operationId,
            DateTimeOffset startedAt,
            int materializedDependencyCount,
            TopologyMaterializationReceipt topologyReceipt,
            IList<ListMaterializationReceipt> listReceipts,
            IList<PageFieldImportResult> fieldResults,
            IList<MigrationMutationReceipt> steps,
            IEnumerable<string> warnings,
            string expectedContentTypeId,
            SharedTopologyExecutionProof sharedTopologyProof,
            PublishingPageImportExecutionSeam executionSeam)
        {
            var state = executionSeam.ReadTargetPage();
            if (state == null || !state.Exists)
            {
                throw new InvalidOperationException("Fresh controlled target readback could not find the imported page.");
            }

            var replacements = PublishingPageExecutionReplacementProjector.Project(package, executionScope);
            var expectedContent = PageTextTransformer.Rewrite(package.Snapshot.PublishingPageContent, replacements);
            var content = StringField(state.Fields, "PublishingPageContent");
            var storageContentEqual = !executionScope.PublishingContent
                || PublishingPageContentStorageCanonicalizer.AreEquivalent(expectedContent, content);
            var contentTypeId = StringField(state.Fields, "ContentTypeId");
            var contentTypeMatched = !executionScope.ContentType
                || PublishingPageContentTypeIdentity.MatchesSiteContentType(contentTypeId, expectedContentTypeId);
            var effectiveLifecycle = executionScope.Lifecycle
                ? package.Plan.TargetLifecycle
                : PublishingPageTargetLifecycle.Draft;
            var actualLevel = state.Level.ToString();
            var actualCheckOutType = state.CheckOutType.ToString();
            var actualModerationStatus = IntField(state.Fields, "_ModerationStatus");
            var lifecycleVerification = PublishingPageLifecycleVerifier.Verify(
                effectiveLifecycle,
                state.PagesLibraryModerationEnabled,
                state.Level,
                state.CheckOutType,
                actualModerationStatus);
            var moderationContractMatched = package.Plan.TargetProbe != null
                && state.PagesLibraryModerationEnabled.HasValue
                && package.Plan.TargetProbe.EnableModeration == state.PagesLibraryModerationEnabled.Value;
            var lifecycleMatched = moderationContractMatched && lifecycleVerification.Matched;
            var securityMatched = !executionScope.Security
                || package.Snapshot.Security.HasUniqueRoleAssignments
                || !state.HasUniqueRoleAssignments;
            var ownershipMatched = PublishingPageTargetOwnership.MatchesApprovedPlan(
                state.Properties,
                package.Plan.OriginalIdentifier,
                package.SnapshotDigest,
                package.PlanDigest);
            var executableFieldActions = executionScope.PageFieldActions(package)
                .Where(value => value.WillApply)
                .ToArray();
            var freshFieldResults = PublishingPageFieldFreshReadbackVerifier.Verify(
                state.Fields,
                package.Snapshot.Fields,
                executableFieldActions,
                replacements,
                fieldResults);
            var plannedFieldsPassed = freshFieldResults.Count == executableFieldActions.Length
                && freshFieldResults.All(value => value.Attempted && value.Succeeded);
            var layoutMatched = PublishingPageFieldFreshReadbackVerifier.LayoutMatches(
                state.Fields,
                package.Plan.LayoutMaterialization?.TargetServerRelativeUrl);
            var topologyMatched = TopologyMatched(package, executionScope, topologyReceipt, sharedTopologyProof);
            var listsMatched = ListsMatched(executionScope, listReceipts);
            var expectedMaterializedDependencies = executionScope.ReferenceActions(package)
                .Count(value => value.Disposition == PageReferenceDisposition.MaterializeAtTarget);
            var dependenciesMatched = materializedDependencyCount == expectedMaterializedDependencies;
            var webPartsMatched = executionScope.WebPartActions(package).Count == 0;
            var taxonomyRelationshipsMatched = executionScope.TaxonomyActions(package).Count == 0;
            var readbackPassed = storageContentEqual
                && contentTypeMatched
                && lifecycleMatched
                && securityMatched
                && ownershipMatched
                && layoutMatched
                && plannedFieldsPassed
                && topologyMatched
                && listsMatched
                && dependenciesMatched
                && webPartsMatched
                && taxonomyRelationshipsMatched;
            var receiptWarnings = (warnings ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (!storageContentEqual) receiptWarnings.Add("Fresh PublishingPageContent readback differs from the approved rewritten content.");
            if (!contentTypeMatched) receiptWarnings.Add("Fresh ContentTypeId readback differs from the approved target content type.");
            if (!lifecycleMatched) receiptWarnings.Add(
                $"Target lifecycle mismatch. Expected {effectiveLifecycle}; actual level is {actualLevel}, checkout state is {actualCheckOutType}, moderation-enabled is {(state.PagesLibraryModerationEnabled.HasValue ? state.PagesLibraryModerationEnabled.Value.ToString() : "unknown")}, moderation is {(actualModerationStatus.HasValue ? actualModerationStatus.Value.ToString() : "unknown")}. "
                + (moderationContractMatched ? lifecycleVerification.Message : "The target Pages-library moderation setting is unavailable or changed after approval."));
            if (!securityMatched) receiptWarnings.Add("Fresh target security readback differs from the approved inheritance policy.");
            if (!ownershipMatched) receiptWarnings.Add("Fresh target ownership provenance differs from the approved sealed plan.");
            if (!layoutMatched) receiptWarnings.Add("Fresh PublishingPageLayout readback differs from the approved target Page Layout path.");
            if (!plannedFieldsPassed) receiptWarnings.Add("Fresh page field readback did not verify every executable field value.");
            if (!topologyMatched) receiptWarnings.Add("Fresh topology evidence did not verify every target Web mapping.");
            if (!listsMatched) receiptWarnings.Add("Fresh List evidence did not verify every executable List transaction.");
            if (!dependenciesMatched) receiptWarnings.Add("Materialized dependency count differs from the admitted dependency frontier.");
            if (!webPartsMatched) receiptWarnings.Add("The controlled target storage session does not support executable Web Part verification.");
            if (!taxonomyRelationshipsMatched) receiptWarnings.Add("The controlled target storage session does not support executable taxonomy relationship verification.");

            var runtimeVerificationRequired = executionScope.Runtime
                && package.Plan.RuntimeVerification.Requirements.Any(value => value.Required);
            var ingredientVerification = PublishingPageIngredientVerificationProjector.Project(
                package,
                executionScope,
                new PublishingPageIngredientVerificationEvidence
                {
                    StructuralMaterializersPassed = topologyMatched && listsMatched,
                    PageArtifactMatched = ownershipMatched,
                    ContentTypeMatched = contentTypeMatched,
                    PublishingContentMatched = storageContentEqual,
                    SecurityMatched = securityMatched,
                    LifecycleMatched = lifecycleMatched,
                    TaxonomyRelationshipsMatched = taxonomyRelationshipsMatched,
                    TopologyMatched = topologyMatched,
                    DependenciesMatched = dependenciesMatched,
                    RuntimeVerificationRequired = runtimeVerificationRequired,
                    FieldResults = freshFieldResults,
                    WebPartResults = new List<PublishingPageWebPartVerificationResult>(),
                    ListReceipts = listReceipts
                });
            return new PublishingPageImportReceipt
            {
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                OperationId = operationId,
                ExecutionStatus = readbackPassed ? SuccessStatus(executionScope) : MigrationExecutionStatus.FailedUnexpectedly,
                PartialExecution = executionScope.IsPartial,
                ExecutionFrontier = executionScope.Frontier,
                CompletedIngredientIds = executionScope.ExecutableIngredientIds,
                VerifiedIngredientIds = ingredientVerification.VerifiedIngredientIds,
                PendingVerificationIngredientIds = ingredientVerification.PendingIngredientIds,
                FailedVerificationIngredientIds = ingredientVerification.FailedIngredientIds,
                MutationStarted = true,
                Steps = steps,
                ApprovedPlanDigest = approvedPlanDigest,
                TargetWebUrl = package.Plan.TargetWebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                TargetFileUniqueId = state.FileUniqueId,
                TargetListItemId = state.ListItemId,
                TargetContentTypeId = contentTypeId,
                TargetVersionLabel = state.VersionLabel,
                ExpectedLifecycle = effectiveLifecycle,
                ApprovedLifecycle = package.Plan.TargetLifecycle,
                ActualFileLevel = actualLevel,
                ActualCheckOutType = actualCheckOutType,
                ActualModerationStatus = actualModerationStatus,
                LifecycleMatched = lifecycleMatched,
                SecurityMatched = securityMatched,
                OwnershipMatched = ownershipMatched,
                PageArtifactMatched = ownershipMatched,
                LayoutMatched = layoutMatched,
                ContentTypeMatched = contentTypeMatched,
                PageFieldsMatched = plannedFieldsPassed,
                DependenciesMatched = dependenciesMatched,
                StorageContentEqual = storageContentEqual,
                WebPartsMatched = webPartsMatched,
                MaterializedDependencyCount = materializedDependencyCount,
                TopologyMaterialization = topologyReceipt,
                TopologyMatched = topologyMatched,
                ListMaterializations = listReceipts,
                ListsMatched = listsMatched,
                FieldResults = freshFieldResults,
                TaxonomyRelationshipsMatched = taxonomyRelationshipsMatched,
                FreshReadbackPassed = readbackPassed,
                StorageVerificationStatus = readbackPassed ? StorageVerificationStatus.Passed : StorageVerificationStatus.Failed,
                RuntimeVerificationStatus = runtimeVerificationRequired ? RuntimeVerificationStatus.Pending : RuntimeVerificationStatus.NotRequired,
                AcceptanceStatus = !readbackPassed
                    ? MigrationAcceptanceStatus.Rejected
                    : runtimeVerificationRequired
                        ? MigrationAcceptanceStatus.Pending
                        : executionScope.IsPartial
                            ? MigrationAcceptanceStatus.PartiallyAccepted
                            : MigrationAcceptanceStatus.Accepted,
                Warnings = receiptWarnings.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList()
            };
        }

        private static string StringField(IDictionary<string, object> fields, string name)
        {
            return fields != null && fields.TryGetValue(name, out var value)
                ? Convert.ToString(value) ?? string.Empty
                : string.Empty;
        }

        private static int? IntField(IDictionary<string, object> fields, string name)
        {
            return fields != null && fields.TryGetValue(name, out var value) && value != null
                ? Convert.ToInt32(value)
                : (int?)null;
        }

        private static PublishingPageImportReceipt VerifyComponents(
            ClientContext targetContext,
            PublishingPageMigrationPackage package,
            PublishingPageExecutionScope executionScope,
            string approvedPlanDigest,
            Guid operationId,
            DateTimeOffset startedAt,
            int materializedDependencyCount,
            TopologyMaterializationReceipt topologyReceipt,
            IList<ListMaterializationReceipt> listReceipts,
            IList<MigrationMutationReceipt> steps,
            IEnumerable<string> warnings,
            SharedTopologyExecutionProof sharedTopologyProof)
        {
            var receiptWarnings = warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var topologyMatched = TopologyMatched(package, executionScope, topologyReceipt, sharedTopologyProof);
            var listsMatched = ListsMatched(executionScope, listReceipts);
            var expectedMaterializedDependencies = executionScope.ReferenceActions(package)
                .Count(value => value.Disposition == PageReferenceDisposition.MaterializeAtTarget);
            var dependenciesMatched = materializedDependencyCount == expectedMaterializedDependencies;
            var layoutMatched = true;
            if (executionScope.Layout)
            {
                var freshLayoutProbe = PublishingPageLayoutTargetInspector.Inspect(
                    targetContext,
                    package.Plan.LayoutMaterialization);
                var freshLayoutAdmission = PublishingPageLayoutTargetAdmissionEvaluator.Evaluate(
                    package.Plan.LayoutMaterialization,
                    freshLayoutProbe);
                layoutMatched = freshLayoutAdmission.IsEligible && freshLayoutProbe.FileExists;
                if (!layoutMatched)
                {
                    receiptWarnings.Add("Fresh target Page Layout readback did not verify the approved layout transaction.");
                    receiptWarnings.AddRange(freshLayoutAdmission.Issues.Select(value => value.Code + ": " + value.Message));
                }
            }
            if (!topologyMatched)
            {
                receiptWarnings.Add("Fresh topology readback did not verify every Site/Web mapping in the execution frontier.");
            }
            if (!listsMatched)
            {
                receiptWarnings.Add("Fresh List readback did not verify every List transaction in the execution frontier.");
            }
            if (!dependenciesMatched)
            {
                receiptWarnings.Add(
                    $"Materialized dependency count differs. Expected {expectedMaterializedDependencies}; observed {materializedDependencyCount}.");
            }
            foreach (var listReceipt in listReceipts.Where(value => !value.FreshReadbackPassed))
            {
                receiptWarnings.AddRange(listReceipt.Diagnostics.Select(value =>
                    "List " + listReceipt.SourceListId.ToString("D") + ": " + value));
            }
            AddFrontierWarnings(receiptWarnings, executionScope);
            var sourceFidelityLimited = SourceFidelityLimited(package);
            if (sourceFidelityLimited)
            {
                receiptWarnings.Add("Source Web fidelity remains authorization-limited; target path verification does not claim source topology parity.");
            }

            var readbackPassed = topologyMatched && listsMatched && dependenciesMatched && layoutMatched;
            var completedIngredientIds = executionScope.ExecutableIngredientIds;
            var ingredientVerification = PublishingPageIngredientVerificationProjector.Project(
                package,
                executionScope,
                new PublishingPageIngredientVerificationEvidence
                {
                    StructuralMaterializersPassed = layoutMatched,
                    PageArtifactMatched = false,
                    ContentTypeMatched = true,
                    PublishingContentMatched = false,
                    SecurityMatched = false,
                    LifecycleMatched = false,
                    TaxonomyRelationshipsMatched = true,
                    TopologyMatched = topologyMatched,
                    DependenciesMatched = dependenciesMatched,
                    RuntimeVerificationRequired = false,
                    ListReceipts = listReceipts
                });
            return new PublishingPageImportReceipt
            {
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                OperationId = operationId,
                ExecutionStatus = readbackPassed
                    ? SuccessStatus(executionScope)
                    : MigrationExecutionStatus.FailedUnexpectedly,
                PartialExecution = executionScope.IsPartial,
                ExecutionFrontier = executionScope.Frontier,
                CompletedIngredientIds = completedIngredientIds,
                VerifiedIngredientIds = ingredientVerification.VerifiedIngredientIds,
                PendingVerificationIngredientIds = ingredientVerification.PendingIngredientIds,
                FailedVerificationIngredientIds = ingredientVerification.FailedIngredientIds,
                DeferredIngredientCount = DeferredCount(executionScope),
                AuthorizationBlockedIngredientCount = AuthorizationBlockedCount(executionScope),
                MutationStarted = true,
                Steps = steps,
                ApprovedPlanDigest = approvedPlanDigest,
                TargetWebUrl = package.Plan.TargetWebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                ApprovedLifecycle = package.Plan.TargetLifecycle,
                ExpectedLifecycle = PublishingPageTargetLifecycle.Draft,
                LifecycleMatched = true,
                SecurityMatched = true,
                OwnershipMatched = true,
                PageArtifactMatched = true,
                LayoutMatched = layoutMatched,
                ContentTypeMatched = true,
                PageFieldsMatched = true,
                DependenciesMatched = dependenciesMatched,
                StorageContentEqual = true,
                WebPartsMatched = true,
                MaterializedDependencyCount = materializedDependencyCount,
                TopologyMaterialization = topologyReceipt,
                SharedTopologyMaterialization = sharedTopologyProof?.Receipt,
                TopologyMatched = topologyMatched,
                ListMaterializations = listReceipts,
                ApprovedProtectedDocumentExclusionCount = ApprovedProtectedDocumentExclusionCount(listReceipts),
                DroppedDependentItemAbsentCount = DroppedDependentItemCount(
                    listReceipts,
                    DroppedDependentTargetIdentityStatus.Absent),
                DroppedDependentItemPresentCount = DroppedDependentItemCount(
                    listReceipts,
                    DroppedDependentTargetIdentityStatus.Present),
                ListsMatched = listsMatched,
                TaxonomyRelationshipsMatched = true,
                FreshReadbackPassed = readbackPassed,
                StorageVerificationStatus = readbackPassed
                    ? StorageVerificationStatus.Passed
                    : StorageVerificationStatus.Failed,
                RuntimeVerificationStatus = RuntimeVerificationStatus.NotRequired,
                AcceptanceStatus = !readbackPassed
                    ? MigrationAcceptanceStatus.Rejected
                    : executionScope.IsPartial || sourceFidelityLimited
                        ? MigrationAcceptanceStatus.PartiallyAccepted
                        : MigrationAcceptanceStatus.Accepted,
                Warnings = receiptWarnings
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList()
            };
        }

        private static bool TopologyMatched(
            PublishingPageMigrationPackage package,
            PublishingPageExecutionScope executionScope,
            TopologyMaterializationReceipt receipt,
            SharedTopologyExecutionProof sharedProof)
        {
            if (package.Plan.SharedTopologyReference != null)
            {
                try
                {
                    SharedTopologyPageReferenceFactory.ValidateReceipt(
                        package.Plan.SharedTopologyReference,
                        sharedProof?.SourcePlans,
                        sharedProof?.GlobalActionDag,
                        sharedProof?.ActionPlan,
                        sharedProof?.Receipt);
                    return true;
                }
                catch (System.IO.InvalidDataException)
                {
                    return false;
                }
            }
            var expected = executionScope.TopologyPlan?.SiteCollections
                .SelectMany(value => value.Webs)
                .Count() ?? 0;
            return receipt != null
                && receipt.FreshReadbackPassed
                && receipt.Webs.Count == expected;
        }

        private static bool SourceFidelityLimited(PublishingPageMigrationPackage package)
        {
            return package.Plan.SharedTopologyReference?.SourceFidelity?.Any(value =>
                value.State == SourceWebFidelityState.AuthorizationBlocked) == true;
        }

        private static bool ListsMatched(
            PublishingPageExecutionScope executionScope,
            IList<ListMaterializationReceipt> receipts)
        {
            var expected = executionScope.ListScope?.Lists.Count(value => value.HasListScopedWork) ?? 0;
            return receipts != null
                && receipts.Count == expected
                && receipts.All(value => value.FreshReadbackPassed);
        }

        private static MigrationExecutionStatus SuccessStatus(PublishingPageExecutionScope executionScope)
        {
            return executionScope.IsPartial
                ? MigrationExecutionStatus.PartiallySucceeded
                : MigrationExecutionStatus.Succeeded;
        }

        private static int DeferredCount(PublishingPageExecutionScope executionScope)
        {
            return executionScope.Frontier.Decisions.Count(value => value != null
                && (value.State == PageIngredientExecutionState.Deferred
                    || value.State == PageIngredientExecutionState.SkippedByDeferredDependency));
        }

        private static int AuthorizationBlockedCount(PublishingPageExecutionScope executionScope)
        {
            return executionScope.Frontier.Decisions.Count(value => value != null
                && (value.State == PageIngredientExecutionState.AuthorizationBlocked
                    || value.State == PageIngredientExecutionState.SkippedByAuthorizationDependency));
        }

        private static int ApprovedProtectedDocumentExclusionCount(
            IEnumerable<ListMaterializationReceipt> listReceipts)
        {
            return (listReceipts ?? Array.Empty<ListMaterializationReceipt>())
                .Where(value => value != null)
                .Sum(value => value.ProtectedDocumentExclusionVerifications?.Count ?? 0);
        }

        internal static int DroppedDependentItemCount(
            IEnumerable<ListMaterializationReceipt> listReceipts,
            DroppedDependentTargetIdentityStatus status)
        {
            return (listReceipts ?? Array.Empty<ListMaterializationReceipt>())
                .Where(value => value != null)
                .SelectMany(value => value.DroppedDependentItemVerifications
                    ?? Array.Empty<ListDroppedDependentItemVerification>())
                .Count(value => value != null && value.Status == status);
        }

        private static void AddFrontierWarnings(
            ICollection<string> warnings,
            PublishingPageExecutionScope executionScope)
        {
            var deferred = DeferredCount(executionScope);
            var authorizationBlocked = AuthorizationBlockedCount(executionScope);
            if (deferred > 0)
            {
                warnings.Add(
                    deferred + " ingredient(s) remain deferred or were skipped because a required dependency is deferred; their snapshot evidence remains sealed for a later run.");
            }
            if (authorizationBlocked > 0)
            {
                warnings.Add(
                    authorizationBlocked + " ingredient(s) are blocked by retained literal HTTP 401/403 evidence or a required dependency on that evidence; independent ingredients continued.");
            }
        }

        private static string Property(PropertyValues values, string name)
        {
            object value;
            return values != null && values.FieldValues.TryGetValue(name, out value) ? Convert.ToString(value) : null;
        }
    }
}
