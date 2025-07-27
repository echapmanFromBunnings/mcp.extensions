using System.Text;
using MCP.Extensions.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging; 

namespace MCP.Extensions.Middleware;

/// <summary>
/// Middleware that filters the list of resources in the response stream based on the agent mode and resource audience.
/// Removes resources from the JSON response that are not allowed for the current agent mode.
/// </summary>
public class StreamingResourceFilteringMiddleware(
    RequestDelegate next,
    IAudienceFilterService audienceFilterService,
    ILogger<FilteringResourceWriteStream> loggerFilteringStream)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;

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
        
        loggerFilteringStream.LogDebug("Parsed X-AGENT-MODE values: [{AgentModes}]", string.Join(", ", agentModes));

        Func<string, bool> shouldRemoveResourceDelegate = (resourceUri) =>
        {
            loggerFilteringStream.LogDebug("Evaluating resource '{ResourceUri}' for removal...", resourceUri);

            if (string.IsNullOrEmpty(resourceUri))
            {
                loggerFilteringStream.LogDebug("Resource URI is empty, keeping resource");
                return false;
            }

            if (!agentModes.Any())
            {
                loggerFilteringStream.LogInformation(
                    "No X-AGENT-MODE header values. Resource '{ResourceUri}' will be removed for security.", resourceUri
                );
                return true;
            }

            string[] actualAllowedAudiences = audienceFilterService.GetAudiencesForResource("resource", resourceUri);
            loggerFilteringStream.LogDebug(
                "Resource '{ResourceUri}' has audiences: [{Join}], Agent modes: [{AgentModes}]", resourceUri, string.Join(", ", actualAllowedAudiences), string.Join(", ", agentModes)
            );

            if (actualAllowedAudiences.Any())
            {
                // Normalize allowed audiences to uppercase for consistent comparison
                string[] normalizedAllowedAudiences = actualAllowedAudiences
                    .Select(audience => audience.ToUpperInvariant())
                    .ToArray();
                
                // Check if ANY of the agent modes match ANY of the allowed audiences
                bool hasMatchingAudience = agentModes.Any(agentMode => 
                    normalizedAllowedAudiences.Contains(agentMode, StringComparer.OrdinalIgnoreCase));
                
                if (!hasMatchingAudience)
                {
                    loggerFilteringStream.LogInformation(
                        "Resource '{ResourceUri}' will be removed. None of the agent modes [{AgentModes}] are in allowed audiences [{Join}].", resourceUri, string.Join(", ", agentModes), string.Join(", ", normalizedAllowedAudiences)
                    );
                    return true;
                }
            }
            loggerFilteringStream.LogDebug(
                "Resource '{ResourceUri}' will be kept. At least one agent mode is allowed or no audience restrictions for this resource.", resourceUri
            );
            return false;
        };

        using var filteringStream = new FilteringResourceWriteStream(
            originalBody,
            context.Response.ContentType,
            loggerFilteringStream,
            shouldRemoveResourceDelegate
        );
        context.Response.Body = filteringStream;

        try
        {
            await next(context);
        }
        finally
        {
            await filteringStream.FlushAsync(context.RequestAborted);
            context.Response.Body = originalBody;
        }
    }
}

public static class ResourceFilteringMiddlewareExtensions
{
    public static IApplicationBuilder UseResourceFiltering(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<StreamingResourceFilteringMiddleware>();
    }
}

public class FilteringResourceWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly string? _contentType;
    private readonly ILogger<FilteringResourceWriteStream> _logger;
    private readonly Func<string, bool> _shouldRemoveResource;

    private readonly StringBuilder _dataBuffer = new StringBuilder();
    private bool _inResourcesArray = false;
    private bool _firstResourceWrittenInArray = false;

    public FilteringResourceWriteStream(
        Stream inner,
        string? contentType,
        ILogger<FilteringResourceWriteStream> logger,
        Func<string, bool> shouldRemoveResource
    )
    {
        _inner = inner;
        _contentType = contentType;
        _logger = logger;
        _shouldRemoveResource = shouldRemoveResource;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await ProcessBufferedDataAsync(cancellationToken, true);
        if (_dataBuffer.Length > 0)
        {
            _logger.LogWarning(
                $"Flushing stream with unprocessed data in buffer (possibly incomplete at end of stream): {_dataBuffer.ToString(0, Math.Min(_dataBuffer.Length, 200))}"
            );
            await WriteStringToInnerAsync(_dataBuffer.ToString(), cancellationToken);
            _dataBuffer.Clear();
        }
        await _inner.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        string chunk = Encoding.UTF8.GetString(buffer, offset, count);
        _dataBuffer.Append(chunk);
        await ProcessBufferedDataAsync(cancellationToken, false);
    }

    private async Task ProcessBufferedDataAsync(CancellationToken cancellationToken, bool isFlushing)
    {
        _logger.LogTrace(
            $"ProcessBufferedDataAsync called. Buffer length: {_dataBuffer.Length}, isFlushing: {isFlushing}, inResourcesArray: {_inResourcesArray}"
        );
        int processedOffset = 0;

        while (true)
        {
            if (processedOffset >= _dataBuffer.Length)
                break;

            string currentContent = _dataBuffer.ToString(processedOffset, _dataBuffer.Length - processedOffset);

            if (!_inResourcesArray)
            {
                int resourcesArrayStartIndex = currentContent.IndexOf("\"resources\":[");
                _logger.LogTrace(
                    $"Looking for resources array start in content: {currentContent.Substring(0, Math.Min(currentContent.Length, 200))}..."
                );
                if (resourcesArrayStartIndex != -1)
                {
                    int consumeUntil = resourcesArrayStartIndex + "\"resources\":[".Length;
                    string prefix = currentContent.Substring(0, consumeUntil);
                    _logger.LogDebug($"Found resources array at index {resourcesArrayStartIndex}, writing prefix: {prefix}");
                    await WriteStringToInnerAsync(prefix, cancellationToken);
                    processedOffset += consumeUntil;
                    _inResourcesArray = true;
                    _firstResourceWrittenInArray = false;
                    _logger.LogInformation("Entered resources array - filtering mode activated.");
                    continue;
                }

                int passUntil = currentContent.Length;
                if (!isFlushing)
                {
                    int lastNewline = currentContent.LastIndexOf('\n');
                    if (lastNewline != -1)
                        passUntil = lastNewline + 1;
                    else
                        break;
                }

                if (passUntil > 0)
                {
                    string passThru = currentContent.Substring(0, passUntil);
                    await WriteStringToInnerAsync(passThru, cancellationToken);
                    processedOffset += passThru.Length;
                }
                if (!isFlushing && passUntil < currentContent.Length)
                    break;
                if (isFlushing && passUntil == currentContent.Length)
                    break;
                continue;
            }

            if (_inResourcesArray)
            {
                currentContent = _dataBuffer.ToString(processedOffset, _dataBuffer.Length - processedOffset);
                _logger.LogTrace(
                    $"In resources array, processing content: {currentContent.Substring(0, Math.Min(currentContent.Length, 200))}..."
                );

                int resourceStartMarkerIndex = currentContent.IndexOf("{\"uri\":\"", StringComparison.Ordinal);
                int arrayEndMarkerIndex = currentContent.IndexOf("]", StringComparison.Ordinal);

                _logger.LogTrace(
                    "Resource start marker index: {ResourceStartMarkerIndex}, Array end marker index: {ArrayEndMarkerIndex}", resourceStartMarkerIndex, arrayEndMarkerIndex
                );

                if (
                    resourceStartMarkerIndex != -1
                    && (arrayEndMarkerIndex == -1 || resourceStartMarkerIndex < arrayEndMarkerIndex)
                )
                {
                    _logger.LogDebug("Found resource start at index {ResourceStartMarkerIndex}, parsing resource JSON...", resourceStartMarkerIndex);
                    int resourceJsonStartIndex = resourceStartMarkerIndex;
                    int openBraceCount = 0;
                    int resourceJsonEndIndex = -1;

                    for (int i = resourceJsonStartIndex; i < currentContent.Length; i++)
                    {
                        if (currentContent[i] == '{')
                            openBraceCount++;
                        else if (currentContent[i] == '}')
                            openBraceCount--;

                        if (openBraceCount == 0 && i >= resourceJsonStartIndex)
                        {
                            resourceJsonEndIndex = i;
                            break;
                        }
                    }

                    if (resourceJsonEndIndex != -1)
                    {
                        string resourceJson = currentContent.Substring(
                            resourceJsonStartIndex,
                            resourceJsonEndIndex - resourceJsonStartIndex + 1
                        );
                        _logger.LogDebug(
                            "Extracted complete resource JSON: {ResourceJson}", resourceJson.Length > 150 ? resourceJson.Substring(0, 150) + "..." : resourceJson
                        );

                        string resourceUri = string.Empty;
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(resourceJson);
                            if (doc.RootElement.TryGetProperty("uri", out var uriEl))
                            {
                                resourceUri = uriEl.GetString() ?? "";
                                _logger.LogDebug("Parsed resource URI: '{ResourceUri}'", resourceUri);
                            }
                            else
                            {
                                _logger.LogWarning("Resource JSON does not contain 'uri' property");
                            }
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to parse resource JSON: {ResourceJson}", resourceJson.Length > 100 ? resourceJson.Substring(0, 100) + "..." : resourceJson
                            );
                        }

                        bool removeResource = !string.IsNullOrEmpty(resourceUri) && _shouldRemoveResource(resourceUri);
                        _logger.LogDebug("Resource '{ResourceUri}' removal decision: {RemoveResource}", resourceUri, removeResource);

                        if (!removeResource)
                        {
                            StringBuilder resourceToWrite = new StringBuilder();
                            if (_firstResourceWrittenInArray)
                            {
                                resourceToWrite.Append(",");
                            }
                            resourceToWrite.Append(resourceJson);
                            await WriteStringToInnerAsync(resourceToWrite.ToString(), cancellationToken);
                            _firstResourceWrittenInArray = true;
                            _logger.LogDebug("Kept resource: {ResourceUri}", resourceUri);
                        }
                        else
                        {
                            _logger.LogInformation("Removed resource: {ResourceUri}", resourceUri);
                        }

                        processedOffset += (resourceJsonEndIndex - resourceJsonStartIndex + 1);

                        // Consume the following comma if present (and any whitespace before it)
                        if (_dataBuffer.Length > processedOffset)
                        {
                            string remainingAfterResource = _dataBuffer.ToString(
                                processedOffset,
                                _dataBuffer.Length - processedOffset
                            );
                            int k = 0;
                            while (k < remainingAfterResource.Length && char.IsWhiteSpace(remainingAfterResource[k]))
                                k++;
                            if (k < remainingAfterResource.Length && remainingAfterResource[k] == ',')
                            {
                                processedOffset += (k + 1);
                                _logger.LogTrace("Consumed trailing comma after resource");
                            }
                        }
                        continue;
                    }
                    else
                    {
                        _logger.LogTrace("Resource JSON incomplete, waiting for more data");
                        if (!isFlushing)
                            break;
                    }
                }
                else if (arrayEndMarkerIndex != -1) // Found end of array "]"
                {
                    _logger.LogDebug("Found end of resources array at index {ArrayEndMarkerIndex}", arrayEndMarkerIndex);
                    int consumeUntil = arrayEndMarkerIndex + 1;
                    string suffix = currentContent.Substring(0, consumeUntil);
                    await WriteStringToInnerAsync(suffix, cancellationToken);
                    processedOffset += consumeUntil;
                    _inResourcesArray = false;
                    _logger.LogInformation("Exited resources array - filtering mode deactivated.");
                    continue;
                }
                else
                {
                    _logger.LogTrace("No resource start or array end found, waiting for more data");
                    if (!isFlushing)
                        break;
                }
            }

            if (isFlushing && processedOffset < _dataBuffer.Length)
            {
                string remainingPassThru = _dataBuffer.ToString(processedOffset, _dataBuffer.Length - processedOffset);
                _logger.LogDebug(
                    "Flushing remaining unprocessed content: {RemainingPassThru}", remainingPassThru.Length > 100 ? remainingPassThru.Substring(0, 100) + "..." : remainingPassThru
                );
                await WriteStringToInnerAsync(remainingPassThru, cancellationToken);
                processedOffset += remainingPassThru.Length;
            }
            break;
        }

        if (processedOffset > 0)
        {
            _dataBuffer.Remove(0, processedOffset);
            _logger.LogTrace("Processed and removed {ProcessedOffset} chars. Remaining buffer: {RemainingBufferLength}", processedOffset, _dataBuffer.Length);
        }
    }

    private async Task WriteStringToInnerAsync(string value, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            await _inner.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }
    }
}
