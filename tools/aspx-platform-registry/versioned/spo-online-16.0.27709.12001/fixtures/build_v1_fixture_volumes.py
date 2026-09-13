#!/usr/bin/env python3
"""Rebind the deterministic legacy Assessment fixture to the exact CCD-834 release."""

from __future__ import annotations

import json
import sqlite3
import sys
from pathlib import Path


FIXTURES = Path(__file__).resolve().parent
REGISTRY_ROOT = FIXTURES.parents[2]
sys.path.insert(0, str(REGISTRY_ROOT))

from generate_registry import discovery_hash  # noqa: E402


RUN_ID = "11111111-2222-3333-4444-555555555555"
BUILD = "16.0.27709.12001"
REGISTRY_REVISION = "spo-online-16.0.27709.12001-r1"
REGISTRY_HASH = "3138b6d0b1e1af1e170801939d2c1e00793e61cb17f53c6f7d01e61cf3507830"


def json_bytes(value: object) -> bytes:
    return (
        json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
    ).encode("utf-8")


def main() -> None:
    store_path = FIXTURES / "volumes" / "reference-store-v1.sqlite"
    connection = sqlite3.connect(":memory:")
    try:
        connection.deserialize(store_path.read_bytes())
        row = connection.execute(
            "SELECT ManifestJson FROM ReferenceRuns WHERE RunId=?", (RUN_ID,)
        ).fetchone()
        if row is None:
            raise RuntimeError("legacy reference fixture has no run manifest")
        manifest = json.loads(row[0])
        manifest.update(
            platformBuildRef=BUILD,
            registryRevision=REGISTRY_REVISION,
            registryHash=REGISTRY_HASH,
        )
        manifest_json = json.dumps(manifest, separators=(",", ":"))
        manifest_hash = discovery_hash(manifest_json)
        connection.execute(
            "UPDATE ReferenceRuns SET ManifestJson=?, ManifestHash=? WHERE RunId=?",
            (manifest_json, manifest_hash, RUN_ID),
        )
        connection.commit()
        store_path.write_bytes(connection.serialize())
    finally:
        connection.close()

    output_path = FIXTURES / "volumes" / "reference-output-v1.json"
    output = json.loads(output_path.read_text(encoding="utf-8"))
    output["manifestHash"] = manifest_hash
    output_path.write_bytes(json_bytes(output))


if __name__ == "__main__":
    main()
