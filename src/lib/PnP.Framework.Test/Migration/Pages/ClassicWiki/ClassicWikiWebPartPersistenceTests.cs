using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Test.Utilities.WebParts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Test.Migration.Pages.ClassicWiki
{
    [TestClass]
    public class ClassicWikiWebPartPersistenceTests
    {
        private static readonly Guid OperationId = new Guid("88888888-8888-8888-8888-888888888888");
        private static readonly Guid SourceId = new Guid("99999999-9999-9999-9999-999999999999");
        private static readonly string PlanDigest = new string('a', 64);

        [DataTestMethod]
        [DataRow(false, "wpz", 0)]
        [DataRow(true, "wpz", 3)]
        [DataRow(false, null, 2)]
        [DataRow(true, null, 0)]
        public void DirectWriterUsesSharedSaveAndPreservesZoneCountAndOperationIdentity(bool xlv, string zone, int index)
        {
            var xml = xlv ? ViewBoundRequestFixture.V3() : NativeV2Export.Xml;
            var requests = new ViewBoundRequestFixture();
            var journal = new InMemoryMigrationExecutionJournal();
            var recorder = new MigrationExecutionRecorder(OperationId, PlanDigest, journal);
            var warnings = new List<string>();
            using (var context = requests.CreateContext())
            {
                var file = context.Web.GetFileByServerRelativeUrl(ViewBoundRequestFixture.PagePath);
                var count = ClassicWikiWebPartWriter.WriteWebParts(context, file,
                    new[] { Placement(SourceId, xml, zone, index) }, recorder, warnings);
                Assert.AreEqual(1, count);
            }
            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(1, requests.Bodies.Count, "Save is batched inside the existing recorder operation.");
            requests.AssertOneAdd(xml, 1, zone ?? "Bottom", index);
            Assert.AreEqual(1, journal.Intents.Count);
            Assert.AreEqual(1, journal.Receipts.Count);
            Assert.AreEqual(1, recorder.Steps.Count);
            Assert.AreEqual(MutationOutcome.Applied, journal.Receipts.Single().Outcome);
            Assert.AreEqual("webpart.place." + SourceId.ToString("N"), journal.Receipts.Single().ActionId);
            Assert.AreEqual(OperationId, journal.Receipts.Single().OperationId);
            Assert.AreEqual(PlanDigest, journal.Receipts.Single().PlanDigest);
            Assert.AreEqual(0, journal.Receipts.Single().Sequence);
            Assert.AreEqual(0, journal.Verifications.Count, "The helper cannot award fresh verification or maturity.");
        }

        [DataTestMethod]
        [DataRow("AddWebPart", -2146233079)]
        [DataRow("SaveWebPartChanges", -2146233079)]
        [DataRow("Execute", -2146233079)]
        [DataRow("SaveWebPartChanges", -2147024891)]
        public void FailedOrDeniedInstanceIsNotAppliedAndIndependentInstanceContinues(string failure, int code)
        {
            var requests = new ViewBoundRequestFixture { FailOn = failure, FailureCode = code };
            var journal = new InMemoryMigrationExecutionJournal();
            var recorder = new MigrationExecutionRecorder(OperationId, PlanDigest, journal);
            var warnings = new List<string>();
            var independentId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            using (var context = requests.CreateContext())
            {
                var file = context.Web.GetFileByServerRelativeUrl(ViewBoundRequestFixture.PagePath);
                var count = ClassicWikiWebPartWriter.WriteWebParts(context, file, new[]
                {
                    Placement(SourceId, NativeV2Export.Xml),
                    Placement(independentId, ViewBoundRequestFixture.V3("ContentEditorWebPart"))
                }, recorder, warnings);
                Assert.AreEqual(1, count, "A failed Add/save/execute must never increment success count.");
            }
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "Synthetic " + failure + " failure");
            Assert.AreEqual(2, journal.Intents.Count, "Only the two original placements may write intents.");
            Assert.AreEqual(2, journal.Receipts.Count);
            Assert.AreEqual(MutationOutcome.Failed, journal.Receipts[0].Outcome);
            Assert.AreEqual(MutationOutcome.Applied, journal.Receipts[1].Outcome);
            Assert.AreEqual("webpart.place." + SourceId.ToString("N"), journal.Receipts[0].ActionId);
            Assert.AreEqual("webpart.place." + independentId.ToString("N"), journal.Receipts[1].ActionId);
            Assert.AreEqual(0, journal.Receipts[0].Sequence);
            Assert.AreEqual(1, journal.Receipts[1].Sequence);
            Assert.IsTrue(journal.Receipts.All(value => value.OperationId == OperationId && value.PlanDigest == PlanDigest));
            Assert.AreEqual(0, journal.Verifications.Count);
            Assert.AreEqual(2, requests.Bodies.Count, "Do not blindly import/Add again after an ambiguous save failure.");
            Assert.AreEqual(2, requests.Bodies.Sum(body => ViewBoundRequestFixture.Paths(body, "ImportWebPart").Count()));
            Assert.AreEqual(2, requests.Bodies.Sum(body => ViewBoundRequestFixture.Paths(body, "AddWebPart").Count()));
            Assert.AreEqual(1, requests.SaveCount, "The independent CEWP keeps its pre-existing non-save behavior.");
            Assert.AreEqual(1, ViewBoundRequestFixture.Actions(requests.Bodies[0], "SaveWebPartChanges").Count());
            Assert.AreEqual(0, ViewBoundRequestFixture.Actions(requests.Bodies[1], "SaveWebPartChanges").Count());
        }

        [DataTestMethod]
        [DataRow("ContentEditorWebPart")]
        [DataRow("ScriptEditorWebPart")]
        [DataRow("PageViewerWebPart")]
        [DataRow("ListFormWebPart")]
        public void DirectWriterPreservesNonViewLegacyBehavior(string type)
        {
            var requests = new ViewBoundRequestFixture();
            var journal = new InMemoryMigrationExecutionJournal();
            var xml = ViewBoundRequestFixture.V2(type);
            using (var context = requests.CreateContext())
            {
                var count = ClassicWikiWebPartWriter.WriteWebParts(context,
                    context.Web.GetFileByServerRelativeUrl(ViewBoundRequestFixture.PagePath),
                    new[] { Placement(SourceId, xml) },
                    new MigrationExecutionRecorder(OperationId, PlanDigest, journal), new List<string>());
                Assert.AreEqual(1, count);
                requests.AssertOneAdd(xml, 0);
                Assert.AreEqual(MutationOutcome.Applied, journal.Receipts.Single().Outcome);
            }
        }

        [TestMethod]
        public void EmptyExportsStillSkipWithoutIntentCountOrRequest()
        {
            var requests = new ViewBoundRequestFixture();
            var journal = new InMemoryMigrationExecutionJournal();
            using (var context = requests.CreateContext())
            {
                var count = ClassicWikiWebPartWriter.WriteWebParts(context,
                    context.Web.GetFileByServerRelativeUrl(ViewBoundRequestFixture.PagePath),
                    new[] { Placement(SourceId, " ") },
                    new MigrationExecutionRecorder(OperationId, PlanDigest, journal), new List<string>());
                Assert.AreEqual(0, count);
                Assert.AreEqual(0, requests.Bodies.Count);
                Assert.AreEqual(0, journal.Intents.Count);
                Assert.AreEqual(0, journal.Receipts.Count);
            }
        }

        private static ClassicWikiWebPartPlacementPlan Placement(Guid id, string xml, string zone = "wpz", int index = 0)
        {
            return new ClassicWikiWebPartPlacementPlan
            {
                SourceId = id, Title = "Persistence fixture", Xml = xml, ZoneId = zone, TargetZoneIndex = index
            };
        }
    }
}
