using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Seedarr.Http.Security;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            headers.TryAdd("Permissions-Policy", "geolocation=(), camera=(), microphone=()");
            headers.TryAdd("X-XSS-Protection", "0");
            headers.TryAdd("Content-Security-Policy", "frame-ancestors 'self';");
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
