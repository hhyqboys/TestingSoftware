using System;

namespace TestingSoftware.Protocols.Bluetoolth
{
    /// <summary>
    /// PDU 接收缓冲类 - 线性缓冲 + 前移方式（线程安全版本）
    /// PDU 格式: 55 AA [长度高] [长度低] [payload...] [checksum高] [checksum低]
    /// </summary>
    public class PduReceiveBuffer
    {
        private readonly byte[] _data = new byte[32 * 1024];
        private int _length = 0;
        // 在 while 循环里加一个计数器
        private int errorCount = 0;
        private const int MAX_ERROR_BEFORE_DISCARD = 100; // 或 500
        // 用于保护 _data 和 _length 的锁对象
        private readonly object _lock = new object();

        /// <summary>
        /// 向缓冲区尾部追加数据（线程安全）
        /// </summary>
        /// <param name="data">要追加的字节数组</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException">缓冲区已满</exception>
        public void AddData(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            lock (_lock)
            {
                int need = data.Length;
                int remain = _data.Length - _length;

                if (need > remain)
                {
                    throw new InvalidOperationException(
                        $"缓冲区空间不足，需要 {need} 字节，剩余 {remain} 字节");
                }

                Array.Copy(data, 0, _data, _length, need);
                _length += need;
            }
        }

        /// <summary>
        /// 尝试解析一个完整的 PDU（线程安全）
        /// 成功返回 payload（不含帧头、长度、checksum），并自动移除已解析部分
        /// 失败返回 null，缓冲区保持不变
        /// </summary>
        /// <returns>解析到的 payload，或 null</returns>
        public byte[]? ParsePdu()
        {
            lock (_lock)
            {
                if (_length < 6) return null;

                int pos = 0;

                while (pos + 6 <= _length)  // 至少要有 6 字节才可能是一个最小帧
                {
                    if (_data[pos] == 0x55 && _data[pos + 1] == 0xAA)
                    {
                        // 读取长度（大端序）
                        int payloadLen = (_data[pos + 2] << 8) | _data[pos + 3];
                        if( payloadLen > 247)
                        {
                            pos += 1;
                            Array.Copy(_data, pos, _data, 0, _length - pos);
                            return null;
                        }
                        // 整包长度
                        int totalLen = 2 + 2 + payloadLen + 2;

                        // 数据是否足够一整包？
                        if (pos + totalLen > _length)
                        {
                            return null; // 等待更多数据
                        }

                        // 计算预期校验和（从长度字段开始，到 payload 结束）
                        ushort calculated = CalcChecksum(_data, pos + 2, payloadLen + 2);

                        // 接收到的校验和
                        ushort received = (ushort)(
                            (_data[pos + 4 + payloadLen] << 8) |
                            _data[pos + 5 + payloadLen]
                        );

                        if (received == calculated)
                        {
                            errorCount = 0;
                            // 校验通过，提取 payload
                            byte[] payload = payloadLen > 0
                                ? new byte[payloadLen]
                                : Array.Empty<byte>();

                            if (payloadLen > 0)
                            {
                                Array.Copy(_data, pos + 4, payload, 0, payloadLen);
                            }

                            // 前移剩余数据
                            int remaining = _length - (pos + totalLen);
                            if (remaining > 0)
                            {
                                Array.Copy(_data, pos + totalLen, _data, 0, remaining);
                            }
                            _length = remaining;

                            return payload;
                        }
                        else
                        {
                            pos += 1;
                           
                            errorCount++;
                            if (errorCount > MAX_ERROR_BEFORE_DISCARD)
                            {
                                // 连续太多校验失败，认为是严重噪声，直接丢弃前面所有数据
                                _length = 0;
                                errorCount = 0;
                                return null;
                            }
                            continue;
                        }

                       
                    }

                    pos++;
                }
                _length = 0;
                // 没找到完整有效帧
                return null;
            }
        }

        /// <summary>
        /// 当前缓冲区已使用字节数（线程安全）
        /// </summary>
        public int Length
        {
            get
            {
                lock (_lock)
                {
                    return _length;
                }
            }
        }

        /// <summary>
        /// 清空缓冲区（线程安全）
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _length = 0;
                // 可选：Array.Clear(_data, 0, _data.Length); 但通常不需要
            }
        }

        /// <summary>
        /// 计算校验和（与原方法一致，无需加锁，因为是纯计算）
        /// </summary>
        public static ushort CalcChecksum(byte[] data, int offset, int length)
        {
            byte aux = 0;
            int end = Math.Min(offset + length, data.Length);
            for (int i = offset; i < end; i++)
            {
                aux = (byte)(aux + data[i]);
            }
            return (ushort)((aux << 8) | (byte)(~aux));
        }
    }
}