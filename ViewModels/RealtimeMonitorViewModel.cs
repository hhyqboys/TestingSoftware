// <copyright file=RealtimeMonitorViewModel company="Sumarte Technology">
// Copyright (c) 2026 All Rights Reserved
// </copyright>
// <summary>
// 实时监控面板：沿用 MotorStudio 的“动态 DID + 2A 主动上传”方案，
// 由上位机下发 2C 定义动态 DID（0xF2E3），再用 2A 以快速率开启下位机周期上报，
// 上位机只被动接收周期帧，实时刷新母线电流 / 转速 / 电机温度 / 故障码。
// </summary>
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TestingSoftware.Commands;
using TestingSoftware.Models;
using TestingSoftware.Protocols;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Services;
using TestingSoftware.Utilities;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// 实时监控 ViewModel（母线电流、转速、电机温度、故障码）。
    /// </summary>
    public class RealtimeMonitorViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// 动态 DID 的标识符，与 MotorStudio 保持同一约定（高位必须是 0xF2/0xF3）。
        /// </summary>
        public const UInt16 DYNAMIC_DID = 0xF2E3;

        /// <summary>
        /// 监控通道使用的源 DID 及其数据类型。
        /// 母线电流 0x909(S16/A)、转速 0x903(S16/RPM)、电机温度 0x901(S16/Celsius)、故障码 0x722(U16 位掩码)。
        /// </summary>
        public const UInt16 DID_BUS_CURRENT = 0x909;
        public const UInt16 DID_MOTOR_SPEED = 0x903;
        public const UInt16 DID_MOTOR_TEMPERATURE = 0x901;
        public const UInt16 DID_FAULT_CODE = 0x722;

        /// <summary>
        /// UI 刷新最小间隔（下位机上报速率远高于屏幕刷新，必须限频，否则会压垮 UI 线程）。
        /// </summary>
        private const int UiNotifyMinIntervalMs = 100;

        /// <summary>
        /// 停止主动上传时，2A/2C 请求的超时，避免设备异常时卡住停止流程。
        /// </summary>
        private const int StopRequestTimeoutMs = 1200;

        private readonly UdsService _uds;
        private readonly McuSecurityService _securityService;
        private readonly DynamicDataIdentifier _dynamicDid = new DynamicDataIdentifier();

        private CancellationTokenSource? _cts;
        private Task? _rxTask;

        private long _frameCount;
        private long _pendingFrames;
        /// <summary>最近一帧周期数据被解析的时刻（HighResolutionTimer.Milliseconds），用于判定实时数据是否新鲜。</summary>
        private long _lastFrameMs;
        private int _frameRate;
        private long _lastNotifyMs = -UiNotifyMinIntervalMs;
        private long _lastRateMs;

        private bool _isMonitoring;
        private string _monitorStatus = "未开启（点击“开始监控”或开始测试后自动开启）";

        /// <summary>实时数据是否有效：收到新帧=true；断连 / 长时间无新帧=false，界面统一显示 FF。</summary>
        private bool _dataValid;

        /// <summary>数据无效时界面显示的占位文字。</summary>
        private const string StaleText = "FF";

        /// <summary>实时数据有效期：超过该时间没有新帧，即按无效数据处理（显示 FF）。</summary>
        private const int DataStaleTimeoutMs = 2000;

        /// <summary>数据新鲜度看门狗（500ms 一次）：Windows 断连事件会晚到，这里按帧时间自行判定。</summary>
        private readonly DispatcherTimer _staleTimer;

        private double _busCurrent;
        private double _motorSpeed;
        private double _motorTemperature;
        private uint _faultCode;

        /// <summary>母线电流历史峰值（只增不减，用于锁定仪表量程）。</summary>
        private double _busCurrentPeak;

        /// <summary>转速历史峰值（只增不减，用于锁定仪表量程）。</summary>
        private double _motorSpeedPeak;

        /// <summary>母线电流量程余量（A）：量程 = 峰值 + 30。</summary>
        private const double BusCurrentMargin = 30d;

        /// <summary>转速量程余量（RPM）：量程 = 峰值 + 200。</summary>
        private const double MotorSpeedMargin = 200d;

        /// <summary>电机温度仪表固定量程上限（℃）。</summary>
        private const double TemperatureMaximum = 200d;

        /// <summary>本次连接内是否已做过会话准备（10 03）。</summary>
        private bool _sessionPrepared;

        /// <summary>安全访问连续失败次数；达 2 次后本连接内不再重复尝试（固件可能不支持读 22 F1 86）。</summary>
        private int _securityFailures;

        /// <summary>
        /// 开启 / 停止监控的串行锁。连接成功后主流程与测试启动流程会同时调用 StartAsync，
        /// 两个 StartAsync 并发下发 2C/2A 会互相抢应答（日志里成串的 "Uds received async is null"），
        /// 这里保证同一时刻只有一个在跑。
        /// </summary>
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

        /// <summary>本次连接内是否已收到过 2A(快速率) 的 6A 正响应（即下位机已在上报）。</summary>
        private bool _uploadStarted;

        /// <summary>
        /// 属性变更事件。
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 母线电流监控通道。
        /// </summary>
        public SignalViewModel BusCurrentSignal { get; }

        /// <summary>
        /// 转速监控通道。
        /// </summary>
        public SignalViewModel MotorSpeedSignal { get; }

        /// <summary>
        /// 电机温度监控通道。
        /// </summary>
        public SignalViewModel MotorTemperatureSignal { get; }

        /// <summary>
        /// 故障码监控通道。
        /// </summary>
        public SignalViewModel FaultCodeSignal { get; }

        /// <summary>
        /// 参与动态 DID 定义的全部通道，顺序即上报解析顺序。
        /// </summary>
        public List<SignalViewModel> Signals { get; } = new List<SignalViewModel>();

        /// <summary>
        /// 开始/停止监控命令。
        /// </summary>
        public ICommand ToggleMonitorCommand { get; }

        /// <summary>
        /// 当前检出的故障明细（每项一条，故障可能同时存在多个）。
        /// </summary>
        public ObservableCollection<string> FaultItems { get; } = new ObservableCollection<string>();

        public RealtimeMonitorViewModel(UdsService uds, McuSecurityService securityService)
        {
            _uds = uds;
            _securityService = securityService;

            BusCurrentSignal = CreateSignal(DID_BUS_CURRENT, "母线电流", "A", SignalModel.S16);
            MotorSpeedSignal = CreateSignal(DID_MOTOR_SPEED, "转速", "RPM", SignalModel.S16);
            MotorTemperatureSignal = CreateSignal(DID_MOTOR_TEMPERATURE, "电机温度", "Celsius", SignalModel.S16);
            FaultCodeSignal = CreateSignal(DID_FAULT_CODE, "故障码", "N/A", SignalModel.U16);

            Signals.Add(BusCurrentSignal);
            Signals.Add(MotorSpeedSignal);
            Signals.Add(MotorTemperatureSignal);
            Signals.Add(FaultCodeSignal);

            // 使用出厂默认标定参数（工程加载后会用工程里的 RShunt/Aop 覆盖）。
            Configure(null);

            ToggleMonitorCommand = new AsyncCommand(async _ => await ToggleAsync());

            // 数据新鲜度看门狗：断链后 Windows 事件可能晚到一两分钟，界面不能一直挂着旧值
            _staleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _staleTimer.Tick += (s, e) => CheckDataFreshness();
            _staleTimer.Start();
        }

        #region 可绑定属性

        /// <summary>
        /// 是否正在接收主动上报数据。
        /// </summary>
        public bool IsMonitoring
        {
            get => _isMonitoring;
            private set
            {
                if (_isMonitoring == value) return;
                _isMonitoring = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ToggleButtonText));
            }
        }

        /// <summary>
        /// 监控按钮文字。
        /// </summary>
        public string ToggleButtonText => IsMonitoring ? "停止监控" : "开始监控";

        /// <summary>
        /// 监控状态说明（下发结果、错误原因等）。
        /// </summary>
        public string MonitorStatus
        {
            get => _monitorStatus;
            private set
            {
                if (_monitorStatus == value) return;
                _monitorStatus = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 母线电流显示值（A）；数据无效（断连 / 超时）显示 FF。
        /// </summary>
        public string BusCurrentText => _dataValid ? $"{_busCurrent:F1}" : StaleText;

        /// <summary>
        /// 转速显示值（RPM）；数据无效（断连 / 超时）显示 FF。
        /// </summary>
        public string MotorSpeedText => _dataValid ? $"{_motorSpeed:F0}" : StaleText;

        /// <summary>
        /// 电机温度显示值（℃）；数据无效（断连 / 超时）显示 FF。
        /// </summary>
        public string MotorTemperatureText => _dataValid ? $"{_motorTemperature:F1}" : StaleText;

        /// <summary>
        /// 故障码原始位掩码（十六进制显示）；数据无效（断连 / 超时）显示 FF。
        /// </summary>
        public string FaultCodeText => _dataValid ? $"0x{_faultCode:X4}" : StaleText;

        /// <summary>
        /// 故障码翻译结果。
        /// </summary>
        public string FaultText => !_dataValid ? "数据无效" : (_faultCode == 0 ? "无故障" : TestingSoftwareViewModel.GetFaultText(_faultCode));

        /// <summary>
        /// 是否存在故障（用于界面告警着色）；数据无效时不报警。
        /// </summary>
        public bool HasFault => _dataValid && _faultCode != 0;

        /// <summary>
        /// 上报速率显示；数据无效时显示 FF。
        /// </summary>
        public string FrameRateText => _dataValid ? $"约 {_frameRate} 帧/秒" : StaleText;

        /// <summary>
        /// 累计接收帧数显示（每次连接清零）。
        /// </summary>
        public string FrameCountText => $"累计 {_frameCount} 帧";

        #region 仪表绑定值

        /// <summary>
        /// 母线电流仪表当前值（A）；数据无效时归零，避免指针停在旧值上误导。
        /// </summary>
        public double BusCurrentValue => _dataValid ? _busCurrent : 0;

        /// <summary>
        /// 母线电流仪表量程上限（A）：历史峰值 + 30，只增不减。
        /// </summary>
        public double BusCurrentMaximum => Math.Max(BusCurrentMargin, Math.Ceiling(_busCurrentPeak + BusCurrentMargin));

        /// <summary>
        /// 转速仪表当前值（RPM）；数据无效时归零。
        /// </summary>
        public double MotorSpeedValue => _dataValid ? _motorSpeed : 0;

        /// <summary>
        /// 当前转速（RPM），直接从最近一帧 2A 周期数据取，不受 UI 限频影响，供测试判定使用。
        /// </summary>
        public double CurrentMotorSpeed => ToDouble(MotorSpeedSignal);

        /// <summary>最近一帧周期数据的解析时刻（毫秒）；0 表示还没收到过数据。</summary>
        public long LastFrameMs => _lastFrameMs;

        /// <summary>
        /// 转速仪表量程上限（RPM）：历史峰值 + 200，只增不减。
        /// </summary>
        public double MotorSpeedMaximum => Math.Max(MotorSpeedMargin, Math.Ceiling(_motorSpeedPeak + MotorSpeedMargin));

        /// <summary>
        /// 电机温度仪表当前值（℃）；数据无效时归零。
        /// </summary>
        public double MotorTemperatureValue => _dataValid ? _motorTemperature : 0;

        /// <summary>
        /// 电机温度仪表量程上限（固定 0~200℃）。
        /// </summary>
        public double MotorTemperatureMaximum => TemperatureMaximum;

        /// <summary>
        /// 故障条数/概要文字。
        /// </summary>
        public string FaultSummaryText => HasFault ? $"检出 {FaultItems.Count} 项故障" : "无故障";

        #endregion

        #endregion

        /// <summary>
        /// 用工程（或出厂默认）的采样标定参数重建各通道的换算系数。
        /// </summary>
        /// <param name="project">已加载的工程模型，可为 null。</param>
        public void Configure(TestingSoftwareModel? project)
        {
            foreach (var signal in Signals)
            {
                SignalModel model = signal._model;
                if (project != null)
                {
                    model.Aop = project.Aop;
                    model.RShunt = project.RShunt;
                    model.VbusAop = project.VbusAop;
                    model.AdcRefVolt = project.AdcRefVolt;
                    model.NominalBusVoltage = project.NominalBusVoltage;
                }

                // 按单位匹配仿射变换（A / RPM / Celsius …），与工程内信号使用同一套换算。
                model.UpdateAffine();
                model.BuildReferences();
            }
        }

        /// <summary>
        /// 切换监控状态。
        /// </summary>
        public async Task ToggleAsync()
        {
            if (IsMonitoring)
            {
                await StopAsync();
            }
            else
            {
                await StartAsync();
            }
        }

        /// <summary>
        /// 下发 2C 定义动态 DID + 2A 开启主动上传，并启动周期帧接收循环。
        /// </summary>
        /// <returns>0 成功，其它为失败。</returns>
        public async Task<int> StartAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (IsMonitoring) return 0;
                return await StartCoreAsync();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>真正的开启流程；调用方必须已持有 _lifecycleLock。</summary>
        private async Task<int> StartCoreAsync()
        {
            // 蓝牙已连上时 UDS 接收任务可能还没起，这里补一次启动，保证能收到 2A 周期帧。
            if (!_uds.Running)
            {
                // 协议栈重启 = 新会话：扩展会话与"上报已开启"状态都归零
                _sessionPrepared = false;
                _uploadStarted = false;
                try
                {
                    await _uds.StartAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "实时监控：启动 UDS 服务失败");
                }
            }

            if (!_uds.Running)
            {
                MonitorStatus = "未连接设备，无法开启实时监控";
                return -1;
            }

            try
            {
                // 2A / 2C 只在扩展会话下被固件接受：默认会话下发直接回 7F xx 7F，
                // 日志里连接成功后连试 3 次全废就是这么来的。这里先尽力进 10 03。
                if (!_sessionPrepared)
                {
                    await TryExtendedSessionAsync();
                    _sessionPrepared = _uds.Running;
                }

                // 先复位下位机可能残留的周期上报与动态定义，避免长度/顺序错位；
                // 本轮上报已确认在跑时不要复位，否则和随后的 2A 互相打架。
                if (!_uploadStarted)
                {
                    await ResetPeriodDidAsync();
                }

                // 1) 由监控通道重新构建动态 DID，并下发 2C（defineDataByDataIdentifier）。
                _dynamicDid.Clear();
                foreach (var signal in Signals)
                {
                    _dynamicDid.AddSignal(signal);
                }

                Service2c service2c = new Service2c
                {
                    Id = DYNAMIC_DID,
                    Subfunc = Service2c.DEFINE_BY_IDENTIFIER,
                    BuildPayload = writer => _dynamicDid.BuildRequest(writer)
                };

                Pdu? response = await _uds.Request(service2c.BuildRequest());
                int result = response == null ? -1 : service2c.ParseResponse(response);
                if (result != 0)
                {
                    MonitorStatus = $"定义动态 DID 0x{DYNAMIC_DID:X4} 失败";
                    Log.Warning("实时监控：2C 定义动态 DID 失败，返回 {Result}", result);
                    return -3;
                }

                // 2) 先把 Uds 的周期帧过滤标识设为动态 DID 低字节，
                //    否则 2A 周期帧会被当成普通应答塞进请求队列，污染后续 22 请求的应答。
                _uds.Pid = (byte)(DYNAMIC_DID & 0xFF);

                // 3) 下发 2A 以快速率开启下位机主动上传。
                //    本轮已经拿到过 6A（下位机正在上报）就不要再发一次：上报洪水期再发 2A 抢不到应答，
                //    这正是之前"2A 开启失败返回 -4、重试三次全废"的原因。
                if (_uploadStarted)
                {
                    Log.Information("实时监控：本轮主动上传已开启，跳过重复下发 2A");
                }
                else
                {
                    Service2a service2a = new Service2a
                    {
                        Mode = Service2a.FAST_RATE,
                        Ids = new byte[] { (byte)(DYNAMIC_DID & 0xFF) }
                    };

                    // 本版固件 2A 开启后直接进入周期上报，通常不回 6A（实测迟到 2s 左右），
                    // 用 Request2aAsync：只把 7F 2A NRC 判为失败，超时无否定响应按已下发处理。
                    var ack = await _uds.Request2aAsync(service2a.BuildRequest());
                    if (ack.Result != 0)
                    {
                        int nrc = ack.Response?.Data is { Length: >= 3 } d && d[0] == 0x7F ? d[2] : -1;
                        MonitorStatus = nrc >= 0 ? $"开启主动上传失败（NRC 0x{nrc:X2}）" : "开启主动上传失败（发送异常）";
                        Log.Warning("实时监控：2A 开启主动上传失败，结果 {Result}，NRC 0x{Nrc:X2}", ack.Result, nrc);
                        _uds.Pid = 0;
                        return -4;
                    }

                    _uploadStarted = true;
                }

                // 4) 启动接收循环，持续消费周期帧。
                //    每次（重）连接都清零帧数统计；首帧到达前数据按无效处理（显示 FF）。
                ResetStatistics();
                _lastNotifyMs = -UiNotifyMinIntervalMs;

                IsMonitoring = true;
                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                _rxTask = Task.Run(() => ReceiveLoopAsync(token), token);

                MonitorStatus = $"已开启主动上传（动态 DID 0x{DYNAMIC_DID:X4}，快速率）";
                Log.Information("实时监控：动态 DID 主动上传已开启");
                return 0;
            }
            catch (Exception ex)
            {
                MonitorStatus = $"开启实时监控异常：{ex.Message}";
                Log.Error(ex, "实时监控：开启失败");
                return -5;
            }
        }

        /// <summary>
        /// 开启监控，失败后重试：默认 3 次重试、每次间隔 500ms（共 4 次尝试）。
        /// </summary>
        /// <param name="retryCount">重试次数（不含首次尝试）。</param>
        /// <param name="intervalMs">每次重试前的等待时间（毫秒）。</param>
        /// <returns>0 表示最终成功，其它为最后一次失败码。</returns>
        public async Task<int> StartWithRetryAsync(int retryCount = 3, int intervalMs = 500)
        {
            int result = await StartAsync();
            if (result == 0) return 0;

            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                if (IsMonitoring) return 0;

                Log.Warning($"实时监控：开启失败（返回 {result}），{intervalMs}ms 后进行第 {attempt}/{retryCount} 次重试");
                MonitorStatus = $"主动上传开启失败（返回 {result}），第 {attempt}/{retryCount} 次重试中…";

                await Task.Delay(intervalMs);

                // 连接已断开就没必要继续重试
                if (!_uds.Running) break;

                result = await StartAsync();
                if (result == 0)
                {
                    Log.Information($"实时监控：第 {attempt} 次重试成功");
                    return 0;
                }
            }

            MonitorStatus = $"主动上传开启失败（返回 {result}），已重试 {retryCount} 次";
            Log.Warning($"实时监控：重试结束仍未成功，最后返回 {result}");
            return result;
        }

        /// <summary>
        /// 停止周期帧接收，并下发 2A(STOP) + 2C(清除定义) 关掉下位机主动上传。
        /// </summary>
        public async Task<int> StopAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                return await StopCoreAsync();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>真正的停止流程；调用方必须已持有 _lifecycleLock。</summary>
        private async Task<int> StopCoreAsync()
        {
            // 先停止接收，避免解析线程与关闭命令互相干扰。
            _cts?.Cancel();
            if (_rxTask != null)
            {
                try
                {
                    await _rxTask;
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "实时监控：接收任务退出异常");
                }
                _rxTask = null;
            }
            _cts?.Dispose();
            _cts = null;

            bool wasMonitoring = IsMonitoring;
            IsMonitoring = false;

            // 停止/断连后界面不再显示旧数据：帧率与四项实时值统一置 FF
            InvalidateData("已停止主动上传");

            // 上报已被 2A(STOP) 关掉，下次开启需要重新下发 2A
            _uploadStarted = false;

            int result = 0;
            if (_uds.Running && wasMonitoring)
            {
                result = await ResetPeriodDidAsync();
            }

            // 无论是否成功，都要清掉周期帧过滤标识，恢复普通请求应答逻辑。
            _uds.Pid = 0;

            if (wasMonitoring)
            {
                MonitorStatus = "已停止主动上传";
                Log.Information("实时监控：已停止动态 DID 主动上传");
            }

            return result;
        }

        /// <summary>
        /// 进扩展会话（10 03），尽力而为：失败仅记录，不阻断后续 2C/2A 下发。
        /// </summary>
        private async Task TryExtendedSessionAsync()
        {
            if (!_uds.Running) return;

            try
            {
                Service10 service10 = new Service10
                {
                    Subfunc = Service10.EXTENDED_DIAG_SESSION
                };
                Pdu? response = await _uds.Request(service10.BuildRequest());
                if (response == null || service10.ParseResponse(response) != 0)
                {
                    Log.Warning("实时监控：进入扩展会话未成功，继续尝试下发 2C/2A");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "实时监控：进入扩展会话异常");
            }
        }

        /// <summary>
        /// 复位：2A(STOP) 关闭上报 + 2C(清除定义) 清空动态 DID。
        /// </summary>
        private async Task<int> ResetPeriodDidAsync()
        {
            if (!_uds.Running) return 0;

            int result = 0;
            using CancellationTokenSource timeoutCts = new CancellationTokenSource(StopRequestTimeoutMs);
            CancellationToken token = timeoutCts.Token;

            try
            {
                Service2a service2a = new Service2a
                {
                    Mode = Service2a.STOP,
                    Ids = new byte[] { (byte)(DYNAMIC_DID & 0xFF) }
                };
                var ack = await _uds.Request2aAsync(service2a.BuildRequest(), token);
                if (ack.Result != 0 && ack.Response != null)
                {
                    result = service2a.ParseResponse(ack.Response);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "实时监控：2A(STOP) 下发失败");
            }

            try
            {
                Service2c service2c = new Service2c
                {
                    Id = DYNAMIC_DID,
                    Subfunc = Service2c.CLEAR_DEFINITION,
                    SourceDataIds = new List<SourceDataId> { new SourceDataId { Id = DYNAMIC_DID } }
                };
                Pdu? response = await _uds.Request(service2c.BuildRequest(), token);
                if (response != null)
                {
                    result = Math.Min(result, service2c.ParseResponse(response));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "实时监控：2C(CLEAR) 下发失败");
            }

            return result;
        }

        /// <summary>
        /// 周期帧接收循环：从 Uds 的周期通道取 2A 上报帧并解析。
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            // 复用同一份 2A 报文对象来解析周期帧：首字节为动态 DID 低字节时进入 ParsePayload。
            Service2a service2a = new Service2a
            {
                Ids = new byte[] { (byte)(DYNAMIC_DID & 0xFF) },
                ParsePayload = reader =>
                {
                    // 一帧里可能携带多组源 DID 数据，按定义顺序依次解析。
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        _dynamicDid.ParseResponse(reader);
                    }
                    return 0;
                }
            };

            while (!token.IsCancellationRequested)
            {
                try
                {
                    Pdu? pdu = await _uds.ReceivePeriodicPduAsync(token);
                    if (pdu == null) continue;

                    if (service2a.ParseResponse(pdu) != 0) continue;

                    Interlocked.Increment(ref _frameCount);
                    Interlocked.Increment(ref _pendingFrames);
                    _lastFrameMs = HighResolutionTimer.Milliseconds;
                    NotifyUiIfDue();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "实时监控：解析周期上报帧失败");
                }
            }
        }

        /// <summary>
        /// 每次（重）连接时清零帧数统计与速率，并把实时值置为无效（显示 FF），
        /// 等首帧周期数据到达后自动恢复显示。
        /// </summary>
        public void ResetStatistics()
        {
            Interlocked.Exchange(ref _frameCount, 0);
            Interlocked.Exchange(ref _pendingFrames, 0);
            _frameRate = 0;
            _lastRateMs = HighResolutionTimer.Milliseconds;
            _lastFrameMs = 0;

            InvalidateData("连接重置");

            void applyCount()
            {
                OnPropertyChanged(nameof(FrameCountText));
                OnPropertyChanged(nameof(FrameRateText));
            }

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(applyCount, DispatcherPriority.Background);
            }
            else
            {
                applyCount();
            }
        }

        /// <summary>
        /// 数据新鲜度检查（500ms 一次）：超时没有新帧就把数据置为无效（显示 FF）。
        /// </summary>
        private void CheckDataFreshness()
        {
            if (!_dataValid) return;

            long now = HighResolutionTimer.Milliseconds;
            if (_lastFrameMs <= 0 || now - _lastFrameMs > DataStaleTimeoutMs)
            {
                Log.Debug("实时监控：已 {Silence}ms 未收到新帧，数据置为无效（显示 FF）", now - _lastFrameMs);
                InvalidateData("数据超时");
            }
        }

        /// <summary>
        /// 把实时数据标记为无效：界面四项显示 FF、仪表归零、帧率显示 FF。
        /// </summary>
        private void InvalidateData(string reason)
        {
            void apply()
            {
                _dataValid = false;
                _frameRate = 0;
                RaiseDataProperties();

                // 故障明细表同时清空，避免断连后仍挂着上一次的故障条目
                if (FaultItems.Count > 0)
                {
                    FaultItems.Clear();
                    OnPropertyChanged(nameof(FaultSummaryText));
                }
            }

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(apply, DispatcherPriority.Background);
            }
            else
            {
                apply();
            }

            Log.Debug("实时监控：数据置为无效（{Reason}），界面显示 FF", reason);
        }

        /// <summary>推送与实时数据相关的全部显示属性。</summary>
        private void RaiseDataProperties()
        {
            OnPropertyChanged(nameof(BusCurrentText));
            OnPropertyChanged(nameof(MotorSpeedText));
            OnPropertyChanged(nameof(MotorTemperatureText));
            OnPropertyChanged(nameof(FaultCodeText));
            OnPropertyChanged(nameof(FaultText));
            OnPropertyChanged(nameof(HasFault));
            OnPropertyChanged(nameof(BusCurrentValue));
            OnPropertyChanged(nameof(MotorSpeedValue));
            OnPropertyChanged(nameof(MotorTemperatureValue));
            OnPropertyChanged(nameof(FrameRateText));
        }

        /// <summary>
        /// 限频推送 UI 刷新（下位机上报频率远高于 UI 刷新需求）。
        /// </summary>
        private void NotifyUiIfDue()
        {
            long now = HighResolutionTimer.Milliseconds;
            if (now - _lastNotifyMs < UiNotifyMinIntervalMs) return;
            _lastNotifyMs = now;

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                UpdateValues();
                return;
            }

            dispatcher.InvokeAsync(UpdateValues, DispatcherPriority.Background);
        }

        /// <summary>
        /// 把解析到的信号值搬到界面属性上（必须在 UI 线程执行）。
        /// </summary>
        private void UpdateValues()
        {
            // 已停止/断连时丢弃迟到的刷新，保持 FF 显示；否则说明刚解析到真实帧，数据恢复有效
            if (!IsMonitoring)
            {
                return;
            }
            _dataValid = true;

            long now = HighResolutionTimer.Milliseconds;
            long frames = Interlocked.Exchange(ref _pendingFrames, 0);
            long elapsed = now - _lastRateMs;
            if (elapsed > 0)
            {
                _frameRate = (int)(frames * 1000L / elapsed);
                _lastRateMs = now;
            }

            _busCurrent = ToDouble(BusCurrentSignal);
            _motorSpeed = ToDouble(MotorSpeedSignal);
            _motorTemperature = ToDouble(MotorTemperatureSignal);

            // 量程只增不减：峰值一旦抬高，仪表上限就固定住，不会跟随回落
            bool busRangeChanged = false;
            if (_busCurrent > _busCurrentPeak)
            {
                _busCurrentPeak = _busCurrent;
                busRangeChanged = true;
            }

            bool speedRangeChanged = false;
            if (_motorSpeed > _motorSpeedPeak)
            {
                _motorSpeedPeak = _motorSpeed;
                speedRangeChanged = true;
            }

            uint fault = (uint)Math.Max(0, (long)ToDouble(FaultCodeSignal));
            bool faultChanged = fault != _faultCode;
            _faultCode = fault;

            OnPropertyChanged(nameof(BusCurrentText));
            OnPropertyChanged(nameof(MotorSpeedText));
            OnPropertyChanged(nameof(MotorTemperatureText));
            OnPropertyChanged(nameof(BusCurrentValue));
            OnPropertyChanged(nameof(MotorSpeedValue));
            OnPropertyChanged(nameof(MotorTemperatureValue));
            OnPropertyChanged(nameof(FaultCodeText));
            OnPropertyChanged(nameof(FaultText));
            OnPropertyChanged(nameof(HasFault));
            OnPropertyChanged(nameof(FrameRateText));
            OnPropertyChanged(nameof(FrameCountText));

            if (busRangeChanged) OnPropertyChanged(nameof(BusCurrentMaximum));
            if (speedRangeChanged) OnPropertyChanged(nameof(MotorSpeedMaximum));
            if (faultChanged) RefreshFaultItems();
        }

        /// <summary>
        /// 按当前位掩码刷新故障明细列表（故障可能同时存在多项）。
        /// </summary>
        private void RefreshFaultItems()
        {
            FaultItems.Clear();
            foreach (string name in TestingSoftwareViewModel.GetFaultNames(_faultCode))
            {
                FaultItems.Add(name);
            }

            OnPropertyChanged(nameof(FaultSummaryText));
        }

        /// <summary>
        /// 读取信号的数值；字符串类型或解析失败时返回 0。
        /// </summary>
        private static double ToDouble(SignalViewModel signal)
        {
            try
            {
                object? value = signal.Value;
                if (value == null) return 0;
                return Convert.ToDouble(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static SignalViewModel CreateSignal(UInt16 id, string name, string unit, int type)
        {
            SignalModel model = new SignalModel
            {
                Id = id,
                Name = name,
                Unit = unit,
                Type = type,
                Offset = 1
            };

            return new SignalViewModel(model);
        }

        /// <summary>
        /// 通知属性变更。
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
