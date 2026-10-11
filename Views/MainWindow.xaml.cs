using System;
using System.ComponentModel;
using Serilog;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Linq;
using System.Windows;
using Splice.ViewModels;

namespace Splice.Views;

public partial class MainWindow : Window
{
    private bool _closingInProgress;
    private bool _approvedClose;
    public MainWindow() => InitializeComponent();

    // Windows 11 默认对顶层窗口做圆角处理；禁用它，与全局直角 UI 保持一致。
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            const int DWMWCP_DONOTROUND = 1;
            var preference = DWMWCP_DONOTROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 老系统 DWM 不支持时，保持默认窗口边框。
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int valueSize);

    // Purely visual window behaviors belong to the View, not the merge ViewModel.
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);


    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_approvedClose || DataContext is not MainViewModel { IsBusy: true } viewModel)
            return;

        // Closing 是同步事件：立即取消关闭，再异步询问，不阻塞 UI 消息循环。
        e.Cancel = true;
        if (_closingInProgress) return;
        _closingInProgress = true;
        try
        {
            if (!await viewModel.Notifications.ConfirmStopAndExitAsync()) return;
            await viewModel.CancelAndWaitAsync();
            _approvedClose = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "取消 FFmpeg 并关闭窗口时发生错误");
            MessageBox.Show("无法安全停止当前合并，请稍后重试。详情已记录到日志。",
                "Splice 视频拼接工具", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _closingInProgress = false;
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && paths.Any(Directory.Exists)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        viewModel.AddFolderPaths(paths.Where(Directory.Exists));
        e.Handled = true;
    }
}
