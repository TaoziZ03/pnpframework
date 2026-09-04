using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Discovery;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.Packaging;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Pages.Security;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiMigrationTests
    {
        [TestMethod]
        public void DiscoveryIdentifiesClassicWikiContentTypeAndRejectsPublishingLineage()
        {
            Assert.IsTrue(ClassicWikiPageDiscovery.IsClassicWikiContentType("0x010108"));
            Assert.IsTrue(ClassicWikiPageDiscovery.IsClassicWikiContentType("0x0101080011223344"));
            Assert.IsFalse(ClassicWikiPageDiscovery.IsClassicWikiContentType("0x010100C568DB52D9")); // EnterpriseWiki
            Assert.IsFalse(ClassicWikiPageDiscovery.IsClassicWikiContentType("0x010100F27161A3C2")); // ProjectPage
            Assert.IsFalse(ClassicWikiPageDiscovery.IsClassicWikiContentType(null));
        }

        [TestMethod]
        public void DiscoveryIdentifiesClassicWikiPageByInheritedPageDirective()
        {
            Assert.IsTrue(ClassicWikiPageDiscovery.IsClassicWikiPage(
                "Microsoft.SharePoint.WebPartPages.WikiEditPage, Microsoft.SharePoint",
                null));
            Assert.IsTrue(ClassicWikiPageDiscovery.IsClassicWikiPage(
                "Microsoft.SharePoint.WebPartPages.WikiEditPage",
                "0x010108"));
            Assert.IsFalse(ClassicWikiPageDiscovery.IsClassicWikiPage(
                "Microsoft.SharePoint.Publishing.TemplateRedirectionPage",
                "0x010100C568DB52D9"));
        }

        [TestMethod]
        public void DiscoveryModelsLibraryTemplatesForWikiPagesVersusDocumentLibrary()
        {
            Assert.IsTrue(ClassicWikiPageDiscovery.IsClassicWikiLibrary(119));
            Assert.IsFalse(ClassicWikiPageDiscovery.IsClassicWikiLibrary(101));
            Assert.IsFalse(ClassicWikiPageDiscovery.IsClassicWikiLibrary(850)); // Pages library
        }

        [TestMethod]
        public void RuntimeResolverIdentifiesWikiEditPageAsRuntimeWiki()
        {
            var artifact = new PageArtifactSnapshot
            {
                PageDirective = new PageDirectiveSnapshot
                {
                    Inherits = "Microsoft.SharePoint.WebPartPages.WikiEditPage, Microsoft.SharePoint"
                }
            };

            var runtime = PageRuntimeResolver.Resolve(artifact, null, "0x010108");

            Assert.AreEqual(PageRuntimeAdapterIds.Wiki, runtime.AdapterId);
            Assert.AreEqual(PageRuntimeDetectionSource.PageDirective, runtime.DetectionSource);
            Assert.AreEqual(PageRuntimeResolutionState.Resolved, runtime.ResolutionState);
        }

        [TestMethod]
        public void WikiFieldWritePolicyBuildsExactAndEntitySafeLiteralBrackets()
        {
            var rawContent = "Welcome to the [[Wiki Page]] with an external link and literal [[escaped brackets]].";
            var plan = WikiFieldWritePolicy.Build(rawContent);

            Assert.AreEqual(rawContent, plan.ExactValue);
            Assert.AreEqual("Welcome to the &#91;&#91;Wiki Page&#93;&#93; with an external link and literal &#91;&#91;escaped brackets&#93;&#93;.", plan.EntitySafeValue);
            Assert.AreEqual(PageDigest.ComputeSha256(rawContent), plan.ExpectedStoredSha256);
        }

        [TestMethod]
        public void WikiFieldWritePolicyHandlesEmptyAndNullContentGracefully()
        {
            var nullPlan = WikiFieldWritePolicy.Build(null);
            Assert.AreEqual(string.Empty, nullPlan.ExactValue);
            Assert.AreEqual(string.Empty, nullPlan.EntitySafeValue);
            Assert.AreEqual(PageDigest.ComputeSha256(string.Empty), nullPlan.ExpectedStoredSha256);

            var emptyPlan = WikiFieldWritePolicy.Build(string.Empty);
            Assert.AreEqual(string.Empty, emptyPlan.ExactValue);
            Assert.AreEqual(string.Empty, emptyPlan.EntitySafeValue);
        }

        [TestMethod]
        public void TargetOwnershipMatchesApprovedPlanProvenance()
        {
            var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ClassicWikiTargetOwnership.OriginalIdentifierPropertyName] = "urn:pnp:spo-wiki-page:v1:site:web:file",
                [ClassicWikiTargetOwnership.SourceSnapshotDigestPropertyName] = "abc123snap",
                [ClassicWikiTargetOwnership.PlanDigestPropertyName] = "def456plan"
            };

            Assert.IsTrue(ClassicWikiTargetOwnership.MatchesApprovedPlan(
                properties,
                "urn:pnp:spo-wiki-page:v1:site:web:file",
                "abc123snap",
                "def456plan"));

            Assert.IsFalse(ClassicWikiTargetOwnership.MatchesApprovedPlan(
                properties,
                "urn:pnp:spo-wiki-page:v1:different:identity",
                "abc123snap",
                "def456plan"));

            Assert.IsFalse(ClassicWikiTargetOwnership.MatchesApprovedPlan(
                properties,
                "urn:pnp:spo-wiki-page:v1:site:web:file",
                "tamperedSnap",
                "def456plan"));

            Assert.IsFalse(ClassicWikiTargetOwnership.MatchesApprovedPlan(
                properties,
                "urn:pnp:spo-wiki-page:v1:site:web:file",
                "abc123snap",
                "tamperedPlan"));
        }

        [TestMethod]
        public void SerializerRoundTripsExportAndMigrationPackages()
        {
            var bundle = CreateSampleBundle("Hello [[World]]", 119);
            var exportPackage = new ClassicWikiExportPackage
            {
                SchemaVersion = ClassicWikiPackageContract.ExportSchemaVersion,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = new ClassicWikiWorkflowSelection { ProfileId = "profile.classic-wiki" },
                SelectionDigest = "sel_digest_123",
                Snapshot = bundle,
                SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(bundle)
            };

            var json = ClassicWikiPackageSerializer.Serialize(exportPackage);
            var deserializedExport = ClassicWikiPackageSerializer.Deserialize<ClassicWikiExportPackage>(json);

            Assert.AreEqual(ClassicWikiPackageContract.ExportSchemaVersion, deserializedExport.SchemaVersion);
            Assert.AreEqual(exportPackage.SnapshotDigest, deserializedExport.SnapshotDigest);
            Assert.AreEqual(bundle.WikiField, deserializedExport.Snapshot.WikiField);
            Assert.AreEqual(bundle.LibraryBaseTemplate, deserializedExport.Snapshot.LibraryBaseTemplate);

            var migrationPlan = new ClassicWikiMigrationPlan
            {
                OriginalIdentifier = "urn:pnp:spo-wiki:test",
                SourceSnapshotDigest = exportPackage.SnapshotDigest,
                TargetPageServerRelativeUrl = "/sites/target/SitePages/Hello.aspx",
                TargetLocation = new ClassicWikiTargetLocationPlan
                {
                    TargetWebUrl = "https://contoso.sharepoint.com/sites/target",
                    TargetLibraryServerRelativeUrl = "/sites/target/SitePages",
                    TargetLibraryTitle = "Site Pages",
                    TargetLibraryTemplate = 119,
                    FileName = "Hello.aspx"
                },
                WikiFieldPlan = WikiFieldWritePolicy.Build(bundle.WikiField)
            };

            var migrationPackage = new ClassicWikiMigrationPackage
            {
                SchemaVersion = ClassicWikiPackageContract.MigrationSchemaVersion,
                PlannedAtUtc = DateTimeOffset.UtcNow,
                ExportSchemaVersion = ClassicWikiPackageContract.ExportSchemaVersion,
                ExportedAtUtc = exportPackage.ExportedAtUtc,
                State = ClassicWikiPackageState.Planned,
                Selection = exportPackage.Selection,
                SelectionDigest = exportPackage.SelectionDigest,
                Snapshot = bundle,
                Plan = migrationPlan,
                SnapshotDigest = exportPackage.SnapshotDigest,
                PlanDigest = ClassicWikiDigest.ComputePlanDigest(migrationPlan),
                Report = new ClassicWikiMigrationReport { Status = "Ready" }
            };

            var migJson = ClassicWikiPackageSerializer.Serialize(migrationPackage);
            var deserializedMig = ClassicWikiPackageSerializer.Deserialize<ClassicWikiMigrationPackage>(migJson);

            Assert.AreEqual(ClassicWikiPackageContract.MigrationSchemaVersion, deserializedMig.SchemaVersion);
            Assert.AreEqual(migrationPackage.PlanDigest, deserializedMig.PlanDigest);
            Assert.AreEqual(119, deserializedMig.Plan.TargetLocation.TargetLibraryTemplate);
        }

        [TestMethod]
        public void PackageValidatorRejectsMismatchedSchemaVersion()
        {
            var exportPackage = new ClassicWikiExportPackage
            {
                SchemaVersion = "invalid-schema/v9",
                Snapshot = CreateSampleBundle("test", 119),
                SnapshotDigest = "digest"
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                ClassicWikiPackageValidator.ValidateExport(exportPackage));
        }

        [TestMethod]
        public void ComparisonPassesOnExactWikiFieldMatch()
        {
            var source = CreateExportPackage("Content with [[Link]] and markup", 119);
            var target = CreateExportPackage("Content with [[Link]] and markup", 119);

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsTrue(comparison.Passed);
            Assert.IsTrue(comparison.WikiContentMatched);
            Assert.IsTrue(comparison.CanariesPassed.Contains("ExactWikiFieldMatch"));
            Assert.IsTrue(comparison.CanariesPassed.Contains("FolderAndFileNameFidelity"));
        }

        [TestMethod]
        public void ComparisonPassesOnBracketNormalizationFidelity()
        {
            var source = CreateExportPackage("Content with [[Link]] and [[Topic]]", 119);
            // Target SharePoint normalized literal brackets to entity-encoded &#91;&#91; and &#93;&#93;
            var target = CreateExportPackage("Content with &#91;&#91;Link&#93;&#93; and &#91;&#91;Topic&#93;&#93;", 119);

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsTrue(comparison.Passed);
            Assert.IsTrue(comparison.WikiContentMatched);
            Assert.IsTrue(comparison.BracketNormalizationMatched);
            Assert.IsTrue(comparison.CanariesPassed.Contains("BracketNormalizationMatch"));
        }

        [TestMethod]
        public void ComparisonPassesEmptyContentCanary()
        {
            var source = CreateExportPackage(string.Empty, 119);
            var target = CreateExportPackage(string.Empty, 119);

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsTrue(comparison.Passed);
            Assert.IsTrue(comparison.EmptyContentPreserved);
            Assert.IsTrue(comparison.CanariesPassed.Contains("EmptyContentPreserved"));
        }

        [TestMethod]
        public void ComparisonPreservesNestedFolderHierarchy()
        {
            var source = CreateExportPackage("test", 119, "/sites/demo/SitePages/Dept/SubDept/Page.aspx");
            var target = CreateExportPackage("test", 119, "/sites/target/SitePages/Dept/SubDept/Page.aspx");

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsTrue(comparison.Passed);
            Assert.IsTrue(comparison.NestedFoldersMatched);
            Assert.IsTrue(comparison.CanariesPassed.Contains("FolderAndFileNameFidelity"));
        }

        [TestMethod]
        public void ComparisonCanaryFailsOnContentMismatch()
        {
            var source = CreateExportPackage("Original content", 119);
            var target = CreateExportPackage("Corrupted target content", 119);

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsFalse(comparison.Passed);
            Assert.IsFalse(comparison.WikiContentMatched);
            Assert.AreEqual(1, comparison.Differences.Count);
        }

        [TestMethod]
        public void ComparisonCanaryVerifiesWebPartsAndDependenciesCounts()
        {
            var wp1 = new ClassicWebPartSnapshot
            {
                Id = Guid.NewGuid(),
                Title = "Script Editor",
                TypeName = "MSContentEditorWebPart",
                ZoneId = "Bottom",
                ZoneIndex = 0
            };
            var dep1 = new PageReferenceSnapshot
            {
                Id = "ref-1",
                OriginalValue = "/sites/demo/images/logo.png",
                SourceServerRelativeUrl = "/sites/demo/images/logo.png",
                Kind = PageReferenceKind.Image
            };

            var source = CreateExportPackage("content with webpart", 119);
            source.Snapshot.WebParts.Add(wp1);
            source.Snapshot.Dependencies.Add(dep1);

            var target = CreateExportPackage("content with webpart", 119);
            target.Snapshot.WebParts.Add(wp1);
            target.Snapshot.Dependencies.Add(dep1);

            var comparison = ClassicWikiComparison.Compare(source, target);

            Assert.IsTrue(comparison.Passed);
            Assert.IsTrue(comparison.WebPartsMatched);
            Assert.IsTrue(comparison.DependenciesMatched);
            Assert.IsTrue(comparison.CanariesPassed.Contains("WebPartCountAndPlacementFidelity"));
            Assert.IsTrue(comparison.CanariesPassed.Contains("DependencyCountFidelity"));
        }

        [TestMethod]
        public void LibraryTemplateModelingSupportsBoth119And101Variants()
        {
            var wiki119 = CreateExportPackage("Wiki in SitePages", 119);
            Assert.AreEqual(119, wiki119.Snapshot.LibraryBaseTemplate);

            var doclib101 = CreateExportPackage("Wiki in DocumentLibrary", 101);
            Assert.AreEqual(101, doclib101.Snapshot.LibraryBaseTemplate);
        }

        private static ClassicWikiExportPackage CreateExportPackage(string content, int libraryTemplate, string pageUrl = "/sites/demo/SitePages/Welcome.aspx")
        {
            var bundle = CreateSampleBundle(content, libraryTemplate, pageUrl);
            return new ClassicWikiExportPackage
            {
                SchemaVersion = ClassicWikiPackageContract.ExportSchemaVersion,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = new ClassicWikiWorkflowSelection(),
                SelectionDigest = "sel_digest",
                Snapshot = bundle,
                SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(bundle)
            };
        }

        private static ClassicWikiCaptureBundle CreateSampleBundle(string content, int libraryTemplate, string pageUrl = "/sites/demo/SitePages/Welcome.aspx")
        {
            return new ClassicWikiCaptureBundle
            {
                Source = new PageIdentity
                {
                    SiteId = Guid.NewGuid(),
                    WebId = Guid.NewGuid(),
                    WebUrl = "https://contoso.sharepoint.com/sites/demo",
                    WebServerRelativeUrl = "/sites/demo",
                    PageServerRelativeUrl = pageUrl,
                    ListItemId = 1,
                    FileUniqueId = Guid.NewGuid(),
                    ContentTypeId = "0x010108",
                    ContentTypeName = "Wiki Page",
                    Title = PagePath.GetFileName(pageUrl)
                },
                PageArtifact = new PageArtifactSnapshot
                {
                    PageDirective = new PageDirectiveSnapshot
                    {
                        Inherits = "Microsoft.SharePoint.WebPartPages.WikiEditPage, Microsoft.SharePoint"
                    }
                },
                Runtime = new PageRuntimeSnapshot
                {
                    AdapterId = PageRuntimeAdapterIds.Wiki,
                    ResolutionState = PageRuntimeResolutionState.Resolved
                },
                WikiField = content,
                WikiFieldSha256 = ClassicWikiDigest.ComputeSha256(content ?? string.Empty),
                LibraryBaseTemplate = libraryTemplate,
                LibraryTitle = libraryTemplate == 119 ? "Site Pages" : "Documents",
                LibraryServerRelativeUrl = "/sites/demo/SitePages",
                Fields = new List<PageFieldValueSnapshot>
                {
                    new PageFieldValueSnapshot { InternalName = "Title", Value = "Test Page" },
                    new PageFieldValueSnapshot { InternalName = "FileLeafRef", Value = PagePath.GetFileName(pageUrl) }
                },
                Lifecycle = new PageLifecycleSnapshot
                {
                    CheckOutType = "None",
                    Level = "Published",
                    CreatedUtc = DateTime.UtcNow.AddDays(-10),
                    ModifiedUtc = DateTime.UtcNow
                },
                Security = new PageSecuritySnapshot
                {
                    HasUniqueRoleAssignments = false
                }
            };
        }
    }
}
