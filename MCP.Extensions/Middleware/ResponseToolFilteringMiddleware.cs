using System.Text.Json;
using System.Text.Json.Nodes;
using MCP.Extensions.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MCP.Extensions.Middleware;

/// <summary>
/// Web middleware to filter out tools that do not match the header record being passed in.
/// This is done in a blocking matter, so any stream calls will not be sent in a streaming manner.
/// </summary>
public class ResponseToolFilteringMiddleware(
    RequestDelegate next,
    ILogger<ResponseToolFilteringMiddleware> logger,
    IToolAudienceService toolAudienceService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var originalResponseBodyStream = context.Response.Body;
        using (var memoryStream = new MemoryStream())
        {
            context.Response.Body = memoryStream; 

            try
            {
                context.RequestAborted.ThrowIfCancellationRequested();
                await next(context); 

                // Process the response from memoryStream
                await ProcessResponseAsync(context, memoryStream, originalResponseBodyStream);
            }
            finally
            {
                // CRITICAL: Always restore the original response body stream.
                context.Response.Body = originalResponseBodyStream;
            }
        }
    }

    private async Task ProcessResponseAsync(
        HttpContext context,
        MemoryStream memoryStream,
        Stream originalResponseBodyStream
    )
    {
        try
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            memoryStream.Position = 0;
            var responseBodyAsString = await ReadResponseBodyAsync(memoryStream, context.RequestAborted);

            if (ShouldAttemptFiltering(context, responseBodyAsString))
            {
                var (prefix, jsonData, suffix, wasPartiallyExtracted) = ExtractJsonForProcessing(
                    responseBodyAsString,
                    context.Response.ContentType
                );

                string? modifiedJsonData = TryApplyFilteringAsync(context, jsonData, wasPartiallyExtracted);

                if (modifiedJsonData != null)
                {
                    responseBodyAsString = wasPartiallyExtracted
                        ? prefix + modifiedJsonData + suffix
                        : modifiedJsonData;
                    await WriteToStreamAsync(memoryStream, responseBodyAsString, context.RequestAborted);
                    logger.LogDebug(
                        $"Modified Response Body after filtering: {responseBodyAsString.Substring(0, Math.Min(responseBodyAsString.Length, 500))}..."
                    );
                }
                // If modifiedJsonData is null, it means filtering wasn't applied or failed, so original responseBodyAsString remains.
            }
            else
            {
                logger.LogDebug(
                    $"Response content type '{context.Response.ContentType}' not targeted for filtering or body is empty. Skipping filtering."
                );
            }

            context.RequestAborted.ThrowIfCancellationRequested();
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalResponseBodyStream, context.RequestAborted);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Operation canceled during response processing in ResponseToolFilteringMiddleware.");
        }
        // JsonException during filtering is caught within TryApplyFilteringAsync
    }

    private async Task<string> ReadResponseBodyAsync(MemoryStream memoryStream, CancellationToken cancellationToken)
    {
        using (
            var reader = new StreamReader(
                memoryStream,
                System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: -1,
                leaveOpen: true
            )
        )
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }

    private bool ShouldAttemptFiltering(HttpContext context, string responseBodyAsString)
    {
        // dodgy - but I can't gauree that the response body is a tools return or not
        return !string.IsNullOrEmpty(responseBodyAsString)
            && (
                context.Response.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true
                || context.Response.ContentType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)
                    == true
            )
            && responseBodyAsString.IndexOf("\"tools\":[{") != -1;
    }

    private (string Prefix, string JsonData, string Suffix, bool WasPartiallyExtracted) ExtractJsonForProcessing(
        string responseBody,
        string? contentType
    )
    {
        logger.LogDebug(
            $"Original Response Body for Filtering ({contentType}): {responseBody.Substring(0, Math.Min(responseBody.Length, 500))}..."
        );
        if (contentType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
        {
            int startIndex = responseBody.IndexOf('{');
            int endIndex = responseBody.LastIndexOf('}');

            if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
            {
                string prefix = responseBody.Substring(0, startIndex);
                string jsonData = responseBody.Substring(startIndex, endIndex - startIndex + 1);
                string suffix = responseBody.Substring(endIndex + 1);
                logger.LogDebug(
                    $"Attempting to process extracted parts from event-stream. Prefix='{prefix.Substring(0, Math.Min(prefix.Length, 50))}', JSON='{jsonData.Substring(0, Math.Min(jsonData.Length, 100))}', Suffix='{suffix.Substring(0, Math.Min(suffix.Length, 50))}'"
                );
                return (prefix, jsonData, suffix, true);
            }
            logger.LogDebug(
                "Event-stream: No JSON object braces found or invalid range. Will attempt to process entire body as JSON."
            );
        }
        return (string.Empty, responseBody, string.Empty, false); // For application/json or fallback
    }

    private string? TryApplyFilteringAsync(
        HttpContext context,
        string jsonDataForProcessing,
        bool wasPartiallyExtracted
    )
    {
        try
        {
            JsonNode? rootNode = JsonNode.Parse(jsonDataForProcessing);
            if (rootNode == null)
            {
                logger.LogWarning("Failed to parse JSON data for filtering.");
                return null;
            }

            JsonArray? toolsArray = rootNode["result"]?["tools"] as JsonArray;
            if (toolsArray == null)
            {
                logger.LogDebug("No 'result.tools' array found in JSON, or it's not an array. No filtering applied.");
                return null; // No tools array to filter
            }

            logger.LogDebug($"Found 'result.tools' array for filtering. Original tool count: {toolsArray.Count}");

            var finalToolsToRemove = new HashSet<string>();
            context.Request.Headers.TryGetValue("X-AGENT-MODE", out var agentModeHeaderValue);
            string? agentModeHeader = agentModeHeaderValue.FirstOrDefault();
            
            // Parse CSV values from X-AGENT-MODE header
            string[] agentModes = Array.Empty<string>();
            if (!string.IsNullOrEmpty(agentModeHeader))
            {
                agentModes = agentModeHeader
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(mode => mode.Trim().ToUpperInvariant())
                    .Where(mode => !string.IsNullOrEmpty(mode))
                    .ToArray();
            }
            
            logger.LogDebug($"Current X-AGENT-MODE values: [{string.Join(", ", agentModes)}]");

            // If no X-AGENT-MODE header is found or no valid values, remove all tools
            if (!agentModes.Any())
            {
                logger.LogInformation("No valid X-AGENT-MODE values found. All tools will be removed for security.");
                foreach (JsonNode? toolNode_loopvar in toolsArray)
                {
                    if (toolNode_loopvar is JsonObject toolObject_loopvar)
                    {
                        string? toolName = toolObject_loopvar["name"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(toolName))
                        {
                            finalToolsToRemove.Add(toolName);
                        }
                    }
                }
            }
            else
            {
                foreach (JsonNode? toolNode_loopvar in toolsArray)
                {
                    if (toolNode_loopvar is JsonObject toolObject_loopvar)
                    {
                        string? toolName = toolObject_loopvar["name"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(toolName))
                        {
                            string[] allowedAudiences = toolAudienceService.GetAudiencesForTool(toolName);

                            if (allowedAudiences.Any()) // Tool has specific audience restrictions
                            {
                                // Check if ANY of the agent modes match ANY of the allowed audiences
                                bool hasMatchingAudience = agentModes.Any(agentMode => 
                                    allowedAudiences.Contains(agentMode, StringComparer.OrdinalIgnoreCase));
                                
                                if (!hasMatchingAudience)
                                {
                                    logger.LogInformation(
                                        $"Tool '{toolName}' will be removed. None of the agent modes [{string.Join(", ", agentModes)}] are in allowed audiences [{string.Join(", ", allowedAudiences)}]."
                                    );
                                    finalToolsToRemove.Add(toolName);
                                }
                                else
                                {
                                    logger.LogDebug(
                                        $"Tool '{toolName}' will be kept. At least one agent mode from [{string.Join(", ", agentModes)}] is in allowed audiences [{string.Join(", ", allowedAudiences)}]."
                                    );
                                }
                            }
                            else
                            {
                                logger.LogDebug(
                                    $"Tool '{toolName}' has no specific audience restrictions defined by McpAudienceAttribute. Kept by default."
                                );
                            }
                        }
                    }
                }
            }

            if (!finalToolsToRemove.Any())
            {
                logger.LogDebug("No tools marked for removal based on audience or agent mode.");
                return null; // No changes needed
            }

            int originalCount = toolsArray.Count;
            bool changesMade = false;

            // Iterate backwards to safely remove items from the JsonArray
            for (int i = toolsArray.Count - 1; i >= 0; i--)
            {
                JsonNode? toolNode = toolsArray[i];
                if (toolNode is JsonObject toolObject)
                {
                    string? toolName = toolObject["name"]?.GetValue<string>();
                    if (toolName != null && finalToolsToRemove.Contains(toolName))
                    {
                        toolsArray.RemoveAt(i);
                        changesMade = true;
                        logger.LogDebug($"Removed tool '{toolName}' from response based on audience policy.");
                    }
                }
            }

            if (changesMade)
            {
                int newCount = toolsArray.Count;
                logger.LogDebug($"Removed {originalCount - newCount} tools from response. New tool count: {newCount}");
                // Using default JsonSerializerOptions, customize if needed for specific serialization behavior
                return rootNode.ToJsonString(new JsonSerializerOptions());
            }

            logger.LogDebug("No tools were actually removed from response based on the specified criteria.");
            return null; // No changes made
        }
        catch (JsonException jsonEx)
        {
            string attemptedJsonSnippet = jsonDataForProcessing.Substring(
                0,
                Math.Min(jsonDataForProcessing.Length, 200)
            );
            logger.LogWarning(
                jsonEx,
                $"Failed to parse or process JSON {(wasPartiallyExtracted ? "extracted JSON part" : "response body")} using JsonNode. Snippet: '{attemptedJsonSnippet}'... Original content will be used."
            );
            return null; // Indicate failure, original content will be used
        }
    }

    private async Task WriteToStreamAsync(
        MemoryStream memoryStream,
        string content,
        CancellationToken cancellationToken
    )
    {
        memoryStream.SetLength(0);
        using (var writer = new StreamWriter(memoryStream, System.Text.Encoding.UTF8, -1, leaveOpen: true))
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
    }
}

public static class ResponseToolFilteringMiddlewareExtensions
{
    public static IApplicationBuilder UseNonStreamingResponseToolFiltering(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResponseToolFilteringMiddleware>();
    }
}
