import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { classifyStorageEvidence, rowsByStorageClassification } from "../scripts/storage-classification.mjs";

const manifest = JSON.parse(await readFile(new URL("../run/manifest.json", import.meta.url), "utf8"));
const input = JSON.parse(await readFile(new URL("../run/lifecycle-input.json", import.meta.url), "utf8"));
const readbackRaw = await readFile(new URL("../run/receipts/fresh-readback.json", import.meta.url), "utf8");
const readback = JSON.parse(readbackRaw);
const digest = value => createHash("sha256").update(value).digest("hex");

test("binds the exact target and CCD-109 plan lineage", () => {
  assert.equal(manifest.targetOrigin, "https://a830edad9050849cupcollect.sharepoint.com");
  assert.equal(input.sourceBundle.planDigest, "8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f");
  assert.equal(manifest.sourcePlanDigest, input.sourceBundle.planDigest);
  assert.equal(manifest.planDigest, input.planDigest);
  assert.match(input.planDigest, /^[a-f0-9]{64}$/u);
  assert.equal(input.pages.length, 30);
});

test("only the three known system default page conflicts are suffixed", () => {
  assert.deepEqual(manifest.systemConflictMappings.map(value => value.rowNumber), [8, 11, 23]);
  for (const mapping of manifest.systemConflictMappings) {
    assert.match(mapping.targetPath, /How To Use This Library-ccd35-[a-z0-9]{8}\.aspx$/u);
  }
  assert.equal(input.pages.filter(page => page.mappingReason === "SOURCE_RELATIVE_EXACT").length, 27);
});

test("applies exact-level site conflict suffixes to every target-relative descendant", () => {
  for (const mapping of manifest.sitePathMappings.filter(value => value.reason === "TARGET_SITE_CONFLICT")) {
    assert.match(mapping.targetPath, new RegExp(`^${mapping.sourceTargetPath}-ccd35-[a-z0-9]{8}(?:-[2-9][0-9]*)?$`, "u"));
    assert.ok(input.sites.some(site => site.targetPath === mapping.targetPath));
    for (const page of input.pages.filter(value => value.sourceUrl.includes(mapping.sourceTargetPath))) {
      assert.ok(page.targetPath.startsWith(`${mapping.targetPath}/`));
    }
  }
});

test("all lifecycle expressions bind plan and lifecycle digests", async () => {
  for (const [name, declared] of Object.entries(manifest.expressions)) {
    const expression = await readFile(new URL(`../run/expressions/${name}`, import.meta.url), "utf8");
    assert.equal(digest(expression), declared.sha256);
    assert.equal(Buffer.byteLength(expression), declared.bytes);
    assert.match(expression, new RegExp(manifest.planDigest, "u"));
    assert.match(expression, new RegExp(manifest.lifecycleDigest, "u"));
    assert.doesNotThrow(() => new Function(`return ${expression}`), `${name} must parse`);
  }
});

test("uses recoverable identity-bound native web readiness", async () => {
  const provision = await readFile(new URL("../run/expressions/02-provision.js", import.meta.url), "utf8");
  const readiness = await readFile(new URL("../run/expressions/03-readiness.js", import.meta.url), "utf8");
  assert.match(provision, /WebCreationInformation|UseSamePermissionsAsParentSite/u);
  assert.match(provision, /_ObjectIdentity_/u);
  assert.doesNotMatch(provision, /webinfos\/add/iu);
  assert.match(readiness, /OpenWebById/u);
  assert.match(readiness, /IsProvisioningComplete/u);
  assert.match(readiness, /ownershipFingerprint/u);
});

test("separates capability admission from native web readiness", () => {
  assert.ok(manifest.expressions["04-capability-readiness.js"]);
  assert.ok(manifest.expressions["05-native-create.js"]);
  assert.ok(manifest.expressions["06-fresh-readback.js"]);
});

test("does not treat a folder envelope with Exists=false as ready", async () => {
  const capability = await readFile(new URL("../run/expressions/04-capability-readiness.js", import.meta.url), "utf8");
  assert.match(capability, /probe\.body\?\.Exists!==true/u);
  assert.match(capability, /value\.body\?\.Exists===true/u);
});

test("archives the exact live producer instead of substituting the post-run builder", async () => {
  const archived = await readFile(new URL("../run/source/build-lifecycle-expressions.live.mjs", import.meta.url), "utf8");
  const postRun = await readFile(new URL("../scripts/build-lifecycle-expressions.mjs", import.meta.url), "utf8");
  assert.equal(digest(archived), manifest.producerRef.sha256);
  assert.equal(digest(archived), "ae5b8fc33d598352d6b998156d256a3fc5c9b7eca01bdc96632a104ed6000801");
  assert.equal(digest(postRun), "441ce088a3e2d4f5860412d8ce4d99e4c83f0ddc9e3890e8a843cff56fde19ca");
  assert.notEqual(digest(postRun), manifest.producerRef.sha256);
});

test("storage equality fails closed when either digest is unavailable", () => {
  const a = "a".repeat(64);
  const b = "b".repeat(64);
  assert.equal(classifyStorageEvidence({ expectedSha256: a, actualSha256: a }), "exact");
  assert.equal(classifyStorageEvidence({ expectedSha256: null, actualSha256: a }), "expected-unavailable");
  assert.equal(classifyStorageEvidence({ expectedSha256: a, actualSha256: null }), "actual-unavailable");
  assert.equal(classifyStorageEvidence({ expectedSha256: null, actualSha256: null }), "expected-and-actual-unavailable");
  assert.equal(classifyStorageEvidence({ expectedSha256: a, actualSha256: b }), "mismatch-deferred");
});

test("preserves the raw readback FAIL and classifies 0 exact / 11 expected-unavailable / 19 mismatch", () => {
  assert.equal(digest(readbackRaw), "7bca5b59a60398d26fce135e1c2ce2d30ae08fd1a9fe8d4810cc537a03ce3638");
  assert.equal(readback.verdict, "fail");
  assert.equal(readback.reasonCode, "FRESH_READBACK_COVERAGE_FAIL");
  const rows = rowsByStorageClassification(readback.pages);
  assert.deepEqual(rows.exact, []);
  assert.deepEqual(rows["expected-unavailable"], [2, 3, 8, 9, 10, 11, 12, 13, 14, 22, 23]);
  assert.deepEqual(rows["actual-unavailable"], []);
  assert.deepEqual(rows["expected-and-actual-unavailable"], []);
  assert.deepEqual(rows["mismatch-deferred"], [1, 4, 5, 6, 7, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37]);
});
