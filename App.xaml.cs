using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System.Configuration;
using System.Data;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using TestingSoftware.Adapters;
using TestingSoftware.Models;
using TestingSoftware.Services;
using TestingSoftware.ViewModels;

namespace TestingSoftware
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// The host which used to implement the dependency injection.
        /// </summary>
        private readonly IHost _host;

        public static Serilog.ILogger? specialLogger;

        public static bool isStore = false;

        public static int AdapterModel = -1;
        public static bool isPause = false;
        public static bool isAnalysis = true;

        // 动态设置DLL搜索目录Win32 API
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectoryW(string lpPathName);

        public App()
        {
            _host = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                // SQLite
                services.AddSingleton<SqliteDbHelper>();
                services.AddSingleton<IDeviceTestRecordService, DeviceTestRecordService>();

                // Register the default root view model.
                services.AddSingleton<TestingSoftwareViewModel>();
                services.AddSingleton<BluetoothViewModel>();
                services.AddSingleton<ModBusService>();
                
                //services.AddSingleton<LineConfigPageViewModel>();
                

                // Register the UDS service
                services.AddSingleton<UdsService>();
                

                // Register the main window and intialilze its data context.
                services.AddSingleton(services => new MainWindow
                {
                    DataContext = services.GetRequiredService<TestingSoftwareViewModel>()
                });
            })
            .Build();

            _ = InitSqliteDbAsync();
        }

        private IntPtr ResolveVbacDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName.Equals("vbac.dll", StringComparison.OrdinalIgnoreCase))
            {
                string baseDir = AppContext.BaseDirectory;
                string archDir = Environment.Is64BitProcess
                    ? Path.Combine(baseDir, @"Lib\x64")
                    : Path.Combine(baseDir, @"Lib\x86");

                string fullDllPath = Path.Combine(archDir, "vbac.dll");
                if (File.Exists(fullDllPath))
                {
                    // 加载完整路径dll
                    return NativeLibrary.Load(fullDllPath);
                }
            }
            return IntPtr.Zero;
        }
        /// <summary>
        /// 程序启动初始化建表，后台执行
        /// </summary>
        private async Task InitSqliteDbAsync()
        {
            try
            {
                var dbHelper = _host.Services.GetRequiredService<SqliteDbHelper>();
                await dbHelper.InitDatabaseAsync();
                Log.Information("SQLite DeviceTestRecord 数据表初始化完成");
            }
            catch (Exception ex)
            {
               Log.Error(ex, "SQLite数据表初始化失败");
            }
        }

        /// <summary>
        /// The method will be called by framework to initialize the application.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnStartup(StartupEventArgs e)
        {

            string appName = "TestingSoftware";

            // 获取 LocalApplicationData 路径并构建日志目录
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logsDirectory = Path.Combine(localAppData, appName, "logs");

            // 确保日志目录存在（Serilog 不会自动创建）
            Directory.CreateDirectory(logsDirectory);

            // 主日志文件路径（每日滚动）
            string mainLogPath = Path.Combine(logsDirectory, "log-.txt");

            // 特殊日志文件路径（每小时滚动）
            string generalLogPath = Path.Combine(logsDirectory, "Data-.txt");

            // Initialize the logger.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .WriteTo.File(
                    path: mainLogPath,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ProcessId}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day,
                    flushToDiskInterval: TimeSpan.FromSeconds(30),
                    retainedFileCountLimit: 31)
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ProcessId}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                    theme: AnsiConsoleTheme.Code)  // 可选：使用代码主题以提升可读性
                .CreateLogger();
            specialLogger = new LoggerConfiguration()
                 .WriteTo.Async(async => async.File(
                        path: generalLogPath,
                        outputTemplate: "{Message:lj}",
                        rollingInterval: RollingInterval.Hour,
                        retainedFileCountLimit: 31,
                        buffered: true, // 启用缓冲
                        flushToDiskInterval: TimeSpan.FromSeconds(10), // 每10秒刷新缓冲区
                        fileSizeLimitBytes: 1_000_000_000 // 限制单个文件大小为 10MB
                    ))
                 .CreateLogger();
            try
            {
                Log.Information("Application starting...");
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application startup failed");
                Shutdown(1);
            }

            // Set the main windows and show it.
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();

            base.OnStartup(e);
            // 接管 "vbac.dll" 的dll解析
            NativeLibrary.SetDllImportResolver(typeof(App).Assembly, ResolveVbacDll);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application shutting down...");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

}
