// <copyright file=BluetoothViewModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 14:15:48</date>
// <summary>
// 蓝牙扫描 ViewModel：
//   1. 点击扫描按钮后进入循环扫描，不再 10 秒自动停止；再次点击停止。
//   2. 列表(SlaveDevices)实时刷新：同地址设备更新 Rssi / LastSeenTime，新设备加入。
//   3. 已连接设备永久保留；其余设备若 30 秒内未再收到广播，自动从列表移除。
// </summary>
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TestingSoftware.Adapters;
using TestingSoftware.Commands;
using Windows.Devices.Bluetooth.Advertisement;

namespace TestingSoftware.ViewModels
{
    public class BluetoothViewModel : ViewModel<SumartBluetoothAdapter>
    {
        /// <summary>扫描状态切换阈值：未连接设备超过该时长未再广播则自动移除</summary>
        private static readonly TimeSpan DeviceOfflineTimeout = TimeSpan.FromSeconds(30);

        /// <summary>离线巡检周期</summary>
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the running state of the device.
        /// </summary>
        public bool Running
        {
            get => Model?.Running ?? false;
            set
            {
                if (Model != null) Model.Running = value;
                OnPropertyChanged(nameof(Running));
            }
        }

        /// <summary>扫描状态事件（true=开始扫描，false=扫描结束）</summary>
        public event EventHandler<bool>? ScanStateChanged;

        /// <summary>列表就绪事件</summary>
        public event EventHandler? BluetoothDeviceFound;

        /// <summary>
        /// Gets or sets the list of Bluetooth addresses.
        /// </summary>
        public List<BluetoothInfo> BluetoothAddresses
        {
            get => _bluetoothAddresses;
            set
            {
                _bluetoothAddresses = value;
                OnPropertyChanged(nameof(BluetoothAddresses));
            }
        }

        /// <summary>下拉框绑定的从机设备集合（ObservableCollection 自动通知 UI 增删）</summary>
        public ObservableCollection<BluetoothInfo> SlaveDevices { get; } = new ObservableCollection<BluetoothInfo>();

        public BluetoothInfo SelectedSlaveDevInfo
        {
            get => Model?.SelectedSlaveDevInfo ?? _selectedSlaveDevInfo;
            set
            {
                _selectedSlaveDevInfo = value;
                if (Model != null)
                {
                    Model.SelectedSlaveDevInfo = value;
                    BluetoothInfo.macAddress = value?.Address ?? string.Empty;
                }
                OnPropertyChanged(nameof(SelectedSlaveDevInfo));
            }
        }

        /// <summary>
        /// 是否允许选择下拉项。XAML 中 ComboBox.IsEnabled 绑定本属性。
        /// 循环扫描期间保持 true，保证用户可以边扫边选设备连接。
        /// </summary>
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged(nameof(IsScanning));
            }
        }

        /// <summary>是否正在循环扫描中（切换按钮状态/判断是否重复开始）</summary>
        public bool IsScanLooping
        {
            get => _isScanLooping;
            set
            {
                _isScanLooping = value;
                OnPropertyChanged(nameof(IsScanLooping));
            }
        }

        /// <summary>
        /// Gets or sets the broadcast state of the device.
        /// </summary>
        public bool IsBroadcast
        {
            get => Model?.IsBroadcast ?? false;
            set
            {
                if (Model != null) Model.IsBroadcast = value;
            }
        }

        private List<BluetoothInfo> _bluetoothAddresses = new List<BluetoothInfo>();
        private BluetoothInfo _selectedSlaveDevInfo = new BluetoothInfo();

        private BluetoothLEAdvertisementWatcher? _watcher;
        private bool _isScanning = true;       // 初始即允许下拉选择
        private bool _isScanLooping;          // 是否正在循环扫描

        /// <summary>离线设备清理定时器</summary>
        private DispatcherTimer? _cleanupTimer;

        public BluetoothViewModel() : base()
        {
            if (Model == null)
            {
                Model = new SumartBluetoothAdapter();
            }

            // 异常断开后 3 秒触发一次轻量复扫（保留原行为）
            SumartBluetoothAdapter.OnScanActionNoChange += async (s, e) =>
            {
                await Task.Delay(3000);
                ScanActionNoChange();
            };
        }

        /// <summary>
        /// 收到 BLE 广播：同地址设备更新信号强度与时间戳；新设备入列。
        /// </summary>
        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var adv = args.Advertisement;
            if (adv == null) return;

            string deviceName = adv.LocalName;
            ulong btAddress = args.BluetoothAddress;
            short rssi = args.RawSignalStrengthInDBm;

            // 仅保留 SM 前缀设备，其余忽略
            if (string.IsNullOrWhiteSpace(deviceName) ||
                !deviceName.StartsWith("SM", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string addressStr = string.Join(":", BitConverter.GetBytes(btAddress)
                .Reverse()
                .Take(8)
                .Select(b => b.ToString("X2")));

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existing = SlaveDevices.FirstOrDefault(d => d.BluetoothAddress == btAddress);
                if (existing != null)
                {
                    // 已存在：实时更新名称/信号/最后发现时间（BluetoothInfo 已实现 INPC）
                    existing.Name = deviceName;
                    existing.Rssi = rssi;
                    existing.LastSeenTime = DateTime.Now;
                }
                else
                {
                    // 新设备：加入列表
                    SlaveDevices.Add(new BluetoothInfo
                    {
                        Name = deviceName,
                        Address = addressStr,
                        BluetoothAddress = btAddress,
                        Rssi = rssi,
                        LastSeenTime = DateTime.Now
                    });
                }

                // 同步连接状态标记：当前正在连接的设备不参与离线清理
                ulong connectedAddr = (Model != null && Model.Running && Model.SelectedSlaveDevInfo != null)
                    ? Model.SelectedSlaveDevInfo.BluetoothAddress
                    : 0;
                foreach (var d in SlaveDevices)
                {
                    d.IsConnected = (d.BluetoothAddress == connectedAddr);
                }
            });
        }

        private void OnAdvertisementWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            if (sender == null) return;

            sender.Received -= OnAdvertisementReceived;
            sender.Stopped -= OnAdvertisementWatcherStopped;

            if (ReferenceEquals(_watcher, sender))
            {
                _watcher = null;
                IsScanLooping = false;
                IsScanning = true;   // 停止后恢复下拉可用
            }

            StopCleanupTimer();
            BluetoothDeviceFound?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 扫描按钮命令：切换循环扫描开始/停止。
        /// </summary>
        public async Task<bool> ScanAction()
        {
            // 正在循环扫描 → 再次点击即停止
            if (IsScanLooping)
            {
                StopLoopingScan();
                return true;
            }

            // 开始新一轮循环扫描
            StopWatcherInternal();
            StopCleanupTimer();

            IsScanLooping = true;
            IsScanning = true;   // 扫描期间下拉框保持可用，可边扫边选
            ScanStateChanged?.Invoke(this, true);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SlaveDevices.Clear();
                SelectedSlaveDevInfo = null;
            });

            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Stopped += OnAdvertisementWatcherStopped;

            try
            {
                _watcher.Start();
                // 不再 Task.Delay(10000) 后 Stop：watcher 持续循环广播，直到用户再次点击停止
                StartCleanupTimer();
                Log.Information("蓝牙循环扫描已启动");
            }
            catch (Exception ex)
            {
                Log.Debug($"扫描失败：{ex.Message}");
                IsScanLooping = false;
                IsScanning = true;
                StopCleanupTimer();
            }
            return true;
        }

        /// <summary>
        /// 停止循环扫描并释放资源
        /// </summary>
        public void StopLoopingScan()
        {
            StopWatcherInternal();
            StopCleanupTimer();
            IsScanLooping = false;
            IsScanning = true;
            ScanStateChanged?.Invoke(this, false);
            Log.Information("蓝牙循环扫描已停止");
        }

        private void StopWatcherInternal()
        {
            if (_watcher != null)
            {
                try
                {
                    _watcher.Received -= OnAdvertisementReceived;
                    _watcher.Stopped -= OnAdvertisementWatcherStopped;
                    if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                    {
                        _watcher.Stop();
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"停止 watcher 异常：{ex.Message}");
                }
                _watcher = null;
            }
        }

        /// <summary>
        /// 启动离线清理定时器：每 5 秒巡检一次，
        /// 未连接且超过 30 秒未再广播的设备自动从列表移除。
        /// </summary>
        private void StartCleanupTimer()
        {
            StopCleanupTimer();
            _cleanupTimer = new DispatcherTimer { Interval = CleanupInterval };
            _cleanupTimer.Tick += (s, e) =>
            {
                ulong connectedAddr = (Model != null && Model.Running && Model.SelectedSlaveDevInfo != null)
                    ? Model.SelectedSlaveDevInfo.BluetoothAddress
                    : 0;

                var now = DateTime.Now;
                // 倒序遍历便于安全移除
                for (int i = SlaveDevices.Count - 1; i >= 0; i--)
                {
                    var d = SlaveDevices[i];
                    d.IsConnected = (d.BluetoothAddress == connectedAddr);

                    // 连接中设备永久保留；其余设备 30 秒未再广播则移除
                    if (!d.IsConnected && (now - d.LastSeenTime) > DeviceOfflineTimeout)
                    {
                        Log.Information($"设备离线超过 {DeviceOfflineTimeout.TotalSeconds}s，自动移除：{d.Name} ({d.Address})");
                        SlaveDevices.RemoveAt(i);
                    }
                }
            };
            _cleanupTimer.Start();
        }

        private void StopCleanupTimer()
        {
            if (_cleanupTimer != null)
            {
                _cleanupTimer.Stop();
                _cleanupTimer = null;
            }
        }

        /// <summary>
        /// 异常断开后的轻量复扫（保留原逻辑：扫描 3 秒，不更新列表）
        /// </summary>
        public void ScanActionNoChange()
        {
            _ = Task.Run(async () =>
            {
                if (!IsScanning)
                {
                    _watcher?.Stop();
                    IsScanning = true;
                    return;
                }

                _watcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = BluetoothLEScanningMode.Active
                };
                _watcher.Stopped += OnAdvertisementWatcherStopped;
                try
                {
                    IsScanning = false;
                    _watcher.Start();
                    await Task.Delay(3000);
                    if (!IsScanning)
                    {
                        _watcher.Stop();
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"复扫失败：{ex.Message}");
                    IsScanning = true;
                }
            });
        }
    }
}
