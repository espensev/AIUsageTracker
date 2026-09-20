// <copyright file="RepositoryTestPaths.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests;

internal static class RepositoryTestPaths
{
    internal static string Root
    {
        get
        {
            var root = Path.Combine(AppContext.BaseDirectory, "TestData", "Repository");
            if (!File.Exists(Path.Combine(root, "Directory.Build.props")))
            {
                throw new DirectoryNotFoundException("Repository test inputs are missing from the test artifact.");
            }

            return root;
        }
    }
}
