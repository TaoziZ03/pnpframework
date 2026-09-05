# Durable scale-run controller

> Status: Draft implementation
> Scope: reusable page-migration orchestration contracts; no SharePoint credentials or live run configuration

The scale layer executes a sealed page manifest through six fixed stages:

1. collect source evidence;
2. plan and sign page/shared actions;
3. reproduce the page on the target;
4. recapture the target through a fresh connection;
5. compare source and target packages;
6. run external browser acceptance.

It provides bounded scheduling, durable action checkpoints, retry metrics, and a
machine-readable loop summary. SharePoint and browser implementations remain
host-provided `IScaleRunStageExecutor` instances. The library contains no URL,
credential, token, cookie, process command, or environment lookup.

## Stable identity and nested gates

`ScaleRunManifest.RunKey` is a stable campaign key. It must remain unchanged
when a 10-page gate expands to 20 pages or when the same campaign is rerun in a
later loop. `LoopId`, page membership/order, queue capacity, concurrency, and
retry tuning belong to one run manifest and affect `ManifestDigest`; they do not
affect an existing page's stage action identity.

Each stage action binds:

- the stable campaign key, page key, source reference key, and target reference key;
- the stage executor's complete behavior contract digest;
- page family, support cohort, and execution cohort;
- content-addressed upstream artifacts and dependency action signatures;
- a target-slot identity derived from target reference and stage, independent
  from campaign so cross-campaign collisions remain visible;

Consequently, adding sibling pages or tuning concurrency does not invalidate a
completed page checkpoint. Changing source/target identity, executor behavior,
cohort semantics, or upstream evidence does invalidate it. Ordinal and load
bucket remain scheduling metadata and do not.

`TargetReferenceKey` must be derived by the host from the canonical target
authority, site fence, and object/path slot. A title or page-relative path alone
is not a sufficient cross-machine ownership key.

## Journals and checkpoints

Read-only work is not described as mutation. The controller writes two separate
append-only facts:

| File | Purpose |
| --- | --- |
| `contracts-v2/scale-stage-journal.jsonl` | Digest-chained start/completion records for every stage, including artifacts, request metrics, ingredient outcomes, and verification identity. |
| `scale-run-journal.jsonl` | Existing migration mutation intents, receipts, evidence references, and fresh verification for the target-writing Repro stage only. |

Both journals flush every appended record. A truncated final JSONL record is
retained as interrupted-write evidence and the next writer continues in a new
segment; malformed complete records or broken chains fail closed. Per-stage
`stage-checkpoint.json` files are atomic projections. A checkpoint is reusable
only when its digest, artifacts, and a matching completed stage-journal record
all validate. The append-only journal is the fact source.

The v2 stage journal, checkpoints, and stage artifacts live below the explicit
`contracts-v2` namespace. Pre-v2 files remain untouched and are not silently
interpreted as the new schema; a fresh/no-resume v2 run can coexist with them
while operators retain the old evidence for audit.

The controller never treats a journal as mutation authority. An intent without
a receipt, or a mutation attempt whose response was lost, forces a fresh target
probe. Exact provenance and exact semantic/target digests converge to
`AlreadySatisfied` or `OutcomeUnknownButConverged`; absence permits the sealed
action to run; drift requires RCA/replanning. Target recapture always runs again.

## Mutation admission

The default modes are `Disabled` and `Simulation`. A live-capable executor is
rejected in either mode. A live run requires both:

1. a sealed manifest whose mode is `ExplicitApproved`; and
2. a command-time confirmation exactly equal to that manifest's digest.

This double confirmation is necessary but does not replace domain-specific plan
approval, ownership checks, fresh probes, leases, or fencing. A fleet host must
still provide those capabilities. No live SharePoint mutation is performed by
the controller itself.

## Failure evidence

Every executor result, including a retry or failure, must retain at least one
content-addressed output/evidence artifact. Executor exceptions are reduced to a
sanitized evidence record containing only stage, attempt, action signature,
exception type, and time; exception messages and stacks are not persisted by the
library.

Page- or stage-level `AuthorizationBlocked` is accepted only when both are present:

- request telemetry containing literal HTTP status 401 or 403; and
- canonical `pnp-scale-http-authorization-evidence/v2` content bound to the
  action signature, target identity digest, operation name, and same status.

The evidence contract stores no URI, headers, response body, cookie, or token.
A 401/403 classification in summary text alone is rejected.

An executor may instead complete a stage with typed ingredient outcomes. Each
authorization-blocked ingredient binds its own ID and retained evidence SHA-256;
only hard-dependent ingredients may be skipped. Independent ingredients and
later stages continue. A page completing all stages with such a terminal is
`AuthorizationLimited`, not `Accepted`. A skipped dependency whose recorded
cause chain does not reach a literal-authorization ingredient remains RCA work.

## Scheduling and backpressure

Each stage has an explicit concurrency bound and a bounded queue. Before Repro,
the scheduler acquires an unverified-target slot; it releases that slot only
after browser acceptance or a terminal disposition. This bounds the
written-but-not-verified backlog independently from worker count. A first stage,
journal, artifact, or checkpoint fault cancels the linked pipeline and completes
all queues so blocked upstream producers cannot deadlock. The controller writes
a partial atomic summary before rethrowing an unexpected pipeline fault whenever
the summary path itself remains writable.

Per-stage summaries report wall time, request count, p50/p95 request duration,
retries, 429/503 counts, `Retry-After` wait, resume skips, shared receipt reuse,
and observed concurrency. `run-summary.json` is atomically replaced and includes
ingredient outcomes plus a compact `pnp-scale-loop-catalog-update/v2` projection. The projection is data
for a host to review and apply; the library does not edit a loop catalog.
Its gate may advance when every page is either accepted or terminally limited by
literal HTTP 401/403. Retryable, RCA, capability, policy, quarantine, and
unexpected outcomes continue to hold the gate.

## Host boundary

PnP Framework intentionally does not commit a process-adapter CLI. An
experimental host may map external commands to the stage executor contract, but
executable paths, arguments, working directories, timeouts, URLs, and credential
acquisition stay outside the library and repository. The process behavior digest
must include every non-secret behavior-affecting setting so adapter drift
invalidates checkpoints.

JSONL files provide single-run durability, not fleet-wide lease authority. A
central multi-machine scheduler must add a shared lease/ownership key and fencing
token around each mutating logical action; local claims or journal presence are
not sufficient to prevent two agents from writing the same shared ingredient.
