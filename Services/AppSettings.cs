namespace Splice.Services;

/// <summary>只保存用户上次使用的目录；不保存任务队列或 FFmpeg 配置。</summary>
public sealed class AppSettings
{
    public string? LastInputDirectory { get; set; }
    public string? OutputDirectory { get; set; }
}
