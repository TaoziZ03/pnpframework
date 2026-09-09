using PnP.Framework.Migration.Execution;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    /// <summary>
    /// Execution-only projection of one sealed Publishing ingredient action.
    /// It is recomputed from the package instead of adding a second package or
    /// receipt format.
    /// </summary>
    internal sealed class PublishingPageIngredientExecutionBinding
    {
        public string IngredientId { get; set; }

        public MigrationTargetOwnership ExpectedOwnership { get; set; }

        public MigrationActionSignature ActionSignature { get; set; }
    }
}
