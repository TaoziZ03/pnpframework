using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal enum IngredientObservationOrigin
    {
        AuthenticatedSource = 1,
        CupCollectFreshReadback = 2,
        Historical = 3,
        Synthetic = 4
    }

    internal sealed class IngredientValueObservation
    {
        public string ValuePath { get; set; }

        public string ValueDigest { get; set; }

        public DateTimeOffset ObservedAtUtc { get; set; }

        public IngredientObservationOrigin Origin { get; set; }

        public string EvidenceReference { get; set; }
    }

    internal sealed class IngredientLiveEvidence
    {
        public bool SourceAuthenticated { get; set; }

        public bool TargetFreshReadback { get; set; }

        public bool HistoricalOrSyntheticSubstitution { get; set; }

        public IList<IngredientValueObservation> Observations { get; set; } =
            new List<IngredientValueObservation>();

        public IList<string> SourceEvidenceReferences { get; set; } = new List<string>();

        public IList<string> TargetEvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class IngredientContentIntegrityEvidence
    {
        public ArtifactReference Artifact { get; set; }

        public string ContentBase64 { get; set; }

        public IMigrationArtifactStore ArtifactStore { get; set; }

        public string SemanticValueCanonicalJson { get; set; }

        public string SemanticDigest { get; set; }

        public IList<string> EvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class IngredientPlanEvidence
    {
        public PublishingPageMigrationPlan Plan { get; set; }

        public string ExpectedSourceSnapshotDigest { get; set; }

        public string ExpectedPlanDigest { get; set; }

        public string IngredientId { get; set; }

        public IList<string> SnapshotPlanEvidenceReferences { get; set; } = new List<string>();

        public IList<string> ActionEvidenceReferences { get; set; } = new List<string>();

        public IList<string> DependencyPolicyEvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class IngredientOperationalEvidence
    {
        public PublishingPageMigrationPlan Plan { get; set; }

        public string AdmittedPlanDigest { get; set; }

        public bool AdmissionPassed { get; set; }

        public string IngredientId { get; set; }

        public MigrationActionSignature ActionSignature { get; set; }

        public PublishingPageImportReceipt ImportReceipt { get; set; }

        public MigrationExecutionStateReceipt JournalState { get; set; }

        public MigrationMutationVerificationReceipt VerificationReceipt { get; set; }

        public RuntimeVerificationReceipt RuntimeReceipt { get; set; }

        public DateTimeOffset ExecutionStartedAtUtc { get; set; }

        public bool RuntimeRequired { get; set; }

        public bool CleanupRequired { get; set; }

        public bool CleanupPassed { get; set; }

        public bool RetryEvidenceRequired { get; set; }

        public bool RetryPassed { get; set; }

        public IList<string> PlanAdmissionEvidenceReferences { get; set; } = new List<string>();

        public IList<string> ActionEvidenceReferences { get; set; } = new List<string>();

        public IList<string> ReceiptEvidenceReferences { get; set; } = new List<string>();

        public IList<string> FreshReadbackEvidenceReferences { get; set; } = new List<string>();

        public IList<string> RuntimeCleanupRetryEvidenceReferences { get; set; } = new List<string>();
    }

    internal sealed class IngredientProductizationEvidence
    {
        public string TestId { get; set; }

        public bool HermeticUnitTest { get; set; }

        public string RedTestEvidenceReference { get; set; }

        public string GreenTestEvidenceReference { get; set; }

        public string ImplementationCommit { get; set; }

        public string BuildCommit { get; set; }

        public string BinaryDigest { get; set; }

        public string BuildEvidenceReference { get; set; }

        public string EndToEndCommit { get; set; }

        public string EndToEndPlanDigest { get; set; }

        public string EndToEndEvidenceReference { get; set; }

        public PublishingPageCompareReport CompareReport { get; set; }

        public string CompareReportDigest { get; set; }

        public string CompareEvidenceReference { get; set; }

        public string ArchitectReviewVerdict { get; set; }

        public string ArchitectReviewEvidenceReference { get; set; }

        public string CtoReviewVerdict { get; set; }

        public string CtoReviewEvidenceReference { get; set; }

        public string IndependentVerificationVerdict { get; set; }

        public string IndependentVerificationEvidenceReference { get; set; }

        public string PrReadyCommit { get; set; }

        public string PrReadyEvidenceReference { get; set; }
    }
}
