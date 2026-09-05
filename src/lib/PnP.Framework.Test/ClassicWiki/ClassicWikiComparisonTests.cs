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
            source.Snapshot.WebParts.Add(new ClassicWebPartSnapshot { Id = Guid.NewGuid(), Title = "B", TypeName = "T", ZoneId = "Top", ZoneIndex = 0 });

            var target = ClassicWikiTestFactory.CreatePackage("test content", 119);
            target.Snapshot.WebParts.Add(new ClassicWebPartSnapshot { Id = Guid.NewGuid(), Title = "B", TypeName = "T", ZoneId = "Bottom", ZoneIndex = 0 });

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
            source.Snapshot.Dependencies.Add(new PageReferenceSnapshot { Id = "ref-1", Kind = PageReferenceKind.Image, SourceServerRelativeUrl = "/sites/demo/img/logo.png" });

            var target = ClassicWikiTestFactory.CreatePackage("test", 119);
            target.Snapshot.Dependencies.Add(new PageReferenceSnapshot { Id = "ref-1", Kind = PageReferenceKind.Anchor, SourceServerRelativeUrl = "/sites/demo/img/logo.png" });

            var comparison = ClassicWikiComparison.Compare(source, target);
            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.DependenciesMatched);
            Assert.IsTrue(comparison.Differences.Count > 0);
        }
    }
}
