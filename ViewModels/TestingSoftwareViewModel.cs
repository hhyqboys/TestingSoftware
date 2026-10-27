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
using System.Xml.Serialization;
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
