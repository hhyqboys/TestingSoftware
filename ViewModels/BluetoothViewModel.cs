// <copyright file=BluetoothViewModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 14:15:48</date>
// <summary></summary>
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TestingSoftware.Adapters;
using TestingSoftware.Commands;
using Windows.Devices.Bluetooth.Advertisement;

namespace TestingSoftware.ViewModels
{
    public class BluetoothViewModel : ViewModel<SumartBluetoothAdapter>
    {

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


        // 新增①：扫描状态事件（true=开始扫描，false=扫描结束）
        public event EventHandler<bool>? ScanStateChanged;
        // 新增②：列表就绪事件（保留原事件，但调用处必须 ?.Invoke 防空引用）
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
                    if (value != null)
                    {
                        BluetoothInfo.macAddress = value.Address;
                    }
                    else
                    {
                        // 清空mac地址，可选
                        BluetoothInfo.macAddress = string.Empty;
                    }
                }
                OnPropertyChanged(nameof(SelectedSlaveDevInfo));
            }
        }
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged(nameof(IsScanning));
            }
        }
        //public bool IsDeviceEditable
        //{
        //    get => _isEditable;
        //    set
        //    {
        //        _isEditable = value;
        //        OnPropertyChanged(nameof(IsDeviceEditable));
        //    }
        //}
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


        private bool _isEditable = false;
        private List<BluetoothInfo> _bluetoothAddresses = new List<BluetoothInfo>();
        private BluetoothInfo _selectedSlaveDevInfo = new BluetoothInfo();



        private BluetoothLEAdvertisementWatcher? _watcher;
        private bool _isScanning = true;
        private bool _noChangeScan;
        private bool _isBroadcast;

        public BluetoothViewModel() : base()
        {
            if (Model == null)
            {
                Model = new SumartBluetoothAdapter();
            }
            SumartBluetoothAdapter.OnScanActionNoChange += async (s, e) =>
            {
                await Task.Delay(3000);
                ScanActionNoChange();
            };

        }

        private void OnAdvertisementReceivedNoCHange(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args) { }
        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var adv = args.Advertisement;
            if (adv == null)
                return;

            string deviceName = adv.LocalName;
            //if (string.IsNullOrWhiteSpace(deviceName))
            if (string.IsNullOrWhiteSpace(deviceName) || !deviceName.StartsWith("SM", StringComparison.OrdinalIgnoreCase))
            {
                deviceName = "未知设备";
                return;
            }

            ulong btAddress = args.BluetoothAddress;
            short Rssi = args.RawSignalStrengthInDBm;
            string addressStr = string.Join(":", BitConverter.GetBytes(btAddress)
                .Reverse()
                .Take(8)
                .Select(b => b.ToString("X2")));
            Log.Information($"发现设备：{deviceName}，地址：{addressStr}，蓝牙地址：{btAddress}，信号强度：{Rssi}");
            var info = new BluetoothInfo
            {
                Name = deviceName,
                Address = addressStr,
                BluetoothAddress = btAddress,
                //Rssi = Rssi
            };
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 去重：若地址已存在，则不重复添加
                if (!SlaveDevices.Any(d => d.BluetoothAddress == btAddress))
                {
                    SlaveDevices.Add(info);   // ObservableCollection 自动通知 UI
                }
            });
        }

        private void OnAdvertisementWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            // 用 sender 而不是 _watcher 字段，避免新旧 watcher 串台
            if (sender == null) return;

            sender.Received -= OnAdvertisementReceived;
            sender.Stopped -= OnAdvertisementWatcherStopped;

            // 只有当前触发的就是活跃 watcher 才清字段
            if (ReferenceEquals(_watcher, sender))
            {
                _watcher = null;
                IsScanning = true;
            }

            BluetoothDeviceFound?.Invoke(this, EventArgs.Empty);
        }

        public async Task<bool> ScanAction()
        {
            if (!IsScanning) return false;
            _noChangeScan = false;

            // 先把旧的停干净，等 Stopped 事件走完
            if (_watcher != null)
            {
                try { _watcher.Stop(); } catch { }
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stopped -= OnAdvertisementWatcherStopped;
                _watcher = null;
            }

            IsScanning = false;
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
                await Task.Delay(10_000);
                if (!IsScanning && _watcher != null)
                {
                    _watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"扫描失败：{ex.Message}");
                IsScanning = true;
            }
            return true;
        }
        /// <summary>
        ///This function uses the selected Bluetooth SelectedDevInfo to scan attached Bluetooth devices
        /// </summary>
        public void ScanActionNoChange()
        {
            _ = Task.Run(async () =>
            {
                if (!IsScanning)
                {
                    // 停止扫描
                    _watcher?.Stop();
                    IsScanning = true;
                    return;
                }
                _noChangeScan = true;
                // 初始化 BLE 广告监视器
                _watcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = BluetoothLEScanningMode.Active  // Active 模式可获取更多数据，包括名称
                };

                _watcher.Received += OnAdvertisementReceivedNoCHange;
                _watcher.Stopped += OnAdvertisementWatcherStopped;
                try
                {


                    IsScanning = false;

                    _watcher.Start();
                    await Task.Delay(3000);  // 扫描 5 秒
                    if (!IsScanning)  // 若用户未手动停止，则自动停止
                    {
                        _watcher.Stop();
                    }
                }
                catch (Exception ex)
                {
                    // 处理异常（如蓝牙未开启）
                    Log.Debug($"扫描失败：{ex.Message}");
                    IsScanning = true;
                }
            });
        }
    }
}
