using System;

namespace PnP.Framework.Migration.Evidence.ProducerBuild
{
    public interface IProducerBuildProvenanceVerifier
    {
        ProducerBuildProvenanceReceipt Verify(ProducerBuildProvenanceManifest manifest);
    }

    public sealed class UnverifiedProducerBuildProvenanceVerifier : IProducerBuildProvenanceVerifier
    {
        public ProducerBuildProvenanceReceipt Verify(ProducerBuildProvenanceManifest manifest)
        {
            return ProducerBuildProvenanceContract.CreateUnverified(manifest);
        }
    }
}
