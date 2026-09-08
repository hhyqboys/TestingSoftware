// <copyright file=UdsSecurityAccess company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:56:26</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Utilities
{
    /// <summary>
    /// Provides key generation for UDS Security Access (SID 0x27) using a custom ECU-specific algorithm.
    /// This class implements the exact logic used by the ECU to transform a 4-byte seed into a 4-byte key.
    /// </summary>
    public static class UdsSecurityAccess
    {
        // This value must match the LOGICAL_ADDRESS defined in the ECU firmware.
        private const uint LOGICAL_ADDRESS = 0x20001001;

        private static readonly uint[] LOGICAL_ADDRESSES =
        {
            0x21205230, // Index 0: 27 
            0x21361371, // Index 1: bootloader
            0x72150483, // Index 2: license
        };

        /// <summary>
        /// Generates a 4-byte security key from a 4-byte seed using the ECU's custom algorithm.
        /// The algorithm is deterministic and must be identical to the one implemented in the ECU.
        /// </summary>
        /// <param name="seed">Input seed (exactly 4 bytes). Byte order as received from ECU (typically big-endian over CAN).</param>
        /// <param name="keyIndex">Index into the logical address table.</param>
        /// <returns>Generated 4-byte key in big-endian format, ready to be sent in the 0x27 02 request.</returns>
        /// <exception cref="ArgumentException">Thrown if seed is null or not exactly 4 bytes long.</exception>
        public static byte[] GenerateKey(byte[] seed, int keyIndex)
        {

            if (seed == null || seed.Length != 4)
            {
                throw new ArgumentException("Seed must be a non-null array of exactly 4 bytes.");
            }

            if (keyIndex < 0 || keyIndex >= LOGICAL_ADDRESSES.Length)
            {
                throw new ArgumentException($"Key index {keyIndex} is out of range. Valid range: 0 to {LOGICAL_ADDRESSES.Length - 1}.");
            }

            uint logicalAddress = LOGICAL_ADDRESSES[keyIndex];

            // Interpret the seed as a big-endian 32-bit unsigned integer.
            // seed[0] is the most significant byte (MSB).
            uint dwKey = (uint)(seed[0] << 24 | seed[1] << 16 | seed[2] << 8 | seed[3]);

            // Compute dynamic iteration threshold based on LSB of seed and logical address.
            uint dwThreshold = dwKey & 0xFF;

            // Extract and rotate bits from the logical address.
            byte dwExtractedBits = (byte)((logicalAddress >> 4) & 0xFF);
            dwExtractedBits = (byte)(((dwExtractedBits << 1) | (dwExtractedBits >> 7)) & 0xFF);

            // Finalize threshold with XOR and constant masking.
            dwThreshold = ((dwThreshold ^ dwExtractedBits) & 0xE5) + 0x5A;

            // Perform bitwise scrambling for 'dwThreshold' iterations.
            for (uint i = 0; i < dwThreshold; i++)
            {
                // Extract specific bits from dwKey.
                uint step1 = (dwKey & 0x20000000U) >> 29; // Bit 29
                uint step2 = (dwKey & 0x01000000U) >> 24; // Bit 24
                uint step3 = (dwKey & 0x00002000U) >> 13; // Bit 13
                uint step4 = (dwKey & 0x00000008U) >> 3;  // Bit 3

                // XOR the extracted bits to produce a single feedback bit.
                uint step5 = ((step1 ^ step2) ^ step3) ^ step4;
                uint step6 = step5 & 1; // Keep only LSB

                // Shift left and inject feedback bit at LSB position.
                dwKey <<= 1;
                uint step7 = dwKey & 0xFFFFFFFEU; // Clear LSB
                dwKey = step6 | step7;            // Set new LSB
            }

            // Apply final mask using the ECU's logical address.
            dwKey ^= logicalAddress;

            // Convert result back to big-endian byte array (MSB first).
            return new byte[]
            {
                (byte)((dwKey >> 24) & 0xFF),
                (byte)((dwKey >> 16) & 0xFF),
                (byte)((dwKey >> 8) & 0xFF),
                (byte)(dwKey & 0xFF)
            };
        }
    }
}
