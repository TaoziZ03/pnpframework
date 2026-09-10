#!/usr/bin/env python3

from __future__ import annotations

import hashlib
import sqlite3
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from generate_registry import (  # noqa: E402
    ARTIFACT_VERIFICATION,
    CONSUMER_PRODUCT_ID,
    CONSUMER_PRODUCT_REF,
    CONSUMER_SOURCE_REF,
    DISPOSITIONS,
    FAILURE_SEMANTICS,
    PLATFORM_BUILD,
    SOURCE_KINDS,
    VOLUME_COMPATIBILITY,
    canonical_json_bytes,
    canonical_utc_from_epoch,
    normalize_sql_definition,
    sqlite_schema_manifest,
    sqlite_where_predicate,
)
from validate_registry import (  # noqa: E402
    apply_artifact_mutation,
    artifact_evidence_summary,
    artifact_sha256,
    evaluate_registry_request,
    evaluate_fixture_suite,
    load_json,
    load_fixture_artifact_evidence,
    schema_validation_errors,
    validate_authority,
    validate_profile,
    validate_registry,
)


class RegistryContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.registry = load_json(
            ROOT / "registry" / "spo-online-16.0.27606.12000.registry.json"
        )
        cls.authority = load_json(
            ROOT / "authority" / "spo-online-16.0.27606.12000.authority.json"
        )
        cls.profile = load_json(
            ROOT / "profile" / "spo-online-16.0.27606.12000.profile.json"
        )
        cls.schema_path = ROOT / "schema" / "aspx-platform-registry.schema.json"
        cls.schema = load_json(cls.schema_path)
        cls.profile_schema = load_json(
            ROOT / "schema" / "aspx-platform-registry-profile.schema.json"
        )
        cls.schema_hash = artifact_sha256(cls.schema_path)
        cls.fixtures = load_json(ROOT / "fixtures" / "contract-cases.json")
        cls.fixture_root = ROOT / "fixtures"

    def test_registry_authority_profile_and_schemas_are_valid(self) -> None:
        self.assertEqual([], schema_validation_errors(self.registry, self.schema))
        self.assertEqual([], schema_validation_errors(self.profile, self.profile_schema))
        self.assertEqual([], validate_authority(self.authority))
        self.assertEqual([], validate_registry(self.registry, self.authority))
        self.assertEqual(
            [],
            validate_profile(
                self.profile,
                self.registry,
                self.authority,
                self.schema_hash,
            ),
        )

    def test_registry_is_finite_and_exact_build_bound(self) -> None:
        self.assertEqual(len(self.registry["entries"]), self.registry["entryCount"])
        self.assertGreater(self.registry["entryCount"], 0)
        self.assertEqual(PLATFORM_BUILD, self.registry["platformBuildMin"])
        self.assertEqual(PLATFORM_BUILD, self.registry["platformBuildMax"])
        self.assertEqual(PLATFORM_BUILD, self.authority["platformBuildMin"])
        self.assertEqual(PLATFORM_BUILD, self.authority["platformBuildMax"])
        self.assertEqual(PLATFORM_BUILD, self.profile["platformBuild"])

    def test_wire_contract_is_closed_and_profile_pinned(self) -> None:
        compatibility = self.registry["consumerCompatibility"]
        self.assertEqual(SOURCE_KINDS, compatibility["sourceKinds"])
        self.assertEqual(set(DISPOSITIONS), set(compatibility["dispositions"]))
        self.assertEqual(VOLUME_COMPATIBILITY, compatibility["volumeCompatibility"])
        self.assertEqual(FAILURE_SEMANTICS, self.registry["failureSemantics"])
        self.assertEqual(self.schema_hash, self.profile["registrySchemaHash"])
        self.assertEqual(CONSUMER_PRODUCT_ID, self.profile["consumerProductId"])
        self.assertEqual(CONSUMER_SOURCE_REF, self.profile["consumerSourceRef"])
        self.assertEqual(CONSUMER_PRODUCT_REF, self.profile["consumerProductRef"])

    def test_self_consistent_rehashed_contract_mutations_fail_closed(self) -> None:
        for kind in [
            "widenBuildRangeAndRehash",
            "reduceSourceKindsAndRehash",
            "driftVolumeProfileAndRehash",
            "emptyFailureSemanticsAndRehash",
        ]:
            with self.subTest(kind=kind):
                registry, authority, _profile = apply_artifact_mutation(
                    self.registry,
                    self.authority,
                    self.profile,
                    {"kind": kind},
                )
                self.assertTrue(schema_validation_errors(registry, self.schema))
                self.assertTrue(validate_registry(registry, authority))

    def test_all_reader_shaped_contract_fixtures(self) -> None:
        receipts = evaluate_fixture_suite(
            self.fixtures,
            self.registry,
            self.authority,
            self.profile,
            self.schema,
            self.schema_hash,
            self.fixture_root,
        )
        failures = [row for row in receipts["results"] if not row["passed"]]
        self.assertEqual([], failures)
        self.assertEqual(receipts["caseCount"], receipts["passCount"])

    def test_actual_output_and_sqlite_fixture_evidence_is_bound(self) -> None:
        evidence, errors = load_fixture_artifact_evidence(
            self.fixtures["artifactBindings"], self.fixture_root
        )
        self.assertEqual([], errors)
        summary = artifact_evidence_summary(evidence)
        for key, descriptor in self.fixtures["artifactBindings"].items():
            self.assertEqual(descriptor["sha256"], summary[key]["sha256"])
            self.assertEqual(descriptor["length"], summary[key]["length"])
        self.assertEqual(
            ARTIFACT_VERIFICATION["physicalStoreSchemaHash"],
            summary["physicalStore"]["schemaManifestHash"],
        )
        self.assertEqual(
            ARTIFACT_VERIFICATION["referenceStoreSchemaHash"],
            summary["referenceStore"]["schemaManifestHash"],
        )

    def test_reader_fails_closed_without_actual_artifact_evidence(self) -> None:
        actual = evaluate_registry_request(
            self.registry,
            self.profile,
            self.fixtures["readerTemplate"],
            self.authority,
            self.schema,
            self.schema_hash,
        )
        self.assertEqual("Unknown", actual["verdict"])
        self.assertEqual("ARTIFACT_EVIDENCE_MISSING", actual["reasonCode"])

    def test_sql_sources_recreate_the_frozen_store_schema_manifests(self) -> None:
        cases = {
            "physical-store-v2.sql": ARTIFACT_VERIFICATION["physicalStoreSchemaHash"],
            "reference-store-v1.sql": ARTIFACT_VERIFICATION["referenceStoreSchemaHash"],
        }
        for sql_name, expected_hash in cases.items():
            with self.subTest(sql=sql_name):
                connection = sqlite3.connect(":memory:")
                try:
                    connection.executescript(
                        (self.fixture_root / "sql" / sql_name).read_text(encoding="utf-8")
                    )
                    actual_hash = hashlib.sha256(
                        canonical_json_bytes(sqlite_schema_manifest(connection))
                    ).hexdigest()
                finally:
                    connection.close()
                self.assertEqual(expected_hash, actual_hash)

    def test_sqlite_schema_manifest_v2_binds_predicates_and_generated_columns(self) -> None:
        source_sql = (self.fixture_root / "sql" / "physical-store-v2.sql").read_text(
            encoding="utf-8"
        )
        baseline = sqlite3.connect(":memory:")
        changed_predicate = sqlite3.connect(":memory:")
        generated_column = sqlite3.connect(":memory:")
        try:
            for connection in [baseline, changed_predicate, generated_column]:
                connection.executescript(source_sql)
            changed_predicate.execute("DROP INDEX UX_DiscoveryAttempts_Active")
            changed_predicate.execute(
                "CREATE UNIQUE INDEX UX_DiscoveryAttempts_Active "
                "ON DiscoveryAttempts(RunId, ScopeKey, SourceKind) WHERE Status='Finished'"
            )
            generated_column.execute(
                "ALTER TABLE DiscoveryInventory ADD COLUMN ReferenceDerived TEXT "
                "GENERATED ALWAYS AS (FileName) VIRTUAL"
            )

            def accepts_two_running_attempts(connection: sqlite3.Connection) -> bool:
                try:
                    for suffix in ["one", "two"]:
                        connection.execute(
                            "INSERT INTO DiscoveryAttempts "
                            "(AttemptId, RunId, ScopeKey, SourceKind, Status, StartedUtc) "
                            "VALUES (?, ?, ?, ?, ?, ?)",
                            (
                                "attempt-" + suffix,
                                "11111111-2222-3333-4444-555555555555",
                                "scope",
                                "RawListLibraryFiles",
                                "Running",
                                "2026-09-10T00:00:00Z",
                            ),
                        )
                except sqlite3.IntegrityError:
                    return False
                return True

            baseline_accepts_duplicate = accepts_two_running_attempts(baseline)
            changed_accepts_duplicate = accepts_two_running_attempts(changed_predicate)
            manifests = [
                sqlite_schema_manifest(connection)
                for connection in [baseline, changed_predicate, generated_column]
            ]
        finally:
            baseline.close()
            changed_predicate.close()
            generated_column.close()
        hashes = [
            hashlib.sha256(canonical_json_bytes(manifest)).hexdigest()
            for manifest in manifests
        ]
        self.assertEqual("sqlite-schema-manifest/v2", manifests[0]["algorithm"])
        self.assertFalse(baseline_accepts_duplicate)
        self.assertTrue(changed_accepts_duplicate)
        self.assertNotEqual(hashes[0], hashes[1])
        self.assertNotEqual(hashes[0], hashes[2])
        active_index = next(
            index
            for index in manifests[0]["indexes"]
            if index["name"] == "UX_DiscoveryAttempts_Active"
        )
        self.assertEqual("Status='Running'", active_index["wherePredicateSql"])
        generated_table = next(
            table
            for table in manifests[2]["tables"]
            if table["name"] == "DiscoveryInventory"
        )
        generated = next(
            column for column in generated_table["columns"] if column["name"] == "ReferenceDerived"
        )
        self.assertNotEqual(0, generated["hidden"])

    def test_sql_normalization_preserves_quoted_content(self) -> None:
        sql = "  CREATE  INDEX ix ON t(value)  WHERE value = 'A  B'  "
        self.assertEqual(
            "CREATE INDEX ix ON t(value) WHERE value = 'A  B'",
            normalize_sql_definition(sql),
        )
        self.assertEqual("value = 'A  B'", sqlite_where_predicate(sql))

    def test_fixture_coverage_closes_all_wire_enums(self) -> None:
        positive_observations = [
            case["observation"]
            for case in self.fixtures["positive"]
            if case["kind"] == "referenceObservation"
        ]
        self.assertEqual(
            set(SOURCE_KINDS),
            {row["sourceKind"] for row in positive_observations},
        )
        self.assertEqual(
            set(DISPOSITIONS),
            {row["disposition"] for row in positive_observations},
        )

    def test_assessment_adapter_conformance_is_version_bound(self) -> None:
        provenance = self.fixtures["fixtureProvenance"]
        self.assertEqual(
            "3012555317d5a8ee981b9e103206f3f0680333d8",
            provenance["assessmentConsumerSourceRef"],
        )
        self.assertEqual("aspx-acquisition-verdict/v1", provenance["consumerWireShape"])
        self.assertEqual(
            "aspx-platform-registry-reader-envelope/v2", provenance["readerShape"]
        )
        self.assertEqual(
            "physicalArtifactHash+physicalVolume.artifactHash",
            provenance["adapterMapping"]["physicalVolume.sha256"],
        )
        self.assertEqual(
            "referenceArtifactHash+referenceVolume.artifactHash",
            provenance["adapterMapping"]["referenceVolume.sha256"],
        )
        self.assertEqual("productRef", provenance["adapterMapping"]["productRef"])
        self.assertEqual(
            "physicalVolume.productRef",
            provenance["adapterMapping"]["physicalVolume.productRef"],
        )
        self.assertEqual(
            "referenceVolume.productRef",
            provenance["adapterMapping"]["referenceVolume.productRef"],
        )

    def test_git_commit_time_is_canonicalized_independent_of_client_spelling(self) -> None:
        self.assertEqual("2026-08-06T02:13:58Z", canonical_utc_from_epoch("1785982438"))
        self.assertRegex(
            self.authority["authorityCommitTime"],
            r"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$",
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)
