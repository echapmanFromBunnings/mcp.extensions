using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;


namespace MCP.Extensions.Middleware;

public class HeaderLoggingMiddleware(RequestDelegate next, ILogger<HeaderLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-AGENT-MODE", out var agentModeHeaderValue))
        {
            logger.LogInformation("X-AGENT-MODE found with a value of : {FirstOrDefault}", agentModeHeaderValue.FirstOrDefault());
        }
        else
        {
            logger.LogDebug("X-AGENT-MODE header not found - using default agent mode of SUPPORT.");
        }

        if (context.Request.Headers.TryGetValue("mcp-session-id", out var sessionId))
        {
            logger.LogDebug("mcp-session-id found with a value of: {FirstOrDefault}", sessionId.FirstOrDefault());
        }
        else
        {
            logger.LogDebug("mcp-session-id header not found.");
        }

        await next(context);
    }
}

public static class HeaderLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseHeaderLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HeaderLoggingMiddleware>();
    }
}
