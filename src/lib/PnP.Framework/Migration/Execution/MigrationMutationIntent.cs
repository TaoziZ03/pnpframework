using System;

namespace PnP.Framework.Migration.Execution
{
    public sealed class MigrationMutationIntent
    {
        public Guid OperationId { get; set; }

        public string PlanDigest { get; set; }

        public string ActionId { get; set; }

        public int Sequence { get; set; }

        public DateTimeOffset WrittenAtUtc { get; set; }

        public string Description { get; set; }

        public string SourceSnapshotDigest { get; set; }

        public string ApprovalDigest { get; set; }

        public string IngredientId { get; set; }

        public string SelectedDisposition { get; set; }

        public string TargetBoundaryDigest { get; set; }

        public string SemanticDigest { get; set; }

        public string IdempotencyKey { get; set; }
    }
}
