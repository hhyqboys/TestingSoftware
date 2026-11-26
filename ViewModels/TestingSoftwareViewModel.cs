using ControlzEx.Standard;
using Serilog;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Serialization;
using TestingSoftware.Adapters;
using TestingSoftware.Commands;
using TestingSoftware.Models;
using TestingSoftware.Protocols;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Services;
using TestingSoftware.Utilities;
using TestingSoftware.View;
using Windows.Foundation.Collections;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using VbaStatus = TestingSoftware.Services.VbaStatus;

namespace TestingSoftware.ViewModels
{
    public class TestingSoftwareViewModel : ViewModel<TestingSoftwareModel>
    {
        public const int JOB_IDLE = 0;   // 空闲/重置
        public const int JOB_RUNNING = 1;   // 测试中
        public const int JOB_PASS = 2;   // 通过
        public const int JOB_FAIL = -1;  // 失败（终态，落库）
        public const int JOB_RETRYING = -2;  // 失败但可重试
        public const int JOB_STOPPED = -3;  // 手动停止

        public const UInt16 SERIAL_NUM = 0xF18d;
        private CancellationTokenSource? _jobCts;
        private int _retestStartIndex = 0;
        private CancellationTokenSource? _job3E80Cts;
        private Task? _jobMainTask;
        private Task? _job3ETask;
        private CancellationToken token;
        private CancellationToken token3E80;
        public const UInt16 DID_PV_SN = 0x0003;
        /// <summary>
        /// The singleton uds service.
        /// </summary>
        private UdsService _udsService;
        /// <summary>
        /// The singleton diag service.
        /// </summary>
        private McuSecurityService _mcuSecurityService;
        /// <summary>
        /// The singleton modbus service.
        /// </summary>
        private ModBusService _modBusService;
        private bool _isConnectButtonEnabled = false;

        /// <summary>
        /// 配置开关：为 true 时开始测试跳过所有 IO 模块（ModBus DI/DO/AI/AO）操作。
        /// 见 <see cref="TestingSoftwareModel.SkipIoModules"/>。
        /// </summary>
        private bool SkipIoModules => Model != null && Model.SkipIoModules;

        /// <summary>
        /// 配置开关：为 true 时开始测试跳过所有 IO 模块（ModBus DI/DO/AI/AO）操作。
        /// 见 <see cref="TestingSoftwareModel.SkipIoModules"/>。
        /// </summary>
        private bool SkipCanModules => Model != null && Model.SkipCanModules;

        /// <summary>串口自动扫描定时器（启动后每 1 秒刷新一次串口列表）。</summary>
        private readonly DispatcherTimer? _serialScanTimer;
        /// <summary>
        /// The bluetooth view model.
        /// </summary>
        public SumartBluetoothAdapter BluetoothAdapter { get; set; } = new SumartBluetoothAdapter();

        private readonly CsvExcelHelper _csvHelper = new CsvExcelHelper();


        private readonly IDeviceTestRecordService _testRecordService;

        /// <summary>
        /// The label on connection command
        /// </summary>
        public string ConnectionLabel { get; set; } = "链接";

        private string _fileName;
        private int AccumulatedLength = 0;
        private int MaximumSignalLength = 60;

        public BluetoothViewModel BluetoothViewAdapter { get; set; }

        /// <summary>
        /// 实时监控面板（母线电流 / 转速 / 电机温度 / 故障码），
        /// 采用“2C 定义动态 DID + 2A 开启主动上传”的方式接收下位机周期上报。
        /// </summary>
        public RealtimeMonitorViewModel Monitor { get; }

        /// <summary>
        /// 下位机 IO（GPIO）外部控制视图模型：2F 接管油门/刹车/档位输入通道。
        /// </summary>
        public IoControlViewModel IoControl { get; }

        /// <summary>
        /// 断开与再次连接之间的强制最小间隔（毫秒）：给 Windows 与下位机留出释放旧链路的时间。
        /// </summary>
        private const long ReconnectGuardMs = 3_000;

        /// <summary>最近一次断开动作完成的时刻（HighResolutionTimer.Milliseconds）。</summary>
        private long _lastDisconnectMs;

        /// <summary>扫码自动流程：扫码值稳定多久后才动作，避免逐字符输入时被反复触发（毫秒）。</summary>
        private const int ScanDebounceMs = 400;

        /// <summary>扫码 SN 后等待蓝牙连接就绪的最长时间（毫秒）。</summary>
        private const int AutoStartConnectWaitMs = 30_000;

        /// <summary>MAC 扫码防抖令牌。</summary>
        private CancellationTokenSource? _macScanDebounceCts;

        /// <summary>SN 扫码防抖令牌。</summary>
        private CancellationTokenSource? _snScanDebounceCts;

        /// <summary>是否已有扫码触发的自动连接在跑（0/1）。</summary>
        private int _autoConnectRunning;

        /// <summary>手动停止标记：只有用户点“停止”才落 JOB_STOPPED；开始/复测取消旧任务不算手动停止。</summary>
        private volatile bool _manualStopRequested;

        /// <summary>测试中异常断连的重连互斥（0=空闲，1=正在重连），避免断连事件与看门狗重复触发。</summary>
        private int _reconnectRunning;

        /// <summary>链路假死看门狗定时器（1 秒一次，只在测试中生效）。</summary>
        private DispatcherTimer? _linkWatchdogTimer;

        /// <summary>当前在跑的扫码自动连接任务，供 SN 流程复用等待。</summary>
        private Task? _autoConnectTask;

        /// <summary>是否已有扫码触发的自动启动测试流程在跑（0/1）。</summary>
        private int _autoStartRunning;

        /// <summary>最近一次扫码自动连接成功的 MAC，避免重复连接。</summary>
        private string _lastAutoConnectedMac = string.Empty;

        private bool _canClickConnect = true;

        /// <summary>
        /// “链接/断开”按钮是否可点击：断开后 3 秒内为 false，防止用户高频反复建连。
        /// </summary>
        public bool CanClickConnect
        {
            get => _canClickConnect;
            private set
            {
                _canClickConnect = value;
                OnPropertyChanged(nameof(CanClickConnect));
            }
        }

        /// <summary>
        /// 线程安全地设置按钮可点击状态（断开可能发生在蓝牙事件线程，不能直接改依赖属性）。
        /// </summary>
        private void SetCanClickConnect(bool value)
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(() => CanClickConnect = value);
                return;
            }

            CanClickConnect = value;
        }

        /// <summary>
        /// 在指定时长内禁用连接按钮，到时自动恢复。
        /// 用 Task.Delay 而非 DispatcherTimer：本方法可能在非 UI 线程调用，DispatcherTimer 会因线程归属而永不触发。
        /// </summary>
        private void LockReconnectFor(int milliseconds)
        {
            SetCanClickConnect(false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(milliseconds);
                }
                finally
                {
                    SetCanClickConnect(true);
                }
            });
        }


        /// <summary>
        /// The list of jobs that are currently visible in the UI.
        /// </summary>
        public ObservableCollection<Job> VisibleJobs { get; } = new ObservableCollection<Job>();
        /// <summary>
        /// The currently selected job in the UI.
        /// </summary>
        public Job? CurrentJob
        {
            get => _currentJob;
            set
            {
                _currentJob = value;
                OnPropertyChanged(nameof(CurrentJob));
            }
        }

        /// <summary>
        /// The list of available serial ports on the system.
        /// </summary>
        public ObservableCollection<string> SerialPortList { get; set; } = new ObservableCollection<string>();
        /// <summary>
        /// The selected serial port from the list.
        /// </summary>
        public string SelectedSerialPort
        {
            get => _selectedSerialPort;
            set
            {
                _selectedSerialPort = value;
                Properties.Settings.Default.LastSerialPort = value;
                Properties.Settings.Default.Save();
                OnPropertyChanged(nameof(SelectedSerialPort));
            }
        }
        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged(nameof(FileName));
            }
        }

        public string Title
        {
            get => Model.Title;
            set
            {
                Model.Title = value;
                OnPropertyChanged(nameof(Title));
            }
        }
        public string ScanSn
        {
            get => _scanSn;
            set
            {
                _scanSn = value;
                OnPropertyChanged(nameof(ScanSn));
                if (IsValidSnFormat(_scanSn))
                {
                    TestStatusInfo = string.Empty;
                    Status = 0;
                    if (!string.IsNullOrEmpty(_scanSn) && _scanSn.Length > 5)
                    {
                        ReTestSn = _scanSn.Substring(_scanSn.Length - 5);
                    }

                    // SN 校验通过：防抖后尝试自动启动测试（未连接则先等蓝牙连上再启动）
                    ScheduleAutoStartAfterSnScan();
                }
                else
                {
                    ReTestSn = string.Empty;
                    TestStatusInfo = "请输入正确的SN码";
                    Status = -1;
                }
            }
        }
        /// <summary>
        /// 校验SN码
        /// </summary>
        public string ReTestSn
        {
            get => _reTestSn;
            set
            {
                _reTestSn = value;
                OnPropertyChanged(nameof(ReTestSn));
            }
        }
        /// <summary>
        /// 校验SN码
        /// </summary>
        public string TestStatusInfo
        {
            get => _testStatusInfo;
            set
            {
                _testStatusInfo = value;
                OnPropertyChanged(nameof(TestStatusInfo));
            }
        }

        public int Status
        {
            get => _status;
            set
            {
                _status = value;
                Log.Debug($"Status changed to {_status}");
                ChangeUI();
                OnPropertyChanged(nameof(Status));
            }
        }
        ///// <summary>
        ///// 
        ///// </summary>
        //private LineConfigPageViewModel _lineConfigPageView;




        /// <summary>
        /// Controls connect button enabled/disabled state, triggers property change
        /// </summary>
        public bool IsConnectButtonEnabled
        {
            get => _isConnectButtonEnabled;
            set
            {
                _isConnectButtonEnabled = value;
                OnPropertyChanged(nameof(IsConnectButtonEnabled));

            }
        }

        #region XmlSerializer 缓存（重要，防止内存泄漏）
        private static XmlSerializer _modelSerializer;
        private string _selectedSerialPort;
        private Job? _currentJob;
        private string _scanSn = string.Empty;
        private string _reTestSn = string.Empty;
        private string _testStatusInfo = string.Empty;
        private int _status = 0;
        private List<BluetoothInfo>? _slaveDevices;
        private BluetoothInfo _selectedSlaveDevInfo;


        // 测试中状态：JOB_RUNNING(1) 或 JOB_RETRYING(-2)
        private bool IsTesting => Status == JOB_RUNNING || Status == JOB_RETRYING;

        // 设备已连接
        private bool IsDeviceConnected => BluetoothViewAdapter?.Running == true;

        // 可复测：上一轮以失败(-1)或手动停止(-3)结束
        private bool CanRetest => !IsTesting && (Status == JOB_FAIL || Status == JOB_STOPPED);


        /// <summary>
        /// 构造Serializer，传入所有DiagService派生类型
        /// </summary>
        private XmlSerializer GetSerializer()
        {
            if (_modelSerializer == null)
            {

                _modelSerializer = new XmlSerializer(typeof(TestingSoftwareModel));
            }
            return _modelSerializer;
        }
        private bool _isScanning = true;
        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(nameof(IsScanning)); }
        }


        public ObservableCollection<BluetoothInfo> SlaveDevices { get; } = new ObservableCollection<BluetoothInfo>();

        public BluetoothInfo SelectedSlaveDevInfo
        {
            get => _selectedSlaveDevInfo ?? new BluetoothInfo();
            set
            {
                _selectedSlaveDevInfo = value;
                BluetoothViewAdapter.SelectedSlaveDevInfo = value;
                OnPropertyChanged(nameof(SelectedSlaveDevInfo));
            }
        }

        private string _macAddressInput = string.Empty;
        /// <summary>
        /// 蓝牙MAC地址输入（不带冒号，12位十六进制）
        /// </summary>
        public string MacAddressInput
        {
            get => _macAddressInput;
            set
            {
                _macAddressInput = (value ?? string.Empty).ToUpperInvariant();
                OnPropertyChanged(nameof(MacAddressInput));
                // 与SN一致：扫码回车(UpdateSource)后立即校验并在右侧提示框显示
                if (!TryNormalizeMac(_macAddressInput, out string macHex, out string macError))
                {
                    Status = -1;
                    TestStatusInfo = macError;
                }
                else if (!string.IsNullOrEmpty(macHex))
                {
                    Status = 0;
                    TestStatusInfo = string.Empty;

                    // MAC 校验通过：防抖后尝试自动连接
                    ScheduleAutoConnectAfterMacScan(macHex);
                }
            }
        }
        #endregion
        /// <summary>
        /// 获取 AppData\Roaming\ElectricControl 路径，不存在则创建，返回绝对路径
        /// </summary>
        /// <returns>绝对路径</returns>
        public static string GetElectricControlFolder()
        {
            // 获取当前用户 AppData\Roaming
            string roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 拼接目标路径
            string targetFolder = Path.Combine(roamingPath, "ElectricControl");

            // 如果不存在则创建
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            // 返回绝对路径
            return targetFolder;
        }
        public TestingSoftwareViewModel(BluetoothViewModel sumartBluetoothAdapter,
             UdsService udsService, ModBusService modBusService,
             IDeviceTestRecordService testRecordService)
        {
            _udsService = udsService;
            _csvHelper.Init(GetElectricControlFolder() + "\\SN设备映射表.csv");
            _modBusService = modBusService;
            BluetoothViewAdapter = sumartBluetoothAdapter;
            BluetoothViewAdapter = sumartBluetoothAdapter;
            BluetoothViewAdapter.ScanStateChanged += (s, started) =>
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsScanning = !started;   // 语义与蓝牙VM一致：扫描中=false，空闲=true
                });
            };
            BluetoothViewAdapter.BluetoothDeviceFound += async (s, e) =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SlaveDevices.Clear();
                    foreach (var dev in BluetoothViewAdapter.SlaveDevices)
                    {
                        SlaveDevices.Add(dev);
                    }
                    SelectedSlaveDevInfo = BluetoothViewAdapter.SelectedSlaveDevInfo;
                    IsScanning = BluetoothViewAdapter.IsScanning;
                    OnPropertyChanged(nameof(SelectedSlaveDevInfo));
                    OnPropertyChanged(nameof(IsScanning));
                });
            };


            _mcuSecurityService = new McuSecurityService(_udsService);
            // 实时监控面板（动态 DID + 2A 主动上传），与 UDS 服务共用同一链路
            Monitor = new RealtimeMonitorViewModel(_udsService, _mcuSecurityService);
            // 下位机 IO（GPIO）外部控制：2F 0xD100/0xD101/0xD102，可与实时监控并存
            IoControl = new IoControlViewModel(_udsService, _mcuSecurityService);
            //_lineConfigPageView = lineConfigPageView;
            _selectedSerialPort = Properties.Settings.Default.LastSerialPort;
            // 启动后立即扫一次串口，并每 1 秒自动刷新（界面不再提供手动扫描按钮）
            RefreshSerialPorts();
            _serialScanTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _serialScanTimer.Tick += (s, e) => RefreshSerialPorts();
            _serialScanTimer.Start();

            // 链路假死看门狗：测试中持续发请求却长时间收不到任何下行报文 → 判定假死并自动重连
            _linkWatchdogTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _linkWatchdogTimer.Tick += (s, e) => CheckLinkAlive();
            _linkWatchdogTimer.Start();
            if (Model == null)
            {
                Model = new TestingSoftwareModel();
            }

            if (BluetoothViewAdapter.Model != null)
            {
                SumartBluetoothAdapter.OnUnexpectedDisconnect += async (s, e) =>
                {
                    bool testing = IsTesting;
                    await DisconnectAsync();
                    App.isPause = false;
                    SetConnectionState(false);

                    if (testing)
                    {
                        // 测试中异常断连：不弹框、不中止测试，直接进入自动重连，成功后继续跑
                        Log.Warning("测试中检测到蓝牙异常断开，启动自动重连");
                        TestStatusInfo = "链路断开，正在自动重连…";
                        await ReconnectDuringTestAsync();
                    }
                    else
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Log.Error(
                                "蓝牙连接意外断开，请检查蓝牙状态后重新连接！!",
                                "蓝牙断开",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning
                            );
                        });
                    }
                    OnPropertyChanged(nameof(ConnectAction));
                };
            }

            // ==========程序启动初始化：自动加载上次工程配置==========
            string lastPath = Properties.Settings.Default.LastProjectPath;
            if (!string.IsNullOrWhiteSpace(lastPath) && File.Exists(lastPath))
            {
                try
                {
                    LoadProjectFile(lastPath);
                }
                catch (Exception ex)
                {
                    // 上次文件损坏/无法打开，清空记录，不阻塞程序启动
                    Properties.Settings.Default.LastProjectPath = string.Empty;
                    Properties.Settings.Default.Save();
                    MessageBox.Show($"自动加载上次配置失败：{ex.Message}");
                }
            }
            _testRecordService = testRecordService;

            //// 测试成功
            //OnDeviceTestFinished("SN‑0001", "AA:BB:CC:DD:EE:FF", true, "");

            //// 测试失败
            //OnDeviceTestFinished("SN‑0002", "AA:BB:CC:DD:EE:01", false, "蓝牙连接超时，无应答");
        }

        /// <summary>
        /// 测试完成后调用落库
        /// </summary>
        public async void OnDeviceTestFinished(string sn, string mac, bool isSuccess, string errorMsg)
        {
            try
            {
                int status = isSuccess ? 0 : 1;
                // 直接一行写入sqlite，不需要UI，后台静默存储
                await _testRecordService.AddTestRecordAsync(sn, mac, status, errorMsg);
            }
            catch (Exception ex)
            {
                // 落库失败不要把业务搞崩，只记日志
                App.specialLogger?.Error(ex, "设备测试记录写入sqlite失败 SN:{Sn}", sn);
            }
        }

        /// <summary>
        /// 公共加载工程方法：开机自动加载 / Open对话框打开 共用
        /// </summary>
        private void LoadProjectFile(string filePath)
        {
            TestingSoftwareModel? loadedModel;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var ser = GetSerializer();
                loadedModel = ser.Deserialize(fs) as TestingSoftwareModel;
            }

            if (loadedModel == null)
            {
                MessageBox.Show("解析配置文件失败");
                return;
            }

            FileName = filePath;
            Model = loadedModel;

            // 实时监控通道沿用工程里的采样标定参数（RShunt / Aop / AdcRefVolt …）
            Monitor.Configure(loadedModel);

            //重建内部引用
            if (loadedModel.Jobs != null)
            {
                foreach (var job in loadedModel.Jobs)
                {
                    if (job.Services != null)
                    {
                        job.SignalViewModels.Clear();
                        foreach (var service in job.Services)
                        {
                            foreach (var sig in service.Signals)
                            {
                                sig.Aop = loadedModel.Aop;
                                sig.RShunt = loadedModel.RShunt;
                                sig.VbusAop = loadedModel.VbusAop;
                                sig.AdcRefVolt = loadedModel.AdcRefVolt;
                                sig.NominalBusVoltage = loadedModel.NominalBusVoltage;
                                sig.UpdateAffine();
                                sig.BuildReferences();
                                job.SignalViewModels.Add(new SignalViewModel(sig));
                            }
                        }
                    }
                }
            }
            if (loadedModel.IsConsole)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
               {
                   ConsoleWindow.Show();
               });
            }
            else
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ConsoleWindow.Hide();
                });

            }
            // 成功加载，保存路径到用户配置
            Properties.Settings.Default.LastProjectPath = filePath;
            Properties.Settings.Default.Save();
            SyncJobsToView();
            OnModelChanged();
        }


        private void SyncJobsToView()
        {
            VisibleJobs.Clear();
            if (Model?.Jobs == null) return;
            foreach (var job in Model.Jobs)
            {
                VisibleJobs.Add(job);
            }
        }

        public void ChangeUI()
        {
            App.Current.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(ConnectAction));
                OnPropertyChanged(nameof(ScanSerialAction));
                OnPropertyChanged(nameof(StopJobAction));
                OnPropertyChanged(nameof(StartJobAction));
                OnPropertyChanged(nameof(RetestJobAction));
            });
        }
        /// <summary>
        /// 打开下位机 IO（GPIO）外部控制窗口。
        /// </summary>
        public ICommand OpenIoControlAction => new SyncCommand(sender =>
        {
            IoControlWindow window = new IoControlWindow
            {
                Owner = Application.Current?.MainWindow,
                DataContext = IoControl
            };
            window.Show();
        });

        /// <summary>
        /// The method is to connect to a Bluetooth device.
        /// </summary>
        public ICommand ConnectAction => new AsyncCommand(async p =>
        {
            if (FileName == "untitled.xml")
            {
                Status = -1;
                TestStatusInfo = $"配置文件未加载";
                return;
            }
            int result = 0;

            if (BluetoothViewAdapter.Model?.Running ?? false)
            {
                // TODO:  
                //await Monitor.StopAsync();

                // Disconnect the uart after closing all modules which dependent on the uart.
                result = await DisconnectAsync();
                if (result == 0)
                {
                    // 断开成功后同样清掉连接失败的错误提示
                    if (Status == -1 && TestStatusInfo == "设备打开关闭失败")
                    {
                        Status = 0;
                        TestStatusInfo = string.Empty;
                    }

                    SetConnectionState(false);
                }
            }
            else
            {
                // 扫码已触发自动连接且仍在进行中：避免与手动点击并发建连
                if (Volatile.Read(ref _autoConnectRunning) == 1)
                {
                    Log.Information("扫码自动连接正在进行，忽略本次手动连接请求");
                    return;
                }

                result = await ConnectAsync();
                if (result == 0)
                {
                    // 连接成功要清掉上一次失败留下的红色提示，否则底部仍显示“设备打开关闭失败”
                    Status = 0;
                    TestStatusInfo = string.Empty;

                    // 连接成功后按钮必须立即可用（此时点它是“断开”，不受 3 秒锁限制）
                    SetCanClickConnect(true);

                    SetConnectionState(true);

                    // 连接成功后自动开启实时监控（动态 DID + 2A 主动上传）；
                    // 开启失败自动重试 3 次，每次间隔 500ms。
                    await Monitor.StartWithRetryAsync();
                }
            }

            if (result != 0)
            {
                Status = -1;
                // 保留 ConnectAsync 中已设置的具体错误信息（如MAC格式错误），仅在无具体信息时显示通用提示
                if (string.IsNullOrEmpty(TestStatusInfo) || TestStatusInfo == "未执行测试项目")
                {
                    TestStatusInfo = $"设备打开关闭失败";
                }
            }
            ChangeUI();
        });

        /// <summary>
        /// The method is to connect to a Bluetooth device.
        /// </summary>
        public ICommand ScanAction => new AsyncCommand(async p =>
        {

            await BluetoothViewAdapter.ScanAction();

        });

        /// <summary>
        /// The method is used to stop the protocol stack and listening.
        /// </summary>
        /// <returns></returns>
        public async Task<int> DisconnectAsync()
        {
            int result = -1;
            // 断开前先关闭实时监控（2A STOP + 2C 清除定义），避免下位机持续上报占用链路
            try
            {
                await Monitor.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "断开连接前停止实时监控失败");
            }

            // 必须先把 UDS 停掉：否则上一轮的接收任务与旧 GATT 订阅会残留，重连后新特征没人监听
            try
            {
                await _udsService.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "断开连接前停止 UDS 服务失败");
            }

            result = await (BluetoothViewAdapter.Model?.StopAsync() ?? Task.FromResult(-1));

            // 断开后实时信息立即归零并显示 FF，避免界面上挂着上一次连接的旧值
            Monitor.ResetStatistics();

            // 记录断开完成时刻：后续连接必须与本次断开间隔至少 ReconnectGuardMs
            _lastDisconnectMs = HighResolutionTimer.Milliseconds;
            // 同时锁住“链接”按钮，3 秒内不允许点击（避免用户手快反复建连）
            LockReconnectFor((int)ReconnectGuardMs);
            Log.Information("蓝牙断开结束，距下次允许连接至少 {Guard}ms", ReconnectGuardMs);

            return result;
        }

        /// <summary>
        /// The method is used to start the protocol stack and listen on it.
        /// </summary>
        /// <returns></returns>
        public async Task<int> ConnectAsync()
        {
            int result = -1;

            // 强制断开与再次连接之间至少间隔 3 秒，给系统与下位机留出释放旧链路的时间
            if (_lastDisconnectMs > 0)
            {
                long elapsed = HighResolutionTimer.Milliseconds - _lastDisconnectMs;
                if (elapsed >= 0 && elapsed < ReconnectGuardMs)
                {
                    int wait = (int)(ReconnectGuardMs - elapsed);
                    Log.Information("蓝牙重连保护：距上次断开 {Elapsed}ms，等待 {Wait}ms 后再连接", elapsed, wait);
                    await Task.Delay(wait);
                }
            }

            // 根据输入的MAC地址设置目标蓝牙设备（不带冒号，12位十六进制）
            if (!string.IsNullOrWhiteSpace(MacAddressInput))
            {
                if (!TryNormalizeMac(MacAddressInput, out string macHex, out string macError))
                {
                    Status = -1;
                    TestStatusInfo = macError;
                    return -1;
                }
                byte[] macBytes = new byte[6];
                for (int i = 0; i < 6; i++)
                {
                    macBytes[i] = Convert.ToByte(macHex.Substring(i * 2, 2), 16);
                }
                // Windows BLE API 的 BluetoothAddress 为小端 ulong，需反转字节序
                Array.Reverse(macBytes);
                byte[] ulongBytes = new byte[8];
                Array.Copy(macBytes, ulongBytes, 6);
                ulong bluetoothAddress = BitConverter.ToUInt64(ulongBytes, 0);

                BluetoothViewAdapter.SelectedSlaveDevInfo = new BluetoothInfo
                {
                    Name = "手动输入设备",
                    Address = macHex,
                    BluetoothAddress = bluetoothAddress
                };
                BluetoothInfo.macAddress = macHex;
            }

            // 重连前彻底停掉上一轮 UDS（取消接收任务、退订旧 GATT），否则接手新链路的接收任务不会重建
            if (_udsService.Running)
            {
                try
                {
                    await _udsService.StopAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "重连前停止 UDS 服务失败");
                }
            }

            // 必须挂在真实建立连接的那个 Adapter 的 Datalink 上：
            // BluetoothViewAdapter.Model 才是实际握手/订阅特征的实例，用错实例会导致收不到任何报文。
            Datalink? datalink = null;
            if (BluetoothViewAdapter.Model != null && BluetoothViewAdapter.Model.Datalinks.Count > 0)
            {
                datalink = BluetoothViewAdapter.Model.Datalinks[0];
            }
            else if (BluetoothAdapter.Datalinks.Count > 0)
            {
                datalink = BluetoothAdapter.Datalinks[0];
            }

            if (datalink == null)
            {
                Status = -1;
                TestStatusInfo = "未找到可用的蓝牙数据链路";
                return -1;
            }

            _udsService.BuildStackFromDatalink(datalink, 0);

            if (BluetoothViewAdapter.Model != null)
            {
                result = await BluetoothViewAdapter.Model.StartAsync();
                if (result == 0)
                {
                    // 连接与握手完成后再启动 UDS：此时 GATT 特征已就绪，才能订阅到新链路的通知
                    int udsResult = await _udsService.StartAsync();
                    if (udsResult != 0)
                    {
                        Log.Warning("蓝牙已连接，但 UDS 服务启动失败，返回 {Result}", udsResult);
                    }

                    // 每次新连接都清零实时信息的帧数统计，并在首帧到达前显示 FF
                    Monitor.ResetStatistics();
                }
            }
            else
            {
                result = -1;
            }
            return result;
        }


        /// <summary>
        /// 保存
        /// </summary>
        public ICommand SaveAction => new SyncCommand(sender =>
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "配置文件(*.xml)|*.xml|所有文件(*.*)|*.*",
                    FileName = string.IsNullOrEmpty(FileName) ? "config.xml" : Path.GetFileName(FileName),
                    RestoreDirectory = true
                };

                // WPF：ShowDialog 返回 bool?，不是 Form 的 DialogResult.OK
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                FileName = dialog.FileName;

                using (var fs = new FileStream(FileName, FileMode.Create, FileAccess.Write))
                {
                    var ser = GetSerializer();
                    // 将ViewModel.Model(TestingSoftwareModel)序列化为xml写入文件
                    ser.Serialize(fs, Model);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存失败：{ex.Message}");
            }
        });

        /// <summary>
        /// 打开xml文件
        /// </summary>
        public ICommand OpenAction => new SyncCommand(sender =>
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "配置文件(*.xml)|*.xml|所有文件(*.*)|*.*",
                    RestoreDirectory = true
                };
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                //调用公共加载逻辑
                LoadProjectFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开文件失败：{ex.Message}");
            }
        });



        /// <summary>
        /// 扫描串口,并将串口添加到下拉列表中
        /// </summary>
        public ICommand ScanSerialAction => new SyncCommand(sender =>
        {
            //扫描串口（保留命令，界面已改为自动扫描）
            RefreshSerialPorts();
        });

        /// <summary>
        /// 刷新可用串口列表：仅在端口集合发生变化时重建，避免每秒刷新导致下拉框闪烁、选中项丢失。
        /// </summary>
        private void RefreshSerialPorts()
        {
            try
            {
                string[] ports = SerialPort.GetPortNames();

                bool changed = ports.Length != SerialPortList.Count;
                if (!changed)
                {
                    for (int i = 0; i < ports.Length; i++)
                    {
                        if (SerialPortList[i] != ports[i])
                        {
                            changed = true;
                            break;
                        }
                    }
                }

                if (!changed) return;

                string? current = SelectedSerialPort;
                SerialPortList.Clear();
                foreach (string port in ports)
                {
                    SerialPortList.Add(port);
                }

                // 保持原选中项，仍存在就继续选中；否则退回第一个端口
                if (!string.IsNullOrEmpty(current) && Array.IndexOf(ports, current) >= 0)
                {
                    SelectedSerialPort = current;
                }
                else if (ports.Length > 0)
                {
                    SelectedSerialPort = ports[0];
                }
                else
                {
                    SelectedSerialPort = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "自动扫描串口失败");
            }
        }
        /// <summary>
        /// 退出
        /// </summary>
        public ICommand ExitAction => new SyncCommand(sender =>
        {

        });
        /// <summary>
        /// 校验SN格式：MSM0005263306900005
        /// 结构：MSM+4数字 + 6日期 +1批次 +5流水
        /// </summary>
        /// <param name="sn">扫码得到的SN</param>
        /// <returns>true=格式合法</returns>
        /// <summary>
        /// 规范化蓝牙MAC输入：去除冒号/横线/空格，校验12位合法十六进制。
        /// 成功返回标准化12位hex到macHex；失败时macHex为空、error为提示文案。空输入视为通过且不报错。
        /// </summary>
        private bool TryNormalizeMac(string raw, out string macHex, out string error)
        {
            macHex = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }
            macHex = raw.Replace(":", "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
            if (macHex.Length != 12)
            {
                error = "蓝牙MAC地址格式错误，需12位十六进制字符";
                return false;
            }
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    Convert.ToByte(macHex.Substring(i * 2, 2), 16);
                }
            }
            catch (FormatException)
            {
                error = "蓝牙MAC地址包含非法十六进制字符";
                return false;
            }
            return true;
        }

        private bool IsValidSnFormat(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
                return false;

            // 正则
            string pattern = Model.SnWildcard;
            return Regex.IsMatch(sn, pattern);
        }

        #region 扫码自动流程

        /// <summary>
        /// MAC 扫码校验通过后的防抖入口：等输入稳定后再尝试自动连接，避免逐字符输入触发多次连接。
        /// </summary>
        private void ScheduleAutoConnectAfterMacScan(string macHex)
        {
            _macScanDebounceCts?.Cancel();
            _macScanDebounceCts?.Dispose();
            CancellationTokenSource cts = new CancellationTokenSource();
            _macScanDebounceCts = cts;
            _ = AutoConnectAfterMacScanAsync(macHex, cts.Token);
        }

        private async Task AutoConnectAfterMacScanAsync(string macHex, CancellationToken token)
        {
            try
            {
                await Task.Delay(ScanDebounceMs, token);
            }
            catch (OperationCanceledException)
            {
                return; // 扫码值又被改动，本次作废
            }

            // 复核当前输入仍是这个 MAC，且格式合法
            if (!TryNormalizeMac(MacAddressInput, out string currentMac, out _)) return;
            if (string.IsNullOrEmpty(currentMac) || currentMac != macHex) return;

            await AutoConnectByMacAsync(macHex);
        }

        /// <summary>
        /// 按 MAC 自动连接电控设备；已连接、测试中则跳过。
        /// </summary>
        private async Task AutoConnectByMacAsync(string macHex)
        {
            if (IsTesting)
            {
                Log.Information("扫码MAC {Mac}：测试进行中，跳过自动连接", macHex);
                return;
            }

            if (IsDeviceConnected)
            {
                if (_lastAutoConnectedMac == macHex || string.IsNullOrEmpty(_lastAutoConnectedMac))
                {
                    Log.Information("扫码MAC {Mac}：蓝牙已连接，无需重复连接", macHex);
                    _lastAutoConnectedMac = macHex;
                }
                else
                {
                    Log.Information("扫码MAC {Mac}：当前已连接其它设备，保持现有链路", macHex);
                }
                return;
            }

            await StartAutoConnect(macHex);
        }

        /// <summary>
        /// 单实例启动自动连接：已有连接任务在跑时复用同一个任务。
        /// </summary>
        private Task StartAutoConnect(string macHex)
        {
            if (Interlocked.CompareExchange(ref _autoConnectRunning, 1, 0) != 0)
            {
                return _autoConnectTask ?? Task.CompletedTask;
            }

            Task task = AutoConnectCoreAsync(macHex);
            _autoConnectTask = task;
            return task;
        }

        private async Task AutoConnectCoreAsync(string macHex)
        {
            try
            {
                if (IsDeviceConnected) return;

                // 用空闲态而不是 JOB_RUNNING(1)，避免被 IsTesting 误判成正在测试
                Status = JOB_IDLE;
                TestStatusInfo = $"正在自动连接 {macHex} …";
                ChangeUI();

                int result;
                try
                {
                    result = await ConnectAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "扫码MAC {Mac} 自动连接异常", macHex);
                    result = -1;
                }

                if (result == 0)
                {
                    SetConnectionState(true);
                    // 连接成功后自动开启实时监控（失败重试 3 次，间隔 500ms）
                    await Monitor.StartWithRetryAsync();
                    _lastAutoConnectedMac = macHex;
                    Status = 0;
                    TestStatusInfo = "蓝牙连接成功";
                    Log.Information("扫码MAC {Mac} 自动连接成功", macHex);
                }
                else
                {
                    Status = -1;
                    TestStatusInfo = "自动连接电控设备失败";
                    Log.Warning("扫码MAC {Mac} 自动连接失败，返回 {Result}", macHex, result);
                }
                ChangeUI();
            }
            finally
            {
                Volatile.Write(ref _autoConnectRunning, 0);
                _autoConnectTask = null;
            }
        }

        /// <summary>
        /// SN 扫码校验通过后的防抖入口：等输入稳定后再尝试启动测试。
        /// </summary>
        private void ScheduleAutoStartAfterSnScan()
        {
            _snScanDebounceCts?.Cancel();
            _snScanDebounceCts?.Dispose();
            CancellationTokenSource cts = new CancellationTokenSource();
            _snScanDebounceCts = cts;
            _ = AutoStartAfterSnScanAsync(cts.Token);
        }

        private async Task AutoStartAfterSnScanAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(ScanDebounceMs, token);
            }
            catch (OperationCanceledException)
            {
                return; // 扫码值又被改动，本次作废
            }

            if (string.IsNullOrWhiteSpace(ScanSn) || !IsValidSnFormat(ScanSn)) return;
            if (IsTesting)
            {
                Log.Information("扫码SN {Sn}：测试已在进行，忽略自动启动", ScanSn);
                return;
            }

            if (Interlocked.CompareExchange(ref _autoStartRunning, 1, 0) != 0) return;
            try
            {
                if (!IsDeviceConnected)
                {
                    // 1) 若已有自动连接在跑，先等它；否则在已知 MAC 时主动发起连接
                    Task? connectTask = _autoConnectTask;
                    if (connectTask == null && !string.IsNullOrWhiteSpace(MacAddressInput))
                    {
                        if (TryNormalizeMac(MacAddressInput, out string macHex, out _) && !string.IsNullOrEmpty(macHex))
                        {
                            connectTask = StartAutoConnect(macHex);
                        }
                    }

                    if (connectTask != null)
                    {
                        try { await connectTask; } catch (Exception ex) { Log.Warning(ex, "扫码SN：等待自动连接结束异常"); }
                    }

                    // 2) 仍未连上：等蓝牙连接成功（用户手动点“链接”或后续扫码触发）
                    if (!IsDeviceConnected)
                    {
                        Status = JOB_IDLE;
                        TestStatusInfo = "等待蓝牙连接成功后自动开始测试";
                        ChangeUI();
                        Log.Information("扫码SN {Sn}：蓝牙未连接，等待连接就绪", ScanSn);
                        bool connected = await WaitUntilConnectedAsync(AutoStartConnectWaitMs);
                        if (!connected)
                        {
                            Status = -1;
                            TestStatusInfo = "蓝牙未连接，未开始测试";
                            ChangeUI();
                            Log.Warning("扫码SN {Sn}：等待蓝牙连接超时（{Wait}ms），放弃自动启动", ScanSn, AutoStartConnectWaitMs);
                            return;
                        }
                    }
                }

                if (!IsDeviceConnected)
                {
                    Status = -1;
                    TestStatusInfo = "蓝牙未连接，未开始测试";
                    ChangeUI();
                    return;
                }

                // 3) 启动测试：复用现有开始测试命令（内含连接/串口/协议栈准备与全量测试流程）
                TestStatusInfo = "扫码SN校验通过，自动开始测试";
                Log.Information("扫码SN {Sn}：蓝牙已连接，自动启动测试", ScanSn);
                StartJobAction.Execute(null);
            }
            finally
            {
                Volatile.Write(ref _autoStartRunning, 0);
            }
        }

        /// <summary>
        /// 轮询等待蓝牙连接就绪，超时或取消返回 false。
        /// </summary>
        private async Task<bool> WaitUntilConnectedAsync(int timeoutMs)
        {
            if (IsDeviceConnected) return true;

            long start = HighResolutionTimer.Milliseconds;
            while (HighResolutionTimer.Milliseconds - start < timeoutMs)
            {
                if (IsDeviceConnected) return true;
                try
                {
                    await Task.Delay(200);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return IsDeviceConnected;
        }

        #endregion

        private void SetConnectionState(bool connected)
        {
            BluetoothViewAdapter.Running = connected;
            ConnectionLabel = connected ? "断开" : "链接";
            IsConnectButtonEnabled = connected;
            ChangeUI();   // ← 关键：刷新开始/停止/复测
        }
        /// <summary>
        /// 开始测试
        /// </summary>
        public ICommand StartJobAction => new AsyncCommand(async sender =>
        {
            ChangeUI();
            Status = 1;
            TestStatusInfo = "开始测试";
            // 本轮是新测试：清掉手动停止标记，避免取消旧任务时被误记为“手动终止”
            _manualStopRequested = false;
            _jobCts?.Cancel();
            _jobCts?.Dispose();
            _jobCts = new CancellationTokenSource();
            token = _jobCts.Token;
            //正常全量测试：从0开始
            _retestStartIndex = 0;

            if (string.IsNullOrEmpty(ScanSn))
            {
                Status = -1;
                TestStatusInfo = $"请先扫码进行测试";
                return;
            }
            if (string.IsNullOrEmpty(BluetoothViewAdapter.SelectedSlaveDevInfo.Address))
            {
                Status = -1;
                TestStatusInfo = $"请先选择电控设备";
                return;
            }
            if (!BluetoothViewAdapter.Running)
            {
                var result = await ConnectAsync();
                if (result == 0)
                {
                    SetConnectionState(true);

                    // 自动连接成功后同样自动开启实时监控（失败重试 3 次，间隔 500ms）
                    await Monitor.StartWithRetryAsync();
                }
                else
                {
                    Status = -1;
                    TestStatusInfo = "自动连接电控设备失败";
                    return;
                }
            }
            // 跳过 IO 模块：不打开 IO 模块串口，避免无 IO 模块时卡在"请选择串口"
            if (!SkipIoModules && !_modBusService.IsRunning)
            {
                if (string.IsNullOrEmpty(SelectedSerialPort))
                {
                    var defaultPort = SerialPortList.FirstOrDefault();
                    if (defaultPort != null) SelectedSerialPort = defaultPort;
                }

                if (!string.IsNullOrEmpty(SelectedSerialPort)) _modBusService.Start(SelectedSerialPort);
                else
                {
                    Status = -1;
                    TestStatusInfo = $"请选择串口";
                    return;
                }
            }
            else if (!SkipIoModules)
            {
                if (!_modBusService.IsConnected)
                {
                    if (string.IsNullOrEmpty(SelectedSerialPort)) { _modBusService.Stop(); _modBusService.Start(SelectedSerialPort); }
                }
            }
            else
            {
                Log.Information("已开启 SkipIoModules，开始测试跳过 IO 模块串口初始化");
            }
            await _udsService.StopAsync();
            await _udsService.StartAsync();
            _jobMainTask = Task.Run(async () =>
            {
                //恢复所有测试项的job的初始状态
                foreach (var _job in VisibleJobs)
                {
                    JobStatusChanged(_job, JOB_IDLE, string.Empty);
                }
                await RunTestLogic(token);
                _jobCts?.Cancel();
            }, token);
        }, _ => IsDeviceConnected && !IsTesting);
        private VbaService? can = null;
        private int ExecuCan(Job job, CancellationToken token)
        {
            // 目标远程帧 ID（十进制；如需十六进制写 0x65、0x66、0x67）
            uint[] targetIds = { 0x101, 0x115, 0x100,0x51,0x52,0x53 };
            int cycleCount = 0;

            try
            {
                // ========== 只打开一次设备 ==========
                if (can != null)
                {
                    can.CloseDevice();
                    can = null;
                }
                can = new VbaService();
                var canBaudRate = Model?.CanBaudRate ?? 500000;
                int ret = can.OpenDevice(channel: 0, baudRate: canBaudRate);
                if (ret != 0)
                {
                    Log.Debug($"打开失败，错误码: {(VbaStatus)ret}");
                    return -1;
                }

                do
                {
                    if (token.IsCancellationRequested)
                    {
                        return -1;
                    }
                    var frame = can.ReceiveRemote(timeoutMs: 1500);

                    if (frame != null)
                    {
                        Log.Debug($"收到远程帧: ID=0x{frame.Id:X}  DLC={frame.DLC}");

                        // 命中目标ID，任务完成
                        if (Array.IndexOf(targetIds, frame.Id) >= 0)
                        {
                            Log.Debug($"命中目标 ID=0x{frame.Id:X}，完成并退出");
                            return 2;
                        }

                        Log.Debug($"ID=0x{frame.Id:X} 不在目标列表，继续等待");
                    }
                    else
                    {
                        Log.Debug("接收远程帧超时");
                        // 超时≠设备故障，不关闭设备，继续下一轮等待
                    }

                    cycleCount++;
                } while (job.Cycle && job.CycleNumber > cycleCount && !token.IsCancellationRequested);

                // 循环次数跑完，未命中目标ID
                Log.Debug("循环次数已耗尽，未收到目标远程帧");
                return -1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecuCan 执行异常，CAN设备发生错误");
                return -1;
            }
            finally
            {
                // ========== 无论正常返回/异常，统一关闭释放设备 ==========
                if (can != null)
                {
                    try
                    {
                        can.CloseDevice();
                        can = null;
                    }
                    catch (Exception exClose)
                    {
                        Log.Debug(exClose, "关闭CAN设备时发生异常");
                    }
                    //can.Dispose(); // 如果VbaService实现IDisposable，调用释放
                }
            }
        }

        /// <summary>
        /// 把 IoService 的 Values 按"两字节一个大端寄存器"顺序批量写入 AO 保持寄存器。
        /// 阶梯递增模式下用它把当前阶梯值下发到 IO 模块。
        /// </summary>
        private void WriteAoValues(IoService service)
        {
            // 跳过 IO 模块：不向 IO 模块写任何 AO 数据
            if (SkipIoModules)
            {
                Log.Information($"已开启 SkipIoModules，跳过 AO 输出:{service.IoType}");
                return;
            }

            if (service.Values == null || service.Values.Length == 0)
                throw new Exception("AO数据长度不能为0");
            if (service.Values.Length % 2 != 0)
                throw new Exception("AO数据长度必须为偶数");

            ushort[] values = new ushort[service.Values.Length / 2];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (ushort)((service.Values[i * 2] << 8) | service.Values[i * 2 + 1]);
            }
            _modBusService.WriteAoMultiRegisters(service.StartAddress, values, service.SlaveId);
        }

        /// <summary>
        /// 递归收集某任务声明的复测前置任务下标（支持前置任务自己再声明前置任务）。
        /// 只接受复测起点之前的任务，避免反向补跑或把任务自己补跑一遍。
        /// </summary>
        private void CollectPrerequisiteIndices(Job job, HashSet<int> indices, int depth)
        {
            if (job?.Prerequisites == null || job.Prerequisites.Count == 0 || depth > 8)
                return;

            foreach (var key in job.Prerequisites)
            {
                int idx = FindJobIndex(key);
                if (idx < 0)
                {
                    Log.Warning($"任务[{job.JobName}]的前置任务[{key}]未找到，已忽略");
                    continue;
                }
                if (idx >= _retestStartIndex)
                {
                    Log.Warning($"任务[{job.JobName}]的前置任务[{key}]不在复测起点之前，已忽略");
                    continue;
                }
                if (!indices.Add(idx))
                    continue;

                CollectPrerequisiteIndices(VisibleJobs[idx], indices, depth + 1);
            }
        }

        /// <summary>
        /// 按任务名称（JobName）或任务序号（SerialNumber）查找任务下标，找不到返回 -1。
        /// </summary>
        private int FindJobIndex(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return -1;

            string k = key.Trim();

            for (int i = 0; i < VisibleJobs.Count; i++)
            {
                if (string.Equals((VisibleJobs[i].JobName ?? string.Empty).Trim(), k, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            for (int i = 0; i < VisibleJobs.Count; i++)
            {
                if (string.Equals((VisibleJobs[i].SerialNumber ?? string.Empty).Trim(), k, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <summary>状态常量：0空闲 1测试中 2通过 -1失败 -2重试中 -3手动停止</summary>
        public void JobStatusChanged(Job job, int status, string? info = null, bool canRetry = false)
        {
            // 1) 状态字段在调用线程同步更新，消除 P5 竞态
            job.Status = status;
            Status = status;

            // 2) 仅 UI 刷新走异步
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CurrentJob = job;
                TestStatusInfo = status switch
                {
                    0 => string.Empty,                                  // 重置
                    1 => $"{job.JobName}：{info} -- 测试中",
                    2 => $"{job.JobName}：{info ?? "测试成功"}",          // P1：保留成功提示
                    -2 => $"{job.JobName}：{info} -- 正在重试",           // P3：独立状态，不再与失败混淆
                    -3 => $"{job.JobName}：{info} -- 手动停止",           // P2：单独分支
                    -1 => $"{job.JobName}：{info} -- 测试失败",
                    _ => $"{job.JobName}：{info}",
                };
            });

            // 3) 落库只在终态执行一次（P4/P7：排除 0/1/-2，且用 job 级标志防重复）
            bool isFinal = status == 2 || status == -1 || status == -3;
            if (isFinal && !job.TestRecorded)
            {
                job.TestRecorded = true;
                OnDeviceTestFinished(ScanSn,
                    BluetoothViewAdapter.SelectedSlaveDevInfo.Address,
                    status == 2, info ?? string.Empty);
            }
        }
        /// <summary>
        /// 停止测试
        /// </summary>
        /// <summary>
        /// 停止测试【修复卡死】
        /// </summary>
        public ICommand StopJobAction => new AsyncCommand(async sender =>
        {
            // 标记为用户手动停止：只有这种情况才落 JOB_STOPPED + 写“手动终止”记录
            _manualStopRequested = true;

            //触发取消
            _jobCts?.Cancel();
            _job3E80Cts?.Cancel();

            // 等待后台任务收尾，但最多等 JobStopTimeoutMs：某个任务卡住时不能让界面一直停在“测试中”
            await WaitTaskOrTimeout(_jobMainTask, JobStopTimeoutMs, "主测试任务");
            await WaitTaskOrTimeout(_job3ETask, JobStopTimeoutMs, "3E后台任务");

            // 兜底：无论后台任务是否按时结束，都把当前任务和全局状态落到“手动停止”
            StopCurrentJobForManualStop();

            // 异步停止uds，不要用Wait()
            if (_udsService != null)
            {
                await _udsService.StopAsync();
            }
            _modBusService.Stop();

            //释放cts资源
            _jobCts?.Dispose();
            _job3E80Cts?.Dispose();
            _jobCts = null;
            _job3E80Cts = null;
            _jobMainTask = null;
            _job3ETask = null;
            ChangeUI();
        }, _ => IsDeviceConnected && IsTesting);

        /// <summary>停止时等待后台任务的最长时间（毫秒）。</summary>
        private const int JobStopTimeoutMs = 5000;

        /// <summary>
        /// 等待后台任务结束，超时则放弃等待并记一条警告，保证停止流程一定往前走。
        /// </summary>
        private async Task WaitTaskOrTimeout(Task? task, int timeoutMs, string name)
        {
            if (task == null) return;

            Task completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task)
            {
                Log.Warning($"停止测试：等待{name}超过 {timeoutMs}ms 仍未结束，继续执行停止流程");
                return;
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"停止测试：等待{name}异常");
            }
        }

        /// <summary>
        /// 3E80 会话保活循环：循环条件是 UDS 处于 Running，断链时 UDS 停掉会让循环自然退出，
        /// 因此重连成功后必须再次调用本方法恢复保活，否则后续任务会因 S3 会话超时而失败。
        /// </summary>
        private Task StartKeepAliveLoopAsync(CancellationToken token3E)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (_udsService.Running)
                    {
                        await _mcuSecurityService.UnlockMCUNoPopAsync();
                        var startTime = HighResolutionTimer.Milliseconds;
                        while (_udsService.Running && !token3E.IsCancellationRequested)
                        {
                            // 正常数据发送后 1.5s 内不发送 3E80，等待静默窗口过后再按周期发送
                            if (HighResolutionTimer.Milliseconds - _udsService.LastNormalRequestMs < 1_500)
                            {
                                await Task.Delay(100, token3E);
                                continue;
                            }
                            if (HighResolutionTimer.Milliseconds - startTime > 3_500)
                            {
                                startTime = HighResolutionTimer.Milliseconds;
                                Service3e service3e = new Service3e();
                                service3e.SubFunction = service3e.SUPPRESS_RESPONSE;
                                await _udsService.Request(service3e.BuildRequest(), token3E);
                            }
                            else
                            {
                                await Task.Delay(100, token3E);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //正常停止
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "3E后台任务异常");
                }
            }, token3E);
        }

        /// <summary>IO 步进（AO 阶梯输出）每级最小间隔（毫秒）：配置小于该值时按该值执行。</summary>
        private const int MinIoStepIntervalMs = 800;

        /// <summary>测试中重连最大尝试次数。</summary>
        private const int ReconnectAttempts = 3;

        /// <summary>测试中重连两次尝试之间的间隔（毫秒）；ConnectAsync 内部还有 3 秒断连保护。</summary>
        private const int ReconnectIntervalMs = 5000;

        /// <summary>链路假死判定：长时间没有任何下行报文。</summary>
        private const int LinkSilenceTimeoutMs = 12000;

        /// <summary>链路假死判定：最近这段时间内确实下发过请求（排除“本来就空闲”的误判）。</summary>
        private const int LinkRequestRecentMs = 6000;

        /// <summary>
        /// 测试中异常断连的自动重连：最多 3 次，成功后重启 UDS 与实时监控，让任务链按原逻辑继续；
        /// 全部失败才把当前任务落 JOB_FAIL（“通讯中断且重连失败”）。
        /// </summary>
        private async Task ReconnectDuringTestAsync()
        {
            if (Interlocked.CompareExchange(ref _reconnectRunning, 1, 0) != 0)
            {
                return; // 已有重连在进行
            }

            try
            {
                SetConnectionState(false);

                // 断链后先收掉上一条链路的会话与监控，避免旧 CTS / 旧 GATT 订阅污染新链路
                if (BluetoothViewAdapter.Running)
                {
                    try
                    {
                        await DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "测试中重连：断开旧链路异常");
                    }
                }
                else
                {
                    try { await Monitor.StopAsync(); } catch (Exception ex) { Log.Debug(ex, "重连前停止实时监控异常"); }
                    try { await _udsService.StopAsync(); } catch (Exception ex) { Log.Debug(ex, "重连前停止 UDS 异常"); }
                }

                for (int attempt = 1; attempt <= ReconnectAttempts; attempt++)
                {
                    if (IsDeviceConnected)
                    {
                        break;
                    }

                    Log.Warning("测试中链路异常：第 {Attempt}/{Max} 次重连尝试", attempt, ReconnectAttempts);
                    TestStatusInfo = $"链路断开，正在重连（{attempt}/{ReconnectAttempts}）…";
                    ChangeUI();

                    int result = -1;
                    try
                    {
                        result = await ConnectAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "测试中重连异常");
                    }

                    if (result == 0)
                    {
                        SetConnectionState(true);
                        // 重新进扩展会话 + 恢复 2A 主动上传；监控起不来也不阻断任务链（转速判定会自动回退 22）
                        int monitorResult = await Monitor.StartWithRetryAsync();
                        Log.Information("测试中链路异常：第 {Attempt} 次重连成功（实时监控={Monitor}），继续测试", attempt, monitorResult);

                        // 3E80 保活循环在断链时已随 UDS 停止自然退出，这里重新拉起，避免 S3 会话超时
                        if (_job3E80Cts != null && !_job3E80Cts.IsCancellationRequested
                            && (_job3ETask == null || _job3ETask.IsCompleted))
                        {
                            _job3ETask = StartKeepAliveLoopAsync(token3E80);
                            Log.Information("测试中链路异常：3E80 会话保活已恢复");
                        }
                        TestStatusInfo = "重连成功，继续测试";
                        ChangeUI();
                        return;
                    }

                    if (attempt < ReconnectAttempts)
                    {
                        await Task.Delay(ReconnectIntervalMs);
                    }
                }

                Log.Error("测试中链路异常：重连 {Max} 次仍失败，测试按通讯失败结束", ReconnectAttempts);
                Job? job = CurrentJob;
                if (job != null)
                {
                    JobStatusChanged(job, JOB_FAIL, "通讯中断且重连失败");
                }
                else
                {
                    Status = JOB_FAIL;
                    TestStatusInfo = "通讯中断且重连失败";
                }
                SetConnectionState(false);
                ChangeUI();
            }
            finally
            {
                Volatile.Write(ref _reconnectRunning, 0);
            }
        }

        /// <summary>
        /// 链路假死看门狗：Windows 的 Disconnected 事件实测会晚到 1~2 分钟，
        /// 这里用“一直在发请求、却长时间收不到任何下行报文”自行判定假死并触发重连。
        /// </summary>
        private void CheckLinkAlive()
        {
            if (!IsTesting || !IsDeviceConnected) return;
            if (Volatile.Read(ref _reconnectRunning) == 1) return;

            long now = HighResolutionTimer.Milliseconds;
            long lastRx = _udsService.LastRxMs;
            long lastTx = _udsService.LastRequestMs;
            if (lastTx < 0) return; // 还没下发过请求，无从判断

            long silence = now - lastRx;
            if (silence > LinkSilenceTimeoutMs && now - lastTx <= LinkRequestRecentMs)
            {
                Log.Warning("链路看门狗：已 {Silence}ms 无任何下行报文（最近 {Tx}ms 前还下发过请求），判定链路假死，启动重连",
                    silence, now - lastTx);
                _ = ReconnectDuringTestAsync();
            }
        }

        /// <summary>
        /// 手动停止时把任务和全局状态落到 JOB_STOPPED 终态。
        /// 之前停止流程只取消 CTS、没有改状态，界面会停在“……-- 测试中”，停止/开始/复测按钮状态也不对。
        /// </summary>
        private void StopCurrentJobForManualStop()
        {
            Job? job = CurrentJob;
            if (job != null)
            {
                JobStatusChanged(job, JOB_STOPPED, "手动停止");
            }
            else
            {
                Status = JOB_STOPPED;
                TestStatusInfo = "已手动停止";
            }

            // 配置里其余还没跑的任务一律标记为“未执行”，避免下次开始时残留 RUNNING
            if (Model?.Jobs != null)
            {
                foreach (var item in Model.Jobs)
                {
                    if (item.Status == JOB_RUNNING || item.Status == JOB_RETRYING)
                    {
                        item.Status = JOB_STOPPED;
                    }
                }
            }
        }

        /// <summary>电机转速 DID（22 09-03 / 2A 动态 DID 里的 0x0903），转速判定的数据来源标识。</summary>
        private const ushort DID_MOTOR_SPEED = 0x0903;

        /// <summary>实时转速的有效期：超过这个时间没收到新周期帧就认为数据不可信，回退到 22 请求。</summary>
        private const int RealtimeSpeedValidMs = 2000;

        /// <summary>
        /// 用实时信息（2A 主动上传的 0x0903）里的 RPM 判定转速，替代 22 09-03 请求。
        /// </summary>
        /// <returns>0=用实时数据判定通过；1=用实时数据判定失败；2=实时数据不可用，调用方回退到 22 请求。</returns>
        private int JudgeMotorSpeedByRealtime(Job job, Service22 service22)
        {
            if (!Monitor.IsMonitoring)
            {
                Log.Debug("转速判定：实时监控未开启，回退到 22 09-03");
                return 2;
            }

            long lastFrameMs = Monitor.LastFrameMs;
            if (lastFrameMs <= 0 || HighResolutionTimer.Milliseconds - lastFrameMs > RealtimeSpeedValidMs)
            {
                Log.Debug("转速判定：实时帧超过 {Ms}ms 未刷新，回退到 22 09-03", RealtimeSpeedValidMs);
                return 2;
            }

            var signal = service22.Signals[0];
            var signalViewModel = new SignalViewModel(signal);
            double rpm = Monitor.CurrentMotorSpeed;
            double expectation = Convert.ToDouble(signalViewModel.Expectation);
            double tolerance = Convert.ToDouble(signalViewModel.Tolerance);

            Log.Information($"实时转速:{rpm:F0} 期望:{expectation:F0}±{tolerance:F0} {signal.Name}");

            if (rpm < expectation - tolerance || rpm > expectation + tolerance)
            {
                Log.Information($"执行任务失败:{job.JobName}-{signal.Name},实时转速不满足");
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// 重新测试
        /// </summary>
        public ICommand RetestJobAction => new AsyncCommand(async sender =>
        {
            ChangeUI();
            Status = 1;
            TestStatusInfo = "复测开始";
            // 复测同样清掉手动停止标记，取消旧任务链时不会被误记为“手动终止”
            _manualStopRequested = false;

            _jobCts?.Cancel();
            _jobCts?.Dispose();
            _jobCts = new CancellationTokenSource();
            token = _jobCts.Token;

            //=====定位复测起始下标=====
            if (CurrentJob != null && VisibleJobs.Contains(CurrentJob))
            {
                _retestStartIndex = VisibleJobs.IndexOf(CurrentJob);
                Log.Information($"复测：从Job[{_retestStartIndex}] {CurrentJob.JobName}开始执行");
            }
            else
            {
                //没有当前Job，从头跑
                _retestStartIndex = 0;
                Log.Information($"复测无有效当前Job，从头全部执行");
            }

            if (string.IsNullOrEmpty(ScanSn))
            {
                Status = -1;
                TestStatusInfo = $"请先扫码进行测试";
                return;
            }
            if (string.IsNullOrEmpty(BluetoothViewAdapter.SelectedSlaveDevInfo.Address))
            {
                Status = -1;
                TestStatusInfo = $"请先选择电控设备";
                return;
            }

            if (!BluetoothViewAdapter.Running)
            {
                var result = await ConnectAsync();
                if (result == 0)
                {
                    SetConnectionState(true);

                    // 自动连接成功后同样自动开启实时监控（失败重试 3 次，间隔 500ms）
                    await Monitor.StartWithRetryAsync();
                }
                else
                {
                    Status = -1;
                    TestStatusInfo = "自动连接电控设备失败";
                    return;
                }
            }

            // 跳过 IO 模块：复测同样不打开 IO 模块串口
            if (!SkipIoModules && !_modBusService.IsRunning)
            {
                if (string.IsNullOrEmpty(SelectedSerialPort))
                {
                    var defaultPort = SerialPortList.FirstOrDefault();
                    if (defaultPort != null) SelectedSerialPort = defaultPort;
                }
                if (!string.IsNullOrEmpty(SelectedSerialPort)) _modBusService.Start(SelectedSerialPort);
                else
                {
                    Status = -1;
                    TestStatusInfo = $"请选择串口";
                    return;
                }
            }

            await _udsService.StopAsync();
            await _udsService.StartAsync();

            _jobMainTask = Task.Run(async () =>
            {
                await RunTestLogic(token);
                _jobCts?.Cancel();
            }, token);
        }, _ => IsDeviceConnected && CanRetest);
        /// <summary>
        /// 公共测试执行逻辑：【正常测试 / 复测】共用
        /// 重试闭环：失败→(可重试)JOB_RETRYING→延时→恢复JOB_RUNNING重跑→成功JOB_PASS / 耗尽JOB_FAIL
        /// </summary>
        /// <param name="ct"></param>
        private async Task RunTestLogic(CancellationToken ct)
        {
            try
            {
                _job3E80Cts?.Cancel();
                _job3E80Cts?.Dispose();
                if (_job3ETask != null) await _job3ETask;
                _job3ETask = null;
                _job3E80Cts = new CancellationTokenSource();
                token3E80 = _job3E80Cts.Token;

                //发送3E80维持会话,在这个job结束后,3E80停止发送
                _job3ETask = StartKeepAliveLoopAsync(token3E80);
                await Task.Delay(1000, ct);

                // ===== 开启实时监控 =====
                // 2C 定义动态 DID + 2A 快速率开启下位机主动上传，之后由接收线程持续刷新
                // 母线电流 / 转速 / 电机温度 / 故障码。失败不影响测试流程（仅记录日志与状态）。
                if (_udsService.Running)
                {
                    // 正常情况下连接成功后已自动开启（此处会直接返回 0）；
                    // 若期间被关闭或首次未成功，这里再按 3 次重试兜底。
                    int monitorResult = await Monitor.StartWithRetryAsync();
                    if (monitorResult == 0)
                    {
                        Log.Information("实时监控已开启（动态 DID 主动上传）");
                    }
                    else
                    {
                        Log.Warning($"实时监控开启失败，返回:{monitorResult}");
                    }
                }

                // ===== 复测前置任务：补跑目标Job声明的 Prerequisites =====
                // 复测时 _retestStartIndex 之前的任务会被跳过，但"设置速度""启动电机"这类任务
                // 留下的设备状态必须重新建立，否则转速类判定永远起不来。
                HashSet<int> retestPrereqIndices = new HashSet<int>();
                if (_retestStartIndex > 0 && _retestStartIndex < VisibleJobs.Count)
                {
                    CollectPrerequisiteIndices(VisibleJobs[_retestStartIndex], retestPrereqIndices, 0);
                    if (retestPrereqIndices.Count > 0)
                    {
                        Log.Information($"复测前置任务补跑:{string.Join(",", retestPrereqIndices.OrderBy(i => i).Select(i => $"[{i}]{VisibleJobs[i].JobName}"))}");
                    }
                }

                for (int jobIndex = 0; jobIndex < VisibleJobs.Count; jobIndex++)
                {
                    var job = VisibleJobs[jobIndex];

                    // =========复测核心：下标小于起始下标，直接跳过不执行=========
                    // 例外：目标任务声明的复测前置任务要补跑（按原顺序补跑，先设置速度/启动电机，再判定）
                    if (jobIndex < _retestStartIndex && !retestPrereqIndices.Contains(jobIndex))
                    {
                        Log.Information($"复测跳过已完成Job:{job.JobName}");
                        continue;
                    }

                    Log.Information($"执行任务开始:{job.JobName}");
                    JobStatusChanged(job, JOB_RUNNING);
                    await Task.Delay(job.StartDelayTimeMs, ct);
                    if (job.IsCan)
                    {
                        if(SkipCanModules)
                        {
                            JobStatusChanged(job, 2);
                            await Task.Delay(50_000, ct);
                            continue;
                        }
                        int res = ExecuCan(job, ct);
                        if (res == -1)
                        {
                            JobStatusChanged(job, JOB_FAIL, "CAN通讯测试失败,请检查CAN通讯连接");
                            break;
                        }
                        else
                        {
                            JobStatusChanged(job, res);
                        }
                        continue;
                    }
                    try
                    {
                        int CycleNumber = 0;
                        bool failedThisRound = false;   // 本轮是否有 service 失败（用于跳过剩余 service）

                        // ===== 阶梯递增输出：只有 IoService(AO) 显式配置了 <Step> 才启用 =====
                        // 老 XML 没有 <Step> 节点 → stepService 为 null → 走原来的重试循环，行为完全不变
                        IoService? stepService = null;
                        ushort stepValue = 0;
                        ushort stepMax = 0;
                        ushort stepDelta = 0;
                        int stepDelayMs = 0;
                        bool stepDone = false;
                        // 跳过 IO 模块时不识别 AO 阶梯输出，避免访问 IO 模块
                        if (!SkipIoModules && job.Services != null)
                        {
                            foreach (var s in job.Services)
                            {
                                if (s is IoService io && io.IoType == IoType.Ao && io.Step != null)
                                {
                                    stepService = io;
                                    break;
                                }
                            }
                        }
                        if (stepService != null)
                        {
                            stepValue = stepService.Step!.StartValue;
                            stepDelta = stepService.Step.StepValue;
                            stepMax = stepService.EffectiveMaxValue;
                            stepDelayMs = stepService.Step.DelayAfterStepMs > 0 ? stepService.Step.DelayAfterStepMs : job.CycleDelayTimeMs;
                            if (stepDelta == 0)
                                throw new Exception($"任务{job.JobName}:阶梯步进StepValue不能为0");

                            // 硬件侧要求：IO 步进每级间隔不得小于 800ms，配置写小了统一按 800ms 执行
                            if (stepDelayMs < MinIoStepIntervalMs)
                            {
                                Log.Warning($"阶梯输出间隔 {stepDelayMs}ms 小于最小值 {MinIoStepIntervalMs}ms，按 {MinIoStepIntervalMs}ms 执行:{job.JobName}");
                                stepDelayMs = MinIoStepIntervalMs;
                            }

                            // 先输出起始值，等一级稳定时间后再开始判定（首级同样不能小于最小间隔）
                            stepService.ApplyStepValue(stepValue);
                            WriteAoValues(stepService);
                            Log.Information($"阶梯输出启用:{job.JobName},起始=0x{stepValue:X4},步进=0x{stepDelta:X4},上限=0x{stepMax:X4},每级等待{stepDelayMs}ms");
                            await Task.Delay(stepDelayMs, ct);
                        }

                        do
                        {
                            failedThisRound = false;

                            // 重试轮提示（第2轮起 job.Status == JOB_RETRYING）
                            if (job.Status == JOB_RETRYING)
                                JobStatusChanged(job, JOB_RUNNING, $"第{CycleNumber + 1}轮重试");

                            foreach (var service in job.Services)
                            {
                                if (failedThisRound) break;   // 已有 service 失败：跳过剩余
                                if (ct.IsCancellationRequested)
                                {
                                    JobStatusChanged(job, JOB_STOPPED, "手动停止");
                                    failedThisRound = true;
                                    Log.Information($"任务终止:{job.JobName},手动停止");
                                    _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},手动停止");

                                    break;
                                }
                                await Task.Delay(100, ct);
                                var diagService = service;
                                bool[] bools;
                                if (diagService is IoService)
                                {
                                    // 跳过 IO 模块：DI/DO/AI/AO 一律不读不写，直接按通过处理
                                    if (SkipIoModules)
                                    {
                                        Log.Information($"已开启 SkipIoModules，跳过 IO 任务:{job.JobName}-{((IoService)diagService).IoType}");
                                        continue;
                                    }

                                    IoService? signalDiagService = (IoService)diagService;
                                    int datalent = 0;
                                    if (signalDiagService.Signals.Count == 0) throw new Exception("信号配置错误");
                                    switch (signalDiagService.IoType)
                                    {
                                        case IoType.Di:
                                            bools = new bool[1];
                                            bools = _modBusService.ReadDiscreteInputs((byte)(signalDiagService.StartAddress - 1), (byte)(signalDiagService.Values.Length), signalDiagService.SlaveId);
                                            Log.Debug($"返回值:{string.Join(",", bools.Select(x => x.ToString()))}");
                                            bool diAllMatch = true;
                                            for (int i = 0; i < signalDiagService.Values.Length; i++)
                                            {
                                                if ((signalDiagService.Values[i] == 1) != bools[i])
                                                {
                                                    diAllMatch = false;
                                                    break;
                                                }
                                            }
                                            if (!diAllMatch)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, signalDiagService.Signals[0].ErrorMsg);
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName}-{signalDiagService.Signals[0].Name},返回值不匹配");
                                                break;
                                            }
                                            JobStatusChanged(job, JOB_RUNNING);   // 重试轮成功必须恢复状态
                                            break;
                                        case IoType.Do:
                                            datalent = signalDiagService.Values.Length;
                                            bools = new bool[datalent];
                                            for (int i = 0; i < datalent; i++)
                                            {
                                                bools[i] = signalDiagService.Values[i] == 1;
                                            }
                                            _modBusService.WriteOutputCoils(signalDiagService.StartAddress, bools, signalDiagService.SlaveId);
                                            JobStatusChanged(job, JOB_RUNNING);
                                            break;
                                        case IoType.Ai:
                                            ushort[] _readdata = _modBusService.ReadAiRegisters(signalDiagService.StartAddress, (ushort)(signalDiagService.Length), signalDiagService.SlaveId);
                                            Log.Debug($"返回值:{string.Join(",", _readdata.Select(x => x.ToString()))}");
                                            if (signalDiagService.Signals[0].Tolerance != 0 && signalDiagService.Signals[0].Expectation != null)
                                            {
                                                if (_readdata[0] < (int)signalDiagService.Signals[0].Expectation - (int)signalDiagService.Signals[0].Tolerance || _readdata[0] > (int)signalDiagService.Signals[0].Expectation + (int)signalDiagService.Signals[0].Tolerance)
                                                {
                                                    bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                    JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, signalDiagService.Signals[0].ErrorMsg);
                                                    failedThisRound = true;

                                                    Log.Information($"执行任务失败:{job.JobName}-{signalDiagService.Signals[0].Name},返回值不匹配");
                                                    break;
                                                }
                                                JobStatusChanged(job, JOB_RUNNING);   // 匹配成功恢复状态，不提前置2
                                            }
                                            else
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, signalDiagService.Signals[0].ErrorMsg);
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName}-{signalDiagService.Signals[0].Name},返回值不匹配");
                                                break;
                                            }
                                            break;
                                        case IoType.Ao:
                                            datalent = signalDiagService.Values.Length;
                                            if (datalent % 2 != 0)
                                            {
                                                throw new Exception("AO数据长度必须为偶数");
                                            }
                                            ushort[] values = new ushort[datalent / 2];
                                            values[0] = (ushort)((signalDiagService.Values[0] << 8) | signalDiagService.Values[1]);
                                            if (datalent == 4)
                                            {
                                                values[1] = (ushort)((signalDiagService.Values[2] << 8) | signalDiagService.Values[3]);
                                            }
                                            _modBusService.WriteAoMultiRegisters(signalDiagService.StartAddress, values, signalDiagService.SlaveId);
                                            JobStatusChanged(job, JOB_RUNNING);
                                            break;
                                        default:
                                            break;
                                    }
                                }
                                else
                                {
                                    if (!_udsService.Running)
                                    {
                                        await _udsService.StartAsync();
                                    }
                                    if (diagService is Service22)
                                    {
                                        int? ResponseValue = 0;
                                        var service22 = (Service22)diagService;
                                        if (service22.Signals.Count == 0) throw new Exception("信号配置错误");

                                        // 转速（22 09-03）判定优先用实时信息里的 RPM：固件对 22 09-03 偶发不回或回 0x0000，
                                        // 而 2A 主动上传的 0x0903 有 60 帧/秒以上，判定更稳。实时数据不可用时才回退到 22 请求。
                                        if (service22.Signals[0].Id == DID_MOTOR_SPEED)
                                        {
                                            int realtimeResult = JudgeMotorSpeedByRealtime(job, service22);
                                            if (realtimeResult == 0)
                                            {
                                                JobStatusChanged(job, JOB_RUNNING);
                                                continue;   // 已用实时转速判定通过，本轮不再下发 22 请求
                                            }
                                            if (realtimeResult == 1)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, service22.Signals[0].ErrorMsg);
                                                failedThisRound = true;
                                                break;      // 实时转速不在范围内：本轮失败
                                            }
                                            // realtimeResult == 2：实时数据不可用，继续走下面的 22 09-03 请求
                                        }

                                        service22.Id = service22.Signals[0].Id;
                                        service22.ParsePayload = (w) =>
                                        {
                                            foreach (var signal in diagService.Signals)
                                            {
                                                var sigv = new SignalViewModel(signal);
                                                sigv.Parse(w);
                                                ResponseValue = Convert.ToInt32(signal.Value);
                                            }
                                            return 0;
                                        };
                                        Pdu? pdu = await _udsService.Request(service22.BuildRequest(), ct);
                                        if (pdu != null)
                                        {
                                            int result = service22.ParseResponse(pdu);
                                            service22.ParsePayload = null;
                                            if (result == -1)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"解析失败");
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName},解析失败");
                                                break;   // 跳出 foreach
                                            }
                                            if (result == 0)
                                            {
                                                foreach (var signal in diagService.Signals)
                                                {

                                                    if (failedThisRound) break;   // 信号失败后跳过剩余信号
                                                    var sigv = new SignalViewModel(signal);
                                                    if (sigv.Value != null)
                                                    {
                                                        Log.Information($"{sigv.Name}:{sigv.Expectation} {sigv.Tolerance}");
                                                        int _ret;
                                                        switch (signal.Type)
                                                        {
                                                            case 0:
                                                                JobStatusChanged(job, JOB_RUNNING);
                                                                break;
                                                            case 2:
                                                                _ret = CheckStringDataType(signal.Value);

                                                                byte _v8;
                                                                if (_ret == 0)
                                                                {
                                                                    _v8 = byte.Parse(signal.Value ?? "00");
                                                                }
                                                                else
                                                                {
                                                                    double.TryParse(signal.Value ?? "00", out double temp);
                                                                    _v8 = (byte)temp;
                                                                }
                                                                if (_v8 < sigv.Expectation - sigv.Tolerance || _v8 > sigv.Expectation + sigv.Tolerance)
                                                                {
                                                                    var smsg = PaseSigalErrorMsg(signal, _v8);
                                                                    bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                                    JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, smsg);
                                                                    failedThisRound = true;
                                                                    Log.Information($"执行任务失败:{smsg}");
                                                                    break;
                                                                }
                                                                JobStatusChanged(job, JOB_RUNNING);
                                                                break;
                                                            case 3:
                                                                _ret = CheckStringDataType(signal.Value);

                                                                UInt16 _vu16;
                                                                if (_ret == 0)
                                                                {
                                                                    _vu16 = UInt16.Parse(signal.Value ?? "00");
                                                                }
                                                                else
                                                                {
                                                                    double.TryParse(signal.Value ?? "00", out double temp);
                                                                    _vu16 = (UInt16)temp;
                                                                }
                                                                if (_vu16 < sigv.Expectation - sigv.Tolerance || _vu16 > sigv.Expectation + sigv.Tolerance)
                                                                {
                                                                    var smsg = PaseSigalErrorMsg(signal, _vu16);
                                                                    bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                                    JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, smsg);
                                                                    failedThisRound = true;
                                                                    Log.Information($"执行任务失败:{smsg}");
                                                                    break;
                                                                }
                                                                JobStatusChanged(job, JOB_RUNNING);
                                                                break;
                                                            case 4:
                                                            case 5:
                                                            case 6:
                                                                int _vs16;
                                                                _ret = CheckStringDataType(signal.Value);

                                                                if (_ret == 0)
                                                                {
                                                                    _vs16 = int.Parse(signal.Value ?? "00");
                                                                }
                                                                else
                                                                {
                                                                    double.TryParse(signal.Value ?? "00", out double temp);
                                                                    _vs16 = (int)temp;
                                                                }
                                                                if (_vs16 < sigv.Expectation - sigv.Tolerance || _vs16 > sigv.Expectation + sigv.Tolerance)
                                                                {
                                                                    var smsg = PaseSigalErrorMsg(signal, _vs16);
                                                                    bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                                    JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, smsg);
                                                                    failedThisRound = true;
                                                                    Log.Information($"执行任务失败:{smsg}");
                                                                    break;
                                                                }
                                                                JobStatusChanged(job, JOB_RUNNING);
                                                                break;
                                                            case 7:
                                                                float _vf = float.Parse(signal.Value ?? "0.0");
                                                                if (_vf < sigv.Expectation - sigv.Tolerance || _vf > sigv.Expectation + sigv.Tolerance)
                                                                {
                                                                    var smsg = PaseSigalErrorMsg(signal, _vf);
                                                                    bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                                    JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, smsg);
                                                                    failedThisRound = true;
                                                                    Log.Information($"执行任务失败:{smsg}");
                                                                    break;
                                                                }
                                                                JobStatusChanged(job, JOB_RUNNING);
                                                                break;
                                                            case 8:
                                                                JobStatusChanged(job, JOB_RUNNING);
                                                                break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                            JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"通讯失败");
                                            failedThisRound = true;
                                            _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},通讯失败");

                                            Log.Information($"执行任务失败:{job.JobName},通讯失败");
                                            break;   // 跳出 foreach
                                        }
                                    }
                                    else if (diagService is Service2e)
                                    {
                                        bool unlooked;
                                        var service2e = (Service2e)diagService;
                                        if (service2e.Signals[0].Id != 0x04)
                                        {
                                            unlooked = await _mcuSecurityService.UnlockMCUNoPopAsync();
                                            if (!unlooked)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"解锁失败");
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName},解锁失败");
                                                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},解锁失败");

                                                break;   // 跳出 foreach
                                            }
                                        }
                                        if (service2e.Signals.Count == 0) throw new Exception("信号配置错误");
                                        service2e.Id = service2e.Signals[0].Id;
                                        int content = 0;
                                        if (service2e.Signals[0].Id == SERIAL_NUM)
                                        {
                                            if (Model == null)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"写入SN失败");
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName},读取默认SN失败");
                                                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},读取默认SN失败");

                                                break;
                                            }
                                            content = await ReadSn(Model.DefaultSN);
                                            if (content == -1)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"写入SN失败");
                                                failedThisRound = true;
                                                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},写入SN失败");

                                                Log.Information($"执行任务失败:{job.JobName},写入SN失败");
                                                break;
                                            }
                                            else if (content == 2)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"设备SN已写入与扫描SN不同");
                                                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},设备SN已写入与扫描SN不同");
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName},设备SN已写入与扫描SN不同");
                                                break;
                                            }
                                            else if (content == 0)
                                            {
                                                service2e.BuildPayload += w =>
                                                {
                                                    int result = 0;
                                                    job.SignalViewModels[0].Value = ScanSn;
                                                    result = job.SignalViewModels[0].Build(w);
                                                    return result;
                                                };
                                            }
                                        }
                                        else if (service2e.Signals[0].Id == 0x04)
                                        {
                                            unlooked = await _mcuSecurityService.UnlockLicenseMCUAsync();
                                            if (!unlooked)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"解锁失败");
                                                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},解锁失败");
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName},解锁失败");
                                                break;
                                            }
                                            //重要：原来BuildPayload回调内部调用Wait会卡死，重构，不要在委托内部Wait！
                                            service2e.BuildPayload = writer =>
                                            {
                                                byte[] data = { 0 };
                                                Service22 service22 = new Service22();
                                                service22.Id = DID_PV_SN;
                                                service22.ParsePayload = reader =>
                                                {
                                                    data = reader.ReadBytes((int)reader.BaseStream.Length);
                                                    return 0;
                                                };
                                                //禁止pduTask.Wait()，BuildPayload委托是同步回调，不能阻塞！
                                                var pdu = _udsService.Request(service22.BuildRequest(), ct).Result;
                                                if (pdu == null)
                                                    return -1;
                                                service22.ParseResponse(pdu);
                                                if (data.Length == 12)
                                                {
                                                    byte[] license = alg_encrypt(data);
                                                    foreach (byte b in license)
                                                    {
                                                        writer.Write(b);
                                                    }
                                                }
                                                return 0;
                                            };
                                        }
                                        else
                                        {
                                            service2e.BuildPayload += w =>
                                            {
                                                int result = 0;
                                                foreach (var sig in service2e.Signals)
                                                {
                                                    var sigv = new SignalViewModel(sig);
                                                    sigv.Text = sigv.DefaultValue;
                                                    result = sigv.Build(w);
                                                }
                                                return result;
                                            };
                                        }
                                        if (content == 0)
                                        {
                                            Pdu? pdu = await _udsService.Request(service2e.BuildRequest(), ct);
                                            service2e.BuildPayload = null;
                                            if (pdu == null || (service2e.ParseResponse(pdu)) != 0)
                                            {
                                                bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                                JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"返回值不匹配");
                                                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},返回值不匹配");
                                                failedThisRound = true;
                                                Log.Information($"执行任务失败:{job.JobName},返回值不匹配");
                                                break;
                                            }
                                        }
                                        JobStatusChanged(job, JOB_RUNNING);   // 2E 通过恢复状态
                                    }
                                    else if (diagService is Service31)
                                    {
                                        bool unlooked;
                                        unlooked = await _mcuSecurityService.UnlockMCUNoPopAsync();
                                        if (!unlooked)
                                        {
                                            bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                            JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"解锁失败");
                                            failedThisRound = true;
                                            _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},解锁失败");
                                            Log.Information($"执行任务失败:{job.JobName},解锁失败");
                                            break;
                                        }
                                        var service31 = (Service31)diagService;
                                        if (service31.Signals.Count == 0) throw new Exception("信号配置错误");
                                        service31.Id = service31.Signals[0].Id;
                                        service31.BuildPayload += w =>
                                        {
                                            int result = 0;
                                            foreach (var signal in service31.Signals)
                                            {
                                                var sigv = new SignalViewModel(signal);
                                                if (sigv.WriteData != null)
                                                {
                                                    w.Write(sigv.WriteData);
                                                }
                                                else
                                                {
                                                    sigv.Build(w);
                                                }
                                            }
                                            return result;
                                        };
                                        Pdu? pdu = await _udsService.Request(service31.BuildRequest(), ct);
                                        service31.BuildPayload = null;
                                        if (pdu == null || (service31.ParseResponse(pdu)) != 0)
                                        {
                                            bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                            JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"返回值不匹配");
                                            failedThisRound = true;
                                            _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},返回值不匹配");
                                            Log.Information($"执行任务失败:{job.JobName},返回值不匹配");
                                            break;
                                        }
                                        JobStatusChanged(job, JOB_RUNNING);
                                    }
                                    else if (diagService is Service2f)
                                    {
                                        bool unlooked;
                                        unlooked = await _mcuSecurityService.UnlockMCUNoPopAsync();
                                        if (!unlooked)
                                        {
                                            bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                            JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"解锁失败");
                                            failedThisRound = true;
                                            Log.Information($"执行任务失败:{job.JobName},解锁失败");
                                            _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},解锁失败");

                                            break;
                                        }
                                        var service2f = (Service2f)diagService;
                                        if (service2f.Signals.Count == 0) throw new Exception("信号配置错误");
                                        service2f.Id = service2f.Signals[0].Id;
                                        service2f.BuildPayload += w =>
                                        {
                                            if (service2f.Ctrl_type == Service2f.RETURN_CONTROL_TO_ECU) return 0;
                                            int result = 0;
                                            foreach (var signal in service2f.Signals)
                                            {
                                                var sigv = new SignalViewModel(signal);
                                                if (sigv.WriteData != null)
                                                {
                                                    w.Write(sigv.WriteData);
                                                }
                                                else
                                                {
                                                    sigv.Build(w);
                                                }
                                            }
                                            return result;
                                        };
                                        Pdu? pdu = await _udsService.Request(service2f.BuildRequest(), ct);
                                        service2f.BuildPayload = null;
                                        if (pdu == null || (service2f.ParseResponse(pdu)) != 0)
                                        {
                                            bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                            JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"返回值不匹配");
                                            failedThisRound = true;
                                            Log.Information($"执行任务失败:{job.JobName},返回值不匹配");
                                            _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"执行任务失败:{job.JobName},返回值不匹配");
                                            break;
                                        }
                                        JobStatusChanged(job, JOB_RUNNING);
                                    }
                                    else if (diagService is Service27)
                                    {
                                        bool unlooked;
                                        unlooked = await _mcuSecurityService.UnlockMCUAsync();
                                        if (!unlooked)
                                        {
                                            bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                            JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, $"解锁失败");
                                            failedThisRound = true;
                                            Log.Information($"执行任务失败:{job.JobName},解锁失败");
                                            break;
                                        }

                                        JobStatusChanged(job, JOB_RUNNING);
                                    }
                                    else if (diagService is Service10)
                                    {
                                        Service10 service10 = (Service10)diagService;
                                        var request = service10.BuildRequest();
                                        var response = await _udsService.Request(request);
                                    }
                                }
                            }

                            // ===== 阶梯递增：本轮全部判定通过 → 抬升一级并重新开始判定 =====
                            if (stepService != null && !failedThisRound && job.Status == JOB_RUNNING)
                            {
                                if (stepValue >= stepMax)
                                {
                                    // 已抬到上限且本轮再次判定通过 → 本任务结束
                                    stepDone = true;
                                    Log.Information($"阶梯输出已达上限且判定通过:{job.JobName},当前=0x{stepValue:X4}");
                                }
                                else
                                {
                                    stepValue = (ushort)Math.Min((int)stepValue + stepDelta, (int)stepMax);
                                    stepService.ApplyStepValue(stepValue);
                                    try
                                    {
                                        WriteAoValues(stepService);
                                    }
                                    catch (Exception ex)
                                    {
                                        string msg = (stepService.Signals.Count > 0 && !string.IsNullOrEmpty(stepService.Signals[0].ErrorMsg))
                                            ? stepService.Signals[0].ErrorMsg!
                                            : "AO输出失败";
                                        bool canRetry = job.Cycle && CycleNumber < job.CycleNumber;
                                        JobStatusChanged(job, canRetry ? JOB_RETRYING : JOB_FAIL, msg);
                                        failedThisRound = true;
                                        Log.Information($"执行任务失败:{job.JobName}-{msg},{ex.Message}");
                                    }

                                    if (!failedThisRound)
                                    {
                                        // 重新启动判定：重试计数归零，等待稳定后从第一个 service 重新判断
                                        CycleNumber = 0;
                                        Log.Information($"阶梯输出抬升:{job.JobName},当前=0x{stepValue:X4},等待{stepDelayMs}ms后重新判定");
                                        await Task.Delay(stepDelayMs, ct);
                                        continue;
                                    }
                                }
                            }

                            // 失败且还可重试 → 延时后进入下一轮
                            if (job.Status == JOB_RETRYING)
                            {
                                await Task.Delay(job.CycleDelayTimeMs, ct);
                                CycleNumber++;
                            }
                        } while (((job.Status == JOB_RETRYING && CycleNumber <= job.CycleNumber)
                                  || (stepService != null && job.Status == JOB_RUNNING && !stepDone))
                                 && !ct.IsCancellationRequested);

                        // 循环结束：只有 RUNNING 才算成功（重试成功已恢复为 RUNNING）
                        if (job.Status == JOB_RUNNING)
                        {
                            Log.Information($"执行任务完成:{job.JobName}");
                            JobStatusChanged(job, JOB_PASS, "测试成功");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        JobStatusChanged(job, JOB_STOPPED, "手动停止");
                        Log.Information($"任务终止:{job.JobName},手动停止");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"执行任务失败:{job.JobName}-{ex.Message}\n{ex.StackTrace}");
                        JobStatusChanged(job, JOB_FAIL, "任务终止:未知错误");
                        break;
                    }
                    finally
                    {
                        // 跳过 IO 模块：任务结束后也不再对 IO 模块做 DO/AO 复位
                        if (!SkipIoModules)
                        {
                            try
                            {

                            _modBusService.WriteOutputCoils(0x00, [false, false, false, false, false, false, false, false], 0x30);
                            _modBusService.WriteOutputCoils(0x00, [false, false, false, false], 0x31);
                            _modBusService.WriteAoMultiRegisters(0x00, [0x00,0x00,0x00,0x00], 0x30);
                            }
                            catch
                            {
                                //防止未接入设备报错
                            }
                        }
                    }

                    // 终态（失败/停止）中断整条测试链
                    if (job.Status == JOB_FAIL || job.Status == JOB_STOPPED)
                    {
                        break;
                    }
                }
                // 关闭实时监控（2A STOP + 2C 清除定义），再停协议栈
                await Monitor.StopAsync();
                await _udsService.StopAsync();
                if (Status == JOB_PASS)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        ConnectAction.Execute(null);
                        await Task.Delay(1000);
                        string result = new string(ScanSn.ToCharArray());
                        _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, null);
                        ScanSn = String.Empty;
                        OnPropertyChanged(nameof(BluetoothViewAdapter.SlaveDevices));
                        Status = JOB_PASS;
                        TestStatusInfo = result + "测试成功";
                        SlaveDevices.Clear();
                        SelectedSlaveDevInfo = new BluetoothInfo();

                    });
                }
            }
            catch (OperationCanceledException)
            {
                await Monitor.StopAsync();

                if (_manualStopRequested)
                {
                    // 用户点“停止”：落到终态，否则任务链中断后界面仍显示“测试中”
                    StopCurrentJobForManualStop();
                    ChangeUI();
                    Log.Information("测试任务被手动终止");
                    _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, "测试任务被手动终止");
                }
                else
                {
                    // 开始/复测时取消上一条任务链：只做清理，不落“手动停止”状态、不写记录
                    Log.Debug("旧测试任务链被新任务取消（非手动停止）");
                }

            }
            catch (Exception ex)
            {
                await Monitor.StopAsync();
                ChangeUI();
                Log.Error(ex, "Job测试主任务异常");
                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, $"Job测试主任务异常:{ex.Message}");
            }

            ChangeUI();
        }

        private int CheckStringDataType(string? input)
        {
            // 空/空白字符串直接未知
            if (string.IsNullOrWhiteSpace(input))
                return 2;

            // 优先尝试整数
            if (int.TryParse(input, out _))
            {
                return 0;
            }
            // 再尝试浮点(double)
            if (double.TryParse(input, out _))
            {
                return 1;
            }
            // 都不行
            return 2;
        }

        private string PaseSigalErrorMsg(SignalModel signal, object value)
        {
            string res = signal.ErrorMsg ?? string.Empty;
            if (signal.ErrorMsg != null && signal.ErrorMsg.Contains("{0}"))
            {


                switch (signal.Type)
                {
                    case 0: break;
                    case 2:
                        byte _v8 = (byte)value;

                        break;
                    case 3:
                        UInt16 _vu16 = (UInt16)value;
                        if (signal.Id == 0x722 || signal.Id == 0x703)
                        {
                            res = string.Format(res, GetFaultText(_vu16));
                        }
                        break;
                    case 4:
                    case 5:
                    case 6:
                        int _vs16 = (int)value;

                        res = string.Format(res, GetFaultPins((UInt32)signal.Expectation, (UInt32)_vs16));
                        break;
                    case 7:
                        float _vf = (float)value;

                        break;
                    case 8: break;
                }
            }
            return res;
        }
        /// <summary>位掩码故障码 -> 中文故障名（同一个故障字可能同时置多个位）。</summary>
        private static readonly Dictionary<uint, string> FaultMap = new Dictionary<uint, string>
        {
            [2] = "过压",
            [4] = "欠压",
            [8] = "过流",
            [16] = "缺相",
            [32] = "驱动保护",
            [64] = "MCU过温",
            [128] = "电机过温",
            [256] = "编码器故障",
            [512] = "MCU温度传感器",
            [1024] = "电机温度传感器",
            [2048] = "MOSFET上管短路",
            [4096] = "MOSFET下管短路",
            [8192] = "堵转",
            [16384] = "MOSFET上管"
        };

        /// <summary>解析位掩码故障码, 返回中文拼接字符串 (示例: 过压, 过流)</summary>
        public static string GetFaultText(uint value)
        {
            if (value == 0) return "无故障";
            return string.Join(", ", GetFaultNames(value));
        }

        /// <summary>
        /// 解析位掩码故障码，返回逐项故障名（按位从低到高）。
        /// 一个故障字里可能同时置多个位，因此返回列表而不是单条文本。
        /// </summary>
        public static List<string> GetFaultNames(uint value)
        {
            List<string> names = new List<string>();
            if (value == 0) return names;

            uint rest = value;
            for (int bit = 0; bit < 32; bit++)
            {
                uint mask = 1u << bit;
                if ((value & mask) != mask) continue;

                rest &= ~mask;
                names.Add(FaultMap.TryGetValue(mask, out string? name) ? name : $"未定义故障位 Bit{bit}");
            }

            // rest 理论上只剩高位，兜底提示，便于现场排查固件新增的故障位
            if (rest != 0)
            {
                names.Add($"未定义故障位 0x{rest:X8}");
            }

            return names;
        }
        /// <summary>
        /// 32bit 数据映射 32 个引脚, 期待值与事实值不匹配的位视为引脚故障
        /// </summary>
        public string GetFaultPins(uint expected, uint actual)
        {
            uint diff = expected ^ actual;        // 不匹配的位(异或: 两者不同即为1)
            var pins = new List<string>();

            for (int i = 0; i < 32; i++)
            {
                if ((diff & (1u << i)) != 0)
                    pins.Add($"{i + 1}号引脚");   // bit0 -> 1号引脚, bit31 -> 32号引脚
            }

            return string.Join(", ", pins);
        }
        /// <summary>
        /// Lightweight Obfuscated Encryption Algorithm (Alternative AES-CBC)
        /// </summary>
        /// <param name="input">输入数据（必须12字节）</param>
        /// <param name="inputLen">输入长度（固定12）</param>
        /// <returns>输出加密数据（固定32字节）</returns>
        /// <exception cref="ArgumentException"></exception>
        public static byte[] alg_encrypt(byte[] input)
        {
            // 输出固定 32 字节
            byte[] output = new byte[32];
            // 输出清 0
            Array.Clear(output, 0, output.Length);
            int inputLen = input.Count();
            // C 原版 uint32_t 对应 C# uint
            uint crc32 = 0xFFFFFFFF;
            uint lfsr = crc32;

            // 和 C 完全一样的 CRC 表
            uint[] crc_table =
            {
            0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA, 0x076DC419, 0x706AF48F, 0xE963A535, 0x9E6495A3,
            0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988, 0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91
        };

            // 输入合法性判断
            if (input == null || inputLen != 12)
            {
                return output;
            }

            // CRC32 计算（和 C 完全一致）
            for (byte i = 0; i < inputLen; i++)
            {
                crc32 = (crc32 >> 4) ^ crc_table[(crc32 & 0x0F) ^ (input[i] & 0x0F)];
                crc32 = (crc32 >> 4) ^ crc_table[(crc32 & 0x0F) ^ (input[i] >> 4)];
                crc32 = crc32 ^ ((uint)input[i] << 24);
            }

            crc32 = ~crc32;



            // LFSR 生成 32 字节
            for (byte i = 0; i < 32; i++)
            {
                uint bit = (lfsr >> 31) ^ (lfsr >> 21) ^ (lfsr >> 1) ^ (lfsr >> 0);
                lfsr = (lfsr << 1) | bit;
                output[i] = (byte)(lfsr & 0xFF);
            }

            // 前 12 字节异或输入
            for (byte i = 0; i < inputLen; i++)
            {
                output[i] ^= input[i];
            }

            return output;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="defaultSN"></param>
        /// <returns> -1 失败  0 成功 需写入SN  1 成功 不写入SN  2 已写入SN 不同设备</returns>
        /// <exception cref="Exception"></exception>
        public async Task<int> ReadSn(string defaultSN)
        {
            int result = -1;
            string sn = string.Empty;
            SignalModel signalModel = new SignalModel()
            {
                Id = SERIAL_NUM,
                Type = 0,
                Length = 16,
            };
            SignalViewModel signalViewModel = new SignalViewModel(signalModel);

            // Project code.
            Service22 service22 = new Service22();
            service22.Id = signalViewModel.Id;
            service22.ParsePayload = w =>
            {
                return signalViewModel.Parse(w, Model.DefaultSN);
            };
            Pdu? pdu = await _udsService.Request(service22.BuildRequest());
            if (pdu != null)
            {
                if (service22.ParseResponse(pdu) == 0)
                {
                    sn = new string(signalViewModel?._model?.Value?.Where(c => !char.IsWhiteSpace(c) && c != '\0').ToArray());
                    if (sn.Equals(defaultSN)) //sn和默认SN相同 新设备 
                    {
                        result = 0;
                    }
                    else  //不同
                    {
                        if (sn.Equals(ScanSn)) //sn和扫码的SN 相同 同一个设备 可以走正常流程不写入
                        {
                            result = 1;
                        }
                        else  //和扫码的SN不同 不同设备已写入SN 不检测后续流程
                        {
                            result = 2;
                        }
                    }
                }
            }
            return result;
        }
    }
}
