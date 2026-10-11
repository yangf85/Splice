namespace Splice.Services;

/// <summary>在系统文件管理器中打开文件夹。可在单元测试中替换。</summary>
public interface IFolderOpener
{
    void Open(string folderPath);
}
