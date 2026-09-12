using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Security;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

[TestClass]
public class ClassicWikiRestSourceAdapterTests
{
    private const string WikiField =
        "<div>"
        + "<a href=\"/teams/source/SitePages/one.aspx\">One</a>"
        + "<a href=\"/teams/source/Docs/two.docx\">Two</a>"
        + "<a href=\"/teams/source/Docs/three.docx\">Three</a>"
        + "<a href=\"/teams/source/Docs/four.docx\">Four</a>"
        + "<a href=\"/teams/source/Docs/five.docx\">Five</a>"
        + "</div>";

    [TestMethod]
    public void SameInventoryAcceptsCanonicalCapturedReference()
    {
        var reconstructed = CreateInventory();
        var declared = CreateInventory();

        Assert.IsTrue(ClassicWikiRestSourceAdapter.SameInventory(declared, reconstructed));
    }

    [TestMethod]
    public void SameInventoryRejectsDeclaredStaleId()
    {
        var reconstructed = CreateInventory();
        var declared = CreateInventory();
        declared[0].Id = new string('0', 64);

        Assert.IsFalse(ClassicWikiRestSourceAdapter.SameInventory(declared, reconstructed));
    }

    [DataTestMethod]
    [DataRow(PageCaptureStatus.Failed)]
    [DataRow(PageCaptureStatus.NotReturned)]
    public void SameInventoryRejectsUnavailableDeclaredCaptureStatus(PageCaptureStatus status)
    {
        var reconstructed = CreateInventory();
        var declared = CreateInventory();
        declared[0].CaptureStatus = status;

        Assert.IsFalse(ClassicWikiRestSourceAdapter.SameInventory(declared, reconstructed));
    }

    [DataTestMethod]
    [DataRow(PageCaptureStatus.Failed, "SOURCE_REFERENCE_CAPTURE_FAILED")]
    [DataRow(PageCaptureStatus.NotReturned, "SOURCE_REFERENCE_NOT_RETURNED")]
    public void AdaptReturnsOneDelegatedUnavailableAndFourIndependentRewrites(
        PageCaptureStatus status,
        string expectedReason)
    {
        var package = CreatePackage(status, "mixed");
        var admittedPlan = CreateAdmittedPlan(package, "mixed");
        var originalOperations = admittedPlan.Operations;

        var result = ClassicWikiRestSourceAdapter.Adapt(package, admittedPlan);

        Assert.AreEqual(5, result.Package.Snapshot.Dependencies.Count);
        Assert.AreEqual(5, result.Package.Plan.Dependencies.Count);
        Assert.AreEqual("Conditional", result.Package.Report.Status);
        Assert.AreEqual(1, result.DependencyDispositions.Count(value => value.Disposition == "Delegate"));
        Assert.AreEqual(4, result.DependencyDispositions.Count(value => value.Disposition == "Rewrite"));
        Assert.AreEqual(expectedReason, result.DependencyDispositions.Single(value => value.Disposition == "Delegate").ReasonCode);
        Assert.AreEqual(status, result.Package.Snapshot.Dependencies[0].CaptureStatus);
        Assert.AreEqual("Delegate", result.Package.Plan.Dependencies[0].Disposition);
        Assert.AreEqual(
            result.Package.Snapshot.Dependencies[0].OriginalValue,
            result.Package.Plan.Dependencies[0].TargetOriginalValue);
        Assert.AreEqual(
            "a830edad9050849cupcollect.sharepoint.com",
            new Uri(result.Package.Plan.Dependencies[0].TargetAbsoluteUrl).Host);
        Assert.AreEqual(
            "microsoft.sharepoint.com",
            new Uri(result.Package.Snapshot.Dependencies[0].SourceAbsoluteUrl).Host);
        StringAssert.Contains(result.Package.Plan.WikiFieldPlan.ExactValue, "/teams/source/SitePages/one.aspx");
        StringAssert.Contains(result.Package.Plan.WikiFieldPlan.ExactValue, "/teams/target/Docs/two.docx");
        var comparison = CompareDependencies(
            result.Package,
            ReadEmittedDependencies(result.Package));
        Assert.IsFalse(comparison.DependenciesMatched);
        Assert.IsTrue(comparison.Differences.Any(value => value.Contains("source disposition 'Delegate'")));
        Assert.IsFalse(comparison.Differences.Any(value => value.Contains("exact-semantics mismatch")));
        Assert.AreNotEqual(originalOperations.MutationOperationId, result.AdmittedPlan.Operations.MutationOperationId);
        Assert.AreNotEqual(originalOperations.ReadbackOperationId, result.AdmittedPlan.Operations.ReadbackOperationId);
        Assert.AreNotEqual(originalOperations.RuntimeOperationId, result.AdmittedPlan.Operations.RuntimeOperationId);
        Assert.AreNotEqual(originalOperations.CleanupOperationId, result.AdmittedPlan.Operations.CleanupOperationId);
    }

    [TestMethod]
    public void AdaptRestoresOnlyTheUnavailableCanonicalValueWhenAnotherReferenceHasAQuerySuffix()
    {
        const string sourceValue = "/teams/source/SitePages/one.aspx";
        const string sourceQueryValue = "/teams/source/SitePages/one.aspx?view=two";
        const string targetValue = "/teams/target/SitePages/one.aspx";
        const string targetQueryValue = "/teams/target/SitePages/one.aspx?view=two";
        var wikiField = "<div>"
            + "<a href=\"" + sourceValue + "\">Unavailable</a>"
            + "<a href=\"" + sourceQueryValue + "\">Independent</a>"
            + "<area href=\"" + sourceValue + "\">Other consumer</area>"
            + "<span>literal " + sourceValue + "</span>"
            + "</div>";
        var package = CreatePackage(PageCaptureStatus.Failed, "query-isolation", wikiField: wikiField);

        var result = ClassicWikiRestSourceAdapter.Adapt(
            package,
            CreateAdmittedPlan(package, "query-isolation"));

        Assert.AreEqual(3, result.Package.Plan.Dependencies.Count);
        Assert.AreEqual(1, result.Package.Plan.Dependencies.Count(value => value.Disposition == "Delegate"));
        Assert.AreEqual(2, result.Package.Plan.Dependencies.Count(value => value.Disposition == "Rewrite"));
        StringAssert.Contains(result.Package.Plan.WikiFieldPlan.ExactValue, "href=\"" + sourceValue + "\"");
        StringAssert.Contains(result.Package.Plan.WikiFieldPlan.ExactValue, "href=\"" + targetQueryValue + "\"");
        StringAssert.Contains(result.Package.Plan.WikiFieldPlan.ExactValue, "<area href=\"" + targetValue + "\"");
        StringAssert.Contains(result.Package.Plan.WikiFieldPlan.ExactValue, "<span>literal " + targetValue + "</span>");
        Assert.IsFalse(result.Package.Plan.WikiFieldPlan.ExactValue.Contains(sourceQueryValue));

        var delegatePlan = result.Package.Plan.Dependencies.Single(value => value.Disposition == "Delegate");
        var queryRewritePlan = result.Package.Plan.Dependencies.Single(value =>
            value.Disposition == "Rewrite" && value.Consumer == "a[href]");
        var otherConsumerPlan = result.Package.Plan.Dependencies.Single(value => value.Consumer == "area[href]");
        Assert.AreEqual(sourceValue, delegatePlan.TargetOriginalValue);
        Assert.AreEqual(sourceValue, delegatePlan.TargetServerRelativeUrl);
        Assert.AreEqual(
            "https://a830edad9050849cupcollect.sharepoint.com" + sourceValue,
            delegatePlan.TargetAbsoluteUrl);
        Assert.AreEqual(targetQueryValue, queryRewritePlan.TargetOriginalValue);
        Assert.AreEqual(targetValue, otherConsumerPlan.TargetOriginalValue);

        var comparison = CompareDependencies(
            result.Package,
            ReadEmittedDependencies(result.Package));
        Assert.IsFalse(comparison.DependenciesMatched);
        Assert.IsTrue(comparison.Differences.Any(value => value.Contains("source disposition 'Delegate'")));
        Assert.IsFalse(comparison.Differences.Any(value => value.Contains("exact-semantics mismatch")));
    }

    [TestMethod]
    public void AdaptContinuesWithTheNextPageAfterAnUnavailableDependency()
    {
        var first = CreatePackage(PageCaptureStatus.Failed, "first");
        var firstResult = ClassicWikiRestSourceAdapter.Adapt(first, CreateAdmittedPlan(first, "first"));
        var second = CreatePackage(null, "second");

        var secondResult = ClassicWikiRestSourceAdapter.Adapt(second, CreateAdmittedPlan(second, "second"));

        Assert.AreEqual("Conditional", firstResult.Package.Report.Status);
        Assert.AreEqual("Ready", secondResult.Package.Report.Status);
        Assert.AreEqual(5, secondResult.DependencyDispositions.Count(value => value.Disposition == "Rewrite"));
    }

    [TestMethod]
    public void AdaptRejectsLimitedStatusAsInventoryCorruption()
    {
        var package = CreatePackage(PageCaptureStatus.CapturedWithLimitations, "limited");

        var error = Assert.ThrowsException<InvalidDataException>(() =>
            ClassicWikiRestSourceAdapter.Adapt(package, CreateAdmittedPlan(package, "limited")));

        Assert.AreEqual("classic_wiki_source_dependency_inventory_conflicts_with_wiki_field", error.Message);
    }

    [TestMethod]
    public void AdaptPreservesLiteralAccessDeniedEvidenceAndReason()
    {
        var package = CreatePackage(PageCaptureStatus.Failed, "denied", authorizationStatus: 403);

        var result = ClassicWikiRestSourceAdapter.Adapt(package, CreateAdmittedPlan(package, "denied"));

        var disposition = result.DependencyDispositions.Single(value => value.Disposition == "Delegate");
        Assert.AreEqual("ACCESS_DENIED_SKIPPED", disposition.ReasonCode);
        Assert.AreEqual(403, result.Package.Snapshot.Dependencies[0].AuthorizationEvidence.HttpStatusCode);
    }

    [TestMethod]
    public void AdaptRejectsForeignAuthorizationEvidence()
    {
        var package = CreatePackage(PageCaptureStatus.Failed, "foreign-evidence");
        package.Snapshot.Dependencies[0].AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
            "capture-page-reference-payload",
            "https://microsoft.sharepoint.com/teams/foreign/Docs/one.docx",
            403,
            new DateTimeOffset(2026, 9, 10, 8, 1, 0, TimeSpan.Zero));
        package.SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(package.Snapshot);
        package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
        package.PlanDigest = ClassicWikiDigest.ComputePlanDigest(package.Plan);

        var error = Assert.ThrowsException<InvalidDataException>(() =>
            ClassicWikiRestSourceAdapter.Adapt(
                package,
                CreateAdmittedPlan(package, "foreign-evidence")));

        Assert.AreEqual("classic_wiki_unavailable_dependency_authorization_evidence_invalid", error.Message);
    }

    [TestMethod]
    public void EntryWritesConditionalPackageAndDispositionManifest()
    {
        var package = CreatePackage(PageCaptureStatus.Failed, "entry");
        var output = Path.Combine(Path.GetTempPath(), "ccd248-entry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = ClassicWikiRestSourceAdaptationEntry.Run(
                "ccd248-entry",
                new string('1', 40),
                new string('2', 64),
                output,
                package,
                CreateAdmittedPlan(package, "entry"),
                CreateJsonOptions());

            Assert.AreEqual("Conditional", manifest.ReportStatus);
            Assert.AreEqual(1, manifest.DependencyDispositions.Count(value => value.Disposition == "Delegate"));
            Assert.IsTrue(File.Exists(Path.Combine(output, manifest.PackagePath)));
            Assert.IsTrue(File.Exists(Path.Combine(output, manifest.AdmittedPlanPath)));
            var manifestPath = Path.Combine(output, "source-adaptation-manifest-v1.json");
            Assert.IsTrue(File.Exists(manifestPath));
            var written = File.ReadAllText(manifestPath);
            StringAssert.Contains(written, "\"reportStatus\": \"Conditional\"");
            StringAssert.Contains(written, "\"disposition\": \"Delegate\"");
            StringAssert.Contains(written, "\"reasonCode\": \"SOURCE_REFERENCE_CAPTURE_FAILED\"");
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private static List<PageReferenceSnapshot> CreateInventory()
    {
        const string consumer = "a[href]";
        const string absoluteUrl = "https://microsoft.sharepoint.com/teams/office_rdx/rm/SitePages/Mac%20Office.aspx";
        return new List<PageReferenceSnapshot>
        {
            new PageReferenceSnapshot
            {
                Id = ClassicWikiDigest.ComputeSha256(consumer + "\n" + absoluteUrl),
                Consumer = consumer,
                Kind = PageReferenceKind.Anchor,
                OriginalValue = "/teams/office_rdx/rm/SitePages/Mac%20Office.aspx",
                SourceAbsoluteUrl = absoluteUrl,
                SourceServerRelativeUrl = "/teams/office_rdx/rm/SitePages/Mac Office.aspx",
                CaptureStatus = PageCaptureStatus.Captured
            }
        };
    }

    private static ClassicWikiMigrationPackage CreatePackage(
        PageCaptureStatus? unavailableStatus,
        string caseId,
        int? authorizationStatus = null,
        string wikiField = null)
    {
        wikiField ??= WikiField;
        var snapshot = new ClassicWikiCaptureBundle
        {
            CapturePolicy = new PageCaptureOptions
            {
                SourcePageServerRelativeUrl = "/teams/source/SitePages/" + caseId + ".aspx",
                IncludeWebParts = true,
                MaximumDependencyBytes = 1024 * 1024
            },
            Source = new PageIdentity
            {
                SiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                WebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                FileUniqueId = Guid.NewGuid(),
                WebUrl = "https://microsoft.sharepoint.com/teams/source",
                WebServerRelativeUrl = "/teams/source",
                PageServerRelativeUrl = "/teams/source/SitePages/" + caseId + ".aspx",
                ListItemId = 42,
                ContentTypeId = "0x010108",
                ContentTypeName = "Wiki Page",
                VersionLabel = "17.0",
                Title = caseId
            },
            WikiField = wikiField,
            WikiFieldSha256 = ClassicWikiDigest.ComputeSha256(wikiField),
            LibraryBaseTemplate = 119,
            LibraryTitle = "Site Pages",
            LibraryServerRelativeUrl = "/teams/source/SitePages",
            Fields = new List<PageFieldValueSnapshot>
            {
                new PageFieldValueSnapshot { InternalName = "Title", Value = caseId }
            },
            Security = new PageSecuritySnapshot { HasUniqueRoleAssignments = false }
        };
        snapshot.Dependencies = ClassicWikiReferenceInventory.CaptureReferenceOnly(snapshot).ToList();
        if (unavailableStatus.HasValue)
        {
            snapshot.Dependencies[0].CaptureStatus = unavailableStatus.Value;
        }
        if (authorizationStatus.HasValue)
        {
            snapshot.Dependencies[0].AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                "capture-page-reference-payload",
                snapshot.Dependencies[0].SourceAbsoluteUrl,
                authorizationStatus.Value,
                new DateTimeOffset(2026, 9, 10, 8, 1, 0, TimeSpan.Zero));
        }

        var export = new ClassicWikiExportPackage
        {
            SchemaVersion = ClassicWikiPackageContract.ExportSchemaVersion,
            ExportedAtUtc = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero),
            Selection = new ClassicWikiWorkflowSelection(),
            SelectionDigest = "ccd248-selection",
            Snapshot = snapshot,
            SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(snapshot)
        };
        return new ClassicWikiMigrationPlanner().PlanOffline(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "https://a830edad9050849cupcollect.sharepoint.com/teams/target",
            "/teams/target",
            export,
            new PagePlanningOptions
            {
                TargetPageServerRelativeUrl = "/teams/target/SitePages/" + caseId + ".aspx"
            });
    }

    private static IList<PageReferenceSnapshot> ReadEmittedDependencies(
        ClassicWikiMigrationPackage package)
    {
        var targetWeb = new Uri(package.Plan.TargetLocation.TargetWebUrl);
        var targetSnapshot = new ClassicWikiCaptureBundle
        {
            CapturePolicy = package.Snapshot.CapturePolicy,
            Source = new PageIdentity
            {
                WebId = package.Plan.TargetLocation.TargetWebId,
                WebUrl = package.Plan.TargetLocation.TargetWebUrl,
                WebServerRelativeUrl = Uri.UnescapeDataString(targetWeb.AbsolutePath).TrimEnd('/'),
                PageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl
            },
            WikiField = package.Plan.WikiFieldPlan.ExactValue,
            WikiFieldSha256 = ClassicWikiDigest.ComputeSha256(package.Plan.WikiFieldPlan.ExactValue)
        };
        return ClassicWikiReferenceInventory.CaptureReferenceOnly(targetSnapshot);
    }

    private static ClassicWikiComparisonResult CompareDependencies(
        ClassicWikiMigrationPackage package,
        IList<PageReferenceSnapshot> observed)
    {
        var verificationType = typeof(ClassicWikiComparisonResult).Assembly.GetType(
            "PnP.Framework.Migration.Pages.ClassicWiki.Verification.ClassicWikiFreshVerification",
            throwOnError: true);
        var compare = verificationType.GetMethod(
            "CompareDependencies",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(compare);
        var result = new ClassicWikiComparisonResult();
        compare.Invoke(null, new object[] { package.Plan.Dependencies, observed, result });
        return result;
    }

    private static AdmittedReproExecutionPlan CreateAdmittedPlan(
        ClassicWikiMigrationPackage package,
        string caseId)
    {
        var observedAt = new DateTimeOffset(2026, 9, 10, 8, 5, 0, TimeSpan.Zero);
        return new AdmittedReproExecutionPlan
        {
            PlanDigest = package.PlanDigest,
            TargetIdentity = new Uri(package.Plan.TargetLocation.TargetWebUrl).GetLeftPart(UriPartial.Authority)
                + package.Plan.TargetPageServerRelativeUrl,
            SourceVersion = new CurrentSourceVersionIdentity
            {
                IdentityDigestSha256 = MigrationDigest.ComputeSha256("identity:" + caseId),
                VersionDigestSha256 = MigrationDigest.ComputeSha256("version:" + caseId),
                ETag = "\"ccd248,1\"",
                VersionLabel = "17.0",
                ObservedAtUtc = observedAt
            },
            Operations = new ReproOperationIds
            {
                MutationOperationId = Guid.NewGuid(),
                ReadbackOperationId = Guid.NewGuid(),
                RuntimeOperationId = Guid.NewGuid(),
                CleanupOperationId = Guid.NewGuid()
            }
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var result = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        result.Converters.Add(new JsonStringEnumConverter());
        return result;
    }
}
