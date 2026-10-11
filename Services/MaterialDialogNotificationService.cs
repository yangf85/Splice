using System;
using System.Threading.Tasks;
using System.Windows;
using MaterialDesignThemes.Wpf;
using Serilog;
using Splice.Views.Dialogs;

namespace Splice.Services;

/// <summary>Material Design 对话框属于 UI 边界，由服务封装，不在 ViewModel 里创建控件。</summary>
public sealed class MaterialDialogNotificationService(ILogger logger) : INotificationService
{
    public const string HostId = "Splice.Notifications";

    public async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            await DialogHost.Show(new AlertDialogView(title, message), HostId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.Warning(ex, "Material 对话框不可用，改用 Windows 提示框");
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task<bool> ConfirmStopAndExitAsync()
    {
        try
        {
            var result = await DialogHost.Show(new ConfirmExitDialogView(), HostId);
            return string.Equals(result as string, "Stop", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.Warning(ex, "Material 关闭确认框不可用，改用 Windows 确认框");
            return MessageBox.Show("正在合并视频。是否停止任务并退出？", "Splice 视频拼接工具",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
    }
}
