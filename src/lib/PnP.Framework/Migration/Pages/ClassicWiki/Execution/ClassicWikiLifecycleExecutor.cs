using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    internal enum ClassicWikiLifecycleAction
    {
        CheckInMinor = 1,
        CheckInMajor = 2,
        Publish = 3,
        Approve = 4
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
            foreach (var action in Plan(
                policy,
                file.CheckOutType,
                library.EnableVersioning,
                library.EnableMinorVersions,
                library.EnableModeration))
            {
                switch (action)
                {
                    case ClassicWikiLifecycleAction.CheckInMinor:
                        recorder.Execute(
                            "page.checkin",
                            $"Check in wiki page '{pagePath}' as a draft minor version.",
                            () =>
                            {
                                file.CheckIn("Migration check-in", CheckinType.MinorCheckIn);
                                return true;
                            },
                            value => MutationOutcome.Applied,
                            value => "Checked in wiki page as a minor version.");
                        context.ExecuteQueryRetry();
                        break;
                    case ClassicWikiLifecycleAction.CheckInMajor:
                        recorder.Execute(
                            "page.checkin",
                            $"Check in wiki page '{pagePath}' as a published major version.",
                            () =>
                            {
                                file.CheckIn("Migration check-in", CheckinType.MajorCheckIn);
                                return true;
                            },
                            value => MutationOutcome.Applied,
                            value => "Checked in wiki page as a major version.");
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
            bool versioningEnabled,
            bool minorVersionsEnabled,
            bool moderationEnabled)
        {
            if (policy != ClassicWikiLifecyclePolicy.Publish)
            {
                throw new InvalidOperationException("Unsupported classic wiki lifecycle policy: " + policy + ".");
            }
            if (!versioningEnabled)
            {
                throw new InvalidOperationException("The Publish lifecycle requires library versioning.");
            }

            var actions = new List<ClassicWikiLifecycleAction>();
            if (checkOutType != CheckOutType.None)
            {
                actions.Add(minorVersionsEnabled
                    ? ClassicWikiLifecycleAction.CheckInMinor
                    : ClassicWikiLifecycleAction.CheckInMajor);
            }
            if (minorVersionsEnabled)
            {
                actions.Add(ClassicWikiLifecycleAction.Publish);
            }
            if (moderationEnabled)
            {
                actions.Add(ClassicWikiLifecycleAction.Approve);
            }
            return actions;
        }
    }
}
