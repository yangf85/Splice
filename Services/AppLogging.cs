using System;
using System.IO;
using Serilog;

namespace Splice.Services;

/// <summary>统一初始化应用日志；不用依赖安装目录的写权限。</summary>
public static class AppLogging
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Splice", "Logs");

    public static void Configure()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(LogDirectory, "splice-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Splice 启动，日志目录：{LogDirectory}", LogDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 日志目录无法写入时不能阻止普通视频合并功能。
            Log.Logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
            System.Diagnostics.Debug.WriteLine($"Splice 日志不可用：{ex}");
        }
    }
}
