using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Pages.Assessment;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.Publishing.Verification;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Content;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Taxonomy.Assets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class PageAssessmentAndReferencePolicyTests
    {
        [TestMethod]
        public void LiteralHttp403IsTheOnlyEvidenceThatCanBlockAnIngredient()
        {
            var assessment = NewAssessment("reference:asset");
            var evidence = new PageAssessmentEvidence
            {
                AuthorizationFailures = new List<PageIngredientAuthorizationEvidence>
                {
                    Authorization("reference:asset", 403)
                }
            };

            PublishingPageAuthorizationEvidenceProjector.Apply(
                new List<PageIngredientAssessment> { assessment },
                evidence);

            Assert.AreEqual(PageIngredientAssessmentState.AuthorizationBlocked, assessment.State);
            Assert.AreEqual(IngredientDisposition.Block, assessment.ProposedDisposition);
            Assert.AreEqual(403, assessment.AuthorizationEvidence.HttpStatusCode);
            Assert.IsNull(assessment.MitigationCode);
        }

        [TestMethod]
        public void CapturedReferenceAuthorizationEvidenceBlocksOnlyItsExactAssessment()
        {
            var source = SourceIdentity();
            var requestUri = PageReferenceAuthorizationEvidence.CsomRequestUri(source.WebUrl);
            var reference = new PageReferenceSnapshot
            {
                Id = "asset-a",
                OriginalValue = "/teams/source/child/SiteAssets/a.js",
                SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/child/SiteAssets/a.js",
                SourceServerRelativeUrl = "/teams/source/child/SiteAssets/a.js",
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.Failed,
                AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                    PageReferenceAuthorizationEvidence.SourceCaptureOperation,
                    requestUri,
                    403,
                    DateTimeOffset.Parse("2026-09-05T00:00:00Z"))
            };
            var taxonomyReviewPlan = new TaxonomyAssetReviewPlan();
            var merged = PublishingPageSnapshotAuthorizationEvidence.Merge(
                new PublishingPageCaptureBundle
                {
                    Source = source,
                    Dependencies = new List<PageReferenceSnapshot> { reference }
                },
                new PageAssessmentEvidence
                {
                    TaxonomyAssetReviewPlan = taxonomyReviewPlan
                });
            var assessments = new List<PageIngredientAssessment>
            {
                NewAssessment(PublishingPageIngredientIds.Reference("asset-a")),
                NewAssessment(PublishingPageIngredientIds.Reference("asset-b"))
            };

            PublishingPageAuthorizationEvidenceProjector.Apply(assessments, merged);

            Assert.AreSame(taxonomyReviewPlan, merged.TaxonomyAssetReviewPlan);
            Assert.AreEqual(
                PageIngredientAssessmentState.AuthorizationBlocked,
                assessments.Single(value => value.IngredientId == PublishingPageIngredientIds.Reference("asset-a")).State);
            Assert.AreNotEqual(
                PageIngredientAssessmentState.AuthorizationBlocked,
                assessments.Single(value => value.IngredientId == PublishingPageIngredientIds.Reference("asset-b")).State);
        }

        [TestMethod]
        public void AssessmentRejectsAuthorizationEvidenceWithForgedSha256()
        {
            var evidence = Authorization("reference:asset", 403);
            evidence.EvidenceSha256 = new string('f', 64);

            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                PublishingPageAuthorizationEvidenceProjector.Apply(
                    new List<PageIngredientAssessment> { NewAssessment("reference:asset") },
                    new PageAssessmentEvidence
                    {
                        AuthorizationFailures = new List<PageIngredientAuthorizationEvidence> { evidence }
                    }));
        }

        [TestMethod]
        public void SourceReference403BlocksOnlyItsExactExecutionFrontierBranch()
        {
            var source = SourceIdentity();
            var blockedId = PublishingPageIngredientIds.Reference("asset-a");
            var independentId = PublishingPageIngredientIds.Reference("asset-b");
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = source,
                Dependencies = new List<PageReferenceSnapshot>
                {
                    new PageReferenceSnapshot
                    {
                        Id = "asset-a",
                        OriginalValue = "/teams/source/child/SiteAssets/a.js",
                        SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/child/SiteAssets/a.js",
                        SourceServerRelativeUrl = "/teams/source/child/SiteAssets/a.js",
                        IsRenderableResource = true,
                        CaptureStatus = PageCaptureStatus.Failed,
                        AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                            PageReferenceAuthorizationEvidence.SourceCaptureOperation,
                            PageReferenceAuthorizationEvidence.CsomRequestUri(source.WebUrl),
                            403,
                            DateTimeOffset.Parse("2026-09-05T00:00:00Z"))
                    },
                    new PageReferenceSnapshot
                    {
                        Id = "asset-b",
                        OriginalValue = "https://service.example.net/b.js",
                        SourceAbsoluteUrl = "https://service.example.net/b.js"
                    }
                }
            };
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = blockedId, Kind = PageIngredientKind.Reference, HasContent = true },
                    new PageIngredientNode { Id = independentId, Kind = PageIngredientKind.Reference, HasContent = true }
                }
            };
            var actions = new Dictionary<string, PageIngredientAction>(StringComparer.Ordinal)
            {
                [blockedId] = ReferenceAction(blockedId),
                [independentId] = ReferenceAction(independentId)
            };

            PublishingPageIngredientAuthorizationPolicy.Apply(snapshot, null, actions);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                graph,
                actions.Values,
                PublishingPageIngredientAuthorizationPolicy.GetEvidence(snapshot));

            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, evaluation.Outcome);
            Assert.AreEqual(PageIngredientExecutionState.AuthorizationBlocked, evaluation.ExecutionFrontier.GetState(blockedId));
            Assert.AreEqual(PageIngredientExecutionState.Executable, evaluation.ExecutionFrontier.GetState(independentId));
        }

        [DataTestMethod]
        [DataRow(200)]
        [DataRow(302)]
        [DataRow(404)]
        [DataRow(429)]
        [DataRow(500)]
        public void NonAuthorizationHttpStatusCannotBlockAnIngredient(int statusCode)
        {
            var evidence = new PageAssessmentEvidence
            {
                AuthorizationFailures = new List<PageIngredientAuthorizationEvidence>
                {
                    Authorization("reference:asset", statusCode)
                }
            };

            Assert.ThrowsException<System.IO.InvalidDataException>(() =>
                PublishingPageAuthorizationEvidenceProjector.Apply(
                    new List<PageIngredientAssessment> { NewAssessment("reference:asset") },
                    evidence));
        }

        [TestMethod]
        public void CapturedCrossWebAssetMapsByExactSiteRelativePath()
        {
            var reference = new PageReferenceSnapshot
            {
                Id = "asset",
                OriginalValue = "/teams/source/SiteAssets/Images/pixel.gif",
                SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/SiteAssets/Images/pixel.gif",
                SourceServerRelativeUrl = "/teams/source/SiteAssets/Images/pixel.gif",
                Kind = PageReferenceKind.Image,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.Captured,
                ContentBase64 = "AQ==",
                ContentSha256 = new string('a', 64),
                ContentLength = 1
            };
            var gaps = new List<string>();

            var action = PageReferencePlanner.BuildActions(
                SourceIdentity(),
                new[] { reference },
                "https://target.sharepoint.com/sites/source-pnp/child",
                "/sites/source-pnp/child",
                SiteMapping(),
                new PagePlanningOptions(),
                gaps).Single();

            Assert.AreEqual(PageReferenceDisposition.MaterializeAtTarget, action.Disposition);
            Assert.AreEqual(
                "/sites/source-pnp/SiteAssets/Images/pixel.gif",
                action.TargetServerRelativeUrl);
            Assert.AreEqual(0, gaps.Count);
        }

        [TestMethod]
        public void ExactDependencyReplacementDoesNotRewriteTheWholeSourceTenant()
        {
            var reference = new PageReferenceSnapshot
            {
                Id = "asset",
                OriginalValue = "/teams/source/SiteAssets/Images/pixel.gif",
                SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/SiteAssets/Images/pixel.gif",
                SourceServerRelativeUrl = "/teams/source/SiteAssets/Images/pixel.gif"
            };
            var action = new PageReferenceAction
            {
                SnapshotDependencyId = "asset",
                Disposition = PageReferenceDisposition.MaterializeAtTarget,
                TargetServerRelativeUrl = "/sites/source-pnp/SiteAssets/Images/pixel.gif",
                TargetAbsoluteUrl = "https://target.sharepoint.com/sites/source-pnp/SiteAssets/Images/pixel.gif"
            };

            var replacements = PageReferencePlanner.BuildTextReplacements(
                SourceIdentity(),
                "https://target.sharepoint.com/sites/source-pnp/child",
                "/sites/source-pnp/child",
                new[] { reference },
                new[] { action });

            Assert.IsTrue(replacements.Any(value =>
                value.Source == reference.OriginalValue
                && value.Target == action.TargetServerRelativeUrl));
            Assert.IsFalse(replacements.Any(value =>
                value.Source == "https://source.sharepoint.com"));
        }

        [TestMethod]
        public void SameTenantIframeIsDelegatedInsteadOfBlockingTheLoop()
        {
            var reference = new PageReferenceSnapshot
            {
                Id = "iframe",
                OriginalValue = "/teams/source/_layouts/15/VideoEmbedHost.aspx",
                SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/_layouts/15/VideoEmbedHost.aspx",
                SourceServerRelativeUrl = "/teams/source/_layouts/15/VideoEmbedHost.aspx",
                Kind = PageReferenceKind.IFrame,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.CapturedWithLimitations
            };
            var gaps = new List<string>();

            var action = PageReferencePlanner.BuildActions(
                SourceIdentity(),
                new[] { reference },
                "https://target.sharepoint.com/sites/source-pnp/child",
                "/sites/source-pnp/child",
                SiteMapping(),
                new PagePlanningOptions(),
                gaps).Single();

            Assert.AreEqual(PageReferenceDisposition.Delegate, action.Disposition);
            Assert.AreEqual(0, gaps.Count);
        }

        [TestMethod]
        public void UnreadableServerRelativeResourcePreservesExactSourceIdentityWhenExternalReferencesAreAllowed()
        {
            var reference = new PageReferenceSnapshot
            {
                Id = "asset",
                OriginalValue = "/teams/source/SiteAssets/Images/pixel.gif",
                SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/SiteAssets/Images/pixel.gif",
                SourceServerRelativeUrl = "/teams/source/SiteAssets/Images/pixel.gif",
                Kind = PageReferenceKind.Image,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.Failed
            };
            var gaps = new List<string>();

            var action = PageReferencePlanner.BuildActions(
                SourceIdentity(),
                new[] { reference },
                "https://target.sharepoint.com/sites/source-pnp/child",
                "/sites/source-pnp/child",
                SiteMapping(),
                new PagePlanningOptions { AllowExternalResourceReferences = true },
                gaps).Single();
            var replacement = PageReferencePlanner.BuildTextReplacements(
                    SourceIdentity(),
                    "https://target.sharepoint.com/sites/source-pnp/child",
                    "/sites/source-pnp/child",
                    new[] { reference },
                    new[] { action })
                .Single(value => value.Source == reference.OriginalValue);

            Assert.AreEqual(PageReferenceDisposition.PreserveExternal, action.Disposition);
            Assert.IsNull(action.TargetServerRelativeUrl);
            Assert.AreEqual(reference.SourceAbsoluteUrl, action.TargetAbsoluteUrl);
            Assert.AreEqual(reference.SourceAbsoluteUrl, replacement.Target);
            Assert.AreEqual(0, gaps.Count);
        }

        [TestMethod]
        public void UnreadableResourceRemainsGapWhenExternalReferencesAreDisabled()
        {
            var reference = new PageReferenceSnapshot
            {
                Id = "asset",
                OriginalValue = "/teams/source/SiteAssets/Images/pixel.gif",
                SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/SiteAssets/Images/pixel.gif",
                SourceServerRelativeUrl = "/teams/source/SiteAssets/Images/pixel.gif",
                Kind = PageReferenceKind.Image,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.Failed
            };
            var gaps = new List<string>();

            var action = PageReferencePlanner.BuildActions(
                SourceIdentity(),
                new[] { reference },
                "https://target.sharepoint.com/sites/source-pnp/child",
                "/sites/source-pnp/child",
                SiteMapping(),
                new PagePlanningOptions { AllowExternalResourceReferences = false },
                gaps).Single();

            Assert.AreEqual(PageReferenceDisposition.Block, action.Disposition);
            Assert.AreEqual(1, gaps.Count);
        }

        [TestMethod]
        public void MaterializableReferenceDependsOnItsCapturedOwnerWeb()
        {
            var rootWebId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = SourceIdentity(),
                SourceTopology = new SourceSiteCollectionSnapshot
                {
                    SiteId = SourceIdentity().SiteId,
                    RootWebId = rootWebId,
                    SiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                    ServerRelativeUrl = "/teams/source",
                    Webs = new List<SourceWebSnapshot>
                    {
                        new SourceWebSnapshot
                        {
                            SiteId = SourceIdentity().SiteId,
                            WebId = rootWebId,
                            WebUrl = "https://source.sharepoint.com/teams/source",
                            ServerRelativeUrl = "/teams/source"
                        },
                        new SourceWebSnapshot
                        {
                            SiteId = SourceIdentity().SiteId,
                            WebId = SourceIdentity().WebId,
                            WebUrl = SourceIdentity().WebUrl,
                            ServerRelativeUrl = SourceIdentity().WebServerRelativeUrl
                        }
                    }
                },
                Dependencies = new List<PageReferenceSnapshot>
                {
                    new PageReferenceSnapshot
                    {
                        Id = "asset",
                        OriginalValue = "/teams/source/SiteAssets/app.js",
                        SourceAbsoluteUrl = "https://source.sharepoint.com/teams/source/SiteAssets/app.js",
                        SourceServerRelativeUrl = "/teams/source/SiteAssets/app.js",
                        IsRenderableResource = true,
                        CaptureStatus = PageCaptureStatus.Captured,
                        ContentBase64 = "AQ==",
                        ContentSha256 = new string('a', 64)
                    }
                }
            };
            var graph = new CanonicalPageIngredientGraph();

            PublishingPageReferenceIngredientGraphProjector.Project(
                snapshot,
                graph,
                PublishingPageIngredientGraphProjectionRevision.CurrentV7);

            Assert.IsTrue(graph.Edges.Any(value =>
                value.FromIngredientId == PublishingPageIngredientIds.Reference("asset")
                && value.ToIngredientId == PublishingPageIngredientIds.Web(snapshot.Source.SiteId, rootWebId)
                && value.Requirement == PageIngredientRequirement.Required));
        }

        [TestMethod]
        public void AuthorizationLimitedCaptureStillProjectsTheExactPageWebIngredient()
        {
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = SourceIdentity(),
                SourceTopology = null
            };
            var graph = new CanonicalPageIngredientGraph();

            PublishingPageTopologyIngredientGraphProjector.Project(snapshot, graph);

            var webIngredientId = PublishingPageIngredientIds.Web(
                snapshot.Source.SiteId,
                snapshot.Source.WebId);
            Assert.IsTrue(graph.Nodes.Any(value =>
                value.Id == webIngredientId
                && value.Kind == PageIngredientKind.Web
                && value.HasContent));
            Assert.IsTrue(graph.Edges.Any(value =>
                value.FromIngredientId == PublishingPageIngredientIds.PageArtifact
                && value.ToIngredientId == webIngredientId
                && value.Relationship == PageIngredientRelationship.DependsOn
                && value.Requirement == PageIngredientRequirement.Required));
        }

        [TestMethod]
        public void ParentTopology403BlocksOnlyTheProjectedPageWebIngredient()
        {
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = SourceIdentity(),
                SourceTopology = null
            };
            var graph = new CanonicalPageIngredientGraph();
            PublishingPageTopologyIngredientGraphProjector.Project(snapshot, graph);
            var accumulator = new PublishingPageAssessmentAccumulator(graph);
            PublishingPageTopologyAssessmentProjector.Project(
                new PublishingPageAssessmentContext
                {
                    Snapshot = snapshot,
                    TargetSite = null,
                    TargetWeb = null
                },
                accumulator);
            var assessments = accumulator.Complete();
            var webIngredientId = PublishingPageIngredientIds.Web(
                snapshot.Source.SiteId,
                snapshot.Source.WebId);

            PublishingPageAuthorizationEvidenceProjector.Apply(
                assessments,
                new PageAssessmentEvidence
                {
                    AuthorizationFailures = new List<PageIngredientAuthorizationEvidence>
                    {
                        Authorization(webIngredientId, 403)
                    }
                });

            var webAssessment = assessments.Single(value =>
                value.IngredientId == webIngredientId);
            Assert.AreEqual(
                PageIngredientAssessmentState.AuthorizationBlocked,
                webAssessment.State);
            Assert.AreEqual(IngredientDisposition.Block, webAssessment.ProposedDisposition);
            Assert.AreEqual(403, webAssessment.AuthorizationEvidence.HttpStatusCode);
            Assert.AreEqual(1, assessments.Count(value =>
                value.State == PageIngredientAssessmentState.AuthorizationBlocked));
        }

        [TestMethod]
        public void PartialReferenceFrontierProjectsOnlyExactSelectedReplacements()
        {
            var package = ReplacementPackage(
                PageIngredientExecutionState.Executable,
                PageIngredientExecutionState.Deferred);
            var scope = PublishingPageExecutionScope.Create(package);

            var replacements = PublishingPageExecutionReplacementProjector.Project(package, scope);

            Assert.AreEqual(1, replacements.Count);
            Assert.AreEqual("/teams/source/SiteAssets/a.js", replacements[0].Source);
            Assert.AreEqual("/sites/source-pnp/SiteAssets/a.js", replacements[0].Target);
        }

        [TestMethod]
        public void CompleteReferenceFrontierRetainsBroadApprovedReplacements()
        {
            var package = ReplacementPackage(
                PageIngredientExecutionState.Executable,
                PageIngredientExecutionState.Executable);
            var scope = PublishingPageExecutionScope.Create(package);

            var replacements = PublishingPageExecutionReplacementProjector.Project(package, scope);

            Assert.AreEqual(3, replacements.Count);
            Assert.IsTrue(replacements.Any(value => value.Source == "https://source.sharepoint.com/teams/source"));
        }

        [TestMethod]
        public void IngredientVerificationSeparatesPassedPendingAndFailedTransactions()
        {
            var package = ReplacementPackage(
                PageIngredientExecutionState.Executable,
                PageIngredientExecutionState.Deferred);
            package.Plan.ExecutionFrontier.Decisions.Add(new PageIngredientExecutionDecision
            {
                IngredientId = PublishingPageIngredientIds.PublishingContent,
                State = PageIngredientExecutionState.Executable
            });
            package.Plan.ExecutionFrontier.Decisions.Add(new PageIngredientExecutionDecision
            {
                IngredientId = PublishingPageIngredientIds.Runtime,
                State = PageIngredientExecutionState.Executable
            });
            var scope = PublishingPageExecutionScope.Create(package);

            var countOnly = PublishingPageIngredientVerificationProjector.Project(
                package,
                scope,
                new PublishingPageIngredientVerificationEvidence
                {
                    StructuralMaterializersPassed = true,
                    PublishingContentMatched = false,
                    DependenciesMatched = true,
                    RuntimeVerificationRequired = true
                });
            CollectionAssert.Contains(
                countOnly.FailedIngredientIds.ToList(),
                PublishingPageIngredientIds.Reference("asset-a"));

            var result = PublishingPageIngredientVerificationProjector.Project(
                package,
                scope,
                new PublishingPageIngredientVerificationEvidence
                {
                    StructuralMaterializersPassed = true,
                    PublishingContentMatched = false,
                    DependenciesMatched = true,
                    ReferenceResults = new List<PageReferenceVerificationResult>
                    {
                        new PageReferenceVerificationResult
                        {
                            SnapshotDependencyId = "asset-a",
                            Disposition = PageReferenceDisposition.MaterializeAtTarget,
                            ConsumerMatched = true,
                            TargetMatched = true
                        }
                    },
                    RuntimeVerificationRequired = true
                });

            CollectionAssert.Contains(
                result.VerifiedIngredientIds.ToList(),
                PublishingPageIngredientIds.Reference("asset-a"));
            CollectionAssert.Contains(
                result.PendingIngredientIds.ToList(),
                PublishingPageIngredientIds.Runtime);
            CollectionAssert.Contains(
                result.FailedIngredientIds.ToList(),
                PublishingPageIngredientIds.PublishingContent);
            CollectionAssert.DoesNotContain(
                result.FailedIngredientIds.ToList(),
                PublishingPageIngredientIds.Reference("asset-b"));
        }

        private static PageIngredientAssessment NewAssessment(string ingredientId)
        {
            return new PageIngredientAssessment
            {
                IngredientId = ingredientId,
                Kind = PageIngredientKind.Reference,
                State = PageIngredientAssessmentState.KnownGap,
                Capability = IngredientCapability.Missing,
                ProposedDisposition = IngredientDisposition.Defer,
                ProposedRealization = "none",
                PolicyId = "policy.reference.page",
                Reason = "Payload is not available yet.",
                MitigationCode = "ReferencePayloadUnavailable"
            };
        }

        private static PageIngredientAuthorizationEvidence Authorization(
            string ingredientId,
            int statusCode)
        {
            var literal = new LiteralHttpAuthorizationEvidence
            {
                Operation = "source-resource-get",
                RequestUri = "https://source.sharepoint.com/asset.gif",
                HttpStatusCode = statusCode,
                ObservedAtUtc = DateTimeOffset.Parse("2026-09-03T00:00:00Z")
            };
            literal.EvidenceSha256 = LiteralHttpAuthorizationEvidence.ComputeSha256(literal);
            return new PageIngredientAuthorizationEvidence
            {
                IngredientId = ingredientId,
                Operation = literal.Operation,
                RequestUri = literal.RequestUri,
                HttpStatusCode = literal.HttpStatusCode,
                ObservedAtUtc = literal.ObservedAtUtc,
                EvidenceSource = "evidence/source-resource-probe.json",
                EvidenceSha256 = literal.EvidenceSha256
            };
        }

        private static PageIngredientAction ReferenceAction(string ingredientId)
        {
            return new PageIngredientAction
            {
                ActionId = "action:" + ingredientId,
                IngredientId = ingredientId,
                Capability = IngredientCapability.Available,
                Disposition = IngredientDisposition.Preserve,
                Realization = "reuse-external-reference",
                PolicyId = "policy.reference.page",
                PolicyVersion = "1",
                Reason = "Preserve the reference."
            };
        }

        private static PageIdentity SourceIdentity()
        {
            return new PageIdentity
            {
                SiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                WebId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                WebUrl = "https://source.sharepoint.com/teams/source/child",
                WebServerRelativeUrl = "/teams/source/child",
                PageServerRelativeUrl = "/teams/source/child/Pages/page.aspx"
            };
        }

        private static SiteCollectionMappingPlan SiteMapping()
        {
            return new SiteCollectionMappingPlan
            {
                SourceSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SourceSiteCollectionUrl = "https://source.sharepoint.com/teams/source",
                TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/source-pnp"
            };
        }

        private static PublishingPageMigrationPackage ReplacementPackage(
            PageIngredientExecutionState first,
            PageIngredientExecutionState second)
        {
            var firstId = PublishingPageIngredientIds.Reference("asset-a");
            var secondId = PublishingPageIngredientIds.Reference("asset-b");
            return new PublishingPageMigrationPackage
            {
                Snapshot = new PublishingPageCaptureBundle
                {
                    Dependencies = new List<PageReferenceSnapshot>
                    {
                        new PageReferenceSnapshot
                        {
                            Id = "asset-a",
                            OriginalValue = "/teams/source/SiteAssets/a.js"
                        },
                        new PageReferenceSnapshot
                        {
                            Id = "asset-b",
                            OriginalValue = "/teams/source/SiteAssets/b.js"
                        }
                    }
                },
                Plan = new PublishingPageMigrationPlan
                {
                    DependencyActions = new List<PageReferenceAction>
                    {
                        new PageReferenceAction
                        {
                            SnapshotDependencyId = "asset-a",
                            Disposition = PageReferenceDisposition.MaterializeAtTarget
                        },
                        new PageReferenceAction
                        {
                            SnapshotDependencyId = "asset-b",
                            Disposition = PageReferenceDisposition.MaterializeAtTarget
                        }
                    },
                    Replacements = new List<PageTextReplacement>
                    {
                        new PageTextReplacement
                        {
                            Source = "https://source.sharepoint.com/teams/source",
                            Target = "https://target.sharepoint.com/sites/source-pnp"
                        },
                        new PageTextReplacement
                        {
                            Source = "/teams/source/SiteAssets/a.js",
                            Target = "/sites/source-pnp/SiteAssets/a.js"
                        },
                        new PageTextReplacement
                        {
                            Source = "/teams/source/SiteAssets/b.js",
                            Target = "/sites/source-pnp/SiteAssets/b.js"
                        }
                    },
                    ExecutionFrontier = new PageIngredientExecutionFrontier
                    {
                        Decisions = new List<PageIngredientExecutionDecision>
                        {
                            new PageIngredientExecutionDecision { IngredientId = firstId, State = first },
                            new PageIngredientExecutionDecision { IngredientId = secondId, State = second }
                        }
                    }
                }
            };
        }
    }
}
