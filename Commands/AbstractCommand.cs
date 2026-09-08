using System.Windows.Input;

namespace TestingSoftware.Commands
{
    /// <summary>
    /// The base class for sync and async command class.
    /// </summary>
    public abstract class AbstractCommand : ICommand
    {
        /// <summary>
        /// Define the method that determines whether the command can be invoked in current state.
        /// </summary>
        protected Func<object?, bool>? _canExecute;

        /// <summary>
        /// The delegate is used to notify the state of CanExecute changed.
        /// </summary>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// The default constructor for abstract command.
        /// </summary>
        /// <param name="canExecute"></param>
        public AbstractCommand(Func<object?, bool>? canExecute = null) { _canExecute = canExecute; }

        /// <summary>
        /// Call the method to determines whether the command is enabled or not.
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public virtual bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute.Invoke(parameter);
        }

        /// <summary>
        /// The method must be override by the sub-class
        /// </summary>
        /// <param name="parameter"></param>
        public abstract void Execute(object? parameter);

        /// <summary>
        /// The utility method which used to notify the state of CanExecute changed.
        /// </summary>
        protected void OnCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, new EventArgs());
        }
    }
}
