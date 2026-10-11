using System;
using System.ComponentModel;
using System.IO;
using Splice.Models;

namespace Splice.Services;

/// <summary>把进程退出与文件系统错误分类；错误文本只用来判别常见的明确标记。</summary>
public sealed class ExceptionHandlerService(DiskSpaceService diskSpace)
{
    public MergeFailureException ClassifyFfmpeg(int exitCode, string stderr, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
            return new(MergeErrorReason.OutputUnavailable,
                "输出目录已不可访问，请检查硬盘或网络共享连接。", true, exitCode, stderr);

        if (ContainsAny(stderr, "No space left on device", "There is not enough space on the disk",
                "not enough space", "Disk full", "ENOSPC", "error number -28", "errno 28", "磁盘空间不足", "磁盘已满"))
            return new(MergeErrorReason.DiskFull, "输出磁盘空间不足，队列已暂停。", true, exitCode, stderr);

        // 进程可能只返回通用 write error；这时辅助查询剩余空间，避免把所有写错误误判为磁盘满。
        if (ContainsAny(stderr, "Error writing", "av_interleaved_write_frame", "Error muxing a packet")
            && diskSpace.TryGetFreeBytes(outputDirectory) is { } free && free < DiskSpaceService.MinimumFreeBytes)
            return new(MergeErrorReason.DiskFull, "输出磁盘剩余空间不足，队列已暂停。", true, exitCode, stderr);

        if (ContainsAny(stderr, "Permission denied", "Access is denied", "权限不足", "拒绝访问"))
        {
            var outputRejected = ContainsAny(stderr, "Error opening output", "Could not write header",
                "Error initializing output stream");
            return new(MergeErrorReason.PermissionDenied,
                outputRejected ? "输出目录没有写入权限，队列已暂停。" : "源文件或视频分片没有读取权限。",
                outputRejected, exitCode, stderr);
        }

        if (ContainsAny(stderr, "No such file or directory", "Failed to open segment", "Error when loading first segment"))
            return new(MergeErrorReason.SourceMissing, "视频分片缺失或路径不可访问，请检查迅雷下载是否完整。",
                false, exitCode, stderr);

        if (ContainsAny(stderr, "Invalid data found when processing input", "Invalid playlist", "Error opening input"))
            return new(MergeErrorReason.InvalidM3u8, "播放列表或视频格式无法解析。", false, exitCode, stderr);

        if (ContainsAny(stderr, "Error writing", "Error opening output", "Error muxing"))
            return new(MergeErrorReason.OutputWriteError,
                "无法写入视频文件，请检查输出存储设备和剩余空间。", true, exitCode, stderr);

        return new(MergeErrorReason.FfmpegError, "FFmpeg 合并失败，详情已写入日志。", false, exitCode, stderr);
    }

    public MergeFailureException Classify(Exception exception, string? outputDirectory = null)
    {
        if (exception is MergeFailureException failure) return failure;
        if (exception is FileNotFoundException)
            return new(MergeErrorReason.SourceMissing, "输入文件不存在或已被移动。", innerException: exception);
        if (exception is UnauthorizedAccessException)
            return new(MergeErrorReason.PermissionDenied, "文件访问被拒绝，请检查目录权限。",
                innerException: exception);
        if (exception is Win32Exception)
            return new(MergeErrorReason.FfmpegNotAvailable, "无法启动内置 FFmpeg，请检查程序安装文件是否完整。",
                innerException: exception);
        if (exception is IOException io)
        {
            // ERROR_DISK_FULL=112 (0x70), ERROR_HANDLE_DISK_FULL=39 (0x27)
            var win32Code = io.HResult & 0xFFFF;
            if (win32Code is 80 or 183)
                return new(MergeErrorReason.OutputWriteError,
                    "目标文件已被其他程序创建，请重试以自动生成新文件名。", innerException: exception);
            if (win32Code is 112 or 39 || outputDirectory is not null &&
                diskSpace.TryGetFreeBytes(outputDirectory) is { } bytes && bytes < DiskSpaceService.MinimumFreeBytes)
                return new(MergeErrorReason.DiskFull, "输出磁盘空间不足，队列已暂停。", true, innerException: exception);
            return new(MergeErrorReason.OutputWriteError, "读取或写入文件时发生错误，请检查存储设备。",
                stopQueue: true, innerException: exception);
        }
        if (exception is ArgumentException or NotSupportedException)
            return new(MergeErrorReason.OutputUnavailable, "文件路径无效或存储位置不受支持。",
                stopQueue: true, innerException: exception);
        return new(MergeErrorReason.Unknown, "处理过程中出现异常，详情已写入日志。", innerException: exception);
    }

    private static bool ContainsAny(string text, params string[] snippets)
    {
        foreach (var fragment in snippets)
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
