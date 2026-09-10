using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.Capture;
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
        var inventoryResolution = declared.Count > 0
            ? ResolveInventory(declared, reconstructed)
            : ClassicWikiInventoryResolution.Empty;
        if (inventoryResolution.HasConflict)
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
        foreach (var unavailable in inventoryResolution.Unavailable)
        {
            unavailable.Diagnostics ??= new List<string>();
            AddOnce(
                unavailable.Diagnostics,
                "Native source adaptation retained this dependency as unavailable and delegated its unsafe target rewrite branch.");
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
        var dependencyDispositions = ApplyDependencyDispositions(
            adaptedPackage,
            package.Snapshot.Source,
            sourceDependencies,
            inventoryResolution.Unavailable);
        adaptedPackage.Report.Dispositions.Add(
            $"Dependencies: {sourceDependencies.Count} authored reference(s) reconstructed from digest-bound REST WikiField");
        adaptedPackage.PlanDigest = ClassicWikiDigest.ComputePlanDigest(adaptedPackage.Plan);
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
            UsedDeclaredInventory = declared.Count > 0,
            DependencyDispositions = dependencyDispositions
        };
    }

    internal static bool SameInventory(
        IList<PageReferenceSnapshot> declared,
        IList<PageReferenceSnapshot> reconstructed)
    {
        var resolution = ResolveInventory(declared, reconstructed);
        return !resolution.HasConflict && resolution.Unavailable.Count == 0;
    }

    private static ClassicWikiInventoryResolution ResolveInventory(
        IList<PageReferenceSnapshot> declared,
        IList<PageReferenceSnapshot> reconstructed)
    {
        if (declared == null || reconstructed == null || declared.Count != reconstructed.Count)
        {
            return ClassicWikiInventoryResolution.Conflict;
        }

        var unused = reconstructed.ToList();
        var unavailable = new List<PageReferenceSnapshot>();
        foreach (var expected in declared)
        {
            var match = unused.FirstOrDefault(actual =>
                expected != null
                && actual != null
                && RequiredEquals(expected.Id, actual.Id)
                && expected.Kind == actual.Kind
                && RequiredEquals(expected.Consumer, actual.Consumer)
                && RequiredEquals(expected.OriginalValue, actual.OriginalValue)
                && RequiredUrlEquals(expected.SourceAbsoluteUrl, actual.SourceAbsoluteUrl)
                && OptionalPathEquals(expected.SourceServerRelativeUrl, actual.SourceServerRelativeUrl));
            if (match == null
                || !Enum.IsDefined(typeof(PageCaptureStatus), expected.CaptureStatus)
                || !Enum.IsDefined(typeof(PageCaptureStatus), match.CaptureStatus))
            {
                return ClassicWikiInventoryResolution.Conflict;
            }
            if (expected.CaptureStatus == PageCaptureStatus.Captured
                && match.CaptureStatus == PageCaptureStatus.Captured)
            {
                unused.Remove(match);
                continue;
            }
            if (IsKnownUnavailable(expected.CaptureStatus)
                && match.CaptureStatus == PageCaptureStatus.Captured)
            {
                unavailable.Add(expected);
                unused.Remove(match);
                continue;
            }
            return ClassicWikiInventoryResolution.Conflict;
        }
        return unused.Count == 0
            ? new ClassicWikiInventoryResolution(unavailable, hasConflict: false)
            : ClassicWikiInventoryResolution.Conflict;
    }

    private static IList<ClassicWikiRestSourceDependencyDisposition> ApplyDependencyDispositions(
        ClassicWikiMigrationPackage adaptedPackage,
        PageIdentity source,
        IList<PageReferenceSnapshot> sourceDependencies,
        IList<PageReferenceSnapshot> unavailableDependencies)
    {
        var unavailableById = unavailableDependencies.ToDictionary(value => value.Id, StringComparer.Ordinal);
        var safeWikiField = adaptedPackage.Plan.WikiFieldPlan.ExactValue;
        var result = new List<ClassicWikiRestSourceDependencyDisposition>();
        foreach (var sourceDependency in sourceDependencies)
        {
            var plan = adaptedPackage.Plan.Dependencies.SingleOrDefault(value =>
                string.Equals(value.SourceId, sourceDependency.Id, StringComparison.Ordinal));
            if (plan == null)
            {
                throw new InvalidDataException(
                    "classic_wiki_source_dependency_inventory_conflicts_with_planned_dependencies");
            }

            if (!unavailableById.ContainsKey(sourceDependency.Id))
            {
                result.Add(new ClassicWikiRestSourceDependencyDisposition
                {
                    SourceId = sourceDependency.Id,
                    CaptureStatus = sourceDependency.CaptureStatus,
                    Disposition = "Rewrite",
                    ReasonCode = "CAPTURED"
                });
                continue;
            }

            var rewrittenValue = plan.TargetOriginalValue;
            if (!string.Equals(rewrittenValue, sourceDependency.OriginalValue, StringComparison.Ordinal))
            {
                if (safeWikiField?.IndexOf(rewrittenValue, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    safeWikiField = ReplaceCaseInsensitive(
                        safeWikiField,
                        rewrittenValue,
                        sourceDependency.OriginalValue);
                }
                else if (safeWikiField?.IndexOf(sourceDependency.OriginalValue, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidDataException(
                        "classic_wiki_unavailable_dependency_cannot_be_preserved_in_wiki_field");
                }
            }

            plan.TargetOriginalValue = sourceDependency.OriginalValue;
            plan.TargetAbsoluteUrl = sourceDependency.SourceAbsoluteUrl;
            plan.TargetServerRelativeUrl = sourceDependency.SourceServerRelativeUrl;
            plan.Disposition = "Delegate";
            var reasonCode = ReasonCode(sourceDependency, source);
            result.Add(new ClassicWikiRestSourceDependencyDisposition
            {
                SourceId = sourceDependency.Id,
                CaptureStatus = sourceDependency.CaptureStatus,
                Disposition = plan.Disposition,
                ReasonCode = reasonCode
            });
            var message = $"Dependency '{sourceDependency.Id}' is {sourceDependency.CaptureStatus}; "
                + $"disposition=Delegate; reason={reasonCode}; source locator is preserved and exact fidelity is not claimed.";
            AddOnce(adaptedPackage.Plan.Warnings, message);
            AddOnce(adaptedPackage.Report.Warnings, message);
            adaptedPackage.Report.Dispositions.Add(message);
        }

        if (unavailableDependencies.Count > 0)
        {
            adaptedPackage.Plan.WikiFieldPlan = WikiFieldWritePolicy.Build(safeWikiField);
            adaptedPackage.Report.Status = "Conditional";
        }
        return result;
    }

    private static bool IsKnownUnavailable(PageCaptureStatus status) =>
        status == PageCaptureStatus.Failed || status == PageCaptureStatus.NotReturned;

    private static string ReasonCode(PageReferenceSnapshot dependency, PageIdentity source)
    {
        var evidence = dependency.AuthorizationEvidence;
        if (evidence != null)
        {
            LiteralHttpAuthorizationEvidence.Validate(evidence);
            var expectedCsomRequest = source?.WebUrl?.TrimEnd('/') + "/_vti_bin/client.svc/ProcessQuery";
            if (!string.Equals(evidence.Operation, "capture-page-reference-payload", StringComparison.Ordinal)
                || !RequiredUrlEquals(evidence.RequestUri, dependency.SourceAbsoluteUrl)
                    && !RequiredUrlEquals(evidence.RequestUri, expectedCsomRequest))
            {
                throw new InvalidDataException(
                    "classic_wiki_unavailable_dependency_authorization_evidence_invalid");
            }
            return "ACCESS_DENIED_SKIPPED";
        }
        return dependency.CaptureStatus == PageCaptureStatus.NotReturned
            ? "SOURCE_REFERENCE_NOT_RETURNED"
            : "SOURCE_REFERENCE_CAPTURE_FAILED";
    }

    private static string ReplaceCaseInsensitive(string input, string pattern, string replacement)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern)) return input;
        return System.Text.RegularExpressions.Regex.Replace(
            input,
            System.Text.RegularExpressions.Regex.Escape(pattern),
            (replacement ?? string.Empty).Replace("$", "$$"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
    public IList<ClassicWikiRestSourceDependencyDisposition> DependencyDispositions { get; init; }
}

internal sealed class ClassicWikiRestSourceDependencyDisposition
{
    public string SourceId { get; init; }
    public PageCaptureStatus CaptureStatus { get; init; }
    public string Disposition { get; init; }
    public string ReasonCode { get; init; }
}

internal sealed class ClassicWikiInventoryResolution
{
    public static ClassicWikiInventoryResolution Empty { get; } =
        new ClassicWikiInventoryResolution(Array.Empty<PageReferenceSnapshot>(), hasConflict: false);

    public static ClassicWikiInventoryResolution Conflict { get; } =
        new ClassicWikiInventoryResolution(Array.Empty<PageReferenceSnapshot>(), hasConflict: true);

    public ClassicWikiInventoryResolution(
        IList<PageReferenceSnapshot> unavailable,
        bool hasConflict)
    {
        Unavailable = unavailable;
        HasConflict = hasConflict;
    }

    public IList<PageReferenceSnapshot> Unavailable { get; }
    public bool HasConflict { get; }
}
