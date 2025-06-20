using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;


namespace MCP.Extensions.Middleware;

public class HeaderLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HeaderLoggingMiddleware> _logger;

    public HeaderLoggingMiddleware(RequestDelegate next, ILogger<HeaderLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-AGENT-MODE", out var agentModeHeaderValue))
        {
            _logger.LogInformation($"X-AGENT-MODE found with a value of : {agentModeHeaderValue.FirstOrDefault()}");
        }
        else
        {
            _logger.LogDebug("X-AGENT-MODE header not found - using default agent mode of SUPPORT.");
        }

        if (context.Request.Headers.TryGetValue("mcp-session-id", out var sessionId))
        {
            _logger.LogDebug($"mcp-session-id found with a value of: {sessionId.FirstOrDefault()}");
        }
        else
        {
            _logger.LogDebug("mcp-session-id header not found.");
        }

        await _next(context);
    }
}

public static class HeaderLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseHeaderLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HeaderLoggingMiddleware>();
    }
}
