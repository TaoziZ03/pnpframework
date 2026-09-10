using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.References;
using System.Collections.Generic;

[TestClass]
public class ClassicWikiRestSourceAdapterTests
{
    [TestMethod]
    public void SameInventoryAcceptsCanonicalCapturedReference()
    {
        var reconstructed = CreateInventory();
        var declared = CreateInventory();

        Assert.IsTrue(ClassicWikiRestSourceAdapter.SameInventory(declared, reconstructed));
    }

    [TestMethod]
    public void SameInventoryRejectsDeclaredStaleId()
    {
        var reconstructed = CreateInventory();
        var declared = CreateInventory();
        declared[0].Id = new string('0', 64);

        Assert.IsFalse(ClassicWikiRestSourceAdapter.SameInventory(declared, reconstructed));
    }

    [DataTestMethod]
    [DataRow(PageCaptureStatus.Failed)]
    [DataRow(PageCaptureStatus.NotReturned)]
    public void SameInventoryRejectsUnavailableDeclaredCaptureStatus(PageCaptureStatus status)
    {
        var reconstructed = CreateInventory();
        var declared = CreateInventory();
        declared[0].CaptureStatus = status;

        Assert.IsFalse(ClassicWikiRestSourceAdapter.SameInventory(declared, reconstructed));
    }

    private static List<PageReferenceSnapshot> CreateInventory()
    {
        const string consumer = "a[href]";
        const string absoluteUrl = "https://microsoft.sharepoint.com/teams/office_rdx/rm/SitePages/Mac%20Office.aspx";
        return new List<PageReferenceSnapshot>
        {
            new PageReferenceSnapshot
            {
                Id = ClassicWikiDigest.ComputeSha256(consumer + "\n" + absoluteUrl),
                Consumer = consumer,
                Kind = PageReferenceKind.Anchor,
                OriginalValue = "/teams/office_rdx/rm/SitePages/Mac%20Office.aspx",
                SourceAbsoluteUrl = absoluteUrl,
                SourceServerRelativeUrl = "/teams/office_rdx/rm/SitePages/Mac Office.aspx",
                CaptureStatus = PageCaptureStatus.Captured
            }
        };
    }
}
