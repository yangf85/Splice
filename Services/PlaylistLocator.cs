using System;
using System.IO;
using System.Linq;
namespace Splice.Services;

/// <summary>
/// 只在根目录和一级子目录寻找，避免大下载目录递归扫描耗时。
/// 优先使用根目录的 index.m3u8，忽略 .bak。
/// </summary>
public sealed class PlaylistLocator
{
    public string? Find(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        var primary = Path.Combine(folder, "index.m3u8");
        if (File.Exists(primary)) return primary;

        var rootList = Directory.EnumerateFiles(folder, "*.m3u8", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (rootList is not null) return rootList;

        var preferredSubfolder = Path.Combine(folder, "index");
        if (Directory.Exists(preferredSubfolder))
        {
            var preferred = Directory.EnumerateFiles(preferredSubfolder, "*.m3u8", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (preferred is not null) return preferred;
        }

        foreach (var subfolder in Directory.EnumerateDirectories(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var playlist = Directory.EnumerateFiles(subfolder, "*.m3u8", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (playlist is not null) return playlist;
        }

        return null;
    }
}
