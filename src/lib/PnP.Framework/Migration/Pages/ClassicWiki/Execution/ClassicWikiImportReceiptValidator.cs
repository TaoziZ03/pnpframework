using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Verification;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    /// <summary>
    /// Validates the admitted execution lineage in a native classic wiki receipt.
    /// </summary>
    public static class ClassicWikiImportReceiptValidator
    {
        public static void ValidateAdmittedExecution(
            ClassicWikiImportReceipt receipt,
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
