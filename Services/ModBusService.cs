using Modbus.Data;
using Modbus.Device;
using Modbus.Message;
using System;
using System.IO.Ports;

namespace TestingSoftware.Services
{
    public class ModBusService
    {
        private SerialPort _serialPort;
        private ModbusSerialMaster _modbusMaster;
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
            _modbusMaster?.Dispose();
            _serialPort?.Close();
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
            try
            {
                return this._modbusMaster.ReadCoils(slaveAddress, start, length);
            }
            catch (Exception ex)
            {
                throw new Exception("【读取输出线圈】失败：" + ex.Message);
            }
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
            try
            {
                return this._modbusMaster.ReadCoils(slaveAddress, start, 1)[0];
            }
            catch (Exception ex)
            {
                throw new Exception("【读取输出线圈】失败：" + ex.Message);
            }
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
            try
            {
                this._modbusMaster.WriteMultipleCoils(slaveAddress, start, values);
            }
            catch (Exception ex)
            {
                throw new Exception("【写入输出线圈】失败：" + ex.Message);
            }
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
            try
            {
                this._modbusMaster.WriteSingleCoil(slaveAddress, start, value);
            }
            catch (Exception ex)
            {
                throw new Exception("【写入输出线圈】失败：" + ex.Message);
            }
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
            try
            {
                return this._modbusMaster.ReadInputs(slaveAddress, start, length);
            }
            catch (Exception ex)
            {
                throw new Exception("【读取离散输入】失败：" + ex.Message);
            }
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
            try
            {
                return _modbusMaster.ReadInputRegisters(slaveAddress, start, length);
            }
            catch (Exception ex)
            {
                throw new Exception("【读取AI输入寄存器】失败：" + ex.Message);
            }
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
            try
            {
                return _modbusMaster.ReadHoldingRegisters(slaveAddress, start, length);
                
            }
            catch (Exception ex)
            {
                throw new Exception("【读取AO保持寄存器】失败：" + ex.Message);
            }
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
            try
            {
                _modbusMaster.WriteSingleRegister(slaveAddress, regAddr, value);
            }
            catch (Exception ex)
            {
                throw new Exception("【写入单个AO寄存器】失败：" + ex.Message);
            }
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
            try
            {
                _modbusMaster.WriteMultipleRegisters(slaveAddress, start, values);
            }
            catch (Exception ex)
            {
                throw new Exception("【批量写入AO寄存器】失败：" + ex.Message);
            }
        }


        #endregion
    }
}
