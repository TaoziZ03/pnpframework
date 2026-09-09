using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Assessment.Maturity
{
    internal sealed class IngredientMaturityGateDefinition
    {
        public IngredientMaturityGateDefinition(
            IngredientMaturityLevel level,
            string gateId,
            IngredientMaturityGateCategory category)
        {
            Level = level;
            GateId = gateId;
            Category = category;
        }

        public IngredientMaturityLevel Level { get; }

        public string GateId { get; }

        public IngredientMaturityGateCategory Category { get; }
    }

    internal static class IngredientMaturityGateCatalog
    {
        public const string CanonicalIdentity = "m0.canonical-identity";
        public const string PrimaryOwner = "m0.primary-owner";
        public const string SourceBinding = "m0.source-binding";
        public const string AuthenticatedSourceCollect = "m1.authenticated-source-collect";
        public const string CupCollectFreshReadback = "m1.cupcollect-fresh-readback";
        public const string PerValueObservation = "m1.per-value-observation";
        public const string RawArtifactIntegrity = "m2.raw-artifact-integrity";
        public const string SemanticIntegrity = "m2.normalized-semantic-integrity";
        public const string SnapshotPlanBinding = "m3.snapshot-plan-binding";
        public const string ActionDisposition = "m3.action-disposition";
        public const string DependencyPolicy = "m3.dependency-policy";
        public const string AdmittedExactPlan = "m4.admitted-exact-plan";
        public const string OperationActionBinding = "m4.operation-action-binding";
        public const string MutationJournalVerification = "m4.mutation-journal-verification";
        public const string OwnershipProvenance = "m4.ownership-provenance";
        public const string FreshStorage = "m4.fresh-storage";
        public const string RuntimeCleanupRetry = "m4.runtime-cleanup-retry";
        public const string HermeticUtRedGreen = "m5.hermetic-ut-red-green";
        public const string ExactCodeBuild = "m5.exact-code-build";
        public const string SameCommitE2E = "m5.same-commit-e2e";
        public const string DeterministicCompare = "m5.deterministic-compare";
        public const string ArchitectReview = "m5.architect-review";
        public const string CtoReview = "m5.cto-review";
        public const string IndependentVerification = "m5.independent-verification";
        public const string PrReady = "m5.pr-ready";

        private static readonly IReadOnlyList<IngredientMaturityGateDefinition> definitions =
            new ReadOnlyCollection<IngredientMaturityGateDefinition>(new[]
            {
                Domain(IngredientMaturityLevel.M0, CanonicalIdentity),
                Domain(IngredientMaturityLevel.M0, PrimaryOwner),
                Domain(IngredientMaturityLevel.M0, SourceBinding),
                Domain(IngredientMaturityLevel.M1, AuthenticatedSourceCollect),
                Domain(IngredientMaturityLevel.M1, CupCollectFreshReadback),
                Domain(IngredientMaturityLevel.M1, PerValueObservation),
                Domain(IngredientMaturityLevel.M2, RawArtifactIntegrity),
                Domain(IngredientMaturityLevel.M2, SemanticIntegrity),
                Domain(IngredientMaturityLevel.M3, SnapshotPlanBinding),
                Domain(IngredientMaturityLevel.M3, ActionDisposition),
                Domain(IngredientMaturityLevel.M3, DependencyPolicy),
                Domain(IngredientMaturityLevel.M4, AdmittedExactPlan),
                Domain(IngredientMaturityLevel.M4, OperationActionBinding),
                Domain(IngredientMaturityLevel.M4, MutationJournalVerification),
                Domain(IngredientMaturityLevel.M4, OwnershipProvenance),
                Domain(IngredientMaturityLevel.M4, FreshStorage),
                Domain(IngredientMaturityLevel.M4, RuntimeCleanupRetry),
                Delivery(IngredientMaturityLevel.M5, HermeticUtRedGreen),
                Delivery(IngredientMaturityLevel.M5, ExactCodeBuild),
                Delivery(IngredientMaturityLevel.M5, SameCommitE2E),
                Domain(IngredientMaturityLevel.M5, DeterministicCompare),
                Delivery(IngredientMaturityLevel.M5, ArchitectReview),
                Delivery(IngredientMaturityLevel.M5, CtoReview),
                Delivery(IngredientMaturityLevel.M5, IndependentVerification),
                Delivery(IngredientMaturityLevel.M5, PrReady)
            });

        public static IReadOnlyList<IngredientMaturityGateDefinition> Definitions => definitions;

        public static IReadOnlyList<IngredientMaturityGateDefinition> ForLevel(IngredientMaturityLevel level)
        {
            return new ReadOnlyCollection<IngredientMaturityGateDefinition>(
                definitions.Where(value => value.Level == level).ToArray());
        }

        public static IngredientMaturityGateDefinition Require(string gateId)
        {
            var matches = definitions.Where(value => string.Equals(value.GateId, gateId, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
            {
                throw new ArgumentException($"Unknown maturity gate '{gateId}'.", nameof(gateId));
            }
            return matches[0];
        }

        private static IngredientMaturityGateDefinition Domain(IngredientMaturityLevel level, string gateId)
        {
            return new IngredientMaturityGateDefinition(level, gateId, IngredientMaturityGateCategory.PnPDomain);
        }

        private static IngredientMaturityGateDefinition Delivery(IngredientMaturityLevel level, string gateId)
        {
            return new IngredientMaturityGateDefinition(level, gateId, IngredientMaturityGateCategory.DeliveryProcess);
        }
    }
}
