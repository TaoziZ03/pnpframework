using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Evidence.ProducerBuild
{
    public sealed class ProducerBuildToolchain
    {
        public string SdkVersion { get; set; }
        public string MsBuildVersion { get; set; }
        public string RuntimeVersion { get; set; }
        public string OperatingSystem { get; set; }
        public string RuntimeIdentifier { get; set; }
        public string TargetFramework { get; set; }
        public string Configuration { get; set; }
    }

    public sealed class ProducerBuildCommand
    {
        public string Executable { get; set; }
        public IList<string> Arguments { get; set; } = new List<string>();
        public IDictionary<string, string> NonSecretProperties { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ProducerBuildArtifact
    {
        public string Role { get; set; }
        public string Path { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }

    public sealed class ProducerBuildProvenanceManifest
    {
        public string SchemaVersion { get; set; } = ProducerBuildProvenanceContract.ManifestSchemaVersion;
        public string ContentSha256 { get; set; }
        public string ProducerId { get; set; }
        public string ProducerVersion { get; set; }
        public string SubjectImplementationRef { get; set; }
        public string RepositoryIdentity { get; set; }
        public string TreeId { get; set; }
        public string CleanSourceReceiptDigestSha256 { get; set; }
        public string BuildDriverImplementationRef { get; set; }
        public IList<string> SourceArtifactDigestsSha256 { get; set; } = new List<string>();
        public IList<string> SubmoduleDigestsSha256 { get; set; } = new List<string>();
        public IList<string> InputArtifactDigestsSha256 { get; set; } = new List<string>();
        public ProducerBuildToolchain Toolchain { get; set; }
        public IList<string> RestoreSourceIdentities { get; set; } = new List<string>();
        public string LockedDependencyGraphSha256 { get; set; }
        public IDictionary<string, string> PackageHashesSha256 { get; set; } = new Dictionary<string, string>();
        public ProducerBuildCommand Command { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset CompletedAtUtc { get; set; }
        public int ExitCode { get; set; }
        public string Result { get; set; }
        public NativePageRuntimeArtifactReference RestoreLog { get; set; }
        public NativePageRuntimeArtifactReference BuildLog { get; set; }
        public IList<ProducerBuildArtifact> Outputs { get; set; } = new List<ProducerBuildArtifact>();
        public IList<ProducerBuildArtifact> RuntimeLoadClosure { get; set; } = new List<ProducerBuildArtifact>();
        public string RequestedHistoricalBinarySha256 { get; set; }
        public IList<string> SchemaCompatibilitySet { get; set; } = new List<string>();
    }
}
