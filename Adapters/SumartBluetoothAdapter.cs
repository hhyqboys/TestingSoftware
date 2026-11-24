// <copyright file=SumartBluetoothAdapter company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:39:46</date>
// <summary></summary>
using HarfBuzzSharp;
using Microsoft.VisualBasic.Devices;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestingSoftware.Protocols;
using TestingSoftware.Protocols.Bluetoolth;
using TestingSoftware.Utilities;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace TestingSoftware.Adapters
{
    public class SumartBluetoothAdapter : Adapter
    {
        private BluetoothInfo _selectedSlaveDevInfo = new BluetoothInfo();


        [XmlIgnore]
        private readonly Guid serviceUuid = new Guid("80dc952b-012e-c017-ba1e-034cbd6e1938");
        [XmlIgnore]
        private readonly Guid rxUuid = new Guid("83110e64-3e8f-084a-e868-02f6e43665c9"); // 通知特征（外设 → 主机）
        [XmlIgnore]
        private readonly Guid txUuid = new Guid("366076ab-53c9-1baa-cd9d-380e749806dd"); // 写特征（主机 → 外设）

        [XmlIgnore]
        private readonly Guid newServiceUuid = new Guid("0000FFF0-0000-1000-8000-00805F9B34FB");
        [XmlIgnore]
        private readonly Guid newRxUuid = new Guid("0000FFF1-0000-1000-8000-00805F9B34FB"); // 通知特征（外设 → 主机）
        [XmlIgnore]
        private readonly Guid newTxUuid = new Guid("0000FFF2-0000-1000-8000-00805F9B34FB"); // 写特征（主机 → 外设）
        public SumartBluetoothAdapter()
        {
            Datalinks = new List<Datalink> { new BluetoothDataLink() };

        }
        [XmlIgnore]
        private BluetoothLEDevice? _device;
        [XmlIgnore]
        private GattDeviceService? _gattService;
        [XmlIgnore]
        public GattCharacteristic? _writeCharacteristic;
        [XmlIgnore]
        public GattCharacteristic? _notifyCharacteristic;
        /// <summary>
        /// BLE广播发布器（核心对象，需保留引用防止被GC回收）
        /// </summary>
        [XmlIgnore]
        private BluetoothLEAdvertisementPublisher? _advertisementPublisher;
        [XmlIgnore]
        private bool handshake = false;


        /// <summary>
        /// 当蓝牙连接意外断开（非通过 StopAsync 方法主动断开）时触发
        /// </summary>
        public static event EventHandler? OnUnexpectedDisconnect;
        public static event EventHandler? OnScanActionNoChange;

        // 用于区分是否为用户主动断开

        [XmlIgnore]
        private bool _isUserInitiatedDisconnect = false;
        private bool _isBroadcast;

        public BluetoothInfo SelectedSlaveDevInfo
        {
            get => _selectedSlaveDevInfo;
            set
            {
                _selectedSlaveDevInfo = value;
            }
        }

        /// <summary>
        /// Gets or sets the broadcast state of the device.
        /// </summary>
        public bool IsBroadcast
        {
            get => _isBroadcast;
            set
            {
                _isBroadcast = value;
            }
        }
        public override async Task<int> StartAsync()
        {
            handshake = false;
            _isUserInitiatedDisconnect = false;

            // 蓝牙适配器不可用（被关闭/无硬件）直接失败
            if (!await IsBluetoothAdapterNormalAsync())
            {
                return -1;
            }

            int result = -1;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                result = await StartOnceAsync();
                if (result == 0) break;

                // 连接失败时彻底释放 GATT 客户端会话：
                // 否则 Windows 侧会残留“已连接”，之后所有重连都会被这个僵尸会话挡住。
                Log.Warning("蓝牙连接第 {Attempt}/2 次失败，清理 GATT 资源后重试", attempt);
                await DisconnectRemainingLinkAsync();
            }

            return result;
        }

        /// <summary>
        /// 单次连接尝试。
        /// </summary>
        private async Task<int> StartOnceAsync()
        {
            int result = 0;

            // 开连之前先释放上一轮可能残留的 GATT 对象，并请求系统断掉残留链路
            if (_device != null)
            {
                await DisconnectRemainingLinkAsync();
            }
            else
            {
                await ReleaseGattResourcesAsync();
            }

            try
            {
                _device = await BluetoothLEDevice.FromBluetoothAddressAsync(SelectedSlaveDevInfo.BluetoothAddress);
                if (_device == null) return -1;

                //在这里订阅连接状态变化事件
                _device.ConnectionStatusChanged += OnConnectionStatusChanged;


                var servicesResult = await _device.GetGattServicesForUuidAsync(serviceUuid, BluetoothCacheMode.Uncached);
                if (servicesResult.Status == GattCommunicationStatus.Success && servicesResult.Services.Count > 0)
                {
                    //Log.Debug("发现服务");
                    var service = servicesResult.Services[0];
                    _gattService = service;
                    // 发现特征
                    var characteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (characteristicsResult.Status != GattCommunicationStatus.Success)
                    {
                        return -1;
                    }
                    // Log.Debug("发现特征");
                    foreach (var characteristic in characteristicsResult.Characteristics)
                    {
                        Log.Debug($"{characteristic.Uuid} - {characteristic.CharacteristicProperties}");
                        if (characteristic.Uuid == txUuid && (characteristic.CharacteristicProperties & GattCharacteristicProperties.Write) != 0)
                        {
                            _writeCharacteristic = characteristic;
                            //  Log.Debug($"找到写方法{characteristic.CharacteristicProperties}");
                        }

                        if (characteristic.Uuid == rxUuid && (characteristic.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0)
                        {
                            _notifyCharacteristic = characteristic;
                            var status = await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);

                            if (status == GattCommunicationStatus.Success)
                            {

                                _notifyCharacteristic.ValueChanged += OnDataReceived;

                                // Log.Debug($"找到读方法{characteristic.CharacteristicProperties}");
                            }
                        }
                    }
                }
                else
                {
                    /* New BLE */
                    servicesResult = await _device.GetGattServicesForUuidAsync(newServiceUuid, BluetoothCacheMode.Uncached);
                    if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                    {
                        return -1;
                    }

                    //Log.Debug("发现服务");
                    var service = servicesResult.Services[0];
                    _gattService = service;
                    // 发现特征
                    var characteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (characteristicsResult.Status != GattCommunicationStatus.Success)
                    {
                        return -1;
                    }
                    // Log.Debug("发现特征");
                    foreach (var characteristic in characteristicsResult.Characteristics)
                    {
                        Log.Debug($"{characteristic.Uuid} - {characteristic.CharacteristicProperties}");
                        if (characteristic.Uuid == newTxUuid && (characteristic.CharacteristicProperties & GattCharacteristicProperties.Write) != 0)
                        {
                            _writeCharacteristic = characteristic;
                            //  Log.Debug($"找到写方法{characteristic.CharacteristicProperties}");
                        }

                        if (characteristic.Uuid == newRxUuid && (characteristic.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0)
                        {
                            _notifyCharacteristic = characteristic;
                            var status = await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);

                            if (status == GattCommunicationStatus.Success)
                            {
                                handshake = true;

                                _notifyCharacteristic.ValueChanged += OnDataReceived;

                                // Log.Debug($"找到读方法{characteristic.CharacteristicProperties}");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return -1;
            }


            if (SelectedSlaveDevInfo.Address != null)
            {
                if (Datalinks.Count > 0)
                {
                    Datalinks.RemoveRange(1, Datalinks.Count - 1);
                }
                if (_notifyCharacteristic != null && _writeCharacteristic != null)
                    ((BluetoothDataLink)Datalinks[0]).SetGattCharacteristic(_writeCharacteristic, _notifyCharacteristic);
            }

            await Task.Run(async () =>
            {
                var _start = HighResolutionTimer.Milliseconds;
                while (!handshake)
                {
                    await Task.Delay(500);
                    if (HighResolutionTimer.Milliseconds - _start > 5_000)
                    {
                        result = -1;
                        break;
                    }
                }
            });
            if (_notifyCharacteristic != null) _notifyCharacteristic.ValueChanged -= OnDataReceived;
            return result;
        }

        private void OnDataReceived(GattCharacteristic sender, GattValueChangedEventArgs evt)
        {
            byte[] data = evt.CharacteristicValue.ToArray();
            string formated = BitConverter.ToString(data);
            Log.Information($"BluetoothAdapter  Rx: Data:{formated}");
            if (!handshake && data.Length == 4)
            {
                var buffer = UdsSecurityAccess.GenerateKey(data, 0);
                IBuffer ibuffer = buffer.AsBuffer();

                GattCommunicationStatus status = Task.Run(async () =>
                {
                    if (_writeCharacteristic != null)
                    {
                        GattCommunicationStatus state = await _writeCharacteristic.WriteValueAsync(ibuffer, GattWriteOption.WriteWithResponse);
                        return state;
                    }
                    else
                    {
                        return GattCommunicationStatus.Unreachable;
                    }
                }).Result;
                if (status == GattCommunicationStatus.Success) handshake = true;
            }
        }
 
        /// <summary>
        /// 蓝牙连接状态变化事件处理程序
        /// </summary>
        /// <param name="sender">蓝牙设备对象</param>
        /// <param name="args">事件参数（通常为 null 或 object）</param>
        private async void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender == null)
            {
                return;
            }

            var status = sender.ConnectionStatus;
            Log.Information($"蓝牙连接状态变更 → {status}");

            if (status == BluetoothConnectionStatus.Connected)
            {
                // 可选：连接成功时的额外处理（如果需要）
                Log.Debug("蓝牙已建立连接");
            }
            else if (status == BluetoothConnectionStatus.Disconnected)
            {
                if (!_isUserInitiatedDisconnect)
                {
                    Log.Warning("检测到蓝牙被动/异常断开（非主动调用 StopAsync）");
                    OnUnexpectedDisconnect?.Invoke(this, EventArgs.Empty);
                    OnScanActionNoChange?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Log.Information("蓝牙连接已被用户主动断开");
                }

                // 执行资源清理（异步方式）
                await CleanupDeviceResourcesAsync();
            }
        }

        /// <summary>
        /// 异步清理蓝牙相关资源并取消事件订阅
        /// </summary>
        private async Task CleanupDeviceResourcesAsync()
        {
            // 防止重复清理
            if (_device == null && _gattService == null && _notifyCharacteristic == null)
            {
                return;
            }

            try
            {
                if (_notifyCharacteristic != null)
                {
                    try
                    {
                        // 关闭通知 
                        await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);

                        // 取消值变化事件订阅
                        _notifyCharacteristic.ValueChanged -= OnDataReceived;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"关闭通知或取消订阅时发生异常: {ex.Message}");
                    }
                    finally
                    {
                        _notifyCharacteristic = null;
                    }
                }

                if (_device != null)
                {
                    try
                    {
                        // 取消连接状态变化事件订阅
                        _device.ConnectionStatusChanged -= OnConnectionStatusChanged;

                        // 释放设备对象
                        _device.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"释放 BluetoothLEDevice 时发生异常: {ex.Message}");
                    }
                    finally
                    {
                        _device = null;
                    }
                }

                if (_gattService != null)
                {
                    try
                    {
                        _gattService.Dispose();
                    }
                    catch { /* 忽略释放异常 */ }
                    finally
                    {
                        _gattService = null;
                    }
                }

                _writeCharacteristic = null;
                handshake = false;

                Log.Information("蓝牙资源已清理完成");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "清理蓝牙资源过程中发生未预期的异常");
            }
        }
    
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override async Task<int> StopAsync()
        {

            _isUserInitiatedDisconnect = true;
            int result = 0;

            try
            {
                // Datalink 内部负责写 CCCD=None 并退订 ValueChanged，必须执行，否则外设侧通知一直挂着
                result = await Datalinks[0].StopAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "停止蓝牙 Datalink 失败，继续释放 GATT 资源");
                result = -1;
            }

            // 关键：MaintainConnection=false 要“生效”后会话才能释放，
            // 立刻 Dispose 会让 Windows 来不及下发断链，链路一直挂着（外设就不再发握手帧）。
            await DisconnectRemainingLinkAsync();

            Log.Information("蓝牙断开完成（GATT 会话已释放）");

            return result;
        }

        /// <summary>
        /// 请求系统断开物理链路并释放全部 GATT 资源：
        /// 设 MaintainConnection=false → 释放 characteristic/service/device → 等系统完成断开 → 释放会话。
        /// </summary>
        private async Task DisconnectRemainingLinkAsync()
        {
            GattSession? session = await SetMaintainConnectionFalseAsync();

            await ReleaseGattResourcesAsync();

            // 给系统时间真正断开链路，这段时间会话必须保持存活
            await Task.Delay(1_200);

            try
            {
                session?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "释放 GattSession 失败");
            }
        }

        /// <summary>
        /// 取设备对应的 GattSession 并设置 MaintainConnection=false（请求系统断开物理链路）。
        /// 返回的会话必须由调用方在断开耗时结束后再 Dispose，否则断开请求不会真正生效。
        /// </summary>
        private async Task<GattSession?> SetMaintainConnectionFalseAsync()
        {
            if (_device == null)
            {
                return null;
            }

            try
            {
                GattSession? session = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId);
                if (session != null)
                {
                    session.MaintainConnection = false;
                }

                return session;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "设置 GattSession 保持连接为 false 失败");
                return null;
            }
        }

        /// <summary>
        /// 按 characteristic → service → device 的顺序彻底释放 GATT 客户端资源。
        /// 任何一步失败都不影响后续释放，最后清空引用（含 Datalink 层持有的静态特征引用），
        /// 避免 Windows 蓝牙列表里残留“已连接”状态导致后续连接始终失败。
        /// </summary>
        private async Task ReleaseGattResourcesAsync()
        {
            if (_notifyCharacteristic != null)
            {
                try
                {
                    await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "释放前关闭通知失败");
                }

                // WinRT 的 GattCharacteristic 没有 Dispose：退订回调后置空引用，由 GC 释放即可
                _notifyCharacteristic.ValueChanged -= OnDataReceived;
                _notifyCharacteristic = null;
            }

            if (_writeCharacteristic != null)
            {
                // 同上：WinRT 特征对象没有 Dispose，置空引用
                _writeCharacteristic = null;
            }

            if (_gattService != null)
            {
                try
                {
                    _gattService.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "释放 GATT 服务失败");
                }

                _gattService = null;
            }

            if (_device != null)
            {
                try
                {
                    _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                    _device.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "释放 BluetoothLEDevice 失败");
                }

                _device = null;
            }

            // Datalink 里保存的是静态特征引用，同样要清掉，防止下次连接前仍指向已释放对象
            BluetoothDataLink._notifyCharacteristic = null;
            BluetoothDataLink._writeCharacteristic = null;

            handshake = false;

            Log.Information("蓝牙 GATT 资源已释放");
        }
        public async Task<bool> IsBluetoothAdapterNormalAsync()
        {


            // 获取所有无线电设备
            var radios = await Radio.GetRadiosAsync();
            var bluetoothRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

            if (bluetoothRadio == null)
            {
                return false;
            }

            // 检查状态是否为 On（启用）
            return bluetoothRadio.State == RadioState.On;
        }
        private async Task<bool> CheckPeripheralSupportAsync()
        {
            try
            {
                // 获取默认蓝牙适配器
                BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync();
                if (adapter == null)
                {
                    Console.WriteLine("未找到蓝牙适配器");
                    return false;
                }

                // 检查是否支持 Low Energy Peripheral 角色（广播模式）
                if (!adapter.IsPeripheralRoleSupported)
                {
                    Console.WriteLine("当前蓝牙适配器不支持 BLE Peripheral 角色（广播模式）");
                    return false;
                }

                // 可选：检查是否支持 LE（通常与 Peripheral 相关）
                if (!adapter.IsLowEnergySupported)
                {
                    Console.WriteLine("当前适配器不支持 Bluetooth Low Energy");
                    return false;
                }

                Console.WriteLine("蓝牙适配器支持 BLE Peripheral 模式");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查蓝牙适配器时发生异常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 广播状态改变事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void Publisher_StatusChanged(BluetoothLEAdvertisementPublisher sender, BluetoothLEAdvertisementPublisherStatusChangedEventArgs args)
        {
            Log.Debug($"广播状态变化：{args.Status}");
            switch (args.Status)
            {
                case BluetoothLEAdvertisementPublisherStatus.Started:
                    Log.Debug("广播已成功启动");
                    break;

                case BluetoothLEAdvertisementPublisherStatus.Aborted:
                    Console.WriteLine($"广播被中止，错误：{args.Error}");
                    break;

                case BluetoothLEAdvertisementPublisherStatus.Stopped:
                    Log.Debug("广播已停止");
                    break;
            }
        }

        /// <summary>
        /// 手动停止广播
        /// </summary>
        public void StopBLEPeripheral()
        {
            if (_advertisementPublisher != null && _advertisementPublisher.Status == BluetoothLEAdvertisementPublisherStatus.Started)
            {
                _advertisementPublisher.Stop();
                _advertisementPublisher.StatusChanged -= Publisher_StatusChanged;
                _advertisementPublisher = null;
                _gattService?.Dispose();
                _gattService = null;
                Console.WriteLine("BLE 广播已停止");
            }
        }
    }
}
