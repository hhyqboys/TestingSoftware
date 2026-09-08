// <copyright file=PidViewModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/26 9:44:11</date>
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
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Protocols;
using TestingSoftware.Services;
using TestingSoftware.Models;
using System.Windows;
using TestingSoftware.Adapters;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// The view model for PID ultility.
    /// </summary>
    public class PidViewModel : ViewModel<McuModel>
    {
        public const UInt16 DID_PID_TORQUE = 0x0B01;
        public const UInt16 DID_PID_FLUX = 0x0B02;
        public const UInt16 DID_PID_SPEED = 0x0B03;
        public const UInt16 DID_PID_POS = 0x0B04;
        public const UInt16 DID_PID_FWC = 0x0B05;
        public const UInt16 DID_THR = 0x0907;
        public const UInt16 DID_PV_SN = 0x0003;
        public string ChipID = string.Empty;
        private readonly CsvExcelHelper _csvHelper = new CsvExcelHelper();
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


        //public System.Windows.Controls.TabItem _selectedTabItem;
        ///// <summary>
        ///// The selected tab item.
        ///// </summary>
        //public System.Windows.Controls.TabItem SelectedTabItem
        //{
        //    get => _selectedTabItem;
        //    set
        //    {
        //        _selectedTabItem = value;
        //        OnPropertyChanged(nameof(SelectedTabItem));
        //    }
        //}

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
        /// The command is used to set the PID.
        /// </summary>
        public ICommand SetCommand => new AsyncCommand(async sender =>
        {
            if (sender == null)
                return;

            string parameter = sender.ToString() ?? "";

            // Attempt to unlock the MCU using UDS Security Access (Service 0x27).
            // Returns true if unlock was successful, false otherwise.
            var success = await _securityService.UnlockMCUAsync();
            if (success)
            {
                if (parameter == "TORQUE")
                {
                    Service2e service2e = new Service2e();
                    service2e.Id = DID_PID_TORQUE;
                    service2e.BuildPayload = writer =>
                    {
                        if (Model != null)
                        {
                            writer.Write(Service2e.SwapS16(Model.PID_TORQUE_KP_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_TORQUE_KP_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_TORQUE_KI_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_TORQUE_KI_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_TORQUE_KD_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_TORQUE_KD_DIV_LOG));
                        }

                        return 0;
                    };

                    Pdu? pdu = await _uds.Request(service2e.BuildRequest());
                }
                else if (parameter == "FLUX")
                {
                    Service2e service2e = new Service2e();
                    service2e.Id = DID_PID_FLUX;
                    service2e.BuildPayload = writer =>
                    {
                        if (Model != null)
                        {
                            writer.Write(Service2e.SwapS16(Model.PID_FLUX_KP_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_FLUX_KP_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_FLUX_KI_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_FLUX_KI_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_FLUX_KD_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_FLUX_KD_DIV_LOG));
                        }

                        return 0;
                    };

                    Pdu? pdu = await _uds.Request(service2e.BuildRequest());
                }
                else if (parameter == "SPEED")
                {
                    Service2e service2e = new Service2e();
                    service2e.Id = DID_PID_SPEED;
                    service2e.BuildPayload = writer =>
                    {
                        if (Model != null)
                        {
                            writer.Write(Service2e.SwapS16(Model.PID_SPEED_KP_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_SPEED_KP_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_SPEED_KI_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_SPEED_KI_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_SPEED_KD_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_SPEED_KD_DIV_LOG));
                        }

                        return 0;
                    };

                    Pdu? pdu = await _uds.Request(service2e.BuildRequest());
                }
                else if (parameter == "POS")
                {
                    Service2e service2e = new Service2e();
                    service2e.Id = DID_PID_POS;
                    service2e.BuildPayload = writer =>
                    {
                        if (Model != null)
                        {
                            writer.Write(Service2e.SwapS16(Model.PID_POS_KP_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_POS_KP_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_POS_KI_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_POS_KI_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_POS_KD_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_POS_KD_DIV_LOG));
                        }

                        return 0;
                    };

                    Pdu? pdu = await _uds.Request(service2e.BuildRequest());
                }
                else if (parameter == "FWC")
                {
                    Service2e service2e = new Service2e();
                    service2e.Id = DID_PID_FWC;
                    service2e.BuildPayload = writer =>
                    {
                        if (Model != null)
                        {
                            writer.Write(Service2e.SwapS16(Model.PID_FWC_KP_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_FWC_KP_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_FWC_KI_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_FWC_KI_DIV_LOG));
                            writer.Write(Service2e.SwapS16(Model.PID_FWC_KD_DEFAULT));
                            writer.Write(Service2e.Swap32((UInt32)Model.PID_FWC_KD_DIV_LOG));
                        }

                        return 0;
                    };

                    Pdu? pdu = await _uds.Request(service2e.BuildRequest());
                }
            }
        }, s => _uds.Running);

        /// <summary>
        /// 
        /// </summary>
        public ICommand ReadAllSignals => new AsyncCommand(async sender =>
        {
            bool isError = false;

            bool udsstarted = false;
            if (!_uds.Running)
            {
                await _uds.StartAsync();
                udsstarted = true;
            }
            App.isAnalysis = true;
            // Project code.
            Service22 service22 = new Service22();
            foreach (var signalvm in _svms)
            {


                try
                {
                    service22.Id = signalvm.Id;
                    service22.ParsePayload = w =>
                    {
                        signalvm.Parse( w);
                        return 0;
                    };
                    Pdu? pdu = await _uds.Request(service22.BuildRequest());
                    if (pdu != null)
                    {
                        var res = service22.ParseResponse(pdu);
                        if (!isError)
                        {
                            isError = res != 0;
                        }

                    }
                    else
                    {
                        isError = true;
                    }
                }
                catch
                {
                    isError = true;
                }
            }

            if (isError)
            {
                MessageBox.Show("Error reading signal, partial signal reading failed!");
            }
            if (udsstarted)
            {
                await _uds.StopAsync();
            }
            SignalsView?.Refresh();
            App.isAnalysis = false;
        });

        /// <summary>
        /// 
        /// </summary>
        public ICommand RestoreDefault => new AsyncCommand(async sender =>
        {
            for (int i = 0; i < _svms.Count; i++)
            {
                if (!string.IsNullOrEmpty(_svms[i].DefaultValue))
                    _svms[i].Text = _svms[i].DefaultValue;
            }
        });
        /// <summary>
        /// 
        /// </summary>
        public ICommand ReadSigCommand => new AsyncCommand(async sender =>
        {
            bool udsstarted = false;
            if (!_uds.Running)
            {
                await _uds.StartAsync();
                udsstarted = true;
            }
            App.isAnalysis = true;

            var parameter = sender as SignalViewModel;
            if (parameter == null) return;
            // Project code.
            Service22 service22 = new Service22();
            service22.Id = parameter.Id;
            service22.ParsePayload = w =>
            {
                parameter.Parse( w);
                return 0;
            };
            Pdu? pdu = await _uds.Request(service22.BuildRequest());
            if (pdu != null) { service22.ParseResponse(pdu); }
            else
            {
                MessageBox.Show("Error reading signal");
            }
            if (udsstarted)
            {
                await _uds.StopAsync();
            }
            SignalsView?.Refresh();
            App.isAnalysis = false;
        });




        /// <summary>
        /// 
        /// </summary>
        public ICommand WriteSigCommand => new AsyncCommand(async sender =>
        {
            int result = -1;
            bool udsstarted = false;
            var parameter = sender as SignalViewModel;

            do
            {
                if (parameter == null) break;

                if (!_uds.Running)
                {
                    result = await _uds.StartAsync();
                    if (result != 0) break;
                    await Task.Delay(1000);
                    udsstarted = true;
                }
                if (parameter?.Id == 0xf18d)
                {
                    ReadID();
                    _csvHelper.Update(ChipID, parameter.Text);
                }
                Pdu request;
                Pdu? response;
                var success = await _securityService.UnlockMCUAsync();
                if (success)
                {
                    // Write the programming date.
                    Service2e service2e = new Service2e();
                    service2e.Id = parameter.Id;


                    service2e.BuildPayload += w =>
                    {
                        int result = 0;
                        var vms = _svms.Where(s => s.Id == parameter.Id).OrderBy(s => s.Offset).ToArray();
                        if (vms != null && vms.Length > 0)
                        {
                            foreach (var vm in vms)
                            {
                                result = vm.Build(w);
                            }
                        }
                        else
                        {
                            result = -1;
                        }

                        return result;
                    };

                    request = service2e.BuildRequest();
                    response = await _uds.Request(request);
                    if (response == null || (result = service2e.ParseResponse(response)) != 0)
                    {
                        MessageBox.Show($"Write failed: {result}");
                        break;
                    }
                    if (parameter.Id == 0xf18d)
                    {
                        _BlueHelper.Update(BluetoothInfo.macAddress, parameter.Text);
                    }
                }
            } while (false);
            if (udsstarted)
            {
                await _uds.StopAsync();
            }
        });




        /// <summary>
        ///  Read the PID value
        /// </summary>
        public ICommand ReadCommand => new AsyncCommand(async sender =>
        {
            if (sender == null)
                return;
            string parameter = sender.ToString() ?? "";

            // Attempt to unlock the MCU using UDS Security Access (Service 0x27).
            var success = await _securityService.UnlockMCUAsync();
            if (success)
            {
                if (parameter == "TORQUE")
                {
                    Service22 service22 = new Service22();
                    service22.Id = DID_PID_TORQUE;
                    service22.ParsePayload += reader =>
                    {
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OV_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidTorqueKpDefault = (short)OV_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var UV_32 = DiagService.Swap32(reader.ReadUInt32());
                            PidTorqueKpDiv = (ushort)UV_32;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OC_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidTorqueKiDefault = OC_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Motor_T = DiagService.Swap32(reader.ReadUInt32());
                            PidTorqueKiDiv = (ushort)Motor_T;

                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.SwapS16(reader.ReadInt16());
                            PidTorqueKdDefault = Mcu_T;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.Swap32(reader.ReadUInt32());
                            PidTorqueKdDiv = (ushort)Mcu_T;
                        }
                        MessageBox.Show("Read operation completed");
                        return 0;
                    };
                    Pdu? pdu = await _uds.Request(service22.BuildRequest());
                    if (pdu != null) { service22.ParseResponse(pdu); }
                }
                else if (parameter == "FLUX")
                {
                    Service22 service22 = new Service22();
                    service22.Id = DID_PID_FLUX;
                    service22.ParsePayload += reader =>
                    {
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OV_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidFluxKpDefault = (short)OV_16;

                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var UV_32 = DiagService.Swap32(reader.ReadUInt32());
                            PidFluxKpDiv = (ushort)UV_32;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OC_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidFluxKiDefault = OC_16;

                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Motor_T = DiagService.Swap32(reader.ReadUInt32());
                            PidFluxKiDiv = (ushort)Motor_T;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.SwapS16(reader.ReadInt16());
                            PidFluxKdDefault = Mcu_T;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.Swap32(reader.ReadUInt32());
                            PidFluxKdDiv = (ushort)Mcu_T;
                        }
                        MessageBox.Show("Read operation completed");
                        return 0;
                    };
                    Pdu? pdu = await _uds.Request(service22.BuildRequest());
                    if (pdu != null) { service22.ParseResponse(pdu); }
                }
                else if (parameter == "SPEED")
                {
                    Service22 service22 = new Service22();
                    service22.Id = DID_PID_SPEED;
                    service22.ParsePayload += reader =>
                    {
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OV_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidSpeedKpDefault = (short)OV_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var UV_32 = DiagService.Swap32(reader.ReadUInt32());
                            PidSpeedKpDiv = (ushort)UV_32;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OC_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidSpeedKiDefault = OC_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Motor_T = DiagService.Swap32(reader.ReadUInt32());
                            PidSpeedKiDiv = (ushort)Motor_T;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.SwapS16(reader.ReadInt16());
                            PidSpeedKdDefault = Mcu_T;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.Swap32(reader.ReadUInt32());
                            PidSpeedKdDiv = (ushort)Mcu_T;
                        }
                        MessageBox.Show("Read operation completed");
                        return 0;
                    };
                    Pdu? pdu = await _uds.Request(service22.BuildRequest());
                    if (pdu != null) { service22.ParseResponse(pdu); }
                }
                else if (parameter == "POS")
                {
                    Service22 service22 = new Service22();
                    service22.Id = DID_PID_POS;
                    service22.ParsePayload += reader =>
                    {
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OV_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidPosKpDefault = (short)OV_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var UV_32 = DiagService.Swap32(reader.ReadUInt32());
                            PidPosKpDiv = (ushort)UV_32;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OC_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidPosKiDefault = OC_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Motor_T = DiagService.Swap32(reader.ReadUInt32());
                            PidPosKiDiv = (ushort)Motor_T;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.SwapS16(reader.ReadInt16());
                            PidPosKdDefault = Mcu_T;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.Swap32(reader.ReadUInt32());
                            PidPosKdDiv = (ushort)Mcu_T;
                        }
                        MessageBox.Show("Read operation completed");
                        return 0;
                    };
                    Pdu? pdu = await _uds.Request(service22.BuildRequest());
                    if (pdu != null) { service22.ParseResponse(pdu); }
                }
                else if (parameter == "FWC")
                {
                    Service22 service22 = new Service22();
                    service22.Id = DID_PID_FWC;
                    service22.ParsePayload += reader =>
                    {
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OV_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidFwcKpDefault = (short)OV_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var UV_32 = DiagService.Swap32(reader.ReadUInt32());
                            PidFwcKpDiv = (ushort)UV_32;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var OC_16 = DiagService.SwapS16(reader.ReadInt16());
                            PidFwcKiDefault = OC_16;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Motor_T = DiagService.Swap32(reader.ReadUInt32());
                            PidFwcKiDiv = (ushort)Motor_T;
                        }
                        if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.SwapS16(reader.ReadInt16());
                            PidFwcKdDefault = Mcu_T;
                        }
                        if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                        {
                            var Mcu_T = DiagService.Swap32(reader.ReadUInt32());
                            PidFwcKdDiv = (ushort)Mcu_T;
                        }
                        MessageBox.Show("Read operation completed");
                        return 0;
                    };
                    Pdu? pdu = await _uds.Request(service22.BuildRequest());
                    if (pdu != null) { service22.ParseResponse(pdu); }
                }
            }

        });

        /// <summary>
        /// Create the view model for the PID ultility.
        /// </summary>
        public PidViewModel(UdsService _udsService)
        {
            _uds = _udsService;
            _uds.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(UdsService.Running))
                {
                    OnPropertyChanged(nameof(SetCommand));
                }
            };
            _securityService = new McuSecurityService(_uds);
            SignalsView = CollectionViewSource.GetDefaultView(Signals);

            _csvHelper.Init($"{GetElectricControlFolder()}\\SN设备映射表.csv");
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
            string targetFolder = Path.Combine(roamingPath, "TestingSoftware");

            // 如果不存在则创建
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            // 返回绝对路径
            return targetFolder;
        }
        /// <summary>
        /// Default torque control loop proportional gain.
        /// </summary>
        public Int16 PidTorqueKpDefault
        {
            get => Model?.PID_TORQUE_KP_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_TORQUE_KP_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidTorqueKpDefault));
            }
        }

        /// <summary>
        /// Default torque control loop integral gain.
        /// </summary>
        public Int16 PidTorqueKiDefault
        {
            get => Model?.PID_TORQUE_KI_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_TORQUE_KI_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidTorqueKiDefault));
            }
        }

        /// <summary>
        /// Default torque control loop differential gain.
        /// </summary>
        public Int16 PidTorqueKdDefault
        {
            get => Model?.PID_TORQUE_KD_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_TORQUE_KD_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidTorqueKdDefault));
            }
        }

        /// <summary>
        /// Torque control loop proportional gain divider.
        /// </summary>
        public UInt16 PidTorqueKpDiv
        {
            get => Model?.PID_TORQUE_KP_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_TORQUE_KP_DIV = value;
                    Model.PID_TORQUE_KP_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidTorqueKpDiv));
            }
        }

        /// <summary>
        /// Torque control loop integral gain divider.
        /// </summary>
        public UInt16 PidTorqueKiDiv
        {
            get => Model?.PID_TORQUE_KI_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_TORQUE_KI_DIV = value;
                    Model.PID_TORQUE_KI_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidTorqueKiDiv));
            }
        }

        /// <summary>
        /// Torque control loop differential gain divider.
        /// </summary>
        public UInt16 PidTorqueKdDiv
        {
            get => Model?.PID_TORQUE_KD_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_TORQUE_KD_DIV = value;
                    Model.PID_TORQUE_KD_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidTorqueKdDiv));
            }
        }

        /// <summary>
        /// Default flux control loop proportional gain.
        /// </summary>
        public Int16 PidFluxKpDefault
        {
            get => Model?.PID_FLUX_KP_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FLUX_KP_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidFluxKpDefault));
            }
        }

        /// <summary>
        /// Default flux control loop integral gain.
        /// </summary>
        public Int16 PidFluxKiDefault
        {
            get => Model?.PID_FLUX_KI_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FLUX_KI_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidFluxKiDefault));
            }
        }

        /// <summary>
        /// Default flux control loop differential gain.
        /// </summary>
        public Int16 PidFluxKdDefault
        {
            get => Model?.PID_FLUX_KD_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FLUX_KD_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidFluxKdDefault));
            }
        }

        /// <summary>
        /// Flux control loop proportional gain divider.
        /// </summary>
        public UInt16 PidFluxKpDiv
        {
            get => Model?.PID_FLUX_KP_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FLUX_KP_DIV = value;
                    Model.PID_FLUX_KP_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidFluxKpDiv));
            }
        }

        /// <summary>
        /// Flux control loop integral gain divider.
        /// </summary>
        public UInt16 PidFluxKiDiv
        {
            get => Model?.PID_FLUX_KI_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FLUX_KI_DIV = value;
                    Model.PID_FLUX_KI_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidFluxKiDiv));
            }
        }

        /// <summary>
        /// Flux control loop differential gain divider.
        /// </summary>
        public UInt16 PidFluxKdDiv
        {
            get => Model?.PID_FLUX_KD_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FLUX_KD_DIV = value;
                    Model.PID_FLUX_KD_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidFluxKdDiv));
            }
        }

        /// <summary>
        /// Default Speed control loop proportional gain.
        /// </summary>
        public Int16 PidSpeedKpDefault
        {
            get => Model?.PID_SPEED_KP_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_SPEED_KP_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidSpeedKpDefault));
            }
        }

        /// <summary>
        /// Default Speed control loop integral gain.
        /// </summary>
        public Int16 PidSpeedKiDefault
        {
            get => Model?.PID_SPEED_KI_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_SPEED_KI_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidSpeedKiDefault));
            }
        }

        /// <summary>
        /// Default Speed control loop differential gain.
        /// </summary>
        public Int16 PidSpeedKdDefault
        {
            get => Model?.PID_SPEED_KD_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_SPEED_KD_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidSpeedKdDefault));
            }
        }

        /// <summary>
        /// Speed control loop proportional gain divider.
        /// </summary>
        public UInt16 PidSpeedKpDiv
        {
            get => Model?.PID_SPEED_KP_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_SPEED_KP_DIV = value;
                    Model.PID_SPEED_KP_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidSpeedKpDiv));
            }
        }

        /// <summary>
        /// Speed control loop integral gain divider.
        /// </summary>
        public UInt16 PidSpeedKiDiv
        {
            get => Model?.PID_SPEED_KI_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_SPEED_KI_DIV = value;
                    Model.PID_SPEED_KI_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidSpeedKiDiv));
            }
        }

        /// <summary>
        /// Speed control loop differential gain divider.
        /// </summary>
        public UInt16 PidSpeedKdDiv
        {
            get => Model?.PID_SPEED_KD_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_SPEED_KD_DIV = value;
                    Model.PID_SPEED_KD_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidSpeedKdDiv));
            }
        }

        /// <summary>
        /// Default position control loop proportional gain.
        /// </summary>
        public Int16 PidPosKpDefault
        {
            get => Model?.PID_POS_KP_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_POS_KP_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidPosKpDefault));
            }
        }

        /// <summary>
        /// Default position control loop integral gain.
        /// </summary>
        public Int16 PidPosKiDefault
        {
            get => Model?.PID_POS_KI_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_POS_KI_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidPosKiDefault));
            }
        }

        /// <summary>
        /// Default position control loop differential gain.
        /// </summary>
        public Int16 PidPosKdDefault
        {
            get => Model?.PID_POS_KD_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_POS_KD_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidPosKdDefault));
            }
        }

        /// <summary>
        /// Position control loop proportional gain divider.
        /// </summary>
        public UInt16 PidPosKpDiv
        {
            get => Model?.PID_POS_KP_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_POS_KP_DIV = value;
                    Model.PID_POS_KP_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidPosKpDiv));
            }
        }

        /// <summary>
        /// Position control loop integral gain divider.
        /// </summary>
        public UInt16 PidPosKiDiv
        {
            get => Model?.PID_POS_KI_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_POS_KI_DIV = value;
                    Model.PID_POS_KI_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidPosKiDiv));
            }
        }

        /// <summary>
        /// Position control loop differential gain divider.
        /// </summary>
        public UInt16 PidPosKdDiv
        {
            get => Model?.PID_POS_KD_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_POS_KD_DIV = value;
                    Model.PID_POS_KD_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidPosKdDiv));
            }
        }


        /// <summary>
        /// Default flux weak control loop proportional gain.
        /// </summary>
        public Int16 PidFwcKpDefault
        {
            get => Model?.PID_FWC_KP_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FWC_KP_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidFwcKpDefault));
            }
        }


        /// <summary>
        /// Default flux weak control loop integral gain.
        /// </summary>
        public Int16 PidFwcKiDefault
        {
            get => Model?.PID_FWC_KI_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FWC_KI_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidFwcKiDefault));
            }
        }

        /// <summary>
        /// Default flux weak control loop differential gain.
        /// </summary>
        public Int16 PidFwcKdDefault
        {
            get => Model?.PID_FWC_KD_DEFAULT ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FWC_KD_DEFAULT = value;
                }
                OnPropertyChanged(nameof(PidFwcKdDefault));
            }
        }

        /// <summary>
        /// Flux weak control loop proportional gain divider.
        /// </summary>
        public UInt16 PidFwcKpDiv
        {
            get => Model?.PID_FWC_KP_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FWC_KP_DIV = value;
                    Model.PID_FWC_KP_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidFwcKpDiv));
            }
        }

        /// <summary>
        /// Flux weak control loop integral gain divider.
        /// </summary>
        public UInt16 PidFwcKiDiv
        {
            get => Model?.PID_FWC_KI_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FWC_KI_DIV = value;
                    Model.PID_FWC_KI_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidFwcKiDiv));
            }
        }

        /// <summary>
        /// Flux weak control loop differential gain divider.
        /// </summary>
        public UInt16 PidFwcKdDiv
        {
            get => Model?.PID_FWC_KD_DIV ?? 0;
            set
            {
                if (Model != null)
                {
                    Model.PID_FWC_KD_DIV = value;
                    Model.PID_FWC_KD_DIV_LOG = PowerOfTwo(value);
                }
                OnPropertyChanged(nameof(PidFwcKdDiv));
            }
        }

        /// <summary>
        /// Update the MCU parameters after the model has been changed.
        /// </summary>
        protected override void OnModelChanged()
        {
            if (Model == null)
                return;

            PidTorqueKpDefault = Model.PID_TORQUE_KP_DEFAULT;
            PidTorqueKiDefault = Model.PID_TORQUE_KI_DEFAULT;
            PidTorqueKdDefault = Model.PID_TORQUE_KD_DEFAULT;
            PidTorqueKpDiv = Model.PID_TORQUE_KP_DIV;
            PidTorqueKiDiv = Model.PID_TORQUE_KI_DIV;
            PidTorqueKdDiv = Model.PID_TORQUE_KD_DIV;
            PidFluxKpDefault = Model.PID_FLUX_KP_DEFAULT;
            PidFluxKiDefault = Model.PID_FLUX_KI_DEFAULT;
            PidFluxKdDefault = Model.PID_FLUX_KD_DEFAULT;
            PidFluxKpDiv = Model.PID_FLUX_KP_DIV;
            PidFluxKiDiv = Model.PID_FLUX_KI_DIV;
            PidFluxKdDiv = Model.PID_FLUX_KD_DIV;
            PidSpeedKpDefault = Model.PID_SPEED_KP_DEFAULT;
            PidSpeedKiDefault = Model.PID_SPEED_KI_DEFAULT;
            PidSpeedKdDefault = Model.PID_SPEED_KD_DEFAULT;
            PidSpeedKpDiv = Model.PID_SPEED_KP_DIV;
            PidSpeedKiDiv = Model.PID_SPEED_KI_DIV;
            PidSpeedKdDiv = Model.PID_SPEED_KD_DIV;
            PidPosKpDefault = Model.PID_POS_KP_DEFAULT;
            PidPosKiDefault = Model.PID_POS_KI_DEFAULT;
            PidPosKdDefault = Model.PID_POS_KD_DEFAULT;
            PidPosKpDiv = Model.PID_POS_KP_DIV;
            PidPosKiDiv = Model.PID_POS_KI_DIV;
            PidPosKdDiv = Model.PID_POS_KD_DIV;

            PidFwcKpDefault = Model.PID_FWC_KP_DEFAULT;
            PidFwcKiDefault = Model.PID_FWC_KI_DEFAULT;
            PidFwcKdDefault = Model.PID_FWC_KD_DEFAULT;
            PidFwcKpDiv = Model.PID_FWC_KP_DIV;
            PidFwcKiDiv = Model.PID_FWC_KI_DIV;
            PidFwcKdDiv = Model.PID_FWC_KD_DIV;
        }

        public static byte PowerOfTwo(UInt16 n)
        {
            byte exponent = 0;

            while (n > 1)
            {
                n >>= 1;
                exponent++;
            }

            return exponent;
        }

        public async void ReadID()
        {
            byte[] data = { 0 };
            Service22 service22 = new Service22();
            service22.Id = DID_PV_SN;


            service22.ParsePayload = reader =>
            {

                //正确的数据只有12个字节
                data = reader.ReadBytes((int)reader.BaseStream.Length);
                ChipID = string.Join("-", data.Select(b => b.ToString("X2")));
                return 0;
            };
            Pdu? pdu = await _uds.Request(service22.BuildRequest());
            if (pdu != null)
            {
                service22.ParseResponse(pdu);
            }
        }
    }
}
