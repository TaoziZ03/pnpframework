using System.Collections.Generic;

namespace PnP.Framework.Migration.Scale
{
    public enum ScaleIngredientOutcome
    {
        Verified = 1,
        VerifiedWithApprovedExclusion = 2,
        TargetRuntimeSatisfied = 3,
        AuthorizationBlocked = 4,
        SkippedByDependency = 5,
        NeedsRca = 6,
        NeedsCapability = 7,
        NeedsPolicyDecision = 8,
        QuarantinedUnexpectedDifference = 9
    }

    public sealed class ScaleIngredientRunResult
    {
        public string IngredientId { get; set; }

        public ScaleIngredientOutcome Outcome { get; set; }

        public IList<string> DependencyIngredientIds { get; set; } = new List<string>();

        public string AuthorizationEvidenceArtifactSha256 { get; set; }

        public string DiagnosticCode { get; set; }
    }
}
