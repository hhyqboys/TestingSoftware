namespace TestingSoftware.Commands
{
    /// <summary>
    /// The relay command is basical implementation for WPF command with sync mode. 
    /// </summary>
    public class SyncCommand : AbstractCommand
    {
        /// <summary>
        /// Define the method to be called when the command is invoked.
        /// </summary>
        private Action<object?> _execute;

        /// <summary>
        /// The default constructor of the relay command.
        /// </summary>
        /// <param name="execute">The execute callback.</param>
        /// <param name="executeFunc">The callback which used to test the command is available or not.</param>
        public SyncCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : base(canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        /// <summary>
        /// Call the method to execute the command.
        /// </summary>
        /// <param name="parameter"></param>
        public override void Execute(object? parameter)
        {
            // Call the callback to execute.
            _execute.Invoke(parameter);

            // Update the UI
            OnCanExecuteChanged();
        }
    }
}
