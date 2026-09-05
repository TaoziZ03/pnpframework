using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Packaging;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Verification;
using System.Linq;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiFreshVerificationTests
    {
        [DataTestMethod]
        [DataRow(101)]
        [DataRow(119)]
        public void FreshVerificationRequiresActualContentTypeAndLibraryIdentity(int template)
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage(template);
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsTrue(result.Passed, string.Join(" | ", result.Differences));
            Assert.IsTrue(result.ContentTypeMatched);
            Assert.IsTrue(result.LibraryMatched);
            Assert.IsTrue(result.RuntimeMatched);
        }

        [DataTestMethod]
        [DataRow(101, "Documents")]
        [DataRow(119, "SitePages")]
        public void PlanningSealsActualLibraryRootSeparatelyFromNestedFolder(int template, string libraryLeaf)
        {
            var sourcePath = $"/sites/demo/{libraryLeaf}/Dept/Welcome.aspx";
            var export = ClassicWikiTestFactory.CreatePackage("approved content", template, sourcePath);
            var package = ClassicWikiMigrationPlanner.PlanCore(
                ClassicWikiTestFactory.TargetWebId,
                "https://contoso.sharepoint.com/sites/target",
                "/sites/target",
                export,
                new PagePlanningOptions());

            Assert.AreEqual($"/sites/target/{libraryLeaf}", package.Plan.TargetLocation.TargetLibraryServerRelativeUrl);
            Assert.AreEqual($"/sites/target/{libraryLeaf}/Dept", package.Plan.TargetLocation.TargetFolderServerRelativeUrl);
            Assert.AreEqual($"/sites/target/{libraryLeaf}/Dept/Welcome.aspx", package.Plan.TargetPageServerRelativeUrl);
        }

        [TestMethod]
        public void ResumedOwnedPageFailsFreshVerificationOnTitleDrift()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.Recapture.Snapshot.Fields
                .First(value => value.InternalName == "Title")
                .Value = "drifted title";

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.FieldsMatched);
            Assert.IsTrue(result.Differences.Any(value => value.Contains("Title field mismatch")));
        }

        [TestMethod]
        public void FreshVerificationRejectsWrongTargetWebEvenWhenPagePathMatches()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.Recapture.Snapshot.Source.WebUrl = "https://contoso.sharepoint.com/sites/wrong";

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.TargetIdentityMatched);
        }

        [TestMethod]
        public void FreshVerificationFailsClosedWithoutIndependentContext()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.IndependentContext = false;

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsTrue(result.Differences.Any(value => value.Contains("independent target context")));
        }

        [TestMethod]
        public void PackageAdmissionRejectsMissingTargetWebIdentity()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            package.Plan.TargetLocation.TargetWebId = System.Guid.Empty;
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);

            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                ClassicWikiPackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void FreshVerificationRejectsWrongContentTypeAndLibraryTemplate()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage(119);
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.Recapture.Snapshot.Source.ContentTypeId = "0x0101";
            evidence.Recapture.Snapshot.LibraryBaseTemplate = 101;

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.ContentTypeMatched);
            Assert.IsFalse(result.LibraryMatched);
        }

        [TestMethod]
        public void FreshVerificationRejectsRuntimeOrOwnershipDrift()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.Recapture.Snapshot.Runtime.AdapterId = PageRuntimeAdapterIds.WebPartPage;
            evidence.FileProperties[ClassicWikiTargetOwnership.SourceSnapshotDigestPropertyName] = "drifted";

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.RuntimeMatched);
            Assert.IsFalse(result.OwnershipMatched);
        }

        [TestMethod]
        public void FreshVerificationRejectsHiddenAndDigestSubstitution()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            const string approvedXml = "<webPart marker='approved' />";
            package.Plan.WebParts.Add(new ClassicWikiWebPartPlacementPlan
            {
                Title = "Approved",
                TypeName = "Approved.Type",
                ZoneId = "Bottom",
                TargetZoneIndex = 0,
                Hidden = false,
                Xml = approvedXml
            });
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.FileProperties[ClassicWikiTargetOwnership.PlanDigestPropertyName] = package.PlanDigest;
            evidence.Recapture.Snapshot.WebParts.Add(new ClassicWebPartSnapshot
            {
                Title = "Approved",
                TypeName = "Approved.Type",
                ZoneId = "Bottom",
                ZoneIndex = 0,
                Hidden = true,
                ExportXml = "<webPart marker='substituted' />",
                ExportSha256 = PageDigest.ComputeSha256("<webPart marker='substituted' />")
            });

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.WebPartsMatched);
        }

        [TestMethod]
        public void FreshVerificationRejectsDependencyUrlDrift()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            package.Plan.Dependencies.Add(new ClassicWikiDependencyPlan
            {
                SourceId = "source-id",
                Consumer = "img[src]",
                Kind = PageReferenceKind.Image,
                SourceOriginalValue = "/sites/demo/img/logo.png",
                SourceOriginalUrl = "/sites/demo/img/logo.png",
                TargetOriginalValue = "/sites/target/img/logo.png",
                TargetAbsoluteUrl = "https://contoso.sharepoint.com/sites/target/img/logo.png",
                TargetServerRelativeUrl = "/sites/target/img/logo.png"
            });
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.FileProperties[ClassicWikiTargetOwnership.PlanDigestPropertyName] = package.PlanDigest;
            evidence.Recapture.Snapshot.Dependencies.Add(new PageReferenceSnapshot
            {
                Id = PageDigest.ComputeSha256("img[src]\nhttps://contoso.sharepoint.com/sites/target/img/logo.png"),
                Consumer = "img[src]",
                Kind = PageReferenceKind.Image,
                OriginalValue = "/sites/target/img/logo.png",
                SourceAbsoluteUrl = "https://contoso.sharepoint.com/sites/target/img/drift.png",
                SourceServerRelativeUrl = "/sites/target/img/drift.png"
            });

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.DependenciesMatched);
        }

        [TestMethod]
        public void FreshVerificationFailsClosedWhenLifecycleOrSecurityEvidenceIsMissing()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.Recapture.Snapshot.Lifecycle = null;
            evidence.Recapture.Snapshot.Security = null;

            var result = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsFalse(result.Passed);
            Assert.IsFalse(result.LifecycleMatched);
            Assert.IsFalse(result.SecurityMatched);
        }

        [TestMethod]
        public void StorageImporterLeavesRuntimeAndAcceptancePendingWithoutExclusions()
        {
            Assert.AreEqual(RuntimeVerificationStatus.Pending, ClassicWikiImportStatusPolicy.RuntimeStatus);
            Assert.AreEqual(
                MigrationAcceptanceStatus.Pending,
                ClassicWikiImportStatusPolicy.Acceptance(
                    storagePassed: true,
                    runtimeStatus: RuntimeVerificationStatus.Pending,
                    hasExplicitExclusions: false));
            Assert.AreEqual(
                MigrationAcceptanceStatus.Rejected,
                ClassicWikiImportStatusPolicy.Acceptance(
                    storagePassed: false,
                    runtimeStatus: RuntimeVerificationStatus.Pending,
                    hasExplicitExclusions: false));
            Assert.AreEqual(
                MigrationAcceptanceStatus.Accepted,
                ClassicWikiImportStatusPolicy.Acceptance(
                    storagePassed: true,
                    runtimeStatus: RuntimeVerificationStatus.Passed,
                    hasExplicitExclusions: false));
        }

        [TestMethod]
        public void DeferredFidelityProducesPartialAcceptance()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage(deferredField: true, deferredSecurity: true);
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            var storage = ClassicWikiFreshVerification.Evaluate(package, evidence);

            Assert.IsTrue(storage.Passed, string.Join(" | ", storage.Differences));
            Assert.IsTrue(ClassicWikiFreshVerification.HasExplicitExclusions(package));
            Assert.AreEqual(
                MigrationAcceptanceStatus.Pending,
                ClassicWikiImportStatusPolicy.Acceptance(
                    storagePassed: storage.Passed,
                    runtimeStatus: RuntimeVerificationStatus.Pending,
                    hasExplicitExclusions: true));
            Assert.AreEqual(
                MigrationAcceptanceStatus.PartiallyAccepted,
                ClassicWikiImportStatusPolicy.Acceptance(
                    storagePassed: storage.Passed,
                    runtimeStatus: RuntimeVerificationStatus.Passed,
                    hasExplicitExclusions: true));
        }

        [TestMethod]
        public void BlockedPackagesAreRejectedBeforeAnyCsomRequestRuns()
        {
            using var context = new ClientContext("https://contoso.sharepoint.com/sites/target");
            var quarantined = ClassicWikiTestFactory.CreateMigrationPackage();
            quarantined.State = ClassicWikiPackageState.Quarantined;
            var receipt = new ClassicWikiMigrationImporter().Import(context, quarantined, quarantined.PlanDigest);
            Assert.IsFalse(receipt.MutationStarted);
            Assert.AreEqual("PackageNotAdmissible", receipt.AdmissionFailure.Code);

            var snapshotBlocked = ClassicWikiTestFactory.CreateMigrationPackage();
            snapshotBlocked.Snapshot.Blockers.Add("Source request returned literal HTTP 403 without typed ingredient authorization evidence.");
            snapshotBlocked.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(snapshotBlocked.Snapshot);
            snapshotBlocked.Plan.SourceSnapshotDigest = snapshotBlocked.SnapshotDigest;
            snapshotBlocked.PlanDigest = ClassicWikiDigest.ComputePlanDigest(snapshotBlocked.Plan);
            receipt = new ClassicWikiMigrationImporter().Import(context, snapshotBlocked, snapshotBlocked.PlanDigest);
            Assert.IsFalse(receipt.MutationStarted);
            Assert.AreEqual("PackageNotAdmissible", receipt.AdmissionFailure.Code);

            var planBlocked = ClassicWikiTestFactory.CreateMigrationPackage();
            planBlocked.Plan.Blockers.Add("Blocked plan ingredient.");
            planBlocked.PlanDigest = ClassicWikiDigest.ComputePlanDigest(planBlocked.Plan);
            receipt = new ClassicWikiMigrationImporter().Import(context, planBlocked, planBlocked.PlanDigest);
            Assert.IsFalse(receipt.MutationStarted);
            Assert.AreEqual("PackageNotAdmissible", receipt.AdmissionFailure.Code);

            var reportBlocked = ClassicWikiTestFactory.CreateMigrationPackage();
            reportBlocked.Report.Status = "Blocked";
            receipt = new ClassicWikiMigrationImporter().Import(context, reportBlocked, reportBlocked.PlanDigest);
            Assert.IsFalse(receipt.MutationStarted);
            Assert.AreEqual("PackageNotAdmissible", receipt.AdmissionFailure.Code);

            var reportBlocker = ClassicWikiTestFactory.CreateMigrationPackage();
            reportBlocker.Report.Blockers.Add("Blocked report ingredient.");
            receipt = new ClassicWikiMigrationImporter().Import(context, reportBlocker, reportBlocker.PlanDigest);
            Assert.IsFalse(receipt.MutationStarted);
            Assert.AreEqual("PackageNotAdmissible", receipt.AdmissionFailure.Code);
        }

        [TestMethod]
        public void LifecyclePolicyIsStrictAndModeratedPublishRequiresApproval()
        {
            var actions = ClassicWikiLifecycleExecutor.Plan(
                ClassicWikiLifecyclePolicy.Publish,
                CheckOutType.Online,
                versioningEnabled: true,
                minorVersionsEnabled: true,
                moderationEnabled: true);
            CollectionAssert.AreEqual(
                new[]
                {
                    ClassicWikiLifecycleAction.CheckInMinor,
                    ClassicWikiLifecycleAction.Publish,
                    ClassicWikiLifecycleAction.Approve
                },
                actions.ToArray());

            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            package.Plan.LifecyclePolicy = (ClassicWikiLifecyclePolicy)999;
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                ClassicWikiPackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void NewAndExistingLibraryLifecycleMatrixCovers101And119Capabilities()
        {
            foreach (var template in new[] { 101, 119 })
            foreach (var newlyCreated in new[] { false, true })
            foreach (var versioning in new[] { false, true })
            foreach (var minorVersions in new[] { false, true })
            foreach (var moderation in new[] { false, true })
            {
                var package = ClassicWikiTestFactory.CreateMigrationPackage(template);
                if (newlyCreated)
                {
                    var creation = ClassicWikiTargetLocationMaterializer.BuildCreationInformation(
                        package.Plan.TargetLocation,
                        "/sites/target");
                    Assert.AreEqual(template, creation.TemplateType);
                }

                if (!versioning)
                {
                    Assert.ThrowsException<System.InvalidOperationException>(() =>
                        ClassicWikiLifecycleExecutor.Plan(
                            ClassicWikiLifecyclePolicy.Publish,
                            CheckOutType.Online,
                            versioning,
                            minorVersions,
                            moderation));
                    Assert.ThrowsException<System.InvalidOperationException>(() =>
                        ClassicWikiTargetLocationMaterializer.ValidateLoadedLibrary(
                            package.Plan.TargetLocation,
                            ClassicWikiLifecyclePolicy.Publish,
                            template,
                            package.Plan.TargetLocation.TargetLibraryServerRelativeUrl,
                            enableVersioning: false));
                    continue;
                }

                var actions = ClassicWikiLifecycleExecutor.Plan(
                    ClassicWikiLifecyclePolicy.Publish,
                    CheckOutType.Online,
                    versioning,
                    minorVersions,
                    moderation).ToArray();
                Assert.AreEqual(
                    minorVersions ? ClassicWikiLifecycleAction.CheckInMinor : ClassicWikiLifecycleAction.CheckInMajor,
                    actions[0],
                    $"template={template}; new={newlyCreated}; minor={minorVersions}; moderation={moderation}");
                Assert.AreEqual(minorVersions, actions.Contains(ClassicWikiLifecycleAction.Publish));
                Assert.AreEqual(moderation, actions.Contains(ClassicWikiLifecycleAction.Approve));

                var alreadyCheckedIn = ClassicWikiLifecycleExecutor.Plan(
                    ClassicWikiLifecyclePolicy.Publish,
                    CheckOutType.None,
                    versioning,
                    minorVersions,
                    moderation).ToArray();
                Assert.IsFalse(alreadyCheckedIn.Contains(ClassicWikiLifecycleAction.CheckInMinor));
                Assert.IsFalse(alreadyCheckedIn.Contains(ClassicWikiLifecycleAction.CheckInMajor));
                Assert.AreEqual(minorVersions, alreadyCheckedIn.Contains(ClassicWikiLifecycleAction.Publish));
                Assert.AreEqual(moderation, alreadyCheckedIn.Contains(ClassicWikiLifecycleAction.Approve));
            }
        }

        [TestMethod]
        public void FreshLifecycleMatrixAcceptsMajorOnlyWithoutRequiringPublishApi()
        {
            foreach (var template in new[] { 101, 119 })
            foreach (var newlyCreated in new[] { false, true })
            foreach (var versioning in new[] { false, true })
            foreach (var minorVersions in new[] { false, true })
            foreach (var moderation in new[] { false, true })
            {
                var package = ClassicWikiTestFactory.CreateMigrationPackage(template);
                var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
                evidence.Recapture.Snapshot.LibraryEnableVersioning = versioning;
                evidence.Recapture.Snapshot.LibraryEnableMinorVersions = minorVersions;
                evidence.Recapture.Snapshot.LibraryEnableModeration = moderation;
                evidence.Recapture.Snapshot.Source.VersionLabel = newlyCreated ? "1.0" : "7.0";
                evidence.Recapture.Snapshot.Lifecycle.Level = "Published";
                evidence.Recapture.Snapshot.Lifecycle.CheckOutType = "None";
                evidence.Recapture.Snapshot.Lifecycle.ModerationStatus = moderation ? 0 : 2;

                var result = ClassicWikiFreshVerification.Evaluate(package, evidence);
                Assert.AreEqual(
                    versioning,
                    result.Passed,
                    $"template={template}; new={newlyCreated}; versioning={versioning}; minor={minorVersions}; moderation={moderation}; {string.Join(" | ", result.Differences)}");

                if (versioning && minorVersions)
                {
                    evidence.Recapture.Snapshot.Source.VersionLabel = "7.1";
                    result = ClassicWikiFreshVerification.Evaluate(package, evidence);
                    Assert.IsFalse(result.Passed, "A draft minor version cannot satisfy Publish.");
                }
                if (versioning && moderation)
                {
                    evidence.Recapture.Snapshot.Source.VersionLabel = "7.0";
                    evidence.Recapture.Snapshot.Lifecycle.ModerationStatus = 1;
                    result = ClassicWikiFreshVerification.Evaluate(package, evidence);
                    Assert.IsFalse(result.Passed, "A moderated page must be approved.");
                }
            }
        }

        [TestMethod]
        public void FreshModeratedPublishRequiresApprovedStatusInsteadOfSourceModerationParity()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            package.Snapshot.Lifecycle.ModerationStatus = 2;
            package.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            var evidence = ClassicWikiTestFactory.CreateFreshEvidence(package);
            evidence.FileProperties[ClassicWikiTargetOwnership.SourceSnapshotDigestPropertyName] = package.SnapshotDigest;
            evidence.FileProperties[ClassicWikiTargetOwnership.PlanDigestPropertyName] = package.PlanDigest;
            evidence.Recapture.Snapshot.LibraryEnableModeration = true;
            evidence.Recapture.Snapshot.Lifecycle.ModerationStatus = 0;

            var approved = ClassicWikiFreshVerification.Evaluate(package, evidence);
            Assert.IsTrue(approved.Passed, string.Join(" | ", approved.Differences));

            evidence.Recapture.Snapshot.Lifecycle.ModerationStatus = 2;
            var pending = ClassicWikiFreshVerification.Evaluate(package, evidence);
            Assert.IsFalse(pending.Passed);
            Assert.IsFalse(pending.LifecycleMatched);
        }

        [TestMethod]
        public void SealedLibraryUrlAndExactPageCompositionAreEnforced()
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            package.Plan.TargetLocation.TargetLibraryTitle = "Friendly Display Name";
            package.Plan.TargetLocation.TargetLibraryServerRelativeUrl = "/sites/target/SealedWiki";
            package.Plan.TargetLocation.TargetFolderServerRelativeUrl = "/sites/target/SealedWiki/Dept";
            package.Plan.TargetLocation.FileName = "Page.aspx";
            package.Plan.TargetPageServerRelativeUrl = "/sites/target/SealedWiki/Dept/Page.aspx";
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);

            var creation = ClassicWikiTargetLocationMaterializer.BuildCreationInformation(
                package.Plan.TargetLocation,
                "/sites/target");
            Assert.AreEqual("Friendly Display Name", creation.Title);
            Assert.AreEqual("SealedWiki", creation.Url);
            Assert.ThrowsException<System.InvalidOperationException>(() =>
                ClassicWikiTargetLocationMaterializer.ValidateLoadedLibrary(
                    package.Plan.TargetLocation,
                    ClassicWikiLifecyclePolicy.Publish,
                    actualTemplate: 119,
                    actualServerRelativeUrl: "/sites/target/SealedWiki",
                    enableVersioning: false));

            package.Plan.TargetLocation.FileName = "Other.aspx";
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                ClassicWikiPackageValidator.ValidateMigration(package));

            package.Plan.TargetLocation.FileName = string.Empty;
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                ClassicWikiPackageValidator.ValidateMigration(package));
        }
    }
}
