using System;

namespace PnP.Framework.Migration.Verification
{
    public sealed class RuntimeHttpEvidence
    {
        public string RequestedUrl { get; set; }

        public string FinalUrl { get; set; }

        public string Method { get; set; }

        public int StatusCode { get; set; }

        public string ContentType { get; set; }

        public string RequestId { get; set; }

        public string SharePointRequestGuid { get; set; }

        public string ResponseHeadersDigestSha256 { get; set; }

        public long EncodedDataLength { get; set; }

        public DateTimeOffset CapturedAtUtc { get; set; }
    }
}
