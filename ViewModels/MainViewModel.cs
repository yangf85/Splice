using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Splice.Models;
using Splice.Services;

namespace Splice.ViewModels;

/// <summary>
/// 合并任务的页面状态。文件选择、清单定位、视频合并均委托给独立服务。
/// 使用 CommunityToolkit.Mvvm 8.4.2 的 partial property 源代码生成。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly PlaylistLocator _playlistLocator;
    private readonly IMergeService _merger;
    private readonly IFolderOpener _folderOpener;
    private readonly ILogger _logger;
    private readonly AppSettingsService _appSettingsService;
    private readonly AppSettings _settings;
    private readonly ExceptionHandlerService _errors;
    public INotificationService Notifications { get; }
    private bool _initializing = true;
    private CancellationTokenSource? _runCancellation;

    public ObservableCollection<MergeJob> Jobs { get; } = [];
    private readonly HashSet<MergeJob> _observedJobs = [];

    // 统计信息是从任务状态派生的，不创建多份可变状态。
    public int TotalJobs => Jobs.Count;
    public int PendingJobs => Jobs.Count(j => j.Status == "等待处理" || j.Status == "已暂停");
    public int CompletedJobs => Jobs.Count(j => j.Status == "已完成");
    public int FailedJobs => Jobs.Count(j => j.Status is "失败" or "已取消");
    public bool HasJobs => Jobs.Count > 0;
    public bool IsFfmpegReady => FfmpegExecutableLocator.Resolve() is not null;
    public string FfmpegStatusText => IsFfmpegReady ? "FFmpeg 已就绪" : "FFmpeg 未找到";

    // TextBox 在失去焦点时更新绑定；保存后下次启动即可恢复输出目录。
    partial void OnOutputDirectoryChanged(string value)
    {
        if (_initializing || string.IsNullOrWhiteSpace(value)) return;
        _settings.OutputDirectory = value.Trim();
        _appSettingsService.Save(_settings);
    }

    private void RememberInputLocation(string sourceDirectory)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDirectory));
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return;
        _settings.LastInputDirectory = parent;
        _appSettingsService.Save(_settings);
    }

    private void OnJobsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 同步监听任务状态，在合并、完成、失败后实时更新顶部统计卡片。
        foreach (var job in _observedJobs.Except(Jobs).ToArray())
        {
            job.PropertyChanged -= OnJobPropertyChanged;
            _observedJobs.Remove(job);
        }
        foreach (var job in Jobs.Except(_observedJobs))
        {
            job.PropertyChanged += OnJobPropertyChanged;
            _observedJobs.Add(job);
        }

        RefreshMetrics();
        StartCommand.NotifyCanExecuteChanged();
    }

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeJob.Status)) RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        OnPropertyChanged(nameof(TotalJobs));
        OnPropertyChanged(nameof(PendingJobs));
        OnPropertyChanged(nameof(CompletedJobs));
        OnPropertyChanged(nameof(FailedJobs));
        OnPropertyChanged(nameof(HasJobs));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    public partial MergeJob? SelectedJob { get; set; }

    [ObservableProperty]
    public partial string OutputDirectory { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFoldersCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportSubfoldersCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial double OverallProgress { get; set; }

    public MainViewModel(
        IFileDialogService dialogs,
        PlaylistLocator playlistLocator,
        IMergeService merger,
        IFolderOpener folderOpener,
        AppSettingsService appSettingsService,
        ILogger logger,
        ExceptionHandlerService errors,
        INotificationService notifications)
    {
        _dialogs = dialogs;
        _playlistLocator = playlistLocator;
        _merger = merger;
        _folderOpener = folderOpener;
        _logger = logger;
        _errors = errors;
        Notifications = notifications;
        _appSettingsService = appSettingsService;
        _settings = _appSettingsService.Load();

        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
            videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        OutputDirectory = !string.IsNullOrWhiteSpace(_settings.OutputDirectory)
            ? _settings.OutputDirectory
            : Path.Combine(videos, "Splice");
        _initializing = false;

        var packagedFfmpeg = FfmpegExecutableLocator.Resolve();
        StatusMessage = packagedFfmpeg is null
            ? "未找到 FFmpeg：发布者需将 Tools/ffmpeg.exe 随程序发布。"
            : "FFmpeg 已就绪，请选择视频文件夹。";

        Jobs.CollectionChanged += OnJobsCollectionChanged;
    }

    private bool CanEdit() => !IsBusy;
    private bool CanStart() => !IsBusy && Jobs.Any(job => job.Status != "已完成");
    private bool CanRemoveSelected() => !IsBusy && SelectedJob is not null;
    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddFolders() => AddFolderPaths(
        _dialogs.ChooseSourceFolders(_settings.LastInputDirectory));

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ImportSubfolders()
    {
        var parent = _dialogs.ChooseFolder("选择包含多个下载文件夹的父目录", _settings.LastInputDirectory);
        if (parent is null) return;

        // 下次打开选择窗口时，从这次选择的父目录开始。
        _settings.LastInputDirectory = parent;
        _appSettingsService.Save(_settings);

        try
        {
            // 仅扫描一级子文件夹，不进入 ts / m4s 视频分片目录。
            AddFolderPaths(Directory.EnumerateDirectories(parent).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFailure($"读取父目录失败：{ex.Message}");
        }
    }

    /// <summary>拖放等 UI 入口在 View 中提取路径，交由 ViewModel 统一处理。</summary>
    public void AddFolderPaths(IEnumerable<string> folders)
    {
        if (IsBusy) return;

        var added = 0;
        var skipped = 0;
        string? firstValidFolder = null;
        foreach (var rawPath in folders)
        {
            try
            {
                if (!Directory.Exists(rawPath))
                {
                    skipped++;
                    continue;
                }

                var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawPath));
                firstValidFolder ??= folder;
                if (Jobs.Any(x => string.Equals(x.SourceDirectory, folder, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                var playlist = _playlistLocator.Find(folder);
                if (playlist is null)
                {
                    skipped++;
                    _logger.Warning("跳过 {Folder}：未找到 .m3u8 文件", folder);
                    continue;
                }

                Jobs.Add(new MergeJob(folder, playlist));
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                skipped++;
                _logger.Warning(ex, "导入视频文件夹失败：{Folder}", rawPath);
            }
        }

        if (firstValidFolder is not null) RememberInputLocation(firstValidFolder);
        StatusMessage = $"成功添加 {added} 个文件夹，跳过 {skipped} 个。";
        _logger.Information("文件夹导入完成：添加 {Added}，跳过 {Skipped}", added, skipped);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseOutput()
    {
        var path = _dialogs.ChooseFolder("选择 MP4 输出目录", OutputDirectory);
        if (path is not null) OutputDirectory = path;
    }

    [RelayCommand]
    private void OpenOutputDirectory()
    {
        try
        {
            _folderOpener.Open(OutputDirectory);
            _logger.Information("已打开输出目录：{Directory}", OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ReportFailure($"无法打开输出目录：{ex.Message}", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        if (SelectedJob is null) return;
        Jobs.Remove(SelectedJob);
        SelectedJob = null;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Clear()
    {
        Jobs.Clear();
        SelectedJob = null;
        OverallProgress = 0;
        StatusMessage = "任务列表已清空。";
    }

    /// <summary>窗口关闭时先终止 FFmpeg 并等待任务结束，避免进程仍在写文件。</summary>
    public async Task CancelAndWaitAsync()
    {
        _runCancellation?.Cancel();
        if (StartCommand.ExecutionTask is { } runningTask)
            await runningTask;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _logger.Information("用户取消批量合并");
        _runCancellation?.Cancel();
        StatusMessage = "正在取消当前合并任务……";
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            ReportFailure("请先设置 MP4 输出文件夹。");
            return;
        }

        var ffmpegExecutable = FfmpegExecutableLocator.Resolve();
        if (ffmpegExecutable is null)
        {
            ReportFailure("未找到 FFmpeg。请确认发布目录含有 Tools/ffmpeg.exe。");
            await Notifications.ShowErrorAsync("无法开始合并", "内置 FFmpeg 文件缺失，请重新安装完整的 Splice。");
            return;
        }

        // 创建输出目录、测试写权限和查询磁盘空间都交给服务层的异步预检。
        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        var token = cancellation.Token;
        var destinationDirectory = OutputDirectory;
        var todo = Jobs.Where(j => j.Status != "已完成").ToList();
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        MergeFailureException? queueStop = null;
        OverallProgress = 0;

        try
        {
            foreach (var job in todo)
            {
                if (token.IsCancellationRequested) break;
                job.Status = "正在合并";
                job.ErrorReason = MergeErrorReason.None;
                job.ProgressPercent = 0;
                job.IsIndeterminate = true;
                job.Detail = "正在启动 FFmpeg";
                job.OutputPath = null;
                StatusMessage = $"正在合并：{job.FolderName}";
                _logger.Information("开始合并任务 {FolderName}，播放列表 {Playlist}", job.FolderName, job.PlaylistPath);

                try
                {
                    var outputPath = OutputFileNaming.GetUniquePath(destinationDirectory, job.FolderName);
                    job.OutputFileName = Path.GetFileName(outputPath);
                    var report = new Progress<MergeProgress>(p =>
                    {
                        if (job.Status != "正在合并") return;
                        job.IsIndeterminate = p.Percent is null;
                        if (p.Percent is { } percent) job.ProgressPercent = percent;
                        job.Detail = p.Detail;
                        OverallProgress = 100.0 * (processed + (p.Percent ?? 0) / 100.0) / todo.Count;
                    });

                    await _merger.MergeAsync(ffmpegExecutable, job.PlaylistPath, outputPath, report, token);
                    job.Status = "已完成";
                    job.ProgressPercent = 100;
                    job.IsIndeterminate = false;
                    job.Detail = "已保存";
                    job.OutputPath = outputPath;
                    succeeded++;
                    _logger.Information("合并成功：{Output}", outputPath);
                }
                catch (OperationCanceledException)
                {
                    job.Status = "已取消";
                    job.IsIndeterminate = false;
                    job.Detail = "用户取消";
                    _logger.Information("已取消合并：{FolderName}", job.FolderName);
                    break;
                }
                catch (Exception ex)
                {
                    var failure = _errors.Classify(ex, destinationDirectory);
                    job.Status = "失败";
                    job.IsIndeterminate = false;
                    job.ErrorReason = failure.Reason;
                    job.Detail = failure.Message;
                    failed++;
                    _logger.Error(ex,
                        "视频合并失败：{FolderName}，分类={Reason}，停止队列={StopQueue}，输入={Playlist}，诊断={Diagnostics}",
                        job.FolderName, failure.Reason, failure.StopQueue, job.PlaylistPath, failure.Diagnostics);
                    if (failure.StopQueue)
                    {
                        queueStop = failure;
                        break;
                    }
                }
                finally
                {
                    processed++;
                    OverallProgress = 100.0 * processed / todo.Count;
                }
            }

            if (queueStop is not null)
            {
                // 未执行任务仍保留在队列，可在修复存储问题后再次点击“开始合并”。
                foreach (var remaining in todo.Where(x => x.Status == "等待处理"))
                {
                    remaining.Status = "已暂停";
                    remaining.Detail = "等待修复输出问题后继续";
                }
                StatusMessage = $"队列已暂停：{queueStop.Message} 已完成 {succeeded} 个，失败 {failed} 个。";
            }
            else
            {
                StatusMessage = token.IsCancellationRequested
                    ? $"操作已取消。成功 {succeeded} 个，失败 {failed} 个。"
                    : $"处理结束。成功 {succeeded} 个，失败 {failed} 个。";
            }
            _logger.Information("批量任务结束：成功={Succeeded}，失败={Failed}，取消={Canceled}，队列暂停={Stopped}",
                succeeded, failed, token.IsCancellationRequested, queueStop is not null);
        }
        finally
        {
            _runCancellation = null;
            IsBusy = false;
            StartCommand.NotifyCanExecuteChanged();
        }

        // 先结束忙碌状态再通知，避免关闭窗口时重复请求取消。
        if (queueStop is not null)
            await Notifications.ShowErrorAsync("合并队列已暂停", queueStop.Message + "\n\n已完成的 MP4 会保留。修复问题后可再次点击“开始合并”。");
    }

    private void ReportFailure(string message, Exception? ex = null)
    {
        StatusMessage = message;
        if (ex is null) _logger.Warning("{Message}", message);
        else _logger.Error(ex, "{Message}", message);
    }
}
