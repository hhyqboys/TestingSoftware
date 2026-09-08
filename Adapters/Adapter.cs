// <copyright file=Adapter company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:40:07</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestingSoftware.Protocols;

namespace TestingSoftware.Adapters
{
    /// <summary>
    /// The class define the adapter devices.
    /// </summary>
    [XmlInclude(typeof(SumartBluetoothAdapter))]
    public abstract class Adapter
    {
        /// <summary>
        /// The adapter running flag.
        /// </summary>
        private bool _running = false;

        /// <summary>
        /// The adapter running flag.
        /// </summary>
        [XmlIgnore]
        public bool Running
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
        /// The name of the adapter.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Get the specific channel datalink with channel type.
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>

        /// <summary>
        /// The available datalinks.
        /// </summary>
        public List<Datalink> Datalinks { get; set; }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public Adapter()
        {
            Name = "";
            Datalinks = new List<Datalink>();
        }

        /// <summary>
        /// Start the adapter.
        /// </summary>
        /// <returns>0 if successful</returns>
        public abstract Task<int> StartAsync();

        /// <summary>
        /// Stop the adapter
        /// </summary>
        /// <returns>0 if successful</returns>
        public abstract Task<int> StopAsync();
    }
}
