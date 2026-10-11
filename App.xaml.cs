using System;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using Splice.Services;
using Splice.ViewModels;
using Splice.Views;

namespace Splice;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogging.Configure();
        DispatcherUnhandledException += (_, args) =>
            Log.Fatal(args.Exception, "WPF UI 线程未处理异常");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "进程未处理异常");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "未观察到的后台任务异常");
            args.SetObserved();
        };

        try
        {
            base.OnStartup(e);
            var diskSpace = new DiskSpaceService(Log.ForContext<DiskSpaceService>());
            var validation = new FileValidationService();
            var errorHandler = new ExceptionHandlerService(diskSpace);
            var notification = new MaterialDialogNotificationService(Log.ForContext<MaterialDialogNotificationService>());
            var window = new MainWindow
            {
                DataContext = new MainViewModel(
                    new FileDialogService(),
                    new PlaylistLocator(),
                    new FfmpegMergeService(Log.ForContext<FfmpegMergeService>(),
                        validation, diskSpace, errorHandler),
                    new ExplorerFolderOpener(),
                    new AppSettingsService(Log.ForContext<AppSettingsService>()),
                    Log.ForContext<MainViewModel>(),
                    errorHandler,
                    notification)
            };

            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Splice 启动失败");
            MessageBox.Show("程序启动失败，详情请查看本地日志。", "Splice", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Splice 正常退出");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
