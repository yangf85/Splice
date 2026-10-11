using System;
using System.IO;
using System.Globalization;

namespace Splice.Services;

public static class PlaylistDurationReader
{
    /// <summary>
    /// 尝试求和 HLS 媒体清单中的 EXTINF 时长；主播放列表可能没有时长。
    /// 仅用于估算进度，不参与视频合并。
    /// </summary>
    public static double? TryReadSeconds(string path)
    {
        try
        {
            double seconds = 0;
            var found = false;

            foreach (var line in File.ReadLines(path))
            {
                if (!line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase)) continue;
                var field = line[8..].Split(',', 2)[0].Trim();
                if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    && double.IsFinite(value) && value >= 0)
                {
                    seconds += value;
                    found = true;
                }
            }
            return found && seconds > 0 ? seconds : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
