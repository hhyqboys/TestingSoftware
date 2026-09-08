// <copyright file=LineConfigPageViewModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/27 12:04:51</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using TestingSoftware.Commands;
using TestingSoftware.Models;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Protocols;
using TestingSoftware.Services;
using System.Windows;
using TestingSoftware.Adapters;
using System.Reflection.Metadata;
using Serilog;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// 
    /// </summary>
    public class LineConfigPageViewModel : ViewModel<McuModel>
    {

        public const UInt16 SERIAL_NUM = 0xF18d;
        public string ChipID = string.Empty;
        //private readonly CsvExcelHelper _csvHelper = new CsvExcelHelper();
        private readonly BluetoothCsvHelper _BlueHelper = new BluetoothCsvHelper();
        /// <summary>
        /// 
        /// </summary>
        public List<SignalViewModel> _svms = new List<SignalViewModel>();
        private ICollectionView? _signalsView;
        public ICollectionView? SignalsView
        {
            get => _signalsView;
            set
            {
                _signalsView = value;
                OnPropertyChanged(nameof(SignalsView));
            }
        }
        /// <summary>
        /// The all available signal view models.
        /// </summary>
        public ICollection<SignalViewModel> Signals => _svms;

        public float RShunt { get; set; } = 0.662f;
        public float Aop { get; set; } = 0.00183f;

        public float VbusAop { get; set; } = 41.0f;

        public float NominalBusVoltage { get; set; } = 72.0f;

        public float AdcRefVolt { get; set; } = 3.3f;

        /// <summary>
        /// The applicaton layer protocol.
        /// </summary>
        private UdsService _uds;


        /// <summary>
        /// 
        /// </summary>
        private readonly McuSecurityService _securityService;
        /// <summary>
        /// 
        /// </summary>
        public bool IsControl;


        /// <summary>
        /// 设置信号失败状态，弹出提示
        /// </summary>
        /// <param name="sig">当前信号对象</param>
        /// <returns>true=发生错误，需要终止批量流程</returns>
        private bool SetSignalFail(SignalViewModel sig)
        {
            sig.Status = -1;
            if (_currentJob !=null)
            {
                _currentJob.Status = -1;
                _currentJob.ShowText = $"编号 {sig.Index} {sig.Name} 写入失败";
            }
            
            Log.Error("写入失败");
            return true;
        }
 

        private JobItem? _currentJob;
        /// <summary>
        /// 
        /// </summary>
        public JobItem? CurrentJob
        {
            get => _currentJob;
            set
            {
                _currentJob = value;
                OnPropertyChanged(nameof(CurrentJob));
            }
        }

        private string _scanSn;
        public string ScanSn
        {
            get => _scanSn;
            set
            {
                _scanSn = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 开始
        /// </summary>
        public ICommand WriteSigCommand => new AsyncCommand(async sender =>
        {
            int result = -1;
            bool udsstarted = false;
            bool isAllStop = false;
            do
            {
                if (!_uds.Running)
                {
                    result = await _uds.StartAsync();
                    if (result != 0) break;
                    await Task.Delay(1000);
                    udsstarted = true;
                }
                _currentJob = new JobItem();
                Pdu request;
                Pdu? response;

                var success = await _securityService.UnlockMCUAsync();
                if (!success)
                {
                    _currentJob.Status = -1;
                    _currentJob.ShowText = "测试失败";
                    Log.Error("安全解锁MCU失败");
                    break;
                }

                foreach (var signal in _svms)
                {
                    if (signal.Id == SERIAL_NUM)
                    {
                        if (string.IsNullOrEmpty(ScanSn))
                        {
                            isAllStop = SetSignalFail(signal);
                            break;
                        }
                    }
                    //进行中
                    signal.Status = 1;

                    // Write the programming date.
                    Service2e service2e = new Service2e();
                    service2e.Id = signal.Id;
                    service2e.BuildPayload += w =>
                    {
                        int result = 0;
                        var vms = _svms.Where(s => s.Id == signal.Id).OrderBy(s => s.Offset).ToArray();
                        if (vms != null && vms.Length > 0)
                        {
                            if (signal.Id == SERIAL_NUM)
                            {
                                signal.Text = ScanSn;
                            }
                            else
                            {
                                signal.Text = signal.DefaultValue;
                            } 
                            result = signal.Build(w);
                        }
                        else
                        {
                            result = -1;
                        }
                        return result;
                    };
                    request = service2e.BuildRequest();
                    if (request == null)
                    {
                        isAllStop = SetSignalFail(signal);
                        Log.Error($"DID 0x{signal.Id:X4} 构建写入报文失败");
                        break;
                    }
                    response = await _uds.Request(request);
                    if (response == null || (result = service2e.ParseResponse(response)) != 0)
                    {
                        isAllStop = SetSignalFail(signal);
                        Log.Error($"DID 0x{signal.Id:X4} 写入应答失败");
                        break;
                    }
                    int source = 0;
                    // Project code.
                    Service22 service22 = new Service22();
                    service22.Id = signal.Id;
                    service22.ParsePayload = w =>
                    {
                        source = signal.Parse(w, signal.DefaultValue);
                        return 0;
                    };
                    Pdu? pdu = await _uds.Request(service22.BuildRequest());
                    if (pdu == null)
                    {
                        Log.Error($"DID 0x{signal.Id:X4} 回读DID报文为空");
                        isAllStop = SetSignalFail(signal);
                        break;
                    }
                    service22.ParseResponse(pdu);
                    if (source == 0)
                    {
                        signal.Status = 2;
                    }
                    else
                    {
                        Log.Error($"DID 0x{signal.Id:X4} 回读校验不通过，与默认值偏差超限0.5");
                        isAllStop = SetSignalFail(signal);
                        break;
                    }
                }
                if (isAllStop)
                {
                    break;
                }
                else
                {
                    _currentJob.Status = 2;
                    _currentJob.ShowText = "测试通过";
                }
            } while (false);
            if (udsstarted)
            {
                await _uds.StopAsync();
            }
        });


        /// <summary>
        /// 
        /// </summary>
        public LineConfigPageViewModel(UdsService _udsService)
        {
            _uds = _udsService;

            _securityService = new McuSecurityService(_uds);
            SignalsView = CollectionViewSource.GetDefaultView(Signals);

            //_csvHelper.Init($"{GetElectricControlFolder()}\\SN设备映射表.csv");
            _BlueHelper.Init($"{GetElectricControlFolder()}\\SN蓝牙地址映射表.csv");

        }
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
    }
}
