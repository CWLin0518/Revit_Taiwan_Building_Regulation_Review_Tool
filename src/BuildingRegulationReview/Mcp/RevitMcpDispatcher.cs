using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Mcp.Tools;

namespace BuildingRegulationReview.Mcp
{
    /// <summary>
    /// Runs MCP tool work in Revit's API context. A tool call arrives on a socket thread, where the
    /// Revit API may not be touched; it is queued here, one <see cref="ExternalEvent"/> is raised, and
    /// the socket thread waits until Revit has run it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One queue for every tool, so calls run one at a time in the order they came — two agents (or
    /// one agent and the user's own buttons) never interleave transactions.
    /// </para>
    /// <para>
    /// Revit only raises an ExternalEvent when it is idle: never while a modal dialog is open or a
    /// command is in an editing mode. A call that has not started within <see cref="StartTimeout"/> is
    /// therefore withdrawn and answered with「Revit 忙碌中」, rather than leaving the agent waiting on a
    /// dialog it cannot see. A call that has started is given its own timeout, after which it is asked
    /// to cancel at its next safe point.
    /// </para>
    /// </remarks>
    internal sealed class RevitMcpDispatcher : IExternalEventHandler, IDisposable
    {
        public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RaiseInterval = TimeSpan.FromSeconds(2);

        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly object _gate = new object();
        private ExternalEvent _event;
        private int _revitThreadId;
        private string _running;

        /// <summary>Must be called in Revit's API context (OnStartup), where ExternalEvent.Create is allowed.</summary>
        public void Initialize()
        {
            if (_event != null) throw new InvalidOperationException("ExternalEvent 已初始化。");
            _event = ExternalEvent.Create(this);
            _revitThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>The tool now running in Revit, or null.</summary>
        public string Running
        {
            get
            {
                lock (_gate) return _running;
            }
        }

        public int Queued
        {
            get
            {
                lock (_gate) return _queue.Count;
            }
        }

        /// <summary>Runs <paramref name="work"/> in Revit's API context and returns its result, or rethrows what it threw.</summary>
        /// <param name="name">What the work is, for <see cref="Running"/>.</param>
        /// <param name="runTimeout">How long the work may run once Revit has started it.</param>
        /// <param name="cancellation">Set when the server stops; withdraws or cancels the work.</param>
        public T Invoke<T>(string name, Func<UIApplication, CancellationToken, T> work, TimeSpan runTimeout, CancellationToken cancellation)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            // Read once: OnShutdown may clear the field while a socket thread is in here.
            var externalEvent = _event ?? throw new McpToolException("MCP 派送器尚未初始化或已關閉。");
            if (Thread.CurrentThread.ManagedThreadId == _revitThreadId)
                throw new InvalidOperationException("不可在 Revit 主執行緒上等待 MCP 派送器，會造成死結。");

            var item = new WorkItem(name, (application, token) => work(application, token));
            lock (_gate) _queue.Enqueue(item);
            try
            {
                // Withdrawing is what decides: if Revit started the item in the instant between the
                // wait giving up and the withdrawal, it is running now and is waited for like any other.
                if (!WaitForStart(externalEvent, item, cancellation) && item.TryWithdraw())
                {
                    throw new McpToolException(cancellation.IsCancellationRequested
                        ? "MCP 服務已停止，這個請求沒有執行。"
                        : $"Revit 忙碌中：等待 {StartTimeout.TotalSeconds:0} 秒仍無法開始執行。Revit 只有在閒置時才會執行外部請求——" +
                          "請確認沒有開著的對話框、沒有進行中的指令（例如正在繪製或選取），再重試一次。");
                }

                if (!Wait(item.Done, runTimeout, cancellation))
                {
                    item.Cancel();
                    if (!item.Done.Wait(CancelGrace))
                        throw new McpToolException($"「{name}」執行超過 {runTimeout.TotalSeconds:0} 秒，已要求取消但 Revit 仍在執行；請稍後以 revit_status 確認狀態。");
                }

                if (item.Error != null) ExceptionDispatchInfo.Capture(item.Error).Throw();
                return (T)item.Result;
            }
            finally
            {
                // Whatever happened above, an item nobody waits for any more must never run: a caller
                // that got an error must not find out later that the model was changed after all.
                // Its handles are released only when Execute can no longer touch them — withdrawn
                // before it started, or finished; one still running keeps them until the GC.
                if (item.TryWithdraw() || item.Done.IsSet) item.Dispose();
            }
        }

        /// <summary>
        /// Raises the event and waits for Revit to start the item, raising again every
        /// <see cref="RaiseInterval"/>. Re-raising covers the moment where a raise lands while Execute
        /// is already returning and Revit answers Pending without ever calling it again; it is cheap,
        /// because Execute drains the whole queue whenever it runs.
        /// </summary>
        /// <returns>True when the item started; false when it did not start in time (and was withdrawn by the caller).</returns>
        private static bool WaitForStart(ExternalEvent externalEvent, WorkItem item, CancellationToken cancellation)
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            while (true)
            {
                var request = externalEvent.Raise();
                if (request == ExternalEventRequest.Denied)
                    throw new McpToolException("Revit 拒絕執行這個請求（ExternalEvent：Denied）。請重新開啟 MCP 服務後再試。");

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return item.Started.IsSet;
                if (Wait(item.Started, remaining < RaiseInterval ? remaining : RaiseInterval, cancellation)) return true;
                if (cancellation.IsCancellationRequested) return false;
            }
        }

        public void Execute(UIApplication application)
        {
            while (true)
            {
                WorkItem item;
                lock (_gate)
                {
                    if (_queue.Count == 0) return;
                    item = _queue.Dequeue();
                }

                if (!item.TryStart()) continue;

                lock (_gate) _running = item.Name;
                try
                {
                    item.Result = item.Work(application, item.Token);
                }
                catch (Exception exception)
                {
                    item.Error = exception;
                }
                finally
                {
                    lock (_gate) _running = null;
                    item.Finish();
                }
            }
        }

        public string GetName() => "建築技術規則檢討 MCP";

        public void Dispose()
        {
            _event?.Dispose();
            _event = null;
        }

        private static bool Wait(ManualResetEventSlim signal, TimeSpan timeout, CancellationToken cancellation)
        {
            try
            {
                return signal.Wait(timeout, cancellation);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private sealed class WorkItem : IDisposable
        {
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly object _gate = new object();
            private bool _withdrawn;
            private bool _disposed;

            public WorkItem(string name, Func<UIApplication, CancellationToken, object> work)
            {
                Name = name;
                Work = work;
            }

            public string Name { get; }
            public Func<UIApplication, CancellationToken, object> Work { get; }
            public CancellationToken Token => _cancellation.Token;
            public ManualResetEventSlim Started { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim Done { get; } = new ManualResetEventSlim();
            public object Result { get; set; }
            public Exception Error { get; set; }

            /// <summary>Takes the item out of the race: true when it had not started and never will.</summary>
            public bool TryWithdraw()
            {
                lock (_gate)
                {
                    if (Started.IsSet) return false;
                    _withdrawn = true;
                    return true;
                }
            }

            /// <summary>Claims the item for running: false when the caller already gave up on it.</summary>
            public bool TryStart()
            {
                lock (_gate)
                {
                    if (_withdrawn) return false;
                    Started.Set();
                    return true;
                }
            }

            public void Cancel()
            {
                lock (_gate)
                {
                    if (!_disposed) _cancellation.Cancel();
                }
            }

            /// <summary>Signals completion; never throws back into Revit, whatever state the caller left the item in.</summary>
            public void Finish()
            {
                lock (_gate)
                {
                    if (!_disposed) Done.Set();
                }
            }

            /// <summary>Called by the waiting thread only once the item is withdrawn or finished, so Execute is done with it.</summary>
            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed) return;
                    _disposed = true;
                }
                _cancellation.Dispose();
                Started.Dispose();
                Done.Dispose();
            }
        }
    }
}
