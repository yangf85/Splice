using System;
using System.Diagnostics;
using System.IO;

namespace Splice.Services;

/// <summary>通过 Windows shell 打开文件夹，而不是再次弹出文件夹选择器。</summary>
public sealed class ExplorerFolderOpener : IFolderOpener
{
    public void Open(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("请选择有效的文件夹路径。", nameof(folderPath));

        var fullPath = Path.GetFullPath(folderPath);
        Directory.CreateDirectory(fullPath);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
            Verb = "open"
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows 资源管理器未能启动。");
        }
    }
}
