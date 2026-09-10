-- Synthetic schema fixture derived from Assessment commit
-- 3012555317d5a8ee981b9e103206f3f0680333d8, DiscoveryStore.InitializeSchema.
PRAGMA journal_mode=DELETE;
PRAGMA page_size=4096;
BEGIN;
CREATE TABLE DiscoveryRuns (RunId TEXT PRIMARY KEY, ManifestJson TEXT NOT NULL, ManifestHash TEXT NOT NULL, ScopeMode TEXT NOT NULL, ExecutionStatus TEXT NOT NULL, Verdict TEXT NULL, FixtureRun INTEGER NOT NULL, CreatedUtc TEXT NOT NULL, FinishedUtc TEXT NULL);
CREATE TABLE DiscoveryScopes (RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, ParentScopeKey TEXT NULL, Kind TEXT NOT NULL, SourceKind TEXT NULL, Locator TEXT NULL, PermissionContext TEXT NULL, Required INTEGER NOT NULL, Outcome TEXT NOT NULL, ExclusionRuleId TEXT NULL, ExclusionRuleVersion TEXT NULL, ExclusionRuleHash TEXT NULL, ExclusionApprovalRef TEXT NULL, PRIMARY KEY (RunId, ScopeKey));
CREATE TABLE DiscoveryChildEnumerations (RunId TEXT NOT NULL, ParentScopeKey TEXT NOT NULL, ChildKind TEXT NOT NULL, Outcome TEXT NOT NULL, ExpectedCount INTEGER NOT NULL, EnumerationFingerprint TEXT NOT NULL, PermissionContext TEXT NULL, UpdatedUtc TEXT NOT NULL, PRIMARY KEY (RunId, ParentScopeKey, ChildKind));
CREATE TABLE DiscoveryExpectedChildren (RunId TEXT NOT NULL, ParentScopeKey TEXT NOT NULL, ChildScopeKey TEXT NOT NULL, ChildKind TEXT NOT NULL, SourceKind TEXT NULL, Locator TEXT NULL, PermissionContext TEXT NULL, Required INTEGER NOT NULL, PRIMARY KEY (RunId, ParentScopeKey, ChildScopeKey));
CREATE TABLE DiscoveryAttempts (AttemptId TEXT PRIMARY KEY, RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, SourceKind TEXT NOT NULL, Status TEXT NOT NULL, StartedUtc TEXT NOT NULL, FinishedUtc TEXT NULL);
CREATE UNIQUE INDEX UX_DiscoveryAttempts_Active ON DiscoveryAttempts(RunId, ScopeKey, SourceKind) WHERE Status='Running';
CREATE TABLE DiscoveryBatches (BatchId TEXT PRIMARY KEY, AttemptId TEXT NOT NULL, BatchOrdinal INTEGER NOT NULL, RequestFingerprint TEXT NOT NULL, ResponseFingerprint TEXT NOT NULL, TerminalFlag INTEGER NOT NULL, NextCheckpoint TEXT NULL, CommittedUtc TEXT NOT NULL, UNIQUE (AttemptId, BatchOrdinal));
CREATE TABLE DiscoveryObservations (ObservationId TEXT PRIMARY KEY, RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, SourceKind TEXT NOT NULL, ObservationKey TEXT NOT NULL, FactHash TEXT NOT NULL, SourceObjectKey TEXT NOT NULL, FileName TEXT NULL, PhysicalLocator TEXT NULL, PermissionContext TEXT NULL, MetadataJson TEXT NOT NULL, UNIQUE (RunId, ObservationKey, FactHash));
CREATE TABLE DiscoveryAttemptObservations (AttemptId TEXT NOT NULL, ObservationId TEXT NOT NULL, BatchId TEXT NOT NULL, Emitted INTEGER NOT NULL, PRIMARY KEY (AttemptId, ObservationId));
CREATE TABLE DiscoveryInventory (RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, CanonicalInventoryKey TEXT NOT NULL, PhysicalLocator TEXT NULL, FileName TEXT NOT NULL, IdentityQuality TEXT NOT NULL, PermissionContext TEXT NULL, PRIMARY KEY (RunId, CanonicalInventoryKey));
CREATE INDEX IX_DiscoveryInventory_Locator ON DiscoveryInventory(RunId, PhysicalLocator);
CREATE TABLE DiscoveryInventoryObservations (RunId TEXT NOT NULL, CanonicalInventoryKey TEXT NOT NULL, ObservationId TEXT NOT NULL, PRIMARY KEY (RunId, CanonicalInventoryKey, ObservationId));
CREATE TABLE DiscoveryGaps (RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, GapKey TEXT NOT NULL, SourceKind TEXT NOT NULL, Code TEXT NOT NULL, Detail TEXT NULL, Resolved INTEGER NOT NULL, PRIMARY KEY (RunId, GapKey));
CREATE TABLE DiscoveryConflicts (RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, ConflictKey TEXT NOT NULL, Code TEXT NOT NULL, ParticipantKeys TEXT NOT NULL, Resolved INTEGER NOT NULL, PRIMARY KEY (RunId, ConflictKey));
INSERT INTO DiscoveryRuns
  (RunId, ManifestJson, ManifestHash, ScopeMode, ExecutionStatus, Verdict, FixtureRun, CreatedUtc, FinishedUtc)
VALUES
  ('11111111-2222-3333-4444-555555555555',
   '{"productRef":"pnp/assessment@3012555317d5a8ee981b9e103206f3f0680333d8","sdkRef":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","contractVersion":"aspx-discovery/v2","schemaVersion":"aspx-discovery-sqlite/v2","migrationSetHash":"1111111111111111111111111111111111111111111111111111111111111111","inputManifestHash":"2222222222222222222222222222222222222222222222222222222222222222","scopePolicyHash":"5555555555555555555555555555555555555555555555555555555555555555","tenantManifestHash":"3333333333333333333333333333333333333333333333333333333333333333","binaryArtifactHash":"4444444444444444444444444444444444444444444444444444444444444444","buildManifestHash":"6666666666666666666666666666666666666666666666666666666666666666","dependencyManifestHash":"7777777777777777777777777777777777777777777777777777777777777777","environmentManifestHash":"8888888888888888888888888888888888888888888888888888888888888888","fixtureRevision":"ccd-415-f3-v1","fixtureHash":"9999999999999999999999999999999999999999999999999999999999999999"}',
   '60295e20e59f434c5f3a8d9bf7abb39af594bcd6c09584d49ce60e4a5d575ca2',
   'declared_subset', 'Finished', 'CompleteAuthorizedSurface', 1,
   '2026-09-10T00:00:00.0000000+00:00', '2026-09-10T00:00:01.0000000+00:00');
COMMIT;
