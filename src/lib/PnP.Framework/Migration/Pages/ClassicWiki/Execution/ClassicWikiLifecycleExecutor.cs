using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal enum ClassicWikiLifecycleAction
    {
        CheckIn = 1,
        Publish = 2,
        Approve = 3
    }

    internal static class ClassicWikiLifecycleExecutor
    {
        public static void Apply(
            ClientContext context,
            Microsoft.SharePoint.Client.File file,
            List library,
            ClassicWikiLifecyclePolicy policy,
            string pagePath,
            MigrationExecutionRecorder recorder)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (library == null) throw new ArgumentNullException(nameof(library));

            context.Load(file, value => value.CheckOutType);
            context.ExecuteQueryRetry();
            foreach (var action in Plan(policy, file.CheckOutType, library.EnableModeration))
            {
                switch (action)
                {
                    case ClassicWikiLifecycleAction.CheckIn:
                        recorder.Execute(
                            "page.checkin",
                            $"Check in wiki page '{pagePath}'.",
                            () =>
                            {
                                file.CheckIn(
                                    "Migration check-in",
                                    library.EnableMinorVersions ? CheckinType.MinorCheckIn : CheckinType.MajorCheckIn);
                                return true;
                            },
                            value => MutationOutcome.Applied,
                            value => "Checked in wiki page.");
                        context.ExecuteQueryRetry();
                        break;
                    case ClassicWikiLifecycleAction.Publish:
                        recorder.Execute(
                            "page.publish",
                            $"Publish wiki page '{pagePath}'.",
                            () =>
                            {
                                file.Publish("Migration publish");
                                return true;
                            },
                            value => MutationOutcome.Applied,
                            value => "Published wiki page.");
                        context.ExecuteQueryRetry();
                        break;
                    case ClassicWikiLifecycleAction.Approve:
                        recorder.Execute(
                            "page.approve",
                            $"Approve moderated wiki page '{pagePath}'.",
                            () =>
                            {
                                file.Approve("Migration approval");
                                return true;
                            },
                            value => MutationOutcome.Applied,
                            value => "Approved moderated wiki page.");
                        context.ExecuteQueryRetry();
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported classic wiki lifecycle action: " + action + ".");
                }
            }
        }

        internal static IList<ClassicWikiLifecycleAction> Plan(
            ClassicWikiLifecyclePolicy policy,
            CheckOutType checkOutType,
            bool moderationEnabled)
        {
            if (policy != ClassicWikiLifecyclePolicy.Publish)
            {
                throw new InvalidOperationException("Unsupported classic wiki lifecycle policy: " + policy + ".");
            }

            var actions = new List<ClassicWikiLifecycleAction>();
            if (checkOutType != CheckOutType.None)
            {
                actions.Add(ClassicWikiLifecycleAction.CheckIn);
            }
            actions.Add(ClassicWikiLifecycleAction.Publish);
            if (moderationEnabled)
            {
                actions.Add(ClassicWikiLifecycleAction.Approve);
            }
            return actions;
        }
    }
}
