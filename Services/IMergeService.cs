using System;
using System.Threading;
using System.Threading.Tasks;
namespace Splice.Services;

public sealed record MergeProgress(double? Percent, string Detail);

public interface IMergeService
{
    Task MergeAsync(string ffmpegPath, string playlistPath, string outputPath,
        IProgress<MergeProgress> progress, CancellationToken cancellationToken);
}
