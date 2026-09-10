# CCD-143 shared target lifecycle adapter

This adapter remediates the Batch 1 target lifecycle gap without changing the
PnP framework core. It consumes the immutable CCD-109 page bundle and emits
fresh, phase-scoped native receipts for:

1. target admission;
2. top-down provision with bounded readiness polling;
3. capability/readiness fencing;
4. native page creation;
5. retained fresh readback, including list schema, content type and page/item identity;
6. ownership-guarded bottom-up cleanup; and
7. fresh post-cleanup absence verification.

The three known system-page conflicts (`row-00008`, `row-00011`, and
`row-00023`) are mapped at the
exact file level to `How To Use This Library-ccd35-<short-run-id>.aspx`. Any
other unexpected conflict fails admission closed.

Build and offline-test:

```text
node ccd-143/scripts/build-lifecycle-expressions.mjs
node --test ccd-143/test/lifecycle-contract.test.mjs
```

The default command remains the fixed 30-page contract. To build the admitted
CCD-147 `row-00003` claim as a page aggregate, use an isolated output directory:

```text
PAPERCLIP_RUN_ID=<run-id> \
CCD143_EXECUTION_CONFIG=ccd-143/config/ccd-147-row-00003.single-claim.json \
CCD143_OUT_DIR=<run-owned-output-directory> \
node ccd-143/scripts/build-lifecycle-expressions.mjs

node ccd-143/test/single-claim-contract.test.mjs
```

The single-claim configuration is fail-closed against the claim ID, row ID,
source URL/list/item/file identity and ETag, admitted plan digest, page-layout
values, PnP implementation commit, target profile and target origin. It emits
only the selected page's site, web, library and page dependencies. Generated
receipts add the claim/source/PnP/target-mapping/ownership/action lineage while
the default mode keeps `expectedPageCount: 30` and the historical 30-page
reason codes.

For a live single-claim run, point the runner at that isolated directory:

```text
CCD143_RUN_DIR=<run-owned-output-directory> \
node ccd-143/scripts/run-cdp-lifecycle.mjs
```

Do not run the live command until the generated producer has an immutable
repository commit. The runner rejects stale or cross-claim phase receipts.

The exact live producer used for run `fc772393-1529-40e9-bcb5-398f8be4aa08`
is archived at `run/source/build-lifecycle-expressions.live.mjs`. Its SHA-256
matches the producer declared by the receipts. The file under `scripts/` is a
post-run corrected builder and must not be substituted for that historical
producer. The nine actual execution expressions are retained under
`run/expressions/` and are verified byte-for-byte against `run/manifest.json`.

Live execution reuses the signed-in Windows Edge target session:

```text
node ccd-143/scripts/run-cdp-lifecycle.mjs
node ccd-143/scripts/verify-lifecycle.mjs
```
