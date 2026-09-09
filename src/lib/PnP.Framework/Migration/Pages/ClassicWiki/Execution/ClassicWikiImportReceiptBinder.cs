using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.IO;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal static class ClassicWikiImportReceiptBinder
    {
        public static ClassicWikiImportReceipt Bind(
            ClassicWikiImportReceipt receipt,
            ClassicWikiMigrationPackage package,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            if (receipt == null)
            {
                throw new InvalidDataException("The classic wiki import producer returned no receipt.");
            }
            if (admittedPlan == null)
            {
                return receipt;
            }

            var targetIdentity = CanonicalTargetIdentity(
                package.Plan.TargetLocation.TargetWebUrl,
                package.Plan.TargetPageServerRelativeUrl);
            var computed = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(
                admittedPlan,
                package.PlanDigest,
                targetIdentity);
            if (!string.Equals(computed, admittedPlanDigestSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The admitted repro plan digest changed during classic wiki import.");
            }
            if (receipt.OperationId != admittedPlan.Operations.MutationOperationId)
            {
                throw new InvalidDataException("The classic wiki import receipt mutation operation ID is not the admitted operation ID.");
            }

            receipt.AdmittedPlanDigestSha256 = computed;
            receipt.SourceVersion = admittedPlan.SourceVersion;
            receipt.Operations = admittedPlan.Operations;
            return receipt;
        }

        internal static string CanonicalTargetIdentity(string webUrl, string pageServerRelativeUrl)
        {
            if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var web))
            {
                throw new InvalidDataException("The admitted classic wiki target Web URL is invalid.");
            }
            return web.GetLeftPart(UriPartial.Authority).TrimEnd('/')
                + "/"
                + (pageServerRelativeUrl ?? string.Empty).TrimStart('/');
        }
    }
}
