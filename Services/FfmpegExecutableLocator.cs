using System;
using System.IO;

namespace Splice.Services;

/// <summary>仅使用随应用发布的 Tools/ffmpeg.exe，无 PATH 查询、无手动更换。</summary>
public static class FfmpegExecutableLocator
{
    public static string? Resolve()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : null;
    }
}
