using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MCP.Extensions.Middleware;

/// <summary>
/// Web middleware to log the response body.
/// This will make the stream seekable and read the body content,
/// doing this will create a blocking stream, so only really use this for debugging purposes.
/// </summary>
public class ResponseBodyLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ResponseBodyLoggingMiddleware> _logger;

    public ResponseBodyLoggingMiddleware(RequestDelegate next, ILogger<ResponseBodyLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalBodyStream = context.Response.Body;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBodyText = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var sanitizedResponseBody = SanitizeResponseBody(responseBodyText);
        _logger.LogDebug("Response Body: {sanitizedResponseBody}", sanitizedResponseBody);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;
    }

    private string SanitizeResponseBody(string responseBodyAsString)
    {
        var sanitized = responseBodyAsString.Replace("\n", "").Replace("\r", "");
        return sanitized;
    }
}

public static class ResponseBodyLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseNonStreamingResponseBodyLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResponseBodyLoggingMiddleware>();
    }
}
