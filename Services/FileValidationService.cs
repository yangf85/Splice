using System;
using System.IO;
using Splice.Models;

namespace Splice.Services;

public sealed class FileValidationService
{
    public void ValidatePlaylist(string path)
    {
        if (!File.Exists(path))
            throw new MergeFailureException(MergeErrorReason.SourceMissing, "源 M3U8 文件不存在或已被移动。", diagnostics: path);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine()?.TrimStart('\uFEFF').Trim();
            if (!string.Equals(firstLine, "#EXTM3U", StringComparison.Ordinal))
                throw new MergeFailureException(MergeErrorReason.InvalidM3u8,
                    "M3U8 文件内容不是有效的 HLS 播放列表。", diagnostics: path);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MergeFailureException(MergeErrorReason.PermissionDenied,
                "没有权限读取源视频播放列表。", diagnostics: path, innerException: ex);
        }
        catch (IOException ex)
        {
            throw new MergeFailureException(MergeErrorReason.SourceMissing,
                "无法读取源视频播放列表，请确认文件未被移动且存储设备仍然连接。",
                diagnostics: path, innerException: ex);
        }
    }

    public void ValidateOutputDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            // 非破坏性探测：仅在目录中建立一个空的随机临时文件，然后自动删除。
            var probe = Path.Combine(path, $".splice-writecheck-{Guid.NewGuid():N}.tmp");
            using var _ = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MergeFailureException(MergeErrorReason.PermissionDenied,
                "没有权限写入输出目录。", stopQueue: true, diagnostics: path, innerException: ex);
        }
        catch (IOException ex)
        {
            throw new MergeFailureException(MergeErrorReason.OutputUnavailable,
                "输出目录无法访问或写入，请确认硬盘已连接、目录可用。",
                stopQueue: true, diagnostics: path, innerException: ex);
        }
        catch (ArgumentException ex)
        {
            throw new MergeFailureException(MergeErrorReason.OutputUnavailable,
                "输出目录路径无效。", stopQueue: true, diagnostics: path, innerException: ex);
        }
    }
}
