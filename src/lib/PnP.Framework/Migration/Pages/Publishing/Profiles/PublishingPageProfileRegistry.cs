using PnP.Framework.Migration.Pages.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public static class PublishingPageProfileRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, PublishingPageWorkflowPolicy> PoliciesByWorkflowId =
            new Dictionary<string, PublishingPageWorkflowPolicy>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PublishingPageWorkflowPolicy> PoliciesByProfileId =
            new Dictionary<string, PublishingPageWorkflowPolicy>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<PublishingProfileRegistration> Registrations =
            new List<PublishingProfileRegistration>();

        static PublishingPageProfileRegistry()
        {
            ResetToDefaults();
        }

        public static void ResetToDefaults()
        {
            lock (SyncRoot)
            {
                PoliciesByWorkflowId.Clear();
                PoliciesByProfileId.Clear();
                Registrations.Clear();

                RegisterCore(
                    EnterpriseWikiV1WorkflowPolicy.Instance,
                    PageProfileIds.EnterpriseWiki,
                    BuiltInContentTypeId.EnterpriseWikiPage);

                RegisterCore(
                    ArticlePageV1WorkflowPolicy.Instance,
                    PageProfileIds.ArticlePage,
                    BuiltInContentTypeId.ArticlePage);

                RegisterCore(
                    WelcomePageV1WorkflowPolicy.Instance,
                    PageProfileIds.WelcomePage,
                    BuiltInContentTypeId.WelcomePage);
            }
        }

        public static void Register(
            PublishingPageWorkflowPolicy policy,
            string profileId,
            string contentTypeIdPrefix = null)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (string.IsNullOrWhiteSpace(policy.WorkflowId))
            {
                throw new ArgumentException("Workflow policy must have a valid WorkflowId.", nameof(policy));
            }

            lock (SyncRoot)
            {
                RegisterCore(policy, profileId, contentTypeIdPrefix);
            }
        }

        private static void RegisterCore(
            PublishingPageWorkflowPolicy policy,
            string profileId,
            string contentTypeIdPrefix)
        {
            PoliciesByWorkflowId[policy.WorkflowId] = policy;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                PoliciesByProfileId[profileId] = policy;
            }

            Registrations.RemoveAll(r => string.Equals(r.Policy.WorkflowId, policy.WorkflowId, StringComparison.OrdinalIgnoreCase));
            Registrations.Add(new PublishingProfileRegistration
            {
                Policy = policy,
                ProfileId = profileId,
                ContentTypeIdPrefix = contentTypeIdPrefix
            });
        }

        public static bool TryGetPolicyByWorkflowId(string workflowId, out PublishingPageWorkflowPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(workflowId))
            {
                policy = null;
                return false;
            }
            lock (SyncRoot)
            {
                return PoliciesByWorkflowId.TryGetValue(workflowId, out policy);
            }
        }

        public static bool TryGetPolicyByProfileId(string profileId, out PublishingPageWorkflowPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                policy = null;
                return false;
            }
            lock (SyncRoot)
            {
                return PoliciesByProfileId.TryGetValue(profileId, out policy);
            }
        }

        public static bool TryResolvePolicyByContentType(string contentTypeId, out PublishingPageWorkflowPolicy policy)
        {
            policy = null;
            if (string.IsNullOrWhiteSpace(contentTypeId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                // Match against specific content type prefixes first (longest match wins)
                var matched = Registrations
                    .Where(reg => !string.IsNullOrWhiteSpace(reg.ContentTypeIdPrefix) &&
                                  contentTypeId.StartsWith(reg.ContentTypeIdPrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(reg => reg.ContentTypeIdPrefix.Length)
                    .FirstOrDefault();

                if (matched != null)
                {
                    policy = matched.Policy;
                    return true;
                }

                // Fallback: evaluate policy's AssessValidationCohort
                foreach (var reg in Registrations)
                {
                    if (reg.Policy.AssessValidationCohort != null)
                    {
                        var assessment = reg.Policy.AssessValidationCohort(contentTypeId);
                        if (assessment?.Disposition == Cohorts.ValidationCohortDisposition.Included)
                        {
                            policy = reg.Policy;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static PublishingPageWorkflowPolicy ResolvePolicy(string workflowId = null, string profileId = null, string contentTypeId = null)
        {
            if (!string.IsNullOrWhiteSpace(workflowId) && TryGetPolicyByWorkflowId(workflowId, out var policyByWorkflow))
            {
                return policyByWorkflow;
            }
            if (!string.IsNullOrWhiteSpace(profileId) && TryGetPolicyByProfileId(profileId, out var policyByProfile))
            {
                return policyByProfile;
            }
            if (!string.IsNullOrWhiteSpace(contentTypeId) && TryResolvePolicyByContentType(contentTypeId, out var policyByCt))
            {
                return policyByCt;
            }
            throw new InvalidOperationException($"No Publishing Page workflow policy could be resolved for workflow '{workflowId}', profile '{profileId}', contentType '{contentTypeId}'.");
        }

        public static IReadOnlyCollection<PublishingPageWorkflowPolicy> RegisteredPolicies
        {
            get
            {
                lock (SyncRoot)
                {
                    return PoliciesByWorkflowId.Values.ToList();
                }
            }
        }

        private sealed class PublishingProfileRegistration
        {
            public PublishingPageWorkflowPolicy Policy { get; set; }
            public string ProfileId { get; set; }
            public string ContentTypeIdPrefix { get; set; }
        }
    }
}

