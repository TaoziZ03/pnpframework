import { execFileSync, spawnSync } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { assertReceiptBinding, resolveWorkspacePath } from "./single-claim-contract.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const workspace = path.resolve(here, "../..");
const runDir = process.env.CCD143_RUN_DIR ? resolveWorkspacePath(workspace, process.env.CCD143_RUN_DIR) : path.join(workspace, "ccd-143/run");
const expressionDir = path.join(runDir, "expressions");
const receiptDir = path.join(runDir, "receipts");
const psScript = path.join(workspace, "ccd35-edge-cdp-file.ps1");
const targetHost = "a830edad9050849cupcollect.sharepoint.com";
const targetUrlContains = "ccd143lifecycle=1";
const lifecycleInput = JSON.parse(await readFile(path.join(runDir, "lifecycle-input.json"), "utf8"));
const phases = [
  ["admission", "01-admission.js"],
  ["provision", "02-provision.js"],
  ["readiness", "03-readiness.js"],
  ["capability", "04-capability-readiness.js"],
  ["native-create", "05-native-create.js"],
  ["fresh-readback", "06-fresh-readback.js"]
];

await mkdir(receiptDir, { recursive: true });
const toWindows = value => execFileSync("wslpath", ["-w", value], { encoding: "utf8" }).trim();
const execute = async (phase, file) => {
  process.stdout.write(`phase:start:${phase}\n`);
  const result = spawnSync("pwsh.exe", [
    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
    "-File", toWindows(psScript), "-ExpressionPath", toWindows(path.join(expressionDir, file)),
    "-TargetHost", targetHost, "-TargetUrlContains", targetUrlContains
  ], { cwd: workspace, encoding: "utf8", maxBuffer: 64 * 1024 * 1024, timeout: 25 * 60 * 1000 });
  if (result.status !== 0) throw new Error(`cdp_phase_failed:${phase}:${result.status}:${String(result.stderr).slice(0, 1200)}`);
  const receipt = JSON.parse(result.stdout.trim());
  assertReceiptBinding(receipt, lifecycleInput, phase === "capability" ? "capability-readiness" : phase === "readiness" ? "native-web-readiness" : phase === "post-cleanup" ? "post-cleanup" : phase);
  await writeFile(path.join(receiptDir, `${phase}.json`), `${JSON.stringify(receipt, null, 2)}\n`);
  process.stdout.write(`phase:finish:${phase}:${receipt.verdict}:${receipt.reasonCode}\n`);
  return receipt;
};

const state = { schema: "ccd.shared-target-lifecycle-run-state/v1", startedAtUtc: new Date().toISOString(), phases: [] };
let markerMayExist = false;
let failure = null;
try {
  for (const [phase, file] of phases) {
    if (phase === "provision") markerMayExist = true;
    const receipt = await execute(phase, file);
    state.phases.push({ phase, verdict: receipt.verdict, reasonCode: receipt.reasonCode, receipt: `receipts/${phase}.json` });
    await writeFile(path.join(runDir, "run-state.json"), `${JSON.stringify(state, null, 2)}\n`);
    if (receipt.verdict !== "pass") throw new Error(`lifecycle_fence:${phase}:${receipt.reasonCode}`);
  }
} catch (error) {
  failure = error;
  state.failure = String(error?.message || error);
} finally {
  if (markerMayExist) {
    try {
      const cleanup = await execute("cleanup", "07-cleanup.js");
      state.phases.push({ phase: "cleanup", verdict: cleanup.verdict, reasonCode: cleanup.reasonCode, receipt: "receipts/cleanup.json" });
      const postCleanup = await execute("post-cleanup", "08-post-cleanup.js");
      state.phases.push({ phase: "post-cleanup", verdict: postCleanup.verdict, reasonCode: postCleanup.reasonCode, receipt: "receipts/post-cleanup.json" });
      if (cleanup.verdict !== "pass" || postCleanup.verdict !== "pass") failure ||= new Error("cleanup_not_terminal");
    } catch (error) {
      failure ||= error;
    }
  }
  state.finishedAtUtc = new Date().toISOString();
  state.verdict = failure ? "fail" : "pass";
  await writeFile(path.join(runDir, "run-state.json"), `${JSON.stringify(state, null, 2)}\n`);
}

if (failure) throw failure;
process.stdout.write(`lifecycle:pass:${JSON.parse(await readFile(path.join(runDir, "manifest.json"), "utf8")).lifecycleDigest}\n`);
