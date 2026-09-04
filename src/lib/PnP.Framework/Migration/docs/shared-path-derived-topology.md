# Shared path-derived Web topology

## Problem boundary

A page can retain an exact source Site ID, leaf Web ID, Site Collection path,
leaf Web path, and page path even when reading `ParentWeb` returns literal HTTP
401 or 403. That failure means the source ancestor Web metadata is unavailable.
It does not make an already reviewed target Site mapping or the exact relative
path ambiguous.

The migration model therefore separates two independent facts:

- `SourceWebFidelityIngredientPlan` retains the real source Site/Web identity and
  literal authorization evidence. It remains `AuthorizationBlocked`.
- one `TargetWebContainerIngredientPlan` is created for each exact relative path
  segment below the target Site Collection. Each container is independently
  inspected, planned, materialized, receipted, and verified.

For the Athena Web, the shared hierarchy is:

```text
Target Site Collection /teams/athena-pnp
└── target-web-container:/teams/athena-pnp/gkb
    └── target-web-container:/teams/athena-pnp/gkb/projects
        └── target-web-container:/teams/athena-pnp/gkb/projects/athenawiki
            └── Pages library
                ├── rollout ordinal 218
                └── rollout ordinal 350
```

The two pages reference the same shared plan/action/receipt digests. They do not
copy the three Web nodes into each page-local graph and do not create or verify
the hierarchy twice.

## Identity and evidence rules

`CapturedSourceWeb`, `TargetSiteRoot`, and `ExactRelativePath` are distinct
identity bases. A path-derived container never carries a fabricated Source
Web ID, parent ID, title, template, or configuration.

Target Web title/template/configuration are explicit target creation values:

- title is either an explicit reviewed override or deterministically derived
  from the target path segment;
- template, configuration, and language must come from the reviewed target
  provisioning policy;
- the plan records these as expected metadata differences because source Web
  metadata was not captured.

Canonical container IDs are case-insensitive normalized target paths. Plan,
container, target-analysis, action-plan, and receipt digests are separate. A
stale or unsupported nested schema fails validation before mutation.

## Path and collision policy

The source suffix is copied segment by segment. Planning rejects traversal,
encoded slash/backslash, empty segments, trailing-dot/space ambiguity, paths
outside the source Site boundary, and target paths outside the mapped target
Site boundary.

No suffix is added in the normal path. A suffix can be allocated only when a
fresh inventory has already classified the exact path as a foreign collision
and the reviewed policy explicitly selects `StableSuffix`. The changed node and
all descendants retain a reviewable collision reason.

## Target state and HTTP semantics

Each target container starts at `TargetInspectionRequired` and advances
independently:

| Observation | State/action |
| --- | --- |
| exact reusable Web | `Reuse` |
| missing path / HTTP 404 | `CreateMissing` |
| literal HTTP 401 or 403 | `AuthorizationBlocked` |
| 408, 409, 423, 429, or 5xx | `RetryableFailure` |
| incompatible object at exact path | `CollisionBlocked` |

Only literal wire 401/403 becomes authorization-blocked. Exception text,
redirects, CSOM payload messages, conflicts, locks, throttling, timeouts, and
server errors never receive that classification.

A blocked parent prevents its hard-required descendants from executing, but it
does not relabel those descendants as authorization failures. Source fidelity
is attached to a page by an optional `GovernedBy` edge. Page, List, Field, View,
Reference, and Web Part execution depends on the target leaf container instead,
which prevents a source-fidelity 403 from creating false downstream gaps.

## Shared execution and page admission

`SharedTopologyMaterializer` executes one sealed action plan through an injected
`ISharedTopologyTargetRuntime`. It writes intents and receipts through the
existing `IMigrationExecutionJournal`; it does not define a file-backed journal.
Every action is freshly inspected before execution and every container is read
again after execution.

The resulting `SharedTopologyMaterializationReceipt` contains actual target
Site/Web/parent IDs and one receipt per shared container. A page plan stores only
`SharedTopologyPageReference`. Page admission requires a fresh receipt covering
all referenced target containers, so multiple pages can consume one receipt
without rerunning topology mutations.

Legacy complete captured-source topology remains `pnp-topology-plan/v1` and
continues to use `WebMappingPlan`. Optional shared fields are omitted when null,
so legacy canonical JSON and digests do not change silently.
