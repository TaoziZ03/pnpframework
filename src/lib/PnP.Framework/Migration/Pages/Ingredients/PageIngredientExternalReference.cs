namespace PnP.Framework.Migration.Pages.Ingredients
{
    public enum PageExternalIngredientState
    {
        SatisfiedBySharedPlan = 1,
        TargetInspectionRequired = 2,
        AuthorizationBlocked = 3,
        RetryableFailure = 4,
        Blocked = 5
    }

    /// <summary>
    /// References a bundle/shared ingredient without copying it into a page-local
    /// graph. The shared plan owns action, receipt, and verification state.
    /// </summary>
    public sealed class PageIngredientExternalReference
    {
        public string IngredientId { get; set; }

        public PageIngredientKind Kind { get; set; }

        public PageIngredientOwnership Ownership { get; set; } = PageIngredientOwnership.Shared;

        public string SharedPlanDigest { get; set; }

        public PageExternalIngredientState State { get; set; }

        public string TargetIdentity { get; set; }

        public string EvidenceDigest { get; set; }
    }
}
