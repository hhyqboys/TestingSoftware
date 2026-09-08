// <copyright file=UdsService company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>2026/8/25 17:16:52</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TestingSoftware.Protocols.Diag;

namespace TestingSoftware.Services
{
    /// <summary>
    /// The sub-class for UDS to implement the native feature.
    /// </summary>
    public class UdsService : Uds, INotifyPropertyChanged
    {
        /// <summary>
        /// Override the base no-virtual property to add a property changed notification
        /// </summary>
        public override bool Running
        {
            get => base.Running;
            set
            {
                base.Running = value;
                OnPropertyChanged(nameof(Running));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// The method is called by sub-class to notify the view that its view model 
        /// property has been changed.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void OnPropertyChanged([CallerMemberName] String? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
