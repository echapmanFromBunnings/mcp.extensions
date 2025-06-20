using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MCP.Extensions.Middleware;

/// <summary>
/// Web middleware to log the request body.
/// This will make the stream seekable and read the body content,
/// doing this will create a blocking stream, so only really use this for debugging purposes.
/// </summary>
public class RequestBodyLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestBodyLoggingMiddleware> _logger;

    public RequestBodyLoggingMiddleware(RequestDelegate next, ILogger<RequestBodyLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength > 0)
        {
            context.Request.Body.Position = 0;
            using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            {
                var requestBodyAsString = await reader.ReadToEndAsync(context.RequestAborted);
                var sanitizedRequestBody = SanitizeRequestBody(requestBodyAsString);
                _logger.LogDebug($"Request Body: {sanitizedRequestBody}");
                context.Request.Body.Position = 0;
            }
        }
        else
        {
            _logger.LogDebug("Request body is empty or content length is not specified.");
        }

        await _next(context);
    }

    private string SanitizeRequestBody(string requestBodyAsString)
    {
        var sanitized = requestBodyAsString.Replace("\n", "").Replace("\r", "");
        return sanitized;
    }
}

public static class RequestBodyLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseNonStreamingRequestBodyLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestBodyLoggingMiddleware>();
    }
}
