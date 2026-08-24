// <copyright file="MonitorApiAuthenticationMiddleware.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Security.Cryptography;
using System.Text;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Monitor.Security;

internal sealed class MonitorApiAuthenticationMiddleware
{
    private const string BearerPrefix = "Bearer ";

    private readonly string _accessToken;
    private readonly RequestDelegate _next;

    public MonitorApiAuthenticationMiddleware(RequestDelegate next, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        this._next = next;
        this._accessToken = accessToken;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!RequiresAuthentication(context.Request.Path) || HasValidBearerToken(context.Request, this._accessToken))
        {
            await this._next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
    }

    private static bool RequiresAuthentication(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(path.Value, MonitorApiRoutes.Health, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValidBearerToken(HttpRequest request, string expectedToken)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presentedToken = header[BearerPrefix.Length..].Trim();
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedToken));
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken));
        return CryptographicOperations.FixedTimeEquals(expectedHash, presentedHash);
    }
}
