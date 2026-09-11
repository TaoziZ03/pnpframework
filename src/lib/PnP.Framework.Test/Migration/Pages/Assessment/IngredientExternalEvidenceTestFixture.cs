using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.Assessment.Maturity.External;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Text.Json.Nodes;

namespace PnP.Framework.Test.Migration.Pages.Assessment
{
    // Original plan/lifecycle bytes are historical evidence. The admission annex,
    // domain graph and M0-M2 observations below are explicitly synthetic controls;
    // this fixture is NOT admission of CCD-147 or a live maturity assessment.
    internal sealed class IngredientExternalEvidenceTestFixture : IIngredientMaturityContributor
    {
        public const string OriginalPlanSha = "6546a2e5c1de5e9c350c9ebca284810858ae3c9a0e6264592f5892d0acd21787";
        public const string PlanDigest = "8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f";
        public const string PlanProducer = "2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c";
        public const string HistoricalImplementation = "5a9f634da422a92b2493967e32dc74edacac9777";
        public const string CurrentCandidate = "3748a6d8c3ad919437f2310a68b81e3029e7b072";
        private static readonly ResourceManager Resources = new ResourceManager(
            "PnP.Framework.Test.Migration.Pages.Assessment.IngredientExternalEvidenceFixtures",
            typeof(IngredientExternalEvidenceTestFixture).Assembly);
        private static readonly string[] FixtureNames =
        {
            "admission", "provision", "readiness", "capability",
            "native-create", "fresh-readback", "cleanup", "post-cleanup"
        };

        public IngredientMaturityEvaluationContext Context { get; }
        public IngredientExternalPlanBinding Binding { get; }
        public IngredientPlanEvidence Plan { get; }
        public IngredientOperationalEvidence Operational { get; }
        public IngredientExternalReceiptSet ReceiptSet { get; }
        public MemoryStore Store { get; } = new MemoryStore();
        public string ContributorId => "test-only.external-maturity/v1";
        public string Lane => "page.layout";
        public bool IncludeM1 { get; set; } = true;
        private readonly byte[] sourceBytes = Encoding.UTF8.GetBytes("synthetic shared-contract source evidence");
        private readonly string snapshotJson;

        public IngredientExternalEvidenceTestFixture()
        {
            var source = new IngredientExternalPageIdentity
            {
                PageUrl = "https://microsoft.sharepoint.com/sites/biatmicrosoft/blog_Archive/SitePages/rss.aspx",
                FileServerRelativeUrl = "/sites/biatmicrosoft/blog_Archive/SitePages/rss.aspx",
                ListId = "6779e40e-3e8f-41c3-bbd4-9a5020797f2f", ItemId = 1,
                FileUniqueId = "df8c03a5-8f4b-4966-bbe6-fc82992c0234",
                ETag = "\"{DF8C03A5-8F4B-4966-BBE6-FC82992C0234},12\""
            };
            snapshotJson = MigrationContractSerializer.SerializeCanonical(new { source, origin = "synthetic-contract-annex" });
            var snapshot = Store.Add(Encoding.UTF8.GetBytes(snapshotJson));
            Context = new IngredientMaturityEvaluationContext
            {
                Identity = new IngredientMaturityIdentity
                {
                    ClaimId = "f411b3cf66979cbcb16479dd79ef2faefecc237df164c6d1e3642967d869c7c7",
                    IngredientId = "ccd.ingredient.page.layout/v1:df8c03a5-8f4b-4966-bbe6-fc82992c0234:wiki-runtime-binding",
                    Lane = Lane, Kind = PageIngredientKind.Layout, Subtype = "layout.wiki",
                    SemanticRole = "wiki-page-family-layout-binding",
                    SourcePredicateId = "test-only.external-layout-source/v1"
                },
                Source = new IngredientMaturitySourceBinding
                {
                    PageOrListItemIdentity = "list:" + source.ListId + "/item:1/file:" + source.FileUniqueId,
                    SourceVersion = source.ETag,
                    SourceArtifactDigest = MigrationDigest.ComputeSha256(sourceBytes),
                    SourceSnapshotDigest = snapshot.Sha256
                },
                Target = new IngredientMaturityTargetBinding
                {
                    TargetProfile = "cupcollect-classic-page/v1",
                    TargetIdentity = "/sites/biatmicrosoft-ccd35-8d6a662a/blog_Archive/SitePages/rss.aspx"
                },
                Producer = new IngredientMaturityProducerBinding
                {
                    ProducerId = "test-only.shared-contract", ProducerVersion = "v1",
                    ImplementationCommit = HistoricalImplementation
                },
                TargetMaturity = IngredientMaturityLevel.M5,
                TechnicalOutcome = new IngredientTechnicalOutcome
                {
                    Status = IngredientTechnicalStatus.Conditional,
                    PnPMigrationOutcome = PageMigrationOutcome.ExecutableWithLoss,
                    ReasonCode = "TEST_ONLY_NO_NATIVE_OR_M5_ACCEPTANCE"
                },
                ObservationWindowStartUtc = Utc("2026-09-10T10:16:00Z"),
                ObservationWindowEndUtc = Utc("2026-09-10T10:18:00Z"),
                ExternalAdmission = new IngredientExternalEvidenceAdmission
                {
                    PlanArtifactSha256 = OriginalPlanSha, PlanDigest = PlanDigest, PlanProducerCommit = PlanProducer,
                    ExecutionWindowStartUtc = Utc("2026-09-10T10:16:00Z"),
                    ExecutionWindowEndUtc = Utc("2026-09-10T10:18:00Z"),
                    Target = new IngredientExternalTargetIdentity
                    {
                        SiteId = "a461d8c2-085e-42c4-9973-063e82aa61fb",
                        WebId = "2984b8cb-6e55-4725-9162-ec3ec644b90b",
                        ListId = "b432724b-3dd7-4667-9b0c-7d28b9f68093",
                        FileUniqueId = "263aa02e-af79-4166-b9ed-c39d91adb242",
                        ItemId = 3, ETag = "\"{263AA02E-AF79-4166-B9ED-C39D91ADB242},1\""
                    }
                }
            };
            Binding = new IngredientExternalPlanBinding
            {
                Identity = Clone(Context.Identity), Source = Clone(Context.Source),
                Target = Clone(Context.Target), Producer = Clone(Context.Producer),
                PlanSchema = IngredientExternalEvidenceContract.BatchPlanSchema,
                PlanArtifactSha256 = OriginalPlanSha, PlanDigest = PlanDigest, PlanProducerCommit = PlanProducer,
                SourceSnapshotArtifact = snapshot, SourcePage = source,
                ExternalPlanOperationId = "ccd109-apply-r00003",
                PlannedTargetUrl = "https://a830edad9050849cupcollect.sharepoint.com/sites/biatmicrosoft/blog_Archive/SitePages/rss.aspx",
                TargetOrigin = "https://a830edad9050849cupcollect.sharepoint.com",
                TargetPath = Context.Target.TargetIdentity,
                TargetSitePath = "/sites/biatmicrosoft-ccd35-8d6a662a",
                TargetWebPath = "/sites/biatmicrosoft-ccd35-8d6a662a/blog_Archive",
                TargetListPath = "/sites/biatmicrosoft-ccd35-8d6a662a/blog_Archive/SitePages",
                LifecycleInputArtifact = Store.Add(OriginalBytes("LifecycleInput")),
                LifecycleDigest = "55147fa133d09a7cdd0868c5142af5c196957e45a625e73984c397fe52c262f0",
                TargetMappingDigest = "8bf3168d449fd4f9ea1cac8ee63e89baa004150a6a594d528f6728e58dd73452",
                RunId = "8d6a662a-c13a-42b7-baf7-368e39e3fdc9", RowId = "row-00003", RowNumber = 3,
                NativeOperationId = "ccd143-native-page-1d6417d33f4582f4",
                NativeActionId = "ccd143-action-native-create-ccd143-native-page-1d6417d33f4582f4",
                OwnershipMarker = "[CCD-143 8d6a662a]",
                LifecycleProducer = new IngredientExternalProducerReference
                {
                    Kind = "workspace-file", Path = "ccd-143/scripts/build-lifecycle-expressions.mjs",
                    Sha256 = "441ce088a3e2d4f5860412d8ce4d99e4c83f0ddc9e3890e8a843cff56fde19ca"
                },
                AdmittedAtUtc = Utc("2026-09-10T10:16:02Z"),
                Graph = new CanonicalPageIngredientGraph(),
                RuntimeManifest = new RuntimeVerificationManifest
                {
                    Requirements = new List<RuntimeVerificationRequirement>
                    {
                        new RuntimeVerificationRequirement { Id = "test-only.reachability", Kind = RuntimeVerificationRequirementKind.PageReachability },
                        new RuntimeVerificationRequirement { Id = "test-only.error-shell", Kind = RuntimeVerificationRequirementKind.ErrorShellAbsence }
                    }
                }
            };
            Binding.Graph.Nodes.Add(new PageIngredientNode
            {
                Id = Context.Identity.IngredientId, Kind = PageIngredientKind.Layout, HasContent = true,
                Subtype = Context.Identity.Subtype, SemanticRole = Context.Identity.SemanticRole,
                SourcePredicateId = Context.Identity.SourcePredicateId, PrimaryOwnerLane = Lane,
                SourcePageOrListItemIdentity = Context.Source.PageOrListItemIdentity,
                SourceVersionIdentity = Context.Source.SourceVersion, EvidenceDigest = Context.Source.SourceArtifactDigest,
                Ownership = PageIngredientOwnership.SourceOwned, SourceAuthority = "test-only",
                RuntimeRequirement = "test-only.reachability"
            });
            Binding.Actions.Add(new PageIngredientAction
            {
                ActionId = Binding.NativeActionId, IngredientId = Context.Identity.IngredientId,
                Capability = IngredientCapability.Available, Disposition = IngredientDisposition.Preserve,
                TargetIdentity = Context.Target.TargetIdentity, PolicyId = "test-only.admitted-lifecycle", PolicyVersion = "v1",
                Reason = "Synthetic annex over unmodified historical plan/lifecycle artifacts."
            });
            Plan = new IngredientPlanEvidence
            {
                ExpectedPlanDigest = PlanDigest, ExpectedSourceSnapshotDigest = Context.Source.SourceSnapshotDigest,
                IngredientId = Context.Identity.IngredientId,
                External = new IngredientExternalPlanEvidence
                {
                    PlanArtifact = Store.Add(OriginalBytes("OriginalPlan")), ArtifactStore = Store
                }
            };
            ReceiptSet = new IngredientExternalReceiptSet
            {
                Receipts = FixtureNames.Select(name => Store.Add(OriginalBytes(name))).ToList()
            };
            Operational = new IngredientOperationalEvidence
            {
                AdmittedPlanDigest = PlanDigest, IngredientId = Context.Identity.IngredientId,
                External = new IngredientExternalOperationalEvidence { PlanEvidence = Plan.External }
            };
            ResealBinding(true);
        }

        public static byte[] OriginalBytes(string name) => Convert.FromBase64String(Resources.GetString(name));
        public static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        private static T Clone<T>(T value) => MigrationContractSerializer.Deserialize<T>(MigrationContractSerializer.SerializeCanonical(value));

        // Test-only re-admission deliberately makes semantic negative controls
        // stronger than a trivial raw-hash mismatch. Never use this in a host.
        public void ResealBinding(bool resign = false)
        {
            if (resign)
                Binding.ActionSignature = MigrationActionSignature.Create(Binding.NativeActionId,
                    IngredientExternalEvidenceContract.LifecycleReceiptSchema, Context.Source.SourceArtifactDigest,
                    PlanDigest, Context.Target.TargetIdentity, Batch1SealedPlanEvidenceAdapter.ActionSemanticDigest(Binding));
            Plan.External.BindingArtifact = Store.Json(Binding);
            Context.ExternalAdmission.BindingArtifactSha256 = Plan.External.BindingArtifact.Sha256;
            ReceiptSet.BindingArtifactSha256 = Plan.External.BindingArtifact.Sha256;
            ResealReceipts();
        }

        public void ResealReceipts()
        {
            Operational.External.ReceiptSetArtifact = Store.Json(ReceiptSet);
            Context.ExternalAdmission.ReceiptSetArtifactSha256 = Operational.External.ReceiptSetArtifact.Sha256;
        }

        public void MutateReceipt(string name, Action<JsonNode> mutation)
        {
            var index = System.Array.IndexOf(FixtureNames, name);
            var root = JsonNode.Parse(Store.Bytes(ReceiptSet.Receipts[index].Sha256));
            mutation(root);
            ReceiptSet.Receipts[index] = Store.Json(root);
            ResealReceipts();
        }

        public void MutateInput(Action<JsonNode> mutation)
        {
            var input = JsonNode.Parse(Store.Bytes(Binding.LifecycleInputArtifact.Sha256));
            mutation(input);
            Binding.LifecycleInputArtifact = Store.Json(input);
            ResealBinding();
        }

        public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
        {
            var node = Binding.Graph.Nodes.Single(value => value.Id == Context.Identity.IngredientId);
            var owners = new PublishingPageIngredientPrimaryOwnerRegistry(new[]
            {
                new PageIngredientPrimaryOwnerDescriptor("test-only.owner", node.Kind, node.Subtype,
                    node.SemanticRole, node.SourcePredicateId, node.PrimaryOwnerLane)
            }, new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>
            {
                [node.SourcePredicateId] = value => value.HasBoundSourceIdentity
            });
            var receipts = new List<IngredientMaturityGateReceipt>();
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(context, node, null, owners, new[] { "test-only:source" }));
            if (IncludeM1)
            {
                receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(context, new IngredientLiveEvidence
                {
                    SourceAuthenticated = true, TargetFreshReadback = true,
                    ReadbackStartedAtUtc = Utc("2026-09-10T10:17:03Z"),
                    SourceEvidenceReferences = new[] { "test-only:source" },
                    TargetEvidenceReferences = new[] { "test-only:target" },
                    Observations = new[]
                    {
                        Observation(context, IngredientObservationOrigin.AuthenticatedSource, "test-only:source", "2026-09-10T10:16:01Z"),
                        Observation(context, IngredientObservationOrigin.CupCollectFreshReadback, "test-only:target", "2026-09-10T10:17:12Z")
                    }
                }));
            }
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(new IngredientContentIntegrityEvidence
            {
                Artifact = MigrationArtifact.Describe(sourceBytes), ContentBase64 = Convert.ToBase64String(sourceBytes),
                SemanticValueCanonicalJson = snapshotJson, SemanticDigest = Context.Source.SourceSnapshotDigest,
                EvidenceReferences = new[] { "test-only:source" }
            }));
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM3(context, Plan));
            receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM4(context, Operational));
            return new IngredientMaturityContribution
            {
                ContributorId = ContributorId, ClaimId = context.Identity.ClaimId, Lane = Lane,
                IngredientId = context.Identity.IngredientId, GateReceipts = receipts
            };
        }

        private static IngredientValueObservation Observation(IngredientMaturityEvaluationContext context,
            IngredientObservationOrigin origin, string reference, string time) => new IngredientValueObservation
        {
            ClaimId = context.Identity.ClaimId, IngredientId = context.Identity.IngredientId,
            Source = Clone(context.Source), Target = Clone(context.Target),
            ValuePath = "test-only.value", ValueDigest = context.Source.SourceSnapshotDigest,
            Origin = origin, EvidenceReference = reference, ObservedAtUtc = Utc(time)
        };

        public IngredientMaturityAssessment Evaluate() => IngredientMaturityEvaluator.Evaluate(Context,
            new IngredientMaturityContributorCatalog(new[] { this }));

        internal sealed class MemoryStore : IMigrationArtifactStore
        {
            private readonly Dictionary<string, byte[]> content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public bool Contains(string sha256) => content.ContainsKey(sha256);
            public Stream OpenRead(string sha256) => new MemoryStream(content[sha256], false);
            public ArtifactReference Put(Stream stream, string mediaType = null, string originalName = null)
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return Add(buffer.ToArray());
            }
            public ArtifactReference Add(byte[] bytes)
            {
                var reference = MigrationArtifact.Describe(bytes, "application/json");
                content[reference.Sha256] = (byte[])bytes.Clone();
                return reference;
            }
            public ArtifactReference Json<T>(T value) => Add(Encoding.UTF8.GetBytes(MigrationContractSerializer.SerializeCanonical(value)));
            public byte[] Bytes(string sha256) => (byte[])content[sha256].Clone();
            public void Corrupt(string sha256) => content[sha256] = Encoding.UTF8.GetBytes("{}");
            public void Remove(string sha256) => content.Remove(sha256);
        }
    }
}
