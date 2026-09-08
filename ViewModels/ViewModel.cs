using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// The view model definition for MVVM model.
    /// </summary>
    public class ViewModel<T> : INotifyPropertyChanged
    {
        /// <summary>
        /// The model of the view model.
        /// </summary>
        private T? _model;

        /// <summary>
        /// The model which the view model manged
        /// </summary>
        public T? Model
        {
            get => _model;
            set
            {
                _model = value;
                OnPropertyChanged();
                OnModelChanged();
            }
        }

        /// <summary>
        /// The flag indicate that the PropertyChangedEvent will be notified or not.
        /// </summary>
        public bool Notify { get; set; }

        /// <summary>
        /// The event handle to notify the property changed.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// The default constructor.
        /// </summary>
        public ViewModel() => Notify = true;

        /// <summary>
        /// The method is called by sub-class to notify the view that its view model 
        /// property has been changed.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void OnPropertyChanged([CallerMemberName] String? propertyName = null)
        {
            // Notify the listener that the property has been changed.
            if (Notify)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// The sub-class overload the method to do same initialize works
        /// </summary>
        protected virtual void OnModelChanged() { }

        /// <summary>
        /// Destroy the view model.
        /// </summary>
        public virtual void Dispose()
        {
            // TODO:
        }


    }
}
