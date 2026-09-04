using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Execution.Resume;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Taxonomy.Assets;
using PnP.Framework.Migration.Taxonomy.Assets.Execution;
using PnP.Framework.Migration.Taxonomy.Assets.Packaging;
using PnP.Framework.Migration.Taxonomy.Assets.Verification;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class MigrationDurableResumeTests
    {
        private readonly IList<string> temporaryDirectories = new List<string>();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var directory in temporaryDirectories)
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [TestMethod]
        public void JsonLinesJournalFlushesAndReopensEveryRecordKind()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteExecutionState(State(fixture));
                journal.WriteIntent(Intent(fixture, 0));
                journal.WriteReceipt(Receipt(fixture, 0, MutationOutcome.Applied));
                journal.WriteVerification(Verification(fixture));
                journal.WriteArtifact(Artifact(fixture));
            }

            var read = MigrationExecutionJournalReader.Read(path);

            Assert.AreEqual(5, read.Records.Count);
            CollectionAssert.AreEqual(
                new[]
                {
                    MigrationExecutionJournalRecordKind.ExecutionState,
                    MigrationExecutionJournalRecordKind.MutationIntent,
                    MigrationExecutionJournalRecordKind.MutationReceipt,
                    MigrationExecutionJournalRecordKind.MutationVerification,
                    MigrationExecutionJournalRecordKind.Artifact
                },
                read.Records.Select(value => value.RecordKind).ToArray());
            CollectionAssert.AreEqual(
                new long[] { 0, 1, 2, 3, 4 },
                read.Records.Select(value => value.JournalSequence).ToArray());
            Assert.IsFalse(read.HasInterruptedTail);
            Assert.AreEqual(64, read.JournalDigest.Length);

            using (var reopened = new JsonLinesMigrationExecutionJournal(path))
            {
                reopened.WriteExecutionState(State(fixture, MigrationExecutionStatus.Succeeded));
            }
            Assert.AreEqual(6, MigrationExecutionJournalReader.Read(path).Records.Count);
        }

        [TestMethod]
        public void ReaderRetainsTruncatedTailAsInterruptedWriteEvidence()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(fixture, 0));
            }
            File.AppendAllText(path, "{\"schemaVersion\":\"partial");

            var read = MigrationExecutionJournalReader.Read(path);

            Assert.AreEqual(1, read.Records.Count);
            Assert.IsTrue(read.HasInterruptedTail);
            Assert.IsTrue(read.InterruptedTail.ByteCount > 0);
            Assert.AreEqual(64, read.InterruptedTail.Sha256.Length);
            Assert.ThrowsException<InvalidDataException>(() => new JsonLinesMigrationExecutionJournal(path));
        }

        [TestMethod]
        public void ReaderRejectsMiddleCorruptionAndDigestTampering()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(fixture, 0));
                journal.WriteReceipt(Receipt(fixture, 0, MutationOutcome.Applied));
            }
            var lines = File.ReadAllLines(path);
            lines[0] = lines[0].Replace(fixture.Boundary.PlanDigest, new string('f', 64));
            File.WriteAllText(path, string.Join("\n", lines) + "\n");

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                MigrationExecutionJournalReader.Read(path));

            StringAssert.Contains(exception.Message, "invalid");
        }

        [TestMethod]
        public void ReaderRejectsDuplicateOrConflictingJournalSequence()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(fixture, 0));
            }
            var first = File.ReadAllText(path);
            File.AppendAllText(path, first);

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                MigrationExecutionJournalReader.Read(path));

            StringAssert.Contains(exception.Message, "sequence");
        }

        [TestMethod]
        public void WriterRejectsAppliedReceiptWithoutMatchingIntent()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                var exception = Assert.ThrowsException<InvalidDataException>(() =>
                    journal.WriteReceipt(Receipt(fixture, 0, MutationOutcome.Applied)));
                StringAssert.Contains(exception.Message, "no matching intent");
            }
            Assert.AreEqual(0, MigrationExecutionJournalReader.Read(path).Records.Count);
        }

        [TestMethod]
        public void ParallelWritesRemainCompleteJsonLinesRecords()
        {
            var path = JournalPath();
            var fixture = Fixture();
            var failures = new ConcurrentQueue<Exception>();
            var actionSequence = -1;
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                Parallel.For(
                    0,
                    100,
                    new ParallelOptions { MaxDegreeOfParallelism = 8 },
                    index =>
                    {
                        try
                        {
                            var sequence = Interlocked.Increment(ref actionSequence);
                            var local = Fixture("ingredient:" + index, "action:" + index);
                            journal.WriteIntent(Intent(local, sequence));
                            journal.WriteReceipt(Receipt(local, sequence, MutationOutcome.Applied));
                        }
                        catch (Exception exception)
                        {
                            failures.Enqueue(exception);
                        }
                    });
            }
            if (failures.TryPeek(out var first))
            {
                Assert.Fail(failures.Count + " parallel write(s) failed. First: " + first);
            }

            var read = MigrationExecutionJournalReader.Read(path);
            Assert.AreEqual(200, read.Records.Count);
            Assert.AreEqual(100, read.Records.Count(value => value.RecordKind == MigrationExecutionJournalRecordKind.MutationIntent));
            Assert.AreEqual(100, read.Records.Count(value => value.RecordKind == MigrationExecutionJournalRecordKind.MutationReceipt));
        }

        [TestMethod]
        public void StatusProjectionIsDeterministicAndAtomicallyReplaceable()
        {
            var journalPath = JournalPath();
            var statusPath = Path.Combine(Path.GetDirectoryName(journalPath), "repro-status.json");
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(journalPath))
            {
                journal.WriteIntent(Intent(fixture, 0));
                journal.WriteReceipt(Receipt(fixture, 0, MutationOutcome.Applied));
                journal.WriteVerification(Verification(fixture));
            }
            var read = MigrationExecutionJournalReader.Read(journalPath);

            MigrationReproStatusProjector.WriteAtomic(statusPath, read);
            var first = File.ReadAllBytes(statusPath);
            MigrationReproStatusProjector.WriteAtomic(statusPath, read);
            var second = File.ReadAllBytes(statusPath);

            CollectionAssert.AreEqual(first, second);
            using (var stream = new MemoryStream())
            {
                MigrationReproStatusProjector.Write(stream, read);
                CollectionAssert.AreEqual(first, stream.ToArray());
            }
            var projected = MigrationReproStatusProjector.Project(read);
            Assert.AreEqual("Verified", projected.Ingredients.Single().State);
            Assert.IsFalse(Directory.GetFiles(Path.GetDirectoryName(statusPath), "repro-status.json.tmp-*").Any());
        }

        [TestMethod]
        public void IntentWithoutReceiptRequiresFreshProbeAndNeverBlindlyReplays()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(fixture, 0));
            }
            var calls = 0;

            var decision = MigrationResumeCoordinator.Evaluate(
                MigrationExecutionJournalReader.Read(path),
                Request(fixture),
                () =>
                {
                    calls++;
                    return ExactProbe(fixture);
                });

            Assert.AreEqual(1, calls);
            Assert.IsTrue(decision.FreshProbePerformed);
            Assert.AreEqual(MigrationResumeDisposition.AlreadySatisfied, decision.Disposition);
        }

        [TestMethod]
        public void VerifiedExactTargetResumesButTargetDriftRequiresNewApproval()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(fixture, 0));
                journal.WriteReceipt(Receipt(fixture, 0, MutationOutcome.Applied));
                journal.WriteVerification(Verification(fixture));
            }
            var read = MigrationExecutionJournalReader.Read(path);

            var exact = MigrationResumeCoordinator.Evaluate(read, Request(fixture), () => ExactProbe(fixture));
            var drift = MigrationResumeCoordinator.Evaluate(read, Request(fixture), () => new MigrationFreshProbeResult
            {
                State = MigrationFreshProbeState.Drifted,
                Diagnostic = "Target name changed."
            });

            Assert.AreEqual(MigrationResumeDisposition.AlreadySatisfied, exact.Disposition);
            Assert.AreEqual(MigrationResumeDisposition.ReplanAndReapprove, drift.Disposition);
        }

        [TestMethod]
        public void ResumeRejectsStaleSnapshotPlanOrApprovalBoundary()
        {
            var path = JournalPath();
            var fixture = Fixture();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(fixture, 0));
            }
            var stale = Fixture(fixture.Identity.IngredientId, fixture.Identity.ActionId);
            stale.Boundary.ApprovalDigest = new string('9', 64);
            stale.Identity = MigrationMutationIdentity.Create(
                stale.Boundary,
                fixture.Identity.IngredientId,
                fixture.Identity.ActionId,
                fixture.Identity.SelectedDisposition,
                fixture.Identity.SemanticDigest);

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                MigrationResumeCoordinator.Evaluate(
                    MigrationExecutionJournalReader.Read(path),
                    Request(stale),
                    () => ExactProbe(stale)));

            StringAssert.Contains(exception.Message, "stale snapshot, plan, approval");
        }

        [TestMethod]
        public void NoIntentRemainsPendingWithoutUsingProbeAsMutationAuthority()
        {
            var fixture = Fixture();
            var calls = 0;

            var decision = MigrationResumeCoordinator.Evaluate(
                MigrationExecutionJournalReader.Read(JournalPath()),
                Request(fixture),
                () =>
                {
                    calls++;
                    return ExactProbe(fixture);
                });

            Assert.AreEqual(MigrationResumeDisposition.Pending, decision.Disposition);
            Assert.AreEqual(0, calls);
            Assert.IsFalse(decision.FreshProbePerformed);
        }

        [TestMethod]
        public void InterruptedTailForcesFreshProbeEvenWithoutCompletedIntent()
        {
            var path = JournalPath();
            var fixture = Fixture();
            File.WriteAllText(path, "{\"schemaVersion\":\"partial");
            var calls = 0;

            var decision = MigrationResumeCoordinator.Evaluate(
                MigrationExecutionJournalReader.Read(path),
                Request(fixture),
                () =>
                {
                    calls++;
                    return new MigrationFreshProbeResult { State = MigrationFreshProbeState.Absent };
                });

            Assert.AreEqual(1, calls);
            Assert.AreEqual(MigrationResumeDisposition.Pending, decision.Disposition);
            StringAssert.Contains(decision.Diagnostic, "interrupted journal tail");
        }

        [TestMethod]
        public void InMemoryJournalRemainsBackwardCompatibleAndCapturesCheckpointExtensions()
        {
            var fixture = Fixture();
            var journal = new InMemoryMigrationExecutionJournal();

            journal.WriteExecutionState(State(fixture));
            journal.WriteIntent(Intent(fixture, 0));
            journal.WriteReceipt(Receipt(fixture, 0, MutationOutcome.Applied));
            journal.WriteVerification(Verification(fixture));
            journal.WriteArtifact(Artifact(fixture));

            Assert.AreEqual(1, journal.ExecutionStates.Count);
            Assert.AreEqual(1, journal.Intents.Count);
            Assert.AreEqual(1, journal.Receipts.Count);
            Assert.AreEqual(1, journal.Verifications.Count);
            Assert.AreEqual(1, journal.Artifacts.Count);
        }

        [TestMethod]
        public void TaxonomyOwnedPlansCarryOriginalIdentityAndStableMappingDigest()
        {
            var tenant = Guid.NewGuid();
            var sourceStore = Guid.NewGuid();
            var sourceSet = Guid.NewGuid();
            var sourceTerm = Guid.NewGuid();
            var targetStore = Guid.NewGuid();
            var setPlan = TaxonomyAssetIdentity.CreateTermSetPlan(
                new TaxonomyTermSetSourceSnapshot
                {
                    SourceTenantId = tenant,
                    SourceTermStoreId = sourceStore,
                    SourceTermSetId = sourceSet,
                    Name = "Wiki Categories",
                    Language = 1033,
                    IsOpenForTermCreation = true,
                    IsAvailableForTagging = true,
                    EvidenceSha256 = new string('a', 64)
                },
                targetStore);
            var termPlan = TaxonomyAssetIdentity.CreateTermPlan(
                new TaxonomyTermSourceSnapshot
                {
                    SourceTenantId = tenant,
                    SourceTermStoreId = sourceStore,
                    SourceTermSetId = sourceSet,
                    SourceTermId = sourceTerm,
                    Name = "Proof Points",
                    Language = 1033,
                    EvidenceSha256 = new string('b', 64)
                },
                targetStore,
                sourceSet,
                null);

            Assert.AreEqual(TaxonomyAssetIdentity.OriginalIdentifierPropertyName, setPlan.OriginalIdentifierPropertyName);
            Assert.AreEqual(TaxonomyAssetIdentity.MappingDigestPropertyName, setPlan.MappingDigestPropertyName);
            Assert.AreEqual(64, setPlan.MappingDigest.Length);
            Assert.AreEqual(64, termPlan.MappingDigest.Length);
            Assert.AreEqual(setPlan.MappingDigest, TaxonomyAssetIdentity.ComputeMappingDigest(setPlan));
            Assert.AreEqual(termPlan.MappingDigest, TaxonomyAssetIdentity.ComputeMappingDigest(termPlan));
        }

        [TestMethod]
        public void TaxonomyMappingMarkerConflictFailsWithoutOverwrite()
        {
            var properties = new Dictionary<string, string>
            {
                [TaxonomyAssetIdentity.MappingDigestPropertyName] = new string('a', 64)
            };

            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                TaxonomyAssetCsomMaterializer.AssertMapping(
                    properties,
                    TaxonomyAssetIdentity.MappingDigestPropertyName,
                    new string('b', 64),
                    "Term"));

            StringAssert.Contains(exception.Message, "was not overwritten");
            Assert.AreEqual(new string('a', 64), properties[TaxonomyAssetIdentity.MappingDigestPropertyName]);
        }

        [TestMethod]
        public void TaxonomyOwnedInspectionRequiresOriginalIdentityAndMappingDigest()
        {
            const string original = "urn:pnp:spo-term:v1:source";
            var mapping = new string('a', 64);

            Assert.AreEqual(
                TaxonomyOwnedProvenanceState.Exact,
                TaxonomyOwnedProvenance.Evaluate(original, original, mapping, mapping));
            Assert.AreEqual(
                TaxonomyOwnedProvenanceState.MappingDigestMissing,
                TaxonomyOwnedProvenance.Evaluate(original, original, null, mapping));
            Assert.AreEqual(
                TaxonomyOwnedProvenanceState.MappingDigestConflict,
                TaxonomyOwnedProvenance.Evaluate(original, original, new string('b', 64), mapping));
            Assert.AreEqual(
                TaxonomyOwnedProvenanceState.NotOwned,
                TaxonomyOwnedProvenance.Evaluate(null, original, mapping, mapping));
        }

        [TestMethod]
        public void ExternalReuseReceiptIsExplicitAndDoesNotBecomeOwned()
        {
            var action = ExternalTermSetApproval();
            var receipt = new TaxonomyAssetActionReceipt
            {
                ActionId = action.ActionId,
                Kind = action.Kind,
                SourceTenantId = action.SourceTenantId,
                SourceTermStoreId = action.SourceTermStoreId,
                SourceTermSetId = action.SourceTermSetId,
                TargetTermStoreId = action.TargetTermStoreId,
                TargetTermSetId = action.TargetTermSetId,
                ReviewedDisposition = action.ReviewedDisposition,
                PreflightDisposition = action.ReviewedDisposition,
                FinalDisposition = action.ReviewedDisposition,
                FreshReadbackPassed = true
            };

            TaxonomyAssetReceiptIdentity.Populate(
                receipt,
                action,
                new string('c', 64),
                new string('d', 64));

            Assert.AreEqual(TaxonomyAssetOwnership.External, receipt.Ownership);
            Assert.AreEqual(TaxonomyAssetReceiptDisposition.ReuseExternal, receipt.ExecutionDisposition);
            Assert.IsTrue(receipt.SourceIdentity.StartsWith("urn:pnp:spo-termset:v1:", StringComparison.Ordinal));
            Assert.IsTrue(receipt.TargetIdentity.Contains(action.TargetTermSetId.ToString("N"), StringComparison.Ordinal));
            Assert.AreEqual(
                TaxonomyOwnedProvenanceState.NotOwned,
                TaxonomyOwnedProvenance.Evaluate(
                    TaxonomyAssetIdentity.ExternalReferencePropertyName,
                    receipt.SourceIdentity,
                    null,
                    receipt.SemanticMappingDigest));
        }

        [TestMethod]
        public void ExternalReferenceStampRequiresSeparateDigestBoundApproval()
        {
            var stamp = new TaxonomyExternalReferenceStamp
            {
                ActionId = "StampExternalReferenceProvenance:termset",
                Kind = TaxonomyAssetKind.TermSet,
                SourceIdentity = "urn:pnp:spo-termset:v1:source",
                TargetIdentity = "urn:pnp:spo-target-termset:v1:target",
                PropertyValue = "urn:pnp:spo-termset:v1:source",
                ReviewPlanDigest = new string('a', 64),
                ApprovalDigest = new string('b', 64),
                ExplicitPerObjectApproval = false
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                TaxonomyExternalReferenceStampPolicy.Seal(stamp));

            stamp.ExplicitPerObjectApproval = true;
            TaxonomyExternalReferenceStampPolicy.Seal(stamp);
            Assert.AreEqual(64, stamp.StampDigest.Length);
            Assert.AreNotEqual(TaxonomyAssetIdentity.OriginalIdentifierPropertyName, stamp.PropertyName);
            Assert.AreNotEqual(TaxonomyAssetIdentity.MappingDigestPropertyName, stamp.PropertyName);
        }

        [TestMethod]
        public void ExternalParentAndCreatedChildHaveIndependentOwnership()
        {
            var parent = ExternalTermSetApproval();
            var child = new TaxonomyAssetActionApproval
            {
                ActionId = "taxonomy:term:proof-points",
                Kind = TaxonomyAssetKind.Term,
                SourceTenantId = parent.SourceTenantId,
                SourceTermStoreId = parent.SourceTermStoreId,
                SourceTermSetId = parent.SourceTermSetId,
                SourceTermId = Guid.Parse("67984a5d-e21d-4f50-9a30-cede4c211a5e"),
                TargetTermStoreId = parent.TargetTermStoreId,
                TargetTermSetId = parent.TargetTermSetId,
                TargetTermId = Guid.Parse("67984a5d-e21d-4f50-9a30-cede4c211a5e"),
                ReviewedDisposition = TaxonomyAssetTargetDisposition.CreateMissingAfterExternalApproval,
                Decision = TaxonomyAssetApprovalDecision.Approve,
                ExternalMutationApproved = true
            };
            var parentReceipt = new TaxonomyAssetActionReceipt();
            var childReceipt = new TaxonomyAssetActionReceipt { ChangedTarget = true };

            TaxonomyAssetReceiptIdentity.Populate(parentReceipt, parent, new string('a', 64), new string('b', 64));
            TaxonomyAssetReceiptIdentity.Populate(childReceipt, child, new string('a', 64), new string('b', 64));

            Assert.AreEqual(TaxonomyAssetOwnership.External, parentReceipt.Ownership);
            Assert.AreEqual(TaxonomyAssetReceiptDisposition.ReuseExternal, parentReceipt.ExecutionDisposition);
            Assert.AreEqual(TaxonomyAssetOwnership.MigrationOwned, childReceipt.Ownership);
            Assert.AreEqual(TaxonomyAssetReceiptDisposition.CreatedOwned, childReceipt.ExecutionDisposition);
        }

        [TestMethod]
        public void InterruptedProofPointsCreateCanResumeOnlyAfterMarkerBackedFreshProbe()
        {
            var action = new TaxonomyAssetActionApproval
            {
                ActionId = "taxonomy:term:proof-points",
                Kind = TaxonomyAssetKind.Term,
                SourceTenantId = Guid.NewGuid(),
                SourceTermStoreId = Guid.NewGuid(),
                SourceTermSetId = Guid.Parse("787ae7d4-495e-46c2-a3be-066d33fcfced"),
                SourceTermId = Guid.Parse("67984a5d-e21d-4f50-9a30-cede4c211a5e"),
                TargetTermStoreId = Guid.NewGuid(),
                TargetTermSetId = Guid.Parse("944420e8-a0f1-4e90-8184-5e7b78d194a9"),
                TargetTermId = Guid.Parse("67984a5d-e21d-4f50-9a30-cede4c211a5e"),
                ReviewedDisposition = TaxonomyAssetTargetDisposition.CreateMissingAfterExternalApproval,
                Decision = TaxonomyAssetApprovalDecision.Approve,
                ExternalMutationApproved = true
            };
            var boundary = MigrationExecutionBoundary.Create(
                new string('1', 64),
                new string('2', 64),
                new string('3', 64),
                "taxonomy-term-store:" + action.TargetTermStoreId.ToString("D"));
            var identity = MigrationMutationIdentity.Create(
                boundary,
                action.ActionId,
                action.ActionId,
                action.ReviewedDisposition.ToString(),
                TaxonomyAssetReceiptIdentity.SemanticMappingDigest(action));
            var fixture = new JournalFixture
            {
                OperationId = Guid.NewGuid(),
                Boundary = boundary,
                Identity = identity
            };
            var path = JournalPath();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(fixture, 0));
            }

            var decision = MigrationResumeCoordinator.Evaluate(
                MigrationExecutionJournalReader.Read(path),
                Request(fixture),
                () => ExactProbe(fixture));

            Assert.AreEqual(MigrationResumeDisposition.AlreadySatisfied, decision.Disposition);
            Assert.IsTrue(decision.Probe.ProvenanceMatched);
            Assert.AreEqual("MigrationOwned", decision.Probe.Ownership);
        }

        [TestMethod]
        public void SealedTaxonomyReceiptAndCatalogRetainExternalMappingOwnership()
        {
            var plan = ExternalTaxonomyPlan();
            var approval = TaxonomyAssetApprovalFactory.CreateTemplate(plan, DateTimeOffset.Parse("2026-09-04T00:00:00Z"));
            foreach (var action in approval.Actions)
            {
                action.Decision = TaxonomyAssetApprovalDecision.Approve;
            }
            TaxonomyAssetApprovalFactory.Seal(
                plan,
                approval,
                "reviewer@example.com",
                DateTimeOffset.Parse("2026-09-04T00:01:00Z"));
            var receipt = new TaxonomyAssetMaterializationReceipt
            {
                OperationId = Guid.NewGuid(),
                ReviewPlanDigest = plan.PlanDigest,
                ApprovalDigest = approval.ApprovalDigest,
                TargetTermStoreId = plan.TargetTermStoreId,
                StartedAtUtc = DateTimeOffset.Parse("2026-09-04T00:02:00Z"),
                CompletedAtUtc = DateTimeOffset.Parse("2026-09-04T00:03:00Z"),
                FreshReadbackPassed = true
            };
            foreach (var action in approval.Actions)
            {
                var actionReceipt = new TaxonomyAssetActionReceipt
                {
                    ActionId = action.ActionId,
                    Kind = action.Kind,
                    SourceTenantId = action.SourceTenantId,
                    SourceTermStoreId = action.SourceTermStoreId,
                    SourceTermSetId = action.SourceTermSetId,
                    SourceTermId = action.SourceTermId,
                    TargetTermStoreId = action.TargetTermStoreId,
                    TargetTermGroupId = action.TargetTermGroupId,
                    TargetTermSetId = action.TargetTermSetId,
                    TargetTermId = action.TargetTermId,
                    ReviewedDisposition = action.ReviewedDisposition,
                    PreflightDisposition = action.ReviewedDisposition,
                    FinalDisposition = TaxonomyAssetVerifier.ExpectedFinalDisposition(action.ReviewedDisposition),
                    FreshReadbackPassed = true
                };
                TaxonomyAssetReceiptIdentity.Populate(
                    actionReceipt,
                    action,
                    plan.PlanDigest,
                    approval.ApprovalDigest);
                receipt.Actions.Add(actionReceipt);
            }
            TaxonomyAssetMaterializationReceiptValidator.Seal(plan, approval, receipt);

            var catalog = TaxonomyAssetMappingCatalogFactory.Create(
                plan,
                approval,
                receipt,
                DateTimeOffset.Parse("2026-09-04T00:04:00Z"));

            Assert.AreEqual(64, receipt.ReceiptDigest.Length);
            Assert.AreEqual(64, catalog.CatalogDigest.Length);
            Assert.AreEqual(3, catalog.AssetMappings.Count);
            Assert.IsTrue(catalog.AssetMappings.Any(value =>
                value.Kind == TaxonomyAssetKind.TermSet
                && value.Ownership == TaxonomyAssetOwnership.External
                && value.Disposition == TaxonomyAssetReceiptDisposition.ReuseExternal));
            Assert.AreEqual(
                Guid.Parse("944420e8-a0f1-4e90-8184-5e7b78d194a9"),
                catalog.FieldBindings.Single().TargetTermSetId);
        }

        private string JournalPath()
        {
            var directory = Path.Combine(Path.GetTempPath(), "pnp-migration-journal-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            temporaryDirectories.Add(directory);
            return Path.Combine(directory, "execution-journal.jsonl");
        }

        private static JournalFixture Fixture(string ingredientId = "ingredient:page", string actionId = "action:ensure-page")
        {
            var boundary = MigrationExecutionBoundary.Create(
                new string('a', 64),
                new string('b', 64),
                new string('c', 64),
                "site:https://contoso.sharepoint.com/sites/target");
            return new JournalFixture
            {
                OperationId = Guid.NewGuid(),
                Boundary = boundary,
                Identity = MigrationMutationIdentity.Create(
                    boundary,
                    ingredientId,
                    actionId,
                    "CreateMissing",
                    new string('d', 64))
            };
        }

        private static MigrationExecutionStateReceipt State(
            JournalFixture fixture,
            MigrationExecutionStatus status = MigrationExecutionStatus.Running)
        {
            return new MigrationExecutionStateReceipt
            {
                OperationId = fixture.OperationId,
                PlanDigest = fixture.Boundary.PlanDigest,
                RecordedAtUtc = DateTimeOffset.Parse("2026-09-04T01:00:00Z"),
                Status = status,
                Message = status.ToString(),
                SourceSnapshotDigest = fixture.Boundary.SourceSnapshotDigest,
                ApprovalDigest = fixture.Boundary.ApprovalDigest,
                TargetBoundaryDigest = fixture.Boundary.TargetBoundaryDigest
            };
        }

        private static MigrationMutationIntent Intent(JournalFixture fixture, int sequence)
        {
            return new MigrationMutationIntent
            {
                OperationId = fixture.OperationId,
                PlanDigest = fixture.Boundary.PlanDigest,
                ActionId = fixture.Identity.ActionId,
                Sequence = sequence,
                WrittenAtUtc = DateTimeOffset.Parse("2026-09-04T01:00:01Z").AddSeconds(sequence),
                Description = "Ensure ingredient.",
                SourceSnapshotDigest = fixture.Boundary.SourceSnapshotDigest,
                ApprovalDigest = fixture.Boundary.ApprovalDigest,
                IngredientId = fixture.Identity.IngredientId,
                SelectedDisposition = fixture.Identity.SelectedDisposition,
                TargetBoundaryDigest = fixture.Boundary.TargetBoundaryDigest,
                SemanticDigest = fixture.Identity.SemanticDigest,
                IdempotencyKey = fixture.Identity.IdempotencyKey
            };
        }

        private static MigrationMutationReceipt Receipt(
            JournalFixture fixture,
            int sequence,
            MutationOutcome outcome)
        {
            return new MigrationMutationReceipt
            {
                OperationId = fixture.OperationId,
                PlanDigest = fixture.Boundary.PlanDigest,
                ActionId = fixture.Identity.ActionId,
                Sequence = sequence,
                CompletedAtUtc = DateTimeOffset.Parse("2026-09-04T01:00:10Z").AddSeconds(sequence),
                Outcome = outcome,
                Message = outcome.ToString(),
                SourceSnapshotDigest = fixture.Boundary.SourceSnapshotDigest,
                ApprovalDigest = fixture.Boundary.ApprovalDigest,
                IngredientId = fixture.Identity.IngredientId,
                SelectedDisposition = fixture.Identity.SelectedDisposition,
                TargetBoundaryDigest = fixture.Boundary.TargetBoundaryDigest,
                SemanticDigest = fixture.Identity.SemanticDigest,
                IdempotencyKey = fixture.Identity.IdempotencyKey
            };
        }

        private static MigrationMutationVerificationReceipt Verification(JournalFixture fixture)
        {
            return new MigrationMutationVerificationReceipt
            {
                OperationId = fixture.OperationId,
                PlanDigest = fixture.Boundary.PlanDigest,
                ActionId = fixture.Identity.ActionId,
                VerifiedAtUtc = DateTimeOffset.Parse("2026-09-04T01:01:00Z"),
                FreshReadbackPassed = true,
                CurrentStateDigest = fixture.Identity.SemanticDigest,
                Ownership = "MigrationOwned",
                TargetIdentity = "urn:pnp:test-target:v1:item",
                SourceSnapshotDigest = fixture.Boundary.SourceSnapshotDigest,
                ApprovalDigest = fixture.Boundary.ApprovalDigest,
                IngredientId = fixture.Identity.IngredientId,
                SelectedDisposition = fixture.Identity.SelectedDisposition,
                TargetBoundaryDigest = fixture.Boundary.TargetBoundaryDigest,
                SemanticDigest = fixture.Identity.SemanticDigest,
                IdempotencyKey = fixture.Identity.IdempotencyKey,
                Message = "Fresh readback matched."
            };
        }

        private static MigrationExecutionArtifact Artifact(JournalFixture fixture)
        {
            const string payload = "{\"schemaVersion\":\"test-artifact/v1\",\"value\":42}";
            return new MigrationExecutionArtifact
            {
                OperationId = fixture.OperationId,
                PlanDigest = fixture.Boundary.PlanDigest,
                WrittenAtUtc = DateTimeOffset.Parse("2026-09-04T01:01:01Z"),
                ArtifactKind = "TestArtifact",
                ArtifactSchemaVersion = "test-artifact/v1",
                ArtifactDigest = MigrationDigest.ComputeSha256(payload),
                PayloadJson = payload,
                PayloadSha256 = MigrationDigest.ComputeSha256(payload)
            };
        }

        private static MigrationResumeRequest Request(JournalFixture fixture)
        {
            return new MigrationResumeRequest
            {
                Boundary = fixture.Boundary,
                Mutation = fixture.Identity,
                ExpectedOwnership = "MigrationOwned"
            };
        }

        private static MigrationFreshProbeResult ExactProbe(JournalFixture fixture)
        {
            return new MigrationFreshProbeResult
            {
                State = MigrationFreshProbeState.Exact,
                ProvenanceMatched = true,
                Ownership = "MigrationOwned",
                CurrentStateDigest = fixture.Identity.SemanticDigest,
                TargetIdentity = "urn:pnp:test-target:v1:item"
            };
        }

        private static TaxonomyAssetActionApproval ExternalTermSetApproval()
        {
            return new TaxonomyAssetActionApproval
            {
                ActionId = "taxonomy:termset:wiki-categories",
                Kind = TaxonomyAssetKind.TermSet,
                SourceTenantId = Guid.NewGuid(),
                SourceTermStoreId = Guid.NewGuid(),
                SourceTermSetId = Guid.Parse("787ae7d4-495e-46c2-a3be-066d33fcfced"),
                TargetTermStoreId = Guid.NewGuid(),
                TargetTermSetId = Guid.Parse("944420e8-a0f1-4e90-8184-5e7b78d194a9"),
                ReviewedDisposition = TaxonomyAssetTargetDisposition.ReviewExternalReuse,
                Decision = TaxonomyAssetApprovalDecision.Approve,
                RequiresExplicitReview = true
            };
        }

        private static TaxonomyAssetReviewPlan ExternalTaxonomyPlan()
        {
            var tenantId = Guid.Parse("72f988bf-86f1-41af-91ab-2d7cd011db47");
            var sourceStoreId = Guid.Parse("e385fb40-52d4-4fae-9c5b-3e8ff8a5878e");
            var targetStoreId = Guid.Parse("c5e18914-52aa-4047-8ef6-f9654987b925");
            var sourceSetId = Guid.Parse("787ae7d4-495e-46c2-a3be-066d33fcfced");
            var targetSetId = Guid.Parse("944420e8-a0f1-4e90-8184-5e7b78d194a9");
            var termId = Guid.Parse("e1440666-fd09-44e5-9ac0-c1c9fa648ec4");
            var source = new TaxonomyAssetSourceSnapshot
            {
                SourceTenantId = tenantId,
                SnapshotDigest = new string('1', 64),
                TermSets = new List<TaxonomyTermSetSourceSnapshot>
                {
                    new TaxonomyTermSetSourceSnapshot
                    {
                        SourceTenantId = tenantId,
                        SourceTermStoreId = sourceStoreId,
                        SourceTermSetId = sourceSetId,
                        Name = "Wiki Categories",
                        Language = 1033,
                        IsOpenForTermCreation = true,
                        IsAvailableForTagging = true,
                        EvidenceSha256 = new string('2', 64),
                        Availability = EvidenceAvailability.Captured
                    }
                },
                Terms = new List<TaxonomyTermSourceSnapshot>
                {
                    new TaxonomyTermSourceSnapshot
                    {
                        SourceTenantId = tenantId,
                        SourceTermStoreId = sourceStoreId,
                        SourceTermSetId = sourceSetId,
                        SourceTermId = termId,
                        Name = "Getting Started",
                        Path = "Getting Started",
                        Language = 1033,
                        IsAvailableForTagging = true,
                        EvidenceSha256 = new string('3', 64),
                        Availability = EvidenceAvailability.Captured
                    }
                }
            };
            var plan = TaxonomyAssetPlanner.Create(source, targetStoreId);
            var groupPlan = plan.TermGroups.Single();
            var termPlan = plan.Terms.Single();
            termPlan.TargetTermSetId = targetSetId;
            termPlan.MappingDigest = TaxonomyAssetIdentity.ComputeMappingDigest(termPlan);
            termPlan.PlanDigest = TaxonomyAssetIdentity.ComputePlanDigest(termPlan);
            plan.TermGroupProbes.Add(new TaxonomyTermGroupTargetProbe
            {
                SourceTenantId = tenantId,
                SourceTermStoreId = sourceStoreId,
                TargetTermStoreId = targetStoreId,
                ResolvedTargetGroupId = groupPlan.PreferredTargetGroupId,
                Disposition = TaxonomyAssetTargetDisposition.ReuseOwned
            });
            plan.TermSetProbes.Add(new TaxonomyTermSetTargetProbe
            {
                SourceTermStoreId = sourceStoreId,
                SourceTermSetId = sourceSetId,
                TargetTermStoreId = targetStoreId,
                ResolvedTargetTermSetId = targetSetId,
                Disposition = TaxonomyAssetTargetDisposition.ReviewExternalReuse
            });
            plan.TermProbes.Add(new TaxonomyTermTargetProbe
            {
                SourceTermStoreId = sourceStoreId,
                SourceTermSetId = sourceSetId,
                SourceTermId = termId,
                TargetTermStoreId = targetStoreId,
                TargetTermSetId = targetSetId,
                ResolvedTargetTermId = termId,
                ExistingTermSetId = targetSetId,
                ExistingTermSetIds = new List<Guid> { targetSetId },
                ExistingIsReused = false,
                ExistingIsSourceTerm = true,
                Disposition = TaxonomyAssetTargetDisposition.ReviewExternalReuse
            });
            plan.MappingCandidates.Add(new TaxonomyAssetMappingCandidate
            {
                SourceTermStoreId = sourceStoreId,
                SourceTermSetId = sourceSetId,
                TargetTermStoreId = targetStoreId,
                TargetTermSetId = targetSetId,
                Disposition = TaxonomyAssetTargetDisposition.ReviewExternalReuse,
                RequiresReview = true,
                EvidenceSha256 = new string('4', 64)
            });
            plan.PlanDigest = TaxonomyAssetPlanner.ComputeDigest(plan);
            TaxonomyAssetReviewPlanValidator.Validate(plan);
            return plan;
        }

        private sealed class JournalFixture
        {
            public Guid OperationId { get; set; }

            public MigrationExecutionBoundary Boundary { get; set; }

            public MigrationMutationIdentity Identity { get; set; }
        }
    }
}
