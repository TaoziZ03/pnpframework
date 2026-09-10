using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Packaging;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.References;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiDependencyInventoryTests
    {
        private const string CanaryWikiField =
            "<div>"
            + "<a href=\"/teams/office_rdx/rm/SitePages/Mac%20Office.aspx\">APEX Release Schedule</a>"
            + "<a href=\"/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/4%20-%20KB%20Process.docx?d=w298fa4b6c9c94ce79f570abe53f95acd\">Create a KB</a>"
            + "<a href=\"/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/1-%20Create%20FWLink.docx?d=w8cf58e60c2044c9890c35fa0024abe8f\">Create FWLink</a>"
            + "<a href=\"/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/Archived/Mac%202011%20Only/2%20-%20Create%20Download%20Families%20in%20DMS.docx?d=wc2cc973a833d4236af4da72db0658583\">Clone a Download family</a>"
            + "<a href=\"/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/Archived/Upload%20File%20to%20CDN.docx?d=w19689efb44a0499faff5e22e92c78d50\">Publish to CDN</a>"
            + "<a href=\"mailto:owner@example.com\">Owner</a>"
            + "</div>";

        [TestMethod]
        public void ReferenceOnlyInventoryReconstructsTheFiveCanaryAnchors()
        {
            var source = CreateCanaryExport();

            var dependencies = ClassicWikiReferenceInventory.CaptureReferenceOnly(source.Snapshot);

            Assert.AreEqual(5, dependencies.Count);
            Assert.IsTrue(dependencies.All(value => value.Kind == PageReferenceKind.Anchor));
            Assert.IsTrue(dependencies.All(value => value.Consumer == "a[href]"));
            Assert.IsTrue(dependencies.All(value => value.CaptureStatus == PageCaptureStatus.Captured));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "/teams/office_rdx/rm/SitePages/Mac%20Office.aspx",
                    "/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/4%20-%20KB%20Process.docx?d=w298fa4b6c9c94ce79f570abe53f95acd",
                    "/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/1-%20Create%20FWLink.docx?d=w8cf58e60c2044c9890c35fa0024abe8f",
                    "/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/Archived/Mac%202011%20Only/2%20-%20Create%20Download%20Families%20in%20DMS.docx?d=wc2cc973a833d4236af4da72db0658583",
                    "/teams/office_rdx/rm/Mac%20Office%20Release%20Docs/Release%20Process/Archived/Upload%20File%20to%20CDN.docx?d=w19689efb44a0499faff5e22e92c78d50"
                },
                dependencies.Select(value => value.OriginalValue).ToArray());
        }

        [TestMethod]
        public void ReferenceOnlyInventoryRejectsAStaleWikiFieldDigest()
        {
            var source = CreateCanaryExport();
            source.Snapshot.WikiFieldSha256 = new string('0', 64);

            var error = Assert.ThrowsException<InvalidDataException>(() =>
                ClassicWikiReferenceInventory.CaptureReferenceOnly(source.Snapshot));

            StringAssert.Contains(error.Message, "missing or stale");
        }

        [TestMethod]
        public void OfflineRestAdaptationClosesTheCanaryZeroToFiveVerificationGap()
        {
            var package = CreatePlannedCanaryPackage();
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            EnsureTargetCapturePolicy(evidence);
            evidence.Recapture.Snapshot.Dependencies = ClassicWikiReferenceInventory
                .CaptureReferenceOnly(evidence.Recapture.Snapshot)
                .ToList();

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.AreEqual(5, package.Snapshot.Dependencies.Count);
            Assert.AreEqual(5, package.Plan.Dependencies.Count);
            Assert.IsTrue(result.Passed, string.Join(" | ", result.Differences));
            Assert.IsTrue(result.DependenciesMatched);
        }

        [TestMethod]
        public void FreshVerificationRejectsMissingAndExtraDependencies()
        {
            var package = CreatePlannedCanaryPackage();
            var evidence = CreateDependencyEvidence(package);
            evidence.Recapture.Snapshot.Dependencies.RemoveAt(0);

            var missing = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(missing.Passed);
            StringAssert.Contains(missing.Differences.Single(value => value.Contains("Dependency count mismatch")), "expected 5, observed 4");

            evidence = CreateDependencyEvidence(package);
            evidence.Recapture.Snapshot.Dependencies.Add(new PageReferenceSnapshot
            {
                Id = PageDigest.ComputeSha256("a[href]\nhttps://a830edad9050849cupcollect.sharepoint.com/foreign.aspx"),
                Consumer = "a[href]",
                Kind = PageReferenceKind.Anchor,
                OriginalValue = "/foreign.aspx",
                SourceAbsoluteUrl = "https://a830edad9050849cupcollect.sharepoint.com/foreign.aspx",
                SourceServerRelativeUrl = "/foreign.aspx",
                CaptureStatus = PageCaptureStatus.Captured
            });

            var extra = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(extra.Passed);
            StringAssert.Contains(extra.Differences.Single(value => value.Contains("Dependency count mismatch")), "expected 5, observed 6");
        }

        [TestMethod]
        public void FreshVerificationRejectsUnavailableStaleAndForeignDependencyEvidence()
        {
            var package = CreatePlannedCanaryPackage();
            var evidence = CreateDependencyEvidence(package);
            evidence.Recapture.Snapshot.Dependencies[0].CaptureStatus = PageCaptureStatus.Failed;

            var unavailable = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(unavailable.Passed);
            Assert.IsTrue(unavailable.Differences.Any(value => value.Contains("fresh evidence is unavailable")));

            evidence = CreateDependencyEvidence(package);
            evidence.Recapture.Snapshot.Dependencies[0].Id = new string('0', 64);
            var stale = ClassicWikiFreshVerification.Evaluate(package, evidence);
            Assert.IsFalse(stale.Passed);
            Assert.IsTrue(stale.Differences.Any(value => value.Contains("exact-semantics mismatch")));

            evidence = CreateDependencyEvidence(package);
            var foreign = evidence.Recapture.Snapshot.Dependencies[0];
            foreign.SourceAbsoluteUrl = "https://foreign.example.com/SitePages/Mac%20Office.aspx";
            foreign.Id = PageDigest.ComputeSha256(foreign.Consumer + "\n" + foreign.SourceAbsoluteUrl);
            var foreignResult = ClassicWikiFreshVerification.Evaluate(package, evidence);
            Assert.IsFalse(foreignResult.Passed);
            Assert.IsTrue(foreignResult.Differences.Any(value => value.Contains("exact-semantics mismatch")));
        }

        [DataTestMethod]
        [DataRow(PageCaptureStatus.NotReturned, "fresh evidence is unavailable")]
        [DataRow(PageCaptureStatus.CapturedWithLimitations, "no reviewed reference-only profile")]
        public void FreshVerificationRejectsNonFidelityCaptureStatuses(
            PageCaptureStatus captureStatus,
            string expectedDiagnostic)
        {
            var package = CreatePlannedCanaryPackage();
            var evidence = CreateDependencyEvidence(package);
            evidence.Recapture.Snapshot.Dependencies[0].CaptureStatus = captureStatus;

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.DependenciesMatched);
            Assert.IsTrue(result.Differences.Any(value => value.Contains(expectedDiagnostic)));
        }

        [TestMethod]
        public void FreshVerificationRejectsUndefinedCaptureStatus()
        {
            var package = CreatePlannedCanaryPackage();
            var evidence = CreateDependencyEvidence(package);
            evidence.Recapture.Snapshot.Dependencies[0].CaptureStatus = (PageCaptureStatus)999;

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.DependenciesMatched);
            Assert.IsTrue(result.Differences.Any(value => value.Contains("unsupported capture status '999'")));
        }

        private static ClassicWikiMigrationPackage CreatePlannedCanaryPackage()
        {
            var source = CreateCanaryExport();
            source.Snapshot.Dependencies = ClassicWikiReferenceInventory
                .CaptureReferenceOnly(source.Snapshot)
                .ToList();
            source.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(source.Snapshot);

            return new ClassicWikiMigrationPlanner().PlanOffline(
                ClassicWikiTestFactory.TargetWebId,
                "https://a830edad9050849cupcollect.sharepoint.com/teams/office_rdx-ccd35-test/rm",
                "/teams/office_rdx-ccd35-test/rm",
                source,
                new PagePlanningOptions
                {
                    TargetPageServerRelativeUrl = "/teams/office_rdx-ccd35-test/rm/SitePages/Mac Office Release Wiki.aspx"
                });
        }

        private static ClassicWikiExportPackage CreateCanaryExport()
        {
            var source = ClassicWikiTestFactory.CreatePackage(
                CanaryWikiField,
                119,
                "/teams/office_rdx/rm/SitePages/Mac Office Release Wiki.aspx");
            source.Snapshot.Source.WebUrl = "https://microsoft.sharepoint.com/teams/office_rdx/rm";
            source.Snapshot.Source.WebServerRelativeUrl = "/teams/office_rdx/rm";
            source.Snapshot.LibraryServerRelativeUrl = "/teams/office_rdx/rm/SitePages";
            source.Snapshot.CapturePolicy = new PageCaptureOptions
            {
                SourcePageServerRelativeUrl = source.Snapshot.Source.PageServerRelativeUrl,
                IncludeWebParts = true,
                MaximumDependencyBytes = 10 * 1024 * 1024
            };
            source.Snapshot.Source.FileUniqueId = new Guid("9bf828fc-5f24-481a-96ee-ade42d84a58a");
            source.Snapshot.Source.VersionLabel = "17.0";
            source.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(source.Snapshot);
            return source;
        }

        private static ClassicWikiFreshTargetEvidence CreateDependencyEvidence(
            ClassicWikiMigrationPackage package)
        {
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            EnsureTargetCapturePolicy(evidence);
            evidence.Recapture.Snapshot.Dependencies = ClassicWikiReferenceInventory
                .CaptureReferenceOnly(evidence.Recapture.Snapshot)
                .ToList();
            return evidence;
        }

        private static void EnsureTargetCapturePolicy(ClassicWikiFreshTargetEvidence evidence)
        {
            evidence.Recapture.Snapshot.CapturePolicy = new PageCaptureOptions
            {
                SourcePageServerRelativeUrl = evidence.Recapture.Snapshot.Source.PageServerRelativeUrl,
                IncludeWebParts = true,
                MaximumDependencyBytes = 10 * 1024 * 1024
            };
        }
    }
}
