using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Splice.Services;

namespace Splice.Models;

/// <summary>单个视频文件夹的合并任务。</summary>
public partial class MergeJob : ObservableObject
{
    public MergeJob(string sourceDirectory, string playlistPath)
    {
        SourceDirectory = sourceDirectory;
        PlaylistPath = playlistPath;
        FolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceDirectory));
        OutputFileName = OutputFileNaming.GetFileName(FolderName);
        Status = "等待处理";
        Detail = "等待开始";
    }

    public string SourceDirectory { get; }
    public string PlaylistPath { get; }
    public string FolderName { get; }

    [ObservableProperty]
    public partial string OutputFileName { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    [ObservableProperty]
    public partial string Detail { get; set; }

    [ObservableProperty]
    public partial string? OutputPath { get; set; }

    [ObservableProperty]
    public partial MergeErrorReason ErrorReason { get; set; }
}
