// <copyright file="SingleInstanceLockServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.UI;

public class SingleInstanceLockServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryAcquire_OrdinaryDuplicate_ActivatesOwnerEvenBeforeListenerStarts(bool listenAfterRequest)
    {
        var mutexName = @"Local\AIUsageTracker_Test_" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceLockService(mutexName, NullLogger<SingleInstanceLockService>.Instance);
        var second = new SingleInstanceLockService(mutexName, NullLogger<SingleInstanceLockService>.Instance);
        using var activated = new ManualResetEventSlim();

        try
        {
            Assert.True(first.TryAcquire());
            if (!listenAfterRequest)
            {
                first.StartActivationListener(activated.Set);
            }

            Assert.False(RunOnBackgroundThread(() => second.TryAcquire(activateExistingInstance: true)));
            second.Release();

            if (listenAfterRequest)
            {
                first.StartActivationListener(activated.Set);
            }

            Assert.True(activated.Wait(TimeSpan.FromSeconds(5)), "The owner did not receive the activation request.");
            activated.Reset();
            Assert.False(RunOnBackgroundThread(() => second.TryAcquire(activateExistingInstance: true)));
            Assert.True(activated.Wait(TimeSpan.FromSeconds(5)), "The listener stopped after the first request.");
        }
        finally
        {
            second.Release();
            first.Release();
        }
    }

    [Fact]
    public void TryAcquire_SilentDuplicate_DoesNotLeavePendingActivation()
    {
        var mutexName = @"Local\AIUsageTracker_Test_" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceLockService(mutexName, NullLogger<SingleInstanceLockService>.Instance);
        var second = new SingleInstanceLockService(mutexName, NullLogger<SingleInstanceLockService>.Instance);

        try
        {
            Assert.True(first.TryAcquire());
            Assert.False(RunOnBackgroundThread(() => second.TryAcquire(activateExistingInstance: false)));
            using var activationEvent = EventWaitHandle.OpenExisting(mutexName + "_Activate");
            Assert.False(activationEvent.WaitOne(0));
        }
        finally
        {
            second.Release();
            first.Release();
        }
    }

    [Fact]
    public void Release_WhenListening_ClosesActivationEventAndAllowsNewOwner()
    {
        var mutexName = @"Local\AIUsageTracker_Test_" + Guid.NewGuid().ToString("N");
        var service = new SingleInstanceLockService(mutexName, NullLogger<SingleInstanceLockService>.Instance);
        try
        {
            Assert.True(service.TryAcquire());
            service.StartActivationListener(() => throw new InvalidOperationException("Unexpected activation after release."));
            service.Release();

            Assert.False(EventWaitHandle.TryOpenExisting(mutexName + "_Activate", out var staleEvent));
            staleEvent?.Dispose();
            Assert.True(service.TryAcquire());
            using var activationEvent = EventWaitHandle.OpenExisting(mutexName + "_Activate");
            Assert.False(activationEvent.WaitOne(0));
        }
        finally
        {
            service.Release();
        }
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("--startup", false)]
    [InlineData("--STARTUP", false)]
    [InlineData("--test", false)]
    [InlineData("--screenshot", false)]
    public void ShouldActivateExistingInstance_RespectsLaunchMode(string argument, bool expected)
    {
        Assert.Equal(expected, App.ShouldActivateExistingInstance(string.IsNullOrEmpty(argument) ? [] : [argument]));
    }

    [Fact]
    public void TryAcquire_WhenLockAlreadyHeldByAnotherInstance_ReturnsFalse()
    {
        var mutexName = @"Local\AIUsageTracker_Test_" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);
        var second = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);

        try
        {
            Assert.True(first.TryAcquire());
            var secondAcquireResult = RunOnBackgroundThread(() => second.TryAcquire());
            Assert.False(secondAcquireResult);
        }
        finally
        {
            second.Release();
            first.Release();
        }
    }

    [Fact]
    public void Release_WhenCalled_AllowsAnotherInstanceToAcquireLock()
    {
        var mutexName = @"Local\AIUsageTracker_Test_" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);
        var second = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);

        try
        {
            Assert.True(first.TryAcquire());
            var blockedAcquireResult = RunOnBackgroundThread(() => second.TryAcquire());
            Assert.False(blockedAcquireResult);

            first.Release();

            var secondAcquireResult = RunOnBackgroundThread(() => second.TryAcquire());
            Assert.True(secondAcquireResult);
        }
        finally
        {
            second.Release();
            first.Release();
        }
    }

    private static bool RunOnBackgroundThread(Func<bool> action)
    {
        var completed = new ManualResetEventSlim(false);
        Exception? exception = null;
        var result = false;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.IsBackground = true;
        thread.Start();
        completed.Wait();

        if (exception != null)
        {
            throw new InvalidOperationException("Background thread execution failed.", exception);
        }

        return result;
    }
}
