using Modbus.Data;
using Modbus.Device;
using Modbus.Message;
using Serilog;
using System;
using System.IO.Ports;

namespace TestingSoftware.Services
{
    public class ModBusService
    {
        private SerialPort _serialPort;
        private ModbusSerialMaster _modbusMaster;

        /// <summary>Start 时记录的串口名，用于串口被意外关闭后自动重开。</summary>
        private string? _portName;

        public bool IsRunning;

        public bool IsConnected
        {
            get
            {
                return _serialPort != null && _serialPort.IsOpen;
            }
        }
        public ModBusService()
        {
        }
        public void Start(string portName)
        {
            if(IsRunning)
            {
                Stop();
            }
            _portName = portName;
            _serialPort = new SerialPort(portName, 9600);
            _serialPort.Open();
            _modbusMaster = ModbusSerialMaster.CreateRtu(_serialPort);
            _modbusMaster.Transport.WriteTimeout = 100;
            _modbusMaster.Transport.ReadTimeout = 100;

            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            try
            {
                _modbusMaster?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "IO 串口 Stop：释放 ModBus 主站异常");
            }
            try
            {
                _serialPort?.Close();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "IO 串口 Stop：关闭串口异常");
            }
        }

        /// <summary>
        /// 串口被外部/异常关闭后重开一次（用户要求：通讯失败若因串口关闭，先执行一次启动再重试）。
        /// </summary>
        /// <returns>重开成功且串口已打开返回 true。</returns>
        public bool EnsureOpen()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_portName))
            {
                Log.Warning("IO 串口已关闭，且没有记录过串口名，无法自动重开");
                return false;
            }

            try
            {
                Log.Warning("IO 串口 {Port} 已被关闭，重新打开后重试", _portName);
                Stop();
                Start(_portName);
                // USB 转串口刚打开时驱动需要短暂稳定，给 200ms 再重试，避免第一次写就超时
                System.Threading.Thread.Sleep(200);
                bool ok = IsRunning && _serialPort != null && _serialPort.IsOpen;
                if (ok) Log.Information("IO 串口 {Port} 重新打开成功", _portName);
                else Log.Warning("IO 串口 {Port} 重新打开后仍未处于打开状态", _portName);
                return ok;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "IO 串口 {Port} 重新打开失败", _portName);
                return false;
            }
        }

        /// <summary>异常是否属于“串口已关闭”这一类（可重开重试）。</summary>
        private bool IsPortClosed(Exception ex)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return true;
            if (ex is InvalidOperationException) return true; // SerialPort 未打开时读写抛的就是它
            return ex.Message.Contains("port is closed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("port is already open", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("端口已关闭");
        }

        /// <summary>执行一次 ModBus 操作；串口被关闭时重开端口再重试一次。</summary>
        private void Execute(Action action, string failPrefix)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (IsPortClosed(ex) && EnsureOpen())
            {
                // 已经重开成功，重试一次
                try
                {
                    action();
                }
                catch (Exception retryEx)
                {
                    throw new Exception(failPrefix + retryEx.Message);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(failPrefix + ex.Message);
            }
        }

        /// <summary>执行一次带返回值的 ModBus 操作；串口被关闭时重开端口再重试一次。</summary>
        private T Execute<T>(Func<T> action, string failPrefix)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsPortClosed(ex) && EnsureOpen())
            {
                try
                {
                    return action();
                }
                catch (Exception retryEx)
                {
                    throw new Exception(failPrefix + retryEx.Message);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(failPrefix + ex.Message);
            }
        }

        #region 读取输出线圈
        /// <summary>
        /// 读取输出多个线圈
        /// </summary>
        /// <param name="start">起始地址</param>
        /// <param name="length">读取长度</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool[] ReadOutputCoils(ushort start, ushort length, byte slaveAddress = 1)
        {
            return Execute(() => this._modbusMaster.ReadCoils(slaveAddress, start, length), "【读取输出线圈】失败：");
        }

        /// <summary>
        /// 读取输出单个线圈
        /// </summary>
        /// <param name="start">起始地址</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool ReadOutputCoil(ushort start, byte slaveAddress = 1)
        {
            return Execute(() => this._modbusMaster.ReadCoils(slaveAddress, start, 1)[0], "【读取输出线圈】失败：");
        }
        /// <summary>
        /// 写入输出多个线圈
        /// </summary>
        /// <param name="start">起始地址</param>
        /// <param name="values">值数组</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <exception cref="Exception"></exception>
        public void WriteOutputCoils(ushort start, bool[] values, byte slaveAddress = 1)
        {
            Execute(() => this._modbusMaster.WriteMultipleCoils(slaveAddress, start, values), "【写入输出线圈】失败：");
        }

        /// <summary>
        /// 写入单个线圈
        /// </summary>
        /// <param name="start">起始地址</param>
        /// <param name="value">值</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <exception cref="Exception"></exception>
        public void WriteOutputCoil(ushort start, bool value, byte slaveAddress = 1)
        {
            Execute(() => this._modbusMaster.WriteSingleCoil(slaveAddress, start, value), "【写入输出线圈】失败：");
        }
        #endregion

        #region 读写离散值
        /// <summary>
        /// 读取离散输入
        /// </summary>
        /// <param name="start">起始地址</param>
        /// <param name="length">读取长度</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool[] ReadDiscreteInputs(ushort start, ushort length, byte slaveAddress = 1)
        {
            return Execute(() => this._modbusMaster.ReadInputs(slaveAddress, start, length), "【读取离散输入】失败：");
        }
        #endregion

        #region AI 模拟输入寄存器 (0x04 输入寄存器，只读)
        /// <summary>
        /// 批量读取AI输入寄存器（04功能码）
        /// </summary>
        /// <param name="start">寄存器起始地址（协议0基地址，如0x0000）</param>
        /// <param name="length">寄存器数量</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <returns>寄存器ushort数组</returns>
        /// <exception cref="Exception"></exception>
        public ushort[] ReadAiRegisters(ushort start, ushort length, byte slaveAddress = 1)
        {
            return Execute(() => _modbusMaster.ReadInputRegisters(slaveAddress, start, length), "【读取AI输入寄存器】失败：");
        }


        #endregion


        #region AO 模拟输出寄存器 (0x03读 /0x06单写 /0x10批量写，保持寄存器)
        /// <summary>
        /// 批量读取AO保持寄存器（03功能码）
        /// </summary>
        /// <param name="start">寄存器起始地址（协议0基地址，如0x0000）</param>
        /// <param name="length">寄存器数量</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <returns>寄存器ushort数组</returns>
        /// <exception cref="Exception"></exception>
        public ushort[] ReadAoRegisters(ushort start, ushort length, byte slaveAddress = 1)
        {
            return Execute(() => _modbusMaster.ReadHoldingRegisters(slaveAddress, start, length), "【读取AO保持寄存器】失败：");
        }



        /// <summary>
        /// 写入单个AO寄存器（06功能码）
        /// </summary>
        /// <param name="regAddr">寄存器地址（0基）</param>
        /// <param name="value">写入值</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <exception cref="Exception"></exception>
        public void WriteAoSingleRegister(ushort regAddr, ushort value, byte slaveAddress = 1)
        {
            Execute(() => _modbusMaster.WriteSingleRegister(slaveAddress, regAddr, value), "【写入单个AO寄存器】失败：");
        }

        /// <summary>
        /// 批量写入AO寄存器（0x10功能码）
        /// </summary>
        /// <param name="start">起始寄存器地址（0基）</param>
        /// <param name="values">待写入数组</param>
        /// <param name="slaveAddress">从机地址</param>
        /// <exception cref="Exception"></exception>
        public void WriteAoMultiRegisters(ushort start, ushort[] values, byte slaveAddress = 1)
        {
            Execute(() => _modbusMaster.WriteMultipleRegisters(slaveAddress, start, values), "【批量写入AO寄存器】失败：");
        }


        #endregion
    }
}
