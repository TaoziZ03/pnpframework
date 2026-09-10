#!/usr/bin/env python3

from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from generate_registry import (  # noqa: E402
    DISPOSITIONS,
    FAILURE_SEMANTICS,
    PLATFORM_BUILD,
    SOURCE_KINDS,
    VOLUME_COMPATIBILITY,
    canonical_utc_from_epoch,
)
from validate_registry import (  # noqa: E402
    apply_artifact_mutation,
    artifact_sha256,
    evaluate_fixture_suite,
    load_json,
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
        )
        failures = [row for row in receipts["results"] if not row["passed"]]
        self.assertEqual([], failures)
        self.assertEqual(receipts["caseCount"], receipts["passCount"])

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

    def test_git_commit_time_is_canonicalized_independent_of_client_spelling(self) -> None:
        self.assertEqual("2026-08-06T02:13:58Z", canonical_utc_from_epoch("1785982438"))
        self.assertRegex(
            self.authority["authorityCommitTime"],
            r"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$",
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)
