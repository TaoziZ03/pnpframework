import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { rowsByStorageClassification } from "./storage-classification.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const workspace = path.resolve(here, "../..");
const runDir = path.join(workspace, "ccd-143/run");
const readRaw = name => readFile(path.join(runDir, name), "utf8");
const sha256 = value => createHash("sha256").update(value).digest("hex");
const names = ["manifest.json", "receipts/admission.json", "receipts/provision.json", "receipts/readiness.json", "receipts/capability.json", "receipts/native-create.json", "receipts/fresh-readback.json", "receipts/cleanup.json", "receipts/post-cleanup.json"];
const raw = Object.fromEntries(await Promise.all(names.map(async name => [name, await readRaw(name)])));
const parsed = Object.fromEntries(Object.entries(raw).map(([name, value]) => [name, JSON.parse(value)]));
const manifest = parsed["manifest.json"];
const admission = parsed["receipts/admission.json"];
const provision = parsed["receipts/provision.json"];
const readiness = parsed["receipts/readiness.json"];
const capability = parsed["receipts/capability.json"];
const create = parsed["receipts/native-create.json"];
const readback = parsed["receipts/fresh-readback.json"];
const cleanup = parsed["receipts/cleanup.json"];
const postCleanup = parsed["receipts/post-cleanup.json"];
const producerArchivePath = "source/build-lifecycle-expressions.live.mjs";
const producerArchiveRaw = await readRaw(producerArchivePath);
const postRunBuilderPath = path.join(workspace, "ccd-143/scripts/build-lifecycle-expressions.mjs");
const postRunBuilderRaw = await readFile(postRunBuilderPath, "utf8");

const expectedRows = [1,2,3,4,5,6,7,8,9,10,11,12,13,14,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37];
const observedRows = readback.pages.map(page => page.rowNumber).sort((a, b) => a - b);
const lifecyclePass = page => page.identity?.valid === true
  && page.storage?.status === 200
  && page.listSchemaReceipt?.status === 200
  && Boolean(page.listSchemaReceipt?.digest)
  && page.contentTypeReceipt?.status === 200
  && Boolean(page.contentTypeReceipt?.digest)
  && page.runtime?.status === 200
  && page.runtime?.containsAccessDenied === false
  && page.runtime?.containsServerError === false;
const lifecyclePassRows = readback.pages.filter(lifecyclePass).map(page => page.rowNumber);
const storageRows = rowsByStorageClassification(readback.pages);
const phaseFiles = names.slice(1);
const artifactDigests = Object.fromEntries(phaseFiles.map(name => [name, { sha256: sha256(raw[name]), bytes: Buffer.byteLength(raw[name]) }]));
const expressionDigests = Object.fromEntries(await Promise.all(Object.entries(manifest.expressions).map(async ([name, declared]) => {
  const value = await readRaw(`expressions/${name}`);
  const actual = { sha256: sha256(value), bytes: Buffer.byteLength(value) };
  return [name, { declared, actual, matches: declared.sha256 === actual.sha256 && declared.bytes === actual.bytes }];
})));
const producerArchiveRef = {
  kind: "workspace-file",
  path: "ccd-143/run/source/build-lifecycle-expressions.live.mjs",
  sha256: sha256(producerArchiveRaw),
  bytes: Buffer.byteLength(producerArchiveRaw)
};
const postRunBuilderRef = {
  kind: "workspace-file",
  path: "ccd-143/scripts/build-lifecycle-expressions.mjs",
  sha256: sha256(postRunBuilderRaw),
  bytes: Buffer.byteLength(postRunBuilderRaw),
  role: "post-run-corrected-builder-not-the-live-producer"
};
const producerIntegrityPass = producerArchiveRef.sha256 === manifest.producerRef.sha256;
const expressionIntegrityPass = Object.values(expressionDigests).every(value => value.matches);
const receiptBindingPass = phaseFiles.every(name => {
  const receipt = parsed[name];
  return receipt.runId === manifest.runId
    && receipt.planDigest === manifest.planDigest
    && receipt.lifecycleDigest === manifest.lifecycleDigest
    && receipt.producerRef?.sha256 === manifest.producerRef.sha256;
});
const mandatoryPhasePass = [admission, provision, readiness, capability, create, cleanup, postCleanup].every(receipt => receipt.verdict === "pass");
const verdict = mandatoryPhasePass
  && producerIntegrityPass
  && expressionIntegrityPass
  && receiptBindingPass
  && JSON.stringify(observedRows) === JSON.stringify(expectedRows)
  && create.pages.length === 30
  && create.pages.every(page => page.verdict === "pass")
  && readback.lists.every(list => list.verdict === "pass")
  && lifecyclePassRows.length === 30
  && cleanup.pages.length === 30
  && cleanup.pages.every(page => page.verdict === "pass")
  ? "pass" : "fail";

const report = {
  schema: "ccd.shared-target-lifecycle-verification/v3",
  issue: "CCD-143",
  generatedAtUtc: new Date().toISOString(),
  verdict,
  targetOrigin: manifest.targetOrigin,
  runId: manifest.runId,
  sourcePlanDigest: manifest.sourcePlanDigest,
  planDigest: manifest.planDigest,
  lifecycleDigest: manifest.lifecycleDigest,
  producerRef: manifest.producerRef,
  producerArchiveRef,
  postRunBuilderRef,
  sitePathMappings: manifest.sitePathMappings,
  artifactDigests,
  expressionDigests,
  archiveIntegrity: {
    producerArchiveMatchesDeclaredLiveProducer: producerIntegrityPass,
    allExpressionsMatchManifest: expressionIntegrityPass,
    allReceiptsMatchRunPlanLifecycleAndProducer: receiptBindingPass,
    recoveryEvidence: "The prior run log records expression generation at 13:24:07Z and the post-run aggregation edit at 13:41:16Z. Removing that later eight-line replacement block reproduces the declared live producer SHA-256 exactly; no target rerun was required."
  },
  coverage: {
    expectedPages: 30,
    nativeCreateIdentityBound: create.pages.filter(page => page.verdict === "pass").length,
    freshLifecycleReadbackPass: lifecyclePassRows.length,
    freshListSchemaReceipts: readback.pages.filter(page => page.listSchemaReceipt?.status === 200 && page.listSchemaReceipt?.digest).length,
    freshContentTypeReceipts: readback.pages.filter(page => page.contentTypeReceipt?.status === 200 && page.contentTypeReceipt?.digest).length,
    freshRuntime200: readback.pages.filter(page => page.runtime?.status === 200).length,
    exactStorageRows: storageRows.exact,
    expectedUnavailableStorageRows: storageRows["expected-unavailable"],
    actualUnavailableStorageRows: storageRows["actual-unavailable"],
    expectedAndActualUnavailableStorageRows: storageRows["expected-and-actual-unavailable"],
    mismatchDeferredStorageRows: storageRows["mismatch-deferred"],
    deferredIngredientRows: storageRows["mismatch-deferred"],
    evidenceInvalidRows: expectedRows.filter(row => !lifecyclePassRows.includes(row))
  },
  nativeWebReadiness: readiness.webs.map(web => ({ path: web.path, operationId: web.operationId, webId: web.ownedWebId, objectIdentity: web.ownedObjectIdentity, isProvisioningComplete: web.identity?.isProvisioningComplete, verdict: web.verdict, reasonCode: web.reasonCode })),
  cleanup: {
    pageDeletesPass: cleanup.pages.filter(page => page.verdict === "pass").length,
    siteDeletesPass: cleanup.sites.filter(site => site.verdict === "pass").length,
    markerPostDeleteStatus: cleanup.ownership?.postDeleteStatus,
    freshSiteAbsence: postCleanup.sites.map(site => ({ path: site.path, status: site.status })),
    markerFreshStatus: postCleanup.ownership?.status
  },
  independentOutcomes: {
    lifecycle: verdict === "pass" ? "pass" : "fail",
    storageExactness: storageRows.exact.length === expectedRows.length ? "pass" : "conditional",
    ingredientAssessment: "deferred-to-ingredient-owners",
    rawReadbackAggregatorVerdict: readback.verdict,
    rawReadbackAggregatorReasonCode: readback.reasonCode,
    reconciliation: "The v1 raw receipt over-gated shared lifecycle on source-equal ASPX bytes. Missing expected or actual digests now fail closed and cannot count as exact. Fresh identity, binary presence, list schema/content type and runtime remain first-class receipts; PublishingPageContent/Web Part fidelity is not claimed."
  },
  sourceRequests: 0,
  sourceMutations: 0
};

await writeFile(path.join(runDir, "verification.json"), `${JSON.stringify(report, null, 2)}\n`);
await writeFile(path.join(runDir, "verification.md"), `# CCD-143 shared target lifecycle verification\n\nVerdict: **${verdict.toUpperCase()}** (shared lifecycle scope only)\n\n- Target planDigest: \`${report.planDigest}\`\n- Archived live producer: \`${report.producerArchiveRef.sha256}\` (declared match: ${report.archiveIntegrity.producerArchiveMatchesDeclaredLiveProducer})\n- Actual expressions matching manifest: ${Object.values(report.expressionDigests).filter(value => value.matches).length}/${Object.keys(report.expressionDigests).length}\n- Native create identity-bound: ${report.coverage.nativeCreateIdentityBound}/30\n- Fresh lifecycle readback: ${report.coverage.freshLifecycleReadbackPass}/30\n- Fresh list schema receipts: ${report.coverage.freshListSchemaReceipts}/30\n- Fresh content type receipts: ${report.coverage.freshContentTypeReceipts}/30\n- Fresh runtime HTTP 200: ${report.coverage.freshRuntime200}/30\n- Cleanup: ${report.cleanup.pageDeletesPass}/30 pages, ${report.cleanup.siteDeletesPass}/3 sites, marker ${report.cleanup.markerFreshStatus}\n- Storage digest classification: exact ${report.coverage.exactStorageRows.length}; expected unavailable ${report.coverage.expectedUnavailableStorageRows.length}; actual unavailable ${report.coverage.actualUnavailableStorageRows.length}; both unavailable ${report.coverage.expectedAndActualUnavailableStorageRows.length}; mismatch/deferred ${report.coverage.mismatchDeferredStorageRows.length}.\n\nThe raw v1 readback aggregator remains ${readback.verdict}/${readback.reasonCode}; its bytes and digest were not rewritten. Missing expected or actual digests fail closed and never count as exact. The lifecycle PASS does not claim PublishingPageContent, Web Part, or source-equal ASPX fidelity.\n`);
process.stdout.write(`${JSON.stringify(report)}\n`);
if (verdict !== "pass") process.exitCode = 1;
