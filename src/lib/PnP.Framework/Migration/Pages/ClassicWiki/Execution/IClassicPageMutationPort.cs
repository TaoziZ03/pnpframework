using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Execution
{
    public sealed record ClassicWikiTargetPageSpec(
        string TargetWebUrl,
        string TargetPageServerRelativeUrl,
        int TargetLibraryTemplate = 119);

    public sealed record ClassicWikiTargetWebPartState(
        Guid Id,
        string TypeName,
        string ZoneId,
        int ZoneIndex,
        string ExportSha256);

    public sealed record ClassicWikiTargetPageState(
        bool Exists,
        Guid? FileUniqueId,
        string WikiField,
        IReadOnlyList<ClassicWikiTargetWebPartState> WebParts);

    public interface IClassicPageMutationPort
    {
        Task<ClassicWikiTargetPageState> ReadPageAsync(
            ClassicWikiTargetPageSpec target,
            CancellationToken cancellationToken = default);

        Task<(ClassicWikiTargetPageState State, IReadOnlyList<string> ExchangeIds)> EnsurePageAsync(
            ClassicWikiTargetPageSpec target,
            CancellationToken cancellationToken = default);

        Task<(ClassicWikiTargetPageState State, IReadOnlyList<string> ExchangeIds)> WriteWikiFieldAsync(
            ClassicWikiTargetPageSpec target,
            string value,
            CancellationToken cancellationToken = default);

        Task<(ClassicWikiTargetWebPartState State, IReadOnlyList<string> ExchangeIds)> EnsureWebPartAsync(
            ClassicWikiTargetPageSpec target,
            string webPartXml,
            string zoneId,
            int zoneIndex,
            CancellationToken cancellationToken = default);

        Task<(ClassicWikiTargetWebPartState State, IReadOnlyList<string> ExchangeIds)> MoveAndSaveWebPartAsync(
            ClassicWikiTargetPageSpec target,
            Guid targetWebPartId,
            string zoneId,
            int zoneIndex,
            CancellationToken cancellationToken = default);
    }
}
