using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Utilities.UnitTests.Web;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiTargetUrlAdmissionTests
    {
        [TestMethod]
        public void ImportAdmittedRejectsPlainDotSegmentsBeforeAnyCsomRequest()
        {
            var package = CreateFixedPackage();
            package.Plan.TargetLocation.TargetFolderServerRelativeUrl =
                "/sites/target/SitePages/Department/../../Documents";
            package.Plan.TargetLocation.FileName = "Escape.aspx";
            package.Plan.TargetPageServerRelativeUrl =
                "/sites/target/SitePages/Department/../../Documents/Escape.aspx";
            Reseal(package);
            var admittedPlan = CreateAdmittedPlan(package);
            var provider = new FailIfRequestedResponseProvider();

            using var context = new ClientContext(package.Plan.TargetLocation.TargetWebUrl);
            context.WebRequestExecutorFactory = new MockWebRequestExecutorFactory(provider);

            var receipt = new ClassicWikiMigrationImporter().ImportAdmitted(
                context,
                package,
                package.PlanDigest,
                admittedPlan);

            Assert.AreEqual(MigrationExecutionStatus.NotStarted, receipt.ExecutionStatus);
            Assert.AreEqual("PackageNotAdmissible", receipt.AdmissionFailure.Code);
            Assert.IsFalse(receipt.MutationStarted);
            Assert.AreEqual(0, receipt.Steps.Count);
            Assert.AreEqual(0, provider.RequestCount, "URL admission must reject before any CSOM request.");
        }

        [TestMethod]
        public void PackageAdmissionRejectsDotSegmentsAcrossWebLibraryFolderPageAndFile()
        {
            AssertPackageRejected(package =>
                package.Plan.TargetLocation.TargetWebUrl =
                    "https://contoso.sharepoint.com/sites/target/../target");

            AssertPackageRejected(package =>
            {
                package.Plan.TargetLocation.TargetLibraryServerRelativeUrl =
                    "/sites/target/SitePages/../Documents";
                package.Plan.TargetLocation.TargetFolderServerRelativeUrl =
                    "/sites/target/SitePages/../Documents";
                package.Plan.TargetPageServerRelativeUrl =
                    "/sites/target/SitePages/../Documents/Escape.aspx";
                package.Plan.TargetLocation.FileName = "Escape.aspx";
            });

            AssertPackageRejected(package =>
            {
                package.Plan.TargetLocation.TargetFolderServerRelativeUrl =
                    "/sites/target/SitePages/Department/..";
                package.Plan.TargetPageServerRelativeUrl =
                    "/sites/target/SitePages/Department/../Escape.aspx";
                package.Plan.TargetLocation.FileName = "Escape.aspx";
            });

            AssertPackageRejected(package =>
                package.Plan.TargetPageServerRelativeUrl =
                    "/sites/target/SitePages/../SitePages/Welcome.aspx");

            AssertPackageRejected(package =>
                package.Plan.TargetLocation.FileName = "../Welcome.aspx");
        }

        [TestMethod]
        public void PackageAdmissionRejectsNestedMismatchAndForeignOrSiblingBoundaries()
        {
            AssertPackageRejected(package =>
                package.Plan.TargetLocation.FileName = "Other.aspx");

            AssertPackageRejected(package =>
            {
                package.Plan.TargetLocation.TargetLibraryServerRelativeUrl =
                    "/sites/foreign/SitePages";
                package.Plan.TargetLocation.TargetFolderServerRelativeUrl =
                    "/sites/foreign/SitePages";
                package.Plan.TargetPageServerRelativeUrl =
                    "/sites/foreign/SitePages/Welcome.aspx";
            });

            AssertPackageRejected(package =>
            {
                package.Plan.TargetLocation.TargetFolderServerRelativeUrl =
                    "/sites/target/Documents";
                package.Plan.TargetPageServerRelativeUrl =
                    "/sites/target/Documents/Welcome.aspx";
            });
        }

        [TestMethod]
        public void ImportAdmittedRejectsForeignOrStaleAdmissionBeforeAnyCsomRequest()
        {
            var package = CreateFixedPackage();
            var foreign = CreateAdmittedPlan(package);
            foreign.TargetIdentity =
                "https://contoso.sharepoint.com/sites/foreign/SitePages/Welcome.aspx";
            AssertAdmittedPlanRejectedBeforeRequest(package, foreign);

            var stale = CreateAdmittedPlan(package);
            stale.PlanDigest = MigrationDigest.ComputeSha256("stale-plan");
            AssertAdmittedPlanRejectedBeforeRequest(package, stale);
        }

        [TestMethod]
        public void OwnershipRejectsMissingAndStaleSealedPlanEvidence()
        {
            var package = CreateFixedPackage();
            var missing = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Assert.IsFalse(ClassicWikiTargetOwnership.MatchesApprovedPlan(
                missing,
                package.Plan.OriginalIdentifier,
                package.SnapshotDigest,
                package.PlanDigest));

            var stale = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ClassicWikiTargetOwnership.OriginalIdentifierPropertyName] = package.Plan.OriginalIdentifier,
                [ClassicWikiTargetOwnership.SourceSnapshotDigestPropertyName] = package.SnapshotDigest,
                [ClassicWikiTargetOwnership.PlanDigestPropertyName] = MigrationDigest.ComputeSha256("stale-plan")
            };
            Assert.IsFalse(ClassicWikiTargetOwnership.MatchesApprovedPlan(
                stale,
                package.Plan.OriginalIdentifier,
                package.SnapshotDigest,
                package.PlanDigest));
        }

        [DataTestMethod]
        [DataRow("/sites/target/SitePages/Welcome.aspx")]
        [DataRow("/sites/target/SitePages/Department/Welcome.aspx")]
        [DataRow("/sites/target/SitePages/Department/Mac Office Release Wiki.aspx")]
        public void PackageAdmissionPreservesRootNestedAndSpacePaths(string targetPageServerRelativeUrl)
        {
            var package = CreateFixedPackage();
            var separator = targetPageServerRelativeUrl.LastIndexOf('/');
            package.Plan.TargetLocation.TargetFolderServerRelativeUrl =
                targetPageServerRelativeUrl.Substring(0, separator);
            package.Plan.TargetLocation.FileName = targetPageServerRelativeUrl.Substring(separator + 1);
            package.Plan.TargetPageServerRelativeUrl = targetPageServerRelativeUrl;
            Reseal(package);

            ClassicWikiPackageValidator.ValidateMigration(package);
        }

        private static void AssertPackageRejected(Action<ClassicWikiMigrationPackage> mutate)
        {
            var package = CreateFixedPackage();
            mutate(package);
            Reseal(package);
            Assert.ThrowsException<InvalidDataException>(() =>
                ClassicWikiPackageValidator.ValidateMigration(package));
        }

        private static void AssertAdmittedPlanRejectedBeforeRequest(
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan)
        {
            var provider = new FailIfRequestedResponseProvider();
            using var context = new ClientContext(package.Plan.TargetLocation.TargetWebUrl);
            context.WebRequestExecutorFactory = new MockWebRequestExecutorFactory(provider);

            Assert.ThrowsException<InvalidDataException>(() =>
                new ClassicWikiMigrationImporter().ImportAdmitted(
                    context,
                    package,
                    package.PlanDigest,
                    admittedPlan));
            Assert.AreEqual(0, provider.RequestCount);
        }

        private static ClassicWikiMigrationPackage CreateFixedPackage()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            package.ExportedAtUtc = new DateTimeOffset(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);
            package.PlannedAtUtc = new DateTimeOffset(2026, 9, 9, 20, 5, 0, TimeSpan.Zero);
            package.Snapshot.Source.SiteId = Guid.Parse("34300000-0000-0000-0000-000000000001");
            package.Snapshot.Source.WebId = Guid.Parse("34300000-0000-0000-0000-000000000002");
            package.Snapshot.Source.FileUniqueId = Guid.Parse("34300000-0000-0000-0000-000000000003");
            package.Snapshot.Lifecycle.CreatedUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            package.Snapshot.Lifecycle.ModifiedUtc = new DateTime(2026, 9, 9, 19, 55, 0, DateTimeKind.Utc);
            Reseal(package);
            return package;
        }

        private static void Reseal(ClassicWikiMigrationPackage package)
        {
            package.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
        }

        private static AdmittedReproExecutionPlan CreateAdmittedPlan(
            ClassicWikiMigrationPackage package)
        {
            var observedAt = new DateTimeOffset(2026, 9, 9, 20, 10, 0, TimeSpan.Zero);
            return new AdmittedReproExecutionPlan
            {
                PlanDigest = package.PlanDigest,
                TargetIdentity = new Uri(package.Plan.TargetLocation.TargetWebUrl)
                    .GetLeftPart(UriPartial.Authority)
                    + package.Plan.TargetPageServerRelativeUrl,
                SourceVersion = new CurrentSourceVersionIdentity
                {
                    IdentityDigestSha256 = MigrationDigest.ComputeSha256("ccd343-source-identity"),
                    VersionDigestSha256 = MigrationDigest.ComputeSha256("ccd343-source-version"),
                    ETag = "\"ccd343,1\"",
                    LastModifiedUtc = observedAt.AddMinutes(-5),
                    VersionLabel = "17.0",
                    ObservedAtUtc = observedAt
                },
                Operations = new ReproOperationIds
                {
                    MutationOperationId = Guid.Parse("34300000-0000-0000-0000-000000000011"),
                    ReadbackOperationId = Guid.Parse("34300000-0000-0000-0000-000000000012"),
                    RuntimeOperationId = Guid.Parse("34300000-0000-0000-0000-000000000013"),
                    CleanupOperationId = Guid.Parse("34300000-0000-0000-0000-000000000014")
                }
            };
        }

        private sealed class FailIfRequestedResponseProvider : IMockResponseProvider
        {
            public int RequestCount { get; private set; }

            public string GetResponse(string url, string verb, string body)
            {
                RequestCount++;
                throw new AssertFailedException("URL admission issued an unexpected CSOM request.");
            }
        }
    }
}
