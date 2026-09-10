using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Execution.Journaling;
using PnP.Framework.Migration.Execution.Resume;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class PublishingPageIngredientExecutionBindingTests
    {
        private readonly IList<string> temporaryDirectories = new List<string>();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var directory in temporaryDirectories)
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [TestMethod]
        public void ContentTextCanaryWritesSignatureBoundIntentApplyAndFreshVerification()
        {
            var package = Package();
            var journal = new InMemoryMigrationExecutionJournal();
            var operationId = Guid.NewGuid();
            var recorder = new MigrationExecutionRecorder(operationId, package.PlanDigest, journal);
            var session = new PublishingPageIngredientExecutionSession(package, recorder);
            var binding = session.GetBinding("action:content");
            var mutationCalls = 0;

            var result = session.Execute(
                "action:content",
                "Write the admitted Publishing page body.",
                () =>
                {
                    mutationCalls++;
                    return "written";
                },
                value => Exact(binding, "fresh content readback"),
                value => MutationOutcome.Applied,
                value => "Publishing page body applied.");

            Assert.AreEqual("written", result);
            Assert.AreEqual(1, mutationCalls);
            Assert.AreEqual(1, journal.Intents.Count);
            Assert.AreEqual(1, journal.Receipts.Count);
            Assert.AreEqual(1, journal.Verifications.Count);
            Assert.AreEqual(operationId, journal.Intents[0].OperationId);
            Assert.AreEqual(package.PlanDigest, journal.Intents[0].PlanDigest);
            Assert.AreEqual(binding.ActionSignature.Signature, journal.Intents[0].ActionSignature);
            Assert.AreEqual(binding.ActionSignature.Signature, journal.Receipts[0].ActionSignature);
            Assert.AreEqual(binding.ActionSignature.Signature, journal.Verifications[0].ActionSignature);
            Assert.AreEqual(binding.ActionSignature.SemanticDigest, journal.Verifications[0].ObservedStateDigest);
            Assert.AreEqual(MigrationTargetOwnership.MigrationOwned, journal.Verifications[0].Ownership);
            Assert.IsTrue(journal.Verifications[0].FreshReadbackPassed);
        }

        [TestMethod]
        public void ResourceImageCanaryResumesInterruptedTailOnlyAfterFreshExactProbe()
        {
            var package = Package();
            var path = JournalPath();
            PublishingPageIngredientExecutionBinding binding;
            using (var journal = new JsonLinesMigrationExecutionJournal(path))
            {
                var recorder = new MigrationExecutionRecorder(Guid.NewGuid(), package.PlanDigest, journal);
                var session = new PublishingPageIngredientExecutionSession(package, recorder);
                binding = session.GetBinding("action:image");
                session.Execute(
                    "action:image",
                    "Materialize the admitted page image.",
                    () => true,
                    value => Exact(binding, "fresh image readback"));
            }
            File.AppendAllText(path, "{\"partial\":");
            var probeCalls = 0;

            var decision = PublishingPageIngredientExecutionSession.EvaluateResume(
                package,
                "action:image",
                path,
                () =>
                {
                    probeCalls++;
                    return Exact(binding, "resume image readback");
                });

            Assert.AreEqual(1, probeCalls);
            Assert.AreEqual(MigrationResumeDisposition.AlreadySatisfied, decision.Disposition);
            Assert.IsTrue(decision.PriorSealedEvidenceFound);
            using (var continuation = new JsonLinesMigrationExecutionJournal(path))
            {
                Assert.AreEqual(1, continuation.ActiveSegmentIndex);
                var recorder = new MigrationExecutionRecorder(Guid.NewGuid(), package.PlanDigest, continuation);
                var session = new PublishingPageIngredientExecutionSession(package, recorder);
                session.RecordAlreadySatisfiedFromResume("action:image", decision);
            }

            var read = MigrationExecutionJournalReader.Read(path);
            Assert.AreEqual(1, read.InterruptedTails.Count);
            Assert.AreEqual(5, read.Records.Count);
            Assert.AreEqual(
                MigrationExecutionJournalRecordKind.MutationVerification,
                read.Records.Last().RecordKind);
            Assert.AreEqual(binding.ActionSignature.Signature, read.Records.Last().ActionSignature);
        }

        [TestMethod]
        public void SignaturesIgnoreSiblingPlanChangesAndInvalidateRequiredConsumers()
        {
            var original = Package(includeSibling: true);
            var changedSibling = Package(includeSibling: true, siblingEvidence: Hex('8'));
            var changedDependency = Package(imageEvidence: Hex('9'), includeSibling: true);
            var originalBindings = PublishingPageIngredientActionSignatureFactory.Create(original);
            var siblingBindings = PublishingPageIngredientActionSignatureFactory.Create(changedSibling);
            var dependencyBindings = PublishingPageIngredientActionSignatureFactory.Create(changedDependency);

            Assert.AreEqual(
                originalBindings["action:image"].ActionSignature.Signature,
                siblingBindings["action:image"].ActionSignature.Signature);
            Assert.AreEqual(
                originalBindings["action:content"].ActionSignature.Signature,
                siblingBindings["action:content"].ActionSignature.Signature);
            Assert.AreNotEqual(original.PlanDigest, changedSibling.PlanDigest);
            Assert.AreNotEqual(
                originalBindings["action:image"].ActionSignature.Signature,
                dependencyBindings["action:image"].ActionSignature.Signature);
            Assert.AreNotEqual(
                originalBindings["action:content"].ActionSignature.Signature,
                dependencyBindings["action:content"].ActionSignature.Signature);
            CollectionAssert.Contains(
                dependencyBindings["action:content"].ActionSignature.DependencySignatures.ToArray(),
                dependencyBindings["action:image"].ActionSignature.Signature);
        }

        [TestMethod]
        public void PackagePlanAndOperationRebindingAreRejectedBeforeMutation()
        {
            var package = Package();
            var wrongPlanRecorder = new MigrationExecutionRecorder(
                Guid.NewGuid(),
                Hex('e'),
                new InMemoryMigrationExecutionJournal());
            Assert.ThrowsException<InvalidDataException>(() =>
                new PublishingPageIngredientExecutionSession(package, wrongPlanRecorder));

            var emptyOperationRecorder = new MigrationExecutionRecorder(
                Guid.Empty,
                package.PlanDigest,
                new InMemoryMigrationExecutionJournal());
            Assert.ThrowsException<InvalidDataException>(() =>
                new PublishingPageIngredientExecutionSession(package, emptyOperationRecorder));

            package.Plan.IngredientActions.Single(value => value.ActionId == "action:content").PolicyVersion = "changed-after-seal";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageIngredientActionSignatureFactory.Create(package));
        }

        [TestMethod]
        public void OldSignatureResumeDecisionCannotBeReboundToChangedDependency()
        {
            var original = Package();
            var changed = Package(imageEvidence: Hex('9'));
            var originalBinding = PublishingPageIngredientActionSignatureFactory.Create(original)["action:content"];
            var changedBinding = PublishingPageIngredientActionSignatureFactory.Create(changed)["action:content"];
            var decision = new MigrationResumeDecision
            {
                Disposition = MigrationResumeDisposition.AlreadySatisfied,
                FreshProbePerformed = true,
                PriorSealedEvidenceFound = true,
                ActionSignature = originalBinding.ActionSignature.Signature,
                Probe = Exact(originalBinding, "old exact state")
            };
            var session = new PublishingPageIngredientExecutionSession(
                changed,
                new MigrationExecutionRecorder(Guid.NewGuid(), changed.PlanDigest, new InMemoryMigrationExecutionJournal()));

            Assert.AreNotEqual(
                originalBinding.ActionSignature.Signature,
                changedBinding.ActionSignature.Signature);
            Assert.ThrowsException<InvalidDataException>(() =>
                session.RecordAlreadySatisfiedFromResume("action:content", decision));
        }

        [TestMethod]
        public void MutationFailureKeepsSignatureBoundIntentAndFailureReceipt()
        {
            var package = Package();
            var journal = new InMemoryMigrationExecutionJournal();
            var session = new PublishingPageIngredientExecutionSession(
                package,
                new MigrationExecutionRecorder(Guid.NewGuid(), package.PlanDigest, journal));
            var binding = session.GetBinding("action:image");

            Assert.ThrowsException<InvalidOperationException>(() => session.Execute<bool>(
                "action:image",
                "Materialize image.",
                () => throw new InvalidOperationException("synthetic apply failure"),
                value => Exact(binding, "must not run")));

            Assert.AreEqual(1, journal.Intents.Count);
            Assert.AreEqual(1, journal.Receipts.Count);
            Assert.AreEqual(MutationOutcome.Failed, journal.Receipts[0].Outcome);
            Assert.AreEqual(binding.ActionSignature.Signature, journal.Receipts[0].ActionSignature);
            Assert.AreEqual(0, journal.Verifications.Count);
        }

        private PublishingPageMigrationPackage Package(
            string imageEvidence = null,
            bool includeSibling = false,
            string siblingEvidence = null)
        {
            var nodes = new List<PageIngredientNode>
            {
                Node("ingredient:image", PageIngredientKind.Asset, imageEvidence ?? Hex('2')),
                Node("ingredient:content", PageIngredientKind.Content, Hex('3'))
            };
            var actions = new List<PageIngredientAction>
            {
                Action("action:image", "ingredient:image", "/sites/target/SiteAssets/image.png"),
                Action("action:content", "ingredient:content", "/sites/target/Pages/page.aspx#PublishingPageContent")
            };
            var decisions = new List<PageIngredientExecutionDecision>
            {
                Decision("ingredient:image"),
                Decision("ingredient:content")
            };
            if (includeSibling)
            {
                nodes.Add(Node("ingredient:sibling", PageIngredientKind.Policy, siblingEvidence ?? Hex('4')));
                actions.Add(Action("action:sibling", "ingredient:sibling", "urn:target:sibling"));
                decisions.Add(Decision("ingredient:sibling"));
            }
            var plan = new PublishingPageMigrationPlan
            {
                TargetWebUrl = "https://target.sharepoint.com/sites/target",
                TargetPageServerRelativeUrl = "/sites/target/Pages/page.aspx",
                IngredientGraph = new CanonicalPageIngredientGraph
                {
                    Nodes = nodes,
                    Edges = new List<PageIngredientEdge>
                    {
                        new PageIngredientEdge
                        {
                            FromIngredientId = "ingredient:content",
                            ToIngredientId = "ingredient:image",
                            Relationship = PageIngredientRelationship.DependsOn,
                            Requirement = PageIngredientRequirement.Required
                        }
                    }
                },
                IngredientActions = actions,
                ExecutionFrontier = new PageIngredientExecutionFrontier
                {
                    Decisions = decisions
                }
            };
            return new PublishingPageMigrationPackage
            {
                SelectionDigest = Hex('1'),
                Plan = plan,
                PlanDigest = PublishingPageDigest.ComputePlanDigest(plan)
            };
        }

        private static PageIngredientNode Node(string id, PageIngredientKind kind, string evidenceDigest)
        {
            return new PageIngredientNode
            {
                Id = id,
                Kind = kind,
                HasContent = true,
                Ownership = PageIngredientOwnership.SourceOwned,
                EvidenceDigest = evidenceDigest
            };
        }

        private static PageIngredientAction Action(string actionId, string ingredientId, string targetIdentity)
        {
            return new PageIngredientAction
            {
                ActionId = actionId,
                IngredientId = ingredientId,
                Capability = IngredientCapability.Available,
                Disposition = IngredientDisposition.Preserve,
                Realization = "copy-exact-value",
                TargetIdentity = targetIdentity,
                PolicyId = "policy.canary",
                PolicyVersion = "1",
                VerificationAssertions = new List<string> { "Fresh target state matches." }
            };
        }

        private static PageIngredientExecutionDecision Decision(string ingredientId)
        {
            return new PageIngredientExecutionDecision
            {
                IngredientId = ingredientId,
                State = PageIngredientExecutionState.Executable
            };
        }

        private static MigrationFreshProbeResult Exact(
            PublishingPageIngredientExecutionBinding binding,
            string diagnostic)
        {
            return new MigrationFreshProbeResult
            {
                State = MigrationFreshProbeState.Exact,
                Ownership = binding.ExpectedOwnership,
                ProvenanceMatched = binding.ExpectedOwnership == MigrationTargetOwnership.MigrationOwned,
                ObservedStateDigest = binding.ActionSignature.SemanticDigest,
                TargetIdentityDigest = binding.ActionSignature.TargetIdentityDigest,
                Diagnostic = diagnostic
            };
        }

        private string JournalPath()
        {
            var directory = Path.Combine(Path.GetTempPath(), "pnp-publishing-action-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            temporaryDirectories.Add(directory);
            return Path.Combine(directory, "journal.jsonl");
        }

        private static string Hex(char value) => new string(value, 64);
    }
}
