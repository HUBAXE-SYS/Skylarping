/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Minimal replacements for CommunityToolkit.Mvvm's commands (no .NET 4.0 build), covering what
// this codebase uses. Matches the toolkit's defaults: async commands don't run concurrently
// (CanExecute is false while running) and exceptions surface on the calling dispatcher.
/*==========================================================*/

using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Skymu.Mvvm
{
    public interface IRelayCommand : ICommand
    {
        /// <summary>Raises CanExecuteChanged so bound controls re-query CanExecute.</summary>
        void NotifyCanExecuteChanged();
    }

    public interface IRelayCommand<in T> : IRelayCommand
    {
        bool CanExecute(T parameter);
        void Execute(T parameter);
    }

    public interface IAsyncRelayCommand : IRelayCommand
    {
        bool IsRunning { get; }
        Task ExecuteAsync(object parameter);
    }

    public interface IAsyncRelayCommand<in T> : IAsyncRelayCommand, IRelayCommand<T>
    {
        Task ExecuteAsync(T parameter);
    }

    internal static class CommandParameter
    {
        /// <summary>Converts a binding parameter to T (null becomes default(T), as in the toolkit).</summary>
        public static T To<T>(object parameter)
        {
            if (parameter == null)
                return default(T);
            return (T)parameter;
        }
    }

    public sealed class RelayCommand : IRelayCommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute();
        }

        public void NotifyCanExecuteChanged()
        {
            EventHandler handler = CanExecuteChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    public sealed class RelayCommand<T> : IRelayCommand<T>
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(T parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public bool CanExecute(object parameter)
        {
            return CanExecute(CommandParameter.To<T>(parameter));
        }

        public void Execute(T parameter)
        {
            _execute(parameter);
        }

        public void Execute(object parameter)
        {
            Execute(CommandParameter.To<T>(parameter));
        }

        public void NotifyCanExecuteChanged()
        {
            EventHandler handler = CanExecuteChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    public sealed class AsyncRelayCommand : IAsyncRelayCommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        public bool CanExecute(object parameter)
        {
            return !_isRunning && (_canExecute == null || _canExecute());
        }

        public async void Execute(object parameter)
        {
            await ExecuteAsync(parameter);
        }

        public async Task ExecuteAsync(object parameter)
        {
            SetRunning(true);
            try
            {
                await _execute();
            }
            finally
            {
                SetRunning(false);
            }
        }

        public void NotifyCanExecuteChanged()
        {
            EventHandler handler = CanExecuteChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void SetRunning(bool running)
        {
            _isRunning = running;
            NotifyCanExecuteChanged();
        }
    }

    public sealed class AsyncRelayCommand<T> : IAsyncRelayCommand<T>
    {
        private readonly Func<T, Task> _execute;
        private readonly Predicate<T> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<T, Task> execute, Predicate<T> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        public bool CanExecute(T parameter)
        {
            return !_isRunning && (_canExecute == null || _canExecute(parameter));
        }

        public bool CanExecute(object parameter)
        {
            return CanExecute(CommandParameter.To<T>(parameter));
        }

        public async void Execute(T parameter)
        {
            await ExecuteAsync(parameter);
        }

        public void Execute(object parameter)
        {
            Execute(CommandParameter.To<T>(parameter));
        }

        public Task ExecuteAsync(object parameter)
        {
            return ExecuteAsync(CommandParameter.To<T>(parameter));
        }

        public async Task ExecuteAsync(T parameter)
        {
            SetRunning(true);
            try
            {
                await _execute(parameter);
            }
            finally
            {
                SetRunning(false);
            }
        }

        public void NotifyCanExecuteChanged()
        {
            EventHandler handler = CanExecuteChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void SetRunning(bool running)
        {
            _isRunning = running;
            NotifyCanExecuteChanged();
        }
    }
}
