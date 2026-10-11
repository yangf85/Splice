using System;
using System.IO;
using Serilog;
using Splice.Models;

namespace Splice.Services;

/// <summary>
/// 保守的磁盘空间检查。仅在能可靠获得可用字节数时预先阻止任务；
/// 无法查询网络共享空间时不猜测，写入期间仍依赖 FFmpeg 错误和 I/O 异常。
/// </summary>
public sealed class DiskSpaceService(ILogger logger)
{
    public const long MinimumFreeBytes = 256L * 1024 * 1024; // 最小安全余量，非视频大小估计

    public long? TryGetFreeBytes(string outputDirectory)
    {
        try
        {
            var fullPath = Path.GetFullPath(outputDirectory);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.Warning(ex, "无法查询输出空间：{OutputDirectory}", outputDirectory);
            return null;
        }
    }

    public void EnsureMinimumFreeSpace(string outputDirectory)
    {
        var free = TryGetFreeBytes(outputDirectory);
        if (free is not { } available || available >= MinimumFreeBytes) return;

        throw new MergeFailureException(MergeErrorReason.DiskFull,
            $"输出磁盘可用空间仅 {available / 1024.0 / 1024:0} MB，低于 256 MB 安全余量，请释放空间后重试。",
            stopQueue: true);
    }
}
