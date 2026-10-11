using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Splice.Services;

public sealed class FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> ChooseSourceFolders(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "请选择一个或多个视频下载文件夹（可按 Ctrl 多选）",
            Multiselect = true,
            InitialDirectory = ExistingDirectoryOrEmpty(initialDirectory)
        };

        return dialog.ShowDialog() == true ? dialog.FolderNames : Array.Empty<string>();
    }

    public string? ChooseFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = ExistingDirectoryOrEmpty(initialDirectory)
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string ExistingDirectoryOrEmpty(string? directory)
        => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : string.Empty;
}
