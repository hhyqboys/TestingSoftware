

using Serilog;

namespace TestingSoftware.Commands
{
    /// <summary>
    /// The base class for all async commands. The class used exception to report
    /// the command failure.
    /// </summary>
    public class AsyncCommand : AbstractCommand
    {
        /// <summary>
        /// The command is executing or not.
        /// </summary>
        private bool _isExecuting;

        /// <summary>
        /// The callback of the command for executing.
        /// </summary>
        private readonly Func<object?, Task> _execute;

        /// <summary>
        /// The callback of the command for notify the listener that a exeception has been occurred.
        /// </summary>
        private readonly Action<Exception>? _onException;

        /// <summary>
        /// The command executing state.
        /// </summary>
        public bool IsExecuting
        {
            get => _isExecuting;
            set 
            {
                _isExecuting = value;
                OnCanExecuteChanged();
            }
        }

        /// <summary>
        /// The default constructor fo the async command.
        /// </summary>
        /// <param name="onException"></param>
        public AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null, 
            Action<Exception>? onException = null) : base(canExecute)
        {
            _isExecuting = false;
            _execute = execute;
            _onException = onException;
        }

        /// <summary>
        /// The Commmand can't be executed when the last command is executing.
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public override bool CanExecute(object? parameter)
        {
            return !IsExecuting && base.CanExecute(parameter);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parameter"></param>
        /// <exception cref="NotImplementedException"></exception>
        public override async void Execute(object? parameter)
        {
            // Update the execute state and UI
            IsExecuting = true;

            try
            {
                await _execute.Invoke(parameter);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to execute the command");
                _onException?.Invoke(ex);
            }

            // Update the execute state and UI
            IsExecuting = false;
        }
    }
}
