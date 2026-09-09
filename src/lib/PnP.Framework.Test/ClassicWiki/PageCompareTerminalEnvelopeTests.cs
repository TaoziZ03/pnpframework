using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class PageCompareTerminalEnvelopeTests
    {
        private const string ImplementationRef = "d7a4f154da0441b2d6e0f0e0cd03f58fa0a58431";

        [TestMethod]
        public void ThreeTerminalStatesSerializeDeserializeConsumeAndReconstructLosslessly()
        {
            var store = new RuntimeArtifactStore();
            var executed = CreateExecutedWikiCase(store);
            var denied = CreateDeniedCase();
            var unsupported = CreateUnsupportedCase();
            var envelope = PageCompareTerminalEnvelopeFactory.Create(
                new DateTimeOffset(2026, 9, 9, 19, 30, 0, TimeSpan.Zero),
                Array.Empty<PublishingPageCompareReport>(),
                new[] { denied, executed, unsupported });

            var json = PageCompareTerminalEnvelopeSerializer.SerializeCanonical(
                envelope,
                ImplementationRef,
                store);
            var roundTrip = PageCompareTerminalEnvelopeSerializer.DeserializeStrict(
                json,
                ImplementationRef,
                store);
            var projection = PageCompareTerminalConsumerAdapter.Consume(
                roundTrip,
                ImplementationRef,
                store);
            var reconstructed = PageCompareTerminalConsumerAdapter.Reconstruct(
                projection,
                ImplementationRef,
                store);

            CollectionAssert.AreEqual(
                new[] { "ccd35-03", "ccd35-08", "ccd35-06" },
                projection.Cases.Select(value => value.CaseId).ToArray(),
                "The consumer changed terminal case order.");
            Assert.AreEqual(PageCompareTerminalContract.SchemaVersion, roundTrip.SchemaVersion);
            Assert.AreEqual(
                "74ef862c2bfe67a49cc267eb3c632333fe8fbed07c01349dc2ed9f2c8ccb283e",
                envelope.EnvelopeDigestSha256);
            Assert.AreEqual(envelope.EnvelopeDigestSha256, reconstructed.EnvelopeDigestSha256);
            Assert.AreEqual(
                MigrationContractSerializer.SerializeCanonical(envelope),
                MigrationContractSerializer.SerializeCanonical(reconstructed));
            Assert.AreEqual("conditional", projection.Cases[0].AcceptanceVerdict);
            Assert.AreEqual(0, projection.Cases[0].CanonicalMaterialRows.Count);
            Assert.AreEqual("pass", projection.Cases[1].AcceptanceVerdict);
            Assert.AreEqual(1, projection.Cases[1].CanonicalMaterialRows.Count);
            Assert.AreEqual("unverified", projection.Cases[2].AcceptanceVerdict);
            Assert.AreEqual(0, projection.Cases[2].CanonicalMaterialRows.Count);
            Assert.AreEqual(
                ClassicWikiPackageContract.MigrationSchemaVersion,
                projection.Cases[1].Terminal.MigrationPackageSchemaVersion);
            Assert.IsFalse(json.Contains("pnp-classic-wiki-migration-package/v1"));
        }

        [TestMethod]
        public void UnknownFieldVersionApproximatePackageAndInformationLossFailClosed()
        {
            var store = new RuntimeArtifactStore();
            var envelope = CreateEnvelope(store);
            var json = PageCompareTerminalEnvelopeSerializer.SerializeCanonical(envelope, ImplementationRef, store);
            var unknown = json.Replace(
                "\"generatedAtUtc\"",
                "\"futureField\":true,\"generatedAtUtc\"");
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeSerializer.DeserializeStrict(unknown, ImplementationRef, store));
            var nestedUnknown = json.Replace(
                "\"nativeReceipt\":{",
                "\"nativeReceipt\":{\"futureNativeField\":true,");
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeSerializer.DeserializeStrict(nestedUnknown, ImplementationRef, store));

            var wrongVersion = Clone(envelope);
            wrongVersion.SchemaVersion = "pnp-page-compare-terminal-envelope/v2";
            Reseal(wrongVersion);
            AssertRejected(wrongVersion, store);

            var approximatePackage = Clone(envelope);
            approximatePackage.Cases.Single(value => value.CaseId == "ccd35-08").MigrationPackageSchemaVersion =
                "pnp-classic-wiki-migration-package/v1";
            Reseal(approximatePackage);
            AssertRejected(approximatePackage, store);

            var unsafePackagePath = Clone(envelope);
            unsafePackagePath.Cases.Single(value => value.CaseId == "ccd35-08").MigrationPackageArtifactLocator =
                "../foreign/package.json";
            Reseal(unsafePackagePath);
            AssertRejected(unsafePackagePath, store);

            var foreignPackageBytes = Clone(envelope);
            var foreignArtifact = store.Add("{\"schemaVersion\":\"pnp-classic-wiki-migration-package/v1\"}");
            var foreignPackageCase = foreignPackageBytes.Cases.Single(value => value.CaseId == "ccd35-08");
            foreignPackageCase.MigrationPackageArtifactDigestSha256 = foreignArtifact.Digest;
            foreignPackageCase.MigrationPackageArtifactLength = foreignArtifact.Length;
            Reseal(foreignPackageBytes);
            AssertRejected(foreignPackageBytes, store);

            var projection = PageCompareTerminalConsumerAdapter.Consume(envelope, ImplementationRef, store);
            projection.Cases[0].Terminal.ReasonCode = "CHANGED_AFTER_ADAPTER";
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalConsumerAdapter.Reconstruct(projection, ImplementationRef, store));
        }

        [TestMethod]
        public void DeniedAndUnsupportedCannotCarrySuccessShapedTargetOrOperationEvidence()
        {
            var store = new RuntimeArtifactStore();
            var deniedWithOperations = CreateEnvelope(store);
            deniedWithOperations.Cases.Single(value => value.CaseId == "ccd35-03").Operations = CreateOperations();
            Reseal(deniedWithOperations);
            AssertRejected(deniedWithOperations, store);

            var deniedWithActual = CreateEnvelope(store);
            var deniedIngredient = deniedWithActual.Cases.Single(value => value.CaseId == "ccd35-03").Ingredients.Single();
            deniedIngredient.ActualEvidenceDigestSha256 = Hash("invented-target-evidence");
            deniedIngredient.Actual.RawDigestSha256 = Hash("invented-target-bytes");
            Reseal(deniedWithActual);
            AssertRejected(deniedWithActual, store);

            var unsupportedPassed = CreateEnvelope(store);
            unsupportedPassed.Cases.Single(value => value.CaseId == "ccd35-06").RuntimeStatus =
                PageCompareTerminalContract.Statuses.Passed;
            Reseal(unsupportedPassed);
            AssertRejected(unsupportedPassed, store);
        }

        [TestMethod]
        public void ExecutedWikiRejectsSourceAsActualForeignRuntimeAndFalseCompleteReconcile()
        {
            var store = new RuntimeArtifactStore();
            var sourceAsActual = CreateEnvelope(store);
            var executedIngredient = sourceAsActual.Cases.Single(value => value.CaseId == "ccd35-08").Ingredients.Single();
            executedIngredient.ActualEvidenceDigestSha256 = executedIngredient.SourceEvidenceDigestSha256;
            Reseal(sourceAsActual);
            AssertRejected(sourceAsActual, store);

            var foreignRuntime = CreateEnvelope(store);
            var foreignCase = foreignRuntime.Cases.Single(value => value.CaseId == "ccd35-08");
            foreignCase.RuntimeReceipt.ImplementationRef = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            foreignCase.RuntimeReceipt.Results[0].ImplementationRef = foreignCase.RuntimeReceipt.ImplementationRef;
            foreignCase.RuntimeReceiptDigestSha256 = ContractDigest(foreignCase.RuntimeReceipt);
            Reseal(foreignRuntime);
            AssertRejected(foreignRuntime, store);

            var falseComplete = CreateEnvelope(store);
            falseComplete.Cases.Single(value => value.CaseId == "ccd35-08").ReconcileStatus =
                PageCompareTerminalContract.Statuses.NotExecuted;
            Reseal(falseComplete);
            AssertRejected(falseComplete, store);
        }

        [TestMethod]
        public void CanonicalV1StrictReaderRejectsNegativeCompatibilityMatrix()
        {
            var valid = CreateCanonicalReport();
            PublishingPageCompareReportValidator.Validate(valid, ImplementationRef);

            var unknownJson = MigrationContractSerializer.SerializeCanonical(valid).Replace(
                "\"generatedAtUtc\"",
                "\"futureField\":true,\"generatedAtUtc\"");
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReportValidator.DeserializeStrict(unknownJson, ImplementationRef));

            AssertCanonicalRejected(valid, value => value.SchemaVersion = "pnp-page-compare-report/v2");
            AssertCanonicalRejected(valid, value => value.Runtime.Status = "unknown");
            AssertCanonicalRejected(valid, value => value.Acceptance.RuntimeStatus = "unknown");
            AssertCanonicalRejected(valid, value => value.Ingredients[0].Lineage.EvidenceRefs.Clear());
            AssertCanonicalRejected(valid, value => value.Ingredients.Add(Clone(value.Ingredients[0])));
            AssertCanonicalRejected(valid, value => value.Ingredients[0].Lineage.CauseIngredientIds.Add("orphan"));
            AssertCanonicalRejected(valid, value =>
            {
                value.Ingredients[0].Actual.RawDigestSha256 = null;
                value.Ingredients[0].Actual.CanonicalDigestSha256 = null;
            });
            AssertCanonicalRejected(valid, value => value.Producer.ImplementationRef =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            AssertCanonicalRejected(valid, value => value.Ingredients[0].Lineage.EvidenceRefs =
                new List<string> { value.Ingredients[0].Lineage.EvidenceRefs[0] });
            AssertCanonicalRejected(valid, value => value.Bindings.Operations.RuntimeOperationId = Guid.Empty);
            AssertCanonicalRejected(valid, value => value.Bindings.MigrationPackageSchemaVersion =
                ClassicWikiPackageContract.MigrationSchemaVersion);
        }

        [TestMethod]
        public void TerminalV2RoundTripsMutationStartedAdverseDeniedAndUnsupportedCasesLosslessly()
        {
            var store = new RuntimeArtifactStore();
            var envelope = CreateEnvelopeV2(store);

            var json = PageCompareTerminalEnvelopeSerializerV2.SerializeCanonical(envelope, ImplementationRef, store);
            var roundTrip = PageCompareTerminalEnvelopeSerializerV2.DeserializeStrict(json, ImplementationRef, store);
            var projection = PageCompareTerminalConsumerAdapterV2.Consume(roundTrip, ImplementationRef, store);
            var projectionJson = PageCompareTerminalConsumerAdapterV2.SerializeProjectionCanonical(
                projection,
                ImplementationRef,
                store);
            var transported = PageCompareTerminalConsumerAdapterV2.DeserializeProjectionStrict(
                projectionJson,
                ImplementationRef,
                store);
            var reconstructed = PageCompareTerminalConsumerAdapterV2.Reconstruct(transported, ImplementationRef, store);

            CollectionAssert.AreEqual(
                new[] { "ccd35-03", "ccd35-08", "ccd35-06" },
                projection.Cases.Select(value => value.CaseId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "conditional", "fail", "unverified" },
                projection.Cases.Select(value => value.AcceptanceVerdict).ToArray());
            Assert.AreEqual(0, projection.Cases[1].CanonicalMaterialRows.Count);
            Assert.AreEqual(PageCompareTerminalContractV2.TerminalKinds.NativeExecutionAdverse,
                projection.Cases[1].Terminal.Terminal.TerminalKind);
            Assert.AreEqual(MigrationExecutionStatus.FailedUnexpectedly,
                projection.Cases[1].Terminal.Terminal.NativeReceipt.ClassicWikiReceipt.ExecutionStatus);
            Assert.IsTrue(projection.Cases[1].Terminal.Terminal.NativeReceipt.ClassicWikiReceipt.MutationStarted);
            Assert.IsTrue(projection.Cases[1].Terminal.Terminal.NativeReceipt.ClassicWikiReceipt.PartialExecution);
            Assert.AreEqual(4, projection.Cases[1].Terminal.Terminal.NativeReceipt.ClassicWikiReceipt.Steps.Count);
            Assert.AreEqual(RuntimeVerificationStatus.Pending,
                projection.Cases[1].Terminal.Terminal.NativeReceipt.ClassicWikiReceipt.RuntimeVerificationStatus);
            Assert.AreEqual(
                envelope.Cases[1].CleanupReceipt.ReceiptJson,
                reconstructed.Cases[1].CleanupReceipt.ReceiptJson);
            var adverse = roundTrip.Cases[1].Terminal;
            Assert.ThrowsException<InvalidDataException>(() =>
                NativePageImportReceiptAggregateValidator.ValidateForCompare(
                    adverse.NativeReceipt,
                    adverse.AdmittedPlan,
                    adverse.AdmittedPlanDigestSha256));
            var adverseBinding = NativePageImportReceiptAggregateValidator.ValidateForTerminalEvidence(
                adverse.NativeReceipt,
                adverse.AdmittedPlan,
                adverse.AdmittedPlanDigestSha256);
            Assert.AreEqual(MigrationExecutionStatus.FailedUnexpectedly, adverseBinding.ExecutionStatus);
            Assert.AreEqual(envelope.EnvelopeDigestSha256, reconstructed.EnvelopeDigestSha256);
            Assert.AreEqual(MigrationContractSerializer.SerializeCanonical(envelope),
                MigrationContractSerializer.SerializeCanonical(reconstructed));
            Assert.AreEqual("pnp-page-compare-terminal-envelope/v1", PageCompareTerminalContract.SchemaVersion);
            Assert.AreEqual("pnp-page-compare-consumer-projection/v1", PageCompareTerminalContract.ConsumerProjectionSchemaVersion);
        }

        [TestMethod]
        public void TerminalV2MaterialPendingUnknownAndSourceDriftCannotPassAndFalseExactFailsClosed()
        {
            foreach (var result in new[]
            {
                PublishingPageCompareContract.ResultClasses.RuntimePending,
                PublishingPageCompareContract.ResultClasses.Unknown,
                PublishingPageCompareContract.ResultClasses.SourceVersionChanged
            })
            {
                var store = new RuntimeArtifactStore();
                var envelope = CreateSuccessfulEnvelopeV2(store);
                envelope.Cases[0].Terminal.Ingredients[0].Result = result;
                Reseal(envelope);
                var projection = PageCompareTerminalConsumerAdapterV2.Consume(envelope, ImplementationRef, store);
                Assert.AreEqual("unverified", projection.Cases[0].AcceptanceVerdict, result);
            }

            var mismatchStore = new RuntimeArtifactStore();
            var mismatch = CreateSuccessfulEnvelopeV2(mismatchStore);
            mismatch.Cases[0].Terminal.Ingredients[0].Actual.RawDigestSha256 = Hash("different-actual-raw");
            mismatch.Cases[0].Terminal.Ingredients[0].Actual.CanonicalDigestSha256 = Hash("different-actual-canonical");
            Reseal(mismatch);
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeValidatorV2.Validate(mismatch, ImplementationRef, mismatchStore));
        }

        [TestMethod]
        public void TerminalV2RejectsUnprovedTransformAndNonApplicabilityClassifications()
        {
            foreach (var classification in new[]
            {
                new
                {
                    Result = PublishingPageCompareContract.ResultClasses.TransformedAsPlanned,
                    Reason = PublishingPageCompareContract.ReasonCodes.ApprovedTransformMatched
                },
                new
                {
                    Result = PublishingPageCompareContract.ResultClasses.NotApplicable,
                    Reason = PublishingPageCompareContract.ReasonCodes.AssertionNotApplicable
                }
            })
            {
                var store = new RuntimeArtifactStore();
                var envelope = CreateSuccessfulEnvelopeV2(store);
                var ingredient = envelope.Cases[0].Terminal.Ingredients[0];
                ingredient.Actual.RawDigestSha256 = Hash("different-actual-raw:" + classification.Result);
                ingredient.Actual.CanonicalDigestSha256 = Hash("different-actual-canonical:" + classification.Result);
                ingredient.Result = classification.Result;
                ingredient.ReasonCode = classification.Reason;
                Reseal(envelope);

                Assert.ThrowsException<InvalidDataException>(() =>
                    PageCompareTerminalEnvelopeSerializerV2.SerializeCanonical(envelope, ImplementationRef, store),
                    classification.Result);
            }
        }

        [TestMethod]
        public void TerminalV2AdverseIngredientVocabularyAndDeniedTargetEvidenceFailClosed()
        {
            foreach (var mutation in new Action<PageCompareTerminalIngredient>[]
            {
                ingredient => ingredient.Result = "future-result",
                ingredient => ingredient.ExecutionStatus = "future-execution",
                ingredient => ingredient.Availability = "future-availability",
                ingredient => ingredient.Disposition = "future-disposition",
                ingredient =>
                {
                    ingredient.Availability = PageCompareTerminalContract.Availability.AccessDenied;
                    ingredient.SemanticAccessDenied = true;
                    ingredient.HttpStatus = 403;
                    ingredient.AttemptCount = 1;
                    ingredient.Disposition = PageCompareTerminalContract.Dispositions.Delegate;
                    ingredient.Result = PageCompareTerminalContract.TerminalResults.AccessDenied;
                    ingredient.ExecutionStatus = PageCompareTerminalContract.Statuses.NotExecuted;
                    ingredient.ReasonCode = PageCompareTerminalContract.ReasonCodes.AccessDeniedSkipped;
                    ingredient.ActualEvidenceDigestSha256 = Hash("invented-denied-target-evidence");
                    ingredient.Actual.RawDigestSha256 = Hash("invented-denied-target-bytes");
                    ingredient.Lineage.TargetIdentity = "target:invented-denied";
                }
            })
            {
                var store = new RuntimeArtifactStore();
                var envelope = CreateEnvelopeV2(store);
                mutation(envelope.Cases.Single(value => value.Terminal.CaseId == "ccd35-08").Terminal.Ingredients[0]);
                Reseal(envelope);

                Assert.ThrowsException<InvalidDataException>(() =>
                    PageCompareTerminalEnvelopeSerializerV2.SerializeCanonical(envelope, ImplementationRef, store));
            }
        }

        [TestMethod]
        public void TerminalV2CleanupReceiptRejectsDuplicateOperationIdentity()
        {
            var store = new RuntimeArtifactStore();
            var envelope = CreateEnvelopeV2(store);
            var cleanup = envelope.Cases.Single(value => value.Terminal.CaseId == "ccd35-08").CleanupReceipt;
            cleanup.ReceiptJson = cleanup.ReceiptJson.Insert(
                1,
                "\"operationId\":\"11111111-1111-1111-1111-111111111111\",");
            cleanup.ReceiptDigestSha256 = MigrationDigest.ComputeSha256(Encoding.UTF8.GetBytes(cleanup.ReceiptJson));
            Reseal(envelope);

            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeSerializerV2.SerializeCanonical(envelope, ImplementationRef, store));
        }

        [TestMethod]
        public void TerminalV2ProjectionPublicFieldTamperingFailsEvenAfterProjectionIsResealed()
        {
            var store = new RuntimeArtifactStore();
            var projection = PageCompareTerminalConsumerAdapterV2.Consume(
                CreateEnvelopeV2(store),
                ImplementationRef,
                store);
            projection.Cases[0].CaseId = "forged-consumer-case";
            projection.Cases[0].TerminalKind = PageCompareTerminalContract.TerminalKinds.WikiNativeExecuted;
            projection.Cases[0].AcceptanceVerdict = "pass";
            projection.Cases[0].CanonicalMaterialRows = Clone(projection.Cases[1].CanonicalMaterialRows);
            projection.Cases[1].CanonicalMaterialRows.Clear();
            projection.ProjectionDigestSha256 = MigrationDigest.ComputeSha256(
                MigrationContractSerializer.SerializeCanonicalWithNullRootProperty(
                    projection,
                    nameof(PageCompareConsumerProjectionV2.ProjectionDigestSha256)));

            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalConsumerAdapterV2.Reconstruct(projection, ImplementationRef, store));
        }

        [TestMethod]
        public void TerminalV2RejectsUnknownFieldsWrongVersionAndCorruptCleanupEvidence()
        {
            var store = new RuntimeArtifactStore();
            var envelope = CreateEnvelopeV2(store);
            var json = PageCompareTerminalEnvelopeSerializerV2.SerializeCanonical(envelope, ImplementationRef, store);
            var unknown = json.Replace("\"generatedAtUtc\"", "\"futureField\":true,\"generatedAtUtc\"");
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeSerializerV2.DeserializeStrict(unknown, ImplementationRef, store));

            var wrongVersion = Clone(envelope);
            wrongVersion.SchemaVersion = "pnp-page-compare-terminal-envelope/v3";
            Reseal(wrongVersion);
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeValidatorV2.Validate(wrongVersion, ImplementationRef, store));

            var corruptCleanup = Clone(envelope);
            corruptCleanup.Cases.Single(value => value.Terminal.CaseId == "ccd35-08").CleanupReceipt.ReceiptJson += " ";
            Reseal(corruptCleanup);
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeValidatorV2.Validate(corruptCleanup, ImplementationRef, store));
        }

        private static PageCompareTerminalEnvelope CreateEnvelope(RuntimeArtifactStore store)
        {
            return PageCompareTerminalEnvelopeFactory.Create(
                new DateTimeOffset(2026, 9, 9, 19, 30, 0, TimeSpan.Zero),
                Array.Empty<PublishingPageCompareReport>(),
                new[] { CreateDeniedCase(), CreateExecutedWikiCase(store), CreateUnsupportedCase() });
        }

        private static PageCompareTerminalEnvelopeV2 CreateEnvelopeV2(RuntimeArtifactStore store)
        {
            return PageCompareTerminalEnvelopeFactoryV2.Create(
                new DateTimeOffset(2026, 9, 9, 19, 30, 0, TimeSpan.Zero),
                Array.Empty<PublishingPageCompareReport>(),
                new[]
                {
                    new PageCompareTerminalCaseV2 { Terminal = CreateDeniedCase() },
                    CreateAdverseWikiCase(store),
                    new PageCompareTerminalCaseV2 { Terminal = CreateUnsupportedCase() }
                });
        }

        private static PageCompareTerminalEnvelopeV2 CreateSuccessfulEnvelopeV2(RuntimeArtifactStore store)
        {
            return PageCompareTerminalEnvelopeFactoryV2.Create(
                new DateTimeOffset(2026, 9, 9, 19, 30, 0, TimeSpan.Zero),
                Array.Empty<PublishingPageCompareReport>(),
                new[] { new PageCompareTerminalCaseV2 { Terminal = CreateExecutedWikiCase(store) } });
        }

        private static PageCompareTerminalCaseV2 CreateAdverseWikiCase(RuntimeArtifactStore store)
        {
            var terminal = CreateExecutedWikiCase(store);
            var receipt = terminal.NativeReceipt.ClassicWikiReceipt;
            receipt.ExecutionStatus = MigrationExecutionStatus.FailedUnexpectedly;
            receipt.PartialExecution = true;
            receipt.FreshReadbackPassed = false;
            receipt.StorageVerificationStatus = StorageVerificationStatus.Failed;
            receipt.RuntimeVerificationStatus = RuntimeVerificationStatus.Pending;
            receipt.DependenciesMatched = false;
            receipt.Diagnostics.Add("Dependency count mismatch: expected 0, observed 5.");
            receipt.Steps = new List<MigrationMutationReceipt>
            {
                CreateAdverseStep(receipt, "folder.ensure", 0, MutationOutcome.AlreadySatisfied, 1),
                CreateAdverseStep(receipt, "page.create", 1, MutationOutcome.Applied, 2),
                CreateAdverseStep(receipt, "wiki-field.exact", 2, MutationOutcome.Applied, 3),
                CreateAdverseStep(receipt, "page.ownership", 3, MutationOutcome.Applied, 4)
            };
            terminal.NativeReceipt = NativePageImportReceiptAggregateFactory.Create(receipt);
            terminal.TerminalKind = PageCompareTerminalContractV2.TerminalKinds.NativeExecutionAdverse;
            terminal.RuntimeManifest = null;
            terminal.RuntimeReceipt = null;
            terminal.RuntimeReceiptDigestSha256 = null;
            terminal.MutationStatus = PageCompareTerminalContract.Statuses.Partial;
            terminal.ReadbackStatus = PageCompareTerminalContractV2.Statuses.Failed;
            terminal.RuntimeStatus = PageCompareTerminalContractV2.Statuses.Pending;
            terminal.ReconcileStatus = PageCompareTerminalContract.Statuses.Partial;
            terminal.ReasonCode = PageCompareTerminalContractV2.ReasonCodes.NativeExecutionFailed;
            var ingredient = terminal.Ingredients.Single();
            ingredient.ExecutionStatus = PageCompareTerminalContract.Statuses.Partial;
            ingredient.Result = PublishingPageCompareContract.ResultClasses.Unknown;
            ingredient.ReasonCode = PageCompareTerminalContractV2.ReasonCodes.NativeExecutionFailed;
            ingredient.ActualEvidenceDigestSha256 = null;
            ingredient.Actual = new CompareDigestPair();
            ingredient.Lineage.TargetIdentity = null;
            ingredient.Lineage.EvidenceRefs = new List<string> { "sha256:" + ingredient.SourceEvidenceDigestSha256 };
            terminal.DependentObligations = new List<PageCompareDependentObligation>
            {
                new PageCompareDependentObligation
                {
                    ObligationId = "obligation:page-artifact:readback",
                    IngredientId = ingredient.IngredientId,
                    Required = true,
                    Status = PageCompareTerminalContract.ObligationStatuses.Unverified,
                    ReasonCode = "DEPENDENCY_COUNT_MISMATCH"
                },
                new PageCompareDependentObligation
                {
                    ObligationId = "obligation:page-artifact:runtime",
                    IngredientId = ingredient.IngredientId,
                    Required = true,
                    Status = PageCompareTerminalContract.ObligationStatuses.NotExecuted,
                    ReasonCode = PageCompareTerminalContractV2.ReasonCodes.NativeExecutionFailed
                },
                new PageCompareDependentObligation
                {
                    ObligationId = "obligation:page-artifact:cleanup",
                    IngredientId = ingredient.IngredientId,
                    Required = true,
                    Status = PageCompareTerminalContract.ObligationStatuses.Satisfied,
                    ReasonCode = "OWNERSHIP_GUARDED_CLEANUP_OBSERVED"
                }
            };
            ingredient.DependentObligationIds = terminal.DependentObligations.Select(value => value.ObligationId).ToList();

            var cleanupJson = "{\"schema\":\"ccd243.ownership-cleanup/v1\",\"operationId\":\""
                + terminal.Operations.CleanupOperationId.ToString("D")
                + "\",\"verdict\":\"pass\"}";
            return new PageCompareTerminalCaseV2
            {
                Terminal = terminal,
                CleanupReceipt = new PageCompareCleanupReceiptEvidence
                {
                    SchemaVersion = "ccd243.ownership-cleanup/v1",
                    OperationId = terminal.Operations.CleanupOperationId,
                    ArtifactLocator = "receipts/ccd35-08-cleanup.json",
                    ReceiptDigestSha256 = MigrationDigest.ComputeSha256(Encoding.UTF8.GetBytes(cleanupJson)),
                    ReceiptJson = cleanupJson
                }
            };
        }

        private static MigrationMutationReceipt CreateAdverseStep(
            ClassicWikiImportReceipt receipt,
            string actionId,
            int sequence,
            MutationOutcome outcome,
            int completedSecond)
        {
            return new MigrationMutationReceipt
            {
                OperationId = receipt.OperationId,
                PlanDigest = receipt.ApprovedPlanDigest,
                ActionId = actionId,
                Sequence = sequence,
                CompletedAtUtc = receipt.StartedAtUtc.AddSeconds(completedSecond),
                Outcome = outcome
            };
        }

        private static PageCompareTerminalCase CreateExecutedWikiCase(RuntimeArtifactStore store)
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            NormalizePackage(package);
            var packageArtifact = store.Add(MigrationContractSerializer.SerializeCanonical(package));
            var source = CreateSource("ccd35-08");
            var operations = CreateOperations();
            var targetIdentity = new Uri(package.Plan.TargetLocation.TargetWebUrl).GetLeftPart(UriPartial.Authority)
                + package.Plan.TargetPageServerRelativeUrl;
            var admittedPlan = new AdmittedReproExecutionPlan
            {
                PlanDigest = package.PlanDigest,
                TargetIdentity = targetIdentity,
                SourceVersion = ToCurrentSource(source),
                Operations = operations
            };
            var admittedDigest = ContractDigest(admittedPlan);
            var started = new DateTimeOffset(2026, 9, 9, 19, 0, 0, TimeSpan.Zero);
            var receipt = new ClassicWikiImportReceipt
            {
                StartedAtUtc = started,
                CompletedAtUtc = started.AddSeconds(5),
                OperationId = operations.MutationOperationId,
                AdmittedPlanDigestSha256 = admittedDigest,
                SourceVersion = admittedPlan.SourceVersion,
                Operations = operations,
                ExecutionStatus = MigrationExecutionStatus.Succeeded,
                PartialExecution = false,
                MutationStarted = true,
                Steps = new List<MigrationMutationReceipt>
                {
                    new MigrationMutationReceipt
                    {
                        OperationId = operations.MutationOperationId,
                        PlanDigest = package.PlanDigest,
                        ActionId = "page.create",
                        Sequence = 0,
                        CompletedAtUtc = started.AddSeconds(2),
                        Outcome = MutationOutcome.Applied
                    }
                },
                ApprovedPlanDigest = package.PlanDigest,
                TargetWebUrl = package.Plan.TargetLocation.TargetWebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                TargetFileUniqueId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                TargetListItemId = 42,
                TargetVersionLabel = "1.0",
                StoredWikiFieldSha256 = package.Plan.WikiFieldPlan.ExpectedStoredSha256,
                FreshReadbackPassed = true,
                StorageVerificationStatus = StorageVerificationStatus.Passed,
                RuntimeVerificationStatus = RuntimeVerificationStatus.Pending
            };
            var native = NativePageImportReceiptAggregateFactory.Create(receipt);
            var manifest = new RuntimeVerificationManifest
            {
                Requirements = new List<RuntimeVerificationRequirement>
                {
                    new RuntimeVerificationRequirement { Id = "runtime:ccd35-08", Required = true }
                }
            };
            var evidence = store.Add("runtime-evidence:ccd35-08");
            var dom = store.Add("runtime-dom:ccd35-08");
            var completed = started.AddMinutes(2);
            var runtime = RuntimeVerificationReceiptFactory.Create(
                admittedPlan,
                admittedDigest,
                native.ReceiptDigestSha256,
                targetIdentity,
                manifest,
                ImplementationRef,
                new RuntimeBrowserContextIdentity
                {
                    BrowserProduct = "Edg",
                    BrowserVersion = "151.0.4129.93",
                    ProtocolVersion = "1.3",
                    ProfileIdentitySha256 = Hash("profile"),
                    BrowserContextId = "context:ccd35-08",
                    TargetId = "target:ccd35-08",
                    IsIncognito = true,
                    FreshContext = true,
                    CreatedAtUtc = completed.AddMinutes(-1),
                    FirstNavigationAtUtc = completed.AddSeconds(-30)
                },
                new[]
                {
                    new RuntimeVerificationResult
                    {
                        RequirementId = "runtime:ccd35-08",
                        Passed = true,
                        EvidenceArtifactSha256 = evidence.Digest,
                        EvidenceArtifactLength = evidence.Length,
                        EvidenceArtifactLocator = "runtime/ccd35-08.html",
                        ImplementationRef = ImplementationRef,
                        BrowserContextId = "context:ccd35-08",
                        Http = new RuntimeHttpEvidence
                        {
                            RequestedUrl = targetIdentity,
                            FinalUrl = targetIdentity,
                            Method = "GET",
                            StatusCode = 200,
                            ContentType = "text/html; charset=utf-8",
                            ResponseHeadersDigestSha256 = Hash("headers"),
                            CapturedAtUtc = completed.AddSeconds(-10)
                        },
                        Cache = new RuntimeCacheEvidence
                        {
                            RequestMode = "no-store",
                            CacheDisabled = true,
                            RequestCacheControl = "no-store",
                            RequestPragma = "no-cache",
                            FromDiskCache = false,
                            FromServiceWorker = false
                        },
                        DomProbeArtifactSha256 = dom.Digest,
                        DomProbeArtifactLength = dom.Length,
                        DomProbeArtifactLocator = "runtime/ccd35-08.dom.json"
                    }
                },
                completed);
            var obligation = "obligation:page-artifact";
            var expected = Hash("wiki-page-content");
            return new PageCompareTerminalCase
            {
                CaseId = "ccd35-08",
                TerminalKind = PageCompareTerminalContract.TerminalKinds.WikiNativeExecuted,
                PageFamily = PageCompareTerminalContract.PageFamilies.ClassicWiki,
                MigrationPackageSchemaVersion = ClassicWikiPackageContract.MigrationSchemaVersion,
                MigrationPackageArtifactLocator = "packages/ccd35-08.classic-wiki-migration-package-v1.json",
                MigrationPackageArtifactDigestSha256 = packageArtifact.Digest,
                MigrationPackageArtifactLength = packageArtifact.Length,
                Source = source,
                PlanDigestSha256 = package.PlanDigest,
                ImplementationRef = ImplementationRef,
                EvidenceDigestSha256 = Hash("case-evidence:ccd35-08"),
                AdmittedPlan = admittedPlan,
                AdmittedPlanDigestSha256 = admittedDigest,
                Operations = operations,
                NativeReceipt = native,
                RuntimeManifest = manifest,
                RuntimeReceipt = runtime,
                RuntimeReceiptDigestSha256 = ContractDigest(runtime),
                MutationStatus = PageCompareTerminalContract.Statuses.Succeeded,
                ReadbackStatus = PageCompareTerminalContract.Statuses.Passed,
                RuntimeStatus = PageCompareTerminalContract.Statuses.Passed,
                ReconcileStatus = PageCompareTerminalContract.Statuses.Passed,
                ReasonCode = PageCompareTerminalContract.ReasonCodes.NativeExecuted,
                Ingredients = new List<PageCompareTerminalIngredient>
                {
                    CreateIngredient(
                        source,
                        package.PlanDigest,
                        "node:page-artifact",
                        "PageArtifact",
                        "page-artifact.source-file",
                        PageCompareTerminalContract.Availability.Captured,
                        PageCompareTerminalContract.Statuses.Succeeded,
                        PageCompareTerminalContract.Dispositions.Preserve,
                        PublishingPageCompareContract.ResultClasses.Exact,
                        PublishingPageCompareContract.ReasonCodes.ExactRawDigest,
                        expected,
                        expected,
                        Hash("source-evidence:ccd35-08"),
                        Hash("actual-evidence:ccd35-08"),
                        "page.create",
                        new[] { obligation })
                },
                DependentObligations = new List<PageCompareDependentObligation>
                {
                    new PageCompareDependentObligation
                    {
                        ObligationId = obligation,
                        IngredientId = "node:page-artifact",
                        Required = true,
                        Status = PageCompareTerminalContract.ObligationStatuses.Satisfied,
                        ReasonCode = PageCompareTerminalContract.ReasonCodes.NativeExecuted
                    }
                }
            };
        }

        private static PageCompareTerminalCase CreateDeniedCase()
        {
            var source = CreateSource("ccd35-03");
            const string obligation = "obligation:page-write";
            var ingredient = CreateIngredient(
                source,
                Hash("plan:ccd35-03"),
                "node:page-artifact",
                "PageArtifact",
                "page-artifact.source-file",
                PageCompareTerminalContract.Availability.AccessDenied,
                PageCompareTerminalContract.Statuses.NotExecuted,
                PageCompareTerminalContract.Dispositions.Delegate,
                PageCompareTerminalContract.TerminalResults.AccessDenied,
                PageCompareTerminalContract.ReasonCodes.AccessDeniedSkipped,
                Hash("expected:ccd35-03"),
                null,
                Hash("source-evidence:ccd35-03"),
                null,
                "page.create",
                new[] { obligation });
            ingredient.SemanticAccessDenied = true;
            ingredient.HttpStatus = 403;
            ingredient.AttemptCount = 2;
            return new PageCompareTerminalCase
            {
                CaseId = "ccd35-03",
                TerminalKind = PageCompareTerminalContract.TerminalKinds.PreWriteDenied,
                PageFamily = PageCompareTerminalContract.PageFamilies.ClassicWiki,
                MigrationPackageSchemaVersion = ClassicWikiPackageContract.MigrationSchemaVersion,
                Source = source,
                PlanDigestSha256 = Hash("plan:ccd35-03"),
                ImplementationRef = ImplementationRef,
                EvidenceDigestSha256 = Hash("case-evidence:ccd35-03"),
                MutationStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                ReadbackStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                RuntimeStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                ReconcileStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                ReasonCode = PageCompareTerminalContract.ReasonCodes.AccessDeniedSkipped,
                Ingredients = new List<PageCompareTerminalIngredient> { ingredient },
                DependentObligations = new List<PageCompareDependentObligation>
                {
                    new PageCompareDependentObligation
                    {
                        ObligationId = obligation,
                        IngredientId = ingredient.IngredientId,
                        Required = true,
                        Status = PageCompareTerminalContract.ObligationStatuses.NotExecuted,
                        ReasonCode = PageCompareTerminalContract.ReasonCodes.AccessDeniedSkipped
                    }
                }
            };
        }

        private static PageCompareTerminalCase CreateUnsupportedCase()
        {
            var source = CreateSource("ccd35-06");
            const string obligation = "obligation:publishing-capability";
            var ingredient = CreateIngredient(
                source,
                Hash("plan:ccd35-06"),
                "node:publishing-layout",
                "PageLayout",
                "publishing.base-template-101",
                PageCompareTerminalContract.Availability.Unsupported,
                PageCompareTerminalContract.Statuses.NotExecuted,
                PageCompareTerminalContract.Dispositions.Delegate,
                PageCompareTerminalContract.TerminalResults.Unsupported,
                PageCompareTerminalContract.ReasonCodes.CapabilityUnsupported,
                Hash("expected:ccd35-06"),
                null,
                Hash("source-evidence:ccd35-06"),
                null,
                "publishing.capability.probe",
                new[] { obligation });
            return new PageCompareTerminalCase
            {
                CaseId = "ccd35-06",
                TerminalKind = PageCompareTerminalContract.TerminalKinds.UnsupportedNoExecution,
                PageFamily = PageCompareTerminalContract.PageFamilies.Publishing,
                MigrationPackageSchemaVersion = PublishingPagePackageContract.MigrationSchemaVersion,
                Source = source,
                PlanDigestSha256 = Hash("plan:ccd35-06"),
                ImplementationRef = ImplementationRef,
                EvidenceDigestSha256 = Hash("case-evidence:ccd35-06"),
                MutationStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                ReadbackStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                RuntimeStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                ReconcileStatus = PageCompareTerminalContract.Statuses.NotExecuted,
                ReasonCode = PageCompareTerminalContract.ReasonCodes.CapabilityUnsupported,
                Ingredients = new List<PageCompareTerminalIngredient> { ingredient },
                DependentObligations = new List<PageCompareDependentObligation>
                {
                    new PageCompareDependentObligation
                    {
                        ObligationId = obligation,
                        IngredientId = ingredient.IngredientId,
                        Required = true,
                        Status = PageCompareTerminalContract.ObligationStatuses.Unverified,
                        ReasonCode = PageCompareTerminalContract.ReasonCodes.CapabilityUnsupported
                    }
                }
            };
        }

        private static PageCompareTerminalIngredient CreateIngredient(
            PageCompareSourceIdentity source,
            string planDigest,
            string id,
            string kind,
            string subtype,
            string availability,
            string executionStatus,
            string disposition,
            string result,
            string reason,
            string expected,
            string actual,
            string sourceEvidence,
            string actualEvidence,
            string actionId,
            IEnumerable<string> obligationIds)
        {
            var evidenceRefs = new List<string> { "sha256:" + sourceEvidence };
            if (!string.IsNullOrWhiteSpace(actualEvidence))
            {
                evidenceRefs.Add("sha256:" + actualEvidence);
            }
            return new PageCompareTerminalIngredient
            {
                IngredientId = id,
                Kind = kind,
                Subtype = subtype,
                Material = true,
                Required = true,
                FidelitySignificant = true,
                SourceIdentityDigestSha256 = source.IdentityDigestSha256,
                SourceVersionDigestSha256 = source.VersionDigestSha256,
                PlanDigestSha256 = planDigest,
                ImplementationRef = ImplementationRef,
                SourceEvidenceDigestSha256 = sourceEvidence,
                ActualEvidenceDigestSha256 = actualEvidence,
                ActionId = actionId,
                Disposition = disposition,
                Result = result,
                Availability = availability,
                ExecutionStatus = executionStatus,
                ReasonCode = reason,
                Lineage = new IngredientCompareLineage
                {
                    RequestedUrlHashSha256 = Hash("requested:" + id),
                    SourceArtifactDigestSha256 = sourceEvidence,
                    SourceIngredientId = id,
                    ActionId = actionId,
                    TargetIdentity = actual == null ? null : "target:" + id,
                    EvidenceRefs = evidenceRefs,
                    CauseIngredientIds = new List<string>()
                },
                Expected = new CompareDigestPair { RawDigestSha256 = expected, CanonicalDigestSha256 = expected },
                Actual = new CompareDigestPair { RawDigestSha256 = actual, CanonicalDigestSha256 = actual },
                DependentObligationIds = obligationIds.ToList()
            };
        }

        private static PublishingPageCompareReport CreateCanonicalReport()
        {
            var operations = CreateOperations();
            var report = new PublishingPageCompareReport
            {
                GeneratedAtUtc = new DateTimeOffset(2026, 9, 9, 19, 0, 0, TimeSpan.Zero),
                Producer = new CompareProducer
                {
                    Id = "pnp-compare",
                    Version = "1.0.0",
                    ImplementationRef = ImplementationRef
                },
                AssessmentHandoff = new AssessmentHandoffProjection
                {
                    SchemaVersion = PublishingPageCompareContract.AssessmentHandoffSchemaVersion,
                    ProducerRevisionId = PublishingPageCompareContract.AssessmentProducerRevisionId,
                    ConformanceRevisionId = PublishingPageCompareContract.AssessmentConformanceRevisionId,
                    HandoffDigestSha256 = Hash("handoff"),
                    ManifestRevisionId = "sha256:" + Hash("manifest-revision"),
                    AllowlistRevisionId = "sha256:" + Hash("allowlist-revision"),
                    SelectionId = "A1-001",
                    CanonicalLocatorHash = "sha256:" + Hash("locator"),
                    ResourceIdentityHash = "sha256:" + Hash("resource"),
                    ApprovedHostHash = "hmac-sha256:" + Hash("host"),
                    PermissionSignal = "known_readable",
                    DiscoveryObservationStatus = PublishingPageCompareContract.UnavailableByProducerContract,
                    ProducerAttestationStatus = PublishingPageCompareContract.UnavailableByProducerContract,
                    CoverageStatus = PublishingPageCompareContract.UnavailableByProducerContract
                },
                Bindings = new CompareBindings
                {
                    ManifestDigestSha256 = Hash("manifest"),
                    SourceCaptureReceiptDigestSha256 = Hash("capture"),
                    ExportSchemaVersion = PublishingPagePackageContract.ExportSchemaVersion,
                    SnapshotDigestSha256 = Hash("snapshot"),
                    IngredientGraphSchemaVersion = "pnp-page-ingredient-graph/v2",
                    IngredientProjectionVersion = "v2",
                    MigrationPackageSchemaVersion = PublishingPagePackageContract.MigrationSchemaVersion,
                    PlanDigestSha256 = Hash("plan"),
                    AdmittedPlanDigestSha256 = Hash("admitted"),
                    SourceVersionDigestSha256 = Hash("source-version"),
                    Operations = operations,
                    ImportReceiptSchemaVersion = PublishingPagePackageContract.ReceiptSchemaVersion,
                    ImportReceiptDigestSha256 = Hash("receipt"),
                    RuntimeReceiptSchemaVersion = PublishingPageCompareContract.RuntimeReceiptSchemaVersion,
                    RuntimeReceiptDigestSha256 = Hash("runtime")
                },
                SourceVersionComparison = new SourceVersionComparison
                {
                    IdentityDigestSha256 = Hash("source-identity"),
                    ExpectedCompositeDigestSha256 = Hash("source-version"),
                    ObservedCompositeDigestSha256 = Hash("source-version"),
                    ObservedETag = "\"etag,1\"",
                    ObservedAtUtc = new DateTimeOffset(2026, 9, 9, 18, 50, 0, TimeSpan.Zero),
                    Status = "matched"
                },
                TargetIdentity = new CompareTargetIdentity
                {
                    WebUrlHashSha256 = Hash("target-web"),
                    PageServerRelativeUrlHashSha256 = Hash("target-page"),
                    FileUniqueId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    ListItemId = 42,
                    VersionLabel = "1.0"
                },
                Storage = new CompareStorageSummary { Status = "passed", FreshReadback = true },
                Runtime = new CompareRuntimeSummary { Status = "passed" },
                Ingredients = new List<IngredientCompareResult>
                {
                    new IngredientCompareResult
                    {
                        IngredientId = "node:page-layout",
                        Kind = "PageLayout",
                        Material = true,
                        Lineage = new IngredientCompareLineage
                        {
                            RequestedUrlHashSha256 = Hash("requested"),
                            SourceArtifactDigestSha256 = Hash("source-artifact"),
                            SourceIngredientId = "node:page-layout",
                            ActionId = "layout.apply",
                            TargetIdentity = "target:layout",
                            EvidenceRefs = new List<string>
                            {
                                "sha256:" + Hash("source-evidence"),
                                "sha256:" + Hash("actual-evidence")
                            },
                            CauseIngredientIds = new List<string>()
                        },
                        Expected = new CompareDigestPair
                        {
                            RawDigestSha256 = Hash("layout"),
                            CanonicalDigestSha256 = Hash("layout")
                        },
                        Actual = new CompareDigestPair
                        {
                            RawDigestSha256 = Hash("layout"),
                            CanonicalDigestSha256 = Hash("layout")
                        },
                        ResultClass = PublishingPageCompareContract.ResultClasses.Exact,
                        ReasonCode = PublishingPageCompareContract.ReasonCodes.ExactRawDigest
                    }
                },
                Acceptance = new CompareAcceptance
                {
                    Verdict = "pass",
                    StorageStatus = "passed",
                    RuntimeStatus = "passed",
                    ReasonCodes = new List<string>()
                }
            };
            report.ReportDigestSha256 = PublishingPageCompareReconciler.ComputeReportDigest(report);
            return report;
        }

        private static PageCompareSourceIdentity CreateSource(string caseId)
        {
            return new PageCompareSourceIdentity
            {
                OriginalIdentifier = "urn:pnp:test:" + caseId,
                PageServerRelativeUrl = "/sites/source/Pages/" + caseId + ".aspx",
                IdentityDigestSha256 = Hash("source-identity:" + caseId),
                VersionDigestSha256 = Hash("source-version:" + caseId),
                ETag = "\"" + caseId + ",1\"",
                LastModifiedUtc = new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero),
                VersionLabel = "1.0",
                ObservedAtUtc = new DateTimeOffset(2026, 9, 9, 18, 5, 0, TimeSpan.Zero)
            };
        }

        private static void NormalizePackage(ClassicWikiMigrationPackage package)
        {
            var capturedAt = new DateTimeOffset(2026, 9, 9, 17, 0, 0, TimeSpan.Zero);
            package.ExportedAtUtc = capturedAt;
            package.PlannedAtUtc = capturedAt.AddMinutes(5);
            package.Snapshot.Source.SiteId = Guid.Parse("01010101-0101-0101-0101-010101010101");
            package.Snapshot.Source.WebId = Guid.Parse("02020202-0202-0202-0202-020202020202");
            package.Snapshot.Source.FileUniqueId = Guid.Parse("03030303-0303-0303-0303-030303030303");
            package.Snapshot.Lifecycle.CreatedUtc = capturedAt.AddDays(-10).UtcDateTime;
            package.Snapshot.Lifecycle.ModifiedUtc = capturedAt.UtcDateTime;
            package.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
        }

        private static CurrentSourceVersionIdentity ToCurrentSource(PageCompareSourceIdentity value)
        {
            return new CurrentSourceVersionIdentity
            {
                IdentityDigestSha256 = value.IdentityDigestSha256,
                VersionDigestSha256 = value.VersionDigestSha256,
                ETag = value.ETag,
                LastModifiedUtc = value.LastModifiedUtc,
                VersionLabel = value.VersionLabel,
                ObservedAtUtc = value.ObservedAtUtc
            };
        }

        private static ReproOperationIds CreateOperations()
        {
            return new ReproOperationIds
            {
                MutationOperationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ReadbackOperationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                RuntimeOperationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                CleanupOperationId = Guid.Parse("44444444-4444-4444-4444-444444444444")
            };
        }

        private static void AssertRejected(PageCompareTerminalEnvelope envelope, RuntimeArtifactStore store)
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                PageCompareTerminalEnvelopeValidator.Validate(envelope, ImplementationRef, store));
        }

        private static void AssertCanonicalRejected(
            PublishingPageCompareReport source,
            Action<PublishingPageCompareReport> mutate)
        {
            var candidate = Clone(source);
            mutate(candidate);
            candidate.ReportDigestSha256 = PublishingPageCompareReconciler.ComputeReportDigest(candidate);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReportValidator.Validate(candidate, ImplementationRef));
        }

        private static void Reseal(PageCompareTerminalEnvelope envelope)
        {
            envelope.EnvelopeDigestSha256 = PageCompareTerminalEnvelopeFactory.ComputeDigest(envelope);
        }

        private static void Reseal(PageCompareTerminalEnvelopeV2 envelope)
        {
            envelope.EnvelopeDigestSha256 = PageCompareTerminalEnvelopeFactoryV2.ComputeDigest(envelope);
        }

        private static T Clone<T>(T value)
        {
            return MigrationContractSerializer.Deserialize<T>(
                MigrationContractSerializer.SerializeCanonical(value));
        }

        private static string ContractDigest<T>(T value)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value));
        }

        private static string Hash(string value)
        {
            return MigrationDigest.ComputeSha256(value);
        }

        private sealed class RuntimeArtifactStore : IMigrationArtifactStore
        {
            private readonly Dictionary<string, byte[]> artifacts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            public (string Digest, long Length) Add(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                var digest = MigrationDigest.ComputeSha256(bytes);
                artifacts[digest] = bytes;
                return (digest, bytes.LongLength);
            }

            public bool Contains(string sha256) => artifacts.ContainsKey(sha256);

            public Stream OpenRead(string sha256) => new MemoryStream(artifacts[sha256], writable: false);

            public ArtifactReference Put(Stream content, string mediaType = null, string originalName = null)
            {
                throw new NotSupportedException();
            }
        }
    }
}
