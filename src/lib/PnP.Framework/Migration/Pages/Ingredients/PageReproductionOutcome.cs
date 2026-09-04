namespace PnP.Framework.Migration.Pages.Ingredients
{
    public enum PageReproductionOutcome
    {
        Unknown = 0,
        ExactReproduction = 1,
        ReproducedWithTransform = 2,
        ReproducedWithApprovedExclusions = 3,
        ReproducedWithKnownGaps = 4,
        Rejected = 5
    }
}
