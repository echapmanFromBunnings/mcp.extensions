using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;


namespace MCP.Extensions.Middleware;

public class HeaderLoggingMiddleware(RequestDelegate next, ILogger<HeaderLoggingMiddleware> logger)
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-API-Key",
        "X-Auth-Token",
        "Proxy-Authorization"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        // Log all request headers safely
        LogHeaders("Request", context.Request.Headers);

        await next(context);

        // Log all response headers safely
        LogHeaders("Response", context.Response.Headers);
    }

    private void LogHeaders(string headerType, IHeaderDictionary headers)
    {
        logger.LogDebug("{HeaderType} Headers:", headerType);
        
        foreach (var header in headers)
        {
            if (IsSafeToLog(header.Key))
            {
                logger.LogDebug("{HeaderType} Header - {HeaderName}: {HeaderValue}", 
                    headerType, header.Key, header.Value.FirstOrDefault());
            }
            else
            {
                logger.LogDebug("{HeaderType} Header - {HeaderName}: [REDACTED]", 
                    headerType, header.Key);
            }
        }
    }

    private static bool IsSafeToLog(string headerName)
    {
        if (SensitiveHeaders.Contains(headerName))
        {
            return false;
        }

        var lowerHeaderName = headerName.ToLowerInvariant();
        return !lowerHeaderName.Contains("password") &&
               !lowerHeaderName.Contains("secret") &&
               !lowerHeaderName.Contains("key") &&
               !lowerHeaderName.Contains("token");
    }
}

public static class HeaderLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseHeaderLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HeaderLoggingMiddleware>();
    }
}
