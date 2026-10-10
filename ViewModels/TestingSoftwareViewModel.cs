using ControlzEx.Standard;
using Serilog;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Serialization;
using Windows.Devices.Bluetooth.Advertisement;
using TestingSoftware.Adapters;
using TestingSoftware.Commands;
using TestingSoftware.Models;
using TestingSoftware.Protocols;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Services;
using TestingSoftware.Utilities;
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

        /// <summary>是否正在循环扫描中（再次点击扫描按钮即停止）</summary>
        private bool _isScanLooping;
        public bool IsScanLooping
        {
            get => _isScanLooping;
            set { _isScanLooping = value; OnPropertyChanged(nameof(IsScanLooping)); }
        }

        /// <summary>未连接设备超过该时长未再广播则自动从列表移除</summary>
        private static readonly TimeSpan DeviceOfflineTimeout = TimeSpan.FromSeconds(30);
        /// <summary>离线巡检周期</summary>
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(5);

        private BluetoothLEAdvertisementWatcher? _bleWatcher;
        private DispatcherTimer? _bleCleanupTimer;


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
            // 蓝牙循环扫描与列表维护由本 VM 直接驱动（ScanAction / OnBleAdvertisementReceived / CleanupTimer），
            // 不再订阅 BluetoothViewAdapter.ScanStateChanged / BluetoothDeviceFound。

            _mcuSecurityService = new McuSecurityService(_udsService);
            //_lineConfigPageView = lineConfigPageView;
            _selectedSerialPort = Properties.Settings.Default.LastSerialPort;
            ScanSerialAction.Execute(null);
            if (Model == null)
            {
                Model = new TestingSoftwareModel();
            }

            if (BluetoothViewAdapter.Model != null)
            {
                SumartBluetoothAdapter.OnUnexpectedDisconnect += async (s, e) =>
                {
                    await DisconnectAsync();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        App.isPause = false;
                        SetConnectionState(false);
                        Log.Error(
                            "蓝牙连接意外断开，请检查蓝牙状态后重新连接！!",
                            "蓝牙断开",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    });
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
                if (result == 0) SetConnectionState(false);
            }
            else
            {
                result = await ConnectAsync();
                if (result == 0) SetConnectionState(true);
            }

            if (result != 0)
            {
                Status = -1;
                TestStatusInfo = $"设备打开关闭失败";
            }
            ChangeUI();
        });

        /// <summary>
        /// 扫描按钮：切换循环扫描开始/停止。
        /// 扫描过程中 SlaveDevices 实时增删改；非连接设备 30s 未再广播自动移除；
        /// 已连接设备永久保留。
        /// </summary>
        public ICommand ScanAction => new AsyncCommand(async p =>
        {
            if (IsScanLooping)
            {
                StopLoopingScan();
            }
            else
            {
                await StartLoopingScan();
            }
        });

        private async Task StartLoopingScan()
        {
            StopBleWatcherInternal();
            StopCleanupTimer();

            IsScanLooping = true;
            IsScanning = false;   // 与原语义一致：扫描中=false，空闲=true
            OnPropertyChanged(nameof(ScanAction));

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SlaveDevices.Clear();
                SelectedSlaveDevInfo = null;
            });

            _bleWatcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _bleWatcher.Received += OnBleAdvertisementReceived;
            _bleWatcher.Stopped  += OnBleWatcherStopped;

            try
            {
                _bleWatcher.Start();
                StartCleanupTimer();
                Log.Information("蓝牙循环扫描已启动（TestingSoftwareViewModel）");
            }
            catch (Exception ex)
            {
                Log.Error($"蓝牙扫描启动失败：{ex.Message}");
                IsScanLooping = false;
                IsScanning = true;
                StopCleanupTimer();
            }
        }

        private void StopLoopingScan()
        {
            StopBleWatcherInternal();
            StopCleanupTimer();
            IsScanLooping = false;
            IsScanning = true;
            OnPropertyChanged(nameof(ScanAction));
            Log.Information("蓝牙循环扫描已停止");
        }

        private void StopBleWatcherInternal()
        {
            if (_bleWatcher == null) return;
            try
            {
                _bleWatcher.Received -= OnBleAdvertisementReceived;
                _bleWatcher.Stopped  -= OnBleWatcherStopped;
                if (_bleWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    _bleWatcher.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"停止 BLE watcher 异常：{ex.Message}");
            }
            _bleWatcher = null;
        }

        private void OnBleAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var adv = args.Advertisement;
            if (adv == null) return;

            string deviceName = adv.LocalName;
            // 只保留 SM 前缀设备（与原过滤规则一致）
            if (string.IsNullOrWhiteSpace(deviceName) ||
                !deviceName.StartsWith("SM", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ulong btAddress = args.BluetoothAddress;
            short rssi = args.RawSignalStrengthInDBm;
            string addressStr = string.Join(":", BitConverter.GetBytes(btAddress)
                .Reverse().Take(8).Select(b => b.ToString("X2")));

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existing = SlaveDevices.FirstOrDefault(d => d.BluetoothAddress == btAddress);
                if (existing != null)
                {
                    // 已知设备：实时刷新名称/信号/最后发现时间
                    existing.Name = deviceName;
                    existing.Rssi = rssi;
                    existing.LastSeenTime = DateTime.Now;
                }
                else
                {
                    // 新设备：加入列表（ObservableCollection 自动通知 ComboBox）
                    SlaveDevices.Add(new BluetoothInfo
                    {
                        Name = deviceName,
                        Address = addressStr,
                        BluetoothAddress = btAddress,
                        Rssi = rssi,
                        LastSeenTime = DateTime.Now
                    });
                }
                RefreshConnectedFlags();
            });
        }

        private void OnBleWatcherStopped(BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            if (!ReferenceEquals(_bleWatcher, sender)) return;
            try
            {
                sender.Received -= OnBleAdvertisementReceived;
                sender.Stopped  -= OnBleWatcherStopped;
            }
            catch { /* ignore */ }

            _bleWatcher = null;
            IsScanLooping = false;
            IsScanning = true;
            StopCleanupTimer();
            OnPropertyChanged(nameof(ScanAction));
        }

        /// <summary>每 5s 巡检：未连接且 30s 未再广播的设备自动移除；连接中设备永久保留</summary>
        private void StartCleanupTimer()
        {
            StopCleanupTimer();
            _bleCleanupTimer = new DispatcherTimer { Interval = CleanupInterval };
            _bleCleanupTimer.Tick += (s, e) =>
            {
                var now = DateTime.Now;
                RefreshConnectedFlags();
                for (int i = SlaveDevices.Count - 1; i >= 0; i--)
                {
                    var d = SlaveDevices[i];
                    if (!d.IsConnected && (now - d.LastSeenTime) > DeviceOfflineTimeout)
                    {
                        Log.Information($"设备离线超过 {DeviceOfflineTimeout.TotalSeconds}s，自动移除：{d.Name} ({d.Address})");
                        if (SelectedSlaveDevInfo?.BluetoothAddress == d.BluetoothAddress)
                        {
                            SelectedSlaveDevInfo = null;
                        }
                        SlaveDevices.RemoveAt(i);
                    }
                }
            };
            _bleCleanupTimer.Start();
        }

        private void StopCleanupTimer()
        {
            if (_bleCleanupTimer != null)
            {
                _bleCleanupTimer.Stop();
                _bleCleanupTimer = null;
            }
        }

        /// <summary>根据当前连接状态，刷新列表中每个设备的 IsConnected 标记</summary>
        private void RefreshConnectedFlags()
        {
            ulong connectedAddr = 0;
            if (BluetoothViewAdapter?.Model != null && BluetoothViewAdapter.Model.Running
                && BluetoothViewAdapter.Model.SelectedSlaveDevInfo != null)
            {
                connectedAddr = BluetoothViewAdapter.Model.SelectedSlaveDevInfo.BluetoothAddress;
            }
            foreach (var d in SlaveDevices)
            {
                d.IsConnected = (connectedAddr != 0 && d.BluetoothAddress == connectedAddr);
            }
        }

        /// <summary>
        /// The method is used to stop the protocol stack and listening.
        /// </summary>
        /// <returns></returns>
        public async Task<int> DisconnectAsync()
        {
            int result = -1;
            result = await (BluetoothViewAdapter.Model?.StopAsync() ?? Task.FromResult(-1));
            return result;
        }

        /// <summary>
        /// The method is used to start the protocol stack and listen on it.
        /// </summary>
        /// <returns></returns>
        public async Task<int> ConnectAsync()
        {
            int result = -1;

            //_udsService.BuildStackFromDatalink(Project.BluetoothAdapter.Datalinks[0], SelectModeIndex);
            _udsService.BuildStackFromDatalink(BluetoothAdapter.Datalinks[0], 0);
            if (BluetoothViewAdapter.Model != null)
            {
                result = await BluetoothViewAdapter.Model.StartAsync();
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
            //扫描串口
            var serialPortList = SerialPort.GetPortNames();
            SerialPortList.Clear();
            foreach (var port in serialPortList)
            {
                SerialPortList.Add(port);
                if (string.IsNullOrEmpty(SelectedSerialPort))
                {
                    SelectedSerialPort = port;
                }
            }

        });
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
        private bool IsValidSnFormat(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
                return false;

            // 正则
            string pattern = Model.SnWildcard;
            return Regex.IsMatch(sn, pattern);
        }
        private void SetConnectionState(bool connected)
        {
            BluetoothViewAdapter.Running = connected;
            ConnectionLabel = connected ? "断开" : "链接";
            IsConnectButtonEnabled = connected;
            RefreshConnectedFlags();   // 连接/断开后立即刷新列表中设备的 IsConnected 标记
            ChangeUI();   // ← 关键：刷新开始/停止/复测

            // 连接成功 → 自动停止扫描；断开（主动/被动）→ 自动开始扫描
            if (connected)
            {
                if (IsScanLooping) StopLoopingScan();
            }
            else
            {
                if (!IsScanLooping)
                {
                    _ = StartLoopingScan();
                }
            }
        }
        /// <summary>
        /// 开始测试
        /// </summary>
        public ICommand StartJobAction => new AsyncCommand(async sender =>
        {
            ChangeUI();
            Status = 1;
            TestStatusInfo = "开始测试";
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
                }
                else
                {
                    Status = -1;
                    TestStatusInfo = "自动连接电控设备失败";
                    return;
                }
            }
            if (!_modBusService.IsRunning)
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
            else
            {
                if (!_modBusService.IsConnected)
                {
                    if (string.IsNullOrEmpty(SelectedSerialPort)) { _modBusService.Stop(); _modBusService.Start(SelectedSerialPort); }
                }
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
            uint[] targetIds = { 0x101, 0x115, 0x100 };
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
            //触发取消
            _jobCts?.Cancel();
            _job3E80Cts?.Cancel();

            //等待后台任务完成，不要用Wait，用await，不阻塞UI线程
            if (_jobMainTask != null)
            {
                try
                {
                    await _jobMainTask;
                }
                catch (OperationCanceledException)
                {
                    //正常取消，忽略
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "停止时等待主测试任务异常");
                }
            }
            if (_job3ETask != null)
            {
                try
                {
                    await _job3ETask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "停止时等待3E后台任务异常");
                }
            }

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

        /// <summary>
        /// 重新测试
        /// </summary>
        public ICommand RetestJobAction => new AsyncCommand(async sender =>
        {
            ChangeUI();
            Status = 1;
            TestStatusInfo = "复测开始";

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
                }
                else
                {
                    Status = -1;
                    TestStatusInfo = "自动连接电控设备失败";
                    return;
                }
            }

            if (!_modBusService.IsRunning)
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
                _job3ETask = Task.Run(async () =>
                {
                    try
                    {
                        if (_udsService.Running)
                        {
                            await _mcuSecurityService.UnlockMCUNoPopAsync();
                            var _securityService = new McuSecurityService(_udsService);
                            var startTime = HighResolutionTimer.Milliseconds;
                            while (_udsService.Running && !token3E80.IsCancellationRequested)
                            {
                                if (HighResolutionTimer.Milliseconds - startTime > 3_500)
                                {
                                    startTime = HighResolutionTimer.Milliseconds;
                                    Service3e service3e = new Service3e();
                                    service3e.SubFunction = service3e.SUPPRESS_RESPONSE;
                                    await _udsService.Request(service3e.BuildRequest(), token3E80);
                                }
                                else
                                {
                                    await Task.Delay(100, token3E80);
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
                }, token3E80);
                await Task.Delay(1000, ct);

                for (int jobIndex = 0; jobIndex < VisibleJobs.Count; jobIndex++)
                {
                    var job = VisibleJobs[jobIndex];

                    // =========复测核心：下标小于起始下标，直接跳过不执行=========
                    if (jobIndex < _retestStartIndex)
                    {
                        Log.Information($"复测跳过已完成Job:{job.JobName}");
                        continue;
                    }

                    Log.Information($"执行任务开始:{job.JobName}");
                    JobStatusChanged(job, JOB_RUNNING);
                    await Task.Delay(job.StartDelayTimeMs, ct);
                    if (job.IsCan)
                    {
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

                            // 失败且还可重试 → 延时后进入下一轮
                            if (job.Status == JOB_RETRYING)
                            {
                                await Task.Delay(job.CycleDelayTimeMs, ct);
                                CycleNumber++;
                            }
                        } while (job.Status == JOB_RETRYING && CycleNumber <= job.CycleNumber && !ct.IsCancellationRequested);

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
                        _modBusService.WriteOutputCoils(0x00, [false, false, false, false, false, false, false, false], 0x30);
                        _modBusService.WriteOutputCoils(0x00, [false, false, false, false], 0x31);
                    }

                    // 终态（失败/停止）中断整条测试链
                    if (job.Status == JOB_FAIL || job.Status == JOB_STOPPED)
                    {
                        break;
                    }
                }
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
                ChangeUI();
                Log.Information("测试任务被手动终止");
                _csvHelper.Update(BluetoothViewAdapter.SelectedSlaveDevInfo.Address, ScanSn, Status == JOB_PASS, "测试任务被手动终止");

            }
            catch (Exception ex)
            {
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
        /// <summary>解析位掩码故障码, 返回中文拼接字符串 (示例: 190980)</summary>
        public static string GetFaultText(uint value)
        {
            // 位掩码 -> 中文故障名
            var map = new Dictionary<uint, string>
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

            if (value == 0) return map[0];

            // 逐个位检测并拼接
            var hits = map.Where(k => k.Key != 0 && (value & k.Key) == k.Key)
                          .Select(k => k.Value);
            return string.Join(", ", hits);
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