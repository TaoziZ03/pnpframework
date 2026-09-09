namespace PnP.Framework.Migration.Verification
{
    public sealed class RuntimeCacheEvidence
    {
        public string RequestMode { get; set; }

        public bool CacheDisabled { get; set; }

        public string RequestCacheControl { get; set; }

        public string RequestPragma { get; set; }

        public string ResponseCacheControl { get; set; }

        public string ResponseAge { get; set; }

        public string ResponseExpires { get; set; }

        public bool FromDiskCache { get; set; }

        public bool FromServiceWorker { get; set; }
    }
}
