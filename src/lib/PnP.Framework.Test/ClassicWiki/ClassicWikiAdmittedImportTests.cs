using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Comparison;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiAdmittedImportTests
    {
        [TestMethod]
        public void ImportAdmittedBindsSourcePlanAndFourOperationsBeforeMutation()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var admittedPlan = CreateAdmittedPlan(package);
            var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                admittedPlan.TargetIdentity);

            using var context = new ClientContext(package.Plan.TargetLocation.TargetWebUrl);
            var receipt = new ClassicWikiMigrationImporter().ImportAdmitted(
                context,
                package,
                Hash("unapproved-plan"),
                admittedPlan);

            Assert.AreEqual(MigrationExecutionStatus.NotStarted, receipt.ExecutionStatus);
            Assert.AreEqual("PlanDigestNotApproved", receipt.AdmissionFailure.Code);
            Assert.AreEqual(admittedPlan.Operations.MutationOperationId, receipt.OperationId);
            Assert.AreEqual(admittedDigest, receipt.AdmittedPlanDigestSha256);
            Assert.IsTrue(AdmittedReproExecutionPlanValidator.SameSourceVersion(
                admittedPlan.SourceVersion,
                receipt.SourceVersion));
            Assert.IsTrue(AdmittedReproExecutionPlanValidator.SameOperations(
                admittedPlan.Operations,
                receipt.Operations));
            Assert.IsFalse(receipt.MutationStarted);
            Assert.AreEqual(0, receipt.Steps.Count);
        }

        [DataTestMethod]
        [DataRow("ccd35-03")]
        [DataRow("ccd35-08")]
        public void NativeAggregatePreservesClassicWikiReceiptAndProducesCompareBinding(string caseId)
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var admittedPlan = CreateAdmittedPlan(package, caseId);
            var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                admittedPlan.TargetIdentity);
            var receipt = CreateSuccessfulReceipt(package, admittedPlan, admittedDigest, caseId);
            var aggregate = NativePageImportReceiptAggregateFactory.Create(receipt);

            var serialized = MigrationContractSerializer.SerializeCanonical(aggregate);
            var roundTrip = MigrationContractSerializer.Deserialize<NativePageImportReceiptAggregate>(serialized);
            Assert.AreEqual(
                MigrationContractSerializer.SerializeCanonical(receipt),
                MigrationContractSerializer.SerializeCanonical(roundTrip.ClassicWikiReceipt),
                "The typed Classic Wiki receipt changed during aggregate round-trip.");
            var binding = NativePageImportReceiptAggregateValidator.ValidateForCompare(
                roundTrip,
                admittedPlan,
                admittedDigest);

            Assert.AreEqual(NativePageImportReceiptContract.ClassicWikiFamily, binding.PageFamily);
            Assert.AreEqual(ClassicWikiPackageContract.ReceiptSchemaVersion, binding.ReceiptSchemaVersion);
            Assert.AreEqual(admittedDigest, binding.AdmittedPlanDigestSha256);
            Assert.AreEqual(admittedPlan.SourceVersion.VersionDigestSha256, binding.SourceVersionDigestSha256);
            Assert.AreEqual(admittedPlan.Operations.MutationOperationId, binding.MutationOperationId);
            Assert.IsTrue(AdmittedReproExecutionPlanValidator.SameOperations(
                admittedPlan.Operations,
                binding.Operations));
            Assert.AreEqual(1, binding.NativeStepCount);
            Assert.AreEqual(receipt.StoredWikiFieldSha256, roundTrip.ClassicWikiReceipt.StoredWikiFieldSha256);
            Assert.IsNull(roundTrip.PublishingReceipt);
            Assert.AreEqual(
                MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(receipt)),
                binding.ReceiptDigestSha256);
        }

        [TestMethod]
        public void NativeAggregateRejectsMissingStaleForeignCorruptPartialDuplicateAndOrphanReceipts()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var admittedPlan = CreateAdmittedPlan(package);
            var admittedDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                admittedPlan.TargetIdentity);
            var receipt = CreateSuccessfulReceipt(package, admittedPlan, admittedDigest, "ccd35-03");

            var missing = NativePageImportReceiptAggregateFactory.Create(Clone(receipt));
            missing.ClassicWikiReceipt = null;
            AssertRejected(missing, admittedPlan, admittedDigest);

            var missingSteps = Clone(receipt);
            missingSteps.Steps.Clear();
            AssertRejected(NativePageImportReceiptAggregateFactory.Create(missingSteps), admittedPlan, admittedDigest);

            var stale = Clone(receipt);
            stale.AdmittedPlanDigestSha256 = Hash("stale-admitted-plan");
            AssertRejected(NativePageImportReceiptAggregateFactory.Create(stale), admittedPlan, admittedDigest);

            var foreign = Clone(receipt);
            foreign.OperationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            AssertRejected(NativePageImportReceiptAggregateFactory.Create(foreign), admittedPlan, admittedDigest);

            var corrupt = NativePageImportReceiptAggregateFactory.Create(Clone(receipt));
            corrupt.ReceiptDigestSha256 = Hash("corrupt-receipt-bytes");
            AssertRejected(corrupt, admittedPlan, admittedDigest);

            var partial = Clone(receipt);
            partial.PartialExecution = true;
            AssertRejected(NativePageImportReceiptAggregateFactory.Create(partial), admittedPlan, admittedDigest);

            var duplicate = Clone(receipt);
            duplicate.Steps.Add(new MigrationMutationReceipt
            {
                OperationId = duplicate.OperationId,
                PlanDigest = duplicate.ApprovedPlanDigest,
                ActionId = duplicate.Steps[0].ActionId,
                Sequence = 1,
                CompletedAtUtc = duplicate.Steps[0].CompletedAtUtc,
                Outcome = MutationOutcome.Applied
            });
            AssertRejected(NativePageImportReceiptAggregateFactory.Create(duplicate), admittedPlan, admittedDigest);

            var orphan = Clone(receipt);
            orphan.Steps[0].Sequence = 1;
            AssertRejected(NativePageImportReceiptAggregateFactory.Create(orphan), admittedPlan, admittedDigest);
        }

        private static void AssertRejected(
            NativePageImportReceiptAggregate aggregate,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedDigest)
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                NativePageImportReceiptAggregateValidator.ValidateForCompare(
                    aggregate,
                    admittedPlan,
                    admittedDigest));
        }

        private static ClassicWikiImportReceipt CreateSuccessfulReceipt(
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedDigest,
            string caseId)
        {
            var startedAt = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
            return new ClassicWikiImportReceipt
            {
                StartedAtUtc = startedAt,
                CompletedAtUtc = startedAt.AddSeconds(5),
                OperationId = admittedPlan.Operations.MutationOperationId,
                AdmittedPlanDigestSha256 = admittedDigest,
                SourceVersion = admittedPlan.SourceVersion,
                Operations = admittedPlan.Operations,
                ExecutionStatus = MigrationExecutionStatus.Succeeded,
                PartialExecution = false,
                MutationStarted = true,
                Steps = new List<MigrationMutationReceipt>
                {
                    new MigrationMutationReceipt
                    {
                        OperationId = admittedPlan.Operations.MutationOperationId,
                        PlanDigest = package.PlanDigest,
                        ActionId = "classic-wiki.apply:" + caseId,
                        Sequence = 0,
                        CompletedAtUtc = startedAt.AddSeconds(2),
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
        }

        private static AdmittedReproExecutionPlan CreateAdmittedPlan(
            ClassicWikiMigrationPackage package,
            string caseId = "ccd35-03")
        {
            return new AdmittedReproExecutionPlan
            {
                PlanDigest = package.PlanDigest,
                TargetIdentity = new Uri(package.Plan.TargetLocation.TargetWebUrl).GetLeftPart(UriPartial.Authority)
                    + package.Plan.TargetPageServerRelativeUrl,
                SourceVersion = new CurrentSourceVersionIdentity
                {
                    IdentityDigestSha256 = Hash("source-identity:" + caseId),
                    VersionDigestSha256 = Hash("source-version:" + caseId),
                    ETag = "\"" + caseId + ",1\"",
                    LastModifiedUtc = new DateTimeOffset(2026, 9, 9, 11, 50, 0, TimeSpan.Zero),
                    VersionLabel = "1.0",
                    ObservedAtUtc = new DateTimeOffset(2026, 9, 9, 11, 55, 0, TimeSpan.Zero)
                },
                Operations = new ReproOperationIds
                {
                    MutationOperationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    ReadbackOperationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    RuntimeOperationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    CleanupOperationId = Guid.Parse("44444444-4444-4444-4444-444444444444")
                }
            };
        }

        private static ClassicWikiImportReceipt Clone(ClassicWikiImportReceipt value)
        {
            return MigrationContractSerializer.Deserialize<ClassicWikiImportReceipt>(
                MigrationContractSerializer.SerializeCanonical(value));
        }

        private static string Hash(string value)
        {
            return MigrationDigest.ComputeSha256(value);
        }
    }
}
