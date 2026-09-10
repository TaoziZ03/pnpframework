# CCD-255 single-claim lifecycle implementation progress

## Verdict

Implementation and hermetic verification: **pass**.

CCD-202 environment edge: **resolved and consumed**. The canonical IT receipt
proved the managed Git launcher, author identity and bounded push path. Because
the shared project `.git` metadata is read-only inside this sandbox, the exact
CCD-147 commit was copied through the local object database into a run-owned,
writable clone; the original dirty worktree was not changed.

Live CUPCollect execution is intentionally deferred until this adapter revision
has an immutable commit. The issue comment and live receipts bind that exact
commit; this in-tree report does not predict its SHA.

## Implemented scope

- Added `ccd143.single-claim-execution/v1` with the fixed CCD-147 claim,
  `row-00003`, source list/item/file/ETag, PnP commit
  `5a9f634da422a92b2493967e32dc74edacac9777`, admitted plan digest and target
  profile.
- Validates the claim ledger and CCD-147 code-evidence receipt before emitting
  expressions. Wrong row, claim, plan, source version, ingredient or PnP
  implementation evidence fails closed.
- Projects only the selected page aggregate: one site, one child Web, one Wiki
  library and one page. Default execution remains 30 pages with its historical
  30-page reason codes.
- Single-claim phase receipts bind run/operation/action IDs, claim/source
  version, PnP commit/branch, admitted plan digest, producer digest, target
  mapping digest, target profile and ownership marker.
- Capability admission records target Feature inventory, BaseTemplate `119`,
  Wiki Page content-type lineage, absent `PublishingPageLayout`, native
  `AddTemplateFile.WikiPage` mode and absent target page path before page
  creation.
- Fresh readback records file, list item, list schema, content types, per-value
  `page.layout` evidence and resolved `runtime.wiki`, including statuses,
  request IDs and timestamps.
- Cleanup now requires the marker plus matching target path, file UniqueId,
  item ID, item UniqueId and parent list ID. Post-cleanup separately probes the
  page path, site path and marker for absence.
- The runner accepts an isolated `CCD143_RUN_DIR` and rejects stale or
  cross-claim receipts before persisting them.

## Verification

Commands executed (offline only):

```text
node ccd-143/test/lifecycle-contract.test.mjs
node ccd-143/test/single-claim-contract.test.mjs
node --check ccd-143/scripts/build-lifecycle-expressions.mjs
node --check ccd-143/scripts/run-cdp-lifecycle.mjs
node --check ccd-143/scripts/single-claim-contract.mjs
```

Results: existing lifecycle contract `10/10` pass; new single-claim contract
`7/7` pass; all syntax checks pass. The tests cover single-row dependency
projection, wrong row/claim/plan/PnP binding, stale receipt, partial readback,
cleanup identity mismatch and default 30-page preservation.

Deterministic hermetic build:

```text
PAPERCLIP_RUN_ID=ccd255-hermetic-final \
CCD143_EXECUTION_CONFIG=ccd-143/config/ccd-147-row-00003.single-claim.json \
CCD143_OUT_DIR=$PAPERCLIP_RUN_SCRATCH_DIR/ccd255-final-hermetic \
node ccd-143/scripts/build-lifecycle-expressions.mjs
```

- page/dependency counts: `1 / 1 site / 1 Web / 1 library`
- plan digest: `8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f`
- target mapping digest: `f2085c11736671201f2bc0b479660f53b185e14555a191cb4cdd7a8bdb4a2c9b`
- lifecycle digest: `ce9ba70a60ca1140b3e710fb0f22fc596c72e82abf1fbd4a9b341d9b4a849adc`
- builder SHA-256: `441ce088a3e2d4f5860412d8ce4d99e4c83f0ddc9e3890e8a843cff56fde19ca`
- generated manifest SHA-256: `57bb33a86695d922f5a6d6e4223e0791203e06ad69e1a8b6589646e4d7847a`
- generated lifecycle input SHA-256: `aab3cac45fd00a7b0ebf7044d64592194ec6f11f167bb6d8f1fdc7d601d47382`

## Evidence classes and remaining uncertainty

- Claim/source/PnP inputs: immutable local evidence from CCD-109 and CCD-147.
- Implementation: current workspace files, compared against the CCD-143 v3
  bundle baseline.
- Verification: this-run hermetic build and Node tests; no network or tenant
  execution.
- Unverified at this report revision: same-commit target admission/create/
  readback/runtime/cleanup receipts. They are the next phase after the immutable
  adapter commit.

## KB workflow

Queried scenarios for `CCD-143 lifecycle single row claim CUPCollect native
AddTemplateFile WikiPage readiness cleanup receipt` and read:

- `private/personal/classic-page-repro/integrate-and-verify-page-runtime.md`
- `spo/classic-page/reproduction/create-target-subweb-with-recoverable-native-readiness.md`
- `spo/classic-page/reproduction/implement-cross-family-page-content-reproduction.md`

The implementation follows those existing contracts. No conflicting or new
verified reusable product fact was found, so no KB contribution was made.

## Next action

Commit the listed CCD-143 and CCD-255 files on top of PnP commit
`5a9f634da422a92b2493967e32dc74edacac9777`, rebuild from that exact commit,
run the scoped target lifecycle, and publish non-secret receipts.

## 2026-09-10 heartbeat revalidation

Run `1f821c1b-d5f2-4f30-a051-130437d9cb07` revalidated the implementation
without source or target access:

- Existing lifecycle tests: `10/10` pass.
- Single-claim contract tests: `7/7` pass.
- Syntax checks for the builder, runner and single-claim contract: `3/3` pass.
- Fresh isolated build: `1 site / 1 Web / 1 Wiki library / 1 page`, admitted
  plan digest `8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f`,
  target mapping digest
  `f2085c11736671201f2bc0b479660f53b185e14555a191cb4cdd7a8bdb4a2c9b`,
  producer SHA-256
  `441ce088a3e2d4f5860412d8ce4d99e4c83f0ddc9e3890e8a843cff56fde19ca`.
- The root `.git` remains a read-only empty `tmpfs` mount and
  `PAPERCLIP_GITHUB_OPERATION_ACTIVE` remains empty. `/usr/bin/git status`
  still returns `fatal: not a git repository (or any of the parent
  directories): .git`.
- Fresh Paperclip read shows CCD-202 is active with the Toolchain & Environment
  IT Engineer and still owns exact-consumer worktree/provider acceptance.

At that heartbeat, the implementation verdict remained pass while the immutable
adapter commit and same-commit CUPCollect receipts were blocked by CCD-202. No
live target lifecycle was started and no source/target mutation occurred in
that earlier run. The next section records the later resolution of that edge.

## 2026-09-10 CCD-202 consumption and exact-base verification

Run `8d6a662a-c13a-42b7-baf7-368e39e3fdc9` consumed the resolved CCD-202
environment edge and created an isolated writable clone at the exact CCD-147
PnP commit `5a9f634da422a92b2493967e32dc74edacac9777` (tree
`450a5eb3e59cc6215d8460373eb87258622da6cd`). The original shared worktree and
its unrelated changes were not modified.

- Existing lifecycle tests: `10/10` pass.
- Single-claim tests: `7/7` pass.
- Syntax checks: `3/3` pass.
- Fresh build: `1 site / 1 Web / 1 Wiki library / 1 page`.
- Admitted plan digest:
  `8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f`.
- Target mapping digest:
  `f2085c11736671201f2bc0b479660f53b185e14555a191cb4cdd7a8bdb4a2c9b`.
- Producer SHA-256:
  `441ce088a3e2d4f5860412d8ce4d99e4c83f0ddc9e3890e8a843cff56fde19ca`.

This verification made zero source/target requests and mutations. CCD-202 is no
longer a blocker; target evidence remains a separate product/runtime phase.
