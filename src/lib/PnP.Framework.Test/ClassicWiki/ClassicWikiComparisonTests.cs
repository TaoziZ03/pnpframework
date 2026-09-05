using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiComparisonTests
    {
        [TestMethod]
        public void ComparisonFailsOnWebPartPlacementMismatch()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test content", 119);
            source.Snapshot.WebParts.Add(CreateWebPart("Top", false, "source"));

            var target = ClassicWikiTestFactory.CreatePackage("test content", 119);
            target.Snapshot.WebParts.Add(CreateWebPart("Bottom", false, "source"));

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.WebPartsMatched);
            Assert.IsTrue(comparison.Differences.Count > 0);
        }

        [TestMethod]
        public void ComparisonFailsOnLifecycleMismatch()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test content", 119);
            source.Snapshot.Lifecycle = new PageLifecycleSnapshot { Level = "Published", CheckOutType = "None", ModerationStatus = 0 };

            var target = ClassicWikiTestFactory.CreatePackage("test content", 119);
            target.Snapshot.Lifecycle = new PageLifecycleSnapshot { Level = "Draft", CheckOutType = "None", ModerationStatus = 0 };

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.LifecycleMatched);
            Assert.IsTrue(comparison.Differences.Count > 0);
        }

        [TestMethod]
        public void ComparisonPassesOnExactLifecycleMatch()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test content", 119);
            source.Snapshot.Lifecycle = new PageLifecycleSnapshot { Level = "Published", CheckOutType = "None", ModerationStatus = 0 };

            var target = ClassicWikiTestFactory.CreatePackage("test content", 119);
            target.Snapshot.Lifecycle = new PageLifecycleSnapshot { Level = "Published", CheckOutType = "None", ModerationStatus = 0 };

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsTrue(comparison.Passed);
            Assert.IsTrue(comparison.LifecycleMatched);
            Assert.IsTrue(comparison.CanariesPassed.Contains("LifecycleFidelity"));
        }

        [TestMethod]
        public void ComparisonFailsOnSecurityMismatch()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test content", 119);
            source.Snapshot.Security = new PageSecuritySnapshot
            {
                HasUniqueRoleAssignments = true,
                RoleAssignments = new List<PageRoleAssignmentSnapshot> { new PageRoleAssignmentSnapshot { PrincipalLoginName = "user1", RoleDefinitionNames = new List<string> { "Contribute" } } }
            };

            var target = ClassicWikiTestFactory.CreatePackage("test content", 119);
            target.Snapshot.Security = new PageSecuritySnapshot { HasUniqueRoleAssignments = false };

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.SecurityMatched);
            Assert.IsTrue(comparison.Differences.Count > 0);
        }

        [TestMethod]
        public void ComparisonPassesOnExactSecurityMatch()
        {
            var roles = new List<PageRoleAssignmentSnapshot> { new PageRoleAssignmentSnapshot { PrincipalLoginName = "user1", RoleDefinitionNames = new List<string> { "Contribute" } } };
            var source = ClassicWikiTestFactory.CreatePackage("test content", 119);
            source.Snapshot.Security = new PageSecuritySnapshot { HasUniqueRoleAssignments = true, RoleAssignments = roles };

            var target = ClassicWikiTestFactory.CreatePackage("test content", 119);
            target.Snapshot.Security = new PageSecuritySnapshot { HasUniqueRoleAssignments = true, RoleAssignments = new List<PageRoleAssignmentSnapshot> { new PageRoleAssignmentSnapshot { PrincipalLoginName = "user1", RoleDefinitionNames = new List<string> { "Contribute" } } } };

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsTrue(comparison.Passed);
            Assert.IsTrue(comparison.SecurityMatched);
            Assert.IsTrue(comparison.CanariesPassed.Contains("SecurityFidelity"));
        }

        [TestMethod]
        public void ComparisonFailsOnNestedFolderMismatch()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test", 119, "/sites/demo/SitePages/Dept/Finance/Page.aspx");
            var target = ClassicWikiTestFactory.CreatePackage("test", 119, "/sites/target/SitePages/Dept/Marketing/Page.aspx");

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.NestedFoldersMatched);
            Assert.IsTrue(comparison.Differences.Count > 0);
        }

        [TestMethod]
        public void ComparisonFailsOnEmptyContentMismatch()
        {
            var source = ClassicWikiTestFactory.CreatePackage(string.Empty, 119);
            var target = ClassicWikiTestFactory.CreatePackage("Unexpected content", 119);

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.EmptyContentPreserved);
            Assert.IsTrue(comparison.Differences.Count > 0);
        }

        [TestMethod]
        public void ComparisonFailsOnDependencyMismatch()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test", 119);
            source.Snapshot.Dependencies.Add(CreateDependency(PageReferenceKind.Image, "/sites/demo/img/logo.png"));

            var target = ClassicWikiTestFactory.CreatePackage("test", 119);
            target.Snapshot.Dependencies.Add(CreateDependency(PageReferenceKind.Anchor, "/sites/demo/img/logo.png"));

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.DependenciesMatched);
            Assert.IsTrue(comparison.Differences.Count > 0);
        }

        [TestMethod]
        public void ComparisonFailsWhenWebPartHiddenOrDigestIsSubstituted()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test", 119);
            source.Snapshot.WebParts.Add(CreateWebPart("Bottom", false, "approved"));
            var target = ClassicWikiTestFactory.CreatePackage("test", 119);
            target.Snapshot.WebParts.Add(CreateWebPart("Bottom", true, "substituted"));

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.WebPartsMatched);
        }

        [TestMethod]
        public void ComparisonFailsWhenDependencyUrlDriftsDespiteMatchingIdentifier()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test", 119);
            var expected = CreateDependency(PageReferenceKind.Image, "/sites/demo/img/logo.png");
            source.Snapshot.Dependencies.Add(expected);
            var target = ClassicWikiTestFactory.CreatePackage("test", 119);
            var drifted = CreateDependency(PageReferenceKind.Image, "/sites/demo/img/other.png");
            drifted.Id = expected.Id;
            target.Snapshot.Dependencies.Add(drifted);

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.DependenciesMatched);
        }

        [TestMethod]
        public void ComparisonFailsClosedWhenLifecycleAndSecurityEvidenceAreMissing()
        {
            var source = ClassicWikiTestFactory.CreatePackage("test", 119);
            var target = ClassicWikiTestFactory.CreatePackage("test", 119);
            source.Snapshot.Lifecycle = null;
            target.Snapshot.Lifecycle = null;
            source.Snapshot.Security = null;
            target.Snapshot.Security = null;

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.LifecycleMatched);
            Assert.IsFalse(comparison.SecurityMatched);
        }

        private static ClassicWebPartSnapshot CreateWebPart(string zone, bool hidden, string exportMarker)
        {
            var xml = $"<webPart marker='{exportMarker}' />";
            return new ClassicWebPartSnapshot
            {
                Id = Guid.NewGuid(),
                Title = "B",
                TypeName = "T",
                ZoneId = zone,
                ZoneIndex = 0,
                Hidden = hidden,
                ExportXml = xml,
                ExportSha256 = PnP.Framework.Migration.Pages.Packaging.PageDigest.ComputeSha256(xml)
            };
        }

        private static PageReferenceSnapshot CreateDependency(PageReferenceKind kind, string path)
        {
            var absolute = "https://contoso.sharepoint.com" + path;
            const string consumer = "img[src]";
            return new PageReferenceSnapshot
            {
                Id = PnP.Framework.Migration.Pages.Packaging.PageDigest.ComputeSha256(consumer + "\n" + absolute),
                Consumer = consumer,
                Kind = kind,
                OriginalValue = path,
                SourceAbsoluteUrl = absolute,
                SourceServerRelativeUrl = path
            };
        }
    }
}
