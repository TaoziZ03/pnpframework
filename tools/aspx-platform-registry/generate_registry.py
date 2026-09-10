#!/usr/bin/env python3
"""Generate the CCD setup/virtual ASPX registry from frozen SPO.Core manifests.

The generator intentionally reads only a caller-selected SPO.Core Git ref. It
does not accept Assessment output, tenant observations, or CUPCollect paths as
input. The deployed destination rows in otools/deploy/*.xml are the finite
setup-artifact authority. FilesToRedirect.sts.xms is a second, bounded authority
for legacy LCID aliases handled by SPLayoutsMappedFile.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import subprocess
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable


SCHEMA_VERSION = "aspx-platform-registry/v1"
AUTHORITY_SCHEMA_VERSION = "aspx-platform-authority/v1"
REGISTRY_REVISION = "spo-online-16.0.27606.12000-r1"
PLATFORM_FAMILY = "SharePointOnline-16"
PLATFORM_BUILD = "16.0.27606.12000"
AUTHORITY_REF = "cee0ed61136e17c742c1cf98c9dea9446f10c564"
AUTHORITY_TAG = "release/16.0.27606.12000"
CONTRACT_REVIEW_REF = (
    "CCD-394#document-aspx-surface-applicability-denominator-v3"
    "@7d44f61d-919e-478a-ab8e-bb4c8f9b4b4a"
)
COMPATIBILITY_DECISION_REF = "CCD-411:approve_with_changes"
DEFAULT_INDEPENDENT_REVIEW_REF = "CCD-415:independent-review-pending"

DEPLOY_PATHSPEC = "otools/deploy/*.xml"
REDIRECT_MAP_PATH = "sts/template/sts/layouts/FilesToRedirect.sts.xms"
VIRTUAL_HANDLER_SOURCE_PATH = "sts/stsom/ApplicationRuntime/spvirtualpathprovider.cs"
VIRTUAL_FILE_SOURCE_PATH = "sts/stsom/ApplicationRuntime/spvirtualfile.cs"

DEST_RE = re.compile(r'dest="([^"]+)"', re.IGNORECASE)
LAYOUT_DEST_RE = re.compile(
    r"^FILES\\Program Files\\Common Files\\Microsoft Shared(?: Debug)?\\"
    r"Web Server Extensions\\16\\TEMPLATE\\LAYOUTS\\(.+\.aspx)$",
    re.IGNORECASE,
)
GREP_LINE_RE = re.compile(r"^[0-9a-f]{40}:(.*?):([0-9]+):(.*)$")


def canonical_json_bytes(value: Any) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def object_hash(value: dict[str, Any], hash_field: str) -> str:
    material = copy.deepcopy(value)
    material.pop(hash_field, None)
    return hashlib.sha256(canonical_json_bytes(material)).hexdigest()


def rule_hash(value: dict[str, Any]) -> str:
    material = copy.deepcopy(value)
    material.pop("applicabilityRuleHash", None)
    return hashlib.sha256(canonical_json_bytes(material)).hexdigest()


def run_git(git_executable: str, repo: str, args: Iterable[str]) -> str:
    command = [git_executable, "-C", repo, *args]
    completed = subprocess.run(
        command,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"git command failed ({completed.returncode}): {' '.join(command)}\n"
            f"{completed.stderr.strip()}"
        )
    return completed.stdout


def parse_tree_blobs(tree_output: str) -> dict[str, str]:
    blobs: dict[str, str] = {}
    for line in tree_output.splitlines():
        metadata, path = line.split("\t", 1)
        _mode, kind, object_id = metadata.split(" ", 2)
        if kind == "blob":
            blobs[path] = object_id
    return blobs


def extract_deploy_rows(grep_output: str) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for raw_line in grep_output.splitlines():
        match = GREP_LINE_RE.match(raw_line)
        if not match:
            raise ValueError(f"unexpected git grep line: {raw_line}")
        manifest_path, line_text, xml_text = match.groups()
        dest_match = DEST_RE.search(xml_text)
        if not dest_match:
            continue
        destination = dest_match.group(1)
        layout_match = LAYOUT_DEST_RE.match(destination)
        if not layout_match:
            continue
        relative_path = layout_match.group(1).replace("\\", "/")
        rows.append(
            {
                "manifestPath": manifest_path,
                "manifestLine": int(line_text),
                "destination": destination,
                "relativeLayoutPath": relative_path,
                "flavor": "Debug" if "Microsoft Shared Debug" in destination else "Ship",
            }
        )
    if not rows:
        raise ValueError("the frozen deploy authority returned no ASPX layout rows")
    return rows


def parse_redirect_map(xml_text: str) -> dict[str, dict[str, Any]]:
    aliases: dict[str, dict[str, Any]] = {}
    for attrs, file_name in re.findall(
        r"<file(?P<attrs>[^>]*)>(?P<name>[^<]+)</file>", xml_text, re.IGNORECASE
    ):
        name = file_name.strip().replace("\\", "/")
        key = name.casefold()
        if key in aliases:
            raise ValueError(f"duplicate redirect-map entry: {name}")
        aliases[key] = {
            "fileName": name,
            "serverTransferOnPost": bool(
                re.search(r'serverTransferOnPost\s*=\s*"true"', attrs, re.IGNORECASE)
            ),
        }
    if not aliases:
        raise ValueError("FilesToRedirect.sts.xms returned no alias entries")
    return aliases


def normalize_registry_key(path: str) -> str:
    """Normalize an explicit registry path; request URL parsing stays consumer-side."""

    if not isinstance(path, str) or not path.startswith("/"):
        raise ValueError("registry path must be an absolute server-relative path")
    if "?" in path or "#" in path or "\\" in path:
        raise ValueError("registry path cannot contain query, fragment, or backslash")
    if "%2f" in path.casefold() or "%5c" in path.casefold():
        raise ValueError("encoded separators are not allowed")
    segments = path.split("/")
    if any(segment in (".", "..") for segment in segments):
        raise ValueError("dot segments are not allowed")
    while "//" in path:
        path = path.replace("//", "/")
    folded = path.casefold()
    if folded.startswith("/_layouts/") and not folded.startswith("/_layouts/15/"):
        path = "/_layouts/15/" + path[len("/_layouts/") :]
    return path.casefold()


def reference_id(canonical_request_path: str) -> str:
    digest = hashlib.sha256(canonical_request_path.casefold().encode("utf-8")).hexdigest()
    return f"spo-layouts-{digest[:32]}"


def create_rule() -> dict[str, Any]:
    rule = {
        "applicabilityRuleId": "spo.setup.layouts.shipping-manifest.v1",
        "applicabilityRuleVersion": "1",
        "applicabilityRuleHash": "",
        "reviewRef": CONTRACT_REVIEW_REF,
        "definition": {
            "admitWhen": (
                "The frozen release shipping manifest contains a File destination under "
                "Web Server Extensions\\16\\TEMPLATE\\LAYOUTS ending in .aspx."
            ),
            "identityAxes": [
                "requestReferenceIdentity",
                "setupArtifactIdentity",
                "virtualHandlerIdentity",
                "ghostedPhysicalFileIdentity",
            ],
            "absenceRule": (
                "Absence is known only after a compatible registry volume validates. "
                "A runtime path missing from the registry is a counterexample and yields Unknown."
            ),
        },
    }
    rule["applicabilityRuleHash"] = rule_hash(rule)
    return rule


def create_virtual_mapping_rule() -> dict[str, Any]:
    rule = {
        "applicabilityRuleId": "spo.layouts.legacy-lcid-virtual-map.v1",
        "applicabilityRuleVersion": "1",
        "applicabilityRuleHash": "",
        "reviewRef": CONTRACT_REVIEW_REF,
        "definition": {
            "admitWhen": (
                "The frozen FilesToRedirect.sts.xms authority contains the ASPX name."
            ),
            "handler": "Microsoft.SharePoint.ApplicationRuntime.SPLayoutsMappedFile",
            "targetResolution": (
                "If the target setup artifact is absent from the same frozen shipping "
                "authority, retain the request identity as ReferenceUnavailable."
            ),
            "identityAxes": [
                "requestReferenceIdentity",
                "setupArtifactIdentity",
                "virtualHandlerIdentity",
                "ghostedPhysicalFileIdentity",
            ],
        },
    }
    rule["applicabilityRuleHash"] = rule_hash(rule)
    return rule


def create_consumer_compatibility() -> dict[str, Any]:
    return {
        "compatibilityDecisionRef": COMPATIBILITY_DECISION_REF,
        "sourceKinds": [
            "ListFormReference",
            "ListViewReference",
            "WebWelcomePageReference",
            "PlatformRegistryReference",
            "RuntimeRequestReference",
        ],
        "dispositions": {
            "ReferenceOnlyAvailable": {
                "assessment": "Emit a typed reference observation with no physical identity.",
                "pnpGraph": "Create a Reference node or edge; do not create PageArtifact bytes.",
                "repro": "ReuseTargetProvider only with target capability evidence; otherwise Delegate.",
                "compare": "Compare registry-bound request identity; payload equality is not implied.",
            },
            "ReferenceUnavailable": {
                "assessment": "Preserve unavailable evidence; do not synthesize an identity or empty payload.",
                "pnpGraph": "Keep an explicit gap edge and no physical node.",
                "repro": "Skip unsafe dependent writes; required dependencies remain Delegate/degraded.",
                "compare": "Denied/failed is Incomplete; unresolved registry identity is Unknown.",
            },
            "LinkedPhysicalGhosted": {
                "assessment": "Link to one verified physical inventory key and FileUniqueId.",
                "pnpGraph": "Reference the existing physical node exactly once.",
                "repro": "Plan one physical action regardless of reference count.",
                "compare": "Compare physical content once plus origin/registry reference semantics.",
            },
            "LinkedPhysicalCustomized": {
                "assessment": "Link to one verified customized physical inventory key and FileUniqueId.",
                "pnpGraph": "Reference the existing customized physical node exactly once.",
                "repro": "Plan one physical action and preserve customized origin.",
                "compare": "Compare physical content once plus customized-origin reference semantics.",
            },
            "VirtualHandler": {
                "assessment": "Emit a typed runtime dependency with no physical identity.",
                "pnpGraph": "Create a runtime/reference dependency, not a PageArtifact.",
                "repro": "Never upload; require target capability evidence or Delegate.",
                "compare": "Compare handler/path/registry/build; incompatible binding is Unknown.",
            },
            "NonAspx": {
                "assessment": "Retain the source observation as a negative ASPX classification.",
                "pnpGraph": "Do not create an ASPX PageArtifact from this observation.",
                "repro": "No ASPX write branch.",
                "compare": "Exclude from ASPX equality while retaining evidence.",
            },
            "Unknown": {
                "assessment": "Preserve raw evidence and fail closed.",
                "pnpGraph": "Do not guess a physical or virtual node kind.",
                "repro": "Do not execute an unsafe dependent write.",
                "compare": "Aggregate remains Unknown.",
            },
        },
        "volumeCompatibility": {
            "physicalVolume": "aspx-discovery-output/v2",
            "referenceVolume": "aspx-reference-output/v1",
            "aggregateVolume": "aspx-acquisition-verdict/v1",
            "referenceStore": "aspx-reference-sqlite/v1",
            "directMixedVolume": "aspx-discovery-output/v3",
        },
    }


def build_authority(
    rows: list[dict[str, Any]],
    manifest_blobs: dict[str, str],
    redirect_blob: str,
    redirect_map: dict[str, dict[str, Any]],
    commit_time: str,
) -> dict[str, Any]:
    used_manifests = sorted({row["manifestPath"] for row in rows})
    missing_blobs = [path for path in used_manifests if path not in manifest_blobs]
    if missing_blobs:
        raise ValueError(f"missing manifest blob IDs: {missing_blobs}")
    enriched_rows = []
    for row in rows:
        enriched_rows.append(
            {
                **row,
                "manifestBlobId": manifest_blobs[row["manifestPath"]],
            }
        )
    authority = {
        "authoritySchemaVersion": AUTHORITY_SCHEMA_VERSION,
        "authorityArtifactHash": "",
        "authorityKind": "SPOCoreReleaseShippingManifest",
        "authoritySourceRef": AUTHORITY_REF,
        "authoritySourceTag": AUTHORITY_TAG,
        "authorityCommitTime": commit_time,
        "platformFamily": PLATFORM_FAMILY,
        "platformBuildMin": PLATFORM_BUILD,
        "platformBuildMax": PLATFORM_BUILD,
        "extractionAlgorithm": "ccd-spocore-layouts-deploy-manifest/v1",
        "independenceBoundary": {
            "allowedInputs": [
                "SPO.Core otools/deploy/*.xml at authoritySourceRef",
                f"SPO.Core {REDIRECT_MAP_PATH} at authoritySourceRef",
                f"SPO.Core {VIRTUAL_HANDLER_SOURCE_PATH} at authoritySourceRef",
                f"SPO.Core {VIRTUAL_FILE_SOURCE_PATH} at authoritySourceRef",
            ],
            "forbiddenInputs": [
                "Assessment provider output",
                "Assessment scanner-observed paths",
                "CUPCollect scan output",
                "tenant runtime enumeration",
            ],
        },
        "sourceArtifacts": {
            "shippingManifests": [
                {
                    "path": path,
                    "blobId": manifest_blobs[path],
                    "matchedRowCount": sum(1 for row in rows if row["manifestPath"] == path),
                }
                for path in used_manifests
            ],
            "legacyLcidRedirectMap": {
                "path": REDIRECT_MAP_PATH,
                "blobId": redirect_blob,
                "entryCount": len(redirect_map),
            },
            "virtualHandlerSources": [
                VIRTUAL_HANDLER_SOURCE_PATH,
                VIRTUAL_FILE_SOURCE_PATH,
            ],
        },
        "deployRows": enriched_rows,
        "legacyLcidRedirects": [redirect_map[key] for key in sorted(redirect_map)],
    }
    authority["authorityArtifactHash"] = object_hash(authority, "authorityArtifactHash")
    return authority


def build_registry(
    authority: dict[str, Any],
    independent_review_ref: str,
) -> dict[str, Any]:
    rows = authority["deployRows"]
    redirect_map = {
        row["fileName"].casefold(): row for row in authority["legacyLcidRedirects"]
    }
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        grouped[row["relativeLayoutPath"].casefold()].append(row)

    rule = create_rule()
    virtual_mapping_rule = create_virtual_mapping_rule()
    entries: list[dict[str, Any]] = []
    matched_redirect_keys: set[str] = set()
    for normalized_relative in sorted(grouped):
        evidence_rows = sorted(
            grouped[normalized_relative],
            key=lambda row: (
                0 if row["flavor"] == "Ship" else 1,
                row["manifestPath"],
                row["manifestLine"],
                row["destination"],
            ),
        )
        relative_path = evidence_rows[0]["relativeLayoutPath"]
        canonical_path = f"/_layouts/15/{relative_path}"
        versionless_alias = f"/_layouts/{relative_path}"
        root_file_key = relative_path.casefold() if "/" not in relative_path else None
        redirect = redirect_map.get(root_file_key or "")
        alias_patterns = []
        virtual_handler_identity = None
        if redirect:
            matched_redirect_keys.add(root_file_key or "")
            alias_patterns.append(f"/_layouts/{{lcid}}/{redirect['fileName']}")
            virtual_handler_identity = {
                "handlerType": "Microsoft.SharePoint.ApplicationRuntime.SPLayoutsMappedFile",
                "sourcePath": VIRTUAL_HANDLER_SOURCE_PATH,
                "authorityMapPath": REDIRECT_MAP_PATH,
                "serverTransferOnPost": redirect["serverTransferOnPost"],
            }

        distinct_sources = []
        seen_source_keys: set[tuple[str, str]] = set()
        for evidence in evidence_rows:
            source_key = (evidence["manifestPath"], evidence["manifestBlobId"])
            if source_key in seen_source_keys:
                continue
            seen_source_keys.add(source_key)
            distinct_sources.append(
                {
                    "manifestPath": evidence["manifestPath"],
                    "manifestBlobId": evidence["manifestBlobId"],
                }
            )

        entry = {
            "referenceId": reference_id(canonical_path),
            "surfaceKind": "SetupLayoutApplicationPage",
            "canonicalRequestPath": canonical_path,
            "aliases": [versionless_alias],
            "aliasPatterns": alias_patterns,
            "handlerOrArtifactType": "SetupArtifact.AspxApplicationPage",
            "applicabilityRuleId": rule["applicabilityRuleId"],
            "applicabilityRuleHash": rule["applicabilityRuleHash"],
            "expectedAvailability": "AvailableForExactPlatformBuild",
            "downstreamDisposition": "ReferenceOnlyAvailable",
            "identityAxes": {
                "requestReferenceIdentity": {
                    "canonicalRequestPath": canonical_path,
                    "aliases": [versionless_alias],
                    "aliasPatterns": alias_patterns,
                },
                "setupArtifactIdentity": {
                    "relativeLayoutPath": relative_path,
                    "shippingManifestEvidence": distinct_sources,
                },
                "virtualHandlerIdentity": virtual_handler_identity,
                "ghostedPhysicalFileIdentity": None,
            },
            "physicalIdentity": {
                "storageIdentity": "NoPhysicalFile",
                "fileUniqueId": None,
                "physicalLocator": None,
            },
            "contentOrigin": "SetupArtifact",
        }
        entries.append(entry)

    redirect_map_blob = authority["sourceArtifacts"]["legacyLcidRedirectMap"]["blobId"]
    for redirect_key in sorted(set(redirect_map) - matched_redirect_keys):
        redirect = redirect_map[redirect_key]
        relative_path = redirect["fileName"]
        canonical_path = f"/_layouts/15/{relative_path}"
        versionless_alias = f"/_layouts/{relative_path}"
        alias_pattern = f"/_layouts/{{lcid}}/{relative_path}"
        entries.append(
            {
                "referenceId": reference_id(canonical_path),
                "surfaceKind": "VirtualMappedRequest",
                "canonicalRequestPath": canonical_path,
                "aliases": [versionless_alias],
                "aliasPatterns": [alias_pattern],
                "handlerOrArtifactType": "VirtualHandler.SPLayoutsMappedFile",
                "applicabilityRuleId": virtual_mapping_rule["applicabilityRuleId"],
                "applicabilityRuleHash": virtual_mapping_rule["applicabilityRuleHash"],
                "expectedAvailability": "ReferenceTargetAbsentAtFrozenBuild",
                "downstreamDisposition": "ReferenceUnavailable",
                "identityAxes": {
                    "requestReferenceIdentity": {
                        "canonicalRequestPath": canonical_path,
                        "aliases": [versionless_alias],
                        "aliasPatterns": [alias_pattern],
                    },
                    "setupArtifactIdentity": None,
                    "virtualHandlerIdentity": {
                        "handlerType": (
                            "Microsoft.SharePoint.ApplicationRuntime.SPLayoutsMappedFile"
                        ),
                        "sourcePath": VIRTUAL_HANDLER_SOURCE_PATH,
                        "authorityMapPath": REDIRECT_MAP_PATH,
                        "authorityMapBlobId": redirect_map_blob,
                        "serverTransferOnPost": redirect["serverTransferOnPost"],
                        "mappedTargetState": "AbsentFromFrozenShippingManifest",
                    },
                    "ghostedPhysicalFileIdentity": None,
                },
                "physicalIdentity": {
                    "storageIdentity": "NoPhysicalFile",
                    "fileUniqueId": None,
                    "physicalLocator": None,
                },
                "contentOrigin": "VirtualHandler",
            }
        )

    entries.sort(key=lambda entry: entry["canonicalRequestPath"].casefold())

    registry = {
        "$schema": "../schema/aspx-platform-registry.schema.json",
        "registrySchemaVersion": SCHEMA_VERSION,
        "registryRevision": REGISTRY_REVISION,
        "registryHash": "",
        "authorityKind": authority["authorityKind"],
        "authoritySourceRef": AUTHORITY_REF,
        "authoritySourceTag": AUTHORITY_TAG,
        "authorityArtifactHash": authority["authorityArtifactHash"],
        "authorityArtifactPath": (
            "authority/spo-online-16.0.27606.12000.authority.json"
        ),
        "reviewRef": independent_review_ref,
        "contractReviewRef": CONTRACT_REVIEW_REF,
        "compatibilityDecisionRef": COMPATIBILITY_DECISION_REF,
        "generatedAtUtc": authority["authorityCommitTime"],
        "platformFamily": PLATFORM_FAMILY,
        "platformBuildMin": PLATFORM_BUILD,
        "platformBuildMax": PLATFORM_BUILD,
        "normalization": {
            "algorithmId": "sharepoint-layout-request-path/v1",
            "caseSensitivity": "OrdinalIgnoreCase",
            "queryAndFragment": "RemoveBeforeLookupButPreserveInRawLocator",
            "siteQualifiedPrefix": "StripOnlyThroughTheFirstExact_LayoutsSegment",
            "versionlessLayouts": "Map /_layouts/x to /_layouts/15/x",
            "legacyLcidAlias": (
                "Map /_layouts/{decimal-lcid}/x only when x is present in "
                "FilesToRedirect.sts.xms"
            ),
            "encodedSeparators": "Reject",
            "dotSegments": "Reject",
            "collisionRule": (
                "If any normalized explicit path or expanded reviewed alias maps to more "
                "than one referenceId, reject the registry and return Unknown."
            ),
        },
        "failureSemantics": {
            "unknownBuild": "Unknown",
            "missingRegistryVolume": "Unknown",
            "sameRevisionChangedHash": "Unknown",
            "aliasCollision": "Unknown",
            "runtimePathAbsent": "Unknown",
            "unknownSourceKindOrDisposition": "Unknown",
            "bindingMismatch": "Unknown",
        },
        "applicabilityRules": [rule, virtual_mapping_rule],
        "consumerCompatibility": create_consumer_compatibility(),
        "entryCount": len(entries),
        "entries": entries,
    }
    validate_explicit_alias_collisions(registry)
    registry["registryHash"] = object_hash(registry, "registryHash")
    return registry


def validate_explicit_alias_collisions(registry: dict[str, Any]) -> None:
    owners: dict[str, str] = {}
    for entry in registry["entries"]:
        for path in [entry["canonicalRequestPath"], *entry["aliases"]]:
            key = normalize_registry_key(path)
            previous = owners.get(key)
            if previous is not None and previous != entry["referenceId"]:
                raise ValueError(
                    f"alias normalization collision: {path} maps to {previous} and "
                    f"{entry['referenceId']}"
                )
            owners[key] = entry["referenceId"]


def collect_source(
    git_executable: str,
    repo: str,
) -> tuple[list[dict[str, Any]], dict[str, str], str, dict[str, dict[str, Any]], str]:
    resolved = run_git(git_executable, repo, ["rev-parse", AUTHORITY_REF]).strip()
    if resolved != AUTHORITY_REF:
        raise ValueError(f"authority ref resolved to unexpected object: {resolved}")
    tag_resolved = run_git(git_executable, repo, ["rev-parse", f"{AUTHORITY_TAG}^{{}}"] ).strip()
    if tag_resolved != AUTHORITY_REF:
        raise ValueError(f"authority tag does not resolve to authority ref: {tag_resolved}")
    commit_time = run_git(
        git_executable, repo, ["show", "-s", "--format=%cI", AUTHORITY_REF]
    ).strip()
    grep_output = run_git(
        git_executable,
        repo,
        [
            "grep",
            "-n",
            "-i",
            "-E",
            r"TEMPLATE\\LAYOUTS.*\.aspx",
            AUTHORITY_REF,
            "--",
            DEPLOY_PATHSPEC,
        ],
    )
    rows = extract_deploy_rows(grep_output)
    tree_output = run_git(
        git_executable,
        repo,
        ["ls-tree", "-r", AUTHORITY_REF, "--", "otools/deploy", REDIRECT_MAP_PATH],
    )
    blobs = parse_tree_blobs(tree_output)
    redirect_blob = blobs.get(REDIRECT_MAP_PATH)
    if not redirect_blob:
        raise ValueError("redirect-map blob ID is missing from the frozen tree")
    redirect_xml = run_git(
        git_executable, repo, ["show", f"{AUTHORITY_REF}:{REDIRECT_MAP_PATH}"]
    )
    redirect_map = parse_redirect_map(redirect_xml)
    return rows, blobs, redirect_blob, redirect_map, commit_time


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--spocore-repo", required=True)
    parser.add_argument("--git-executable", default="git")
    parser.add_argument(
        "--output-root",
        type=Path,
        default=Path(__file__).resolve().parent,
    )
    parser.add_argument(
        "--independent-review-ref",
        default=DEFAULT_INDEPENDENT_REVIEW_REF,
    )
    args = parser.parse_args()

    rows, blobs, redirect_blob, redirect_map, commit_time = collect_source(
        args.git_executable, args.spocore_repo
    )
    authority = build_authority(rows, blobs, redirect_blob, redirect_map, commit_time)
    registry = build_registry(authority, args.independent_review_ref)

    write_json(
        args.output_root / "authority" / "spo-online-16.0.27606.12000.authority.json",
        authority,
    )
    write_json(
        args.output_root / "registry" / "spo-online-16.0.27606.12000.registry.json",
        registry,
    )
    print(
        json.dumps(
            {
                "authoritySourceRef": AUTHORITY_REF,
                "authorityArtifactHash": authority["authorityArtifactHash"],
                "registryRevision": registry["registryRevision"],
                "registryHash": registry["registryHash"],
                "entryCount": registry["entryCount"],
                "platformBuildMin": registry["platformBuildMin"],
                "platformBuildMax": registry["platformBuildMax"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
