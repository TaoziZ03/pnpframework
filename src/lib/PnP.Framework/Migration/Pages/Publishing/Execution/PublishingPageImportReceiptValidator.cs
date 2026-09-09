using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    /// <summary>
    /// Validates the execution lineage carried by an admitted publishing-page
    /// import receipt. This validator deliberately checks the native recorder
    /// steps instead of accepting a structurally populated sidecar receipt.
    /// </summary>
    public static class PublishingPageImportReceiptValidator
    {
        public static void ValidateAdmittedExecution(
            PublishingPageImportReceipt receipt,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            AdmittedPageImportReceiptValidator.ValidateSuccessfulExecution(
                receipt,
                admittedPlan,
                admittedPlanDigestSha256);
        }
    }
}
