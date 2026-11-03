/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// .NET 4.0 support for C# async/await (the framework pieces arrived in .NET 4.5).
//
// The compiler looks these types up by name, so defining them here makes `async`/`await`
// compile in the app (and in the tests, which reference it). Written
// for this project instead of using Microsoft.Bcl.Async, whose license isn't compatible with
// the AGPL. Behaviour follows .NET 4.5:
//   - `await` resumes on the caller's SynchronizationContext (e.g. the WPF UI thread)
//     unless ConfigureAwait(false) is used;
//   - awaiting a faulted task rethrows its first inner exception with the original stack trace;
//   - exceptions from `async void` methods are raised on the caller's SynchronizationContext.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    public interface IAsyncStateMachine
    {
        void MoveNext();
        void SetStateMachine(IAsyncStateMachine stateMachine);
    }

    public interface INotifyCompletion
    {
        void OnCompleted(Action continuation);
    }

    public interface ICriticalNotifyCompletion : INotifyCompletion
    {
        void UnsafeOnCompleted(Action continuation);
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class StateMachineAttribute : Attribute
    {
        public StateMachineAttribute(Type stateMachineType) { StateMachineType = stateMachineType; }
        public Type StateMachineType { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncStateMachineAttribute : StateMachineAttribute
    {
        public AsyncStateMachineAttribute(Type stateMachineType) : base(stateMachineType) { }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class IteratorStateMachineAttribute : StateMachineAttribute
    {
        public IteratorStateMachineAttribute(Type stateMachineType) : base(stateMachineType) { }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class CallerMemberNameAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class CallerFilePathAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class CallerLineNumberAttribute : Attribute { }

    // ------------------------------------------------------------------------ awaiters

    internal static class AwaitHelpers
    {
        private static readonly MethodInfo PreserveStackTrace = typeof(Exception).GetMethod(
            "InternalPreserveStackTrace", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Rethrows an exception without losing the stack trace from where it was first thrown.</summary>
        public static void Rethrow(Exception ex)
        {
            if (PreserveStackTrace != null)
                PreserveStackTrace.Invoke(ex, null);
            throw ex;
        }

        /// <summary>Waits for the task and throws what awaiting it should throw (like .NET 4.5's GetResult).</summary>
        public static void ThrowIfNotSuccessful(Task task)
        {
            if (!task.IsCompleted)
            {
                try { task.Wait(); }
                catch (AggregateException) { } // inspected below
            }
            if (task.IsCanceled)
                throw new TaskCanceledException(task);
            if (task.IsFaulted)
            {
                AggregateException aggregate = task.Exception;
                Rethrow(aggregate.InnerExceptions.Count > 0 ? aggregate.InnerExceptions[0] : aggregate);
            }
        }

        /// <summary>Runs <paramref name="continuation"/> when the task finishes, on the captured context if requested.</summary>
        public static void OnCompleted(Task task, Action continuation, bool continueOnCapturedContext)
        {
            if (continuation == null) throw new ArgumentNullException("continuation");

            SynchronizationContext context = continueOnCapturedContext ? SynchronizationContext.Current : null;
            if (context != null && context.GetType() != typeof(SynchronizationContext))
            {
                task.ContinueWith(_ => context.Post(state => ((Action)state)(), continuation),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return;
            }

            TaskScheduler scheduler = continueOnCapturedContext ? TaskScheduler.Current : TaskScheduler.Default;
            task.ContinueWith(_ => continuation(),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, scheduler);
        }
    }

    public struct TaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly Task _task;

        internal TaskAwaiter(Task task) { _task = task; }

        public bool IsCompleted { get { return _task.IsCompleted; } }
        public void OnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, true); }
        public void UnsafeOnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, true); }
        public void GetResult() { AwaitHelpers.ThrowIfNotSuccessful(_task); }
    }

    public struct TaskAwaiter<TResult> : ICriticalNotifyCompletion
    {
        private readonly Task<TResult> _task;

        internal TaskAwaiter(Task<TResult> task) { _task = task; }

        public bool IsCompleted { get { return _task.IsCompleted; } }
        public void OnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, true); }
        public void UnsafeOnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, true); }

        public TResult GetResult()
        {
            AwaitHelpers.ThrowIfNotSuccessful(_task);
            return _task.Result;
        }
    }

    public struct ConfiguredTaskAwaitable
    {
        private readonly ConfiguredTaskAwaiter _awaiter;

        internal ConfiguredTaskAwaitable(Task task, bool continueOnCapturedContext)
        {
            _awaiter = new ConfiguredTaskAwaiter(task, continueOnCapturedContext);
        }

        public ConfiguredTaskAwaiter GetAwaiter() { return _awaiter; }

        public struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion
        {
            private readonly Task _task;
            private readonly bool _continueOnCapturedContext;

            internal ConfiguredTaskAwaiter(Task task, bool continueOnCapturedContext)
            {
                _task = task;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public bool IsCompleted { get { return _task.IsCompleted; } }
            public void OnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, _continueOnCapturedContext); }
            public void UnsafeOnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, _continueOnCapturedContext); }
            public void GetResult() { AwaitHelpers.ThrowIfNotSuccessful(_task); }
        }
    }

    public struct ConfiguredTaskAwaitable<TResult>
    {
        private readonly ConfiguredTaskAwaiter _awaiter;

        internal ConfiguredTaskAwaitable(Task<TResult> task, bool continueOnCapturedContext)
        {
            _awaiter = new ConfiguredTaskAwaiter(task, continueOnCapturedContext);
        }

        public ConfiguredTaskAwaiter GetAwaiter() { return _awaiter; }

        public struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion
        {
            private readonly Task<TResult> _task;
            private readonly bool _continueOnCapturedContext;

            internal ConfiguredTaskAwaiter(Task<TResult> task, bool continueOnCapturedContext)
            {
                _task = task;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public bool IsCompleted { get { return _task.IsCompleted; } }
            public void OnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, _continueOnCapturedContext); }
            public void UnsafeOnCompleted(Action continuation) { AwaitHelpers.OnCompleted(_task, continuation, _continueOnCapturedContext); }

            public TResult GetResult()
            {
                AwaitHelpers.ThrowIfNotSuccessful(_task);
                return _task.Result;
            }
        }
    }

    /// <summary>Awaitable returned by TaskEx.Yield(): always continues asynchronously.</summary>
    public struct YieldAwaitable
    {
        public YieldAwaiter GetAwaiter() { return new YieldAwaiter(); }

        public struct YieldAwaiter : ICriticalNotifyCompletion
        {
            public bool IsCompleted { get { return false; } }

            public void OnCompleted(Action continuation) { Schedule(continuation); }
            public void UnsafeOnCompleted(Action continuation) { Schedule(continuation); }
            public void GetResult() { }

            private static void Schedule(Action continuation)
            {
                if (continuation == null) throw new ArgumentNullException("continuation");
                SynchronizationContext context = SynchronizationContext.Current;
                if (context != null && context.GetType() != typeof(SynchronizationContext))
                    context.Post(state => ((Action)state)(), continuation);
                else
                    ThreadPool.QueueUserWorkItem(state => ((Action)state)(), continuation);
            }
        }
    }

    // ------------------------------------------------------------------------ method builders

    /// <summary>
    /// Holds the boxed state machine so awaits resume the same instance. The compiler generates the
    /// state machine as a struct in Release builds; it is boxed on the first await that doesn't
    /// complete synchronously, and SetStateMachine points the boxed copy's builder at the box.
    /// </summary>
    internal struct MoveNextRunner
    {
        private IAsyncStateMachine _stateMachine;

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        public Action GetContinuation<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            if (_stateMachine == null)
            {
                IAsyncStateMachine boxed = stateMachine; // boxes value-type state machines
                _stateMachine = boxed;
                boxed.SetStateMachine(boxed);
            }
            IAsyncStateMachine target = _stateMachine;
            return target.MoveNext;
        }
    }

    public struct AsyncVoidMethodBuilder
    {
        private SynchronizationContext _context;
        private MoveNextRunner _runner;

        public static AsyncVoidMethodBuilder Create()
        {
            var builder = new AsyncVoidMethodBuilder { _context = SynchronizationContext.Current };
            if (builder._context != null)
                builder._context.OperationStarted();
            return builder;
        }

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) { _runner.SetStateMachine(stateMachine); }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.OnCompleted(_runner.GetContinuation(ref stateMachine));
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.UnsafeOnCompleted(_runner.GetContinuation(ref stateMachine));
        }

        public void SetResult()
        {
            if (_context != null)
                _context.OperationCompleted();
        }

        public void SetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException("exception");

            // Like .NET 4.5: raise it where the async void method was called from.
            SynchronizationContext context = _context;
            if (context != null)
            {
                try
                {
                    context.Post(state => AwaitHelpers.Rethrow((Exception)state), exception);
                }
                finally
                {
                    context.OperationCompleted();
                }
            }
            else
            {
                ThreadPool.QueueUserWorkItem(state => AwaitHelpers.Rethrow((Exception)state), exception);
            }
        }
    }

    public struct AsyncTaskMethodBuilder
    {
        private TaskCompletionSource<object> _tcs;
        private MoveNextRunner _runner;

        public static AsyncTaskMethodBuilder Create()
        {
            // Created up front so the original builder and the boxed copy share the same Task.
            return new AsyncTaskMethodBuilder { _tcs = new TaskCompletionSource<object>() };
        }

        public Task Task { get { return _tcs.Task; } }

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) { _runner.SetStateMachine(stateMachine); }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.OnCompleted(_runner.GetContinuation(ref stateMachine));
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.UnsafeOnCompleted(_runner.GetContinuation(ref stateMachine));
        }

        public void SetResult() { _tcs.TrySetResult(null); }

        public void SetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException("exception");
            if (exception is OperationCanceledException)
                _tcs.TrySetCanceled();
            else
                _tcs.TrySetException(exception);
        }
    }

    public struct AsyncTaskMethodBuilder<TResult>
    {
        private TaskCompletionSource<TResult> _tcs;
        private MoveNextRunner _runner;

        public static AsyncTaskMethodBuilder<TResult> Create()
        {
            return new AsyncTaskMethodBuilder<TResult> { _tcs = new TaskCompletionSource<TResult>() };
        }

        public Task<TResult> Task { get { return _tcs.Task; } }

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) { _runner.SetStateMachine(stateMachine); }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.OnCompleted(_runner.GetContinuation(ref stateMachine));
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.UnsafeOnCompleted(_runner.GetContinuation(ref stateMachine));
        }

        public void SetResult(TResult result) { _tcs.TrySetResult(result); }

        public void SetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException("exception");
            if (exception is OperationCanceledException)
                _tcs.TrySetCanceled();
            else
                _tcs.TrySetException(exception);
        }
    }
}

namespace System.Threading.Tasks
{
    /// <summary>.NET 4.5's Task.Run/Delay/FromResult/WhenAll/WhenAny for .NET 4.0 (same names as Microsoft.Bcl.Async).</summary>
    public static class TaskEx
    {
        private static readonly object TimersLock = new object();
        // Pending Delay timers are kept referenced here; an unreferenced Timer can be garbage collected before it fires.
        private static readonly HashSet<Timer> PendingTimers = new HashSet<Timer>();

        public static Task Run(Action action) { return Run(action, CancellationToken.None); }

        public static Task Run(Action action, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(action, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);
        }

        public static Task<TResult> Run<TResult>(Func<TResult> function) { return Run(function, CancellationToken.None); }

        public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(function, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);
        }

        public static Task Run(Func<Task> function) { return Run(function, CancellationToken.None); }

        public static Task Run(Func<Task> function, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(function, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
        }

        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function) { return Run(function, CancellationToken.None); }

        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(function, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
        }

        /// <summary>Yields back to the current context (UI message loop or thread pool) before continuing.</summary>
        public static YieldAwaitable Yield() { return new YieldAwaitable(); }

        public static Task<TResult> FromResult<TResult>(TResult result)
        {
            var tcs = new TaskCompletionSource<TResult>();
            tcs.SetResult(result);
            return tcs.Task;
        }

        public static Task Delay(int millisecondsDelay) { return Delay(millisecondsDelay, CancellationToken.None); }

        public static Task Delay(TimeSpan delay) { return Delay(delay, CancellationToken.None); }

        public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            long ms = (long)delay.TotalMilliseconds;
            if (ms < -1 || ms > int.MaxValue) throw new ArgumentOutOfRangeException("delay");
            return Delay((int)ms, cancellationToken);
        }

        public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
        {
            if (millisecondsDelay < -1) throw new ArgumentOutOfRangeException("millisecondsDelay");

            var tcs = new TaskCompletionSource<bool>();
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }
            if (millisecondsDelay == 0)
            {
                tcs.SetResult(true);
                return tcs.Task;
            }

            Timer timer = null;
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            Action<bool> finish = canceled =>
            {
                lock (TimersLock) PendingTimers.Remove(timer);
                timer.Dispose();
                registration.Dispose();
                if (canceled) tcs.TrySetCanceled();
                else tcs.TrySetResult(true);
            };

            timer = new Timer(_ => finish(false), null, Timeout.Infinite, Timeout.Infinite);
            lock (TimersLock) PendingTimers.Add(timer);
            if (cancellationToken.CanBeCanceled)
                registration = cancellationToken.Register(() => finish(true));
            timer.Change(millisecondsDelay, Timeout.Infinite);
            return tcs.Task;
        }

        public static Task WhenAll(params Task[] tasks) { return WhenAll((IEnumerable<Task>)tasks); }

        public static Task WhenAll(IEnumerable<Task> tasks)
        {
            Task[] array = tasks.ToArray();
            var tcs = new TaskCompletionSource<object>();
            if (array.Length == 0)
            {
                tcs.SetResult(null);
                return tcs.Task;
            }
            Task.Factory.ContinueWhenAll(array, done => Complete(tcs, done, () => null),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return tcs.Task;
        }

        public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks)
        {
            return WhenAll((IEnumerable<Task<TResult>>)tasks);
        }

        public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks)
        {
            Task<TResult>[] array = tasks.ToArray();
            var tcs = new TaskCompletionSource<TResult[]>();
            if (array.Length == 0)
            {
                tcs.SetResult(new TResult[0]);
                return tcs.Task;
            }
            Task.Factory.ContinueWhenAll(array, done => Complete(tcs, done, () => done.Select(t => t.Result).ToArray()),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return tcs.Task;
        }

        public static Task<Task> WhenAny(params Task[] tasks) { return WhenAny((IEnumerable<Task>)tasks); }

        public static Task<Task> WhenAny(IEnumerable<Task> tasks)
        {
            Task[] array = tasks.ToArray();
            if (array.Length == 0) throw new ArgumentException("At least one task is required.", "tasks");
            return Task.Factory.ContinueWhenAny(array, first => first,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public static Task<Task<TResult>> WhenAny<TResult>(params Task<TResult>[] tasks)
        {
            return WhenAny((IEnumerable<Task<TResult>>)tasks);
        }

        public static Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks)
        {
            Task<TResult>[] array = tasks.ToArray();
            if (array.Length == 0) throw new ArgumentException("At least one task is required.", "tasks");
            return Task.Factory.ContinueWhenAny(array, first => first,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static void Complete<T>(TaskCompletionSource<T> tcs, Task[] done, Func<T> result)
        {
            List<Exception> errors = done.Where(t => t.IsFaulted).SelectMany(t => t.Exception.InnerExceptions).ToList();
            if (errors.Count > 0)
                tcs.TrySetException(errors);
            else if (done.Any(t => t.IsCanceled))
                tcs.TrySetCanceled();
            else
                tcs.TrySetResult(result());
        }
    }
}

/// <summary>Makes Task awaitable. In the global namespace (as in Microsoft.Bcl.Async) so it's always in scope.</summary>
public static class AwaitExtensions
{
    public static TaskAwaiter GetAwaiter(this Task task)
    {
        if (task == null) throw new ArgumentNullException("task");
        return new TaskAwaiter(task);
    }

    public static TaskAwaiter<TResult> GetAwaiter<TResult>(this Task<TResult> task)
    {
        if (task == null) throw new ArgumentNullException("task");
        return new TaskAwaiter<TResult>(task);
    }

    public static ConfiguredTaskAwaitable ConfigureAwait(this Task task, bool continueOnCapturedContext)
    {
        if (task == null) throw new ArgumentNullException("task");
        return new ConfiguredTaskAwaitable(task, continueOnCapturedContext);
    }

    public static ConfiguredTaskAwaitable<TResult> ConfigureAwait<TResult>(this Task<TResult> task, bool continueOnCapturedContext)
    {
        if (task == null) throw new ArgumentNullException("task");
        return new ConfiguredTaskAwaitable<TResult>(task, continueOnCapturedContext);
    }
}
