using Serilog;
using System;
using System.Windows.Input;
using TestingSoftware.Commands;
using TestingSoftware.Models;
using TestingSoftware.Protocols;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Services;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// 下位机 IO（GPIO）外部控制：用 2F 服务接管 ECU 的油门/刹车/档位输入通道，
    /// 对应 MotorStudio 的 IO 控制页（0xD100 油门、0xD101 刹车、0xD102 档位）。
    /// 可随时通过 RETURN_CONTROL_TO_ECU 把控制权交还 ECU。
    /// </summary>
    public class IoControlViewModel : ViewModel<McuModel>
    {
        /// <summary>油门输入通道 ID。</summary>
        public const ushort IOID_ACCER_CTRL = 0xD100;

        /// <summary>刹车输入通道 ID。</summary>
        public const ushort IOID_BRAKE_CTRL = 0xD101;

        /// <summary>档位输入通道 ID。</summary>
        public const ushort IOID_GEAR_POSITION_CTRL = 0xD102;

        private readonly UdsService _uds;
        private readonly McuSecurityService _security;

        private int _targetThrottle;
        private int _targetBrake;
        private int _targetGear = 1;
        private bool _isThrottleActive;
        private bool _isBrakeActive;
        private bool _isGearActive;
        private string _statusText = "未接管：ECU 自主控制外部 IO";

        /// <summary>
        /// Create the IO control view model.
        /// </summary>
        public IoControlViewModel(UdsService uds, McuSecurityService security)
        {
            _uds = uds;
            _security = security;
        }

        /// <summary>油门目标值（0~255，按 ECU 标定解释）。</summary>
        public int TargetThrottle
        {
            get => _targetThrottle;
            set
            {
                _targetThrottle = Math.Clamp(value, 0, 255);
                OnPropertyChanged(nameof(TargetThrottle));
            }
        }

        /// <summary>刹车目标值（0~255）。</summary>
        public int TargetBrake
        {
            get => _targetBrake;
            set
            {
                _targetBrake = Math.Clamp(value, 0, 255);
                OnPropertyChanged(nameof(TargetBrake));
            }
        }

        /// <summary>档位目标值（0~255）。</summary>
        public int TargetGear
        {
            get => _targetGear;
            set
            {
                _targetGear = Math.Clamp(value, 0, 255);
                OnPropertyChanged(nameof(TargetGear));
            }
        }

        /// <summary>油门通道是否已由本工具接管。</summary>
        public bool IsThrottleActive
        {
            get => _isThrottleActive;
            private set
            {
                _isThrottleActive = value;
                OnPropertyChanged(nameof(IsThrottleActive));
                OnPropertyChanged(nameof(ThrottleStateText));
            }
        }

        /// <summary>刹车通道是否已由本工具接管。</summary>
        public bool IsBrakeActive
        {
            get => _isBrakeActive;
            private set
            {
                _isBrakeActive = value;
                OnPropertyChanged(nameof(IsBrakeActive));
                OnPropertyChanged(nameof(BrakeStateText));
            }
        }

        /// <summary>档位通道是否已由本工具接管。</summary>
        public bool IsGearActive
        {
            get => _isGearActive;
            private set
            {
                _isGearActive = value;
                OnPropertyChanged(nameof(IsGearActive));
                OnPropertyChanged(nameof(GearStateText));
            }
        }

        /// <summary>油门通道状态描述。</summary>
        public string ThrottleStateText => IsThrottleActive ? "已接管" : "ECU 控制";

        /// <summary>刹车通道状态描述。</summary>
        public string BrakeStateText => IsBrakeActive ? "已接管" : "ECU 控制";

        /// <summary>档位通道状态描述。</summary>
        public string GearStateText => IsGearActive ? "已接管" : "ECU 控制";

        /// <summary>最近一次操作结果。</summary>
        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>设置油门开度（接管油门通道）。</summary>
        public ICommand SetThrottleAction => new AsyncCommand(async sender =>
            await SendAsync(IOID_ACCER_CTRL, Service2f.SHORT_TERM_ADJUSTMENT, (byte)TargetThrottle, "油门", true));

        /// <summary>把油门控制权交还 ECU。</summary>
        public ICommand ReturnThrottleAction => new AsyncCommand(async sender =>
            await SendAsync(IOID_ACCER_CTRL, Service2f.RETURN_CONTROL_TO_ECU, null, "油门", false));

        /// <summary>设置刹车值（接管刹车通道）。</summary>
        public ICommand SetBrakeAction => new AsyncCommand(async sender =>
            await SendAsync(IOID_BRAKE_CTRL, Service2f.SHORT_TERM_ADJUSTMENT, (byte)TargetBrake, "刹车", true));

        /// <summary>把刹车控制权交还 ECU。</summary>
        public ICommand ReturnBrakeAction => new AsyncCommand(async sender =>
            await SendAsync(IOID_BRAKE_CTRL, Service2f.RETURN_CONTROL_TO_ECU, null, "刹车", false));

        /// <summary>设置档位（接管档位通道）。</summary>
        public ICommand SetGearAction => new AsyncCommand(async sender =>
            await SendAsync(IOID_GEAR_POSITION_CTRL, Service2f.SHORT_TERM_ADJUSTMENT, (byte)TargetGear, "档位", true));

        /// <summary>把档位控制权交还 ECU。</summary>
        public ICommand ReturnGearAction => new AsyncCommand(async sender =>
            await SendAsync(IOID_GEAR_POSITION_CTRL, Service2f.RETURN_CONTROL_TO_ECU, null, "档位", false));

        /// <summary>
        /// 下发 2F 控制报文。安全访问按尽力而为处理：
        /// 部分固件不支持读诊断等级（22 F1 86 无应答），此时不阻断控制下发，由 ECU 自行拒绝。
        /// </summary>
        private async Task SendAsync(ushort ioId, byte ctrlType, byte? value, string name, bool takeOver)
        {
            if (_uds == null || !_uds.Running)
            {
                StatusText = "设备未连接，无法下发 IO 控制";
                return;
            }

            try
            {
                await _security.UnlockMCUNoPopAsync();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "IO 控制：安全访问异常（继续下发）");
            }

            try
            {
                Service2f service2f = new Service2f
                {
                    Id = ioId,
                    Ctrl_type = ctrlType
                };

                if (value.HasValue)
                {
                    byte target = value.Value;
                    service2f.BuildPayload = writer =>
                    {
                        writer.Write(target);
                        return 0;
                    };
                }

                Pdu? response = await _uds.Request(service2f.BuildRequest());
                int result = response == null ? -1 : service2f.ParseResponse(response);

                if (result == 0)
                {
                    if (ioId == IOID_ACCER_CTRL) IsThrottleActive = takeOver;
                    else if (ioId == IOID_BRAKE_CTRL) IsBrakeActive = takeOver;
                    else if (ioId == IOID_GEAR_POSITION_CTRL) IsGearActive = takeOver;

                    StatusText = takeOver
                        ? $"{name}控制已接管（0x{ioId:X4} = {value}）"
                        : $"{name}控制已交还 ECU（0x{ioId:X4}）";
                    Log.Information("IO 控制：{Name} 0x{IoId:X4} 下发成功，ctrl_type=0x{CtrlType:X2}", name, ioId, ctrlType);
                }
                else
                {
                    StatusText = response == null
                        ? $"{name}控制无应答（0x{ioId:X4}），请确认设备在线"
                        : $"{name}控制被拒绝（返回 {result}）";
                    Log.Warning("IO 控制：{Name} 0x{IoId:X4} 下发失败，返回 {Result}", name, ioId, result);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"{name}控制下发异常：{ex.Message}";
                Log.Warning(ex, "IO 控制：{Name} 0x{IoId:X4} 下发异常", name, ioId);
            }
        }
    }
}
