// <copyright file="TestUiPreferencesStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.UI.Slim;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.UI;

internal static class TestUiPreferencesStore
{
    public static UiPreferencesStore Create()
    {
        // Click handlers save asynchronously, so keep each window's path isolated
        // until TestTempPaths reclaims it after the test process has finished.
        var path = TestTempPaths.CreateFilePath("wpf-preferences", "preferences.json");
        var pathProvider = new Mock<IAppPathProvider>();
        pathProvider.Setup(provider => provider.GetPreferencesFilePath()).Returns(path);
        return new UiPreferencesStore(NullLogger<UiPreferencesStore>.Instance, pathProvider.Object);
    }
}
