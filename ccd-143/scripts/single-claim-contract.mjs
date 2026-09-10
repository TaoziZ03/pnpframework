import path from "node:path";

const SHA256 = /^[a-f0-9]{64}$/u;
const GIT_COMMIT = /^(?:[a-f0-9]{40}|[a-f0-9]{64})$/u;
const GUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/u;

const fail = code => {
  throw new Error(code);
};

const equalGuid = (left, right) => String(left || "").replace(/[{}]/gu, "").toLowerCase() === String(right || "").replace(/[{}]/gu, "").toLowerCase();

export const DEFAULT_EXECUTION = Object.freeze({
  mode: "batch-30",
  expectedPageCount: 30,
  consumerIssue: null,
  claimId: null
});

export function selectSingleClaimExecution(config, { claim, implementationEvidence, sourcePage, targetPage, sourcePlanDigest }) {
  if (config?.schema !== "ccd143.single-claim-execution/v1" || config?.mode !== "single-claim") fail("single_claim_config_schema_invalid");
  if (!SHA256.test(config.claimId || "")) fail("single_claim_id_invalid");
  if (!SHA256.test(config.admittedPlanDigest || "")) fail("single_claim_plan_digest_invalid");
  if (!GIT_COMMIT.test(config.pnpImplementation?.commit || "")) fail("single_claim_pnp_commit_invalid");
  if (config.targetOrigin !== "https://a830edad9050849cupcollect.sharepoint.com") fail("single_claim_target_origin_invalid");
  if (config.targetProfile !== "cupcollect-classic-page/v1") fail("single_claim_target_profile_invalid");
  if (config.rowId !== `row-${String(config.rowNumber).padStart(5, "0")}`) fail("single_claim_row_id_invalid");
  if (claim?.claimId !== config.claimId || claim?.claimIssue !== config.consumerIssue) fail("single_claim_claim_binding_mismatch");
  if (implementationEvidence?.claimId !== config.claimId || implementationEvidence?.issue !== config.consumerIssue || implementationEvidence?.code?.implementationCommit !== config.pnpImplementation.commit || implementationEvidence?.code?.branch !== config.pnpImplementation.branch) fail("single_claim_pnp_evidence_mismatch");
  if (!equalGuid(implementationEvidence?.sourceBinding?.listId, config.source.listId) || implementationEvidence?.sourceBinding?.itemId !== config.source.itemId || !equalGuid(implementationEvidence?.sourceBinding?.fileUniqueId, config.source.fileUniqueId) || implementationEvidence?.sourceBinding?.version !== config.source.etag || implementationEvidence?.sourceBinding?.ingredientId !== config.ingredient.id) fail("single_claim_pnp_source_binding_mismatch");
  if (claim?.binding?.planDigest !== config.admittedPlanDigest || sourcePlanDigest !== config.admittedPlanDigest) fail("single_claim_plan_binding_mismatch");
  if (sourcePage?.page?.rowNumber !== config.rowNumber || targetPage?.rowNumber !== config.rowNumber) fail("single_claim_row_binding_mismatch");
  if (sourcePage.page.url !== config.source.pageUrl || targetPage.sourceUrl !== config.source.pageUrl) fail("single_claim_source_url_mismatch");
  if (sourcePage.source.fileServerRelativeUrl !== config.source.fileServerRelativeUrl) fail("single_claim_source_path_mismatch");
  if (!equalGuid(sourcePage.source.listId, config.source.listId) || sourcePage.source.itemId !== config.source.itemId || !equalGuid(sourcePage.source.uniqueId, config.source.fileUniqueId) || sourcePage.source.etag !== config.source.etag) fail("single_claim_source_version_mismatch");
  if (!equalGuid(claim.source?.listId, config.source.listId) || claim.source?.itemId !== config.source.itemId || !equalGuid(claim.source?.uniqueId, config.source.fileUniqueId) || claim.source?.version !== config.source.etag) fail("single_claim_claim_source_mismatch");
  if (claim.ingredient?.id !== config.ingredient.id || claim.ingredient?.kind !== config.ingredient.kind || claim.ingredient?.subtype !== config.ingredient.subtype) fail("single_claim_ingredient_mismatch");
  if (sourcePage.source.listBaseTemplate !== config.ingredient.baseTemplate || sourcePage.source.contentTypeId !== config.ingredient.contentTypeId || sourcePage.source.layout !== config.ingredient.publishingPageLayout) fail("single_claim_layout_value_mismatch");
  if (targetPage.layoutSemantic !== "wiki-native" || config.ingredient.nativeCreateMode !== "AddTemplateFile.WikiPage" || config.ingredient.runtime !== "runtime.wiki") fail("single_claim_runtime_contract_mismatch");

  return {
    mode: config.mode,
    expectedPageCount: 1,
    consumerIssue: config.consumerIssue,
    claimId: config.claimId,
    rowId: config.rowId,
    rowNumber: config.rowNumber,
    claimLocator: config.claimLocator,
    implementationEvidenceLocator: config.implementationEvidenceLocator,
    admittedPlanDigest: config.admittedPlanDigest,
    pnpImplementation: config.pnpImplementation,
    targetProfile: config.targetProfile,
    source: config.source,
    ingredient: config.ingredient
  };
}

export function selectPageAggregate(originalInput, execution) {
  if (execution.mode !== "single-claim") return originalInput;
  const pages = originalInput.pages.filter(page => page.rowNumber === execution.rowNumber);
  if (pages.length !== 1) fail("single_claim_target_row_not_unique");
  const page = pages[0];
  const sourceSitePath = page.targetPath.match(/^\/sites\/[^/]+/u)?.[0];
  if (!sourceSitePath) fail("single_claim_site_path_invalid");
  const sites = originalInput.sites.filter(site => site.targetPath === sourceSitePath);
  const webs = originalInput.webs.filter(web => page.webPath === web.targetPath || page.webPath.startsWith(`${web.targetPath}/`));
  const libraries = originalInput.libraries.filter(library => library.webPath === page.webPath && library.rootPath === page.listRootPath);
  if (sites.length !== 1 || libraries.length !== 1) fail("single_claim_dependency_projection_incomplete");
  return { ...originalInput, sites, webs, libraries, pages };
}

export function assertReceiptBinding(receipt, input, expectedPhase) {
  if (receipt?.phase !== expectedPhase) fail("receipt_phase_mismatch");
  for (const key of ["runId", "planDigest", "lifecycleDigest", "targetOrigin", "ownershipMarker", "targetMappingDigest"]) {
    if (receipt?.[key] !== input?.[key]) fail(`receipt_${key}_mismatch`);
  }
  if (receipt?.producerRef?.sha256 !== input?.producerRef?.sha256) fail("receipt_producer_mismatch");
  if (receipt?.executionBinding?.mode !== input?.execution?.mode || receipt?.executionBinding?.expectedPageCount !== input?.execution?.expectedPageCount) fail("receipt_execution_mode_mismatch");
  if (input.execution.mode === "single-claim") {
    if (receipt.executionBinding.claimId !== input.execution.claimId || receipt.executionBinding.rowId !== input.execution.rowId || receipt.executionBinding.consumerIssue !== input.execution.consumerIssue) fail("receipt_claim_binding_mismatch");
    if (receipt.executionBinding.pnpCommit !== input.execution.pnpImplementation.commit || receipt.executionBinding.admittedPlanDigest !== input.execution.admittedPlanDigest) fail("receipt_claim_producer_binding_mismatch");
  }
  const started = Date.parse(receipt.startedAtUtc);
  const finished = Date.parse(receipt.finishedAtUtc || receipt.startedAtUtc);
  const generated = Date.parse(input.generatedAtUtc);
  if (![started, finished, generated].every(Number.isFinite) || started < generated || finished < started) fail("receipt_stale_or_invalid_time");
  return true;
}

export function evaluateReadbackCoverage(pages, expectedPageCount) {
  const lifecyclePass = pages.filter(page => page.lifecycleVerdict === "pass").length;
  return {
    expected: expectedPageCount,
    observed: pages.length,
    lifecyclePass,
    verdict: pages.length === expectedPageCount && lifecyclePass === expectedPageCount ? "pass" : "fail"
  };
}

export function cleanupIdentityMatches(owned, observed) {
  return Boolean(owned && observed)
    && equalGuid(owned.fileUniqueId, observed.fileUniqueId)
    && owned.itemId === observed.itemId
    && equalGuid(owned.itemUniqueId, observed.itemUniqueId)
    && equalGuid(owned.parentListId, observed.parentListId)
    && owned.targetPath === observed.targetPath;
}

export function resolveWorkspacePath(workspace, value) {
  return path.isAbsolute(value) ? value : path.resolve(workspace, value);
}
