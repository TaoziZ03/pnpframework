using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Content;
using PnP.Framework.Migration.Pages.Publishing.EnterpriseWiki;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Comparison;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Lifecycle;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.Publishing.Reporting;
using PnP.Framework.Migration.Pages.Security;
using PnP.Framework.Migration.Pages.Publishing.Verification;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWebParts.Bindings;
using PnP.Framework.Migration.Pages.ClassicWebParts.Planning;
using PnP.Framework.Migration.Pages.Publishing.Layouts;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Pages.Profiles;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Cohorts;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Lists.Items;
using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Execution;
using PnP.Framework.Migration.Lists.Fields;
using PnP.Framework.Migration.Lists.ContentTypes;
using PnP.Framework.Migration.Lists.Packaging;
using PnP.Framework.Migration.Lists.Views;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Schema.Fields;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Taxonomy;
using PnP.Framework.Migration.Pages.Fields.Taxonomy;
using Microsoft.SharePoint.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class EnterpriseWikiMigrationTests
    {
        [TestMethod]
        public void ContentTypeClassificationExcludesProjectPages()
        {
            Assert.IsTrue(EnterpriseWikiPageDiscovery.IsEnterpriseWikiContentType(BuiltInContentTypeId.EnterpriseWikiPage + "001122"));
            Assert.IsFalse(EnterpriseWikiPageDiscovery.IsEnterpriseWikiContentType(BuiltInContentTypeId.ProjectPage + "001122"));
            Assert.IsFalse(EnterpriseWikiPageDiscovery.IsEnterpriseWikiContentType("0x010100C568DB52D9"));
        }

        [TestMethod]
        public void PublishingExporterDerivesTheWebPathWithoutASeparateSharePointQuery()
        {
            Assert.AreEqual(
                "/teams/campus ipkits/site",
                PublishingPagePackageExporter.GetContextWebServerRelativeUrl(
                    "https://source.sharepoint.com/teams/campus%20ipkits/site/"));
            Assert.AreEqual(
                "/",
                PublishingPagePackageExporter.GetContextWebServerRelativeUrl(
                    "https://source.sharepoint.com/"));
        }

        [TestMethod]
        public void RuntimeResolutionUsesClrPageTypeBeforeContentTypeProfile()
        {
            var artifact = new PageArtifactSnapshot
            {
                PageDirective = new PageDirectiveSnapshot
                {
                    Inherits = "Microsoft.SharePoint.WebPartPages.WikiEditPage, Microsoft.SharePoint"
                }
            };

            var runtime = PageRuntimeResolver.Resolve(
                artifact,
                null,
                BuiltInContentTypeId.EnterpriseWikiPage);

            Assert.AreEqual(PageRuntimeAdapterIds.Wiki, runtime.AdapterId);
            Assert.AreEqual(PageRuntimeDetectionSource.PageDirective, runtime.DetectionSource);
            Assert.AreEqual(PageRuntimeResolutionState.Resolved, runtime.ResolutionState);
        }

        [TestMethod]
        public void ProjectPageProducesNonExclusiveProfileSignalsButIsOutsideEwV1Cohort()
        {
            var source = new PageIdentity
            {
                ContentTypeId = BuiltInContentTypeId.ProjectPage + "001122"
            };
            var signals = PublishingPageProfileSignalProjector.Project(
                source,
                new PublishingPageLayoutSnapshot { FileName = "ProjectPage.aspx" },
                new[]
                {
                    new PageFieldValueSnapshot { InternalName = "Wiki_x0020_Page_x0020_Categories" },
                    new PageFieldValueSnapshot { InternalName = "TaskStatus" }
                });

            Assert.IsTrue(signals.Any(value => value.ProfileId == PageProfileIds.EnterpriseWiki));
            Assert.IsTrue(signals.Any(value => value.ProfileId == PageProfileIds.ProjectPage));
            Assert.AreEqual(
                ValidationCohortDisposition.Excluded,
                EnterpriseWikiV1CohortPolicy.Assess(source.ContentTypeId).Disposition);
        }

        [TestMethod]
        public void RequiredIngredientCanBeDroppedOnlyWhenConsumerTransformReleasesIt()
        {
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = "consumer", Kind = PageIngredientKind.Content, HasContent = true },
                    new PageIngredientNode { Id = "dependency", Kind = PageIngredientKind.Reference, HasContent = true }
                },
                Edges = new List<PageIngredientEdge>
                {
                    new PageIngredientEdge
                    {
                        FromIngredientId = "consumer",
                        ToIngredientId = "dependency",
                        Relationship = PageIngredientRelationship.DependsOn,
                        Requirement = PageIngredientRequirement.Required
                    }
                }
            };
            var actions = new List<PageIngredientAction>
            {
                new PageIngredientAction
                {
                    ActionId = "action:consumer",
                    IngredientId = "consumer",
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Preserve
                },
                new PageIngredientAction
                {
                    ActionId = "action:dependency",
                    IngredientId = "dependency",
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Drop
                }
            };

            var blocked = PageIngredientPlanEvaluator.Evaluate(graph, actions);
            Assert.AreEqual(PageMigrationOutcome.Invalid, blocked.Outcome);
            Assert.IsTrue(blocked.Issues.Any(value => value.Code == "RequiredIngredientDependencyUnsatisfied"));

            actions[0].ReleasedDependencyIngredientIds.Add("dependency");
            var invalidRelease = PageIngredientPlanEvaluator.Evaluate(graph, actions);
            Assert.AreEqual(PageMigrationOutcome.Invalid, invalidRelease.Outcome);
            Assert.IsTrue(invalidRelease.Issues.Any(value => value.Code == "IngredientDependencyReleaseInvalid"));

            actions[0].Disposition = IngredientDisposition.Transform;
            var released = PageIngredientPlanEvaluator.Evaluate(graph, actions);
            Assert.AreEqual(PageMigrationOutcome.ExecutableWithLoss, released.Outcome);
            Assert.AreEqual(0, released.Issues.Count);
        }

        [TestMethod]
        public void DeferredIngredientIsMitigationPendingRatherThanBlocked()
        {
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = "ingredient", Kind = PageIngredientKind.Field, HasContent = true }
                }
            };
            var actions = new List<PageIngredientAction>
            {
                new PageIngredientAction
                {
                    ActionId = "action:ingredient",
                    IngredientId = "ingredient",
                    Capability = IngredientCapability.Incompatible,
                    Disposition = IngredientDisposition.Defer,
                    Reason = "More source evidence is required."
                }
            };

            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, actions);

            Assert.AreEqual(PageMigrationOutcome.MitigationPending, evaluation.Outcome);
            Assert.IsFalse(evaluation.IsExecutable);
            Assert.IsTrue(evaluation.Issues.Any(value =>
                value.Code == "IngredientMitigationPending"
                && value.Severity == MigrationIssueSeverity.Warning));
        }

        [TestMethod]
        public void DeferredIngredientPrunesOnlyItsRequiredConsumerBranch()
        {
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = "consumer", Kind = PageIngredientKind.Content, HasContent = true },
                    new PageIngredientNode { Id = "dependency", Kind = PageIngredientKind.Reference, HasContent = true },
                    new PageIngredientNode { Id = "independent", Kind = PageIngredientKind.List, HasContent = true }
                },
                Edges = new List<PageIngredientEdge>
                {
                    new PageIngredientEdge
                    {
                        FromIngredientId = "consumer",
                        ToIngredientId = "dependency",
                        Relationship = PageIngredientRelationship.DependsOn,
                        Requirement = PageIngredientRequirement.Required
                    }
                }
            };
            var actions = new List<PageIngredientAction>
            {
                new PageIngredientAction { ActionId = "action:consumer", IngredientId = "consumer", Capability = IngredientCapability.Available, Disposition = IngredientDisposition.Preserve },
                new PageIngredientAction { ActionId = "action:dependency", IngredientId = "dependency", Capability = IngredientCapability.Incompatible, Disposition = IngredientDisposition.Defer },
                new PageIngredientAction { ActionId = "action:independent", IngredientId = "independent", Capability = IngredientCapability.Available, Disposition = IngredientDisposition.Preserve }
            };

            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, actions);

            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, evaluation.Outcome);
            Assert.IsTrue(evaluation.IsExecutable);
            Assert.AreEqual(PageIngredientExecutionState.Deferred, evaluation.ExecutionFrontier.GetState("dependency"));
            Assert.AreEqual(PageIngredientExecutionState.SkippedByDeferredDependency, evaluation.ExecutionFrontier.GetState("consumer"));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState("independent"));
            CollectionAssert.AreEqual(
                new[] { "dependency" },
                evaluation.ExecutionFrontier.Decisions.Single(value => value.IngredientId == "consumer").CauseIngredientIds.ToArray());
        }

        [TestMethod]
        public void IngredientBlockRequiresMatchingLiteralHttpAuthorizationEvidence()
        {
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = "ingredient", Kind = PageIngredientKind.Layout, HasContent = true }
                }
            };
            var actions = new List<PageIngredientAction>
            {
                new PageIngredientAction
                {
                    ActionId = "action:ingredient",
                    IngredientId = "ingredient",
                    Capability = IngredientCapability.Missing,
                    Disposition = IngredientDisposition.Block,
                    Reason = "The wire request returned HTTP 403."
                }
            };

            var withoutEvidence = PageIngredientPlanEvaluator.Evaluate(graph, actions);
            Assert.AreEqual(PageMigrationOutcome.Invalid, withoutEvidence.Outcome);
            Assert.IsTrue(withoutEvidence.Issues.Any(value =>
                value.Code == "IngredientBlockWithoutAuthorizationEvidence"));

            var evidence = new Dictionary<string, LiteralHttpAuthorizationEvidence>(StringComparer.Ordinal)
            {
                ["ingredient"] = LiteralHttpAuthorizationEvidence.Create(
                    "capture-layout",
                    "https://source.example/_vti_bin/client.svc/ProcessQuery",
                    403,
                    DateTimeOffset.UtcNow)
            };
            var withEvidence = PageIngredientPlanEvaluator.Evaluate(graph, actions, evidence);

            Assert.AreEqual(PageMigrationOutcome.AuthorizationBlocked, withEvidence.Outcome);
            Assert.IsFalse(withEvidence.IsExecutable);
            Assert.IsTrue(withEvidence.Issues.Any(value =>
                value.Code == "IngredientAuthorizationBlocked"
                && value.Severity == MigrationIssueSeverity.Blocker));
        }

        [TestMethod]
        public void AuthorizationBlockPrunesOnlyItsRequiredConsumerBranch()
        {
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = "consumer", Kind = PageIngredientKind.PageArtifact, HasContent = true },
                    new PageIngredientNode { Id = "dependency", Kind = PageIngredientKind.Layout, HasContent = true },
                    new PageIngredientNode { Id = "independent", Kind = PageIngredientKind.List, HasContent = true }
                },
                Edges = new List<PageIngredientEdge>
                {
                    new PageIngredientEdge
                    {
                        FromIngredientId = "consumer",
                        ToIngredientId = "dependency",
                        Relationship = PageIngredientRelationship.DependsOn,
                        Requirement = PageIngredientRequirement.Required
                    }
                }
            };
            var actions = new List<PageIngredientAction>
            {
                new PageIngredientAction { ActionId = "action:consumer", IngredientId = "consumer", Capability = IngredientCapability.Available, Disposition = IngredientDisposition.Preserve },
                new PageIngredientAction { ActionId = "action:dependency", IngredientId = "dependency", Capability = IngredientCapability.Missing, Disposition = IngredientDisposition.Block },
                new PageIngredientAction { ActionId = "action:independent", IngredientId = "independent", Capability = IngredientCapability.Available, Disposition = IngredientDisposition.Preserve }
            };
            var evidence = new Dictionary<string, LiteralHttpAuthorizationEvidence>(StringComparer.Ordinal)
            {
                ["dependency"] = LiteralHttpAuthorizationEvidence.Create(
                    "capture-layout",
                    "https://source.example/_vti_bin/client.svc/ProcessQuery",
                    403,
                    DateTimeOffset.UtcNow)
            };

            var evaluation = PageIngredientPlanEvaluator.Evaluate(graph, actions, evidence);

            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, evaluation.Outcome);
            Assert.IsTrue(evaluation.IsExecutable);
            Assert.AreEqual(PageIngredientExecutionState.AuthorizationBlocked, evaluation.ExecutionFrontier.GetState("dependency"));
            Assert.AreEqual(PageIngredientExecutionState.SkippedByAuthorizationDependency, evaluation.ExecutionFrontier.GetState("consumer"));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState("independent"));
        }

        [TestMethod]
        public void FinalIngredientProjectionReservesBlockForLiteralAuthorizationEvidence()
        {
            var package = CreateMigrationPackage();
            package.Plan.LayoutAdmission.Disposition = PublishingPageLayoutMaterializationDisposition.Block;
            package.Snapshot.Layout.AuthorizationEvidence = null;

            var mitigationActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                package.Plan.IngredientGraph);
            var mitigationEvaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Plan.IngredientGraph,
                mitigationActions);

            Assert.AreEqual(
                IngredientDisposition.Defer,
                mitigationActions.Single(value => value.IngredientId == PublishingPageIngredientIds.Layout).Disposition);
            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, mitigationEvaluation.Outcome);
            Assert.IsTrue(mitigationEvaluation.ExecutionFrontier.HasExecutableIngredients);

            package.Snapshot.Layout.AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                "capture-page-layout-owner",
                "https://source.example/_vti_bin/client.svc/ProcessQuery",
                403,
                DateTimeOffset.UtcNow);
            var authorizationActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                package.Plan.IngredientGraph);
            var authorizationEvaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Plan.IngredientGraph,
                authorizationActions,
                PublishingPageIngredientAuthorizationPolicy.GetEvidence(package.Snapshot));

            Assert.AreEqual(
                IngredientDisposition.Block,
                authorizationActions.Single(value => value.IngredientId == PublishingPageIngredientIds.Layout).Disposition);
            Assert.AreEqual(
                IngredientDisposition.Block,
                authorizationActions.Single(value => value.IngredientId == PublishingPageIngredientIds.ContentType).Disposition);
            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, authorizationEvaluation.Outcome);
            Assert.IsTrue(authorizationEvaluation.ExecutionFrontier.HasExecutableIngredients);
        }

        [TestMethod]
        public void LayoutFieldSchemaDoesNotRequireOptionalIdentityItemValue()
        {
            var package = CreateMigrationPackage();
            var field = package.Snapshot.Fields.Single(value => value.InternalName == "OOCLReference");
            field.TypeAsString = "User";
            field.Kind = PageFieldValueKind.User;
            field.Required = false;
            package.Snapshot.Layout.Controls.Add(new PublishingPageLayoutControl
            {
                TagPrefix = "PublishingWebControls",
                ControlName = "UserField",
                FieldName = field.InternalName
            });
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);

            var layoutValueEdge = package.Snapshot.IngredientGraph.Edges.Single(value =>
                value.FromIngredientId == PublishingPageIngredientIds.Layout
                && value.ToIngredientId == PublishingPageIngredientIds.Field(field.InternalName));
            Assert.AreEqual(PageIngredientRequirement.Optional, layoutValueEdge.Requirement);

            var fieldPlan = package.Plan.FieldActions.Single(value => value.SourceInternalName == field.InternalName);
            fieldPlan.Disposition = PageFieldDisposition.EvidenceOnly;
            fieldPlan.Reason = "Retain the optional source identity as evidence and leave the target value unset.";
            package.Plan.IngredientActions = PublishingPageIngredientActionProjector.Project(package.Snapshot, package.Plan);
            var fieldAction = package.Plan.IngredientActions.Single(value =>
                value.IngredientId == PublishingPageIngredientIds.Field(field.InternalName));
            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Snapshot.IngredientGraph,
                package.Plan.IngredientActions);

            Assert.AreEqual(IngredientDisposition.Delegate, fieldAction.Disposition);
            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, evaluation.Outcome);
            Assert.IsFalse(evaluation.Issues.Any(value =>
                value.Code == "IngredientBlocked"
                || value.Code == "RequiredIngredientDependencyUnsatisfied"));
        }

        [TestMethod]
        public void EmptyIngredientDispositionDoesNotDegradeAggregateOutcome()
        {
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = "present", Kind = PageIngredientKind.Content, HasContent = true },
                    new PageIngredientNode { Id = "empty", Kind = PageIngredientKind.Field, HasContent = false }
                }
            };
            var actions = new List<PageIngredientAction>
            {
                new PageIngredientAction
                {
                    ActionId = "action:present",
                    IngredientId = "present",
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Preserve
                },
                new PageIngredientAction
                {
                    ActionId = "action:empty",
                    IngredientId = "empty",
                    Capability = IngredientCapability.Unknown,
                    Disposition = IngredientDisposition.Drop
                }
            };

            Assert.AreEqual(PageMigrationOutcome.Exact, PageIngredientPlanEvaluator.Evaluate(graph, actions).Outcome);
        }

        [TestMethod]
        public void EmptyUnavailableFieldProjectsAsDropInsteadOfBlock()
        {
            var package = CreateMigrationPackage();
            var field = package.Snapshot.Fields.Single(value => value.InternalName == "OOCLReference");
            field.HasValue = false;
            field.Kind = PageFieldValueKind.Null;
            field.RawType = null;
            field.RawValue = null;
            field.RawValueJson = null;
            field.CaptureStatus = PageCaptureStatus.NotReturned;
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.Plan.FieldActions.Clear();
            package.Plan.FieldActions.Add(new PageFieldAction
            {
                SourceInternalName = field.InternalName,
                TargetInternalName = field.InternalName,
                Disposition = PageFieldDisposition.CaptureUnavailable,
                Reason = "The field definition was captured, but no source value was returned."
            });

            var actions = PublishingPageIngredientActionProjector.Project(package.Snapshot, package.Plan);
            var fieldAction = actions.Single(value => value.IngredientId == "field:OOCLReference");
            var evaluation = PageIngredientPlanEvaluator.Evaluate(package.Snapshot.IngredientGraph, actions);

            Assert.AreEqual(IngredientCapability.Unknown, fieldAction.Capability);
            Assert.AreEqual(IngredientDisposition.Drop, fieldAction.Disposition);
            Assert.AreEqual("discard-no-source-value", fieldAction.Realization);
            Assert.IsFalse(evaluation.Issues.Any(value => value.Code == "IngredientBlocked"));
        }

        [TestMethod]
        public void DanglingTaxonomyRelationshipIsSealedToExactFieldValuesAndHiddenIdentity()
        {
            var field = CreateDanglingTaxonomyField();

            Assert.AreEqual(0, PageTaxonomyRelationshipEvidence.ValidateSealedField(field).Count);
            Assert.AreEqual(0, PageTaxonomyRelationshipEvidence.GetFidelityErrors(field, field.TaxonomyValues.Single()).Count);
            Assert.AreEqual(TaxonomyRelationshipState.DanglingTermAbsent, field.TaxonomyValues.Single().Relationship.State);
            Assert.IsFalse(string.IsNullOrWhiteSpace(field.TaxonomyValueSetSha256));
            Assert.IsFalse(string.IsNullOrWhiteSpace(field.TaxonomyValues.Single().Relationship.EvidenceSha256));

            field.TaxonomyValues.Single().Label = "healed-label";

            Assert.IsTrue(PageTaxonomyRelationshipEvidence.ValidateSealedField(field).Any(value =>
                value.Contains("digest", StringComparison.OrdinalIgnoreCase)
                || value.Contains("proof", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void ConflictedTaxonomyBindingRemainsExportableButNotExecutable()
        {
            var snapshot = CreateSnapshot();
            var field = CreateDanglingTaxonomyField();
            field.TaxonomyBinding.TermStoreId = Guid.Empty;
            field.TaxonomyBinding.BoundTermSetId = Guid.Empty;
            field.TaxonomyBinding.TextFieldId = Guid.Empty;
            var relationship = field.TaxonomyValues.Single().Relationship;
            relationship.State = TaxonomyRelationshipState.Conflict;
            relationship.Diagnostics.Add("The source taxonomy field binding could not be read.");
            PageTaxonomyRelationshipProof.Seal(field);
            snapshot.Fields.Add(field);
            snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(snapshot.Source, snapshot.Layout, snapshot.Fields);
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            PublishingPagePackageValidator.ValidateExport(export);

            Assert.AreEqual(0, PageTaxonomyRelationshipEvidence.ValidateSealedField(field).Count);
            Assert.IsTrue(PageTaxonomyRelationshipEvidence.GetFidelityErrors(
                field,
                field.TaxonomyValues.Single()).Any(value =>
                    value.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("Conflict", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void TaxonomyHiddenListLocalizationPreservesCapturedLcidValues()
        {
            var entry = new TaxonomyHiddenListEntrySnapshot
            {
                Title = "Category",
                Terms = new List<TaxonomyLocalizedTextSnapshot>
                {
                    new TaxonomyLocalizedTextSnapshot { FieldInternalName = "Term1033", Value = "Category" },
                    new TaxonomyLocalizedTextSnapshot { FieldInternalName = "Term2052", Value = "Category ZH" }
                },
                Paths = new List<TaxonomyLocalizedTextSnapshot>
                {
                    new TaxonomyLocalizedTextSnapshot { FieldInternalName = "Path1033", Value = "Root;Category" },
                    new TaxonomyLocalizedTextSnapshot { FieldInternalName = "Path2052", Value = "Root ZH;Category ZH" }
                }
            };
            var targetValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Term1033"] = "Category",
                ["Term2052"] = "Category ZH",
                ["Path1033"] = "Root;Category",
                ["Path2052"] = "Root ZH;Category ZH"
            };

            Assert.AreEqual("Root ZH;Category ZH", entry.PreferredPath("Category ZH"));
            Assert.AreEqual(0, PageTaxonomyHiddenListLocalization.GetTargetCoverageErrors(
                entry,
                new[] { "Term1033", "Term2052" },
                new[] { "Path1033", "Path2052" }).Count);
            Assert.IsTrue(PageTaxonomyHiddenListLocalization.MatchesCapturedValues(
                entry,
                new[] { "Term1033", "Term2052" },
                new[] { "Path1033", "Path2052" },
                fieldName => targetValues[fieldName]));
            Assert.IsTrue(PageTaxonomyHiddenListLocalization.GetTargetCoverageErrors(
                entry,
                new[] { "Term1033", "Term2052" },
                new[] { "Path1033" }).Any(value => value.Contains("Path2052", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void TaxonomyRelationshipIsARequiredIngredientOfThePageField()
        {
            var snapshot = CreateSnapshot();
            var field = CreateDanglingTaxonomyField();
            snapshot.Fields.Add(field);
            snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(snapshot.Source, snapshot.Layout, snapshot.Fields);
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);
            var termId = Guid.Parse(field.TaxonomyValues.Single().TermGuid);
            var relationshipId = PublishingPageIngredientIds.TaxonomyRelationship(field.Id, termId, field.TaxonomyValues.Single().WssId);

            var node = snapshot.IngredientGraph.Nodes.Single(value => value.Id == relationshipId);
            var edge = snapshot.IngredientGraph.Edges.Single(value =>
                value.FromIngredientId == PublishingPageIngredientIds.Field(field.InternalName)
                && value.ToIngredientId == relationshipId);

            Assert.AreEqual(PageIngredientKind.Taxonomy, node.Kind);
            Assert.AreEqual(field.TaxonomyValues.Single().Relationship.EvidenceSha256, node.EvidenceDigest);
            Assert.AreEqual(PageIngredientRequirement.Required, edge.Requirement);
        }

        [TestMethod]
        public void ExportValidationRejectsRedigestedTaxonomyRelationshipTampering()
        {
            var snapshot = CreateSnapshot();
            var field = CreateDanglingTaxonomyField();
            snapshot.Fields.Add(field);
            snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(snapshot.Source, snapshot.Layout, snapshot.Fields);
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };
            PublishingPagePackageValidator.ValidateExport(export);

            field.TaxonomyValues.Single().Relationship.ValueHiddenListEntry.Paths.Single().Value = "different/path";
            export.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot);

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateExport(export));
        }

        [TestMethod]
        public void InvalidTaxonomyRelationshipProjectsAsExplicitTransformationWithoutRepair()
        {
            var package = CreateMigrationPackage();
            var field = CreateDanglingTaxonomyField();
            var value = field.TaxonomyValues.Single();
            var termId = Guid.Parse(value.TermGuid);
            package.Snapshot.Fields.Add(field);
            package.Snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(
                package.Snapshot.Source,
                package.Snapshot.Layout,
                package.Snapshot.Fields);
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.Plan.FieldActions.Add(new PageFieldAction
            {
                SourceInternalName = field.InternalName,
                TargetInternalName = field.InternalName,
                TargetTypeAsString = field.TypeAsString,
                Disposition = PageFieldDisposition.ApplyTaxonomyRelationships,
                Reason = "Reproduce exact relationship."
            });
            package.Plan.TaxonomyRelationshipActions.Add(new TaxonomyRelationshipAction
            {
                SourceFieldId = field.Id,
                SourceFieldInternalName = field.InternalName,
                SourceTermId = termId,
                SourceWssId = value.WssId,
                SourceEvidenceSha256 = value.Relationship.EvidenceSha256,
                SourceState = TaxonomyRelationshipState.DanglingTermAbsent,
                Disposition = TaxonomyRelationshipDisposition.PreserveDanglingTermAbsent,
                TargetFieldId = field.Id,
                TargetTextFieldId = Guid.Parse("dddddddd-1111-2222-3333-eeeeeeeeeeee"),
                TargetFieldOpen = field.TaxonomyBinding.Open,
                TargetTermStoreId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
                TargetBoundTermSetId = Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd"),
                TargetValueHiddenListTermSetId = Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd"),
                TargetTaxCatchAllHiddenListTermSetId = Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd"),
                Reason = "Keep the Term absent and reproduce the dangling relationship.",
                VerificationAssertions = new List<string>
                {
                    "The Term remains absent.",
                    "The target-local WssId resolves to the sealed hidden identity."
                }
            });

            var actions = PublishingPageIngredientActionProjector.Project(package.Snapshot, package.Plan);
            var relationship = actions.Single(action => action.IngredientId ==
                PublishingPageIngredientIds.TaxonomyRelationship(field.Id, termId, value.WssId));

            Assert.AreEqual(IngredientDisposition.Transform, relationship.Disposition);
            Assert.AreEqual("reproduce-dangling-term-with-target-local-wssid", relationship.Realization);
            StringAssert.Contains(relationship.Reason, "absent");
        }

        [TestMethod]
        public void UnselectedTaxonomyRelationshipRetainsSealedEvidenceWithoutTargetClaims()
        {
            var package = CreateMigrationPackage();
            var field = CreateDanglingTaxonomyField();
            var value = field.TaxonomyValues.Single();
            var termId = Guid.Parse(value.TermGuid);
            package.Snapshot.Fields.Add(field);
            package.Snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(
                package.Snapshot.Source,
                package.Snapshot.Layout,
                package.Snapshot.Fields);
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.Plan.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.Plan.FieldActions.Add(new PageFieldAction
            {
                SourceInternalName = field.InternalName,
                TargetInternalName = field.InternalName,
                Disposition = PageFieldDisposition.EvidenceOnly,
                Reason = "The importer does not own this field."
            });
            package.Plan.TaxonomyRelationshipActions.Add(new TaxonomyRelationshipAction
            {
                SourceFieldId = field.Id,
                SourceFieldInternalName = field.InternalName,
                SourceTermId = termId,
                SourceWssId = value.WssId,
                SourceEvidenceSha256 = value.Relationship.EvidenceSha256,
                SourceState = value.Relationship.State,
                Disposition = TaxonomyRelationshipDisposition.RetainEvidenceOnly,
                Reason = "The owning page field is not selected for replay; its exact taxonomy relationship evidence remains sealed in the snapshot."
            });
            package.Plan.IngredientActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                package.Plan.IngredientGraph);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Plan.IngredientGraph,
                package.Plan.IngredientActions);
            package.Plan.MigrationOutcome = evaluation.Outcome;
            package.Plan.IngredientIssues = evaluation.Issues;
            package.Plan.ExecutionFrontier = evaluation.ExecutionFrontier;
            package.State = PublishingPagePackageState.ApprovalReady;
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

            PublishingPagePackageValidator.ValidateMigration(package);
            var relationship = package.Plan.IngredientActions.Single(action => action.IngredientId ==
                PublishingPageIngredientIds.TaxonomyRelationship(field.Id, termId, value.WssId));

            Assert.AreEqual(IngredientCapability.Unknown, relationship.Capability);
            Assert.AreEqual(IngredientDisposition.Delegate, relationship.Disposition);
            Assert.AreEqual("retain-sealed-relationship-evidence", relationship.Realization);
            Assert.IsNull(relationship.TargetIdentity);
            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, package.Plan.MigrationOutcome);

            package.Plan.TaxonomyRelationshipActions.Single().TargetTermStoreId =
                Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void MigrationPackageRequiresOneSealedActionPerTaxonomyRelationship()
        {
            var package = CreateMigrationPackage();
            var field = CreateDanglingTaxonomyField();
            AddDanglingTaxonomyPlan(package, field);

            PublishingPagePackageValidator.ValidateMigration(package);
            var report = PublishingPageMigrationReportBuilder.Build(package);
            StringAssert.Contains(report, "DanglingTermAbsent");
            StringAssert.Contains(report, "PreserveDanglingTermAbsent");
            StringAssert.Contains(report, "evidenceSha256");

            package.Plan.TaxonomyRelationshipActions.Clear();
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void ContentRewriteUsesLongestCaseInsensitiveMappingFirst()
        {
            var replacements = new[]
            {
                new PageTextReplacement
                {
                    Source = "https://source.sharepoint.com/sites/source",
                    Target = "https://target.sharepoint.com/sites/target"
                },
                new PageTextReplacement
                {
                    Source = "https://source.sharepoint.com",
                    Target = "https://target.sharepoint.com"
                }
            };

            var actual = PageTextTransformer.Rewrite(
                "<a href=\"HTTPS://SOURCE.SHAREPOINT.COM/sites/source/Pages/A.aspx\">A</a>",
                replacements);

            Assert.AreEqual("<a href=\"https://target.sharepoint.com/sites/target/Pages/A.aspx\">A</a>", actual);
        }

        [TestMethod]
        public void ExportValidationDetectsSnapshotMutation()
        {
            var snapshot = CreateSnapshot();
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            PublishingPagePackageValidator.ValidateExport(export);
            export.Snapshot.PublishingPageContent = "<p>changed</p>";

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateExport(export));
        }

        [TestMethod]
        public void ExportValidationRederivesRuntimeEvenAfterSnapshotIsRedigested()
        {
            var snapshot = CreateSnapshot();
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot
            };
            snapshot.Runtime.AdapterId = PageRuntimeAdapterIds.Wiki;
            snapshot.Runtime.DetectionSource = PageRuntimeDetectionSource.PageDirective;
            snapshot.Runtime.ResolutionState = PageRuntimeResolutionState.Resolved;
            export.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot);

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateExport(export));
        }

        [TestMethod]
        public void ExportValidationRederivesIngredientGraphEvenAfterSnapshotIsRedigested()
        {
            var snapshot = CreateSnapshot();
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot
            };
            snapshot.IngredientGraph.Nodes.Single(value => value.Id == PublishingPageIngredientIds.Lifecycle).Label = "tampered";
            export.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot);

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateExport(export));
        }

        [TestMethod]
        public void ExportValidationAcceptsLegacyIngredientProjectionWithoutChangingItsSnapshotDigest()
        {
            var snapshot = CreateSnapshot();
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.ProjectLegacy(snapshot);
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            var roundTrip = PublishingPagePackageSerializer.Deserialize<PublishingPageExportPackage>(
                PublishingPagePackageSerializer.Serialize(export));

            Assert.IsNull(roundTrip.Snapshot.IngredientGraph.ProjectionVersion);
            Assert.AreEqual(export.SnapshotDigest, PublishingPageDigest.ComputeSnapshotDigest(roundTrip.Snapshot));
            PublishingPagePackageValidator.ValidateExport(roundTrip);
        }

        [TestMethod]
        public void ExportValidationAcceptsVersion2IngredientProjectionWithoutChangingItsSnapshotDigest()
        {
            var snapshot = CreateSnapshot();
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.ProjectVersion2(snapshot);
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            var roundTrip = PublishingPagePackageSerializer.Deserialize<PublishingPageExportPackage>(
                PublishingPagePackageSerializer.Serialize(export));

            Assert.AreEqual(PublishingPageIngredientGraphProjector.ProjectionVersionV2, roundTrip.Snapshot.IngredientGraph.ProjectionVersion);
            Assert.AreEqual(export.SnapshotDigest, PublishingPageDigest.ComputeSnapshotDigest(roundTrip.Snapshot));
            PublishingPagePackageValidator.ValidateExport(roundTrip);
        }

        [TestMethod]
        public void ExportPackageCanBeDeserializedFromAStreamWithoutChangingItsSnapshotDigest()
        {
            var snapshot = CreateSnapshot();
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(PublishingPagePackageSerializer.Serialize(export)));

            var roundTrip = PublishingPagePackageSerializer.Deserialize<PublishingPageExportPackage>(stream);

            Assert.AreEqual(export.SnapshotDigest, roundTrip.SnapshotDigest);
            PublishingPagePackageValidator.ValidateExport(roundTrip);
        }

        [TestMethod]
        public void MigrationPlanReprojectsLegacySnapshotEvidenceWithoutRewritingTheSnapshot()
        {
            var package = CreateMigrationPackage();
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.ProjectLegacy(package.Snapshot);
            package.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.Plan.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.Plan.IngredientActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                package.Plan.IngredientGraph);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Plan.IngredientGraph,
                package.Plan.IngredientActions);
            package.Plan.MigrationOutcome = evaluation.Outcome;
            package.Plan.IngredientIssues = evaluation.Issues;
            package.Plan.ExecutionFrontier = evaluation.ExecutionFrontier;
            package.State = PublishingPagePackageStatePolicy.Derive(package.Plan);
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

            PublishingPagePackageValidator.ValidateMigration(package);

            Assert.IsNull(package.Snapshot.IngredientGraph.ProjectionVersion);
            Assert.AreEqual(
                PublishingPageIngredientGraphProjector.CurrentProjectionVersion,
                package.Plan.IngredientGraph.ProjectionVersion);
        }

        [TestMethod]
        public void ExportValidationDetectsPageLayoutByteMutation()
        {
            var snapshot = CreateSnapshot();
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            export.Snapshot.Layout.ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed"));

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateExport(export));
        }

        [TestMethod]
        public void PackageValidationDetectsWorkflowSelectionMutation()
        {
            var package = CreateMigrationPackage();
            package.Selection.ValidationCohort.Disposition = ValidationCohortDisposition.Excluded;

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void ImportPolicyRecomputesCohortAssessmentFromSourceEvidence()
        {
            var package = CreateMigrationPackage();
            package.Selection.ValidationCohort.Reasons = new List<string> { "tampered but re-digested reason" };
            package.SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(package.Selection);

            PublishingPagePackageValidator.ValidateMigration(package);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageImportPlanValidator.Validate(
                    package,
                    EnterpriseWikiV1WorkflowPolicy.Instance,
                    PublishingPageExecutionScope.Create(package)));
        }

        [TestMethod]
        public void PublishingRuntimeCanImportOutsideValidationCohort()
        {
            var package = CreateMigrationPackage();
            package.Snapshot.Source.ContentTypeId = BuiltInContentTypeId.ProjectPage + "001122";
            package.Selection = EnterpriseWikiV1WorkflowPolicy.Instance.Select(package.Snapshot.Source.ContentTypeId);
            package.SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(package.Selection);

            PublishingPageImportPlanValidator.Validate(
                package,
                EnterpriseWikiV1WorkflowPolicy.Instance,
                PublishingPageExecutionScope.Create(package));
        }

        [TestMethod]
        public void MigrationValidationDetectsPlanMutation()
        {
            var package = CreateMigrationPackage();

            PublishingPagePackageValidator.ValidateMigration(package);
            package.Plan.TargetPageServerRelativeUrl = "/sites/target/Pages/changed.aspx";

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void MigrationValidationRecomputesIngredientOutcomeAndDependencyIssues()
        {
            var package = CreateMigrationPackage();
            var fieldAction = package.Plan.IngredientActions.Single(value => value.IngredientId == "field:OOCLReference");
            fieldAction.Disposition = IngredientDisposition.Preserve;
            fieldAction.Capability = IngredientCapability.Available;
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void LifecycleRuleMapsRealR11DraftAndE05PublishedEvidence()
        {
            var r11 = new PageLifecycleSnapshot
            {
                CheckOutType = "Online",
                Level = "Draft",
                ModerationStatus = 3
            };
            var e05 = new PageLifecycleSnapshot
            {
                CheckOutType = "None",
                Level = "Published",
                ModerationStatus = 0
            };

            Assert.AreEqual(PublishingPageTargetLifecycle.Draft, PublishingPageLifecyclePolicy.DeriveTargetLifecycle(r11));
            Assert.AreEqual(PublishingPageTargetLifecycle.Published, PublishingPageLifecyclePolicy.DeriveTargetLifecycle(e05));
            Assert.AreEqual(PublishingPageTargetLifecycle.Draft, PublishingPageLifecyclePolicy.DeriveTargetLifecycle(new PageLifecycleSnapshot
            {
                CheckOutType = "Online",
                Level = "Published",
                ModerationStatus = 0
            }));
            Assert.AreEqual(PublishingPageTargetLifecycle.Draft, PublishingPageLifecyclePolicy.DeriveTargetLifecycle(null));
        }

        [TestMethod]
        public void ReportIncludesEveryCapturedFieldAndItsPlanDisposition()
        {
            var package = CreateMigrationPackage();

            var report = PublishingPageMigrationReportBuilder.Build(package);

            StringAssert.Contains(report, "OOCLReference");
            StringAssert.Contains(report, "Custom recovery field");
            StringAssert.Contains(report, "EvidenceOnly");
            StringAssert.Contains(report, "rawValueJson");
            StringAssert.Contains(report, "Only an unconflicted Published");
            StringAssert.Contains(report, "selection.validationCohort.disposition");
            StringAssert.Contains(report, "CLR runtime resolution");
            StringAssert.Contains(report, "Canonical ingredient nodes");
            StringAssert.Contains(report, "Ingredient actions");
            StringAssert.Contains(report, "snapshot.layout.customizedPageStatus");
            StringAssert.Contains(report, "Page Layout materialization plan");
            StringAssert.Contains(report, "Page Layout target admission");
            StringAssert.Contains(report, "Site collection and Web topology");
            StringAssert.Contains(report, "List dependency closure");
            StringAssert.Contains(report, "Web Part plan actions");
        }

        [TestMethod]
        public void WebPartPortabilityDelegatesListBindingsAndBlocksKnownUnsupportedTypes()
        {
            const string listView = @"<webParts><webPart xmlns=""http://schemas.microsoft.com/WebPart/v3""><metaData><type name=""Microsoft.SharePoint.WebPartPages.XsltListViewWebPart"" /></metaData><data><properties><property name=""ListId"">58a84d5d-b1ee-4da0-a49b-7e597ee8ae35</property></properties></data></webPart></webParts>";
            const string rss = @"<webParts><webPart xmlns=""http://schemas.microsoft.com/WebPart/v3""><metaData><type name=""Microsoft.SharePoint.Portal.WebControls.RSSAggregatorWebPart"" /></metaData></webPart></webParts>";
            const string scriptEditor = @"<webParts><webPart xmlns=""http://schemas.microsoft.com/WebPart/v3""><metaData><type name=""Microsoft.SharePoint.WebPartPages.ScriptEditorWebPart"" /></metaData></webPart></webParts>";
            const string contentEditorV2 = @"<WebPart xmlns=""http://schemas.microsoft.com/WebPart/v2""><Assembly>Microsoft.SharePoint.Core, Version=16.0.0.0</Assembly><TypeName>Microsoft.SharePoint.WebPartPages.ContentEditorWebPart</TypeName></WebPart>";
            const string contactV2 = @"<WebPart xmlns=""http://schemas.microsoft.com/WebPart/v2""><Assembly>Microsoft.SharePoint.Portal, Version=16.0.0.0</Assembly><TypeName>Microsoft.SharePoint.Portal.WebControls.ContactFieldControl</TypeName></WebPart>";

            Assert.IsNull(ClassicWebPartReplayCapabilityPolicy.GetBlocker(listView));
            StringAssert.Contains(ClassicWebPartReplayCapabilityPolicy.GetBlocker(rss), "not supported");
            Assert.IsNull(ClassicWebPartReplayCapabilityPolicy.GetBlocker(scriptEditor));
            Assert.IsNull(ClassicWebPartReplayCapabilityPolicy.GetBlocker(contentEditorV2));
            Assert.IsNull(ClassicWebPartReplayCapabilityPolicy.GetBlocker(contactV2));
        }

        [TestMethod]
        public void SerializerRoundTripsTheGenericPublishingPageContract()
        {
            var package = CreateMigrationPackage();

            var json = PublishingPagePackageSerializer.Serialize(package);
            var roundTripped = PublishingPagePackageSerializer.Deserialize<PublishingPageMigrationPackage>(json);

            PublishingPagePackageValidator.ValidateMigration(roundTripped);
            Assert.AreEqual(EnterpriseWikiV1CohortPolicy.CohortId, roundTripped.Selection.WorkflowId);
            Assert.AreEqual(PageRuntimeAdapterIds.Publishing, roundTripped.Snapshot.Runtime.AdapterId);
            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, roundTripped.Plan.MigrationOutcome);
            Assert.AreEqual("https://source.sharepoint.com/_catalogs/masterpage/EnterpriseWiki.aspx", roundTripped.Snapshot.Layout.Url);
            Assert.AreEqual(package.Snapshot.Layout.Bytes.Sha256, roundTripped.Snapshot.Layout.Bytes.Sha256);
            Assert.AreEqual("PublishingPageContent", roundTripped.Snapshot.Layout.Controls.Single().FieldName);
            Assert.AreEqual(package.PlanDigest, roundTripped.PlanDigest);
            Assert.AreEqual(1, roundTripped.Plan.RuntimeVerification.Requirements.Count);
            Assert.AreEqual(RuntimeVerificationRequirementKind.AuthoredDomEquality, roundTripped.Plan.RuntimeVerification.Requirements[0].Kind);
        }

        [TestMethod]
        public void RuntimeVerificationRequirementsAreSealedByThePlanDigest()
        {
            var package = CreateMigrationPackage();

            package.Plan.RuntimeVerification.Requirements[0].Description = "changed after approval";

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void ImportReturnsAZeroMutationReceiptWhenThePlanDigestWasNotApproved()
        {
            var package = CreateMigrationPackage();
            var journal = new InMemoryMigrationExecutionJournal();
            using (var context = new ClientContext(package.Plan.TargetWebUrl))
            {
                var receipt = new EnterpriseWikiMigrationImporter().Import(context, package, "not-approved", journal);

                Assert.AreEqual(MigrationExecutionStatus.NotStarted, receipt.ExecutionStatus);
                Assert.AreEqual("PlanDigestNotApproved", receipt.AdmissionFailure.Code);
                Assert.IsFalse(receipt.MutationStarted);
                Assert.AreEqual(StorageVerificationStatus.NotRun, receipt.StorageVerificationStatus);
                Assert.AreEqual(RuntimeVerificationStatus.NotRun, receipt.RuntimeVerificationStatus);
                Assert.AreEqual(MigrationAcceptanceStatus.Rejected, receipt.AcceptanceStatus);
                Assert.AreEqual(0, journal.Intents.Count);
                Assert.AreEqual(1, journal.ExecutionStates.Count);
            }
        }

        [TestMethod]
        public void LayoutMarkupParserCapturesFieldsZonesRegistrationsAndEncodedResources()
        {
            const string markup = @"<%@ Register TagPrefix=""PublishingWebControls"" Namespace=""Microsoft.SharePoint.Publishing.WebControls"" Assembly=""Microsoft.SharePoint.Publishing"" %>
<PublishingWebControls:RichHtmlField ID=""PageContent"" FieldName=""PublishingPageContent"" runat=""server"" />
<WebPartPages:WebPartZone ID=""Main"" runat=""server"" />
<SharePoint:CssRegistration Name=""<% $SPUrl:~sitecollection/Style Library/Contoso/site.css %>"" runat=""server"" />
&lt;script src=&quot;~site/SiteAssets/Contoso/app.js&quot;&gt;&lt;/script&gt;";

            var parsed = PublishingPageLayoutMarkupParser.Parse(markup);

            Assert.AreEqual(1, parsed.Registrations.Count);
            Assert.IsTrue(parsed.RequiredFieldIdentifiers.Contains("Title", StringComparer.OrdinalIgnoreCase));
            Assert.IsTrue(parsed.RequiredFieldIdentifiers.Contains("PublishingPageContent", StringComparer.OrdinalIgnoreCase));
            Assert.AreEqual("Main", parsed.Zones.Single().Id);
            Assert.IsTrue(parsed.ResourceReferences.Any(value => value.Value == "~sitecollection/Style Library/Contoso/site.css"));
            Assert.IsTrue(parsed.ResourceReferences.Any(value => value.Value == "~site/SiteAssets/Contoso/app.js"));
        }

        [TestMethod]
        public void ContentTypeFieldIdentifierMatcherAcceptsNamesAndGuidForms()
        {
            var fieldId = Guid.Parse("f55c4d88-1f2e-4ad9-aaa8-819af4ee7ee8");

            Assert.IsTrue(ContentTypeSchemaSnapshotReader.MatchesFieldIdentifier(
                new[] { "PublishingPageContent" }, fieldId, "PublishingPageContent"));
            Assert.IsTrue(ContentTypeSchemaSnapshotReader.MatchesFieldIdentifier(
                new[] { fieldId.ToString("D") }, fieldId, "PublishingPageContent"));
            Assert.IsTrue(ContentTypeSchemaSnapshotReader.MatchesFieldIdentifier(
                new[] { "{" + fieldId.ToString("D") + "}" }, fieldId, "PublishingPageContent"));
            Assert.IsFalse(ContentTypeSchemaSnapshotReader.MatchesFieldIdentifier(
                new[] { "Title" }, fieldId, "PublishingPageContent"));
        }

        [TestMethod]
        public void ContentTypeClosureReaderIsolatesOneMemberFailureAndContinuesItsSiblingAndParent()
        {
            const string failedId = "0x010100AA";
            const string siblingId = "0x010100BB";
            const string parentId = "0x010100CC";
            var observed = new List<string>();
            var diagnostics = new List<string>();

            var snapshots = ContentTypeClosureSnapshotReader.Read(
                new[] { failedId, siblingId },
                "https://source.sharepoint.com/sites/source",
                contentTypeId =>
                {
                    observed.Add(contentTypeId);
                    if (string.Equals(contentTypeId, failedId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("dynamic server detail must not be retained");
                    }
                    return new ContentTypeSchemaSnapshot
                    {
                        EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                        Availability = EvidenceAvailability.Captured,
                        SourceWebUrl = "https://source.sharepoint.com/sites/source",
                        ContentTypeId = contentTypeId,
                        Name = contentTypeId,
                        ParentContentTypeId = string.Equals(contentTypeId, siblingId, StringComparison.Ordinal)
                            ? parentId
                            : null
                    };
                },
                diagnostics);

            Assert.AreEqual(3, snapshots.Count);
            Assert.IsTrue(observed.Contains(siblingId));
            Assert.IsTrue(observed.Contains(parentId));
            var failed = snapshots.Single(value => value.ContentTypeId == failedId);
            Assert.AreEqual(ContentTypeSchemaEvidenceState.Partial, failed.EvidenceState);
            Assert.AreEqual(EvidenceAvailability.Partial, failed.Availability);
            Assert.AreEqual("/sites/source", failed.SourceScope);
            Assert.AreEqual(1, failed.Diagnostics.Count);
            StringAssert.Contains(failed.Diagnostics[0],
                "ContentTypeClosureMemberCapturePartial: contentTypeId=" + failedId);
            Assert.IsFalse(failed.Diagnostics[0].Contains("dynamic server detail"));
            Assert.IsTrue(diagnostics.Any(value => value.Contains(
                "ContentTypeClosureMemberCapturePartial: contentTypeId=" + failedId)));
        }

        [TestMethod]
        public void PartialSiteContentTypeClosureRemainsPackageableAndNonExecutable()
        {
            const string contentTypeId = "0x010100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            var partial = ContentTypeClosureSnapshotReader.Read(
                new[] { contentTypeId },
                "https://source.sharepoint.com/sites/source",
                _ => throw new InvalidOperationException("member"),
                new List<string>()).Single();
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var source = CreateListSnapshot(
                siteId,
                webId,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Items");
            source.SiteContentTypes.Add(partial);

            ListDependencyPackageValidator.Validate(
                Array.Empty<ClassicWebPartSnapshot>(),
                Array.Empty<ClassicListWebPartBindingSnapshot>(),
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                null,
                null);
            var listPlan = ListMigrationPlanFactory.Create(
                new[] { source },
                null,
                CreateTopology(siteId, webId),
                null,
                null).Lists.Single();

            Assert.AreEqual(ListMaterializationDisposition.Block, listPlan.Disposition);
            Assert.IsTrue(listPlan.Issues.Any(value =>
                value.Code == "ContentTypeSchemaUnavailable"
                && value.Subject.Contains(contentTypeId)));
        }

        [TestMethod]
        public void ContentTypeClosureReaderDoesNotSwallowAnUnclassifiedNetworkFailure()
        {
            Assert.ThrowsException<WebException>(() => ContentTypeClosureSnapshotReader.Read(
                new[] { "0x010100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" },
                "https://source.sharepoint.com/sites/source",
                _ => throw new WebException("no literal response status"),
                new List<string>()));
        }

        [TestMethod]
        public void ListMemberFallbackDoesNotSwallowWebTransportFailures()
        {
            Assert.IsFalse(ListDependencySnapshotReader.IsRecoverableMemberCaptureFailure(
                new WebException("no literal response status")));
            Assert.IsTrue(ListDependencySnapshotReader.IsRecoverableMemberCaptureFailure(
                new InvalidOperationException("member")));
        }

        [TestMethod]
        public void PartialListContentTypeIsRetainedButOnlyItsIngredientIsDeferred()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            const string contentTypeId = "0x010100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            var source = CreateListSnapshot(siteId, webId, listId, "Items");
            source.ContentTypes.Add(new ListContentTypeSnapshot
            {
                Id = contentTypeId,
                Name = "Partially captured member",
                FieldLinks = new List<ListContentTypeFieldLinkSnapshot>(),
                Availability = EvidenceAvailability.Partial
            });
            source.Diagnostics.Add(
                "ListContentTypeMetadataPartial: contentTypeId=" + contentTypeId
                + "; exceptionType=Microsoft.SharePoint.Client.ServerException.");

            ListDependencyPackageValidator.Validate(
                Array.Empty<ClassicWebPartSnapshot>(),
                Array.Empty<ClassicListWebPartBindingSnapshot>(),
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                null,
                null);
            var topology = CreateTopology(siteId, webId);
            var listPlan = ListMigrationPlanFactory.Create(
                new[] { source },
                null,
                topology,
                null,
                null);
            var plan = listPlan.Lists.Single();
            Assert.AreEqual(ListMaterializationDisposition.Block, plan.Disposition);
            Assert.IsTrue(plan.Issues.Any(value => value.Code == "ListContentTypeEvidenceUnavailable"));

            var package = CreateMigrationPackage();
            package.Snapshot.ListDependencies = new List<ListDependencySnapshot> { source };
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.Plan.ListMigration = listPlan;
            var actions = PublishingPageIngredientActionProjector.Project(package.Snapshot, package.Plan)
                .ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            Assert.AreEqual(
                IngredientDisposition.Preserve,
                actions[PublishingPageIngredientIds.List(webId, listId)].Disposition);
            Assert.AreEqual(
                IngredientDisposition.Defer,
                actions[PublishingPageIngredientIds.ListContentType(webId, listId, contentTypeId)].Disposition);

            using (var context = new ClientContext("https://target.sharepoint.com/sites/target"))
            {
                var targetList = context.Web.Lists.GetByTitle("Items");
                Assert.ThrowsException<InvalidDataException>(() =>
                    ListContentTypeMaterializer.EnsureMembership(context, targetList, source));
            }
        }

        [TestMethod]
        public void PartialListViewIsRetainedButOnlyItsIngredientIsDeferred()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var viewId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            const string viewXml = "<View />";
            var source = CreateListSnapshot(siteId, webId, listId, "Items");
            source.Views.Add(new ListViewSnapshot
            {
                Id = viewId,
                Title = "Partial view",
                ViewType = "Html",
                ViewFields = new List<string>(),
                ListViewXml = viewXml,
                ListViewXmlSha256 = MigrationDigest.ComputeSha256(viewXml),
                Availability = EvidenceAvailability.Partial,
                Diagnostics = new List<string>
                {
                    "ListViewMetadataPartial: exceptionType=Microsoft.SharePoint.Client.ServerException."
                }
            });

            ListDependencyPackageValidator.Validate(
                Array.Empty<ClassicWebPartSnapshot>(),
                Array.Empty<ClassicListWebPartBindingSnapshot>(),
                new[] { source },
                Array.Empty<ListLookupDependency>(),
                null,
                null);
            var topology = CreateTopology(siteId, webId);
            var listPlan = ListMigrationPlanFactory.Create(
                new[] { source },
                null,
                topology,
                null,
                null);
            var plan = listPlan.Lists.Single();
            var viewPlan = plan.Views.Single();
            Assert.AreEqual(ListViewMaterializationDisposition.Block, viewPlan.Disposition);
            Assert.IsTrue(plan.Issues.Any(value => value.Code == "ViewEvidenceUnavailable"));

            var package = CreateMigrationPackage();
            package.Snapshot.ListDependencies = new List<ListDependencySnapshot> { source };
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.Plan.ListMigration = listPlan;
            var actions = PublishingPageIngredientActionProjector.Project(package.Snapshot, package.Plan)
                .ToDictionary(value => value.IngredientId, StringComparer.Ordinal);
            Assert.AreEqual(
                IngredientDisposition.Preserve,
                actions[PublishingPageIngredientIds.List(webId, listId)].Disposition);
            Assert.AreEqual(
                IngredientDisposition.Defer,
                actions[PublishingPageIngredientIds.View(webId, listId, viewId)].Disposition);

            viewPlan.Disposition = ListViewMaterializationDisposition.CreateOrReuseOwnedPublicView;
            using (var context = new ClientContext("https://target.sharepoint.com/sites/target"))
            {
                var targetList = context.Web.Lists.GetByTitle("Items");
                Assert.ThrowsException<InvalidDataException>(() =>
                    ListViewMaterializer.Ensure(context, targetList, plan));
            }
        }

        [TestMethod]
        public void CapturedListContentTypeKeepsLegacyCanonicalJsonUntilPartialEvidenceExists()
        {
            var snapshot = new ListContentTypeSnapshot
            {
                Id = "0x010100AA",
                Name = "Captured",
                ParentId = "0x0101"
            };

            var captured = MigrationContractSerializer.SerializeCanonical(snapshot);
            Assert.IsFalse(captured.Contains("\"availability\""));

            snapshot.Availability = EvidenceAvailability.Partial;
            var partial = MigrationContractSerializer.SerializeCanonical(snapshot);
            StringAssert.Contains(partial, "\"availability\":\"Partial\"");
        }

        [TestMethod]
        public void FieldSchemaCanonicalizerIgnoresStorageSlotsAndRebindsTaxonomy()
        {
            const string left = @"<Field ID=""{11111111-1111-1111-1111-111111111111}"" Name=""Category"" Type=""TaxonomyFieldType"" SourceID=""source-a"" ColName=""nvarchar1"" RowOrdinal=""1""><Customization><ArrayOfProperty><Property><Name>SspId</Name><Value>{22222222-2222-2222-2222-222222222222}</Value></Property><Property><Name>TermSetId</Name><Value>33333333-3333-3333-3333-333333333333</Value></Property><Property><Name>TextField</Name><Value>44444444-4444-4444-4444-444444444444</Value></Property></ArrayOfProperty></Customization></Field>";
            const string right = @"<Field RowOrdinal=""99"" ColName=""nvarchar42"" SourceID=""source-b"" Type=""TaxonomyFieldType"" Name=""Category"" ID=""{11111111-1111-1111-1111-111111111111}""><Customization><ArrayOfProperty><Property><Name>SspId</Name><Value>22222222-2222-2222-2222-222222222222</Value></Property><Property><Name>TermSetId</Name><Value>{33333333-3333-3333-3333-333333333333}</Value></Property><Property><Name>TextField</Name><Value>{44444444-4444-4444-4444-444444444444}</Value></Property></ArrayOfProperty></Customization></Field>";

            Assert.AreEqual(FieldSchemaCanonicalizer.PortableDigest(left), FieldSchemaCanonicalizer.PortableDigest(right));

            var rewritten = FieldSchemaCanonicalizer.RewriteForTarget(
                left,
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

            Assert.IsFalse(rewritten.Contains("ColName"));
            Assert.IsFalse(rewritten.Contains("RowOrdinal"));
            StringAssert.Contains(rewritten, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            StringAssert.Contains(rewritten, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            StringAssert.Contains(rewritten, "cccccccc-cccc-cccc-cccc-cccccccccccc");
        }

        [TestMethod]
        public void ContentTypeSchemaPlannerCreatesScalarClosureAndBlocksUnmappedTaxonomy()
        {
            var scalarId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var taxonomyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var schema = new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                Availability = EvidenceAvailability.Captured,
                SourceWebUrl = "https://source.sharepoint.com/sites/source",
                ContentTypeId = "0x010100AA",
                Name = "Custom Page",
                ParentContentTypeId = "0x01010007FF3E057FA8AB4AA42FCB67B453FFC",
                ParentContentTypeName = "Page",
                RequiredFieldLinks = new List<ContentTypeFieldLinkSnapshot>
                {
                    new ContentTypeFieldLinkSnapshot { FieldId = scalarId, Name = "Activity", Role = FieldSchemaRole.DirectBinding },
                    new ContentTypeFieldLinkSnapshot { FieldId = taxonomyId, Name = "Category", Role = FieldSchemaRole.DirectBinding }
                },
                RequiredFieldClosure = new List<FieldSchemaSnapshot>
                {
                    Field(scalarId, "Activity", "Text", "<Field ID=\"{11111111-1111-1111-1111-111111111111}\" Name=\"Activity\" Type=\"Text\" ColName=\"nvarchar1\" />"),
                    new FieldSchemaSnapshot
                    {
                        Id = taxonomyId,
                        InternalName = "Category",
                        Title = "Category",
                        TypeAsString = "TaxonomyFieldType",
                        SchemaXml = "<Field ID=\"{22222222-2222-2222-2222-222222222222}\" Name=\"Category\" Type=\"TaxonomyFieldType\" />",
                        PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest("<Field ID=\"{22222222-2222-2222-2222-222222222222}\" Name=\"Category\" Type=\"TaxonomyFieldType\" />"),
                        Role = FieldSchemaRole.DirectBinding,
                        Taxonomy = new TaxonomyFieldBindingSnapshot
                        {
                            SourceTermStoreId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                            SourceTermSetId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                            HiddenTextFieldId = Guid.Parse("55555555-5555-5555-5555-555555555555")
                        }
                    }
                }
            };

            var plan = ContentTypeSchemaPlanner.CreateRequiredClosure(schema);

            Assert.AreEqual(ContentTypeMaterializationDisposition.Block, plan.Disposition);
            Assert.AreEqual(FieldSchemaMaterializationDisposition.CreateOrReuseOwned,
                plan.Fields.Single(value => value.FieldId == scalarId).Disposition);
            Assert.AreEqual(FieldSchemaMaterializationDisposition.Block,
                plan.Fields.Single(value => value.FieldId == taxonomyId).Disposition);
            Assert.IsFalse(plan.Fields.Single(value => value.FieldId == scalarId).TargetSchemaXml.Contains("ColName"));
        }

        [TestMethod]
        public void CustomLayoutPlanMapsExactSchemaResourcesAndDeterministicTargetBytes()
        {
            var layout = CreateCustomLayout();

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                "EnterpriseWiki.aspx");

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.CreateOwned, plan.Disposition);
            StringAssert.StartsWith(plan.TargetFileName, "pnp-custom-");
            StringAssert.EndsWith(plan.TargetFileName, ".aspx");
            Assert.AreEqual("/sites/target/_catalogs/masterpage/" + plan.TargetFileName, plan.TargetServerRelativeUrl);
            Assert.AreEqual(ContentTypeMaterializationDisposition.CreateOwned, plan.ContentTypeSchema.Disposition);
            Assert.AreEqual(FieldSchemaMaterializationDisposition.CreateOrReuseOwned, plan.ContentTypeSchema.Fields.Single().Disposition);
            Assert.AreEqual("/sites/target/SiteAssets/Contoso/app.js", plan.ResourceMaterializations.Single().TargetServerRelativeUrl);
            Assert.AreEqual("https://target.sharepoint.com/sites/target/SiteAssets/Contoso/app.js", plan.ResourceRewrites.Single().TargetReference);
            Assert.AreNotEqual(plan.SourceBytes.Sha256, plan.TargetBytes.Sha256);
        }

        [TestMethod]
        public void CustomLayoutPreservesUnreadableAbsoluteResourceWhenExternalReferencesAreAllowed()
        {
            const string authoredReference = "https://source.sharepoint.com/sites/source/SiteAssets/Contoso/app.js";
            var layout = CreateCustomLayout();
            var layoutBytes = Encoding.UTF8.GetBytes(
                "<%@ Page %><PublishingWebControls:TextField FieldName=\"Activity\" runat=\"server\" /><script src=\""
                + authoredReference
                + "\"></script>");
            layout.Bytes = MigrationArtifact.Describe(layoutBytes, "application/vnd.ms-aspx", "Custom.aspx");
            layout.ContentBase64 = Convert.ToBase64String(layoutBytes);
            layout.ResourceReferences.Single().Value = authoredReference;
            var resource = layout.ResourceArtifacts.Single();
            resource.Reference = layout.ResourceReferences.Single();
            resource.EvidenceState = PublishingPageLayoutResourceEvidenceState.AccessDenied;
            resource.ResolvedSourceUrl = authoredReference;
            resource.Artifact = null;
            resource.ContentBase64 = null;
            resource.Diagnostics = new List<string> { "Access denied." };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                "EnterpriseWiki.aspx");

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.CreateOwned, plan.Disposition);
            Assert.AreEqual(
                PublishingPageLayoutResourceMaterializationDisposition.PreserveExternal,
                plan.ResourceMaterializations.Single().Disposition);
            Assert.AreEqual(authoredReference, plan.ResourceMaterializations.Single().TargetReference);
            Assert.AreEqual(0, plan.ResourceRewrites.Count);
            Assert.AreEqual(plan.SourceBytes.Sha256, plan.TargetBytes.Sha256);
        }

        [TestMethod]
        public void CustomLayoutBlocksUnreadableAbsoluteResourceWhenExternalReferencesAreDisabled()
        {
            const string authoredReference = "https://source.sharepoint.com/sites/source/SiteAssets/Contoso/app.js";
            var layout = CreateCustomLayout();
            layout.ResourceReferences.Single().Value = authoredReference;
            var resource = layout.ResourceArtifacts.Single();
            resource.Reference = layout.ResourceReferences.Single();
            resource.EvidenceState = PublishingPageLayoutResourceEvidenceState.AccessDenied;
            resource.ResolvedSourceUrl = authoredReference;
            resource.Artifact = null;
            resource.ContentBase64 = null;
            resource.Diagnostics = new List<string> { "Access denied." };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                "EnterpriseWiki.aspx",
                allowExternalResourceReferences: false);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.Block, plan.Disposition);
            Assert.AreEqual(
                PublishingPageLayoutResourceMaterializationDisposition.Block,
                plan.ResourceMaterializations.Single().Disposition);
        }

        [DataTestMethod]
        [DataRow("EnterpriseWiki.aspx", "Basic Page", "Enterprise Wiki Page", BuiltInContentTypeId.EnterpriseWikiPage)]
        [DataRow("BlankWebPartPage.aspx", "Blank Web Part page", "Welcome Page", BuiltInContentTypeId.WelcomePage)]
        public void UnavailableNativeLayoutRequiresReviewedTargetRuntimeStock(
            string fileName,
            string title,
            string contentTypeName,
            string contentTypeId)
        {
            var layout = new PublishingPageLayoutSnapshot
            {
                EvidenceState = PublishingPageLayoutEvidenceState.AccessDenied,
                Availability = EvidenceAvailability.Unavailable,
                Url = "https://source.sharepoint.com/sites/source/_catalogs/masterpage/" + fileName,
                ServerRelativeUrl = "/sites/source/_catalogs/masterpage/" + fileName,
                Description = title,
                Diagnostics = new List<string> { "Access is denied." }
            };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                string.Equals(contentTypeId, BuiltInContentTypeId.EnterpriseWikiPage, StringComparison.OrdinalIgnoreCase)
                    ? EnterpriseWikiV1WorkflowPolicy.Instance
                    : WelcomePageV1WorkflowPolicy.Instance);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.ReuseTargetStock, plan.Disposition);
            Assert.AreEqual(fileName, plan.TargetFileName);
            Assert.AreEqual(contentTypeName, plan.AssociatedContentTypeName);
            Assert.AreEqual(contentTypeId, plan.AssociatedContentTypeId);
            Assert.IsNull(plan.TargetBytes);
        }

        [TestMethod]
        public void UnavailableCustomLayoutDoesNotImpersonateNativeStock()
        {
            var layout = new PublishingPageLayoutSnapshot
            {
                EvidenceState = PublishingPageLayoutEvidenceState.Failed,
                Availability = EvidenceAvailability.Unavailable,
                Url = "https://source.sharepoint.com/sites/source/_catalogs/masterpage/Custom.aspx",
                ServerRelativeUrl = "/sites/source/_catalogs/masterpage/Custom.aspx",
                Description = "Custom Page Layout",
                Diagnostics = new List<string> { "Source evidence unavailable." }
            };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                "EnterpriseWiki.aspx");

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.Block, plan.Disposition);
        }

        [TestMethod]
        public void CustomLayoutAdmissionCreatesOnlyWhenSchemaResourcesAndTargetAreEligible()
        {
            var layout = CreateCustomLayout();
            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                "EnterpriseWiki.aspx");
            var probe = CreateEligibleCustomLayoutProbe(plan);

            var admission = PublishingPageLayoutTargetAdmissionEvaluator.Evaluate(plan, probe);

            Assert.IsTrue(admission.IsEligible);
            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.CreateOwned, admission.Disposition);
            Assert.AreEqual(ContentTypeMaterializationDisposition.CreateOwned, admission.ContentTypeSchema.Disposition);

            probe.Resources.Single().FileExists = true;
            probe.Resources.Single().ExistingBytesSha256 = new string('0', 64);
            var collision = PublishingPageLayoutTargetAdmissionEvaluator.Evaluate(plan, probe);
            Assert.IsFalse(collision.IsEligible);
            Assert.IsTrue(collision.Issues.Any(value => value.Code == "TargetLayoutResourceCollision"));
        }

        [TestMethod]
        public void ExportValidationRequiresOneEvidenceRecordPerLayoutResourceReference()
        {
            var snapshot = CreateSnapshot();
            snapshot.Layout = CreateCustomLayout();
            snapshot.Layout.ResourceArtifacts.Clear();
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot)
            };

            Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateExport(export));
        }

        [TestMethod]
        public void DirectoryArtifactStoreRoundTripsAndDeduplicatesByDigest()
        {
            var root = Path.Combine(Path.GetTempPath(), "pnp-migration-artifacts-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new DirectoryMigrationArtifactStore(root);
                var bytes = Encoding.UTF8.GetBytes("exact migration payload");
                ArtifactReference first;
                ArtifactReference second;
                using (var content = new MemoryStream(bytes, false))
                {
                    first = store.Put(content, "text/plain", "payload.txt");
                }

                using (var content = new MemoryStream(bytes, false))
                {
                    second = store.Put(content, "text/plain", "another-name.txt");
                }

                Assert.AreEqual(first.Sha256, second.Sha256);
                Assert.AreEqual(bytes.LongLength, first.Length);
                Assert.IsTrue(store.Contains(first.Sha256));
                using (var content = store.OpenRead(first.Sha256))
                using (var buffer = new MemoryStream())
                {
                    content.CopyTo(buffer);
                    CollectionAssert.AreEqual(bytes, buffer.ToArray());
                }
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [TestMethod]
        public void ListWebPartBindingRewritesListWebViewAndPageIdentities()
        {
            var sourceWeb = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var sourceList = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var sourceView = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var targetWeb = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var targetList = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var targetView = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            var exportXml = "<webParts><webPart xmlns=\"http://schemas.microsoft.com/WebPart/v3\"><metaData><type name=\"Microsoft.SharePoint.WebPartPages.XsltListViewWebPart\" /></metaData><data><properties>"
                + "<property name=\"ListId\">" + sourceList.ToString("D") + "</property>"
                + "<property name=\"ListName\">{" + sourceList.ToString("D") + "}</property>"
                + "<property name=\"WebId\">00000000-0000-0000-0000-000000000000</property>"
                + "<property name=\"ViewGuid\">" + sourceView.ToString("D") + "</property>"
                + "<property name=\"TitleUrl\">/teams/source/child/Lists/Resources</property>"
                + "<property name=\"XmlDefinition\">&lt;View Name=\"{" + sourceView.ToString("D") + "}\" Url=\"/teams/source/child/Pages/A.aspx\"&gt;&lt;JSLink&gt;clienttemplates.js&lt;/JSLink&gt;&lt;/View&gt;</property>"
                + "</properties></data></webPart></webParts>";
            var snapshot = new ClassicWebPartSnapshot
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Title = "Resources",
                TypeName = "Microsoft.SharePoint.WebPartPages.XsltListViewWebPart",
                ExportXml = exportXml,
                ExportSha256 = MigrationDigest.ComputeSha256(exportXml)
            };

            var parsed = ClassicListWebPartBindingParser.Parse(
                snapshot,
                sourceWeb,
                "https://source.sharepoint.com/teams/source/child",
                "/teams/source/child/Pages/A.aspx");

            Assert.IsTrue(parsed.IsExecutable, string.Join(Environment.NewLine, parsed.Issues.Select(value => value.Message)));
            Assert.AreEqual(sourceWeb, parsed.Binding.SourceListWebId);
            Assert.AreEqual(sourceView, parsed.Binding.SourceViewId);
            Assert.AreEqual("clienttemplates.js", parsed.Binding.JsLink);

            var rewritten = ClassicListWebPartRewriter.Rewrite(parsed.Binding, new ClassicListWebPartTargetMap
            {
                SourceWebId = sourceWeb,
                SourceListId = sourceList,
                SourceViewId = sourceView,
                TargetWebId = targetWeb,
                TargetListId = targetList,
                TargetViewId = targetView,
                TargetListServerRelativeUrl = "/sites/target/child/Lists/Resources",
                TargetListAbsoluteUrl = "https://target.sharepoint.com/sites/target/child/Lists/Resources",
                TargetPageServerRelativeUrl = "/sites/target/child/Pages/A.aspx"
            });
            var properties = XDocument.Parse(rewritten.ExportXml).Descendants()
                .Where(value => value.Name.LocalName == "property")
                .ToDictionary(value => (string)value.Attribute("name"), value => value.Value, StringComparer.OrdinalIgnoreCase);

            Assert.AreEqual(targetWeb.ToString("D"), properties["WebId"]);
            Assert.AreEqual(targetList.ToString("D"), properties["ListId"]);
            Assert.AreEqual("{" + targetList.ToString("D") + "}", properties["ListName"]);
            Assert.AreEqual(targetView.ToString("D"), properties["ViewGuid"]);
            var view = XDocument.Parse(properties["XmlDefinition"]).Root;
            Assert.AreEqual("{" + targetView.ToString("D") + "}", (string)view.Attribute("Name"));
            Assert.AreEqual("/sites/target/child/Pages/A.aspx", (string)view.Attribute("Url"));
        }

        [TestMethod]
        public void LookupDependencyGraphOrdersLookupListsAndBlocksCycles()
        {
            var owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var lookup = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var ordered = ListLookupDependencyGraph.Order(
                new[] { owner, lookup },
                new[]
                {
                    new ListLookupDependency
                    {
                        SourceListId = owner,
                        LookupListId = lookup,
                        FieldId = Guid.NewGuid(),
                        FieldInternalName = "Lookup"
                    }
                });

            Assert.IsTrue(ordered.IsExecutable);
            CollectionAssert.AreEqual(new[] { lookup, owner }, ordered.OrderedSourceListIds.ToArray());

            var cycle = ListLookupDependencyGraph.Order(
                new[] { owner, lookup },
                new[]
                {
                    new ListLookupDependency { SourceListId = owner, LookupListId = lookup },
                    new ListLookupDependency { SourceListId = lookup, LookupListId = owner }
                });
            Assert.IsFalse(cycle.IsExecutable);
            Assert.IsTrue(cycle.Issues.Any(value => value.Code == "LookupDependencyCycle"));
        }

        [TestMethod]
        public void TopologyPlannerPreservesNestedWebOwnershipAndStableDigest()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var rootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var childId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var source = new SourceSiteCollectionSnapshot
            {
                SiteId = siteId,
                SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                ServerRelativeUrl = "/teams/source",
                RootWebId = rootId,
                Webs = new List<SourceWebSnapshot>
                {
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = childId,
                        ParentWebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                        WebUrl = "https://source.sharepoint.com/teams/source/child",
                        ServerRelativeUrl = "/teams/source/child",
                        Title = "Child",
                        WebTemplate = "CMSPUBLISHING"
                    },
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                        WebUrl = "https://source.sharepoint.com/teams/source",
                        ServerRelativeUrl = "/teams/source",
                        Title = "Root",
                        WebTemplate = "CMSPUBLISHING"
                    }
                }
            };
            var target = new TargetSiteCollectionSpec
            {
                SourceSiteId = siteId,
                Mode = TargetSiteMode.ExistingTargetSite,
                TargetSiteUrl = "https://target.sharepoint.com/sites/target",
                ExpectedTargetSiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Target"
            };
            var policy = new TopologyPlanningPolicy
            {
                WebOverrides = new List<TargetWebOverride>
                {
                    new TargetWebOverride { SourceWebId = childId, TargetUrlSegment = "area", TargetTitle = "Area" }
                }
            };

            var first = new TopologyPlanner().Build(new[] { source }, new[] { target }, policy);
            var second = new TopologyPlanner().Build(new[] { source }, new[] { target }, policy);

            Assert.IsTrue(first.IsExecutable, string.Join(Environment.NewLine, first.Issues.Select(value => value.Message)));
            var child = first.Plan.SiteCollections.Single().Webs.Single(value => value.SourceWebId == childId);
            Assert.AreEqual("/sites/target/area", child.TargetServerRelativeUrl);
            Assert.AreEqual("/sites/target/area/Lists/Resources", TopologyPlanner.MapWebOwnedServerRelativePath(
                "/teams/source/child/Lists/Resources",
                "/teams/source/child",
                child.TargetServerRelativeUrl));
            Assert.AreEqual(first.Plan.PlanDigest, second.Plan.PlanDigest);
            Assert.AreEqual(
                TopologyPlanner.ComputeSiteMappingDigest(first.Plan.SiteCollections.Single()),
                TopologyPlanner.ComputeSiteMappingDigest(second.Plan.SiteCollections.Single()));
            var boundSite = first.Plan.SiteCollections.Single();
            var createSiteDigest = TopologyPlanner.ComputeSiteMappingDigest(boundSite);
            boundSite.TargetMode = TargetSiteMode.ExistingTargetSite;
            boundSite.ExpectedTargetSiteId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            Assert.AreEqual(createSiteDigest, TopologyPlanner.ComputeSiteMappingDigest(boundSite));
        }

        [TestMethod]
        public void TopologyPlannerPreservesSourceWebUrlSegmentByDefault()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var rootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var childId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var source = new SourceSiteCollectionSnapshot
            {
                SiteId = siteId,
                SiteCollectionUrl = "https://source.sharepoint.com/teams/Source_Site",
                ServerRelativeUrl = "/teams/Source_Site",
                RootWebId = rootId,
                Webs = new List<SourceWebSnapshot>
                {
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/Source_Site",
                        WebUrl = "https://source.sharepoint.com/teams/Source_Site",
                        ServerRelativeUrl = "/teams/Source_Site",
                        Title = "Root",
                        WebTemplate = "CMSPUBLISHING"
                    },
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = childId,
                        ParentWebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/Source_Site",
                        WebUrl = "https://source.sharepoint.com/teams/Source_Site/Child_Web",
                        ServerRelativeUrl = "/teams/Source_Site/Child_Web",
                        Title = "Child",
                        WebTemplate = "CMSPUBLISHING"
                    }
                }
            };
            var target = new TargetSiteCollectionSpec
            {
                SourceSiteId = siteId,
                Mode = TargetSiteMode.ExistingTargetSite,
                TargetSiteUrl = "https://target.sharepoint.com/teams/Source_Site-pnp",
                ExpectedTargetSiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Target"
            };

            var result = new TopologyPlanner().Build(new[] { source }, new[] { target });

            Assert.IsTrue(result.IsExecutable, string.Join(Environment.NewLine, result.Issues.Select(value => value.Message)));
            var child = result.Plan.SiteCollections.Single().Webs.Single(value => value.SourceWebId == childId);
            Assert.AreEqual("/teams/Source_Site-pnp/Child_Web", child.TargetServerRelativeUrl);
            Assert.AreEqual("CMSPUBLISHING", child.TargetTemplate);
            Assert.AreEqual(0, child.TargetConfiguration);
            Assert.AreEqual(
                "/teams/Source_Site-pnp/Child_Web/Pages/Folder/Page.aspx",
                TopologyPlanner.MapWebOwnedServerRelativePath(
                    "/teams/Source_Site/Child_Web/Pages/Folder/Page.aspx",
                    "/teams/Source_Site/Child_Web",
                    child.TargetServerRelativeUrl));
        }

        [TestMethod]
        public void TopologyPlannerRejectsDirectChildWhenIntermediateParentClosureIsMissing()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var rootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var childId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var source = new SourceSiteCollectionSnapshot
            {
                SiteId = siteId,
                SiteCollectionUrl = "https://source.sharepoint.com/teams/athena",
                ServerRelativeUrl = "/teams/athena",
                RootWebId = rootId,
                Webs = new List<SourceWebSnapshot>
                {
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/athena",
                        WebUrl = "https://source.sharepoint.com/teams/athena",
                        ServerRelativeUrl = "/teams/athena",
                        Title = "Root",
                        WebTemplate = "CMSPUBLISHING"
                    },
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = childId,
                        ParentWebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/athena",
                        WebUrl = "https://source.sharepoint.com/teams/athena/gkb/projects/AthenaWiki",
                        ServerRelativeUrl = "/teams/athena/gkb/projects/AthenaWiki",
                        Title = "Athena Wiki",
                        WebTemplate = "CMSPUBLISHING"
                    }
                }
            };
            var target = new TargetSiteCollectionSpec
            {
                SourceSiteId = siteId,
                Mode = TargetSiteMode.ExistingTargetSite,
                TargetSiteUrl = "https://target.sharepoint.com/teams/athena-pnp",
                ExpectedTargetSiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Athena"
            };

            var result = new TopologyPlanner().Build(new[] { source }, new[] { target });

            Assert.IsFalse(result.IsExecutable);
            Assert.IsNull(result.Plan);
            Assert.IsTrue(result.Issues.Any(value => value.Code == "InvalidTargetWebPath"
                && value.Subject == "source-web:" + childId.ToString("D")));
        }

        [TestMethod]
        public void TopologyPlannerRejectsPartialSourceWebEvidence()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var rootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var childId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var source = new SourceSiteCollectionSnapshot
            {
                SiteId = siteId,
                SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                ServerRelativeUrl = "/teams/source",
                RootWebId = rootId,
                Webs = new List<SourceWebSnapshot>
                {
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                        WebUrl = "https://source.sharepoint.com/teams/source",
                        ServerRelativeUrl = "/teams/source",
                        Title = "Root",
                        WebTemplate = "CMSPUBLISHING"
                    },
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = childId,
                        ParentWebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                        WebUrl = "https://source.sharepoint.com/teams/source/child",
                        ServerRelativeUrl = "/teams/source/child",
                        Title = "Child",
                        WebTemplate = "CMSPUBLISHING",
                        Availability = EvidenceAvailability.Partial
                    }
                }
            };
            var target = new TargetSiteCollectionSpec
            {
                SourceSiteId = siteId,
                Mode = TargetSiteMode.ExistingTargetSite,
                TargetSiteUrl = "https://target.sharepoint.com/teams/target",
                ExpectedTargetSiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Target"
            };

            var result = new TopologyPlanner().Build(new[] { source }, new[] { target });

            Assert.IsFalse(result.IsExecutable);
            Assert.IsNull(result.Plan);
            Assert.IsTrue(result.Issues.Any(value => value.Code == "InvalidSourceWeb"
                && value.Subject == "source-web:" + childId.ToString("D")));
        }

        [TestMethod]
        public void TopologyPlanValidatorRejectsResealedMultiSegmentDirectChild()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var rootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var childId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var source = new SourceSiteCollectionSnapshot
            {
                SiteId = siteId,
                SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                ServerRelativeUrl = "/teams/source",
                RootWebId = rootId,
                Webs = new List<SourceWebSnapshot>
                {
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                        WebUrl = "https://source.sharepoint.com/teams/source",
                        ServerRelativeUrl = "/teams/source",
                        Title = "Root",
                        WebTemplate = "CMSPUBLISHING"
                    },
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = childId,
                        ParentWebId = rootId,
                        SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                        WebUrl = "https://source.sharepoint.com/teams/source/child",
                        ServerRelativeUrl = "/teams/source/child",
                        Title = "Child",
                        WebTemplate = "CMSPUBLISHING"
                    }
                }
            };
            var target = new TargetSiteCollectionSpec
            {
                SourceSiteId = siteId,
                Mode = TargetSiteMode.ExistingTargetSite,
                TargetSiteUrl = "https://target.sharepoint.com/teams/target",
                ExpectedTargetSiteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Target"
            };
            var plan = new TopologyPlanner().Build(new[] { source }, new[] { target }).Plan;
            var child = plan.SiteCollections.Single().Webs.Single(value => value.SourceWebId == childId);
            child.TargetWebUrl = "https://target.sharepoint.com/teams/target/missing/child";
            child.PreferredTargetWebUrl = child.TargetWebUrl;
            child.TargetServerRelativeUrl = "/teams/target/missing/child";
            child.PreferredTargetServerRelativeUrl = child.TargetServerRelativeUrl;
            plan.PlanDigest = TopologyPlanner.ComputeDigest(plan);

            Assert.ThrowsException<InvalidDataException>(() => TopologyPlanValidator.Validate(plan));
        }

        [TestMethod]
        public void TopologyTargetPathAllocatorOnlyAddsSuffixForCollision()
        {
            Assert.AreEqual(
                "Code.aspx",
                TopologyTargetPathAllocator.AllocateSegment(
                    "Code.aspx",
                    "urn:pnp:page:source-1",
                    new[] { "Other.aspx" },
                    preserveFileExtension: true));

            var allocated = TopologyTargetPathAllocator.AllocateSegment(
                "Code.aspx",
                "urn:pnp:page:source-1",
                new[] { "code.aspx" },
                preserveFileExtension: true);

            StringAssert.StartsWith(allocated, "Code-pnp-");
            StringAssert.EndsWith(allocated, ".aspx");
            Assert.AreEqual(
                allocated,
                TopologyTargetPathAllocator.AllocateSegment(
                    "Code.aspx",
                    "urn:pnp:page:source-1",
                    new[] { "CODE.ASPX" },
                    preserveFileExtension: true));
        }

        [TestMethod]
        public void TopologyTargetPathAllocatorExtendsStableSuffixWhenNeeded()
        {
            var first = TopologyTargetPathAllocator.AllocateSegment(
                "Child_Web",
                "urn:pnp:web:source-1",
                new[] { "Child_Web" });
            var second = TopologyTargetPathAllocator.AllocateSegment(
                "Child_Web",
                "urn:pnp:web:source-1",
                new[] { "Child_Web", first });

            Assert.AreNotEqual(first, second);
            Assert.IsTrue(second.Length > first.Length);
            StringAssert.StartsWith(second, "Child_Web-pnp-");
        }

        [TestMethod]
        public void TopologyTargetPathAllocatorChangesOnlyCollidingLeaf()
        {
            var allocated = TopologyTargetPathAllocator.AllocateServerRelativePath(
                "/sites/target/Web/Pages/Code.aspx",
                "urn:pnp:spo-page:v1:source-1",
                new[]
                {
                    "/sites/other/Web/Pages/Code.aspx",
                    "/sites/target/Web/Pages/Code.aspx"
                },
                preserveFileExtension: true);

            StringAssert.StartsWith(allocated, "/sites/target/Web/Pages/Code-pnp-");
            StringAssert.EndsWith(allocated, ".aspx");
            Assert.AreEqual(
                "/sites/target/Web/Pages/Code.aspx",
                TopologyTargetPathAllocator.AllocateServerRelativePath(
                    "/sites/target/Web/Pages/Code.aspx",
                    "urn:pnp:spo-page:v1:source-1",
                    new[] { "/sites/other/Web/Pages/Code.aspx" },
                    preserveFileExtension: true));
        }

        [TestMethod]
        public void ListTargetPathResolverSuffixesOnlyTheCollidingListNode()
        {
            var plan = new ListMaterializationPlan
            {
                SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SourceWebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                SourceListId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TargetWebUrl = "https://target.sharepoint.com/sites/site-pnp/web",
                TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/site-pnp",
                TargetWebServerRelativeUrl = "/sites/site-pnp/web",
                PreferredTargetRootFolderServerRelativeUrl = "/sites/site-pnp/web/Shared Documents",
                TargetRootFolderServerRelativeUrl = "/sites/site-pnp/web/Shared Documents",
                PreferredTargetTitle = "Shared Documents",
                TargetTitle = "Shared Documents",
                OriginalIdentifier = "urn:pnp:spo-list:v1:source-list"
            };
            var resolution = ListTargetPathResolver.Resolve(
                plan,
                101,
                new[]
                {
                    new ListTargetInventoryItem
                    {
                        ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        RootFolderServerRelativeUrl = "/sites/site-pnp/web/Shared Documents",
                        Title = "Shared Documents",
                        BaseTemplate = 101
                    },
                    new ListTargetInventoryItem
                    {
                        ListId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        RootFolderServerRelativeUrl = "/sites/other/Shared Documents",
                        Title = "Other",
                        BaseTemplate = 101
                    }
                });

            Assert.IsTrue(resolution.CollisionResolved);
            StringAssert.StartsWith(
                resolution.TargetRootFolderServerRelativeUrl,
                "/sites/site-pnp/web/Shared Documents-pnp-");
            StringAssert.StartsWith(resolution.TargetTitle, "Shared Documents-pnp-");
            Assert.IsNull(resolution.ExistingOwnedTarget);
        }

        [TestMethod]
        public void ListTargetPathResolverRediscoversOwnedStableSuffix()
        {
            var plan = new ListMaterializationPlan
            {
                SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SourceWebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                SourceListId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TargetWebUrl = "https://target.sharepoint.com/sites/site-pnp/web",
                TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/site-pnp",
                TargetWebServerRelativeUrl = "/sites/site-pnp/web",
                PreferredTargetRootFolderServerRelativeUrl = "/sites/site-pnp/web/Resources",
                TargetRootFolderServerRelativeUrl = "/sites/site-pnp/web/Resources",
                PreferredTargetTitle = "Resources",
                TargetTitle = "Resources",
                OriginalIdentifier = "urn:pnp:spo-list:v1:source-list"
            };
            var foreign = new ListTargetInventoryItem
            {
                ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                RootFolderServerRelativeUrl = plan.PreferredTargetRootFolderServerRelativeUrl,
                Title = plan.PreferredTargetTitle,
                BaseTemplate = 100
            };
            var first = ListTargetPathResolver.Resolve(plan, 100, new[] { foreign });
            plan.TargetRootFolderServerRelativeUrl = first.TargetRootFolderServerRelativeUrl;
            plan.TargetTitle = first.TargetTitle;
            var owned = new ListTargetInventoryItem
            {
                ListId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                RootFolderServerRelativeUrl = plan.TargetRootFolderServerRelativeUrl,
                Title = plan.TargetTitle,
                BaseTemplate = 100,
                OriginalIdentifier = plan.OriginalIdentifier,
                PlanDigest = ListMigrationPlanFactory.ComputePlanDigest(plan)
            };
            plan.TargetRootFolderServerRelativeUrl = plan.PreferredTargetRootFolderServerRelativeUrl;
            plan.TargetTitle = plan.PreferredTargetTitle;

            var second = ListTargetPathResolver.Resolve(plan, 100, new[] { foreign, owned });

            Assert.AreSame(owned, second.ExistingOwnedTarget);
            Assert.AreEqual(owned.RootFolderServerRelativeUrl, second.TargetRootFolderServerRelativeUrl);
            Assert.AreEqual(owned.Title, second.TargetTitle);
            Assert.IsTrue(second.CollisionResolved);
        }

        [TestMethod]
        public void PublishingPageTargetPathResolverPreservesLibraryAndSuffixesOnlyFileName()
        {
            var resolution = PublishingPageTargetPathResolver.Resolve(
                "/sites/site-pnp/web/Pages/Templates/Guidance/Status.aspx",
                "urn:pnp:spo-page:v1:source-page",
                new[]
                {
                    "/sites/site-pnp/web/Pages/Templates/Guidance/Status.aspx",
                    "/sites/other/Pages/Status.aspx"
                });

            Assert.IsTrue(resolution.CollisionResolved);
            StringAssert.StartsWith(
                resolution.TargetPageServerRelativeUrl,
                "/sites/site-pnp/web/Pages/Templates/Guidance/Status-pnp-");
            StringAssert.EndsWith(resolution.TargetPageServerRelativeUrl, ".aspx");
            Assert.AreEqual(
                "/sites/site-pnp/web/Pages/Templates/Guidance",
                PagePath.GetDirectoryName(resolution.TargetPageServerRelativeUrl));
        }

        [TestMethod]
        public void PublishingPageTargetFolderPreservesExactWebRelativeHierarchy()
        {
            Assert.AreEqual(
                "Pages/Templates/IPKit with Managed Code",
                PublishingPageTargetLocationMaterializer.GetWebRelativeDirectory(
                    "/teams/uat_campusipkit-pnp",
                    "/teams/uat_campusipkit-pnp/Pages/Templates/IPKit with Managed Code"));
        }

        [TestMethod]
        public void PublishingPageTargetOwnershipUsesSiteWebAndFileIdentity()
        {
            var source = CreateSnapshot().Source;

            Assert.AreEqual(
                "urn:pnp:spo-page:v1:" + source.SiteId.ToString("D") + ":"
                    + source.WebId.ToString("D") + ":" + source.FileUniqueId.ToString("D"),
                PublishingPageTargetOwnership.OriginalIdentifier(source));
        }

        [TestMethod]
        public void TopologyWebTargetPathResolverSuffixesOnlyCollidingWebLeaf()
        {
            var plan = new WebMappingPlan
            {
                Kind = TopologyNodeKind.ChildWeb,
                SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SourceWebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                SourceParentWebId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/site-pnp",
                TargetParentWebUrl = "https://target.sharepoint.com/sites/site-pnp",
                PreferredTargetWebUrl = "https://target.sharepoint.com/sites/site-pnp/teams/Delivery",
                TargetWebUrl = "https://target.sharepoint.com/sites/site-pnp/teams/Delivery",
                PreferredTargetServerRelativeUrl = "/sites/site-pnp/teams/Delivery",
                TargetServerRelativeUrl = "/sites/site-pnp/teams/Delivery",
                TargetTitle = "Delivery",
                TargetTemplate = "STS#0",
                OriginalIdentifier = "urn:pnp:spo-web:v1:source-web"
            };

            var resolution = TopologyWebTargetPathResolver.Resolve(
                plan,
                new[]
                {
                    new TopologyWebTargetInventoryItem
                    {
                        WebId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        Url = plan.TargetWebUrl,
                        ServerRelativeUrl = plan.TargetServerRelativeUrl,
                        Title = "Foreign",
                        Template = "STS",
                        Configuration = 0
                    }
                });

            Assert.IsTrue(resolution.CollisionResolved);
            StringAssert.StartsWith(
                resolution.TargetServerRelativeUrl,
                "/sites/site-pnp/teams/Delivery-pnp-");
            Assert.AreEqual(
                resolution.TargetServerRelativeUrl,
                Uri.UnescapeDataString(new Uri(resolution.TargetWebUrl).AbsolutePath));
        }

        [TestMethod]
        public void TopologyPlanRetargeterMovesOnlySourceGraphDescendants()
        {
            var rootId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var selectedId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var descendantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var siblingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var site = new SiteCollectionMappingPlan
            {
                TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/site-pnp",
                Webs = new List<WebMappingPlan>
                {
                    new WebMappingPlan
                    {
                        Kind = TopologyNodeKind.SiteCollectionRoot,
                        SourceWebId = rootId,
                        TargetWebUrl = "https://target.sharepoint.com/sites/site-pnp",
                        TargetServerRelativeUrl = "/sites/site-pnp"
                    },
                    new WebMappingPlan
                    {
                        Kind = TopologyNodeKind.ChildWeb,
                        SourceWebId = selectedId,
                        SourceParentWebId = rootId,
                        TargetWebUrl = "https://target.sharepoint.com/sites/site-pnp/A",
                        TargetServerRelativeUrl = "/sites/site-pnp/A",
                        TargetParentWebUrl = "https://target.sharepoint.com/sites/site-pnp"
                    },
                    new WebMappingPlan
                    {
                        Kind = TopologyNodeKind.ChildWeb,
                        SourceWebId = descendantId,
                        SourceParentWebId = selectedId,
                        TargetWebUrl = "https://target.sharepoint.com/sites/site-pnp/A/B",
                        TargetServerRelativeUrl = "/sites/site-pnp/A/B",
                        TargetParentWebUrl = "https://target.sharepoint.com/sites/site-pnp/A"
                    },
                    new WebMappingPlan
                    {
                        Kind = TopologyNodeKind.ChildWeb,
                        SourceWebId = siblingId,
                        SourceParentWebId = rootId,
                        TargetWebUrl = "https://target.sharepoint.com/sites/site-pnp/A/Unrelated",
                        TargetServerRelativeUrl = "/sites/site-pnp/A/Unrelated",
                        TargetParentWebUrl = "https://target.sharepoint.com/sites/site-pnp"
                    }
                }
            };

            TopologyPlanRetargeter.RetargetWeb(site, selectedId, "/sites/site-pnp/A-pnp-12345678");

            Assert.AreEqual(
                "/sites/site-pnp/A-pnp-12345678",
                site.Webs.Single(value => value.SourceWebId == selectedId).TargetServerRelativeUrl);
            Assert.AreEqual(
                "/sites/site-pnp/A-pnp-12345678/B",
                site.Webs.Single(value => value.SourceWebId == descendantId).TargetServerRelativeUrl);
            Assert.AreEqual(
                "/sites/site-pnp/A/Unrelated",
                site.Webs.Single(value => value.SourceWebId == siblingId).TargetServerRelativeUrl);
        }

        [TestMethod]
        public void TopologyPlanRetargeterMovesSiteRootAndPreservesEveryWebTail()
        {
            var rootId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var childId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var site = new SiteCollectionMappingPlan
            {
                SourceSiteId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                PreferredTargetSiteCollectionUrl = "https://target.sharepoint.com/teams/source-pnp",
                TargetSiteCollectionUrl = "https://target.sharepoint.com/teams/source-pnp",
                Webs = new List<WebMappingPlan>
                {
                    new WebMappingPlan
                    {
                        Kind = TopologyNodeKind.SiteCollectionRoot,
                        SourceWebId = rootId,
                        PreferredTargetWebUrl = "https://target.sharepoint.com/teams/source-pnp",
                        TargetWebUrl = "https://target.sharepoint.com/teams/source-pnp",
                        PreferredTargetServerRelativeUrl = "/teams/source-pnp",
                        TargetServerRelativeUrl = "/teams/source-pnp"
                    },
                    new WebMappingPlan
                    {
                        Kind = TopologyNodeKind.ChildWeb,
                        SourceWebId = childId,
                        SourceParentWebId = rootId,
                        PreferredTargetWebUrl = "https://target.sharepoint.com/teams/source-pnp/Delivery",
                        TargetWebUrl = "https://target.sharepoint.com/teams/source-pnp/Delivery",
                        PreferredTargetServerRelativeUrl = "/teams/source-pnp/Delivery",
                        TargetServerRelativeUrl = "/teams/source-pnp/Delivery",
                        TargetParentWebUrl = "https://target.sharepoint.com/teams/source-pnp"
                    }
                }
            };

            TopologyPlanRetargeter.RetargetSiteCollection(
                site,
                "https://target.sharepoint.com/teams/source-pnp-pnp-12345678",
                "The preferred Site Collection is a foreign collision.");

            Assert.AreEqual("https://target.sharepoint.com/teams/source-pnp", site.PreferredTargetSiteCollectionUrl);
            Assert.AreEqual("https://target.sharepoint.com/teams/source-pnp-pnp-12345678", site.TargetSiteCollectionUrl);
            Assert.IsTrue(site.TargetSiteCollisionResolved);
            Assert.AreEqual("/teams/source-pnp-pnp-12345678", site.Webs[0].TargetServerRelativeUrl);
            Assert.AreEqual("/teams/source-pnp-pnp-12345678/Delivery", site.Webs[1].TargetServerRelativeUrl);
            Assert.AreEqual(
                "https://target.sharepoint.com/teams/source-pnp-pnp-12345678",
                site.Webs[1].TargetParentWebUrl);
            Assert.AreEqual("/teams/source-pnp/Delivery", site.Webs[1].PreferredTargetServerRelativeUrl);
        }

        [TestMethod]
        public void ListItemValueCaptureKeepsUnsupportedRawEvidenceForFutureRecovery()
        {
            var captured = ListItemValueSerializer.Serialize("FutureField", new Dictionary<string, object>
            {
                ["reference"] = "OOCL-42",
                ["sequence"] = 7
            });

            Assert.AreEqual(ListItemValueKind.Unsupported, captured.Kind);
            Assert.AreEqual(EvidenceAvailability.Partial, captured.Availability);
            Assert.AreEqual(typeof(Dictionary<string, object>).FullName, captured.RawType);
            StringAssert.Contains(captured.RawValueJson, "OOCL-42");
            Assert.IsTrue(captured.Diagnostics.Any(value => value.Contains("No typed list-item serializer")));
        }

        [TestMethod]
        public void ListPlannerOrdersLookupClosureAndRetainsUnusedUnknownFieldsAsEvidence()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var ownerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var lookupId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var lookupFieldId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var unknownFieldId = Guid.Parse("66666666-6666-6666-6666-666666666666");
            var owner = CreateListSnapshot(siteId, webId, ownerId, "Owner");
            owner.Fields.Add(new ListFieldSnapshot
            {
                Id = lookupFieldId,
                InternalName = "Category",
                TypeAsString = "Lookup",
                SchemaXml = "<Field ID='{55555555-5555-5555-5555-555555555555}' Name='Category' Type='Lookup' List='{44444444-4444-4444-4444-444444444444}' ShowField='Title' />",
                SourceLookupWebId = webId,
                SourceLookupListId = lookupId,
                LookupField = "Title"
            });
            owner.Fields.Add(new ListFieldSnapshot
            {
                Id = unknownFieldId,
                InternalName = "FutureValue",
                TypeAsString = "FutureType",
                SchemaXml = "<Field ID='{66666666-6666-6666-6666-666666666666}' Name='FutureValue' Type='FutureType' />"
            });
            var lookup = CreateListSnapshot(siteId, webId, lookupId, "Lookup");

            var plan = ListMigrationPlanFactory.Create(
                new[] { owner, lookup },
                new[] { new ListLookupDependency { SourceListId = ownerId, LookupListId = lookupId, FieldId = lookupFieldId, FieldInternalName = "Category" } },
                CreateTopology(siteId, webId),
                null,
                null);

            Assert.IsTrue(plan.IsExecutable, string.Join(Environment.NewLine, plan.Issues.Select(value => value.Message)));
            CollectionAssert.AreEqual(new[] { lookupId, ownerId }, plan.OrderedSourceListIds.ToArray());
            var ownerPlan = plan.Lists.Single(value => value.SourceListId == ownerId);
            Assert.AreEqual(ListFieldMaterializationDisposition.MapLookup, ownerPlan.Fields.Single(value => value.SourceFieldId == lookupFieldId).Disposition);
            Assert.AreEqual(ListFieldMaterializationDisposition.EvidenceOnly, ownerPlan.Fields.Single(value => value.SourceFieldId == unknownFieldId).Disposition);
        }

        [TestMethod]
        public void IngredientProjectionAssignsExplicitActionsAcrossTopologyAndListContents()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var libraryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var titleFieldId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var futureFieldId = Guid.Parse("66666666-6666-6666-6666-666666666666");
            var libraryFieldId = Guid.Parse("77777777-7777-7777-7777-777777777777");
            const string siteContentTypeId = "0x010100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            const string listContentTypeId = "0x010100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA00BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
            var attachmentBytes = Encoding.UTF8.GetBytes("attachment");
            var documentBytes = Encoding.UTF8.GetBytes("document");
            var list = CreateListSnapshot(siteId, webId, listId, "Items");
            list.EnableAttachments = true;
            list.Fields.Add(new ListFieldSnapshot
            {
                Id = titleFieldId,
                InternalName = "CaseTitle",
                Title = "Case title",
                TypeAsString = "Text",
                SchemaXml = "<Field ID='{55555555-5555-5555-5555-555555555555}' Name='CaseTitle' Type='Text' />"
            });
            list.Fields.Add(new ListFieldSnapshot
            {
                Id = futureFieldId,
                InternalName = "FutureValue",
                Title = "Future value",
                TypeAsString = "FutureType",
                SchemaXml = "<Field ID='{66666666-6666-6666-6666-666666666666}' Name='FutureValue' Type='FutureType' />"
            });
            list.ContentTypes.Add(new ListContentTypeSnapshot { Id = "0x01", Name = "Item" });
            list.SourceItemCount = 1;
            list.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 7,
                Values = new List<ListItemValueSnapshot>
                {
                    new ListItemValueSnapshot { InternalName = "CaseTitle", Kind = ListItemValueKind.String, ScalarValue = "OOCL" }
                },
                Attachments = new List<ListAttachmentSnapshot>
                {
                    new ListAttachmentSnapshot
                    {
                        FileName = "evidence.txt",
                        ServerRelativeUrl = "/sites/source/Lists/Items/Attachments/7/evidence.txt",
                        Content = new ListBinaryArtifactSnapshot
                        {
                            Artifact = MigrationArtifact.Describe(attachmentBytes, "text/plain", "evidence.txt"),
                            ContentBase64 = Convert.ToBase64String(attachmentBytes),
                            Availability = EvidenceAvailability.Captured,
                            RepresentationKind = ListBinaryRepresentationKind.OrdinaryFilePayload
                        }
                    }
                }
            });
            var library = CreateListSnapshot(siteId, webId, libraryId, "Documents");
            library.BaseTemplate = 101;
            library.BaseType = "DocumentLibrary";
            library.RootFolderServerRelativeUrl = "/sites/source/Documents";
            var libraryFieldSchema = "<Field ID='{77777777-7777-7777-7777-777777777777}' Name='CaseNumber' Type='Text' />";
            library.Fields.Add(new ListFieldSnapshot
            {
                Id = libraryFieldId,
                InternalName = "CaseNumber",
                Title = "Case number",
                TypeAsString = "Text",
                SchemaXml = libraryFieldSchema,
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(libraryFieldSchema)
            });
            library.SiteContentTypes.Add(new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                Availability = EvidenceAvailability.Captured,
                SourceWebUrl = "https://source.sharepoint.com/sites/source",
                SourceScope = "/sites/source",
                ContentTypeId = siteContentTypeId,
                Name = "Case document",
                ParentContentTypeId = "0x0101",
                ParentContentTypeName = "Document",
                RequiredFieldLinks = new List<ContentTypeFieldLinkSnapshot>
                {
                    new ContentTypeFieldLinkSnapshot { FieldId = libraryFieldId, Name = "CaseNumber" }
                },
                RequiredFieldClosure = new List<FieldSchemaSnapshot>
                {
                    new FieldSchemaSnapshot
                    {
                        Id = libraryFieldId,
                        InternalName = "CaseNumber",
                        Title = "Case number",
                        TypeAsString = "Text",
                        SchemaXml = libraryFieldSchema,
                        PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(libraryFieldSchema),
                        Role = FieldSchemaRole.DirectBinding
                    }
                }
            });
            library.ContentTypes.Add(new ListContentTypeSnapshot
            {
                Id = listContentTypeId,
                Name = "Case document",
                ParentId = siteContentTypeId,
                FieldLinks = new List<ListContentTypeFieldLinkSnapshot>
                {
                    new ListContentTypeFieldLinkSnapshot { FieldId = libraryFieldId, InternalName = "CaseNumber" }
                }
            });
            library.SourceItemCount = 1;
            library.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 9,
                Values = new List<ListItemValueSnapshot>
                {
                    new ListItemValueSnapshot { InternalName = "CaseNumber", Kind = ListItemValueKind.String, ScalarValue = "C-42" }
                },
                Document = new ListDocumentSnapshot
                {
                    Kind = ListDocumentObjectKind.File,
                    Name = "case.docx",
                    ServerRelativeUrl = "/sites/source/Documents/case.docx",
                    Length = documentBytes.LongLength,
                    Content = new ListBinaryArtifactSnapshot
                    {
                        Artifact = MigrationArtifact.Describe(documentBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "case.docx"),
                        ContentBase64 = Convert.ToBase64String(documentBytes),
                        Availability = EvidenceAvailability.Captured,
                        RepresentationKind = ListBinaryRepresentationKind.OrdinaryFilePayload
                    }
                }
            });

            var package = CreateMigrationPackage();
            var snapshot = package.Snapshot;
            snapshot.Source.SiteId = siteId;
            snapshot.Source.WebId = webId;
            snapshot.SourceTopology = new SourceSiteCollectionSnapshot
            {
                SiteId = siteId,
                RootWebId = webId,
                SiteCollectionUrl = snapshot.Source.WebUrl,
                ServerRelativeUrl = snapshot.Source.WebServerRelativeUrl,
                Webs = new List<SourceWebSnapshot>
                {
                    new SourceWebSnapshot
                    {
                        SiteId = siteId,
                        WebId = webId,
                        SiteCollectionUrl = snapshot.Source.WebUrl,
                        WebUrl = snapshot.Source.WebUrl,
                        ServerRelativeUrl = snapshot.Source.WebServerRelativeUrl,
                        Title = "Source",
                        WebTemplate = "STS",
                        Availability = EvidenceAvailability.Captured
                    }
                }
            };
            snapshot.ListDependencies = new List<ListDependencySnapshot> { list, library };
            var topology = CreateTopology(siteId, webId);
            package.Plan.Topology = topology;
            package.Plan.TopologyTargetAnalysis = CreateAdmittedTopologyAnalysis(topology, siteId, webId);
            package.Plan.ListMigration = ListMigrationPlanFactory.Create(snapshot.ListDependencies, null, topology, null, null);
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);

            var actions = PublishingPageIngredientActionProjector.Project(snapshot, package.Plan);
            var byId = actions.ToDictionary(value => value.IngredientId, StringComparer.Ordinal);

            Assert.IsFalse(actions.Any(value => value.PolicyId == "policy.ingredient.unknown"));
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.Web(siteId, webId)].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.ListField(webId, listId, titleFieldId)].Disposition);
            Assert.AreEqual(IngredientDisposition.Drop, byId[PublishingPageIngredientIds.ListField(webId, listId, futureFieldId)].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.ListContentType(webId, listId, "0x01")].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.ListItem(webId, listId, 7)].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.ListAttachment(webId, listId, 7, "evidence.txt")].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.SiteContentType("/sites/source", siteContentTypeId)].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.SiteField("/sites/source", libraryFieldId)].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.ListContentType(webId, libraryId, listContentTypeId)].Disposition);
            Assert.AreEqual(IngredientDisposition.Preserve, byId[PublishingPageIngredientIds.ListDocument(webId, libraryId, 9)].Disposition);
            Assert.AreEqual(0, PageIngredientPlanEvaluator.Evaluate(snapshot.IngredientGraph, actions).Issues.Count);
        }

        [TestMethod]
        public void IngredientProjectionVersion6KeepsListAndFieldAsIndependentTransactions()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var fieldId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var list = CreateListSnapshot(siteId, webId, listId, "Items");
            list.Fields.Add(new ListFieldSnapshot
            {
                Id = fieldId,
                InternalName = "RepeatedValue",
                Title = "Repeated value",
                TypeAsString = "Text",
                SchemaXml = "<Field ID='{44444444-4444-4444-4444-444444444444}' Name='RepeatedValue' Type='Text' />"
            });
            list.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 7,
                Values = new List<ListItemValueSnapshot>
                {
                    new ListItemValueSnapshot { InternalName = "RepeatedValue", Kind = ListItemValueKind.String, ScalarValue = "first" },
                    new ListItemValueSnapshot { InternalName = "RepeatedValue", Kind = ListItemValueKind.String, ScalarValue = "second" }
                }
            });

            var snapshot = CreateMigrationPackage().Snapshot;
            snapshot.ListDependencies = new List<ListDependencySnapshot> { list };
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);

            var itemId = PublishingPageIngredientIds.ListItem(webId, listId, 7);
            var listFieldId = PublishingPageIngredientIds.ListField(webId, listId, fieldId);
            var projectedListId = PublishingPageIngredientIds.List(webId, listId);
            Assert.AreEqual(PublishingPageIngredientGraphProjector.CurrentProjectionVersion, snapshot.IngredientGraph.ProjectionVersion);
            Assert.AreEqual(0, snapshot.IngredientGraph.Edges.Count(edge =>
                edge.FromIngredientId == itemId
                && edge.ToIngredientId == listFieldId));
            Assert.AreEqual(1, snapshot.IngredientGraph.Edges.Count(edge =>
                edge.FromIngredientId == projectedListId
                && edge.ToIngredientId == listFieldId
                && edge.Relationship == PageIngredientRelationship.Backs
                && edge.Requirement == PageIngredientRequirement.Conditional));
            Assert.AreEqual(1, snapshot.IngredientGraph.Edges.Count(edge =>
                edge.FromIngredientId == listFieldId
                && edge.ToIngredientId == projectedListId
                && edge.Relationship == PageIngredientRelationship.DependsOn
                && edge.Requirement == PageIngredientRequirement.Required));

            var version3 = PublishingPageIngredientGraphProjector.ProjectVersion3(snapshot);
            Assert.AreEqual(1, version3.Edges.Count(edge =>
                edge.FromIngredientId == projectedListId
                && edge.ToIngredientId == listFieldId
                && edge.Relationship == PageIngredientRelationship.Backs
                && edge.Requirement == PageIngredientRequirement.Required));

            var version2 = PublishingPageIngredientGraphProjector.ProjectVersion2(snapshot);
            Assert.AreEqual(1, version2.Edges.Count(edge =>
                edge.FromIngredientId == itemId
                && edge.ToIngredientId == listFieldId
                && edge.Relationship == PageIngredientRelationship.BindsTo
                && edge.Requirement == PageIngredientRequirement.Required));
        }

        [TestMethod]
        public void CurrentIngredientProjectionMakesInformationProtectionAnAcyclicDocumentTransaction()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var list = CreateListSnapshot(siteId, webId, listId, "Protected documents");
            list.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 7,
                Document = new ListDocumentSnapshot
                {
                    Name = "protected.docx",
                    ServerRelativeUrl = "/sites/source/Protected documents/protected.docx",
                    InformationProtection = new ListDocumentInformationProtectionSnapshot
                    {
                        LabelId = "9fbde396-1a24-4c79-8edf-9254a0f35055",
                        AssignmentMethod = "1",
                        LabelHash = "label-hash;00"
                    }
                }
            });
            var snapshot = CreateMigrationPackage().Snapshot;
            snapshot.ListDependencies = new List<ListDependencySnapshot> { list };

            var current = PublishingPageIngredientGraphProjector.Project(snapshot);
            var version5 = PublishingPageIngredientGraphProjector.ProjectVersion5(snapshot);
            var documentId = PublishingPageIngredientIds.ListDocument(webId, listId, 7);
            var policyId = PublishingPageIngredientIds.ListDocumentInformationProtection(webId, listId, 7);

            Assert.AreEqual(PublishingPageIngredientGraphProjector.CurrentProjectionVersion, current.ProjectionVersion);
            Assert.AreEqual(1, current.Edges.Count(edge =>
                edge.FromIngredientId == documentId
                && edge.ToIngredientId == policyId
                && edge.Relationship == PageIngredientRelationship.GovernedBy
                && edge.Requirement == PageIngredientRequirement.Optional));
            Assert.AreEqual(1, current.Edges.Count(edge =>
                edge.FromIngredientId == policyId
                && edge.ToIngredientId == documentId
                && edge.Relationship == PageIngredientRelationship.DependsOn
                && edge.Requirement == PageIngredientRequirement.Required));
            Assert.AreEqual(1, version5.Edges.Count(edge =>
                edge.FromIngredientId == documentId
                && edge.ToIngredientId == policyId
                && edge.Relationship == PageIngredientRelationship.GovernedBy
                && edge.Requirement == PageIngredientRequirement.Required));
        }

        [TestMethod]
        public void CurrentIngredientProjectionBindsLayoutTransactionsToTheRootWeb()
        {
            var package = CreateMigrationPackage();
            var snapshot = package.Snapshot;
            snapshot.Layout = CreateCustomLayout();
            snapshot.SourceTopology = new SourceSiteCollectionSnapshot
            {
                SiteId = snapshot.Source.SiteId,
                RootWebId = snapshot.Source.WebId,
                SiteCollectionUrl = snapshot.Source.WebUrl,
                ServerRelativeUrl = snapshot.Source.WebServerRelativeUrl,
                Webs = new List<SourceWebSnapshot>
                {
                    new SourceWebSnapshot
                    {
                        SiteId = snapshot.Source.SiteId,
                        WebId = snapshot.Source.WebId,
                        SiteCollectionUrl = snapshot.Source.WebUrl,
                        WebUrl = snapshot.Source.WebUrl,
                        ServerRelativeUrl = snapshot.Source.WebServerRelativeUrl,
                        Availability = EvidenceAvailability.Captured
                    }
                }
            };

            var graph = PublishingPageIngredientGraphProjector.Project(snapshot);
            var version4 = PublishingPageIngredientGraphProjector.ProjectVersion4(snapshot);
            var ownerWebId = PublishingPageIngredientIds.Web(snapshot.Source.SiteId, snapshot.Source.WebId);
            var resourceId = PublishingPageIngredientIds.LayoutResource(
                snapshot.Layout.ResourceArtifacts.Single().Reference.Value);
            var fieldId = PublishingPageIngredientIds.PageContentTypeField(
                snapshot.Layout.AssociatedContentTypeSchema.RequiredFieldClosure.Single().Id);

            Assert.AreEqual(PublishingPageIngredientGraphProjector.CurrentProjectionVersion, graph.ProjectionVersion);
            Assert.IsTrue(HasRequiredEdge(graph, PublishingPageIngredientIds.Layout, ownerWebId));
            Assert.IsTrue(HasRequiredEdge(graph, PublishingPageIngredientIds.ContentType, ownerWebId));
            Assert.IsTrue(HasRequiredEdge(graph, resourceId, ownerWebId));
            Assert.IsTrue(HasRequiredEdge(graph, fieldId, ownerWebId));
            Assert.IsFalse(HasRequiredEdge(version4, PublishingPageIngredientIds.Layout, ownerWebId));
            Assert.AreEqual(PublishingPageIngredientGraphProjector.ProjectionVersionV4, version4.ProjectionVersion);
        }

        [TestMethod]
        public void MaterializedReferenceRemainsExecutableWhenPageContentIsDeferred()
        {
            var package = CreateMigrationPackage();
            package.Snapshot.Dependencies.Add(new PageReferenceSnapshot
            {
                Id = "asset",
                OriginalValue = "/sites/source/SiteAssets/app.js",
                SourceAbsoluteUrl = "https://source.sharepoint.com/sites/source/SiteAssets/app.js",
                SourceServerRelativeUrl = "/sites/source/SiteAssets/app.js",
                ContentBase64 = "AQ==",
                ContentSha256 = new string('a', 64),
                ContentLength = 1,
                CaptureStatus = PageCaptureStatus.Captured
            });
            package.Plan.DependencyActions.Add(new PageReferenceAction
            {
                SnapshotDependencyId = "asset",
                TargetAbsoluteUrl = "https://target.sharepoint.com/sites/target/SiteAssets/app.js",
                TargetServerRelativeUrl = "/sites/target/SiteAssets/app.js",
                Disposition = PageReferenceDisposition.MaterializeAtTarget
            });
            var graph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            var actions = PublishingPageIngredientActionProjector.Project(package.Snapshot, package.Plan, graph);
            var content = actions.Single(value => value.IngredientId == PublishingPageIngredientIds.PublishingContent);
            content.Disposition = IngredientDisposition.Defer;

            var result = PageIngredientPlanEvaluator.Evaluate(graph, actions);
            var referenceId = PublishingPageIngredientIds.Reference("asset");
            var referenceAction = actions.Single(value => value.IngredientId == referenceId);

            Assert.AreEqual(IngredientDisposition.Transform, referenceAction.Disposition);
            CollectionAssert.Contains(referenceAction.ReleasedDependencyIngredientIds.ToList(), PublishingPageIngredientIds.PublishingContent);
            Assert.AreEqual(PageIngredientExecutionState.Executable, result.ExecutionFrontier.GetState(referenceId));
            Assert.AreEqual(PageIngredientExecutionState.Deferred, result.ExecutionFrontier.GetState(PublishingPageIngredientIds.PublishingContent));
        }

        private static bool HasRequiredEdge(CanonicalPageIngredientGraph graph, string from, string to)
        {
            return graph.Edges.Any(value => value.FromIngredientId == from
                && value.ToIngredientId == to
                && value.Requirement == PageIngredientRequirement.Required);
        }

        [TestMethod]
        public void ListPackageValidationAllowsSiblingContentTypesWithTheSameParent()
        {
            var list = CreateListSnapshot(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Academy");
            list.ContentTypes.Add(new ListContentTypeSnapshot
            {
                Id = "0x0100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                Name = "Academy article",
                ParentId = "0x01"
            });
            list.ContentTypes.Add(new ListContentTypeSnapshot
            {
                Id = "0x0100BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                Name = "Academy announcement",
                ParentId = "0x01"
            });

            ListDependencyPackageValidator.Validate(
                Array.Empty<ClassicWebPartSnapshot>(),
                Array.Empty<ClassicListWebPartBindingSnapshot>(),
                new[] { list },
                Array.Empty<ListLookupDependency>(),
                null,
                null);

            list.SourceItemCount = 1;
            list.BaseTemplate = 101;
            var plan = ListMigrationPlanFactory.Create(
                new[] { list },
                null,
                CreateTopology(list.SourceSiteId, list.SourceWebId),
                null,
                null);

            Assert.IsFalse(plan.Lists.Single().Issues.Any(value =>
                value.Code == "ListBinaryEvidenceUnavailable"));
        }

        [TestMethod]
        public void ListPackageValidationAcceptsCapturedIrmPolicyEvidence()
        {
            var list = CreateListSnapshot(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Protected documents");
            list.InformationRightsManagement = new ListInformationRightsManagementSnapshot
            {
                IrmEnabled = true,
                Availability = EvidenceAvailability.Captured,
                Policy = new ListInformationRightsManagementPolicySnapshot
                {
                    PolicyTitle = "Source protected library",
                    TemplateId = "source-template"
                }
            };

            ListDependencyPackageValidator.Validate(
                Array.Empty<ClassicWebPartSnapshot>(),
                Array.Empty<ClassicListWebPartBindingSnapshot>(),
                new[] { list },
                Array.Empty<ListLookupDependency>(),
                null,
                null);
        }

        [TestMethod]
        public void ListPackageValidationRejectsUnavailableIrmWithoutLiteralAuthorizationEvidence()
        {
            var list = CreateListSnapshot(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Protected documents");
            list.InformationRightsManagement = new ListInformationRightsManagementSnapshot
            {
                IrmEnabled = true,
                Availability = EvidenceAvailability.Unavailable
            };

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                ListDependencyPackageValidator.Validate(
                    Array.Empty<ClassicWebPartSnapshot>(),
                    Array.Empty<ClassicListWebPartBindingSnapshot>(),
                    new[] { list },
                    Array.Empty<ListLookupDependency>(),
                    null,
                    null));

            StringAssert.Contains(exception.Message, "without literal HTTP 401/403 evidence");
        }

        [TestMethod]
        public void ListPackageValidationAcceptsLiteralIrmAuthorizationEvidence()
        {
            var list = CreateListSnapshot(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Protected documents");
            list.InformationRightsManagement = new ListInformationRightsManagementSnapshot
            {
                IrmEnabled = true,
                Availability = EvidenceAvailability.Unavailable,
                AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                    "capture-list-irm-policy",
                    "https://source.sharepoint.com/sites/source/_vti_bin/client.svc/ProcessQuery",
                    403,
                    new DateTimeOffset(2026, 9, 4, 1, 2, 3, TimeSpan.Zero))
            };

            ListDependencyPackageValidator.Validate(
                Array.Empty<ClassicWebPartSnapshot>(),
                Array.Empty<ClassicListWebPartBindingSnapshot>(),
                new[] { list },
                Array.Empty<ListLookupDependency>(),
                null,
                null);
        }

        [TestMethod]
        public void ListPackageValidationRetainsBindingEvidenceWhenTopologyCaptureIsUnavailable()
        {
            var webPartId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var siteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var webId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var listId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            const string exportXml = "<webPart />";
            const string exportDigest = "captured-export-digest";
            var list = CreateListSnapshot(siteId, webId, listId, "Referenced list");
            var webPart = new ClassicWebPartSnapshot
            {
                Id = webPartId,
                ExportXml = exportXml,
                ExportSha256 = exportDigest
            };
            var binding = new ClassicListWebPartBindingSnapshot
            {
                SourceWebPartId = webPartId,
                SourceListWebId = webId,
                SourceListId = listId,
                SourceExportXml = exportXml,
                SourceExportSha256 = exportDigest
            };

            ListDependencyPackageValidator.Validate(
                new[] { webPart },
                new[] { binding },
                new[] { list },
                Array.Empty<ListLookupDependency>(),
                null,
                null);
        }

        [TestMethod]
        public void ListPackageValidationRetainsExplicitDocumentLengthMismatchEvidence()
        {
            var bytes = Encoding.UTF8.GetBytes("captured payload");
            var list = CreateListSnapshot(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Documents");
            list.BaseType = "DocumentLibrary";
            list.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 7,
                Availability = EvidenceAvailability.Partial,
                Document = new ListDocumentSnapshot
                {
                    Kind = ListDocumentObjectKind.File,
                    Name = "changing.pptx",
                    ServerRelativeUrl = "/sites/source/Documents/changing.pptx",
                    Length = bytes.LongLength + 10,
                    Content = new ListBinaryArtifactSnapshot
                    {
                        Artifact = MigrationArtifact.Describe(bytes, "application/octet-stream", "changing.pptx"),
                        ContentBase64 = Convert.ToBase64String(bytes),
                        Availability = EvidenceAvailability.Partial,
                        RepresentationKind = ListBinaryRepresentationKind.OrdinaryFilePayload,
                        Diagnostics = { "DocumentMetadataLengthMismatch: metadataLength=26; payloadLength=16." }
                    }
                }
            });

            ListDependencyPackageValidator.Validate(
                Array.Empty<ClassicWebPartSnapshot>(),
                Array.Empty<ClassicListWebPartBindingSnapshot>(),
                new[] { list },
                Array.Empty<ListLookupDependency>(),
                null,
                null);

            Assert.IsTrue(ListMigrationPlanFactory.HasReplayableBinary(
                list.Items.Single().Document.Content));
        }

        [TestMethod]
        public void IngredientProjectionAssignsLayoutResourceActionFromMaterializationPlan()
        {
            var package = CreateMigrationPackage();
            var snapshot = package.Snapshot;
            snapshot.Layout = CreateCustomLayout();
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);
            package.Plan.LayoutMaterialization = PublishingPageLayoutPlanFactory.Create(
                snapshot.Layout,
                new Uri(snapshot.Source.WebUrl),
                new Uri(package.Plan.TargetWebUrl),
                new Uri(package.Plan.TargetWebUrl),
                "EnterpriseWiki.aspx");
            package.Plan.LayoutAdmission = null;

            var actions = PublishingPageIngredientActionProjector.Project(snapshot, package.Plan);
            var resource = actions.Single(value => value.IngredientId == PublishingPageIngredientIds.LayoutResource("~site/SiteAssets/Contoso/app.js"));

            Assert.AreEqual(IngredientDisposition.Preserve, resource.Disposition);
            Assert.AreEqual("copy-exact-bytes-create-only", resource.Realization);
            Assert.AreEqual("policy.layout.resource", resource.PolicyId);
            Assert.IsTrue(actions.Any(value => value.PolicyId == "policy.ingredient.unknown"
                && value.Disposition == IngredientDisposition.Defer));
        }

        [TestMethod]
        public void ListPlannerBlocksNonemptyPrincipalValuesWithoutExplicitMapping()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var fieldId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var source = CreateListSnapshot(siteId, webId, listId, "People");
            source.Fields.Add(new ListFieldSnapshot
            {
                Id = fieldId,
                InternalName = "Owner",
                TypeAsString = "User",
                SchemaXml = "<Field ID='{44444444-4444-4444-4444-444444444444}' Name='Owner' Type='User' />"
            });
            source.SourceItemCount = 1;
            source.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 1,
                Values = new List<ListItemValueSnapshot>
                {
                    new ListItemValueSnapshot { InternalName = "Owner", Kind = ListItemValueKind.User, ScalarValue = "i:0#.f|membership|owner@example.com" }
                }
            });

            var plan = ListMigrationPlanFactory.Create(new[] { source }, null, CreateTopology(siteId, webId), null, null);

            Assert.IsFalse(plan.IsExecutable);
            Assert.AreEqual(ListFieldMaterializationDisposition.Block, plan.Lists.Single().Fields.Single(value => value.SourceFieldId == fieldId).Disposition);
            Assert.IsTrue(plan.Lists.Single().Issues.Any(value => value.Code == "PrincipalMappingUnavailable"));
        }

        [TestMethod]
        public void WebPartReplayCompositionUsesMaterializedListAndViewReceipts()
        {
            var sourceWeb = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var sourceList = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var sourceView = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var webPartId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var targetWeb = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var targetList = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var targetView = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            var xml = "<webParts><webPart><data><properties>"
                + "<property name='ListId'>" + sourceList.ToString("D") + "</property>"
                + "<property name='ListName'>{" + sourceList.ToString("D") + "}</property>"
                + "<property name='WebId'>" + sourceWeb.ToString("D") + "</property>"
                + "<property name='ViewGuid'>" + sourceView.ToString("D") + "</property>"
                + "<property name='TitleUrl'>/sites/source/Lists/Items</property>"
                + "<property name='XmlDefinition'>&lt;View Name=\"{" + sourceView.ToString("D") + "}\" Url=\"/sites/source/Pages/A.aspx\" /&gt;</property>"
                + "</properties></data></webPart></webParts>";
            var captured = new ClassicWebPartSnapshot { Id = webPartId, ExportXml = xml };
            var binding = new ClassicListWebPartBindingSnapshot
            {
                SourceWebPartId = webPartId,
                SourceListWebId = sourceWeb,
                SourceListId = sourceList,
                SourceViewId = sourceView,
                SourceTitleUrl = "/sites/source/Lists/Items",
                SourceExportXml = xml
            };
            var receipt = new ListMaterializationReceipt
            {
                SourceWebId = sourceWeb,
                SourceListId = sourceList,
                TargetWebId = targetWeb,
                TargetListId = targetList,
                TargetRootFolderServerRelativeUrl = "/sites/target/Lists/Items",
                TargetViewIds = new Dictionary<Guid, Guid> { [sourceView] = targetView }
            };

            var replay = ClassicWebPartReplayComposer.Compose(
                captured,
                new ClassicWebPartAction { SourceWebPartId = webPartId, Disposition = ClassicWebPartDisposition.RebindListAfterMaterialization },
                binding,
                receipt,
                "/sites/target/Pages/A.aspx",
                Array.Empty<PageTextReplacement>());
            var properties = XDocument.Parse(replay).Descendants().Where(value => value.Name.LocalName == "property")
                .ToDictionary(value => (string)value.Attribute("name"), value => value.Value, StringComparer.OrdinalIgnoreCase);

            Assert.AreEqual(targetWeb.ToString("D"), properties["WebId"]);
            Assert.AreEqual(targetList.ToString("D"), properties["ListId"]);
            Assert.AreEqual(targetView.ToString("D"), properties["ViewGuid"]);
            Assert.AreEqual("/sites/target/Lists/Items", properties["TitleUrl"]);
        }

        [TestMethod]
        public void CustomDocumentContentTypeIsCapturedAsClosureInsteadOfMisclassifiedAsRuntime()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var fieldId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            const string siteContentTypeId = "0x010100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            const string listContentTypeId = siteContentTypeId + "00BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
            var fieldSchema = "<Field ID='{44444444-4444-4444-4444-444444444444}' Name='CaseNumber' DisplayName='Case number' Type='Text' />";
            var source = CreateListSnapshot(siteId, webId, listId, "Documents");
            source.BaseTemplate = 101;
            source.BaseType = "DocumentLibrary";
            source.Fields.Add(new ListFieldSnapshot
            {
                Id = fieldId,
                InternalName = "CaseNumber",
                TypeAsString = "Text",
                SchemaXml = fieldSchema,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(fieldSchema),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(fieldSchema)
            });
            source.ContentTypes.Add(new ListContentTypeSnapshot
            {
                Id = listContentTypeId,
                Name = "Case document",
                ParentId = siteContentTypeId,
                FieldLinks = new List<ListContentTypeFieldLinkSnapshot>
                {
                    new ListContentTypeFieldLinkSnapshot { FieldId = fieldId, InternalName = "CaseNumber" }
                }
            });
            source.SiteContentTypes.Add(new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                Availability = EvidenceAvailability.Captured,
                SourceWebUrl = "https://source.sharepoint.com/sites/source",
                SourceScope = "/sites/source",
                ContentTypeId = siteContentTypeId,
                Name = "Case document",
                Hidden = true,
                ReadOnly = true,
                Sealed = true,
                ParentContentTypeId = "0x0101",
                ParentContentTypeName = "Document",
                RequiredFieldLinks = new List<ContentTypeFieldLinkSnapshot>
                {
                    new ContentTypeFieldLinkSnapshot { FieldId = fieldId, Name = "CaseNumber", Role = FieldSchemaRole.DirectBinding }
                },
                RequiredFieldClosure = new List<FieldSchemaSnapshot>
                {
                    new FieldSchemaSnapshot
                    {
                        Id = fieldId,
                        InternalName = "CaseNumber",
                        Title = "Case number",
                        TypeAsString = "Text",
                        SchemaXml = fieldSchema,
                        SchemaXmlSha256 = MigrationDigest.ComputeSha256(fieldSchema),
                        PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(fieldSchema),
                        Role = FieldSchemaRole.DirectBinding
                    }
                }
            });

            var plan = ListMigrationPlanFactory.Create(new[] { source }, null, CreateTopology(siteId, webId), null, null);
            var listPlan = plan.Lists.Single();

            Assert.IsFalse(ContentTypeRuntimeCatalog.IsTargetRuntime(siteContentTypeId));
            Assert.IsTrue(plan.IsExecutable, string.Join(Environment.NewLine, listPlan.Issues.Select(value => value.Message)));
            Assert.AreEqual(1, listPlan.SiteContentTypes.Count);
            Assert.AreEqual(siteContentTypeId, listPlan.SiteContentTypes[0].Schema.ContentTypeId);
            Assert.IsTrue(listPlan.SiteContentTypes[0].Schema.Hidden);
            Assert.IsTrue(listPlan.SiteContentTypes[0].Schema.ReadOnly);
            Assert.IsTrue(listPlan.SiteContentTypes[0].Schema.Sealed);
            Assert.AreEqual("https://target.sharepoint.com/sites/target", listPlan.SiteContentTypes[0].TargetOwnerWebUrl);
        }

        [TestMethod]
        public void ListSemanticDigestExcludesMutableTargetAnalysis()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var source = CreateListSnapshot(siteId, webId, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Items");
            source.SiteContentTypes.Add(new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                Availability = EvidenceAvailability.Captured,
                SourceWebUrl = "https://source.sharepoint.com/sites/source",
                SourceScope = "/sites/source",
                ContentTypeId = "0x0100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                Name = "Custom item",
                ParentContentTypeId = "0x01",
                ParentContentTypeName = "Item"
            });
            source.ContentTypes.Add(new ListContentTypeSnapshot
            {
                Id = "0x0100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA00BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                Name = "Custom item",
                ParentId = "0x0100AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
            });
            var planSet = ListMigrationPlanFactory.Create(new[] { source }, null, CreateTopology(siteId, webId), null, null);
            var listPlan = planSet.Lists.Single();
            var before = listPlan.PlanDigest;
            listPlan.SiteContentTypes[0].DeferredUntilTopologyMaterialization = true;
            listPlan.SiteContentTypes[0].TargetProbe = new ContentTypeTargetProbe { ContentTypeId = "changed" };
            listPlan.SiteContentTypes[0].TargetAdmission = new ContentTypeTargetAdmission
            {
                IsEligible = true,
                Disposition = ContentTypeMaterializationDisposition.CreateOwned
            };
            listPlan.TargetProbe = new ListTargetProbe
            {
                TargetWebExists = true,
                Disposition = ListMaterializationDisposition.ReuseOwned
            };
            ListMigrationPlanFactory.SealTargetAnalysis(planSet);

            Assert.AreEqual(ListMaterializationDisposition.ReuseOwned, listPlan.Disposition);
            Assert.AreEqual(before, ListMigrationPlanFactory.ComputePlanDigest(listPlan));
            Assert.AreEqual(planSet.PlanDigest, ListMigrationPlanFactory.ComputeSetDigest(planSet));
        }

        [TestMethod]
        public void UnconsumedReadOnlyRuntimeListFieldsRemainEvidenceOnly()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var source = CreateListSnapshot(siteId, webId, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Items");
            const string schema = "<Field ID='{44444444-4444-4444-4444-444444444444}' Name='Modified' DisplayName='Modified' Type='DateTime' ReadOnly='TRUE' SourceID='http://schemas.microsoft.com/sharepoint/v3' />";
            source.Fields.Add(new ListFieldSnapshot
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                InternalName = "Modified",
                Title = "Modified",
                TypeAsString = "DateTime",
                SchemaXml = schema,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(schema),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(schema),
                ReadOnly = true,
                FromBaseType = true
            });
            source.Items.Add(new ListItemSnapshot
            {
                SourceItemId = 1,
                Values = new List<ListItemValueSnapshot>
                {
                    new ListItemValueSnapshot { InternalName = "Modified", Kind = ListItemValueKind.DateTime, ScalarValue = "2026-08-31T00:00:00.0000000Z" }
                }
            });
            source.SourceItemCount = 1;

            var plan = ListMigrationPlanFactory.Create(new[] { source }, null, CreateTopology(siteId, webId), null, null);

            Assert.AreEqual(ListFieldMaterializationDisposition.EvidenceOnly, plan.Lists.Single().Fields.Single().Disposition);
        }

        [TestMethod]
        public void UnconsumedListScopedReadOnlyAndSealedFieldsRemainEvidenceOnly()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var listId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var source = CreateListSnapshot(siteId, webId, listId, "Assets");
            var readOnlySchema = $"<Field ID='{{44444444-4444-4444-4444-444444444444}}' Name='GeneratedMetadata' Type='Note' ReadOnly='TRUE' SourceID='{{{listId:D}}}' />";
            var sealedSchema = $"<Field ID='{{55555555-5555-5555-5555-555555555555}}' Name='AssetAuthor' Type='Text' Sealed='TRUE' SourceID='{{{listId:D}}}' />";
            source.Fields.Add(new ListFieldSnapshot
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                InternalName = "GeneratedMetadata",
                Title = "Generated Metadata",
                TypeAsString = "Note",
                SchemaXml = readOnlySchema,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(readOnlySchema),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(readOnlySchema),
                ReadOnly = true
            });
            source.Fields.Add(new ListFieldSnapshot
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                InternalName = "AssetAuthor",
                Title = "Asset Author",
                TypeAsString = "Text",
                SchemaXml = sealedSchema,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(sealedSchema),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(sealedSchema),
                Sealed = true
            });

            var plan = ListMigrationPlanFactory.Create(new[] { source }, null, CreateTopology(siteId, webId), null, null);
            var fields = plan.Lists.Single().Fields.ToDictionary(value => value.InternalName);

            Assert.AreEqual(ListFieldMaterializationDisposition.EvidenceOnly, fields["GeneratedMetadata"].Disposition);
            Assert.AreEqual(ListFieldMaterializationDisposition.EvidenceOnly, fields["AssetAuthor"].Disposition);
        }

        [TestMethod]
        public void RuntimeListFieldCompatibilityPreservesScalarAndCollectionShapes()
        {
            Assert.IsTrue(ListFieldTypeCompatibility.IsCompatibleRuntimeType("Note", "Text"));
            Assert.IsTrue(ListFieldTypeCompatibility.IsCompatibleRuntimeType("Choice", "Text"));
            Assert.IsFalse(ListFieldTypeCompatibility.IsCompatibleRuntimeType("MultiChoice", "Choice"));
            Assert.IsFalse(ListFieldTypeCompatibility.IsCompatibleRuntimeType("UserMulti", "User"));
            Assert.IsFalse(ListFieldTypeCompatibility.IsCompatibleRuntimeType("LookupMulti", "Lookup"));
            Assert.IsFalse(ListFieldTypeCompatibility.IsCompatibleRuntimeType("TaxonomyFieldTypeMulti", "TaxonomyFieldType"));
        }

        [TestMethod]
        public void CalculatedListFieldsArePlannedInFormulaDependencyOrder()
        {
            var siteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var webId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var source = CreateListSnapshot(siteId, webId, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Items");
            var alphaSchema = "<Field ID='{44444444-4444-4444-4444-444444444444}' Name='Alpha' DisplayName='Alpha Result' Type='Calculated' ReadOnly='TRUE'><Formula>=[Zulu Result]+1</Formula></Field>";
            var zuluSchema = "<Field ID='{55555555-5555-5555-5555-555555555555}' Name='Zulu' DisplayName='Zulu Result' Type='Calculated' ReadOnly='TRUE'><Formula>=1</Formula></Field>";
            source.Fields.Add(CalculatedListField(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Alpha", "Alpha Result", alphaSchema));
            source.Fields.Add(CalculatedListField(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Zulu", "Zulu Result", zuluSchema));

            var plan = ListMigrationPlanFactory.Create(new[] { source }, null, CreateTopology(siteId, webId), null, null);
            var fields = plan.Lists.Single().Fields;

            Assert.AreEqual("Zulu", fields[0].InternalName);
            Assert.AreEqual("Alpha", fields[1].InternalName);
        }

        private static ListDependencySnapshot CreateListSnapshot(Guid siteId, Guid webId, Guid listId, string title)
        {
            return new ListDependencySnapshot
            {
                SourceSiteId = siteId,
                SourceWebId = webId,
                SourceWebUrl = "https://source.sharepoint.com/sites/source",
                SourceListId = listId,
                Title = title,
                BaseTemplate = 100,
                BaseType = "GenericList",
                RootFolderServerRelativeUrl = "/sites/source/Lists/" + title,
                Availability = EvidenceAvailability.Captured
            };
        }

        private static ListFieldSnapshot CalculatedListField(Guid id, string internalName, string title, string schema)
        {
            return new ListFieldSnapshot
            {
                Id = id,
                InternalName = internalName,
                Title = title,
                TypeAsString = "Calculated",
                SchemaXml = schema,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(schema),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(schema),
                ReadOnly = true
            };
        }

        private static TopologyPlan CreateTopology(Guid siteId, Guid webId)
        {
            var plan = new TopologyPlan
            {
                SiteCollections = new List<SiteCollectionMappingPlan>
                {
                    new SiteCollectionMappingPlan
                    {
                        SourceSiteId = siteId,
                        TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/target",
                        Webs = new List<WebMappingPlan>
                        {
                            new WebMappingPlan
                            {
                                Kind = TopologyNodeKind.SiteCollectionRoot,
                                SourceSiteId = siteId,
                                SourceWebId = webId,
                                SourceServerRelativeUrl = "/sites/source",
                                TargetWebUrl = "https://target.sharepoint.com/sites/target",
                                TargetServerRelativeUrl = "/sites/target"
                            }
                        }
                    }
                }
            };
            plan.PlanDigest = TopologyPlanner.ComputeDigest(plan);
            return plan;
        }

        private static TopologyTargetAnalysis CreateAdmittedTopologyAnalysis(TopologyPlan topology, Guid siteId, Guid webId)
        {
            return new TopologyTargetAnalysis
            {
                TopologyPlanDigest = topology.PlanDigest,
                SiteCollections = new List<TopologySiteTargetProbe>
                {
                    new TopologySiteTargetProbe
                    {
                        SourceSiteId = siteId,
                        TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/target",
                        Exists = true,
                        Disposition = TopologyMaterializationDisposition.ReuseApprovedHost,
                        Webs = new List<TopologyWebTargetProbe>
                        {
                            new TopologyWebTargetProbe
                            {
                                SourceSiteId = siteId,
                                SourceWebId = webId,
                                TargetWebUrl = "https://target.sharepoint.com/sites/target",
                                Exists = true,
                                Disposition = TopologyMaterializationDisposition.ReuseApprovedHost
                            }
                        }
                    }
                }
            };
        }

        private static PublishingPageCaptureBundle CreateSnapshot()
        {
            var fileUniqueId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var modifiedUtc = new DateTime(2026, 8, 26, 1, 2, 3, DateTimeKind.Utc);
            var pageBytes = Encoding.UTF8.GetBytes(
                "<%@ Page Language=\"C#\" Inherits=\"Microsoft.SharePoint.Publishing.TemplateRedirectionPage, Microsoft.SharePoint.Publishing\" %>");
            var snapshot = new PublishingPageCaptureBundle
            {
                CapturePolicy = new PageCaptureOptions
                {
                    SourcePageServerRelativeUrl = "/sites/source/Pages/source.aspx"
                },
                Source = new PageIdentity
                {
                    SiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    WebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    WebUrl = "https://source.sharepoint.com/sites/source",
                    WebServerRelativeUrl = "/sites/source",
                    PageServerRelativeUrl = "/sites/source/Pages/source.aspx",
                    FileUniqueId = fileUniqueId,
                    ContentTypeId = BuiltInContentTypeId.EnterpriseWikiPage,
                    ContentTypeName = "Enterprise Wiki Page",
                    Title = "Source",
                    Length = pageBytes.LongLength,
                    ModifiedUtc = modifiedUtc,
                    VersionLabel = "0.1"
                },
                PageArtifact = new PageArtifactSnapshot
                {
                    FileUniqueId = fileUniqueId,
                    ServerRelativeUrl = "/sites/source/Pages/source.aspx",
                    Bytes = MigrationArtifact.Describe(pageBytes, "application/vnd.ms-aspx", "source.aspx"),
                    ContentBase64 = Convert.ToBase64String(pageBytes),
                    PageDirective = PageDirectiveParser.Parse(Encoding.UTF8.GetString(pageBytes)),
                    Availability = EvidenceAvailability.Captured
                },
                Layout = CreateStockLayout(),
                PublishingPageContent = "<p>source</p>",
                PublishingPageContentSha256 = PublishingPageDigest.ComputeSha256("<p>source</p>"),
                Fields = new List<PageFieldValueSnapshot>
                {
                    new PageFieldValueSnapshot
                    {
                        Id = Guid.Parse("20d0d2ea-fd8e-4e91-a549-70a48e8932ef"),
                        InternalName = "OOCLReference",
                        Title = "Custom recovery field",
                        TypeAsString = "Text",
                        SchemaXml = "<Field Name=\"OOCLReference\" Type=\"Text\" />",
                        HasValue = true,
                        Kind = PageFieldValueKind.Unsupported,
                        RawType = "Contoso.CustomFieldValue",
                        RawValue = "OOCL-42",
                        RawValueJson = "{\"reference\":\"OOCL-42\"}",
                        CaptureStatus = PageCaptureStatus.CapturedWithLimitations
                    }
                },
                Security = new PageSecuritySnapshot(),
                Lifecycle = new PageLifecycleSnapshot
                {
                    CheckOutType = "Online",
                    Level = "Draft",
                    ModerationStatus = 3
                },
                SourceFence = new SourcePageFence
                {
                    FileUniqueId = fileUniqueId,
                    VersionLabel = "0.1",
                    Length = pageBytes.LongLength,
                    ModifiedUtc = modifiedUtc
                }
            };
            snapshot.Runtime = PageRuntimeResolver.Resolve(
                snapshot.PageArtifact,
                snapshot.Layout.PageDirective,
                snapshot.Source.ContentTypeId);
            snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(
                snapshot.Source,
                snapshot.Layout,
                snapshot.Fields);
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);
            return snapshot;
        }

        private static PageFieldValueSnapshot CreateDanglingTaxonomyField()
        {
            var fieldId = Guid.Parse("e1a5b98c-dd71-426d-acb6-e478c7a5882f");
            var storeId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var setId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
            var termId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
            var catchAllData = string.Join("|",
                Convert.ToBase64String(storeId.ToByteArray()),
                Convert.ToBase64String(setId.ToByteArray()),
                Convert.ToBase64String(termId.ToByteArray()));
            var hidden = new TaxonomyHiddenListEntrySnapshot
            {
                WssId = 42,
                TermStoreId = storeId,
                TermSetId = setId,
                TermId = termId,
                Title = "Retired category",
                CatchAllData = catchAllData,
                CatchAllDataLabel = "Retired category",
                Terms = new List<TaxonomyLocalizedTextSnapshot>
                {
                    new TaxonomyLocalizedTextSnapshot
                    {
                        FieldInternalName = "Term1033",
                        Value = "Retired category"
                    }
                },
                Paths = new List<TaxonomyLocalizedTextSnapshot>
                {
                    new TaxonomyLocalizedTextSnapshot
                    {
                        FieldInternalName = "Path1033",
                        Value = "Retired category"
                    }
                }
            };
            var field = new PageFieldValueSnapshot
            {
                Id = fieldId,
                InternalName = "Wiki_x0020_Page_x0020_Categories",
                Title = "Wiki Categories",
                TypeAsString = "TaxonomyFieldTypeMulti",
                SchemaXml = "<Field Name=\"Wiki_x0020_Page_x0020_Categories\" Type=\"TaxonomyFieldTypeMulti\" />",
                HasValue = true,
                Kind = PageFieldValueKind.TaxonomyCollection,
                CaptureStatus = PageCaptureStatus.Captured,
                TaxonomyBinding = new TaxonomyFieldRelationshipBindingSnapshot
                {
                    FieldId = fieldId,
                    FieldInternalName = "Wiki_x0020_Page_x0020_Categories",
                    TermStoreId = storeId,
                    BoundTermSetId = setId,
                    TextFieldId = Guid.Parse("12345678-1234-1234-1234-123456789012")
                },
                TaxonomyValues = new List<PageTaxonomyValueSnapshot>
                {
                    new PageTaxonomyValueSnapshot
                    {
                        Label = "Retired category",
                        TermGuid = termId.ToString("D"),
                        WssId = 42,
                        Relationship = new TaxonomyValueRelationshipSnapshot
                        {
                            CapturedAtUtc = new DateTimeOffset(2026, 8, 31, 1, 2, 3, TimeSpan.Zero),
                            State = TaxonomyRelationshipState.DanglingTermAbsent,
                            ValueHiddenListEntry = hidden,
                            TaxCatchAllHiddenListEntry = hidden
                        }
                    }
                }
            };
            PageTaxonomyRelationshipProof.Seal(field);
            return field;
        }

        private static void AddDanglingTaxonomyPlan(
            PublishingPageMigrationPackage package,
            PageFieldValueSnapshot field)
        {
            var value = field.TaxonomyValues.Single();
            var termId = Guid.Parse(value.TermGuid);
            var targetStoreId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
            var targetSetId = Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd");
            package.Snapshot.Fields.Add(field);
            package.Snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(
                package.Snapshot.Source,
                package.Snapshot.Layout,
                package.Snapshot.Fields);
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.Plan.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.Plan.PlanningPolicy.TaxonomySchemaMappings.Add(new TaxonomyTargetMapping
            {
                SourceTermStoreId = field.TaxonomyBinding.TermStoreId,
                SourceTermSetId = field.TaxonomyBinding.BoundTermSetId,
                TargetTermStoreId = targetStoreId,
                TargetTermSetId = targetSetId
            });
            package.Plan.FieldActions.Add(new PageFieldAction
            {
                SourceInternalName = field.InternalName,
                TargetInternalName = field.InternalName,
                TargetTypeAsString = field.TypeAsString,
                Disposition = PageFieldDisposition.ApplyTaxonomyRelationships,
                Reason = "Reproduce exact relationship."
            });
            package.Plan.TaxonomyRelationshipActions.Add(new TaxonomyRelationshipAction
            {
                SourceFieldId = field.Id,
                SourceFieldInternalName = field.InternalName,
                SourceTermId = termId,
                SourceWssId = value.WssId,
                SourceEvidenceSha256 = value.Relationship.EvidenceSha256,
                SourceState = TaxonomyRelationshipState.DanglingTermAbsent,
                Disposition = TaxonomyRelationshipDisposition.PreserveDanglingTermAbsent,
                TargetFieldId = field.Id,
                TargetTextFieldId = Guid.Parse("dddddddd-1111-2222-3333-eeeeeeeeeeee"),
                TargetFieldOpen = field.TaxonomyBinding.Open,
                TargetTermStoreId = targetStoreId,
                TargetBoundTermSetId = targetSetId,
                TargetValueHiddenListTermSetId = targetSetId,
                TargetTaxCatchAllHiddenListTermSetId = targetSetId,
                Reason = "Keep the Term absent and reproduce the dangling relationship.",
                VerificationAssertions = new List<string>
                {
                    "The Term remains absent.",
                    "The target-local WssId resolves to the sealed hidden identity."
                }
            });
            package.Plan.IngredientActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                package.Plan.IngredientGraph);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Plan.IngredientGraph,
                package.Plan.IngredientActions);
            package.Plan.MigrationOutcome = evaluation.Outcome;
            package.Plan.IngredientIssues = evaluation.Issues;
            package.Plan.ExecutionFrontier = evaluation.ExecutionFrontier;
            package.State = PublishingPagePackageState.ApprovalReady;
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
        }

        private static PublishingPageLayoutSnapshot CreateStockLayout()
        {
            var bytes = Encoding.UTF8.GetBytes("<%@ Page %><PublishingWebControls:RichHtmlField FieldName=\"PublishingPageContent\" runat=\"server\" />");
            return new PublishingPageLayoutSnapshot
            {
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                Availability = EvidenceAvailability.Captured,
                Url = "https://source.sharepoint.com/_catalogs/masterpage/EnterpriseWiki.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/EnterpriseWiki.aspx",
                FileName = "EnterpriseWiki.aspx",
                CustomizedPageStatus = 1,
                AssociatedContentTypeName = "Enterprise Wiki Page",
                AssociatedContentTypeId = BuiltInContentTypeId.EnterpriseWikiPage,
                Bytes = MigrationArtifact.Describe(bytes, "application/vnd.ms-aspx", "EnterpriseWiki.aspx"),
                ContentBase64 = Convert.ToBase64String(bytes),
                Controls = new List<PublishingPageLayoutControl>
                {
                    new PublishingPageLayoutControl
                    {
                        TagPrefix = "PublishingWebControls",
                        ControlName = "RichHtmlField",
                        FieldName = "PublishingPageContent"
                    }
                }
            };
        }

        private static PublishingPageLayoutSnapshot CreateCustomLayout()
        {
            const string authoredReference = "~site/SiteAssets/Contoso/app.js";
            var layoutBytes = Encoding.UTF8.GetBytes(
                "<%@ Page %><PublishingWebControls:TextField FieldName=\"Activity\" runat=\"server\" /><script src=\""
                + authoredReference
                + "\"></script>");
            var resourceBytes = Encoding.UTF8.GetBytes("console.log('source');");
            var fieldId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var field = Field(
                fieldId,
                "Activity",
                "Text",
                "<Field ID=\"{11111111-1111-1111-1111-111111111111}\" Name=\"Activity\" DisplayName=\"Activity\" Type=\"Text\" />");
            var reference = new PublishingPageLayoutResourceReference
            {
                Attribute = "src",
                Value = authoredReference
            };
            return new PublishingPageLayoutSnapshot
            {
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                Availability = EvidenceAvailability.Captured,
                Url = "https://source.sharepoint.com/sites/source/_catalogs/masterpage/Custom.aspx",
                ServerRelativeUrl = "/sites/source/_catalogs/masterpage/Custom.aspx",
                FileName = "Custom.aspx",
                CustomizedPageStatus = 0,
                AssociatedContentTypeName = "Custom Publishing Page",
                AssociatedContentTypeId = "0x01010007FF3E057FA8AB4AA42FCB67B453FFC001122",
                Bytes = MigrationArtifact.Describe(layoutBytes, "application/vnd.ms-aspx", "Custom.aspx"),
                ContentBase64 = Convert.ToBase64String(layoutBytes),
                Controls = new List<PublishingPageLayoutControl>
                {
                    new PublishingPageLayoutControl
                    {
                        TagPrefix = "PublishingWebControls",
                        ControlName = "TextField",
                        FieldName = "Activity"
                    }
                },
                ResourceReferences = new List<PublishingPageLayoutResourceReference> { reference },
                ResourceArtifacts = new List<PublishingPageLayoutResourceSnapshot>
                {
                    new PublishingPageLayoutResourceSnapshot
                    {
                        Reference = reference,
                        EvidenceState = PublishingPageLayoutResourceEvidenceState.Readable,
                        ResolvedSourceUrl = "https://source.sharepoint.com/sites/source/SiteAssets/Contoso/app.js",
                        Artifact = MigrationArtifact.Describe(resourceBytes, "text/javascript", "app.js"),
                        ContentBase64 = Convert.ToBase64String(resourceBytes)
                    }
                },
                AssociatedContentTypeSchema = new ContentTypeSchemaSnapshot
                {
                    EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                    Availability = EvidenceAvailability.Captured,
                    SourceWebUrl = "https://source.sharepoint.com/sites/source",
                    ContentTypeId = "0x01010007FF3E057FA8AB4AA42FCB67B453FFC001122",
                    Name = "Custom Publishing Page",
                    Description = "Custom page schema",
                    Group = "Custom Content Types",
                    ParentContentTypeId = "0x01010007FF3E057FA8AB4AA42FCB67B453FFC",
                    ParentContentTypeName = "Page",
                    RequiredFieldLinks = new List<ContentTypeFieldLinkSnapshot>
                    {
                        new ContentTypeFieldLinkSnapshot
                        {
                            FieldId = fieldId,
                            Name = "Activity",
                            Role = FieldSchemaRole.DirectBinding
                        }
                    },
                    RequiredFieldClosure = new List<FieldSchemaSnapshot> { field }
                }
            };
        }

        private static PublishingPageLayoutTargetProbe CreateEligibleCustomLayoutProbe(
            PublishingPageLayoutMaterializationPlan plan)
        {
            return new PublishingPageLayoutTargetProbe
            {
                TargetServerRelativeUrl = plan.TargetServerRelativeUrl,
                FileExists = false,
                CanAddAndCustomizePages = true,
                Availability = EvidenceAvailability.Captured,
                ContentTypeSchema = new ContentTypeTargetProbe
                {
                    ContentTypeId = plan.ContentTypeSchema.ContentTypeId,
                    ParentContentTypeAvailable = true,
                    ResolvedParentContentTypeId = plan.ContentTypeSchema.ParentContentTypeId,
                    CanManageContentTypes = true,
                    Availability = EvidenceAvailability.Captured
                },
                Resources = plan.ResourceMaterializations
                    .Where(value => value.Disposition == PublishingPageLayoutResourceMaterializationDisposition.CreateOrReuseOwned)
                    .Select(value => new PublishingPageLayoutResourceTargetProbe
                    {
                        TargetServerRelativeUrl = value.TargetServerRelativeUrl,
                        FileExists = false,
                        CanWrite = true,
                        Availability = EvidenceAvailability.Captured
                    })
                    .ToList()
            };
        }

        private static FieldSchemaSnapshot Field(Guid id, string name, string type, string schemaXml)
        {
            return new FieldSchemaSnapshot
            {
                Id = id,
                InternalName = name,
                Title = name,
                TypeAsString = type,
                SchemaXml = schemaXml,
                SchemaXmlSha256 = MigrationDigest.ComputeSha256(schemaXml),
                PortableSchemaSha256 = FieldSchemaCanonicalizer.PortableDigest(schemaXml),
                Role = FieldSchemaRole.DirectBinding
            };
        }

        [TestMethod]
        public void CompareReportIsDeterministicAndPreservesOneResultPerIngredient()
        {
            var request = CreateCompareRequest();
            var forward = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());
            request.Ingredients = request.Ingredients.Reverse().ToList();
            var reverse = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());
            var replay = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());

            Assert.AreEqual(
                "pass",
                forward.Acceptance.Verdict,
                string.Join(",", forward.Ingredients.Select(value => value.IngredientId + "=" + value.ResultClass)));
            Assert.AreEqual(request.Package.Plan.IngredientGraph.Nodes.Count, forward.Ingredients.Count);
            Assert.IsTrue(forward.Ingredients.Where(value => value.Material).All(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.Exact));
            Assert.IsTrue(forward.Ingredients.Where(value => !value.Material).All(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.Deferred));
            Assert.AreEqual(forward.ReportDigestSha256, reverse.ReportDigestSha256);
            Assert.AreEqual(reverse.ReportDigestSha256, replay.ReportDigestSha256);
            CollectionAssert.AreEqual(
                forward.Ingredients.Select(value => value.IngredientId).ToArray(),
                reverse.Ingredients.Select(value => value.IngredientId).ToArray());
        }

        [TestMethod]
        public void CompareReportRetainsRawDigestsForKnownCanonicalNormalization()
        {
            var request = CreateCompareRequest();
            var observation = ExecutableObservation(request);
            observation.Expected.RawDigestSha256 = Hash("raw-before-normalization");
            observation.Actual.RawDigestSha256 = Hash("raw-after-normalization");
            observation.Expected.CanonicalDigestSha256 = Hash("canonical-form");
            observation.Actual.CanonicalDigestSha256 = Hash("canonical-form");

            var report = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());
            var result = report.Ingredients.Single(value => value.IngredientId == observation.IngredientId);

            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.CanonicalEquivalent, result.ResultClass);
            Assert.AreEqual(Hash("raw-before-normalization"), result.Expected.RawDigestSha256);
            Assert.AreEqual(Hash("raw-after-normalization"), result.Actual.RawDigestSha256);
        }

        [TestMethod]
        public void SourceVersionDriftPrecedesTargetMismatch()
        {
            var request = CreateCompareRequest();
            request.AdmittedPlan.SourceVersion.VersionDigestSha256 = Hash("new-source-version");
            request.SourceVersionComparison.ObservedCompositeDigestSha256 = request.AdmittedPlan.SourceVersion.VersionDigestSha256;
            request.SourceVersionComparison.Status = "changed";
            ResealAdmittedChain(request);
            var observation = ExecutableObservation(request);
            observation.Actual.RawDigestSha256 = Hash("mismatching-target");
            observation.Actual.CanonicalDigestSha256 = Hash("mismatching-target-canonical");

            var report = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());

            Assert.AreEqual("unverified", report.Acceptance.Verdict);
            Assert.IsTrue(report.Ingredients.All(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.SourceVersionChanged));
            Assert.IsFalse(report.Ingredients.Any(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.Mismatch));
        }

        [TestMethod]
        public void PartialTargetEvidenceIsUnknownAndCannotFalseComplete()
        {
            var request = CreateCompareRequest();
            var observation = ExecutableObservation(request);
            observation.TargetEvidenceComplete = false;
            observation.TargetPresent = false;

            var report = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());
            var result = report.Ingredients.Single(value => value.IngredientId == observation.IngredientId);

            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.Unknown, result.ResultClass);
            Assert.AreEqual("unverified", report.Acceptance.Verdict);
        }

        [TestMethod]
        public void ExpiredSourceAuthorizationCannotBecomeMissingOrExact()
        {
            var request = CreateCompareRequest();
            request.SourceAuthState = "expired";

            var report = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());

            Assert.AreEqual("unverified", report.Acceptance.Verdict);
            Assert.IsTrue(report.Ingredients.All(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.AuthExpired));
        }

        [TestMethod]
        public void AuthorizationBlockedDependencyRetainsItsSealedCause()
        {
            var request = CreateCompareRequest();
            var observation = ExecutableObservation(request);
            var decision = request.Package.Plan.ExecutionFrontier.Decisions.Single(value =>
                value.IngredientId == observation.IngredientId);
            decision.State = PageIngredientExecutionState.SkippedByAuthorizationDependency;
            decision.CauseIngredientIds = new List<string> { "ingredient.authorization-owner" };

            var result = PublishingPageCompareReconciler.Classify(request, observation, "passed");

            Assert.AreEqual(PublishingPageCompareContract.ResultClasses.AuthorizationBlocked, result.ResultClass);
            CollectionAssert.AreEqual(
                new[] { "ingredient.authorization-owner" },
                result.Lineage.CauseIngredientIds.ToArray());
        }

        [TestMethod]
        public void MissingRuntimeReceiptIsPendingAndStorageFailureStillWinsOverRuntimePass()
        {
            var pending = CreateCompareRequest(includeRuntimeReceipt: false);
            ExecutableObservation(pending).RuntimeRequirementId = pending.Package.Plan.RuntimeVerification.Requirements.First().Id;
            var pendingReport = PublishingPageCompareReconciler.Reconcile(pending, new PermissiveArtifactStore());
            Assert.AreEqual("pending", pendingReport.Runtime.Status);
            Assert.AreEqual("unverified", pendingReport.Acceptance.Verdict);
            Assert.IsTrue(pendingReport.Ingredients.Any(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.RuntimePending));

            var storageFailure = CreateCompareRequest(storageStatus: StorageVerificationStatus.Failed);
            var failedReport = PublishingPageCompareReconciler.Reconcile(storageFailure, new PermissiveArtifactStore());
            Assert.AreEqual("passed", failedReport.Runtime.Status);
            Assert.AreEqual("failed", failedReport.Storage.Status);
            Assert.AreEqual("fail", failedReport.Acceptance.Verdict);

            var runtimeFailure = CreateCompareRequest(runtimePassed: false);
            var runtimeFailedReport = PublishingPageCompareReconciler.Reconcile(runtimeFailure, new PermissiveArtifactStore());
            Assert.AreEqual("failed", runtimeFailedReport.Runtime.Status);
            Assert.AreEqual("fail", runtimeFailedReport.Acceptance.Verdict);
        }

        [TestMethod]
        public void RuntimeReceiptBindingCoverageAndDigestFailClosed()
        {
            var unbound = CreateCompareRequest();
            unbound.RuntimeReceipt.PlanDigest = Hash("different-plan");
            unbound.RuntimeReceiptDigestSha256 = ContractDigest(unbound.RuntimeReceipt);
            unbound.Bindings.RuntimeReceiptDigestSha256 = unbound.RuntimeReceiptDigestSha256;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(unbound, new PermissiveArtifactStore()));

            var duplicate = CreateCompareRequest();
            duplicate.RuntimeReceipt.Results.Add(duplicate.RuntimeReceipt.Results[0]);
            duplicate.RuntimeReceiptDigestSha256 = ContractDigest(duplicate.RuntimeReceipt);
            duplicate.Bindings.RuntimeReceiptDigestSha256 = duplicate.RuntimeReceiptDigestSha256;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(duplicate, new PermissiveArtifactStore()));

            var tampered = CreateCompareRequest();
            tampered.RuntimeReceipt.Results[0].Message = "changed after sealing";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(tampered, new PermissiveArtifactStore()));
        }

        [TestMethod]
        public void RuntimeEvidenceBytesContextCacheManifestAndRefFailClosed()
        {
            var missingBytes = CreateCompareRequest();
            missingBytes.RuntimeReceipt.Results[0].EvidenceArtifactSha256 = Hash("missing-runtime-bytes");
            missingBytes.RuntimeReceipt.Results[0].EvidenceArtifactLength = 21;
            ResealRuntimeReceipt(missingBytes);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(missingBytes, new PermissiveArtifactStore()));

            var staleBytes = CreateCompareRequest();
            staleBytes.RuntimeReceipt.Results[0].Http.CapturedAtUtc =
                staleBytes.RuntimeReceipt.BrowserContext.CreatedAtUtc.AddSeconds(-1);
            ResealRuntimeReceipt(staleBytes);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(staleBytes, new PermissiveArtifactStore()));

            var foreignBytes = CreateCompareRequest();
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(foreignBytes, new ForeignArtifactStore()));

            var wrongContext = CreateCompareRequest();
            wrongContext.RuntimeReceipt.Results[0].BrowserContextId = "context:foreign";
            ResealRuntimeReceipt(wrongContext);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(wrongContext, new PermissiveArtifactStore()));

            var wrongRef = CreateCompareRequest();
            wrongRef.RuntimeReceipt.ImplementationRef = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            wrongRef.RuntimeReceipt.Results[0].ImplementationRef = wrongRef.RuntimeReceipt.ImplementationRef;
            ResealRuntimeReceipt(wrongRef);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(wrongRef, new PermissiveArtifactStore()));

            var missingCache = CreateCompareRequest();
            missingCache.RuntimeReceipt.Results[0].Cache = null;
            ResealRuntimeReceipt(missingCache);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(missingCache, new PermissiveArtifactStore()));

            var foreignManifest = CreateCompareRequest();
            foreignManifest.RuntimeReceipt.RequirementsManifestDigestSha256 = Hash("foreign-manifest");
            ResealRuntimeReceipt(foreignManifest);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(foreignManifest, new PermissiveArtifactStore()));

            var partial = CreateCompareRequest();
            partial.RuntimeReceipt.Results.Clear();
            ResealRuntimeReceipt(partial);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(partial, new PermissiveArtifactStore()));
        }

        [TestMethod]
        public void AdmittedPlanSourceOperationsAndNativeReceiptsFailClosed()
        {
            var wrongPlan = CreateCompareRequest();
            wrongPlan.AdmittedPlan.PlanDigest = Hash("foreign-plan");
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(wrongPlan, new PermissiveArtifactStore()));

            var wrongSource = CreateCompareRequest();
            wrongSource.SourceVersionComparison.ObservedCompositeDigestSha256 = Hash("stale-source");
            wrongSource.SourceVersionComparison.Status = "changed";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(wrongSource, new PermissiveArtifactStore()));

            var missingOperation = CreateCompareRequest();
            missingOperation.AdmittedPlan.Operations.CleanupOperationId = Guid.Empty;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(missingOperation, new PermissiveArtifactStore()));

            var corruptReceipt = CreateCompareRequest();
            corruptReceipt.ImportReceipt.TargetVersionLabel = "changed-after-sealing";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(corruptReceipt, new PermissiveArtifactStore()));

            var partialReceipt = CreateCompareRequest();
            partialReceipt.ImportReceipt.PartialExecution = true;
            partialReceipt.ImportReceiptDigestSha256 = ContractDigest(partialReceipt.ImportReceipt);
            partialReceipt.Bindings.ImportReceiptDigestSha256 = partialReceipt.ImportReceiptDigestSha256;
            partialReceipt.RuntimeReceipt.ImportReceiptDigestSha256 = partialReceipt.ImportReceiptDigestSha256;
            partialReceipt.RuntimeReceiptDigestSha256 = ContractDigest(partialReceipt.RuntimeReceipt);
            partialReceipt.Bindings.RuntimeReceiptDigestSha256 = partialReceipt.RuntimeReceiptDigestSha256;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(partialReceipt, new PermissiveArtifactStore()));
        }

        [TestMethod]
        public void DuplicateOrMissingIngredientRowsFailClosed()
        {
            var duplicate = CreateCompareRequest();
            duplicate.Ingredients.Add(duplicate.Ingredients[0]);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(duplicate, new PermissiveArtifactStore()));

            var missing = CreateCompareRequest();
            missing.Ingredients.RemoveAt(0);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(missing, new PermissiveArtifactStore()));
        }

        [TestMethod]
        public void AssessmentActualShapeIsRevisionBoundWithoutInventingDiscoveryClaims()
        {
            var request = CreateCompareRequest();

            var report = PublishingPageCompareReconciler.Reconcile(request, new PermissiveArtifactStore());
            var json = MigrationContractSerializer.SerializeCanonical(report);
            var handoffJson = MigrationContractSerializer.SerializeCanonical(request.AssessmentHandoff);
            var roundTrip = MigrationContractSerializer.Deserialize<AssessmentCaptureHandoff>(handoffJson);

            Assert.AreEqual(PublishingPageCompareContract.AssessmentHandoffSchemaVersion, report.AssessmentHandoff.SchemaVersion);
            Assert.AreEqual(PublishingPageCompareContract.AssessmentProducerRevisionId, report.AssessmentHandoff.ProducerRevisionId);
            Assert.AreEqual(PublishingPageCompareContract.AssessmentConformanceRevisionId, report.AssessmentHandoff.ConformanceRevisionId);
            Assert.AreEqual(request.AssessmentHandoffDigestSha256, report.AssessmentHandoff.HandoffDigestSha256);
            Assert.AreEqual(PublishingPageCompareContract.UnavailableByProducerContract, report.AssessmentHandoff.DiscoveryObservationStatus);
            Assert.AreEqual(PublishingPageCompareContract.UnavailableByProducerContract, report.AssessmentHandoff.ProducerAttestationStatus);
            Assert.AreEqual(PublishingPageCompareContract.UnavailableByProducerContract, report.AssessmentHandoff.CoverageStatus);
            Assert.IsFalse(json.Contains(request.AssessmentHandoff.CanonicalLocatorRef));
            Assert.IsFalse(json.Contains("discoveryObservationId"));
            Assert.IsTrue(handoffJson.Contains("\"manifest_revision_id\""));
            Assert.IsTrue(handoffJson.Contains("\"request_policy\""));
            Assert.AreEqual(request.AssessmentHandoff.SampleId, roundTrip.SampleId);
        }

        [TestMethod]
        public void AssessmentMissingUnsupportedOrTamperedProvenanceFailsClosed()
        {
            var missing = CreateCompareRequest();
            missing.AssessmentHandoff.SampleId = null;
            missing.AssessmentHandoffDigestSha256 = ContractDigest(missing.AssessmentHandoff);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(missing, new PermissiveArtifactStore()));

            var unsupported = CreateCompareRequest();
            unsupported.AssessmentHandoff.Schema = "ccd-aspx-capture-handoff/2.0.0";
            unsupported.AssessmentHandoffDigestSha256 = ContractDigest(unsupported.AssessmentHandoff);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(unsupported, new PermissiveArtifactStore()));

            var wrongRevision = CreateCompareRequest();
            wrongRevision.AssessmentProducerRevisionId = Guid.Empty.ToString();
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(wrongRevision, new PermissiveArtifactStore()));

            var tampered = CreateCompareRequest();
            tampered.AssessmentHandoff.StratumId = "S2-after-seal";
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageCompareReconciler.Reconcile(tampered, new PermissiveArtifactStore()));
        }

        [TestMethod]
        public void AssessmentDeniedAndUnknownPermissionSignalsCannotFalseComplete()
        {
            var denied = CreateCompareRequest();
            denied.AssessmentHandoff.PermissionSignal = "known_forbidden";
            denied.AssessmentHandoffDigestSha256 = ContractDigest(denied.AssessmentHandoff);
            var deniedReport = PublishingPageCompareReconciler.Reconcile(denied, new PermissiveArtifactStore());
            Assert.AreEqual("conditional", deniedReport.Acceptance.Verdict);
            Assert.IsTrue(deniedReport.Ingredients.All(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.AuthorizationBlocked));

            var unknown = CreateCompareRequest();
            unknown.AssessmentHandoff.PermissionSignal = "inherited_unknown";
            unknown.AssessmentHandoffDigestSha256 = ContractDigest(unknown.AssessmentHandoff);
            var unknownReport = PublishingPageCompareReconciler.Reconcile(unknown, new PermissiveArtifactStore());
            Assert.AreEqual("unverified", unknownReport.Acceptance.Verdict);
            Assert.IsTrue(unknownReport.Ingredients.All(value =>
                value.ResultClass == PublishingPageCompareContract.ResultClasses.Unknown));
        }

        [TestMethod]
        public void ActualArchiveIntakeAdmitsCcd35OracleAndPreservesBothLocatorDigests()
        {
            var raw = ReadActualArchiveFixture();

            var projection = PublishingPageActualArchiveIntake.AdmitCanonicalBundle(
                raw,
                CreateActualArchiveBindings());

            Assert.AreEqual("ccd35-03", projection.CaseId);
            Assert.AreEqual("Wiki Page", projection.Family);
            Assert.AreEqual(5413, projection.RawBundleLength);
            Assert.AreEqual("2730972faf1e81d9f9134e07568f87e65b80ded28a9da9ffcbbc51ccbc0354f6", projection.RawBundleDigestSha256);
            Assert.AreEqual(3980, projection.CanonicalLength);
            Assert.AreEqual("cd0805a08b1480b62537019d5b2d516b2c6f4ea54138f5a6401c790c01b86d88", projection.CanonicalDigestSha256);
            Assert.AreEqual("6d72a70363112b758d0e6e5df04be9a601b281b5ca914f9ee7a5d401a87bf0f6", projection.RawLocatorDigestSha256);
            Assert.AreEqual("22c8b73670cf11249b150bdd0ae55e31f1bc3817e0d5d34dea90bd36bf6f9af1", projection.TransportLocatorDigestSha256);
            Assert.AreNotEqual(projection.RawLocatorDigestSha256, projection.TransportLocatorDigestSha256);
            Assert.AreEqual(PublishingPageCompareContract.UnavailableByProducerContract, projection.DiscoveryObservationStatus);
            Assert.AreEqual(PublishingPageCompareContract.UnavailableByProducerContract, projection.ProducerAttestationStatus);
            Assert.AreEqual(PublishingPageCompareContract.UnavailableByProducerContract, projection.CoverageStatus);
            Assert.AreEqual(5, projection.Ingredients.Count);
            Assert.IsTrue(projection.Ingredients.Any(value =>
                value.IngredientId == "node:wiki-content"
                && value.EvidenceDigestSha256 == "deed1ebc8fac43e0a9af5e2a740dd2c159f15aa8f0589fa6bc502d9e1615b12d"));
        }

        [TestMethod]
        public void ActualArchiveIntakeRejectsRawRewriteBeforeSemanticNormalization()
        {
            var raw = ReadActualArchiveFixture();
            var rewritten = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw).Replace("  \"schema\"", " \"schema\""));

            Assert.AreEqual(5412, rewritten.Length);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageActualArchiveIntake.AdmitCanonicalBundle(
                    rewritten,
                    CreateActualArchiveBindings()));
        }

        [TestMethod]
        public void ActualArchiveIntakeRejectsSemanticTamperEvenWhenRawBindingIsResealed()
        {
            var raw = ReadActualArchiveFixture();
            var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw).Replace("ccd35-03", "ccd35-04"));
            var bindings = CreateActualArchiveBindings();
            bindings.ExpectedCaseId = "ccd35-04";
            bindings.RawBundleDigestSha256 = MigrationDigest.ComputeSha256(tampered);

            Assert.AreEqual(raw.Length, tampered.Length);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageActualArchiveIntake.AdmitCanonicalBundle(tampered, bindings));
        }

        [TestMethod]
        public void ActualArchiveIntakeRejectsMissingOrOverwrittenLocatorLineage()
        {
            var raw = ReadActualArchiveFixture();
            var missing = CreateActualArchiveBindings();
            missing.RawLocatorDigestSha256 = null;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageActualArchiveIntake.AdmitCanonicalBundle(raw, missing));

            var overwritten = CreateActualArchiveBindings();
            overwritten.TransportLocatorDigestSha256 = overwritten.RawLocatorDigestSha256;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageActualArchiveIntake.AdmitCanonicalBundle(raw, overwritten));
        }

        [TestMethod]
        public void ActualArchiveReceiptRequiresIndependentlyDeserializedNativeReceipts()
        {
            var captures = new[]
            {
                AdmitActualCapture("ccd35-03"),
                AdmitActualCapture("ccd35-06"),
                AdmitActualCapture("ccd35-08")
            };
            var rawReceipt = ReadMigrationResource("ccd35-live-thin-slice-final-receipt.actual.json");
            var nativeReceipts = CreateActualArchiveNativeReceipts(captures);

            var result = PublishingPageActualArchiveIntake.AdmitThinSliceReceipt(
                rawReceipt,
                new ActualArchiveReceiptBindings
                {
                    RawReceiptDigestSha256 = "3740a146a66eb0f9c1a1e2d99f882d9906f36e4fa4a4be930c940cff70980e5b",
                    RawReceiptLength = 22413,
                    SourceArchiveDigestSha256 = "788b0d54f8a4e5521dcd5060db732f2f0fcd4d8b1ac759e777ad3ec0eb9956ef",
                    SourceArchiveLength = 38702,
                    ExpectedTargetOrigin = "https://a830edad9050849cupcollect.sharepoint.com"
                },
                captures,
                nativeReceipts);
            var replay = PublishingPageActualArchiveIntake.AdmitThinSliceReceipt(
                rawReceipt,
                new ActualArchiveReceiptBindings
                {
                    RawReceiptDigestSha256 = "3740a146a66eb0f9c1a1e2d99f882d9906f36e4fa4a4be930c940cff70980e5b",
                    RawReceiptLength = 22413,
                    SourceArchiveDigestSha256 = "788b0d54f8a4e5521dcd5060db732f2f0fcd4d8b1ac759e777ad3ec0eb9956ef",
                    SourceArchiveLength = 38702,
                    ExpectedTargetOrigin = "https://a830edad9050849cupcollect.sharepoint.com"
                },
                captures.Reverse(),
                nativeReceipts.AsEnumerable().Reverse());

            Assert.AreEqual(PublishingPageActualArchiveIntake.ReconciliationProjectionSchemaVersion, result.SchemaVersion);
            Assert.AreEqual(MigrationContractSerializer.SerializeCanonical(result), MigrationContractSerializer.SerializeCanonical(replay));
            Assert.AreEqual("conditional", result.Verdict);
            Assert.AreEqual("native_deserialized", result.NativeImportReceiptStatus);
            Assert.AreEqual("native_deserialized", result.NativeRuntimeReceiptStatus);
            Assert.AreEqual(3, result.Cases.Count);
            Assert.AreEqual(2, result.Cases.Count(value => value.IngredientVerdict == "pass"));
            Assert.AreEqual(1, result.Cases.Count(value => value.IngredientVerdict == "conditional"));
            Assert.IsTrue(result.Cases.All(value => value.StorageStatus == "passed" && value.RuntimeStatus == "passed"));
            Assert.IsTrue(result.Cases.Single(value => value.CaseId == "ccd35-06").Reason.Contains("BaseTemplate 101"));
        }

        [TestMethod]
        public void ActualArchiveReceiptRejectsResealedStorageMismatch()
        {
            var captures = new[]
            {
                AdmitActualCapture("ccd35-03"),
                AdmitActualCapture("ccd35-06"),
                AdmitActualCapture("ccd35-08")
            };
            var rawReceipt = ReadMigrationResource("ccd35-live-thin-slice-final-receipt.actual.json");
            var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(rawReceipt).Replace(
                "\"actualSha256\": \"d0a48786489fb7cfc244b423ec32f4767e0e8aea8d200e97ea0f44174caffb6a\"",
                "\"actualSha256\": \"0000000000000000000000000000000000000000000000000000000000000000\""));
            var bindings = new ActualArchiveReceiptBindings
            {
                RawReceiptDigestSha256 = MigrationDigest.ComputeSha256(tampered),
                RawReceiptLength = tampered.Length,
                SourceArchiveDigestSha256 = "788b0d54f8a4e5521dcd5060db732f2f0fcd4d8b1ac759e777ad3ec0eb9956ef",
                SourceArchiveLength = 38702,
                ExpectedTargetOrigin = "https://a830edad9050849cupcollect.sharepoint.com"
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageActualArchiveIntake.AdmitThinSliceReceipt(
                    tampered,
                    bindings,
                    captures,
                    CreateActualArchiveNativeReceipts(captures)));
        }

        [TestMethod]
        public void ActualArchiveReceiptRejectsMissingOrCorruptNativeReceipt()
        {
            var captures = new[]
            {
                AdmitActualCapture("ccd35-03"),
                AdmitActualCapture("ccd35-06"),
                AdmitActualCapture("ccd35-08")
            };
            var rawReceipt = ReadMigrationResource("ccd35-live-thin-slice-final-receipt.actual.json");
            var bindings = new ActualArchiveReceiptBindings
            {
                RawReceiptDigestSha256 = "3740a146a66eb0f9c1a1e2d99f882d9906f36e4fa4a4be930c940cff70980e5b",
                RawReceiptLength = 22413,
                SourceArchiveDigestSha256 = "788b0d54f8a4e5521dcd5060db732f2f0fcd4d8b1ac759e777ad3ec0eb9956ef",
                SourceArchiveLength = 38702,
                ExpectedTargetOrigin = "https://a830edad9050849cupcollect.sharepoint.com"
            };

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageActualArchiveIntake.AdmitThinSliceReceipt(
                    rawReceipt,
                    bindings,
                    captures,
                    CreateActualArchiveNativeReceipts(captures).Take(2)));

            var corrupt = CreateActualArchiveNativeReceipts(captures);
            corrupt[0].RuntimeReceiptJson[0] ^= 0x01;
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageActualArchiveIntake.AdmitThinSliceReceipt(rawReceipt, bindings, captures, corrupt));
        }

        private static byte[] ReadActualArchiveFixture()
        {
            return ReadMigrationResource("ccd35-03-canonical-bundle.actual.json");
        }

        private static byte[] ReadMigrationResource(string fileName)
        {
            return System.IO.File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                "Migration",
                fileName));
        }

        private static ActualArchiveCaptureProjection AdmitActualCapture(string caseId)
        {
            var bindings = caseId switch
            {
                "ccd35-03" => CreateActualArchiveBindings(),
                "ccd35-06" => new ActualArchiveCaptureBindings
                {
                    ExpectedCaseId = caseId,
                    RawBundleDigestSha256 = "9f3900ba51ca0ed3e27ac7199eb0039ee4890e90b16b51f6aca0674efc576184",
                    RawBundleLength = 6348,
                    RawLocatorDigestSha256 = "cae0ebe1e31d82c980dc34f907368b8d1439275cb774bfdfc99273d62eb01d75",
                    TransportLocatorDigestSha256 = "cae0ebe1e31d82c980dc34f907368b8d1439275cb774bfdfc99273d62eb01d75",
                    ExpectedCaptureReportDigestSha256 = "c701f00950d3bb162a8dead17bcc297c8e0dd0964c9fafce1e58bf3b3b9edc59"
                },
                "ccd35-08" => new ActualArchiveCaptureBindings
                {
                    ExpectedCaseId = caseId,
                    RawBundleDigestSha256 = "4d6a584da895176b3b2ea1e24b2777e99b8dd63b51ce89eebf412906db03331b",
                    RawBundleLength = 5409,
                    RawLocatorDigestSha256 = "6592e68e17dc4deb3bdbe292716a26cf251759a6cd7e1d24f3908a14a7c3c788",
                    TransportLocatorDigestSha256 = "b87657506f7c2e4ba3007e174987d07c01feab6d087c758f4c37d80ae46137ce",
                    ExpectedCaptureReportDigestSha256 = "c701f00950d3bb162a8dead17bcc297c8e0dd0964c9fafce1e58bf3b3b9edc59"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(caseId))
            };
            return PublishingPageActualArchiveIntake.AdmitCanonicalBundle(
                ReadMigrationResource(caseId + "-canonical-bundle.actual.json"),
                bindings);
        }

        private static ActualArchiveCaptureBindings CreateActualArchiveBindings()
        {
            return new ActualArchiveCaptureBindings
            {
                ExpectedCaseId = "ccd35-03",
                RawBundleDigestSha256 = "2730972faf1e81d9f9134e07568f87e65b80ded28a9da9ffcbbc51ccbc0354f6",
                RawBundleLength = 5413,
                RawLocatorDigestSha256 = "6d72a70363112b758d0e6e5df04be9a601b281b5ca914f9ee7a5d401a87bf0f6",
                TransportLocatorDigestSha256 = "22c8b73670cf11249b150bdd0ae55e31f1bc3817e0d5d34dea90bd36bf6f9af1",
                ExpectedCaptureReportDigestSha256 = "c701f00950d3bb162a8dead17bcc297c8e0dd0964c9fafce1e58bf3b3b9edc59"
            };
        }

        private static List<ActualArchiveNativeReceiptSet> CreateActualArchiveNativeReceipts(
            IEnumerable<ActualArchiveCaptureProjection> captures)
        {
            var targetPaths = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ccd35-03"] = "/teams/mswikis-gbi/Getfit/Getfit Program/Home.aspx",
                ["ccd35-06"] = "/teams/campusipkits/industryipkitcybersecurity/Pages/Settings.aspx",
                ["ccd35-08"] = "/teams/office_rdx/rm/SitePages/Mac Office Release Wiki.aspx"
            };
            var index = 0;
            return captures.OrderBy(value => value.CaseId, StringComparer.Ordinal).Select(capture =>
            {
                index++;
                var operations = new ReproOperationIds
                {
                    MutationOperationId = Guid.Parse($"20000000-0000-0000-0000-{index:000000000000}"),
                    ReadbackOperationId = Guid.Parse($"30000000-0000-0000-0000-{index:000000000000}"),
                    RuntimeOperationId = Guid.Parse($"40000000-0000-0000-0000-{index:000000000000}"),
                    CleanupOperationId = Guid.Parse($"50000000-0000-0000-0000-{index:000000000000}")
                };
                var sourceVersion = new CurrentSourceVersionIdentity
                {
                    IdentityDigestSha256 = capture.RawLocatorDigestSha256,
                    VersionDigestSha256 = capture.CanonicalDigestSha256,
                    ETag = "\"" + capture.CaseId + ",1\"",
                    LastModifiedUtc = new DateTimeOffset(2026, 9, 8, 12, index, 0, TimeSpan.Zero),
                    VersionLabel = "1.0",
                    ObservedAtUtc = new DateTimeOffset(2026, 9, 9, 5, index, 0, TimeSpan.Zero)
                };
                var targetIdentity = "https://a830edad9050849cupcollect.sharepoint.com" + targetPaths[capture.CaseId];
                var admittedPlan = new AdmittedReproExecutionPlan
                {
                    PlanDigest = Hash("live-plan:" + capture.CaseId),
                    TargetIdentity = targetIdentity,
                    SourceVersion = sourceVersion,
                    Operations = operations
                };
                var admittedPlanDigest = ContractDigest(admittedPlan);
                var importReceipt = new PublishingPageImportReceipt
                {
                    OperationId = operations.MutationOperationId,
                    AdmittedPlanDigestSha256 = admittedPlanDigest,
                    SourceVersion = sourceVersion,
                    Operations = operations,
                    ExecutionStatus = MigrationExecutionStatus.Succeeded,
                    PartialExecution = false,
                    ApprovedPlanDigest = admittedPlan.PlanDigest,
                    TargetWebUrl = "https://a830edad9050849cupcollect.sharepoint.com",
                    TargetPageServerRelativeUrl = targetPaths[capture.CaseId],
                    FreshReadbackPassed = true,
                    StorageVerificationStatus = StorageVerificationStatus.Passed,
                    RuntimeVerificationStatus = RuntimeVerificationStatus.Passed
                };
                var importJson = Encoding.UTF8.GetBytes(MigrationContractSerializer.SerializeCanonical(importReceipt));
                var importDigest = ContractDigest(importReceipt);
                var manifest = new RuntimeVerificationManifest
                {
                    Requirements = new List<RuntimeVerificationRequirement>
                    {
                        new RuntimeVerificationRequirement { Id = "runtime:" + capture.CaseId, Required = true }
                    }
                };
                var runtimeReceipt = RuntimeVerificationReceiptFactory.Create(
                    admittedPlan,
                    admittedPlanDigest,
                    importDigest,
                    targetIdentity,
                    manifest,
                    "e1b500da5437e2f78c7eff8ef7a5374310c4d978",
                    CreateRuntimeBrowserContext(
                        "context:" + capture.CaseId,
                        "target:" + capture.CaseId,
                        new DateTimeOffset(2026, 9, 9, 5, 30 + index, 0, TimeSpan.Zero)),
                    new[]
                    {
                        new RuntimeVerificationResult
                        {
                            RequirementId = "runtime:" + capture.CaseId,
                            Passed = true,
                            EvidenceArtifactSha256 = Hash("runtime-evidence:" + capture.CaseId),
                            EvidenceArtifactLength = Encoding.UTF8.GetByteCount("runtime-evidence:" + capture.CaseId),
                            EvidenceArtifactLocator = "runtime/" + capture.CaseId + ".html",
                            ImplementationRef = "e1b500da5437e2f78c7eff8ef7a5374310c4d978",
                            BrowserContextId = "context:" + capture.CaseId,
                            Http = CreateRuntimeHttpEvidence(
                                targetIdentity,
                                "headers:" + capture.CaseId,
                                new DateTimeOffset(2026, 9, 9, 5, 30 + index, 0, TimeSpan.Zero)),
                            Cache = CreateRuntimeCacheEvidence(),
                            DomProbeArtifactSha256 = Hash("runtime-dom:" + capture.CaseId),
                            DomProbeArtifactLength = Encoding.UTF8.GetByteCount("runtime-dom:" + capture.CaseId),
                            DomProbeArtifactLocator = "runtime/" + capture.CaseId + ".dom.json"
                        }
                    },
                    new DateTimeOffset(2026, 9, 9, 5, 30 + index, 0, TimeSpan.Zero));
                var runtimeJson = Encoding.UTF8.GetBytes(MigrationContractSerializer.SerializeCanonical(runtimeReceipt));
                return new ActualArchiveNativeReceiptSet
                {
                    CaseId = capture.CaseId,
                    AdmittedPlan = admittedPlan,
                    AdmittedPlanDigestSha256 = admittedPlanDigest,
                    ImportReceiptJson = importJson,
                    ImportReceiptDigestSha256 = importDigest,
                    RuntimeReceiptJson = runtimeJson,
                    RuntimeReceiptDigestSha256 = ContractDigest(runtimeReceipt)
                };
            }).ToList();
        }

        private static PublishingPageCompareRequest CreateCompareRequest(
            bool includeRuntimeReceipt = true,
            StorageVerificationStatus storageStatus = StorageVerificationStatus.Passed,
            bool runtimePassed = true)
        {
            var package = CreateMigrationPackage(includeDeferredField: false);
            var fileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var canonicalTarget = "https://target.sharepoint.com/sites/target/pages/source.aspx";
            var operations = new ReproOperationIds
            {
                MutationOperationId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                ReadbackOperationId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                RuntimeOperationId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                CleanupOperationId = Guid.Parse("10000000-0000-0000-0000-000000000004")
            };
            var sourceVersion = new CurrentSourceVersionIdentity
            {
                IdentityDigestSha256 = Hash("source-identity"),
                VersionDigestSha256 = Hash("source-version"),
                ETag = "\"source-etag,1\"",
                LastModifiedUtc = new DateTimeOffset(2026, 9, 8, 11, 55, 0, TimeSpan.Zero),
                VersionLabel = "7.0",
                ObservedAtUtc = new DateTimeOffset(2026, 9, 8, 11, 59, 0, TimeSpan.Zero)
            };
            var admittedPlan = new AdmittedReproExecutionPlan
            {
                PlanDigest = package.PlanDigest,
                TargetIdentity = canonicalTarget,
                SourceVersion = sourceVersion,
                Operations = operations
            };
            var admittedPlanDigest = ContractDigest(admittedPlan);
            var importReceiptValue = new PublishingPageImportReceipt
            {
                OperationId = operations.MutationOperationId,
                AdmittedPlanDigestSha256 = admittedPlanDigest,
                SourceVersion = sourceVersion,
                Operations = operations,
                ApprovedPlanDigest = package.PlanDigest,
                TargetWebUrl = package.Plan.TargetWebUrl,
                TargetPageServerRelativeUrl = package.Plan.TargetPageServerRelativeUrl,
                TargetFileUniqueId = fileId,
                TargetListItemId = 42,
                TargetVersionLabel = "1.0",
                ExecutionStatus = MigrationExecutionStatus.Succeeded,
                PartialExecution = false,
                FreshReadbackPassed = storageStatus == StorageVerificationStatus.Passed,
                StorageVerificationStatus = storageStatus,
                RuntimeVerificationStatus = includeRuntimeReceipt
                    ? (runtimePassed ? RuntimeVerificationStatus.Passed : RuntimeVerificationStatus.Failed)
                    : RuntimeVerificationStatus.Pending
            };
            var importReceipt = MigrationContractSerializer.Deserialize<PublishingPageImportReceipt>(
                MigrationContractSerializer.SerializeCanonical(importReceiptValue));
            var importDigest = ContractDigest(importReceipt);
            RuntimeVerificationReceipt runtimeReceipt = null;
            string runtimeDigest = null;
            if (includeRuntimeReceipt)
            {
                var producedRuntimeReceipt = RuntimeVerificationReceiptFactory.Create(
                    admittedPlan,
                    admittedPlanDigest,
                    importDigest,
                    canonicalTarget,
                    package.Plan.RuntimeVerification,
                    "2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c",
                    CreateRuntimeBrowserContext(
                        "context:compare",
                        "target:compare",
                        new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
                    package.Plan.RuntimeVerification.Requirements.Select(requirement =>
                        new RuntimeVerificationResult
                        {
                            RequirementId = requirement.Id,
                            Passed = runtimePassed,
                            EvidenceArtifactSha256 = Hash("runtime-evidence:" + requirement.Id),
                            EvidenceArtifactLength = Encoding.UTF8.GetByteCount("runtime-evidence:" + requirement.Id),
                            EvidenceArtifactLocator = "runtime/" + requirement.Id + ".html",
                            ImplementationRef = "2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c",
                            BrowserContextId = "context:compare",
                            Http = CreateRuntimeHttpEvidence(
                                canonicalTarget,
                                "headers:" + requirement.Id,
                                new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
                            Cache = CreateRuntimeCacheEvidence(),
                            DomProbeArtifactSha256 = Hash("runtime-dom:" + requirement.Id),
                            DomProbeArtifactLength = Encoding.UTF8.GetByteCount("runtime-dom:" + requirement.Id),
                            DomProbeArtifactLocator = "runtime/" + requirement.Id + ".dom.json",
                            Message = "synthetic fixed evidence"
                        }),
                    new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
                runtimeReceipt = MigrationContractSerializer.Deserialize<RuntimeVerificationReceipt>(
                    MigrationContractSerializer.SerializeCanonical(producedRuntimeReceipt));
                runtimeDigest = ContractDigest(runtimeReceipt);
            }

            var request = new PublishingPageCompareRequest
            {
                GeneratedAtUtc = new DateTimeOffset(2026, 9, 8, 12, 5, 0, TimeSpan.Zero),
                Producer = new CompareProducer
                {
                    Id = "pnp-compare",
                    Version = "1.0.0-test",
                    ImplementationRef = "2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c"
                },
                AssessmentHandoff = CreateAssessmentHandoff(),
                AssessmentProducerRevisionId = PublishingPageCompareContract.AssessmentProducerRevisionId,
                Package = package,
                AdmittedPlan = admittedPlan,
                AdmittedPlanDigestSha256 = admittedPlanDigest,
                ImportReceipt = importReceipt,
                ImportReceiptDigestSha256 = importDigest,
                RuntimeReceipt = runtimeReceipt,
                RuntimeReceiptDigestSha256 = runtimeDigest,
                Bindings = new CompareBindings
                {
                    ManifestDigestSha256 = Hash("manifest"),
                    SourceCaptureReceiptDigestSha256 = Hash("capture-receipt"),
                    ExportSchemaVersion = package.ExportSchemaVersion,
                    SnapshotDigestSha256 = package.SnapshotDigest,
                    IngredientGraphSchemaVersion = package.Plan.IngredientGraph.SchemaVersion,
                    IngredientProjectionVersion = package.Plan.IngredientGraph.ProjectionVersion,
                    MigrationPackageSchemaVersion = package.SchemaVersion,
                    PlanDigestSha256 = package.PlanDigest,
                    AdmittedPlanDigestSha256 = admittedPlanDigest,
                    SourceVersionDigestSha256 = sourceVersion.VersionDigestSha256,
                    Operations = operations,
                    ImportReceiptSchemaVersion = importReceipt.SchemaVersion,
                    ImportReceiptDigestSha256 = importDigest,
                    RuntimeReceiptSchemaVersion = runtimeReceipt?.SchemaVersion,
                    RuntimeReceiptDigestSha256 = runtimeDigest
                },
                SourceVersionComparison = new SourceVersionComparison
                {
                    IdentityDigestSha256 = sourceVersion.IdentityDigestSha256,
                    ExpectedCompositeDigestSha256 = Hash("source-version"),
                    ObservedCompositeDigestSha256 = Hash("source-version"),
                    ObservedETag = sourceVersion.ETag,
                    ObservedLastModifiedUtc = sourceVersion.LastModifiedUtc,
                    ObservedVersionLabel = sourceVersion.VersionLabel,
                    ObservedAtUtc = sourceVersion.ObservedAtUtc,
                    Status = "matched"
                },
                PlannedTargetIdentity = new CompareTargetIdentity
                {
                    WebUrlHashSha256 = Hash(package.Plan.TargetWebUrl.ToLowerInvariant()),
                    PageServerRelativeUrlHashSha256 = Hash(package.Plan.TargetPageServerRelativeUrl.ToLowerInvariant()),
                    FileUniqueId = fileId,
                    ListItemId = 42,
                    VersionLabel = "1.0",
                    CanonicalIdentity = canonicalTarget
                }
            };
            request.AssessmentHandoffDigestSha256 = ContractDigest(request.AssessmentHandoff);
            request.Ingredients = package.Plan.IngredientGraph.Nodes.Select(node =>
            {
                var action = package.Plan.IngredientActions.SingleOrDefault(value => value.IngredientId == node.Id);
                var executionState = package.Plan.ExecutionFrontier.GetState(node.Id);
                var digest = Hash("ingredient:" + node.Id);
                return new IngredientCompareObservation
                {
                    IngredientId = node.Id,
                    Kind = node.Kind.ToString(),
                    Material = executionState == PageIngredientExecutionState.Executable,
                    RuntimeRequirementId = node.RuntimeRequirement,
                    Lineage = new IngredientCompareLineage
                    {
                        RequestedUrlHashSha256 = Hash("requested-url"),
                        SourceArtifactDigestSha256 = Hash("source-artifact:" + node.Id),
                        SourceIngredientId = node.Id,
                        ActionId = action?.ActionId,
                        TargetIdentity = action?.TargetIdentity,
                        EvidenceRefs = new List<string> { "sha256:" + Hash("evidence:" + node.Id) }
                    },
                    Expected = new CompareDigestPair { RawDigestSha256 = digest, CanonicalDigestSha256 = digest },
                    Actual = new CompareDigestPair { RawDigestSha256 = digest, CanonicalDigestSha256 = digest }
                };
            }).ToList();
            return request;
        }

        private static AssessmentCaptureHandoff CreateAssessmentHandoff()
        {
            return new AssessmentCaptureHandoff
            {
                Schema = PublishingPageCompareContract.AssessmentHandoffSchemaVersion,
                ManifestRevisionId = "sha256:" + Hash("assessment-manifest-revision"),
                AllowlistRevisionId = "sha256:" + Hash("assessment-allowlist-revision"),
                SampleId = "A1-001",
                StageMembership = new List<string> { "A0", "A1" },
                CanonicalLocatorRef = "restricted://inventory/A1-001",
                CanonicalLocatorHash = "sha256:" + Hash("assessment-canonical-locator"),
                ResourceIdentityHash = "sha256:" + Hash("assessment-resource-identity"),
                ApprovedHostHash = "hmac-sha256:" + Hash("assessment-approved-host"),
                StratumId = "S1-known-classic",
                ExpectedProfile = "publishing_page",
                PermissionSignal = "known_readable",
                RedirectSignal = "none_observed",
                UnknownSignals = new List<string>(),
                RequestPolicy = new AssessmentCaptureRequestPolicy
                {
                    Methods = new List<string> { "HEAD", "GET" },
                    AutoDiscovery = false,
                    FollowRedirects = "same-approved-host-only",
                    MaxRedirects = 3,
                    MutationAllowed = false
                }
            };
        }

        private static IngredientCompareObservation ExecutableObservation(PublishingPageCompareRequest request)
        {
            return request.Ingredients.First(value =>
            {
                var state = request.Package.Plan.ExecutionFrontier.GetState(value.IngredientId);
                return state != PageIngredientExecutionState.Deferred
                    && state != PageIngredientExecutionState.SkippedByDeferredDependency
                    && state != PageIngredientExecutionState.AuthorizationBlocked
                    && state != PageIngredientExecutionState.SkippedByAuthorizationDependency;
            });
        }

        private static void ResealAdmittedChain(PublishingPageCompareRequest request)
        {
            request.AdmittedPlanDigestSha256 = ContractDigest(request.AdmittedPlan);
            request.Bindings.AdmittedPlanDigestSha256 = request.AdmittedPlanDigestSha256;
            request.Bindings.SourceVersionDigestSha256 = request.AdmittedPlan.SourceVersion.VersionDigestSha256;
            request.ImportReceipt.AdmittedPlanDigestSha256 = request.AdmittedPlanDigestSha256;
            request.ImportReceipt.SourceVersion = request.AdmittedPlan.SourceVersion;
            request.ImportReceipt.Operations = request.AdmittedPlan.Operations;
            request.ImportReceiptDigestSha256 = ContractDigest(request.ImportReceipt);
            request.Bindings.ImportReceiptDigestSha256 = request.ImportReceiptDigestSha256;
            if (request.RuntimeReceipt != null)
            {
                request.RuntimeReceipt.AdmittedPlanDigestSha256 = request.AdmittedPlanDigestSha256;
                request.RuntimeReceipt.ImportReceiptDigestSha256 = request.ImportReceiptDigestSha256;
                request.RuntimeReceipt.SourceVersion = request.AdmittedPlan.SourceVersion;
                request.RuntimeReceipt.Operations = request.AdmittedPlan.Operations;
                request.RuntimeReceiptDigestSha256 = ContractDigest(request.RuntimeReceipt);
                request.Bindings.RuntimeReceiptDigestSha256 = request.RuntimeReceiptDigestSha256;
            }
        }

        private static string Hash(string value)
        {
            return MigrationDigest.ComputeSha256(value);
        }

        private static string ContractDigest<T>(T value)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(value));
        }

        private static void ResealRuntimeReceipt(PublishingPageCompareRequest request)
        {
            request.RuntimeReceiptDigestSha256 = ContractDigest(request.RuntimeReceipt);
            request.Bindings.RuntimeReceiptDigestSha256 = request.RuntimeReceiptDigestSha256;
        }

        private static RuntimeBrowserContextIdentity CreateRuntimeBrowserContext(
            string browserContextId,
            string targetId,
            DateTimeOffset completedAtUtc)
        {
            return new RuntimeBrowserContextIdentity
            {
                BrowserProduct = "Edg",
                BrowserVersion = "151.0.4129.93",
                ProtocolVersion = "1.3",
                ProfileIdentitySha256 = Hash("test-browser-profile"),
                BrowserContextId = browserContextId,
                TargetId = targetId,
                IsIncognito = true,
                FreshContext = true,
                CreatedAtUtc = completedAtUtc.AddMinutes(-1),
                FirstNavigationAtUtc = completedAtUtc.AddSeconds(-30)
            };
        }

        private static RuntimeHttpEvidence CreateRuntimeHttpEvidence(
            string targetIdentity,
            string headersIdentity,
            DateTimeOffset capturedAtUtc)
        {
            return new RuntimeHttpEvidence
            {
                RequestedUrl = targetIdentity,
                FinalUrl = targetIdentity,
                Method = "GET",
                StatusCode = 200,
                ContentType = "text/html; charset=utf-8",
                RequestId = "test-request",
                SharePointRequestGuid = "10000000-0000-4000-8000-000000000001",
                ResponseHeadersDigestSha256 = Hash(headersIdentity),
                EncodedDataLength = 128,
                CapturedAtUtc = capturedAtUtc.AddSeconds(-10)
            };
        }

        private static RuntimeCacheEvidence CreateRuntimeCacheEvidence()
        {
            return new RuntimeCacheEvidence
            {
                RequestMode = "no-store",
                CacheDisabled = true,
                RequestCacheControl = "no-cache, no-store",
                RequestPragma = "no-cache",
                ResponseCacheControl = "private, max-age=0",
                ResponseAge = "0",
                ResponseExpires = "-1",
                FromDiskCache = false,
                FromServiceWorker = false
            };
        }

        private sealed class PermissiveArtifactStore : IMigrationArtifactStore
        {
            private static readonly Dictionary<string, byte[]> Artifacts = new[]
            {
                "runtime-evidence:authored-dom-equality",
                "runtime-dom:authored-dom-equality"
            }.ToDictionary(
                value => Hash(value),
                value => Encoding.UTF8.GetBytes(value),
                StringComparer.OrdinalIgnoreCase);

            public bool Contains(string sha256) => Artifacts.ContainsKey(sha256);

            public Stream OpenRead(string sha256)
            {
                return new MemoryStream(Artifacts[sha256], writable: false);
            }

            public ArtifactReference Put(Stream content, string mediaType = null, string originalName = null)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ForeignArtifactStore : IMigrationArtifactStore
        {
            public bool Contains(string sha256) => true;

            public Stream OpenRead(string sha256)
            {
                return new MemoryStream(Encoding.UTF8.GetBytes("foreign-runtime-bytes"), writable: false);
            }

            public ArtifactReference Put(Stream content, string mediaType = null, string originalName = null)
            {
                throw new NotSupportedException();
            }
        }

        private static PublishingPageMigrationPackage CreateMigrationPackage(bool includeDeferredField = true)
        {
            var snapshot = CreateSnapshot();
            if (!includeDeferredField)
            {
                snapshot.Fields = new List<PageFieldValueSnapshot>();
                snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(
                    snapshot.Source,
                    snapshot.Layout,
                    snapshot.Fields);
                snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);
            }
            var snapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot);
            var layoutPlan = PublishingPageLayoutPlanFactory.Create(
                snapshot.Layout,
                new Uri(snapshot.Source.WebUrl),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                "EnterpriseWiki.aspx");
            var layoutProbe = new PublishingPageLayoutTargetProbe
            {
                TargetServerRelativeUrl = layoutPlan.TargetServerRelativeUrl,
                FileExists = true,
                ExistingAssociatedContentTypeName = "Enterprise Wiki Page",
                ExistingAssociatedContentTypeId = BuiltInContentTypeId.EnterpriseWikiPage,
                AssociatedContentTypeAvailable = true,
                ResolvedAssociatedContentTypeId = BuiltInContentTypeId.EnterpriseWikiPage,
                Availability = EvidenceAvailability.Captured
            };
            var layoutAdmission = PublishingPageLayoutTargetAdmissionEvaluator.Evaluate(layoutPlan, layoutProbe);
            var plan = new PublishingPageMigrationPlan
            {
                SourceSnapshotDigest = snapshotDigest,
                SourceWebUrl = snapshot.Source.WebUrl,
                SourcePageServerRelativeUrl = snapshot.Source.PageServerRelativeUrl,
                OriginalIdentifier = PublishingPageTargetOwnership.OriginalIdentifier(snapshot.Source),
                TargetWebUrl = "https://target.sharepoint.com/sites/target",
                TargetWebServerRelativeUrl = "/sites/target",
                PreferredTargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx",
                TargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx",
                PageLayoutName = "EnterpriseWiki",
                TargetLifecycle = PublishingPageTargetLifecycle.Draft,
                LifecycleReason = "The source file level is 'Draft', so the target will remain Draft.",
                PlanningPolicy = new PagePlanningOptions
                {
                    TargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx"
                },
                TargetProbe = new PublishingPageTargetSnapshot
                {
                    WebUrl = "https://target.sharepoint.com/sites/target",
                    WebServerRelativeUrl = "/sites/target",
                    PagesLibraryServerRelativeUrl = "/sites/target/Pages",
                    PagesLibraryBaseTemplate = 850,
                    PageContentTypeId = BuiltInContentTypeId.EnterpriseWikiPage,
                    PageLayoutUrl = "https://target.sharepoint.com/_catalogs/masterpage/EnterpriseWiki.aspx",
                    PageLayoutExists = true,
                    PreferredTargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx",
                    TargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx"
                },
                LayoutMaterialization = layoutPlan,
                LayoutTargetProbe = layoutProbe,
                LayoutAdmission = layoutAdmission,
                FieldActions = includeDeferredField
                    ? new List<PageFieldAction>
                    {
                        new PageFieldAction
                        {
                            SourceInternalName = "OOCLReference",
                            TargetInternalName = "OOCLReference",
                            Disposition = PageFieldDisposition.EvidenceOnly,
                            Reason = "The field is retained for a future mapper."
                        }
                    }
                    : new List<PageFieldAction>(),
                ExpectedPublishingPageContentSha256 = snapshot.PublishingPageContentSha256,
                RuntimeVerification = new RuntimeVerificationManifest
                {
                    Requirements = new List<RuntimeVerificationRequirement>
                    {
                        new RuntimeVerificationRequirement
                        {
                            Id = "authored-dom-equality",
                            Kind = RuntimeVerificationRequirementKind.AuthoredDomEquality,
                            Required = true,
                            Description = "Normalized authored DOM is equal."
                        }
                    }
                },
                IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot)
            };
            plan.IngredientActions = PublishingPageIngredientActionProjector.Project(snapshot, plan, plan.IngredientGraph);
            var ingredientEvaluation = PageIngredientPlanEvaluator.Evaluate(plan.IngredientGraph, plan.IngredientActions);
            plan.MigrationOutcome = ingredientEvaluation.Outcome;
            plan.IngredientIssues = ingredientEvaluation.Issues;
            plan.ExecutionFrontier = ingredientEvaluation.ExecutionFrontier;
            return new PublishingPageMigrationPackage
            {
                PlannedAtUtc = DateTimeOffset.UtcNow,
                ExportedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                State = PublishingPagePackageState.ApprovalReady,
                Selection = CreateSelection(),
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(CreateSelection()),
                Snapshot = snapshot,
                Plan = plan,
                SnapshotDigest = snapshotDigest,
                PlanDigest = PublishingPageDigest.ComputePlanDigest(plan),
                Report = new PublishingPageMigrationReport
                {
                    Summary = "Test report"
                }
            };
        }

        private static PublishingPageWorkflowSelection CreateSelection()
        {
            return new PublishingPageWorkflowSelection
            {
                WorkflowId = EnterpriseWikiV1CohortPolicy.CohortId,
                ValidationCohort = new ValidationCohortAssessment
                {
                    CohortId = EnterpriseWikiV1CohortPolicy.CohortId,
                    PolicyVersion = EnterpriseWikiV1CohortPolicy.PolicyVersion,
                    Disposition = ValidationCohortDisposition.Included,
                    Reasons = new List<string>
                    {
                        "Enterprise Wiki Content Type lineage is included by the EW-v1 validation policy."
                    }
                }
            };
        }
    }
}
