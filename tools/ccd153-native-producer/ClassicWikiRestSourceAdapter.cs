using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Verification;

internal static class ClassicWikiRestSourceAdapter
{
    public static ClassicWikiRestSourceAdaptationResult Adapt(
        ClassicWikiMigrationPackage package,
        AdmittedReproExecutionPlan admittedPlan)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        if (admittedPlan == null) throw new ArgumentNullException(nameof(admittedPlan));

        ClassicWikiPackageValidator.ValidateMigration(package);
        var priorAdmittedPlanDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
            admittedPlan,
            package.PlanDigest,
            admittedPlan.TargetIdentity);

        var warnings = new List<string>();
        var reconstructed = ClassicWikiReferenceInventory
            .CaptureReferenceOnly(package.Snapshot, warnings)
            .ToList();
        var declared = package.Snapshot.Dependencies?.ToList() ?? new List<PageReferenceSnapshot>();
        if (declared.Count > 0 && !SameInventory(declared, reconstructed))
        {
            throw new InvalidDataException(
                "classic_wiki_source_dependency_inventory_conflicts_with_wiki_field");
        }

        var sourceDependencies = declared.Count > 0 ? declared : reconstructed;
        package.Snapshot.Dependencies = sourceDependencies;
        package.Snapshot.Warnings ??= new List<string>();
        AddOnce(
            package.Snapshot.Warnings,
            "Classic Wiki authored references were reconstructed from the digest-bound REST WikiField by the native reference-only source adapter.");
        foreach (var warning in warnings)
        {
            AddOnce(package.Snapshot.Warnings, "Reference-only source adaptation: " + warning);
        }

        var export = new ClassicWikiExportPackage
        {
            SchemaVersion = package.ExportSchemaVersion,
            ExportedAtUtc = package.ExportedAtUtc,
            Selection = package.Selection,
            SelectionDigest = package.SelectionDigest,
            Snapshot = package.Snapshot,
            SnapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(package.Snapshot)
        };
        var target = package.Plan.TargetLocation;
        var targetWeb = new Uri(target.TargetWebUrl);
        var adaptedPackage = new ClassicWikiMigrationPlanner().PlanOffline(
            target.TargetWebId,
            target.TargetWebUrl,
            Uri.UnescapeDataString(targetWeb.AbsolutePath),
            export,
            new PagePlanningOptions
            {
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl
            });

        RequireSameTarget(package, adaptedPackage);
        adaptedPackage.Report.Dispositions.Add(
            $"Dependencies: {sourceDependencies.Count} authored reference(s) reconstructed from digest-bound REST WikiField");
        ClassicWikiPackageValidator.ValidateMigration(adaptedPackage);

        admittedPlan.PlanDigest = adaptedPackage.PlanDigest;
        admittedPlan.Operations = new ReproOperationIds
        {
            MutationOperationId = Guid.NewGuid(),
            ReadbackOperationId = Guid.NewGuid(),
            RuntimeOperationId = Guid.NewGuid(),
            CleanupOperationId = Guid.NewGuid()
        };
        var adaptedAdmittedPlanDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
            admittedPlan,
            adaptedPackage.PlanDigest,
            admittedPlan.TargetIdentity);

        return new ClassicWikiRestSourceAdaptationResult
        {
            Package = adaptedPackage,
            AdmittedPlan = admittedPlan,
            PriorPlanDigestSha256 = package.PlanDigest,
            AdaptedPlanDigestSha256 = adaptedPackage.PlanDigest,
            PriorAdmittedPlanDigestSha256 = priorAdmittedPlanDigest,
            AdaptedAdmittedPlanDigestSha256 = adaptedAdmittedPlanDigest,
            DependencyCount = sourceDependencies.Count,
            ReconstructedDependencyCount = reconstructed.Count,
            UsedDeclaredInventory = declared.Count > 0
        };
    }

    private static bool SameInventory(
        IList<PageReferenceSnapshot> declared,
        IList<PageReferenceSnapshot> reconstructed)
    {
        if (declared.Count != reconstructed.Count)
        {
            return false;
        }

        var unused = reconstructed.ToList();
        foreach (var expected in declared)
        {
            var match = unused.FirstOrDefault(actual =>
                expected.Kind == actual.Kind
                && RequiredEquals(expected.Consumer, actual.Consumer)
                && RequiredEquals(expected.OriginalValue, actual.OriginalValue)
                && RequiredUrlEquals(expected.SourceAbsoluteUrl, actual.SourceAbsoluteUrl)
                && OptionalPathEquals(expected.SourceServerRelativeUrl, actual.SourceServerRelativeUrl));
            if (match == null)
            {
                return false;
            }
            unused.Remove(match);
        }
        return unused.Count == 0;
    }

    private static void RequireSameTarget(
        ClassicWikiMigrationPackage original,
        ClassicWikiMigrationPackage adapted)
    {
        var left = original.Plan.TargetLocation;
        var right = adapted.Plan.TargetLocation;
        if (left.TargetWebId != right.TargetWebId
            || !RequiredUrlEquals(left.TargetWebUrl, right.TargetWebUrl)
            || !OptionalPathEquals(left.TargetLibraryServerRelativeUrl, right.TargetLibraryServerRelativeUrl)
            || !OptionalPathEquals(left.TargetFolderServerRelativeUrl, right.TargetFolderServerRelativeUrl)
            || !OptionalPathEquals(original.Plan.TargetPageServerRelativeUrl, adapted.Plan.TargetPageServerRelativeUrl)
            || left.TargetLibraryTemplate != right.TargetLibraryTemplate
            || !RequiredEquals(left.TargetLibraryTitle, right.TargetLibraryTitle)
            || !RequiredEquals(left.FileName, right.FileName))
        {
            throw new InvalidDataException("classic_wiki_source_adaptation_changed_target_scope");
        }
    }

    private static bool RequiredEquals(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool RequiredUrlEquals(string left, string right) =>
        Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
        && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
        && string.Equals(leftUri.AbsoluteUri.TrimEnd('/'), rightUri.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool OptionalPathEquals(string left, string right) =>
        string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)
        || (!string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(
                Uri.UnescapeDataString(left).TrimEnd('/'),
                Uri.UnescapeDataString(right).TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase));

    private static void AddOnce(ICollection<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal))
        {
            values.Add(value);
        }
    }
}

internal sealed class ClassicWikiRestSourceAdaptationResult
{
    public ClassicWikiMigrationPackage Package { get; init; }
    public AdmittedReproExecutionPlan AdmittedPlan { get; init; }
    public string PriorPlanDigestSha256 { get; init; }
    public string AdaptedPlanDigestSha256 { get; init; }
    public string PriorAdmittedPlanDigestSha256 { get; init; }
    public string AdaptedAdmittedPlanDigestSha256 { get; init; }
    public int DependencyCount { get; init; }
    public int ReconstructedDependencyCount { get; init; }
    public bool UsedDeclaredInventory { get; init; }
}
