// <copyright file=Class1 company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>2026/8/25 14:52:10</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestingSoftware.ViewModels;

namespace TestingSoftware.Models
{
    /// <summary>
    /// The class defines the mode which must be serialize to or de-service from the local file.
    /// The file will be saved as xml file format.
    /// </summary>
    public abstract class FileModel :ViewModel<int>
    {
        /// <summary>
        /// The function is used to build the interface object reference after loading.
        /// </summary>
        /// <returns></returns>
        public virtual bool BuildReferences() { return true; }
    }
}
