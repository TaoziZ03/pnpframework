using System.Collections.Generic;

namespace PnP.Framework.Migration.Verification
{
    /// <summary>
    /// A non-persisted runtime assertion sealed inside the migration plan.
    /// It references canonical ingredients and their existing actions; it is not
    /// a PageIngredientKind, graph node, materializer, or persisted action.
    /// </summary>
    public sealed class RuntimeVerificationAssertion
    {
        public string AssertionId { get; set; }

        public string AssertionOwnerLane { get; set; }

        public string Subtype { get; set; }

        public string SemanticRole { get; set; }

        public string SourcePredicateId { get; set; }

        public string SourcePredicateVersion { get; set; }

        public string SourcePageOrListItemIdentity { get; set; }

        public string SourceVersionIdentity { get; set; }

        public string SourceEvidenceDigestSha256 { get; set; }

        public string StableAssertionKey { get; set; }

        public string TargetProfileId { get; set; }

        public string FixtureContractVersion { get; set; }

        public IList<RuntimeVerificationActionReference> AttachedActionReferences { get; set; } =
            new List<RuntimeVerificationActionReference>();

        public RuntimeVerificationAssertionIntent Intent { get; set; }
    }

    public sealed class RuntimeVerificationActionReference
    {
        public string IngredientId { get; set; }

        public string ActionId { get; set; }

        public string DependencyRole { get; set; }
    }

    public sealed class RuntimeVerificationAssertionIntent
    {
        public RuntimeVerificationStateExpectation InitialState { get; set; }

        public RuntimeVerificationActionIntent Action { get; set; }

        public RuntimeVerificationStateExpectation ExpectedFinalState { get; set; }
    }

    public sealed class RuntimeVerificationStateExpectation
    {
        public string StateId { get; set; }

        public string Predicate { get; set; }

        public string EvidenceKind { get; set; }
    }

    public sealed class RuntimeVerificationActionIntent
    {
        public string ActionId { get; set; }

        public string Kind { get; set; }

        public string Selector { get; set; }

        public string Input { get; set; }

        public int TimeoutMilliseconds { get; set; }
    }
}
