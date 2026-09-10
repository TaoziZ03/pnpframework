#!/usr/bin/env python3

from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from validate_registry import (  # noqa: E402
    apply_fixture_mutation,
    evaluate_registry_request,
    load_json,
    validate_reference_observation,
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
        cls.fixtures = load_json(ROOT / "fixtures" / "contract-cases.json")

    def test_registry_and_authority_are_valid(self) -> None:
        self.assertEqual([], validate_registry(self.registry, self.authority))

    def test_registry_is_finite_and_exact_build_bound(self) -> None:
        self.assertEqual(len(self.registry["entries"]), self.registry["entryCount"])
        self.assertGreater(self.registry["entryCount"], 0)
        self.assertEqual("16.0.27606.12000", self.registry["platformBuildMin"])
        self.assertEqual("16.0.27606.12000", self.registry["platformBuildMax"])

    def test_all_contract_fixtures(self) -> None:
        failures: list[str] = []
        cases = [*self.fixtures["positive"], *self.fixtures["negative"]]
        for case in cases:
            try:
                self._run_case(case)
            except AssertionError as error:
                failures.append(f"{case['id']}: {error}")
        self.assertEqual([], failures)

    def _run_case(self, case: dict) -> None:
        kind = case["kind"]
        if kind == "registryRequest":
            registry = apply_fixture_mutation(self.registry, case.get("mutation"))
            request = dict(case["request"])
            request.setdefault("registryRevision", registry["registryRevision"])
            request.setdefault("registryHash", registry["registryHash"])
            result = evaluate_registry_request(registry, request)
            self.assertEqual(case["expectedVerdict"], result["verdict"])
            self.assertEqual(case["expectedReasonCode"], result["reasonCode"])
            return
        if kind == "referenceObservation":
            result = validate_reference_observation(
                case["observation"], case.get("physicalInventoryKeys")
            )
            self.assertEqual(case["expectedVerdict"], result["verdict"])
            self.assertEqual(case["expectedReasonCode"], result["reasonCode"])
            return
        if kind == "dedupeJoin":
            results = [
                validate_reference_observation(row, case["physicalInventoryKeys"])
                for row in case["observations"]
            ]
            self.assertTrue(all(row["reasonCode"] == "UNIQUE_PHYSICAL_JOIN" for row in results))
            physical_actions = {
                row["linkedPhysicalCanonicalInventoryKey"] for row in case["observations"]
            }
            self.assertEqual(case["expectedReferenceCount"], len(results))
            self.assertEqual(case["expectedPhysicalActionCount"], len(physical_actions))
            return
        self.fail(f"unknown fixture kind: {kind}")


if __name__ == "__main__":
    unittest.main(verbosity=2)
