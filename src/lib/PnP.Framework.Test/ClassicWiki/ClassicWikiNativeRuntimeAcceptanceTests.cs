using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using PnP.Framework.Test.Migration.Verification;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiNativeRuntimeAcceptanceTests
    {
        [TestMethod]
        public void NativeProducerHasSoleAuthorityToAcceptExactRuntimeEvidence()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var binding = fixture.CreateNativeBinding();

            var receipt = Decide(fixture, binding);

            Assert.AreEqual(NativePageRuntimeContract.NativeAuthority, receipt.AcceptanceAuthority);
            Assert.AreEqual(RuntimeVerificationStatus.Passed, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Accepted, receipt.AcceptanceStatus);
            Assert.AreEqual("NATIVE_RUNTIME_PASSED", receipt.DecisionCode);
            Assert.AreEqual(64, receipt.BindingDigestSha256.Length);
        }

        [TestMethod]
        public void CustomReportClaimingPassCannotChangePending()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var external = fixture.CreateExternalEvidence(RuntimeVerificationStatus.Passed);
            var binding = fixture.CreatePendingBinding(external);

            var receipt = ClassicWikiNativeRuntimeAcceptance.Decide(
                binding,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Target,
                new ClassicWikiRuntimeEvidencePolicy(),
                hasExplicitExclusions: false,
                externalEvidence: external,
                decidedAtUtc: new System.DateTimeOffset(2026, 9, 10, 0, 11, 0, System.TimeSpan.Zero));

            Assert.AreEqual(RuntimeVerificationStatus.Pending, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Pending, receipt.AcceptanceStatus);
            Assert.AreEqual("NATIVE_RUNTIME_EVIDENCE_PENDING", receipt.DecisionCode);
            StringAssert.Contains(receipt.Diagnostics[0], "canonical native runtime evidence");
        }

        [TestMethod]
        public void MissingNativeEvidencePreservesPending()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var binding = fixture.CreatePendingBinding();

            var receipt = ClassicWikiNativeRuntimeAcceptance.Decide(
                binding,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Target,
                new ClassicWikiRuntimeEvidencePolicy(),
                hasExplicitExclusions: false,
                decidedAtUtc: new System.DateTimeOffset(2026, 9, 10, 0, 11, 0, System.TimeSpan.Zero));

            Assert.AreEqual(RuntimeVerificationStatus.Pending, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Pending, receipt.AcceptanceStatus);
        }

        [TestMethod]
        public void TerminalAccessDeniedObservationIsRejectedNotDropped()
        {
            var fixture = NativeRuntimeTestFixture.Create(runtimePassed: false);
            var binding = fixture.CreateNativeBinding();

            var receipt = Decide(fixture, binding);

            Assert.AreEqual(RuntimeVerificationStatus.Failed, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Rejected, receipt.AcceptanceStatus);
            Assert.AreEqual("NATIVE_RUNTIME_FAILED", receipt.DecisionCode);
            Assert.AreEqual(403, fixture.RuntimeReceipt.Results[0].Http.StatusCode);
        }

        [TestMethod]
        public void ExplicitExclusionsProducePartialAcceptanceAndBindUnverifiedProvenance()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            var manifest = NativeRuntimeTestFixture.CreateProvenanceManifest();
            var provenance = new UnverifiedProducerBuildProvenanceVerifier().Verify(manifest);
            var binding = ClassicWikiRuntimeBindingFactory.CreateNative(
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Target,
                fixture.RuntimeReceipt,
                fixture.RuntimeManifest,
                fixture.ImplementationRef,
                fixture.ArtifactStore,
                provenance,
                manifest);

            var receipt = ClassicWikiNativeRuntimeAcceptance.Decide(
                binding,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Target,
                new ClassicWikiRuntimeEvidencePolicy(),
                hasExplicitExclusions: true,
                runtimeReceipt: fixture.RuntimeReceipt,
                requirementsManifest: fixture.RuntimeManifest,
                expectedImplementationRef: fixture.ImplementationRef,
                artifactStore: fixture.ArtifactStore,
                provenanceReceipt: provenance,
                provenanceManifest: manifest,
                decidedAtUtc: new System.DateTimeOffset(2026, 9, 10, 0, 11, 0, System.TimeSpan.Zero));

            Assert.AreEqual(MigrationAcceptanceStatus.PartiallyAccepted, receipt.AcceptanceStatus);
            Assert.AreEqual(ProducerBuildProvenanceContract.Unverified, receipt.ProducerBuildProvenanceStatus);
        }

        private static NativePageRuntimeAcceptanceReceipt Decide(
            NativeRuntimeTestFixture fixture,
            NativePageRuntimeBinding binding)
        {
            return ClassicWikiNativeRuntimeAcceptance.Decide(
                binding,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Target,
                new ClassicWikiRuntimeEvidencePolicy(),
                hasExplicitExclusions: false,
                runtimeReceipt: fixture.RuntimeReceipt,
                requirementsManifest: fixture.RuntimeManifest,
                expectedImplementationRef: fixture.ImplementationRef,
                artifactStore: fixture.ArtifactStore,
                decidedAtUtc: new System.DateTimeOffset(2026, 9, 10, 0, 11, 0, System.TimeSpan.Zero));
        }
    }
}
