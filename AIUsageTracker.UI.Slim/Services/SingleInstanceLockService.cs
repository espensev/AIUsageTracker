// <copyright file="SingleInstanceLockService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

public sealed class SingleInstanceLockService
{
    private const int SingleInstanceLockWaitMilliseconds = 250;

    private readonly Lock _sync = new();
    private readonly string _mutexName;
    private readonly ILogger<SingleInstanceLockService> _logger;

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;

    public SingleInstanceLockService(ILogger<SingleInstanceLockService> logger)
        : this(MutexNameBuilder.BuildLocalName("AIUsageTracker_SlimUI_"), logger)
    {
    }

    internal SingleInstanceLockService(string mutexName, ILogger<SingleInstanceLockService> logger)
    {
        this._mutexName = mutexName;
        this._logger = logger;
    }

    public bool TryAcquire(bool activateExistingInstance = false)
    {
        lock (this._sync)
        {
            if (this._ownsMutex)
            {
                return true;
            }

            // Create the event before the mutex so a duplicate launch during startup
            // can leave a request pending until the owner's window is ready.
            this._activationEvent ??= new EventWaitHandle(false, EventResetMode.AutoReset, this._mutexName + "_Activate");
            this._mutex ??= new Mutex(initiallyOwned: false, name: this._mutexName);

            try
            {
                this._ownsMutex = this._mutex.WaitOne(TimeSpan.FromMilliseconds(SingleInstanceLockWaitMilliseconds));
            }
            catch (AbandonedMutexException ex)
            {
                this._ownsMutex = true;
                this._logger.LogWarning(ex, "Slim UI single-instance lock was abandoned; continuing.");
                UiDiagnosticFileLog.Write("[DIAGNOSTIC] Slim UI single-instance lock was abandoned; continuing.");
            }

            if (this._ownsMutex)
            {
                this._logger.LogInformation("Slim UI single-instance lock acquired.");
                UiDiagnosticFileLog.Write("[DIAGNOSTIC] Slim UI single-instance lock acquired.");
                return true;
            }

            if (activateExistingInstance)
            {
                this._activationEvent.Set();
            }

            this._logger.LogWarning("Duplicate Slim UI launch detected; exiting second instance.");
            UiDiagnosticFileLog.Write("[DIAGNOSTIC] Duplicate Slim UI launch detected; exiting second instance.");
            return false;
        }
    }

    public void StartActivationListener(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);

        lock (this._sync)
        {
            if (!this._ownsMutex || this._activationEvent == null || this._activationWait != null)
            {
                throw new InvalidOperationException("Only the owning instance can start an activation listener, once.");
            }

            var activationEvent = this._activationEvent;
            this._activationWait = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, _) =>
                {
                    lock (this._sync)
                    {
                        if (this._ownsMutex && ReferenceEquals(this._activationEvent, activationEvent))
                        {
                            activate();
                        }
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
    }

    public void Release()
    {
        lock (this._sync)
        {
            this._activationWait?.Unregister(null);
            this._activationWait = null;
            this._activationEvent?.Dispose();
            this._activationEvent = null;

            if (this._ownsMutex && this._mutex != null)
            {
                try
                {
                    this._mutex.ReleaseMutex();
                    this._logger.LogInformation("Slim UI single-instance lock released.");
                    UiDiagnosticFileLog.Write("[DIAGNOSTIC] Slim UI single-instance lock released.");
                }
                catch (ApplicationException ex)
                {
                    this._logger.LogWarning(ex, "Failed to release Slim UI single-instance lock.");
                    UiDiagnosticFileLog.Write($"[DIAGNOSTIC] Failed to release Slim UI lock: {ex.Message}");
                }
            }

            this._mutex?.Dispose();
            this._mutex = null;
            this._ownsMutex = false;
        }
    }
}
