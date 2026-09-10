using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence.ProducerBuild;
using PnP.Framework.Migration.Pages.ClassicWiki.Verification;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using PnP.Framework.Test.Migration.Verification;
using System;

namespace PnP.Framework.Test.ClassicWiki
{
    [TestClass]
    public class ClassicWikiNativeRuntimeAcceptanceTests
    {
        [TestMethod]
        public void ExactNativeEvidenceNeedsTrustedProvenanceBeforeAcceptance()
        {
            var fixture = NativeRuntimeTestFixture.Create();

            var receipt = Evaluate(fixture, new VerifiedTestProducerBuildProvenanceVerifier());

            Assert.AreEqual(NativePageRuntimeContract.BindingValid, receipt.BindingValidationStatus);
            Assert.AreEqual(RuntimeVerificationStatus.Passed, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Accepted, receipt.AcceptanceStatus);
            Assert.AreEqual(ProducerBuildProvenanceContract.Verified, receipt.ProvenanceStatus);
            Assert.AreEqual("NATIVE_RUNTIME_PASSED", receipt.ReasonCodes[0]);
            Assert.AreEqual(64, receipt.ContentSha256.Length);
        }

        [TestMethod]
        public void MissingOrUnverifiedProvenancePreservesPending()
        {
            var fixture = NativeRuntimeTestFixture.Create();

            var receipt = Evaluate(fixture, null);

            Assert.AreEqual(NativePageRuntimeContract.BindingIncomplete, receipt.BindingValidationStatus);
            Assert.AreEqual(RuntimeVerificationStatus.Pending, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Pending, receipt.AcceptanceStatus);
            Assert.AreEqual(ProducerBuildProvenanceContract.Unverified, receipt.ProvenanceStatus);
            Assert.IsTrue(receipt.ReasonCodes.Contains("PRODUCER_BUILD_PROVENANCE_NOT_VERIFIED"));
        }

        [TestMethod]
        public void SelfAuthoredVerifiedReceiptWithoutIndependentGatesPreservesPending()
        {
            var fixture = NativeRuntimeTestFixture.Create();

            var receipt = Evaluate(fixture, new SelfAuthoredVerifiedProvenanceVerifier());

            Assert.AreEqual(RuntimeVerificationStatus.Pending, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Pending, receipt.AcceptanceStatus);
            Assert.AreEqual(ProducerBuildProvenanceContract.Unverified, receipt.ProvenanceStatus);
            Assert.IsTrue(receipt.ReasonCodes[0].StartsWith("PROVENANCE_RECEIPT_INVALID:", StringComparison.Ordinal));
        }

        [TestMethod]
        public void CustomReportOrMissingExternalEnvelopeCannotChangePending()
        {
            var fixture = NativeRuntimeTestFixture.Create();

            var receipt = ClassicWikiNativeRuntimeAcceptance.Evaluate(
                fixture.Package,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Binding,
                externalEvidence: null,
                fixture.ArtifactStore,
                new AlwaysTrueRuntimePolicy(),
                fixture.ProvenanceManifest,
                new VerifiedTestProducerBuildProvenanceVerifier(),
                "ccd.native-runtime-evaluator",
                NativeRuntimeTestFixture.ContractRef,
                new DateTimeOffset(2026, 9, 10, 0, 20, 0, TimeSpan.Zero));

            Assert.AreEqual(NativePageRuntimeContract.BindingIncomplete, receipt.BindingValidationStatus);
            Assert.AreEqual(RuntimeVerificationStatus.Pending, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Pending, receipt.AcceptanceStatus);
        }

        [TestMethod]
        public void Semantic200DenialIsTerminalRejectedEvidence()
        {
            var fixture = NativeRuntimeTestFixture.Create(semanticDenial: true);

            var receipt = Evaluate(fixture, new VerifiedTestProducerBuildProvenanceVerifier());

            Assert.AreEqual(RuntimeVerificationStatus.Failed, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Rejected, receipt.AcceptanceStatus);
            Assert.IsTrue(receipt.ReasonCodes.Contains("NATIVE_RUNTIME_TERMINAL_NEGATIVE"));
        }

        [TestMethod]
        public void Http403IsTerminalRejectedEvidenceWithoutBatchWideBlocker()
        {
            var fixture = NativeRuntimeTestFixture.Create(runtimePassed: false);

            var receipt = Evaluate(fixture, null);

            Assert.AreEqual(RuntimeVerificationStatus.Failed, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Rejected, receipt.AcceptanceStatus);
            Assert.AreEqual(403, fixture.External.RuntimeReceipt.Results[0].Http.StatusCode);
        }

        [TestMethod]
        public void ExplicitExclusionsUseExistingClassicWikiAcceptancePolicy()
        {
            var fixture = NativeRuntimeTestFixture.Create(exclusions: true);

            var receipt = Evaluate(fixture, new VerifiedTestProducerBuildProvenanceVerifier());

            Assert.IsTrue(receipt.ExplicitExclusions);
            Assert.AreEqual(RuntimeVerificationStatus.Passed, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.PartiallyAccepted, receipt.AcceptanceStatus);
            Assert.IsTrue(receipt.ReasonCodes.Contains("NATIVE_RUNTIME_PASSED_WITH_EXCLUSIONS"));
        }

        [TestMethod]
        public void ConsistentlyChangedObservedTupleCannotAliasSealedExpectedIdentity()
        {
            var fixture = NativeRuntimeTestFixture.Create();
            fixture.External.ObservedTargetIdentity.SiteId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            fixture.External.PreCaptureTargetReadback.Identity.SiteId = fixture.External.ObservedTargetIdentity.SiteId;
            fixture.External.PostCaptureTargetReadback.Identity.SiteId = fixture.External.ObservedTargetIdentity.SiteId;
            fixture.ResealExternal();

            var receipt = Evaluate(fixture, new VerifiedTestProducerBuildProvenanceVerifier());

            Assert.AreEqual(NativePageRuntimeContract.BindingInvalid, receipt.BindingValidationStatus);
            Assert.AreEqual(RuntimeVerificationStatus.Pending, receipt.RuntimeVerificationStatus);
            Assert.AreEqual(MigrationAcceptanceStatus.Pending, receipt.AcceptanceStatus);
        }

        private static NativePageRuntimeAcceptanceReceipt Evaluate(
            NativeRuntimeTestFixture fixture,
            IProducerBuildProvenanceVerifier verifier)
        {
            return ClassicWikiNativeRuntimeAcceptance.Evaluate(
                fixture.Package,
                fixture.AdmittedPlan,
                fixture.AdmittedDigest,
                fixture.ImportAggregate,
                fixture.Binding,
                fixture.External,
                fixture.ArtifactStore,
                new ClassicWikiRuntimeEvidencePolicy(),
                fixture.ProvenanceManifest,
                verifier,
                "ccd.native-runtime-evaluator",
                NativeRuntimeTestFixture.ContractRef,
                new DateTimeOffset(2026, 9, 10, 0, 20, 0, TimeSpan.Zero));
        }
    }
}
