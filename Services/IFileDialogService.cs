using System.Collections.Generic;

namespace Splice.Services;

public interface IFileDialogService
{
    IReadOnlyList<string> ChooseSourceFolders(string? initialDirectory);
    string? ChooseFolder(string title, string? initialDirectory);
}
