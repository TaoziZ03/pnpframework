using PnP.Framework.Migration.Topology;
using PnP.Framework.Migration.Topology.Ingredients;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Planning
{
    public sealed class ClassicWikiWebPartPlacementPlan
    {
        public Guid SourceId { get; set; }

        public string Title { get; set; }

        public string TypeName { get; set; }

        public string ZoneId { get; set; } = "Bottom";

        public int SourceZoneIndex { get; set; }

        public int TargetZoneIndex { get; set; }

        public string Xml { get; set; }
    }

    public sealed class ClassicWikiDependencyPlan
    {
        public string SourceOriginalUrl { get; set; }

        public string TargetServerRelativeUrl { get; set; }

        public string Disposition { get; set; } = "Rewrite";
    }

    public sealed class ClassicWikiMigrationPlan
    {
        public string OriginalIdentifier { get; set; }

        public string SourceSnapshotDigest { get; set; }

        public string TargetPageServerRelativeUrl { get; set; }

        public ClassicWikiTargetLocationPlan TargetLocation { get; set; }

        public WikiFieldWritePlan WikiFieldPlan { get; set; }

        public IList<ClassicWikiWebPartPlacementPlan> WebParts { get; set; } = new List<ClassicWikiWebPartPlacementPlan>();

        public IList<ClassicWikiDependencyPlan> Dependencies { get; set; } = new List<ClassicWikiDependencyPlan>();

        public TopologyPlan Topology { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SharedTopologyPageReference SharedTopologyReference { get; set; }

        public string LifecyclePolicy { get; set; } = "Publish";

        public IList<string> Warnings { get; set; } = new List<string>();

        public IList<string> Blockers { get; set; } = new List<string>();
    }
}
