const presentDigest = value => typeof value === "string" && /^[a-f0-9]{64}$/u.test(value);

export function classifyStorageEvidence(storage = {}) {
  const expectedPresent = presentDigest(storage.expectedSha256);
  const actualPresent = presentDigest(storage.actualSha256);

  if (!expectedPresent && !actualPresent) return "expected-and-actual-unavailable";
  if (!expectedPresent) return "expected-unavailable";
  if (!actualPresent) return "actual-unavailable";
  if (storage.expectedSha256 === storage.actualSha256) return "exact";
  return "mismatch-deferred";
}

export function rowsByStorageClassification(pages) {
  const rows = {
    exact: [],
    "expected-unavailable": [],
    "actual-unavailable": [],
    "expected-and-actual-unavailable": [],
    "mismatch-deferred": []
  };
  for (const page of pages) rows[classifyStorageEvidence(page.storage)].push(page.rowNumber);
  return rows;
}
