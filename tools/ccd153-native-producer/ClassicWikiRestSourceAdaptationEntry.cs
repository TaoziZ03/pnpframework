using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Verification;
using System.Text;
using System.Text.Json;

internal static class ClassicWikiRestSourceAdaptationEntry
{
    public static ClassicWikiRestSourceAdaptationManifest Run(
        string caseId,
        string implementationRef,
        string binarySha256,
        string outputDirectory,
        ClassicWikiMigrationPackage package,
        AdmittedReproExecutionPlan admittedPlan,
        JsonSerializerOptions options)
    {
        var result = ClassicWikiRestSourceAdapter.Adapt(package, admittedPlan);
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, "classic-wiki-migration-package-v1.json");
        var admittedPlanPath = Path.Combine(outputDirectory, "admitted-plan-v1.json");
        Write(packagePath, result.Package, options);
        Write(admittedPlanPath, result.AdmittedPlan, options);
        var manifest = new ClassicWikiRestSourceAdaptationManifest
        {
            CaseId = caseId,
            Producer = new ClassicWikiRestSourceProducerBinding
            {
                Id = ProducerContract.Id,
                Version = ProducerContract.Version,
                ImplementationRef = implementationRef
            },
            BinarySha256 = binarySha256,
            PriorPlanDigestSha256 = result.PriorPlanDigestSha256,
            AdaptedPlanDigestSha256 = result.AdaptedPlanDigestSha256,
            PriorAdmittedPlanDigestSha256 = result.PriorAdmittedPlanDigestSha256,
            AdaptedAdmittedPlanDigestSha256 = result.AdaptedAdmittedPlanDigestSha256,
            DependencyCount = result.DependencyCount,
            ReconstructedDependencyCount = result.ReconstructedDependencyCount,
            UsedDeclaredInventory = result.UsedDeclaredInventory,
            ReportStatus = result.Package.Report.Status,
            DependencyDispositions = result.DependencyDispositions,
            Operations = result.AdmittedPlan.Operations,
            PackagePath = Path.GetFileName(packagePath),
            AdmittedPlanPath = Path.GetFileName(admittedPlanPath)
        };
        Write(Path.Combine(outputDirectory, "source-adaptation-manifest-v1.json"), manifest, options);
        return manifest;
    }

    private static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, options) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

internal sealed class ClassicWikiRestSourceAdaptationManifest
{
    public string Schema { get; init; } = "ccd153.classic-wiki-rest-source-adaptation-result/v1";
    public string CaseId { get; init; }
    public ClassicWikiRestSourceProducerBinding Producer { get; init; }
    public string BinarySha256 { get; init; }
    public string PriorPlanDigestSha256 { get; init; }
    public string AdaptedPlanDigestSha256 { get; init; }
    public string PriorAdmittedPlanDigestSha256 { get; init; }
    public string AdaptedAdmittedPlanDigestSha256 { get; init; }
    public int DependencyCount { get; init; }
    public int ReconstructedDependencyCount { get; init; }
    public bool UsedDeclaredInventory { get; init; }
    public string ReportStatus { get; init; }
    public IList<ClassicWikiRestSourceDependencyDisposition> DependencyDispositions { get; init; }
    public ReproOperationIds Operations { get; init; }
    public string PackagePath { get; init; }
    public string AdmittedPlanPath { get; init; }
}

internal sealed class ClassicWikiRestSourceProducerBinding
{
    public string Id { get; init; }
    public string Version { get; init; }
    public string ImplementationRef { get; init; }
}
