namespace PnP.Framework.Migration.Evidence.ProducerBuild
{
    public sealed class ProducerBuildProvenanceManifest
    {
        public string SchemaVersion { get; set; } = ProducerBuildProvenanceContract.ManifestSchemaVersion;

        public string ProducerId { get; set; }

        public string ProducerVersion { get; set; }

        public string ImplementationRef { get; set; }

        public string BinaryName { get; set; }

        public string BinarySha256 { get; set; }

        public string TargetFramework { get; set; }

        public string BuildConfiguration { get; set; }
    }
}
