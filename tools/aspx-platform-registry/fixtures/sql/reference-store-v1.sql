-- Synthetic schema fixture derived from Assessment commit
-- 3012555317d5a8ee981b9e103206f3f0680333d8, AspxReferenceStore.Initialize.
PRAGMA journal_mode=DELETE;
PRAGMA page_size=4096;
BEGIN;
CREATE TABLE ReferenceRuns (
  RunId TEXT PRIMARY KEY, ManifestJson TEXT NOT NULL, ManifestHash TEXT NOT NULL,
  OutputVersion TEXT NOT NULL, CoverageVerdict TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
CREATE TABLE ReferenceObservations (
  RunId TEXT NOT NULL, ObservationId TEXT NOT NULL, Json TEXT NOT NULL,
  PRIMARY KEY (RunId, ObservationId));
CREATE TABLE ReferenceDenominator (
  RunId TEXT NOT NULL, SurfaceId TEXT NOT NULL, Json TEXT NOT NULL,
  PRIMARY KEY (RunId, SurfaceId));
CREATE TABLE ReferencePaginationReceipts (
  RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, PageOrdinal INTEGER NOT NULL, Json TEXT NOT NULL,
  PRIMARY KEY (RunId, ScopeKey, PageOrdinal));
CREATE TABLE ReferenceGaps (
  RunId TEXT NOT NULL, GapCode TEXT NOT NULL, PRIMARY KEY (RunId, GapCode));
INSERT INTO ReferenceRuns
  (RunId, ManifestJson, ManifestHash, OutputVersion, CoverageVerdict, UpdatedUtc)
VALUES
  ('11111111-2222-3333-4444-555555555555',
   '{"contractVersion":"aspx-reference/v1","schemaVersion":"aspx-reference-sqlite/v1","productRef":"assessment-fixture@aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sdkRef":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","scopeAuthorityHash":"5555555555555555555555555555555555555555555555555555555555555555","permissionBoundaryHash":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","registryRevision":"spo-online-16.0.27606.12000-r3","registryHash":"61180a9ff5aa3b61ce614bd8faecb2713e40780ccdf55d5ddde174d5a54dfd6d","platformBuildRef":"16.0.27606.12000","snapshotFence":"fixture-snapshot-001","providerVersion":"sharepoint-live-aspx-provider/v1","artifactRunId":"11111111-2222-3333-4444-555555555555"}',
   '1474910ed3a3748d9b5b3eb1101314e05ee21207450b250680a308041d718ff9',
   'aspx-reference-output/v1', 'CompleteAuthorizedSurface',
   '2026-09-10T00:00:01.0000000+00:00');
COMMIT;
