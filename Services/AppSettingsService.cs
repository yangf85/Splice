using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace Splice.Services;

/// <summary>
/// 默认目录偏好设置，写入当前用户的 LocalAppData；不会在安装目录写文件。
/// 配置无效或不可写时优雅降级，不能影响视频合并功能。
/// </summary>
public sealed class AppSettingsService
{
    private readonly ILogger _logger;
    private readonly string _filePath;

    public AppSettingsService(ILogger logger)
    {
        _logger = logger;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Splice", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _logger.Warning(ex, "读取 Splice 最近使用目录失败，将使用默认目录");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var temporaryPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.Warning(ex, "保存 Splice 最近使用目录失败");
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
