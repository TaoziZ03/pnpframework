# CCD-103 live-chain adapters

These adapters are intentionally split into read-only admission and apply stages.

- `source-version-recheck-az.ps1` uses the existing Azure CLI delegated MSIT session and performs only three SharePoint metadata `GET` requests. It never prints the access token.
- `source-version-recheck.js` is the Edge/CDP read-only fallback. It performs only `HEAD` requests.
- `source-page-version-signal.js` reads version-like browser navigation metadata from an already authenticated source page.
- `target-admission-read.js` checks the exact CUPCollect target root and the three source-relative top-level paths without mutation.

Target mutation must not start until source recheck returns authenticated successful evidence and a superseding admitted plan has been frozen. The admitted plan must bind the clean implementation ref, current source-version identity/digest, exact target identity, and unique mutation/readback/runtime/cleanup operation IDs. Every live receipt is then serialized as native `PublishingPageImportReceipt v5` / `RuntimeVerificationReceipt v1` and independently deserialized before compare admission.

All `microsoft*.sharepoint.com` sites remain read-only. The only target origin is `https://a830edad9050849cupcollect.sharepoint.com/`.
