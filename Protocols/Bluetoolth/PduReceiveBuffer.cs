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
        // 单帧 payload 长度上限（含 2C 定义帧），超出视为错位噪声
        private const int MAX_PAYLOAD = 512;
        // 无线索时保留的尾部字节数，避免帧头被跨 BLE 包切断
        private const int KEEP_TAIL_BYTES = 5;
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
        /// 判断是否为帧头（兼容正序 55 AA 与反序 AA 55 / 错位帧头）。
        /// </summary>
        private static bool IsHead(byte a, byte b)
        {
            return (a == 0x55 && b == 0xAA) || (a == 0xAA && b == 0x55);
        }

        /// <summary>
        /// 尝试解析一个完整的 PDU（线程安全）
        /// 成功返回 payload（不含帧头、长度、checksum），并自动移除已解析部分
        /// 失败返回 null，缓冲区保守保留未消费尾部（不清空整缓冲，避免丢失尚未到齐的 ACK 帧）
        /// </summary>
        /// <returns>解析到的 payload，或 null</returns>
        public byte[]? ParsePdu()
        {
            lock (_lock)
            {
                if (_length < 6) return null;

                // 1) 定位第一个帧头
                int head = -1;
                for (int i = 0; i + 1 < _length; i++)
                {
                    if (IsHead(_data[i], _data[i + 1])) { head = i; break; }
                }

                // 2) 缓冲里没有任何帧头：只保留尾部若干字节，防止帧头被跨包切断；其余按噪声丢弃
                if (head < 0)
                {
                    int keep = Math.Min(KEEP_TAIL_BYTES, _length);
                    if (keep > 0) Array.Copy(_data, _length - keep, _data, 0, keep);
                    _length = keep;
                    return null;
                }

                // 3) 帧头之前的噪声前移丢弃
                if (head > 0)
                {
                    Array.Copy(_data, head, _data, 0, _length - head);
                    _length -= head;
                }

                // 4) 读取长度（大端序）
                int payloadLen = (_data[2] << 8) | _data[3];

                // 长度不合理：跳过这个伪帧头，等待下一批数据继续解析
                if (payloadLen > MAX_PAYLOAD)
                {
                    DropFirstByte();
                    return null;
                }

                // 整包长度 = 帧头 2 + 长度 2 + payload + 校验 2
                int totalLen = 2 + 2 + payloadLen + 2;

                // 数据尚未到齐（跨 BLE 包）：保留当前尾部，等待更多数据，不清空
                if (totalLen > _length)
                {
                    return null;
                }

                // 计算预期校验和（从长度字段开始，到 payload 结束）
                ushort calculated = CalcChecksum(_data, 2, payloadLen + 2);

                // 接收到的校验和
                ushort received = (ushort)(
                    (_data[4 + payloadLen] << 8) |
                    _data[5 + payloadLen]
                );

                if (received != calculated)
                {
                    // 校验失败：帧头是错位伪帧头，跳过 1 字节继续
                    DropFirstByte();
                    return null;
                }

                errorCount = 0;

                // 提取 payload
                byte[] payload = payloadLen > 0
                    ? new byte[payloadLen]
                    : Array.Empty<byte>();

                if (payloadLen > 0)
                {
                    Array.Copy(_data, 4, payload, 0, payloadLen);
                }

                // 前移剩余数据
                int remaining = _length - totalLen;
                if (remaining > 0)
                {
                    Array.Copy(_data, totalLen, _data, 0, remaining);
                }
                _length = remaining;

                return payload;
            }
        }

        /// <summary>
        /// 丢弃缓冲第一个字节并记录错位计数；连续错位过多时清空整缓冲。
        /// </summary>
        private void DropFirstByte()
        {
            if (_length <= 1) { _length = 0; return; }
            Array.Copy(_data, 1, _data, 0, _length - 1);
            _length -= 1;

            errorCount++;
            if (errorCount > MAX_ERROR_BEFORE_DISCARD)
            {
                // 连续太多校验失败，认为是严重噪声，直接丢弃前面所有数据
                _length = 0;
                errorCount = 0;
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
            }
        }

        /// <summary>
        /// 计算给定数据的校验和（与原方法一致，无需加锁，因为是纯计算）。
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
