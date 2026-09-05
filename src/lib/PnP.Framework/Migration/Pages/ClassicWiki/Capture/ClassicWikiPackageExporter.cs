using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts.Bindings;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Profiles;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Topology.Ingredients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Capture
{
    public sealed class ClassicWikiPackageExporter
    {
        public ClassicWikiExportPackage Export(ClientContext sourceContext, PageCaptureOptions options)
        {
            return Export(sourceContext, options, null);
        }

        public ClassicWikiExportPackage Export(
            ClientContext sourceContext,
            PageCaptureOptions options,
            IMigrationArtifactStore artifactStore)
        {
            if (sourceContext == null) throw new ArgumentNullException(nameof(sourceContext));
            ValidateOptions(options);

            var sourceWeb = sourceContext.Web;
            sourceContext.Load(sourceWeb, web => web.Url, web => web.ServerRelativeUrl, web => web.Title);
            sourceContext.ExecuteQueryRetry();

            var sourcePagePath = PagePath.Normalize(sourceWeb.ServerRelativeUrl, options.SourcePageServerRelativeUrl, "SitePages");
            var blockers = new List<string>();
            var warnings = new List<string>();

            var sourceCapture = ClassicWikiCaptureReader.Read(
                sourceContext,
                sourcePagePath,
                options,
                artifactStore,
                blockers,
                warnings);

            var listBindings = new List<ClassicListWebPartBindingSnapshot>();
            foreach (var webPart in sourceCapture.WebParts)
            {
                if (!ClassicListWebPartBindingParser.IsListBound(webPart))
                {
                    continue;
                }

                var binding = ClassicListWebPartBindingParser.Parse(
                    webPart,
                    sourceCapture.Identity.WebId,
                    sourceCapture.Identity.WebUrl,
                    sourceCapture.Identity.PageServerRelativeUrl);
                foreach (var issue in binding.Issues)
                {
                    blockers.Add(issue.Code + ": " + issue.Message);
                }
                if (binding.Binding != null)
                {
                    listBindings.Add(binding.Binding);
                }
            }

            var listClosure = ListDependencyClosureSnapshotReader.Read(
                sourceContext,
                listBindings,
                options.MaximumDependencyBytes,
                artifactStore,
                options.ProtectedAssets,
                blockers,
                warnings);

            SourceSiteCollectionSnapshot sourceTopology = null;
            PathDerivedSourceTopologyEvidence pathDerivedTopologyEvidence = null;
            try
            {
                var topologyCapture = SourceTopologySnapshotReader.CaptureRequiredWebClosureWithEvidence(
                    sourceContext,
                    listClosure.RequiredSourceWebIds.Concat(new[] { sourceCapture.Identity.WebId }),
                    sourceCapture.Identity.WebId);
                sourceTopology = topologyCapture.SourceTopology;
                pathDerivedTopologyEvidence = topologyCapture.PathDerivedEvidence;
            }
            catch (Exception ex)
            {
                warnings.Add("Failed to capture source topology closure: " + ex.Message);
            }

            var runtime = PageRuntimeResolver.Resolve(
                sourceCapture.PageArtifact,
                null,
                sourceCapture.Identity.ContentTypeId);

            var profileSignals = new List<PageProfileSignal>
            {
                new PageProfileSignal
                {
                    ProfileId = "profile.classic-wiki",
                    Kind = PageProfileSignalKind.Layout,
                    Subject = "WikiEditPage",
                    Evidence = "Page inherits Microsoft.SharePoint.WebPartPages.WikiEditPage and resides in a Wiki/SitePages library."
                }
            };

            var references = PageReferenceSnapshotReader.Read(
                sourceContext,
                sourceCapture.Identity,
                sourceTopology,
                sourceCapture.WikiField,
                sourceCapture.WebParts,
                options,
                warnings);

            var wikiSha = ClassicWikiDigest.ComputeSha256(sourceCapture.WikiField ?? string.Empty);

            var bundle = new ClassicWikiCaptureBundle
            {
                CapturePolicy = options,
                Source = sourceCapture.Identity,
                PageArtifact = sourceCapture.PageArtifact,
                Runtime = runtime,
                ProfileSignals = profileSignals,
                WikiField = sourceCapture.WikiField,
                WikiFieldSha256 = wikiSha,
                LibraryBaseTemplate = sourceCapture.LibraryBaseTemplate,
                LibraryTitle = sourceCapture.LibraryTitle,
                LibraryServerRelativeUrl = sourceCapture.LibraryServerRelativeUrl,
                LibraryEnableVersioning = sourceCapture.LibraryEnableVersioning,
                LibraryEnableMinorVersions = sourceCapture.LibraryEnableMinorVersions,
                LibraryEnableModeration = sourceCapture.LibraryEnableModeration,
                LibraryForceCheckout = sourceCapture.LibraryForceCheckout,
                Fields = sourceCapture.Fields.OrderBy(field => field.InternalName, StringComparer.Ordinal).ToList(),
                WebParts = sourceCapture.WebParts
                    .OrderBy(webPart => webPart.ZoneId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(webPart => webPart.ZoneIndex)
                    .ThenBy(webPart => webPart.Id)
                    .ToList(),
                ListWebPartBindings = listBindings,
                ListDependencies = listClosure.Dependencies,
                ListLookupDependencies = listClosure.LookupDependencies,
                SourceTopology = sourceTopology,
                PathDerivedTopologyEvidence = pathDerivedTopologyEvidence,
                Dependencies = references,
                Security = sourceCapture.Security,
                Lifecycle = sourceCapture.Lifecycle,
                SourceFence = sourceCapture.SourceFence,
                Blockers = blockers.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList(),
                Warnings = warnings.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList()
            };

            var ingredientGraph = CompileIngredientGraph(bundle);
            bundle.IngredientGraph = ingredientGraph;

            var snapshotDigest = ClassicWikiDigest.ComputeSnapshotDigest(bundle);

            var selection = new ClassicWikiWorkflowSelection
            {
                ProfileId = "profile.classic-wiki",
                WorkflowVersion = "classic-wiki-v1",
                TargetLibraryPolicy = sourceCapture.LibraryBaseTemplate == 119 ? "template-119" : "template-101"
            };
            var selectionDigest = ClassicWikiDigest.ComputeSelectionDigest(selection);

            var package = new ClassicWikiExportPackage
            {
                SchemaVersion = ClassicWikiPackageContract.ExportSchemaVersion,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Selection = selection,
                SelectionDigest = selectionDigest,
                Snapshot = bundle,
                SnapshotDigest = snapshotDigest
            };

            ClassicWikiPackageValidator.ValidateExport(package, artifactStore);
            return package;
        }

        private static CanonicalPageIngredientGraph CompileIngredientGraph(ClassicWikiCaptureBundle bundle)
        {
            var graph = new CanonicalPageIngredientGraph();
            graph.Nodes.Add(new PageIngredientNode
            {
                Id = "node:page-identity",
                Label = "Page Identity",
                Kind = PageIngredientKind.ListItem,
                Ownership = PageIngredientOwnership.SourceOwned
            });
            graph.Nodes.Add(new PageIngredientNode
            {
                Id = "node:wiki-field",
                Label = "Wiki Content",
                Kind = PageIngredientKind.Content,
                HasContent = !string.IsNullOrEmpty(bundle.WikiField),
                Ownership = PageIngredientOwnership.SourceOwned,
                EvidenceDigest = bundle.WikiFieldSha256
            });
            graph.Nodes.Add(new PageIngredientNode
            {
                Id = "node:runtime",
                Label = "Page Runtime",
                Kind = PageIngredientKind.Runtime,
                Ownership = PageIngredientOwnership.TargetRuntime,
                RuntimeRequirement = bundle.Runtime?.AdapterId
            });

            foreach (var wp in bundle.WebParts)
            {
                var wpNodeId = "node:webpart:" + wp.Id.ToString("D");
                graph.Nodes.Add(new PageIngredientNode
                {
                    Id = wpNodeId,
                    Label = wp.Title ?? wp.TypeName,
                    Kind = PageIngredientKind.WebPart,
                    Ownership = PageIngredientOwnership.SourceOwned,
                    EvidenceDigest = wp.ExportSha256
                });
                graph.Edges.Add(new PageIngredientEdge
                {
                    FromIngredientId = "node:page-identity",
                    ToIngredientId = wpNodeId,
                    Relationship = PageIngredientRelationship.PlacedIn,
                    Requirement = PageIngredientRequirement.Required
                });
            }

            return graph;
        }

        private static void ValidateOptions(PageCaptureOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.SourcePageServerRelativeUrl))
            {
                throw new InvalidDataException("SourcePageServerRelativeUrl is required for export.");
            }
        }
    }
}
