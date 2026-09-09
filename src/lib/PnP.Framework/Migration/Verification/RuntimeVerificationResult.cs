namespace PnP.Framework.Migration.Verification
{
    public sealed class RuntimeVerificationResult
    {
        public string RequirementId { get; set; }

        public bool Passed { get; set; }

        public string EvidenceArtifactSha256 { get; set; }

        public long EvidenceArtifactLength { get; set; }

        public string EvidenceArtifactLocator { get; set; }

        public string ImplementationRef { get; set; }

        public string BrowserContextId { get; set; }

        public RuntimeHttpEvidence Http { get; set; }

        public RuntimeCacheEvidence Cache { get; set; }

        public string DomProbeArtifactSha256 { get; set; }

        public long DomProbeArtifactLength { get; set; }

        public string DomProbeArtifactLocator { get; set; }

        public string ScreenshotArtifactSha256 { get; set; }

        public long? ScreenshotArtifactLength { get; set; }

        public string ScreenshotArtifactLocator { get; set; }

        public string Message { get; set; }
    }
}
