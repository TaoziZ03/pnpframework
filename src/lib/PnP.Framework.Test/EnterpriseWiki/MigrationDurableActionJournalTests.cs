using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Execution.Resume;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Taxonomy;
using PnP.Framework.Migration.Taxonomy;
using PnP.Framework.Migration.Taxonomy.Assets;
using PnP.Framework.Migration.Taxonomy.Assets.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class MigrationDurableActionJournalTests
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
        public void ActionSignatureIgnoresSiblingPlansButTracksDependenciesAndSelection()
        {
            var dependency = Signature("dependency", '1');
            var action = Signature("action", '2', dependency.Signature);
            var identicalAfterUnrelatedPlanChange = Signature("action", '2', dependency.Signature);
            var changedDependency = Signature("dependency", '3');
            var downstreamChanged = Signature("action", '2', changedDependency.Signature);
            var selectionChanged = MigrationActionSignature.Create(
                "action",
                "Taxonomy.Term",
                Hex('a'),
                Hex('9'),
                "urn:target:action",
                Hex('2'),
                new[] { dependency.Signature });

            Assert.AreEqual(action.Signature, identicalAfterUnrelatedPlanChange.Signature);
            Assert.AreNotEqual(action.Signature, downstreamChanged.Signature);
            Assert.AreNotEqual(action.Signature, selectionChanged.Signature);
            Assert.AreEqual(dependency.Signature, Signature("dependency", '1').Signature);
        }

        [TestMethod]
        public void ActionSignatureUsesVersionedEmptyDigestsAndRejectsNullContractFields()
        {
            var empty = MigrationActionSignature.Create(
                "empty",
                "Test.Action",
                null,
                null,
                "urn:target:empty",
                Hex('a'));

            Assert.AreEqual(MigrationActionSignature.EmptySourceEvidenceDigest, empty.SourceEvidenceDigest);
            Assert.AreEqual(MigrationActionSignature.EmptySelectionReceiptDigest, empty.SelectionReceiptDigest);
            Assert.AreEqual(64, empty.SourceEvidenceDigest.Length);
            Assert.AreEqual(64, empty.SelectionReceiptDigest.Length);

            empty.SourceEvidenceDigest = null;
            empty.Signature = MigrationActionSignature.ComputeSignature(empty);
            Assert.ThrowsException<InvalidDataException>(() => MigrationActionSignature.Validate(empty));

            var missingSelection = Signature("missing-selection", 'b');
            missingSelection.SelectionReceiptDigest = null;
            missingSelection.Signature = MigrationActionSignature.ComputeSignature(missingSelection);
            Assert.ThrowsException<InvalidDataException>(() => MigrationActionSignature.Validate(missingSelection));
        }

        [TestMethod]
        public void JsonLinesJournalWritesTypedRecordsAndCasReference()
        {
            var path = JournalPath();
            var operationId = Guid.NewGuid();
            var signature = Signature("action", '4');
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteExecutionState(State(operationId));
                journal.WriteIntent(Intent(operationId, signature, 0));
                journal.WriteReceipt(Receipt(operationId, signature, 0, MutationOutcome.Applied));
                journal.WriteVerification(Verification(operationId, signature));
                journal.WriteArtifactReference(new MigrationExecutionArtifactReference
                {
                    OperationId = operationId,
                    PlanDigest = PlanDigest,
                    ActionId = signature.ActionId,
                    ActionSignature = signature.Signature,
                    WrittenAtUtc = DateTimeOffset.UtcNow,
                    ArtifactKind = MigrationExecutionArtifactKind.MaterializationReceipt,
                    ArtifactSchemaVersion = "receipt/v1",
                    Sha256 = Hex('e'),
                    Length = 128,
                    MediaType = "application/json"
                });
            }

            var read = MigrationExecutionJournalReader.Read(path);

            Assert.AreEqual(5, read.Records.Count);
            Assert.IsFalse(read.HasInterruptedTail);
            Assert.IsTrue(read.Records.Skip(1).All(value => !string.IsNullOrWhiteSpace(value.PreviousRecordDigest)));
            Assert.AreEqual(MigrationExecutionJournalRecordKind.ArtifactReference, read.Records.Last().RecordKind);
            Assert.IsNull(typeof(MigrationExecutionArtifactReference).GetProperty("PayloadJson"));
        }

        [TestMethod]
        public void InterruptedTailContinuesInChainedSegmentWithoutTruncation()
        {
            var path = JournalPath();
            var firstOperation = Guid.NewGuid();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteExecutionState(State(firstOperation));
            }
            File.AppendAllText(path, "{\"partial\":");
            var originalLength = new FileInfo(path).Length;
            var signature = Signature("continued-action", '5');
            using (var recovered = new JsonLinesMigrationExecutionJournal(path))
            {
                Assert.AreEqual(1, recovered.ActiveSegmentIndex);
                recovered.WriteIntent(Intent(Guid.NewGuid(), signature, 0));
            }

            var read = MigrationExecutionJournalReader.Read(path);

            Assert.AreEqual(originalLength, new FileInfo(path).Length);
            Assert.AreEqual(2, read.Records.Count);
            Assert.AreEqual(1, read.InterruptedTails.Count);
            Assert.AreEqual(1, read.Records[1].JournalSequence);
            Assert.AreEqual(read.Records[0].RecordDigest, read.Records[1].PreviousRecordDigest);
            Assert.IsTrue(File.Exists(MigrationExecutionJournalReader.SegmentPath(path, 1)));
        }

        [TestMethod]
        public void ExtensionlessInterruptedTailDiscoversContinuationSegment()
        {
            var path = JournalPath("journal");
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteExecutionState(State(Guid.NewGuid()));
            }
            File.AppendAllText(path, "{\"partial\":");
            var signature = Signature("extensionless-action", 'c');
            using (var recovered = new JsonLinesMigrationExecutionJournal(path))
            {
                Assert.AreEqual(path + ".segment-000001", recovered.ActiveSegmentPath);
                recovered.WriteIntent(Intent(Guid.NewGuid(), signature, 0));
            }

            var read = MigrationExecutionJournalReader.Read(path);

            Assert.AreEqual(2, read.Records.Count);
            Assert.AreEqual(1, read.InterruptedTails.Count);
            Assert.AreEqual(read.Records[0].RecordDigest, read.Records[1].PreviousRecordDigest);
        }

        [TestMethod]
        public void ReaderRejectsDigestTamperingAndSecondWriter()
        {
            var path = JournalPath();
            var first = new JsonLinesMigrationExecutionJournal(path);
            first.WriteExecutionState(State(Guid.NewGuid()));
            Assert.ThrowsException<IOException>(() => new JsonLinesMigrationExecutionJournal(path));
            first.Dispose();

            var json = File.ReadAllText(path);
            File.WriteAllText(path, json.Replace("started", "altered"));
            Assert.ThrowsException<InvalidDataException>(() => MigrationExecutionJournalReader.Read(path));
        }

        [TestMethod]
        public void ReaderRejectsUnknownNestedPayloadProperties()
        {
            var path = JournalPath();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteExecutionState(State(Guid.NewGuid()));
            }

            var json = File.ReadAllText(path);
            Assert.IsTrue(json.Contains("\"message\":\"started\"", StringComparison.Ordinal));
            File.WriteAllText(
                path,
                json.Replace(
                    "\"message\":\"started\"",
                    "\"message\":\"started\",\"unexpected\":\"hidden\""));

            Assert.ThrowsException<InvalidDataException>(() => MigrationExecutionJournalReader.Read(path));
        }

        [TestMethod]
        public void JournalRejectsOperationPlanRebinding()
        {
            var path = JournalPath();
            var operationId = Guid.NewGuid();
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteExecutionState(State(operationId));
                var signature = Signature("action", '5');
                var intent = Intent(operationId, signature, 0);
                intent.PlanDigest = Hex('e');

                Assert.ThrowsException<InvalidDataException>(() => journal.WriteIntent(intent));
            }

            Assert.AreEqual(1, MigrationExecutionJournalReader.Read(path).Records.Count);
        }

        [TestMethod]
        public void LegacyJournalRequiresNullRatherThanBlankActionSignature()
        {
            var path = JournalPath();
            var intent = new MigrationMutationIntent
            {
                OperationId = Guid.NewGuid(),
                PlanDigest = PlanDigest,
                ActionId = "legacy",
                ActionSignature = string.Empty,
                Sequence = 0,
                WrittenAtUtc = DateTimeOffset.UtcNow,
                Description = "invalid blank legacy signature"
            };
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                Assert.ThrowsException<InvalidDataException>(() => journal.WriteIntent(intent));
            }
        }

        [TestMethod]
        public void ResumeAlwaysFreshProbesAndNeverUsesOldSignatureAsAuthority()
        {
            var path = JournalPath();
            var oldSignature = Signature("action", '6');
            var currentSignature = Signature("action", '7');
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                var legacyOperation = Guid.NewGuid();
                journal.WriteIntent(new MigrationMutationIntent
                {
                    OperationId = legacyOperation,
                    PlanDigest = PlanDigest,
                    ActionId = "action",
                    Sequence = 0,
                    WrittenAtUtc = DateTimeOffset.UtcNow,
                    Description = "legacy null-signature intent"
                });
                journal.WriteReceipt(new MigrationMutationReceipt
                {
                    OperationId = legacyOperation,
                    PlanDigest = PlanDigest,
                    ActionId = "action",
                    Sequence = 0,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Outcome = MutationOutcome.Applied,
                    Message = "legacy null-signature receipt"
                });
                journal.WriteIntent(Intent(Guid.NewGuid(), oldSignature, 0));
                journal.WriteIntent(Intent(Guid.NewGuid(), currentSignature, 0));
            }
            var calls = 0;

            var decision = MigrationResumeCoordinator.Evaluate(
                path,
                Request(currentSignature),
                () =>
                {
                    calls++;
                    return ExactProbe(currentSignature);
                });

            Assert.AreEqual(1, calls);
            Assert.AreEqual(MigrationResumeDisposition.AlreadySatisfied, decision.Disposition);
            Assert.IsTrue(decision.PriorSealedEvidenceFound);

            var unseen = Signature("unseen", '8');
            var noPrior = MigrationResumeCoordinator.Evaluate(
                path,
                Request(unseen),
                () => ExactProbe(unseen));
            Assert.AreEqual(MigrationResumeDisposition.Pending, noPrior.Disposition);
            Assert.IsFalse(noPrior.PriorSealedEvidenceFound);
        }

        [TestMethod]
        public void ResumeDoesNotBlindReplayAbsentDriftedOrUnavailableTargets()
        {
            var path = JournalPath();
            var signature = Signature("action", '9');
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(Guid.NewGuid(), signature, 0));
            }
            Assert.AreEqual(
                MigrationResumeDisposition.Pending,
                MigrationResumeCoordinator.Evaluate(path, Request(signature), () => new MigrationFreshProbeResult
                {
                    State = MigrationFreshProbeState.Absent
                }).Disposition);
            Assert.AreEqual(
                MigrationResumeDisposition.ReplanAndReapprove,
                MigrationResumeCoordinator.Evaluate(path, Request(signature), () => new MigrationFreshProbeResult
                {
                    State = MigrationFreshProbeState.Drifted
                }).Disposition);
            Assert.AreEqual(
                MigrationResumeDisposition.TargetProbeUnavailable,
                MigrationResumeCoordinator.Evaluate(path, Request(signature), () => new MigrationFreshProbeResult
                {
                    State = MigrationFreshProbeState.Unavailable
                }).Disposition);
        }

        [TestMethod]
        public void ResumeRejectsMutatedJournalBeforeFreshProbe()
        {
            var path = JournalPath();
            var signature = Signature("action", 'd');
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                journal.WriteIntent(Intent(Guid.NewGuid(), signature, 0));
            }
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace("\"description\":\"intent\"", "\"description\":\"mutated\""));
            var calls = 0;

            Assert.ThrowsException<InvalidDataException>(() => MigrationResumeCoordinator.Evaluate(
                path,
                Request(signature),
                () =>
                {
                    calls++;
                    return ExactProbe(signature);
                }));
            Assert.AreEqual(0, calls);
        }

        [TestMethod]
        public void ResumeRejectsForgedStreamAndHasNoUnvalidatedResultOverload()
        {
            var signature = Signature("action", 'e');
            var calls = 0;
            using (var forged = new MemoryStream(Encoding.UTF8.GetBytes("{}\n")))
            {
                Assert.ThrowsException<InvalidDataException>(() => MigrationResumeCoordinator.Evaluate(
                    forged,
                    Request(signature),
                    () =>
                    {
                        calls++;
                        return ExactProbe(signature);
                    }));
            }

            Assert.AreEqual(0, calls);
            Assert.IsFalse(typeof(MigrationResumeCoordinator)
                .GetMethods()
                .Any(method => method.GetParameters().Any(parameter =>
                    parameter.ParameterType == typeof(MigrationExecutionJournalReadResult))));
        }

        [TestMethod]
        public void TaxonomyOwnedMappingMarkerIsStableAndConflictIsNeverOverwritten()
        {
            var setPlan = TaxonomyAssetIdentity.CreateTermSetPlan(
                new TaxonomyTermSetSourceSnapshot
                {
                    SourceTenantId = Guid.NewGuid(),
                    SourceTermStoreId = Guid.NewGuid(),
                    SourceTermSetId = Guid.NewGuid(),
                    Name = "Wiki Categories",
                    Language = 1033,
                    IsOpenForTermCreation = true,
                    IsAvailableForTagging = true,
                    EvidenceSha256 = Hex('a')
                },
                Guid.NewGuid());
            var originalMapping = setPlan.MappingDigest;
            setPlan.TargetTermSetName = "Display name changed without remapping";

            Assert.AreEqual(originalMapping, TaxonomyAssetIdentity.ComputeMappingDigest(setPlan));
            Assert.AreEqual(TaxonomyAssetIdentity.MappingDigestPropertyName, setPlan.MappingDigestPropertyName);
            Assert.AreEqual(
                TaxonomyOwnedProvenanceState.MappingDigestMissing,
                TaxonomyOwnedProvenance.Evaluate(setPlan.OriginalIdentifier, setPlan.OriginalIdentifier, null, setPlan.MappingDigest));

            var properties = new Dictionary<string, string>
            {
                [TaxonomyAssetIdentity.MappingDigestPropertyName] = Hex('b')
            };
            Assert.ThrowsException<InvalidOperationException>(() =>
                TaxonomyAssetCsomMaterializer.AssertMapping(
                    properties,
                    TaxonomyAssetIdentity.MappingDigestPropertyName,
                    setPlan.MappingDigest,
                    "TermSet"));
            Assert.AreEqual(Hex('b'), properties[TaxonomyAssetIdentity.MappingDigestPropertyName]);
        }

        [TestMethod]
        public void TaxonomyExternalReceiptRemainsExternalAndMutationFree()
        {
            var setPlan = TaxonomyAssetIdentity.CreateTermSetPlan(
                new TaxonomyTermSetSourceSnapshot
                {
                    SourceTenantId = Guid.NewGuid(),
                    SourceTermStoreId = Guid.NewGuid(),
                    SourceTermSetId = Guid.NewGuid(),
                    Name = "External Categories",
                    Language = 1033,
                    IsOpenForTermCreation = true,
                    IsAvailableForTagging = true,
                    EvidenceSha256 = Hex('c')
                },
                Guid.NewGuid());
            var action = new TaxonomyAssetActionApproval
            {
                ActionId = TaxonomyAssetApprovalFactory.TermSetActionId(setPlan.Source.TermStoreId, setPlan.Source.TermSetId),
                Kind = TaxonomyAssetKind.TermSet,
                SourceTenantId = setPlan.Source.TenantId,
                SourceTermStoreId = setPlan.Source.TermStoreId,
                SourceTermSetId = setPlan.Source.TermSetId,
                TargetTermStoreId = setPlan.TargetTermStoreId,
                TargetTermSetId = setPlan.PreferredTargetTermSetId,
                ReviewedDisposition = TaxonomyAssetTargetDisposition.ReviewExternalReuse,
                Decision = TaxonomyAssetApprovalDecision.Approve,
                RequiresExplicitReview = true
            };
            var review = new TaxonomyAssetReviewPlan
            {
                TargetTermStoreId = setPlan.TargetTermStoreId,
                TermSets = new List<TaxonomyTermSetMaterializationPlan> { setPlan }
            };
            var approval = new TaxonomyAssetApprovalManifest
            {
                Actions = new List<TaxonomyAssetActionApproval> { action }
            };
            var signature = TaxonomyAssetReceiptIdentity.CreateActionSignatures(review, approval)[action.ActionId];
            var receipt = new TaxonomyAssetActionReceipt { ChangedTarget = false };

            TaxonomyAssetReceiptIdentity.Populate(receipt, action, signature, signature.SemanticDigest);

            Assert.AreEqual(MigrationTargetOwnership.External, receipt.Ownership);
            Assert.AreEqual(TaxonomyAssetReceiptDisposition.ReuseExternal, receipt.ExecutionDisposition);
            Assert.IsFalse(receipt.ChangedTarget);
            Assert.IsFalse(receipt.TargetIdentity.Contains("original_identifier", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void DanglingTaxonomyValueIsNotProjectedAsLiveAssetCreation()
        {
            var storeId = Guid.NewGuid();
            var setId = Guid.NewGuid();
            var danglingTermId = Guid.NewGuid();
            var snapshot = new PublishingPageCaptureBundle
            {
                Fields = new List<PageFieldValueSnapshot>
                {
                    new PageFieldValueSnapshot
                    {
                        Id = Guid.NewGuid(),
                        InternalName = "Categories",
                        TaxonomyBinding = new TaxonomyFieldRelationshipBindingSnapshot
                        {
                            TermStoreId = storeId,
                            BoundTermSetId = setId
                        },
                        TaxonomyValues = new List<PageTaxonomyValueSnapshot>
                        {
                            new PageTaxonomyValueSnapshot
                            {
                                TermGuid = danglingTermId.ToString("D"),
                                WssId = 42,
                                Relationship = new TaxonomyValueRelationshipSnapshot
                                {
                                    State = TaxonomyRelationshipState.DanglingTermAbsent
                                }
                            }
                        }
                    }
                }
            };

            var request = PublishingPageTaxonomyAssetRequirementCollector.Collect(new[] { snapshot }).Single();

            Assert.AreEqual(setId, request.SourceTermSetId);
            Assert.IsFalse(request.RequiredTermIds.Contains(danglingTermId));
        }

        private string JournalPath(string fileName = "journal.jsonl")
        {
            var directory = Path.Combine(Path.GetTempPath(), "pnp-action-journal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            temporaryDirectories.Add(directory);
            return Path.Combine(directory, fileName);
        }

        private static MigrationActionSignature Signature(string actionId, char semantic, params string[] dependencies)
        {
            return MigrationActionSignature.Create(
                actionId,
                "Test.Action",
                Hex('a'),
                Hex('b'),
                "urn:target:" + actionId,
                Hex(semantic),
                dependencies);
        }

        private static MigrationExecutionStateReceipt State(Guid operationId)
        {
            return new MigrationExecutionStateReceipt
            {
                OperationId = operationId,
                PlanDigest = PlanDigest,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Status = MigrationExecutionStatus.Running,
                Message = "started"
            };
        }

        private static MigrationMutationIntent Intent(Guid operationId, MigrationActionSignature signature, int sequence)
        {
            return new MigrationMutationIntent
            {
                OperationId = operationId,
                PlanDigest = PlanDigest,
                ActionId = signature.ActionId,
                ActionSignature = signature.Signature,
                Sequence = sequence,
                WrittenAtUtc = DateTimeOffset.UtcNow,
                Description = "intent"
            };
        }

        private static MigrationMutationReceipt Receipt(
            Guid operationId,
            MigrationActionSignature signature,
            int sequence,
            MutationOutcome outcome)
        {
            return new MigrationMutationReceipt
            {
                OperationId = operationId,
                PlanDigest = PlanDigest,
                ActionId = signature.ActionId,
                ActionSignature = signature.Signature,
                Sequence = sequence,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Outcome = outcome,
                Message = "receipt"
            };
        }

        private static MigrationMutationVerificationReceipt Verification(Guid operationId, MigrationActionSignature signature)
        {
            return new MigrationMutationVerificationReceipt
            {
                OperationId = operationId,
                PlanDigest = PlanDigest,
                ActionId = signature.ActionId,
                ActionSignature = signature.Signature,
                VerifiedAtUtc = DateTimeOffset.UtcNow,
                FreshReadbackPassed = true,
                ObservedStateDigest = signature.SemanticDigest,
                Ownership = MigrationTargetOwnership.MigrationOwned,
                TargetIdentityDigest = signature.TargetIdentityDigest,
                ProvenanceMatched = true,
                Message = "verified"
            };
        }

        private static MigrationResumeRequest Request(MigrationActionSignature signature)
        {
            return new MigrationResumeRequest
            {
                Action = signature,
                ExpectedOwnership = MigrationTargetOwnership.MigrationOwned
            };
        }

        private static MigrationFreshProbeResult ExactProbe(MigrationActionSignature signature)
        {
            return new MigrationFreshProbeResult
            {
                State = MigrationFreshProbeState.Exact,
                Ownership = MigrationTargetOwnership.MigrationOwned,
                ProvenanceMatched = true,
                ObservedStateDigest = signature.SemanticDigest,
                TargetIdentityDigest = signature.TargetIdentityDigest
            };
        }

        private static string Hex(char value) => new string(value, 64);

        private static string PlanDigest => Hex('f');
    }
}
