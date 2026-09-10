# Behavior interaction fixtures

`search-submit.fixture.v1.json` is the hermetic semantic fixture for the
CCD-172 safe Search Box submit canary. It is a runtime-verification assertion,
not a persisted page ingredient or materializer input.

The fixture keeps trigger and target identity separate from observation
locators. The Web Part instance and attached action IDs are canonical; CSS or
logical selectors are replay hints only. The action is bounded to one safe
text replacement and submit, with at most two attempts and a 15 second timeout
per attempt.

The fixture also carries explicit source-to-target ResultScript consumer
topology. It binds the source provider and dynamic-region identities to distinct
target Search Box, provider, and dynamic-region identities on one target page.
The target readback includes independently observed Search Box and provider
endpoints, their common page identity/version, the provider type, query group,
`UpdateAjaxNavigate`, reviewed source and target configuration digests,
admitted target/action/plan bindings, freshness window, operation/marker
references, and the append-only
`ccd347-search-inplace-cfa6f409-v1` lease. Missing, stale, wrongly mapped,
wrong-group, or already-cleaned provider evidence must produce
`DEPENDENT_RESULT_PROVIDER_MISSING` before the submit action is admitted.

The sealed source semantic projection is
`pnp-behavior-interaction-search-submit-semantic/v2`. Version 2 excludes target
mapping, readback, lease, and final-runtime timestamps from the source semantic
digest. This explicit version replaces the pre-integration v1 mixed snapshot;
fresh target retries must not require the authenticated source snapshot to be
resealed. Missing target operational evidence therefore closes M1/runtime
admission without revoking an otherwise valid M0 source assertion.

Search result content is not deterministic across user, permissions, index,
and time. A successful probe therefore proves the state transition and runtime
boundary, not a particular hit set. The lane contributor must preserve the
typed `pass`, `conditional`, `unsupported`, `unknown`, or `fail` disposition
and project it through the frozen shared maturity evidence validators. It must
not assign attained maturity.

The full authenticated source observations remain controlled CCD evidence.
This repository fixture contains no cookie, token, authorization header, raw
page HTML, or replayable credential.
