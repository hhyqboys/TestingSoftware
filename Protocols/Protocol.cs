// <copyright file=Protocol company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:44:34</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols
{
    /// <summary>
    /// All available open system interconnection layers.
    /// </summary>
    public enum Layer
    {
        Application = 7,
        Presentation = 6,
        Session = 5,
        Transport = 4,
        Network = 3,
        Datalink = 2,
        Physical = 1
    }

    /// <summary>
    /// The class is used to describe the vehicle communication protocols. The protocol includes
    /// ISO 15031, ISO 14229, ISO 14230 and etc.
    /// </summary>
    public abstract class Protocol
    {
        /// <summary>
        /// The adapter running flag.
        /// </summary>
        private bool _running = false;

        /// <summary>
        /// The dictionary is used to save the communication paramters of the protocol.
        /// </summary>
        private Dictionary<string, object> _parameters = new Dictionary<string, object>();

        /// <summary>
        /// The adapter running flag.
        /// </summary>
        public virtual bool Running
        {
            get
            {
                lock (this)
                {
                    return _running;
                }
            }
            set
            {
                lock (this)
                {
                    _running = value;
                }
            }
        }

        /// <summary>
        /// The underlying protocol. If the protocol is the datalink layer the service can be null.
        /// </summary>
        public Protocol? LowLevelProtocol { get; set; }

        /// <summary>
        /// The layer in OSI 7 layers.
        /// </summary>
        public Layer Layer { get; }

        /// <summary>
        /// The name of the protocol.
        /// </summary>
        public String Name { get; }

        /// <summary>
        /// The event which used to notify the higher layer protocols that a new PDU has been received.
        /// </summary>
        public event Func<Pdu, bool>? PduRecieved;

        /// <summary>
        /// Create a new protocol and the protocol name must be specified.
        /// </summary>
        /// <param name="name"></param>
        public Protocol(Layer layer, string name)
        {
            Layer = layer;
            Name = name;
        }

        /// <summary>
        /// The method is used to determine the given pdu is accepted by the protocol or not
        /// </summary>
        /// <param name="pdu">protocl data unit</param>
        /// <returns>true if the pdu is acceptted</returns>
        public virtual bool IsPduAccepted(Pdu pdu) { return true; }

        /// <summary>
        /// Call the method to add a high level protocol to this protocol.
        /// </summary>
        /// <param name="highLevelProtocol">The high level protocol</param>
        /// <returns></returns>
        public int AddHighLevelProtocol(Protocol highLevelProtocol)
        {
            int result = 0;

            // Verify the input parameter.
            if (highLevelProtocol != null)
            {
                if (highLevelProtocol.Layer > Layer)
                {
                    // Save the protocol to high level protocol with low level protocol.
                    highLevelProtocol.LowLevelProtocol = this;

                    // Subscribe the PDU event.
                    PduRecieved += highLevelProtocol.OnLowLevelPduReceived;

                }
                else
                {
                    // The layer is wrong.
                    result = -1;
                }
            }
            else
            {
                result = -1;
            }

            return result;
        }

        /// <summary>
        /// Call the method to remove the high level protocol from this protocol.
        /// </summary>
        /// <param name="highLevelProtocol">The high level protocol</param>
        /// <returns></returns>
        public int RemoveHighLevelProtocol(Protocol highLevelProtocol)
        {
            int result = 0;

            // Verify the input parameter.
            if (highLevelProtocol != null)
            {
                // Save the protocol to high level protocol with low level protocol.
                highLevelProtocol.LowLevelProtocol = null;

                // Subscribe the PDU event.
                PduRecieved -= highLevelProtocol.OnLowLevelPduReceived;
            }
            else
            {
                result = -1;
            }

            return result;
        }

        /// <summary>
        /// The method is used to set the protocol parameters.
        /// </summary>
        /// <param name="name">The name of the parameter.</param>
        /// <param name="value"></param>
        public virtual int SetParameter<T>(string name, T value, bool uncheck = false) where T : notnull
        {
            int result = -1;

            // Try to set the given parameter with local dictionary.
            lock (this)
            {
                // uncheck if the first set be called.
                if (uncheck)
                {
                    _parameters[name] = value;
                    result = 0;
                }
                else
                {
                    // The key has been defined?
                    if (_parameters.ContainsKey(name))
                    {
                        _parameters[name] = value;
                        result = 0;
                    }
                }
            }

            // Try to set the given parameter with local protocol.
            if (result != 0)
            {
                // Try the next layer protocol to set.
                if (LowLevelProtocol != null)
                {
                    result = LowLevelProtocol.SetParameter<T>(name, value);
                }
                // The layer is the last one, throw a exception.
                else
                {
                    throw new InvalidOperationException(
                        string.Format("The parameter ({0}) is not available", name));
                }
            }

            return result;
        }

        /// <summary>
        /// The method is used to get the protocol parameter
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public virtual T? GetParameter<T>(string name) where T : notnull
        {
            T? value = default(T);
            bool found = false;

            // Try to get the parameter value from the local dictionary
            lock (this)
            {
                // The parameter find from the local dictionary.
                if (_parameters.ContainsKey(name))
                {
                    value = (T)_parameters[name];
                    found = true;
                }
            }

            // Try to get the given parameter from the local protocol.
            if (!found)
            {
                // Try the next layer protocol to get the parameter value.
                if (LowLevelProtocol != null)
                {
                    value = LowLevelProtocol.GetParameter<T>(name);
                }
                else
                {
                    throw new InvalidOperationException(
                        string.Format("The parameter ({0}) is not available", name));
                }
            }

            return value;
        }

        /// <summary>
        /// The method is used to determine the given parameter is contained by the protocol stack or not.
        /// </summary>
        /// <param name="name">The name of the parameter</param>
        /// <returns>true if the protocol stack contains the given parameter.</returns>
        public bool ContainParameter(string name)
        {
            bool result = false;

            // try to find the parameter in local parameter dictionary.
            lock (this)
            {
                result = _parameters.ContainsKey(name);
            }

            // Try to find the parameter in low protocol layer
            if (!result)
            {
                // Valid the low level protocol
                if (LowLevelProtocol != null)
                {
                    result = LowLevelProtocol.ContainParameter(name);
                }
            }

            return result;
        }

        /// <summary>
        /// The method is used to clear the input and output buffer of the protocol.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<int> ClearAsync(CancellationToken token = default)
        {
            int result = 0;

            if (LowLevelProtocol != null)
            {
                await LowLevelProtocol.ClearHelper();
            }

            return result;
        }

        /// <summary>
        /// The method is used to start the protocol messages handling
        /// </summary>
        /// <returns>0 if successful</returns>
        public virtual int Start() { return 0; }

        /// <summary>
        /// The method is used to stop the protocol messages handling.
        /// </summary>
        /// <returns></returns>
        public virtual int Stop() { return 0; }

        /// <summary>
        /// Call the method to start the protocol and its lower protocols
        /// </summary>
        /// <returns>0 if successfully</returns>
        public virtual async Task<int> StartAsync()
        {
            int result = 0;

            // Only execute if the protocol is not running.
            if (!Running)
            {
                // Valid the lower protocol
                if (LowLevelProtocol != null)
                {
                    result = await LowLevelProtocol.StartAsync();
                }
            }

            // If the protocol start is successfully
            if (result == 0)
            {
                Running = true;
            }

            return result;
        }

        /// <summary>
        /// The method is used to stop the protocol message handling.
        /// </summary>
        /// <returns></returns>
        public virtual async Task<int> StopAsync()
        {
            int result = 0;

            // Only execute if the protocol is running.
            if (Running)
            {
                // Valid the lower protocol
                if (LowLevelProtocol != null)
                {
                    result = await LowLevelProtocol.StopAsync();
                }
            }

            // Set stop flag.
            Running = false;

            return result;
        }

        /// <summary>
        /// The method is used to transmit a new protocol data unit the protocl.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        protected virtual int Transmit(Pdu pdu) { return 0; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        protected virtual Pdu? Receive() { return null; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        protected virtual Pdu[]? Receives() { return null; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        protected virtual Task<Pdu[]?> RecieveAsync() { return Task.FromResult<Pdu[]?>(null); }

        /// <summary>
        /// The method is used to transmit a UDS pdu to underlying protocol data unit with async way.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public virtual Task<int> TransmitAsync(Pdu pdu, CancellationToken token = default) { throw new NotImplementedException(); }

        /// <summary>
        /// The method is used to notify the protocol that a underlying protocl data unit has benen received.
        /// </summary>
        /// <param name="pdu">The underlying protocol data unit</param>
        protected bool OnPduReceived(Pdu pdu)
        {
            return PduRecieved?.Invoke(pdu) ?? false;
        }

        /// <summary>
        /// The method is used to notify that a low level PDU has been received.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns>True if the pdu has been accepted.</returns>
        protected virtual bool OnLowLevelPduReceived(Pdu pdu) { return true; }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public virtual Task<Pdu?> ReceivedAsync(CancellationToken token = default) { throw new NotImplementedException(); }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public virtual Task<Pdu?> Request(Pdu pdu, CancellationToken token = default) { throw new NotImplementedException(); }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public virtual Task<List<Pdu>?> Broadcast(Pdu pdu, CancellationToken token = default) { throw new NotImplementedException(); }

        /// <summary>
        /// The method is used to help the protocol clear its input and output buffer.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        protected virtual async Task<int> ClearHelper(CancellationToken token = default)
        {
            await Task.Delay(0);

            return 0;
        }
    }
}
