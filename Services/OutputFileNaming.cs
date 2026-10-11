using System;
using System.IO;

namespace Splice.Services;

/// <summary>
/// 把下载文件夹名转换为 MP4 文件名，并在输出目录中避免覆盖已有文件。
/// 例如「第一集.m3u8」=>「第一集.mp4」。
/// </summary>
public static class OutputFileNaming
{
    public static string GetFileName(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);

        var stem = folderName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? folderName[..^5]
            : folderName;

        if (string.IsNullOrWhiteSpace(stem))
            stem = "video";

        return $"{stem}.mp4";
    }

    public static string GetUniquePath(string outputDirectory, string folderName)
    {
        var preferredName = GetFileName(folderName);
        var firstPath = Path.Combine(outputDirectory, preferredName);
        if (!File.Exists(firstPath)) return firstPath;

        var stem = Path.GetFileNameWithoutExtension(preferredName);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = Path.Combine(outputDirectory, $"{stem} ({suffix}).mp4");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
