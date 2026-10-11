using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Splice.Models;

namespace Splice.Services;

/// <summary>
/// FFmpeg 独立进程，异步读取 stdout/stderr。只有退出码 0 且临时文件非空时
/// 才将临时文件原子改名为最终 mp4；失败/取消不覆盖用户已有文件。
/// </summary>
public sealed class FfmpegMergeService : IMergeService
{
    private readonly ILogger _logger;
    private readonly FileValidationService _validator;
    private readonly DiskSpaceService _diskSpace;
    private readonly ExceptionHandlerService _errors;

    public FfmpegMergeService(ILogger logger, FileValidationService validator,
        DiskSpaceService diskSpace, ExceptionHandlerService errors)
    {
        _logger = logger;
        _validator = validator;
        _diskSpace = diskSpace;
        _errors = errors;
    }

    public async Task MergeAsync(string ffmpegPath, string playlistPath, string outputPath,
        IProgress<MergeProgress> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(ffmpegPath))
            throw new MergeFailureException(MergeErrorReason.FfmpegNotAvailable,
                "没有找到随程序提供的 FFmpeg，请重新安装完整的 Splice。", stopQueue: true,
                diagnostics: ffmpegPath);

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new MergeFailureException(MergeErrorReason.OutputUnavailable, "输出文件路径无效。", true);
        // 对慢速硬盘和网络共享的文件探测放到线程池，防止同步 I/O 卡住 WPF UI。
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _validator.ValidatePlaylist(playlistPath);
            _validator.ValidateOutputDirectory(outputDirectory);
            _diskSpace.EnsureMinimumFreeSpace(outputDirectory);
        }, cancellationToken);

        // 后缀仍是 mp4，FFmpeg 能根据 -f mp4 封装；保留随机 GUID 避免同名任务干扰。
        var temporaryPath = Path.Combine(outputDirectory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.splice.tmp.mp4");
        var durationSeconds = PlaylistDurationReader.TryReadSeconds(playlistPath);
        var errors = new Queue<string>();
        var success = false;
        var safeToDeleteTemporaryFile = true;
        var ffmpegArgs = new[]
        {
            "-hide_banner", "-nostdin", "-loglevel", "warning",
            "-progress", "pipe:1", "-nostats",
            "-allowed_extensions", "ALL",
            "-protocol_whitelist", "file,crypto,data,http,https,tcp,tls",
            "-i", playlistPath,
            "-map", "0:v?", "-map", "0:a?",
            "-c", "copy", "-movflags", "+faststart",
            "-f", "mp4", "-n", temporaryPath
        };
        var info = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = Path.GetDirectoryName(playlistPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in ffmpegArgs) info.ArgumentList.Add(arg);

        _logger.Information("开始合并：{Playlist} -> {Output}，参数：{Arguments}",
            playlistPath, outputPath, string.Join(" ", ffmpegArgs.Select(QuoteArgument)));

        using var process = new Process { StartInfo = info };
        var processStarted = false;
        try
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!process.Start())
                    throw new MergeFailureException(MergeErrorReason.FfmpegNotAvailable,
                        "FFmpeg 无法启动。", stopQueue: true);
                processStarted = true;
            }
            catch (Win32Exception ex)
            {
                throw new MergeFailureException(MergeErrorReason.FfmpegNotAvailable,
                    "FFmpeg 启动失败，请检查程序文件是否完整或被安全软件阻止。",
                    stopQueue: true, diagnostics: ffmpegPath, innerException: ex);
            }

            // 两路管道放在线程池异步读取；Progress<T> 自动把少量进度更新送回 UI。
            var readProgressTask = Task.Run(() => ReadProgressAsync(process.StandardOutput, durationSeconds, progress));
            var readStderrTask = Task.Run(() => ReadErrorsAsync(process.StandardError, errors, _logger));
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(readProgressTask, readStderrTask);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                // 先停止写入，确认进程退出后才能清理临时 MP4。
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    {
                        _logger.Warning(ex, "取消时无法终止 FFmpeg 进程");
                    }
                }
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(12));
                    await Task.WhenAll(readProgressTask, readStderrTask)
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
                {
                    _logger.Warning(ex, "取消后等待 FFmpeg/输出流结束超时");
                }
                safeToDeleteTemporaryFile = process.HasExited;
                throw;
            }

            if (process.ExitCode != 0)
            {
                var stderr = string.Join(Environment.NewLine, errors);
                throw _errors.ClassifyFfmpeg(process.ExitCode, stderr, outputDirectory);
            }

            if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                throw new MergeFailureException(MergeErrorReason.OutputWriteError,
                    "FFmpeg 未生成有效的 MP4 文件。", diagnostics: temporaryPath);

            // File.Move 不覆盖：若另一进程抢先生成同名目标，保留目标并把当前任务标记失败。
            File.Move(temporaryPath, outputPath, overwrite: false);
            success = true;
            _logger.Information("合并成功：{Output}", outputPath);
            progress.Report(new MergeProgress(100, "合并完成"));
        }
        catch (OperationCanceledException)
        {
            _logger.Information("用户取消合并：{Playlist}", playlistPath);
            throw;
        }
        catch (Exception ex)
        {
            var failure = _errors.Classify(ex, outputDirectory);
            _logger.Error(ex,
                "合并失败：{Reason}，停止队列：{StopQueue}，ExitCode={ExitCode}，输入={Playlist}，输出={Output}，诊断={Diagnostics}",
                failure.Reason, failure.StopQueue, failure.ExitCode, playlistPath, outputPath, failure.Diagnostics);
            if (ReferenceEquals(failure, ex)) throw;
            throw failure;
        }
        finally
        {
            // 任意错误都先尝试关闭子进程。否则 reader 抛错可能导致临时文件仍被写入。
            if (processStarted && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(12));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
                {
                    _logger.Warning(ex, "异常退出时无法确认 FFmpeg 已停止");
                }
            }
            safeToDeleteTemporaryFile = !processStarted || process.HasExited;
            if (!success && safeToDeleteTemporaryFile)
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Warning(ex, "临时 MP4 清理失败，需人工检查：{TemporaryPath}", temporaryPath);
                }
            }
            else if (!success)
            {
                _logger.Warning("FFmpeg 是否退出未确认，保留临时 MP4 以免与进程写入竞争：{TemporaryPath}", temporaryPath);
            }
        }
    }

    private static string QuoteArgument(string arg) =>
        arg.Contains(' ') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;

    private static async Task ReadErrorsAsync(StreamReader reader, Queue<string> errors, ILogger logger)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (errors.Count == 80) errors.Dequeue();
            errors.Enqueue(line);
            logger.Warning("FFmpeg: {Line}", line);
        }
    }

    private static async Task ReadProgressAsync(StreamReader reader, double? durationSeconds,
        IProgress<MergeProgress> progress)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (!line.StartsWith("out_time=", StringComparison.Ordinal)) continue;
            if (!TimeSpan.TryParse(line[9..].Trim(), CultureInfo.InvariantCulture, out var elapsed)) continue;
            var percent = durationSeconds is > 0
                ? Math.Clamp(elapsed.TotalSeconds / durationSeconds.Value * 100.0, 0, 99.0)
                : (double?)null;
            progress.Report(new MergeProgress(percent,
                $"已处理 {elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)}"));
        }
    }
}
