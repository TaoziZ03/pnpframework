# Native page runtime binding v1

`pnp-native-page-runtime-binding/v1` is the identity boundary between a native
page import and the runtime decision for that exact target object. The first
implemented profile is `profile.classic-wiki`.

The binding seals all of the following in one canonical digest:

- admitted runtime operation ID;
- source identity and source-version digests;
- admitted-plan and typed native import-receipt digests;
- target Site/Web IDs, Web URL, page path, File UniqueId, list item ID, version,
  and ETag;
- requested and final runtime URLs;
- the canonical runtime receipt schema, digest, and derived status;
- optional external-report and producer-build provenance receipt digests.

## Authority

`runtimeEvidenceAuthority=native-producer` is the only authority that may
produce a terminal runtime status and change acceptance. The validator reopens
every HTML/DOM/screenshot artifact through `IMigrationArtifactStore`, recomputes
length and SHA-256, checks the fresh browser context and no-store HTTP evidence,
and derives the status again from the requirement manifest.

`runtimeEvidenceAuthority=external-report` is supplemental only. Its
self-sealed digest and identity fields are retained, but even a report that
claims `Passed` leaves native `runtimeVerificationStatus=Pending` and
`acceptanceStatus=Pending`.

`runtimeEvidenceAuthority=none` requires a clean Pending shape without runtime
URLs or evidence digests.

## Fail-closed rules

The contract rejects unknown schema/profile/status values; stale source,
operation, plan, import or target identity; unsafe artifact locators; absent
artifact stores; missing, altered or un-reopened bytes; duplicate/unknown
runtime requirements; and status values not derived from canonical evidence.
The native producer command additionally rejects duplicate JSON keys before
deserialization.

An HTTP or semantic access-denied observation is valid terminal negative
evidence when its receipt is complete. It produces runtime `Failed` and
acceptance `Rejected`; it is not reported as copied, equal, or accepted.

## Producer build provenance

`pnp-producer-build-provenance-manifest/v1` and
`pnp-producer-build-provenance-receipt/v1` are seeded by this contract. The
default verifier result is exactly `UNVERIFIED`. A provenance manifest and
receipt must be supplied together and must bind the same binary digest.
