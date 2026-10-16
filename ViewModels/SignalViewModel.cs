// <copyright file=SignalViewModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/26 9:48:05</date>
// <summary></summary>
using OxyPlot.Series;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestingSoftware.Models;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Utilities;
using Serilog;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// The view model for motor control library signals
    /// </summary>
    public class SignalViewModel : ViewModel<SignalModel>
    {
        /// <summary>
        /// The signal model for this view mode.
        /// </summary>
        public SignalModel _model;

        /// <summary>
        /// The current value of the signal.
        /// </summary>
        private object? _value;

        /// <summary>
        /// The fixed size queue used in chart.
        /// </summary>
        private FixedSizeQueue<DataPoint> _samples = new FixedSizeQueue<DataPoint>(100);

        public EventHandler? StopDrawingHandler { get; set; }


        /// <summary>
        /// The identifier of the signal.
        /// </summary>
        public UInt16 Id => _model.Id;

        public byte[]? WriteData => _model.WriteData;


        /// <summary>
        /// 
        /// </summary>
        public int Index => _model.Index;

        /// <summary>
        /// The name of the signal
        /// </summary>
        public string? Name => _model.Name;
        public UInt16 Tolerance => _model.Tolerance;


        //static private Int64 refresh_time = 0;
        //private string _Max = string.Empty;

        /// <summary>
        /// The unit of the signal
        /// </summary>
        public string? Unit => _model.Unit;


        public string? ErrorMsg => _model.ErrorMsg;
        /// <summary>
        /// The type name of the signal
        /// </summary>
        public string? Type
        {
            get
            {
                string retval = "N/A";

                switch (_model.Type)
                {
                    case SignalModel.STR:
                        retval = "STRING";
                        break;

                    case SignalModel.U8:
                        retval = _model.Table == null ? "U8" : "ENUM";
                        break;

                    case SignalModel.U16:
                        retval = _model.Table == null ? "U16" : "ENUM";
                        break;

                    case SignalModel.S16:
                        retval = _model.Table == null ? "S16" : "ENUM";
                        break;

                    case SignalModel.U32:
                        retval = _model.Table == null ? "U32" : "ENUM";
                        break;

                    case SignalModel.S32:
                        retval = _model.Table == null ? "S16" : "ENUM";
                        break;

                    case SignalModel.FLOAT:
                        retval = "FLOAT";
                        break;

                    case SignalModel.BCD:
                        retval = "BCD";
                        break;

                    default:
                        break;
                }

                return retval;
            }
        }
 

        /// <summary>
        /// The signal data length.
        /// </summary>
        public byte Length => _model.Length;

        /// <summary>
        /// 
        /// </summary>
        public byte Offset => _model.Offset;


        /// <summary>
        /// The current value.
        /// </summary>
        public object? Value
        {
            get
            {
                object result = string.Empty;
                if (_model.Value != null)
                {
                    switch (_model.Type)
                    {
                        case SignalModel.U8:
                            result = BitConverter.GetBytes(int.Parse(_model.Value))[0];
                            break;

                        case SignalModel.S16:
                            result = Int16.Parse(_model.Value);
                            break;

                        case SignalModel.U16:
                            result = UInt16.Parse(_model.Value);
                            break;

                        case SignalModel.S32:
                            result = Int32.Parse(_model.Value);
                            break;

                        case SignalModel.U32:
                            result = UInt32.Parse(_model.Value);
                            break;
                        case SignalModel.FLOAT:
                            result = float.Parse(_model.Value);
                            break;

                        case SignalModel.BCD:
                        case SignalModel.STR:
                            break;

                    }
                    return result;
                }
                else
                {
                    return _value;
                }
            }
            set
            {
                if (_value != value && value != null)
                {
                    _value = value;
                    _model.Value = _value.ToString();
                    MaxText = value;
                    MinText = value;
                }

            }
        }
        private object _max = double.MaxValue;
        /// <summary>
        /// 
        /// </summary>
        public int? Expectation => _model.Expectation;

        private object _min = double.MinValue;
        // 缓冲区大小可设为 8KB~64KB，根据日志量和内存情况调整
        private int LogbufferSize = 8192;
        private StreamWriter? writer;
        private string signalDataLogFile = string.Empty;
        private int writeCount = 0;

        public object MaxText
        {
            get
            {
                if (_model.Table != null || (_max != null && double.TryParse(_max.ToString(), out double max) && max == double.MaxValue))
                {
                    return string.Empty;
                }
                else
                {
                    return _max ?? string.Empty;
                }
            }
            set
            {
                if (value is null || _model.Type == SignalModel.BCD || _model.Type == SignalModel.STR) return;

                string valuestr = value.ToString()!;
                if (string.IsNullOrEmpty(valuestr)) return;

                // 统一先尝试解析为 double（物理值）
                if (double.TryParse(valuestr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double currentPhys))
                {
                    // 将当前存储的 _max 也转为 double 进行比较
                    if (_max is string maxStr &&
                        double.TryParse(maxStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double storedMaxPhys))
                    {
                        if (currentPhys > storedMaxPhys)
                            _max = Text;   // 直接保存物理值字符串
                    }
                    else
                    {
                        // 首次赋值或之前为空
                        _max = Text;
                    }
                }
                // 如果不是合法数字，则保持原值不变（或记录日志）
            }
        }

        public object MinText
        {
            get
            {

                if (_model.Table != null || (_min != null && double.TryParse(_min.ToString(), out double min) && min == double.MinValue))
                {
                    return string.Empty;
                }
                else
                {
                    return _min ?? string.Empty;
                }
            }
            set
            {
                if (value is null || _model.Type == SignalModel.BCD || _model.Type == SignalModel.STR) return;

                string valuestr = value.ToString()!;
                if (string.IsNullOrEmpty(valuestr)) return;

                if (double.TryParse(valuestr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double currentPhys))
                {
                    if (_min is string minStr &&
                        double.TryParse(minStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double storedMinPhys))
                    {
                        if (currentPhys < storedMinPhys)
                            _min = Text;
                    }
                    else
                    {
                        _min = Text;
                    }
                }
            }
        }

         

        public string DefaultValue
        {
            get
            {
                if (_model.DefaultValue !=null) return _model.DefaultValue;
                return string.Empty;
            }
        }

        //执行状态 0:未执行 1:执行中 2:执行完成 -1:执行失败
        private int _status = 0;
        public int Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(); // 修改Status会UI刷新圆球
            }
        }


        public void ClearMaxMin()
        {
            _max = double.MaxValue;
            _min = double.MinValue;
        }
        /// <summary>
        /// Return all available values for enumeration type.
        /// </summary>
        public ICollection<string>? Values => _model.Table != null ? _model.Table.Values : null;

        /// <summary>
        /// Used by Listview.
        /// </summary>
        public string Text
        {
            get => ToString();
            set
            {
                //Log.Debug($"Set signal {Name} to {value}");
                if (_model.Table != null)
                {
                    foreach (var pair in _model.Table.Items)
                    {
                        if (pair.Value.Equals(value))
                        {
                            switch (_model.Type)
                            {
                                case SignalModel.U8:
                                    Value = (byte)pair.Key;
                                    break;

                                case SignalModel.S16:
                                    Value = (Int16)pair.Key;
                                    break;

                                case SignalModel.U16:
                                    Value = (UInt16)pair.Key;
                                    break;

                                case SignalModel.S32:
                                    Value = (Int32)pair.Key;
                                    break;

                                case SignalModel.U32:
                                    Value = (UInt32)pair.Key;
                                    break;
                                case SignalModel.FLOAT:
                                    Value = (float)pair.Key;
                                    break;

                                case SignalModel.BCD:
                                case SignalModel.STR:
                                    Value = pair.Key;
                                    break;
                                default:
                                    Log.Error("Unknown signal type: " + _model.Type);
                                    break;
                            }
                        }
                    }
                }
                else
                {

                    switch (_model.Type)
                    {
                        case SignalModel.U8:
                            Value = (byte)Math.Truncate(double.Parse(value, CultureInfo.InvariantCulture));
                            break;

                        case SignalModel.S16:
                            Value = Int16.Parse(value);
                            break;

                        case SignalModel.U16:
                            Value = UInt16.Parse(value);
                            break;

                        case SignalModel.S32:
                            Value = Int32.Parse(value);
                            break;

                        case SignalModel.U32:
                            Value = UInt32.Parse(value);
                            break;
                        case SignalModel.FLOAT:
                            Value = float.Parse(value);
                            break;

                        case SignalModel.BCD:
                            Value = BcdStringToBytes(value);
                            break;
                        case SignalModel.STR:
                            Value = Encoding.ASCII.GetBytes(value);

                            break;
                        default:
                            Log.Error("Unknown signal type: " + _model.Type);
                            break;
                    }
                }

                OnPropertyChanged(nameof(Text));
            }
        }
        public static byte[] BcdStringToBytes(string bcd)
        {
            // 长度为奇数时，前面补 0
            if (bcd.Length % 2 != 0)
                bcd = "0" + bcd;

            int len = bcd.Length / 2;
            byte[] result = new byte[len];

            for (int i = 0; i < len; i++)
            {
                int high = bcd[i * 2] - '0';
                int low = bcd[i * 2 + 1] - '0';
                result[i] = (byte)((high << 4) | low);
            }

            return result;
        }

        /// <summary>
        /// The default Line series.
        /// </summary>
        public LineSeries LineSeries { get; private set; }

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <param name="model"></param>
        public SignalViewModel(SignalModel model)
        {
            _model = model;

            // Set the default value.
            if (model.Table != null && model.Table.Items.Count > 0)
            {
                Text = model.Table.Items[0].Value;
            }
        }
        /// <summary>
        /// Initialize the log writer.
        /// </summary>
        /// <param name="filePath"></param>
        public void InitializeLogWriter(string filePath)
        {

            var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            // 缓冲区大小可设为 8KB~64KB，根据日志量和内存情况调整
            writer = new StreamWriter(fs, System.Text.Encoding.UTF8, LogbufferSize);
            writer.AutoFlush = false;

        }
        public void DisposeLogWriter()
        {
            writer?.Flush();
            writer?.Close();
            writer?.Dispose();
            writer = null;
            _ = Task.Run(() =>
            {
                var startTime = HighResolutionTimer.Milliseconds;
                do
                {
                    // 检查文件是否存在
                    if (!File.Exists(signalDataLogFile))
                    {
                        break;
                    }

                    try
                    {
                        File.Delete(signalDataLogFile);

                    }
                    catch (UnauthorizedAccessException)
                    {

                        continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (Exception)
                    {
                        break;
                    }
                } while (HighResolutionTimer.Milliseconds - startTime < 5_000);
            });
        }
        /// <summary>
        /// Write the log message to the log file.
        /// </summary>
        /// <param name="message"></param>
        public void WriteLog(string message)
        {

            writer?.WriteLine(message);// 无需手动 Flush，因为 AutoFlush = true 已自动处理

            // 可选：每 N 条记录 Flush 一次（推荐方式）
            if (++writeCount % 5000 == 0)  // 每 100 条刷一次（可调）
            {
                writer?.Flush();
            }
        }
        /// <summary>
        /// Build the signal into the diagnostic request output stream.
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        public int Build(BinaryWriter writer)
        {
            switch (_model.Type)
            {
                case SignalModel.U8:
                    byte u8 = (Value as byte?) ?? 0;
                    writer.Write(u8);
                    break;

                case SignalModel.S16:
                    Int16 s16 = _model.Affine != null ? (Int16)_model.Affine.Inverse(Convert.ToSingle(Value)) : Convert.ToInt16(Value);
                    writer.Write(DiagService.SwapS16(s16));
                    break;

                case SignalModel.U16:
                    UInt16 u16 = _model.Affine != null ? (UInt16)_model.Affine.Inverse(Convert.ToSingle(Value)) : Convert.ToUInt16(Value);
                    writer.Write(DiagService.Swap16(u16));
                    break;

                case SignalModel.S32:
                    Int32 s32 = (Value as Int32?) ?? 0;
                    writer.Write(DiagService.SwapS32(s32));
                    break;

                case SignalModel.U32:
                    UInt32 u32 = (Value as UInt32?) ?? 0;
                    writer.Write(DiagService.Swap32(u32));
                    break;

                case SignalModel.FLOAT:
                    float f32 = (Value as float?) ?? 0.0f;
                    writer.Write(f32);
                    break;

                case SignalModel.STR:
                    string str = (Value as string) ?? "";
                    if (str.Length == 0 && Value != null)
                    { 
                        str = (_value as string) ?? "";
                        if (str.Length == 0 && Value != null)
                            writer.Write((byte[])Value);
                        else
                            writer.Write(Encoding.ASCII.GetBytes(str));
                    }
                    else
                        writer.Write(Encoding.ASCII.GetBytes(str));
                    break;

                case SignalModel.BCD:
                    string bcd = (Value as string) ?? "";
                    writer.Write(StringToBcdBytes(bcd));
                    break;

                default:
                    break;
            }


            return 0;
        }

        /// <summary>
        /// Parse the signal from the diagnostic response input stream.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public int Parse(BinaryReader reader)
        {
            float f32 = 0.0f;
            object? _value = null;
            switch (_model.Type)
            {
                case SignalModel.U8:
                    byte u8 = reader.ReadByte();
                    _value = u8;
                    break;

                case SignalModel.S16:
                    Int16 s16 = DiagService.SwapS16(reader.ReadInt16());
                    _value = f32 = _model.Affine != null ? (Int16)_model.Affine.Foward(s16) : s16;
                    break;

                case SignalModel.U16:
                    UInt16 u16 = DiagService.Swap16(reader.ReadUInt16());
                    _value = f32 = _model.Affine != null ? (UInt16)_model.Affine.Foward(u16) : u16;

                    break;

                case SignalModel.S32:
                    Int32 s32 = DiagService.SwapS32(reader.ReadInt32());
                    _value = s32;
                    break;

                case SignalModel.U32:
                    UInt32 u32 = DiagService.Swap32(reader.ReadUInt32());
                    _value = u32;
                    break;

                case SignalModel.FLOAT:
                    f32 = reader.ReadSingle();
                    _value = f32;
                    break;

                case SignalModel.STR:
                    byte[] bytes = reader.ReadBytes(_model.Length);
                    _value = Encoding.ASCII.GetString(bytes);
                    break;

                case SignalModel.BCD:
                    byte[] bcdbytes = reader.ReadBytes(_model.Length);
                    _value = BytesToBcdString(bcdbytes);
                    break;

                default:
                    break;
            }

            if(_model.Id == 0x903)
            {
                Value = (int)f32;
            } else
            Value = _value;

            return 0;
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


        /// <summary>
        /// Parse the signal from the diagnostic response input stream.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="DefaultValue">预期默认值字符串，用于校验；数值相差≤0.5判定成功</param>
        /// <returns>0:解析&校验成功，‑1:校验失败，‑2:DefaultValue格式错误</returns>
        public int Parse(BinaryReader reader, string? DefaultValue = null)
        {
            float f32 = 0.0f;
            object? _value = null;
            switch (_model.Type)
            {
                case SignalModel.U8:
                    byte u8 = reader.ReadByte();
                    _value = u8;
                    break;
                case SignalModel.S16:
                    Int16 s16 = DiagService.SwapS16(reader.ReadInt16());
                    _value = f32 = _model.Affine != null ? (float)_model.Affine.Foward(s16) : s16;
                    break;
                case SignalModel.U16:
                    UInt16 u16 = DiagService.Swap16(reader.ReadUInt16());
                    _value = f32 = _model.Affine != null ? (float)_model.Affine.Foward(u16) : u16;
                    break;
                case SignalModel.S32:
                    Int32 s32 = DiagService.SwapS32(reader.ReadInt32());
                    _value = s32;
                    break;
                case SignalModel.U32:
                    UInt32 u32 = DiagService.Swap32(reader.ReadUInt32());
                    _value = u32;
                    break;
                case SignalModel.FLOAT:
                    f32 = reader.ReadSingle();
                    _value = f32;
                    break;
                case SignalModel.STR:
                    byte[] bytes = reader.ReadBytes(_model.Length);
                    _value = Encoding.ASCII.GetString(bytes);
                    break;
                case SignalModel.BCD:
                    byte[] bcdbytes = reader.ReadBytes(_model.Length);
                    _value = BytesToBcdString(bcdbytes);
                    break;
                default:
                    break;
            }

            Value = _value;

            // ----------------新增校验逻辑----------------
            // DefaultValue为空：不校验，直接返回成功
            if (string.IsNullOrWhiteSpace(DefaultValue))
            {
                return 0;
            }

            // 字符串/BCD类型不做数值校验，直接返回成功
            if (_model.Type is SignalModel.STR or SignalModel.BCD)
            {
                return 0;
            }

            // 解析预期默认值
            if (!double.TryParse(DefaultValue, out double expectVal))
            {
                return -2; // DefaultValue不是合法数字
            }

            // 当前解析出来的值转double
            double realVal = Convert.ToDouble(Value);

            // 差值绝对值 <=0.5：校验成功，否则校验失败
            if (Math.Abs(realVal - expectVal) <= 0.5)
            {
                return 0;
            }
            else
            {
                return -1; // 偏差超过0.5，校验失败
            }
        }


        /// <summary>
        /// 
        /// </summary>
        public void UpdatePlot()
        {
            LineSeries.Points.Clear();
            LineSeries.Points.AddRange(_samples);
        }
        /// <summary>
        /// 将字节数组转换为BCD码字符串。
        /// 
        /// </summary>
        /// <param name="bcdBytes"> BCD码字节数组，例如[0x12, 0x34]。</param>
        /// <returns> 对应的BCD码字符串，例如“1234”。</returns>
        private string BytesToBcdString(byte[]? bcdBytes)
        {
            if (bcdBytes == null || bcdBytes.Length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder(bcdBytes.Length * 2);
            foreach (byte b in bcdBytes)
            {
                // 提取高半字节（前4位）：右移4位
                int highNibble = (b >> 4) & 0x0F;  // 范围：0-15，但BCD中应为0-9
                                                   // 提取低半字节（后4位）：与0x0F按位与
                int lowNibble = b & 0x0F;          // 范围：0-15，但BCD中应为0-9

                // 验证BCD有效性（可选，非BCD值如10-15可抛异常或忽略）
                if (highNibble > 9 || lowNibble > 9)
                {
                    return "Invalid BCD value";
                }

                sb.Append(highNibble);
                sb.Append(lowNibble);
            }
            return sb.ToString();
        }
        /// <summary>
        /// 将BCD码字符串转换为字节数组。
        /// </summary>
        /// <param name="bcdString">BCD码字符串，例如“1234”。</param>
        /// <returns>对应的字节数组，例如[0x12, 0x34]。</returns>
        private byte[] StringToBcdBytes(string bcdString)
        {
            byte[] result = new byte[0];
            if (string.IsNullOrEmpty(bcdString) || !bcdString.All(char.IsDigit))
                return result;


            // 如果长度为奇数，在末尾填充“0”
            if (bcdString.Length % 2 != 0)
                bcdString += "0";

            result = new byte[bcdString.Length / 2];
            for (int i = 0; i < bcdString.Length; i += 2)
            {
                // 提取高半字节（第一个数字）
                int highNibble = int.Parse(bcdString[i].ToString());
                // 提取低半字节（第二个数字）
                int lowNibble = int.Parse(bcdString[i + 1].ToString());

                // 组合成字节：高半字节左移4位 + 低半字节
                result[i / 2] = (byte)((highNibble << 4) | lowNibble);
            }

            return result;
        }

        /// <summary>
        /// Make enumeration can be string format.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            // Set the default value.
            string retval = Value?.ToString() ?? "N/A";

            // The signal is a enumeration type if the map is not null.
            if (_model.Table != null)
            {
                try
                {
                    // Try to convert the value to a int.
                    int key = Convert.ToInt32(Value);

                    // Get the human readable value.
                    retval = _model.Table.Foward(key);
                    if (retval == "N/A")
                    {
                        retval = $"0x{key:X}";
                    }
                    if (retval == "None")
                    {
                        retval = "M/A";
                    }
                    return retval;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to transform he int value to human readable value");
                }
            }
            else
            {
                switch (_model.Type)
                {
                    case SignalModel.U8:
                    case SignalModel.S16:
                    case SignalModel.U16:
                    case SignalModel.S32:
                    case SignalModel.U32:
                    case SignalModel.FLOAT:
                        break;
                    case SignalModel.STR:
                        if (Value != null && Value is byte[])
                        {
                            retval = Encoding.ASCII.GetString((byte[])Value);
                        }
                        else if (Value != null && Value is string)
                        {
                            retval = (string)Value;
                        }
                        else
                        {
                            retval = BytesToBcdString((byte[]?)Value);
                        }
                        return retval;
                    case SignalModel.BCD:
                        if (Value != null && Value is byte[])
                        {
                            retval = BytesToBcdString((byte[])Value);
                        }
                        else if (Value != null && Value is string)
                        {
                            retval = Value.ToString() ?? string.Empty;
                        }
                        else
                        {
                            retval = BytesToBcdString((byte[]?)Value);
                        }
                        return retval;

                }
            }
            // （U8/S16/U32/FLOAT 等）尝试解析为数值并格式化
            if (double.TryParse(retval, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double numValue))
            {
                // 成功解析为数值 → 保留整数 + 小数点后最多3位（四舍五入）
                retval = numValue.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            }
            return retval;
        }
    }
}
