# Behavior interaction fixtures

`search-submit.fixture.v1.json` is the hermetic semantic fixture for the
CCD-172 safe Search Box submit canary. It is a runtime-verification assertion,
not a persisted page ingredient or materializer input.

The fixture keeps trigger and target identity separate from observation
locators. The Web Part instance and attached action IDs are canonical; CSS or
logical selectors are replay hints only. The action is bounded to one safe
text replacement and submit, with at most two attempts and a 15 second timeout
per attempt.

Search result content is not deterministic across user, permissions, index,
and time. A successful probe therefore proves the state transition and runtime
boundary, not a particular hit set. The lane contributor must preserve the
typed `pass`, `conditional`, `unsupported`, `unknown`, or `fail` disposition
and project it through the frozen shared maturity evidence validators. It must
not assign attained maturity.

The full authenticated source observations remain controlled CCD evidence.
This repository fixture contains no cookie, token, authorization header, raw
page HTML, or replayable credential.
