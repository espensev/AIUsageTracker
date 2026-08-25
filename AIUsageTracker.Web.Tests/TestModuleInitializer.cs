// <copyright file="TestModuleInitializer.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Web.Tests;

internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void InitializeProviderMetadata()
    {
        ProviderMetadataCatalog.Initialize(typeof(GrokProvider).Assembly);
    }
}
