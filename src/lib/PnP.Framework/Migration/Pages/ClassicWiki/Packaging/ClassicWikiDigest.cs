using PnP.Framework.Migration.Pages.ClassicWiki.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.Packaging;
using System;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Packaging
{
    public static class ClassicWikiDigest
    {
        public static string ComputeSelectionDigest(ClassicWikiWorkflowSelection selection)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            return PageDigest.ComputeSha256(ClassicWikiPackageSerializer.SerializeCanonical(selection));
        }

        public static string ComputeSnapshotDigest(ClassicWikiCaptureBundle snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return PageDigest.ComputeSha256(ClassicWikiPackageSerializer.SerializeCanonical(snapshot));
        }

        public static string ComputePlanDigest(ClassicWikiMigrationPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return PageDigest.ComputeSha256(ClassicWikiPackageSerializer.SerializeCanonical(plan));
        }

        public static string ComputeSha256(string value)
        {
            return PageDigest.ComputeSha256(value);
        }
    }
}
