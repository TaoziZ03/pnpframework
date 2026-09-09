using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Utilities.UnitTests.Model;
using PnP.Framework.Utilities.UnitTests.Web;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiTargetWriterTests
    {
        [DataTestMethod]
        [DataRow("/sites/target/SitePages/Welcome.aspx")]
        [DataRow("/sites/target/SitePages/Department/Welcome.aspx")]
        [DataRow("/sites/target/SitePages/Department/Mac Office Release Wiki.aspx")]
        public void ImportPassesAdmittedServerRelativeUrlToAddTemplateFile(string targetPageServerRelativeUrl)
        {
            var package = ClassicWikiTestFactory.CreateMigrationPackage();
            package.Plan.TargetPageServerRelativeUrl = targetPageServerRelativeUrl;
            package.Plan.TargetLocation.TargetFolderServerRelativeUrl = PagePath.GetDirectoryName(targetPageServerRelativeUrl);
            package.Plan.TargetLocation.FileName = PagePath.GetFileName(targetPageServerRelativeUrl);
            package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);
            ClassicWikiPackageValidator.ValidateMigration(package);
            var admittedPlan = CreateAdmittedPlan(package);
            var provider = CreateProvider(package);

            using var context = new ClientContext(package.Plan.TargetLocation.TargetWebUrl);
            context.WebRequestExecutorFactory = new MockWebRequestExecutorFactory(provider);

            var receipt = new ClassicWikiMigrationImporter().ImportAdmitted(
                context,
                package,
                package.PlanDigest,
                admittedPlan);

            Assert.AreEqual(
                MigrationExecutionStatus.FailedUnexpectedly,
                receipt.ExecutionStatus,
                "The mock intentionally stops after the creation request by reporting the fresh target file as absent.");

            var addTemplateFiles = provider.RequestBodies
                .Select(MockEntryResponseProvider.GetRequest)
                .SelectMany(request => request.ObjectPaths)
                .OfType<PnP.Framework.Utilities.UnitTests.Model.ObjectPathMethod>()
                .Where(path => string.Equals(path.Name, "AddTemplateFile", StringComparison.Ordinal))
                .ToList();

            Assert.AreEqual(
                1,
                addTemplateFiles.Count,
                string.Join(Environment.NewLine, receipt.Diagnostics.Concat(provider.RequestBodies)));
            var addTemplateFile = addTemplateFiles[0];

            Assert.AreEqual(
                package.Plan.TargetPageServerRelativeUrl,
                addTemplateFile.Parameters[0].Value,
                "The actual CSOM AddTemplateFile boundary must receive the admitted full server-relative file URL.");
        }

        private static AdmittedReproExecutionPlan CreateAdmittedPlan(ClassicWikiMigrationPackage package)
        {
            var observedAt = new DateTimeOffset(2026, 9, 9, 23, 0, 0, TimeSpan.Zero);
            return new AdmittedReproExecutionPlan
            {
                PlanDigest = package.PlanDigest,
                TargetIdentity = new Uri(package.Plan.TargetLocation.TargetWebUrl).GetLeftPart(UriPartial.Authority)
                    + package.Plan.TargetPageServerRelativeUrl,
                SourceVersion = new CurrentSourceVersionIdentity
                {
                    IdentityDigestSha256 = MigrationDigest.ComputeSha256("ccd235-source-identity"),
                    VersionDigestSha256 = MigrationDigest.ComputeSha256("ccd235-source-version"),
                    ETag = "\"ccd235,1\"",
                    LastModifiedUtc = observedAt.AddMinutes(-5),
                    VersionLabel = "17.0",
                    ObservedAtUtc = observedAt
                },
                Operations = new ReproOperationIds
                {
                    MutationOperationId = Guid.Parse("23500000-0000-0000-0000-000000000001"),
                    ReadbackOperationId = Guid.Parse("23500000-0000-0000-0000-000000000002"),
                    RuntimeOperationId = Guid.Parse("23500000-0000-0000-0000-000000000003"),
                    CleanupOperationId = Guid.Parse("23500000-0000-0000-0000-000000000004")
                }
            };
        }

        private static RecordingResponseProvider CreateProvider(ClassicWikiMigrationPackage package)
        {
            var webUrl = package.Plan.TargetLocation.TargetWebUrl;
            var libraryPath = package.Plan.TargetLocation.TargetLibraryServerRelativeUrl;
            var inner = new MockEntryResponseProvider();
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                PropertyName = "Web",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.Web",
                    ["Id"] = $"/Guid({package.Plan.TargetLocation.TargetWebId:D})/",
                    ["Url"] = webUrl,
                    ["ServerRelativeUrl"] = new Uri(webUrl).AbsolutePath
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                Method = "GetList",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.List",
                    ["Id"] = "/Guid(aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa)/",
                    ["Title"] = package.Plan.TargetLocation.TargetLibraryTitle,
                    ["BaseTemplate"] = package.Plan.TargetLocation.TargetLibraryTemplate,
                    ["ForceCheckout"] = false,
                    ["EnableVersioning"] = true,
                    ["EnableMinorVersions"] = false,
                    ["EnableModeration"] = false,
                    ["RootFolder"] = new Dictionary<string, object>
                    {
                        ["_ObjectType_"] = "SP.Folder",
                        ["ServerRelativeUrl"] = libraryPath
                    }
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                PropertyName = "Lists",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.ListCollection",
                    ["_Child_Items_"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["_ObjectType_"] = "SP.List",
                            ["_ObjectIdentity_"] = "list:sitepages",
                            ["BaseTemplate"] = package.Plan.TargetLocation.TargetLibraryTemplate,
                            ["RootFolder"] = new Dictionary<string, object>
                            {
                                ["_ObjectType_"] = "SP.Folder",
                                ["_ObjectIdentity_"] = "folder:sitepages",
                                ["ServerRelativeUrl"] = libraryPath
                            }
                        }
                    }
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                PropertyName = "Fields",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.FieldCollection",
                    ["_Child_Items_"] = Array.Empty<object>()
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                PropertyName = "Folders",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.FolderCollection",
                    ["_Child_Items_"] = Array.Empty<object>()
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                Method = "AddItemUsingPath",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.ListItem",
                    ["Id"] = 1
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                Method = "GetFolderByServerRelativePath",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.Folder",
                    ["Name"] = PagePath.GetFileName(package.Plan.TargetLocation.TargetFolderServerRelativeUrl),
                    ["ServerRelativeUrl"] = package.Plan.TargetLocation.TargetFolderServerRelativeUrl
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                Method = "GetFileByServerRelativePath",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.File",
                    ["Exists"] = false,
                    ["CheckOutType"] = 2,
                    ["Properties"] = new Dictionary<string, object>
                    {
                        ["_ObjectType_"] = "SP.PropertyValues"
                    }
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                Method = "AddTemplateFile",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.File",
                    ["Exists"] = true,
                    ["ServerRelativeUrl"] = package.Plan.TargetPageServerRelativeUrl
                }
            });
            inner.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                PropertyName = "ListItemAllFields",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.ListItem",
                    ["Id"] = 0
                }
            });
            return new RecordingResponseProvider(inner);
        }

        private sealed class RecordingResponseProvider : IMockResponseProvider
        {
            private readonly IMockResponseProvider inner;

            public RecordingResponseProvider(IMockResponseProvider inner)
            {
                this.inner = inner;
            }

            public IList<string> RequestBodies { get; } = new List<string>();

            public string GetResponse(string url, string verb, string body)
            {
                RequestBodies.Add(body);
                return inner.GetResponse(url, verb, body);
            }
        }
    }
}
