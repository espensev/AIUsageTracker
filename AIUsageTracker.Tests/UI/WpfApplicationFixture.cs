// <copyright file="WpfApplicationFixture.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Threading;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class WpfApplicationFixture : IAsyncLifetime
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Dispatcher? _dispatcher;
    private TaskScheduler? _scheduler;

    public async Task InitializeAsync()
    {
        var thread = new Thread(() =>
        {
            try
            {
                // WPF permits only one Application per process. Keep its STA alive
                // for the whole collection, including asynchronous continuations.
                var app = new TestApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
                this._scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                this._ready.SetResult(app.Dispatcher);
                Dispatcher.Run();
                this._stopped.SetResult();
            }
            catch (Exception ex)
            {
                this._ready.TrySetException(ex);
                this._stopped.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        this._dispatcher = await this._ready.Task.WaitAsync(TestTimeout).ConfigureAwait(false);
    }

    public Task RunAsync(Func<Task> testBody)
    {
        var scheduler = this._scheduler ?? throw new InvalidOperationException("WPF fixture is not initialized.");
        return Task.Factory.StartNew(testBody, CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler)
            .Unwrap().WaitAsync(TestTimeout);
    }

    public async Task DisposeAsync()
    {
        var dispatcher = this._dispatcher ?? throw new InvalidOperationException("WPF fixture is not initialized.");
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        await this._stopped.Task.WaitAsync(TestTimeout).ConfigureAwait(false);
    }

    private sealed class TestApp : App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Pump WPF without starting the real monitor, tray, or preference store.
        }
    }
}
