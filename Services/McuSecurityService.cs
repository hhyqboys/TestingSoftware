// <copyright file=McuSecurityService company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/26 10:03:10</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Protocols;
using TestingSoftware.Utilities;
using System.Windows;
using Serilog;

namespace TestingSoftware.Services
{
    /// <summary>
    /// Provides methods to unlock the MCU using UDS Security Access (Service 0x27).
    /// </summary>
    public class McuSecurityService
    {
        private readonly UdsService _uds;

        public const UInt16 DID_MCU_DIAG_LEVEL = 0xF186;
        public const UInt16 DID_MCU_DIAG_SESSION = 0xF189;

        /// <summary>
        /// Initializes a new instance of the <see cref="McuSecurityService"/> class.
        /// </summary>
        /// <param name="uds">The UDS service instance used for communication.</param>
        public McuSecurityService(UdsService uds)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
        }

        /// <summary>
        /// Get the diagnostic security level
        /// </summary>
        /// <returns>The security level (e.g., 1 = unlocked, 0 or other = locked).</returns>
        private async Task<int> GetMcuLevel()
        {
            int level = 0;
            Service22 service22 = new Service22();
            service22.Id = DID_MCU_DIAG_LEVEL;
            Pdu? pdu = await _uds.Request(service22.BuildRequest());
            if (pdu?.Data != null && pdu.Data.Length >= 4)
            {
                try
                {
                    using (var reader = new BinaryReader(new MemoryStream(pdu.Data)))
                    {
                        reader.BaseStream.Seek(3, SeekOrigin.Begin); // Skip SID + DID (3 bytes)
                        level = reader.ReadByte();
                    }
                }
                catch
                {
                    level = 0;
                }
            }
            else
            {
                throw new Exception("通讯失败");
            }
            return level;
        }

        /// <summary>
        /// Get the diagnostic security level
        /// </summary>
        /// <returns>The security level (e.g., 1 = unlocked, 0 or other = locked).</returns>
        private async Task<int> GetMcuLevelNoPop()
        {
            int level = -1;
            try
            {
                Service22 service22 = new Service22();
                service22.Id = DID_MCU_DIAG_LEVEL;
                Pdu? pdu = await _uds.Request(service22.BuildRequest());
                if (pdu?.Data != null && pdu.Data.Length >= 4)
                {
                    using (var reader = new BinaryReader(new MemoryStream(pdu.Data)))
                    {
                        reader.BaseStream.Seek(3, SeekOrigin.Begin); // Skip SID + DID (3 bytes)
                        level = reader.ReadByte();
                    }
                }
            }
            catch { }

            return level;
        }

        /// <summary>
        /// Get MCU diagnostic session via UDS 22 service
        /// </summary>
        /// <returns></returns>
        private async Task<int> GetMcuSession()
        {
            int result = -1;

            // Project code.
            Service22 service22 = new Service22();
            service22.Id = DID_MCU_DIAG_SESSION;
            Pdu? pdu = await _uds.Request(service22.BuildRequest());
            if (pdu != null && pdu.Data != null)
            {
                try
                {
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(pdu.Data)))
                    {
                        reader.BaseStream.Seek(3, SeekOrigin.Begin);
                        result = reader.ReadByte();
                    }
                }
                catch { }
            }

            return result;
        }

        /// <summary>
        /// Unlocks the MCU by entering extended session and performing Security Access (Level 1).
        /// If already unlocked, this method returns immediately.
        /// </summary>
        /// <returns>True if unlocked successfully; otherwise false.</returns>
        public async Task<bool> CheckUnlockMCUAsync()
        {
            int level = await GetMcuLevel();
            int result = 0;
            do
            {
                if (level != 1)
                {
                    Pdu request;
                    Pdu? response;
                    // Try to request a new seed.
                    Service27 service27 = new Service27();
                    service27.Subfunc = 0x01;
                    request = service27.BuildRequest();
                    response = await _uds.Request(request);
                    if (response == null || (result = service27.ParseResponse(response)) != 0 || service27.Seed == null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Log.Error("Failed to obtain the seed");
                        });
                        result = -1;
                        break;
                    }
                    service27.Key = UdsSecurityAccess.GenerateKey(service27.Seed, 0);
                    service27.Subfunc = 0x02;
                    request = service27.BuildRequest();
                    response = await _uds.Request(request);
                    if (response == null || (result = service27.ParseResponse(response)) != 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Log.Error("Security Unlock Failed");
                        });
                        result = -1;
                        break;
                    }
                }
                return true;
            } while (false);
            return false;
        }


        /// <summary>
        /// Unlocks the MCU by entering extended session and performing Security Access (Level 1).
        /// If already unlocked, this method returns immediately.
        /// </summary>
        /// <returns>True if unlocked successfully; otherwise false.</returns>
        public async Task<bool> UnlockMCUAsync()
        {

            int result = 0;
            do
            {

                if (!_uds.Running)
                {
                    await _uds.StartAsync();
                }
                Pdu request;
                Pdu? response;


                Service10 service10 = new Service10();
                service10.Subfunc = Service10.EXTENDED_DIAG_SESSION;
                request = service10.BuildRequest();
                response = await _uds.Request(request);
                if (response == null || (result = service10.ParseResponse(response)) != 0)
                {
                    result = -1;
                    break;
                }
                // Try to request a new seed.
                Service27 service27 = new Service27();
                service27.Subfunc = 0x01;
                request = service27.BuildRequest();
                response = await _uds.Request(request);
                if (response == null || (result = service27.ParseResponse(response)) != 0 || service27.Seed == null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log.Error("Failed to obtain the seed");
                    });
                    result = -1;
                    break;
                }
                service27.Key = UdsSecurityAccess.GenerateKey(service27.Seed, 0);
                service27.Subfunc = 0x02;
                request = service27.BuildRequest();
                response = await _uds.Request(request);
                if (response == null || (result = service27.ParseResponse(response)) != 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log.Error("Security Unlock Failed");
                    });
                    result = -1;
                    break;
                }
                return true;
            } while (false);
            return false;
        }
        /// <summary>
        /// Unlocks the MCU by entering extended session and performing Security Access (Level 1).
        /// If already unlocked, this method returns immediately.
        /// </summary>
        /// <returns>True if unlocked successfully; otherwise false.</returns>
        public async Task<bool> UnlockLicenseMCUAsync()
        {

            do
            {

                if (!_uds.Running)
                {
                    await _uds.StartAsync();
                }
                int result = 0;
                Pdu request;
                Pdu? response;

                int session = await GetMcuSession();
                if (session < 0)
                    break;

                if (session != 0x03)
                {
                    Service10 service10 = new Service10();
                    service10.Subfunc = Service10.EXTENDED_DIAG_SESSION;
                    request = service10.BuildRequest();
                    response = await _uds.Request(request);
                    if (response == null || (result = service10.ParseResponse(response)) != 0)
                    {
                        result = -1;
                        break;
                    }
                }
                // Try to request a new seed.
                Service27 service27 = new Service27();
                service27.Subfunc = 0x71;
                request = service27.BuildRequest();
                response = await _uds.Request(request);
                if (response == null || (service27.ParseResponse(response)) != 0 || service27.Seed == null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log.Error("Failed to obtain the seed");
                    });
                    break;
                }
                service27.Key = UdsSecurityAccess.GenerateKey(service27.Seed, 2);
                service27.Subfunc = 0x72;
                request = service27.BuildRequest();
                response = await _uds.Request(request);
                if (response == null || (service27.ParseResponse(response)) != 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log.Error("Security Unlock Failed");
                    }); ;
                    break;
                }

                return true;
            } while (false);
            return false;
        }

        /// <summary>
        /// Unlocks the MCU by entering extended session and performing Security Access (Level 1).
        /// If already unlocked, this method returns immediately.
        /// </summary>
        /// <returns>True if unlocked successfully; otherwise false.</returns>
        public async Task<bool> UnlockMCUNoPopAsync()
        {
            int result = 0;

            int level = await GetMcuLevelNoPop();
            if (level < 0)
                return false;

            do
            {
                if (level != 1)
                {
                    Pdu request;
                    Pdu? response;
                    int session = await GetMcuSession();
                    if (session < 0)
                        break;

                    if (session != 0x03)
                    {
                        Service10 service10 = new Service10();
                        service10.Subfunc = Service10.EXTENDED_DIAG_SESSION;
                        request = service10.BuildRequest();
                        response = await _uds.Request(request);
                        if (response == null || (result = service10.ParseResponse(response)) != 0)
                        {
                            result = -1;
                            break;
                        }
                    }
                    // Try to request a new seed.
                    Service27 service27 = new Service27();
                    service27.Subfunc = 0x01;
                    request = service27.BuildRequest();
                    response = await _uds.Request(request);
                    if (response == null || (result = service27.ParseResponse(response)) != 0 || service27.Seed == null)
                    {
                        result = -1;
                        break;
                    }
                    service27.Key = UdsSecurityAccess.GenerateKey(service27.Seed, 0);
                    service27.Subfunc = 0x02;
                    request = service27.BuildRequest();
                    response = await _uds.Request(request);
                    if (response == null || (result = service27.ParseResponse(response)) != 0)
                    {
                        result = -1;
                        break;
                    }
                }

                return true;
            } while (false);

            return false;
        }
    }
}
