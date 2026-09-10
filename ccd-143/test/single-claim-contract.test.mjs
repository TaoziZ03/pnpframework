import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, readFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  assertReceiptBinding,
  cleanupIdentityMatches,
  evaluateReadbackCoverage,
  selectPageAggregate,
  selectSingleClaimExecution
} from "../scripts/single-claim-contract.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const workspace = path.resolve(here, "../..");
const json = async relative => JSON.parse(await readFile(path.join(workspace, relative), "utf8"));
const config = await json("ccd-143/config/ccd-147-row-00003.single-claim.json");
const claim = await json(config.claimLocator);
const implementationEvidence = await json(config.implementationEvidenceLocator);
const plan = await json("ccd-109/run/plan.json");
const sourcePage = await json("ccd-109/run/pages/row-00003.json");
const originalInput = await json("ccd-109/run/target-expression-input.json");
const targetPage = originalInput.pages.find(page => page.rowNumber === 3);

const select = overrides => selectSingleClaimExecution(
  { ...structuredClone(config), ...overrides },
  { claim, implementationEvidence, sourcePage, targetPage, sourcePlanDigest: plan.planDigest }
);

test("selects only row-00003 and its page aggregate dependencies", () => {
  const execution = select({});
  const aggregate = selectPageAggregate(originalInput, execution);
  assert.equal(execution.expectedPageCount, 1);
  assert.deepEqual(aggregate.pages.map(page => page.rowNumber), [3]);
  assert.deepEqual(aggregate.sites.map(site => site.targetPath), ["/sites/biatmicrosoft"]);
  assert.deepEqual(aggregate.webs.map(web => web.targetPath), ["/sites/biatmicrosoft/blog_Archive"]);
  assert.deepEqual(aggregate.libraries.map(library => library.rootPath), ["/sites/biatmicrosoft/blog_Archive/SitePages"]);
});

test("fails closed on wrong row, claim, or admitted plan binding", () => {
  assert.throws(() => select({ rowNumber: 4, rowId: "row-00004" }), /single_claim_row_binding_mismatch/u);
  assert.throws(() => select({ claimId: "a".repeat(64) }), /single_claim_claim_binding_mismatch/u);
  assert.throws(() => select({ admittedPlanDigest: "b".repeat(64) }), /single_claim_plan_binding_mismatch/u);
});

test("fails closed when the immutable PnP evidence does not match", () => {
  assert.throws(() => selectSingleClaimExecution(config, { claim, implementationEvidence: { ...implementationEvidence, code: { ...implementationEvidence.code, implementationCommit: "0".repeat(40) } }, sourcePage, targetPage, sourcePlanDigest: plan.planDigest }), /single_claim_pnp_evidence_mismatch/u);
});

test("rejects stale receipts and accepts a fully bound current receipt", () => {
  const input = {
    runId: "run-1",
    generatedAtUtc: "2026-09-09T20:00:00.000Z",
    planDigest: config.admittedPlanDigest,
    lifecycleDigest: "c".repeat(64),
    targetMappingDigest: "d".repeat(64),
    targetOrigin: config.targetOrigin,
    ownershipMarker: "[CCD-143 run1]",
    producerRef: { sha256: "e".repeat(64) },
    execution: select({})
  };
  const receipt = {
    phase: "fresh-readback",
    runId: input.runId,
    startedAtUtc: "2026-09-09T20:00:01.000Z",
    finishedAtUtc: "2026-09-09T20:00:02.000Z",
    planDigest: input.planDigest,
    lifecycleDigest: input.lifecycleDigest,
    targetMappingDigest: input.targetMappingDigest,
    targetOrigin: input.targetOrigin,
    ownershipMarker: input.ownershipMarker,
    producerRef: input.producerRef,
    executionBinding: {
      mode: input.execution.mode,
      expectedPageCount: 1,
      consumerIssue: input.execution.consumerIssue,
      claimId: input.execution.claimId,
      rowId: input.execution.rowId,
      pnpCommit: input.execution.pnpImplementation.commit,
      admittedPlanDigest: input.execution.admittedPlanDigest
    }
  };
  assert.equal(assertReceiptBinding(receipt, input, "fresh-readback"), true);
  assert.throws(() => assertReceiptBinding({ ...receipt, startedAtUtc: "2026-09-09T19:59:59.000Z" }, input, "fresh-readback"), /receipt_stale_or_invalid_time/u);
});

test("partial single-claim readback fails coverage", () => {
  assert.deepEqual(evaluateReadbackCoverage([], 1), { expected: 1, observed: 0, lifecyclePass: 0, verdict: "fail" });
  assert.equal(evaluateReadbackCoverage([{ lifecycleVerdict: "evidence-invalid" }], 1).verdict, "fail");
  assert.equal(evaluateReadbackCoverage([{ lifecycleVerdict: "pass" }], 1).verdict, "pass");
});

test("cleanup requires file, item, list, path, and marker-owned identity", () => {
  const owned = { fileUniqueId: "11111111-1111-1111-1111-111111111111", itemId: 7, itemUniqueId: "22222222-2222-2222-2222-222222222222", parentListId: "33333333-3333-3333-3333-333333333333", targetPath: "/sites/a/SitePages/x.aspx" };
  assert.equal(cleanupIdentityMatches(owned, { ...owned }), true);
  assert.equal(cleanupIdentityMatches(owned, { ...owned, itemId: 8 }), false);
  assert.equal(cleanupIdentityMatches(owned, { ...owned, targetPath: "/sites/a/SitePages/y.aspx" }), false);
});

test("single-claim builder emits one bound Wiki lifecycle while default stays 30 pages", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "ccd143-single-claim-"));
  const singleOut = path.join(root, "single");
  const single = spawnSync(process.execPath, ["ccd-143/scripts/build-lifecycle-expressions.mjs"], {
    cwd: workspace,
    encoding: "utf8",
    env: { ...process.env, PAPERCLIP_RUN_ID: "ccd255-hermetic", CCD143_EXECUTION_CONFIG: "ccd-143/config/ccd-147-row-00003.single-claim.json", CCD143_OUT_DIR: singleOut }
  });
  assert.equal(single.status, 0, single.stderr);
  const manifest = await json(path.relative(workspace, path.join(singleOut, "manifest.json")));
  const input = await json(path.relative(workspace, path.join(singleOut, "lifecycle-input.json")));
  assert.equal(manifest.planDigest, config.admittedPlanDigest);
  assert.equal(manifest.pageCount, 1);
  assert.equal(input.sites.length, 1);
  assert.equal(input.webs.length, 1);
  assert.equal(input.libraries.length, 1);
  const create = await readFile(path.join(singleOut, "expressions/05-native-create.js"), "utf8");
  const readback = await readFile(path.join(singleOut, "expressions/06-fresh-readback.js"), "utf8");
  const cleanup = await readFile(path.join(singleOut, "expressions/07-cleanup.js"), "utf8");
  assert.match(create, /AddTemplateFile\.WikiPage/u);
  assert.match(create, /NATIVE_CREATE_SINGLE_CLAIM_PASS/u);
  assert.match(readback, /FRESH_LIFECYCLE_READBACK_SINGLE_CLAIM_PASS/u);
  assert.match(readback, /layoutEvidence/u);
  assert.match(cleanup, /ownedItemUniqueId/u);

  const batchOut = path.join(root, "batch");
  const batch = spawnSync(process.execPath, ["ccd-143/scripts/build-lifecycle-expressions.mjs"], {
    cwd: workspace,
    encoding: "utf8",
    env: { ...process.env, PAPERCLIP_RUN_ID: "ccd143-default-hermetic", CCD143_EXECUTION_CONFIG: "", CCD143_OUT_DIR: batchOut }
  });
  assert.equal(batch.status, 0, batch.stderr);
  const batchManifest = JSON.parse(await readFile(path.join(batchOut, "manifest.json"), "utf8"));
  const batchCreate = await readFile(path.join(batchOut, "expressions/05-native-create.js"), "utf8");
  assert.equal(batchManifest.pageCount, 30);
  assert.equal(batchManifest.expectedPageCount, 30);
  assert.match(batchCreate, /NATIVE_CREATE_30_OF_30_PASS/u);
});
