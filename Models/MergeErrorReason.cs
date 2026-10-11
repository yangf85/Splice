namespace Splice.Models;

/// <summary>用户界面和日志可共用的稳定错误分类，而不是依赖 FFmpeg 的整段字符串。</summary>
public enum MergeErrorReason
{
    None,
    DiskFull,
    PermissionDenied,
    OutputUnavailable,
    OutputWriteError,
    SourceMissing,
    InvalidM3u8,
    FfmpegNotAvailable,
    FfmpegError,
    Unknown
}
