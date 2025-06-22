using System.Text;
using MCP.Extensions.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging; 

namespace MCP.Extensions.Middleware;

/// <summary>
/// Middleware that filters the list of tools in the response stream based on the agent mode and tool audience.
/// Removes tools from the JSON response that are not allowed for the current agent mode.
/// </summary>
public class StreamingToolFilteringMiddleware(
    RequestDelegate next,
    IToolAudienceService toolAudienceService,
    ILogger<FilteringWriteStream> loggerFilteringStream)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;

        context.Request.Headers.TryGetValue("X-AGENT-MODE", out var agentModeHeaderValue);
        string? agentMode = agentModeHeaderValue.FirstOrDefault()?.ToUpperInvariant();

        Func<string, bool> shouldRemoveToolDelegate = (toolName) =>
        {
            loggerFilteringStream.LogDebug("Evaluating tool '{ToolName}' for removal...", toolName);

            if (string.IsNullOrEmpty(toolName))
            {
                loggerFilteringStream.LogDebug("Tool name is empty, keeping tool");
                return false;
            }

            if (string.IsNullOrEmpty(agentMode))
            {
                loggerFilteringStream.LogInformation(
                    "No X-AGENT-MODE header. Tool '{ToolName}' will be removed for security.", toolName
                );
                return true;
            }

            string[] actualAllowedAudiences = toolAudienceService.GetAudiencesForTool(toolName);
            loggerFilteringStream.LogDebug(
                "Tool '{ToolName}' has audiences: [{Join}], Agent mode: '{AgentMode}'", toolName, string.Join(", ", actualAllowedAudiences), agentMode
            );

            if (actualAllowedAudiences.Any())
            {
                if (!actualAllowedAudiences.Contains(agentMode))
                {
                    loggerFilteringStream.LogInformation(
                        "Tool '{ToolName}' will be removed. Agent mode '{AgentMode}' is not in allowed audiences [{Join}].", toolName, agentMode, string.Join(", ", actualAllowedAudiences)
                    );
                    return true;
                }
            }
            loggerFilteringStream.LogDebug(
                "Tool '{ToolName}' will be kept. Agent mode '{AgentMode}' is allowed or no audience restrictions for this tool.", toolName, agentMode
            );
            return false;
        };

        await using var filteringStream = new FilteringWriteStream(
            originalBody,
            context.Response.ContentType,
            loggerFilteringStream,
            shouldRemoveToolDelegate
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

public static class ToolFilteringMiddlewareExtensions
{
    public static IApplicationBuilder UseToolFiltering(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<StreamingToolFilteringMiddleware>();
    }
}

public class FilteringWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly string? _contentType;
    private readonly ILogger<FilteringWriteStream> _logger;
    private readonly Func<string, bool> _shouldRemoveTool;

    private readonly StringBuilder _dataBuffer = new StringBuilder();
    private bool _inToolsArray = false;
    private bool _firstToolWrittenInArray = false;

    public FilteringWriteStream(
        Stream inner,
        string? contentType,
        ILogger<FilteringWriteStream> logger,
        Func<string, bool> shouldRemoveTool
    )
    {
        _inner = inner;
        _contentType = contentType;
        _logger = logger;
        _shouldRemoveTool = shouldRemoveTool;
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
            $"ProcessBufferedDataAsync called. Buffer length: {_dataBuffer.Length}, isFlushing: {isFlushing}, inToolsArray: {_inToolsArray}"
        );
        int processedOffset = 0;

        while (true)
        {
            if (processedOffset >= _dataBuffer.Length)
                break;

            string currentContent = _dataBuffer.ToString(processedOffset, _dataBuffer.Length - processedOffset);

            if (!_inToolsArray)
            {
                int toolsArrayStartIndex = currentContent.IndexOf("\"tools\":[");
                _logger.LogTrace(
                    $"Looking for tools array start in content: {currentContent.Substring(0, Math.Min(currentContent.Length, 200))}..."
                );
                if (toolsArrayStartIndex != -1)
                {
                    int consumeUntil = toolsArrayStartIndex + "\"tools\":[".Length;
                    string prefix = currentContent.Substring(0, consumeUntil);
                    _logger.LogDebug($"Found tools array at index {toolsArrayStartIndex}, writing prefix: {prefix}");
                    await WriteStringToInnerAsync(prefix, cancellationToken);
                    processedOffset += consumeUntil;
                    _inToolsArray = true;
                    _firstToolWrittenInArray = false;
                    _logger.LogInformation("Entered tools array - filtering mode activated.");
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

            if (_inToolsArray)
            {
                currentContent = _dataBuffer.ToString(processedOffset, _dataBuffer.Length - processedOffset);
                _logger.LogTrace(
                    $"In tools array, processing content: {currentContent.Substring(0, Math.Min(currentContent.Length, 200))}..."
                );

                int toolStartMarkerIndex = currentContent.IndexOf("{\"name\":\"", StringComparison.Ordinal);
                int arrayEndMarkerIndex = currentContent.IndexOf("]},", StringComparison.Ordinal);

                _logger.LogTrace(
                    "Tool start marker index: {ToolStartMarkerIndex}, Array end marker index: {ArrayEndMarkerIndex}", toolStartMarkerIndex, arrayEndMarkerIndex
                );

                if (
                    toolStartMarkerIndex != -1
                    && (arrayEndMarkerIndex == -1 || toolStartMarkerIndex < arrayEndMarkerIndex)
                )
                {
                    _logger.LogDebug("Found tool start at index {ToolStartMarkerIndex}, parsing tool JSON...", toolStartMarkerIndex);
                    int toolJsonStartIndex = toolStartMarkerIndex;
                    int openBraceCount = 0;
                    int toolJsonEndIndex = -1;

                    for (int i = toolJsonStartIndex; i < currentContent.Length; i++)
                    {
                        if (currentContent[i] == '{')
                            openBraceCount++;
                        else if (currentContent[i] == '}')
                            openBraceCount--;

                        if (openBraceCount == 0 && i >= toolJsonStartIndex)
                        {
                            toolJsonEndIndex = i;
                            break;
                        }
                    }

                    if (toolJsonEndIndex != -1)
                    {
                        string toolJson = currentContent.Substring(
                            toolJsonStartIndex,
                            toolJsonEndIndex - toolJsonStartIndex + 1
                        );
                        _logger.LogDebug(
                            "Extracted complete tool JSON: {ToolJson}", toolJson.Length > 150 ? toolJson.Substring(0, 150) + "..." : toolJson
                        );

                        string toolName = string.Empty;
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(toolJson);
                            if (doc.RootElement.TryGetProperty("name", out var nameEl))
                            {
                                toolName = nameEl.GetString() ?? "";
                                _logger.LogDebug("Parsed tool name: '{ToolName}'", toolName);
                            }
                            else
                            {
                                _logger.LogWarning("Tool JSON does not contain 'name' property");
                            }
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to parse tool JSON: {ToolJson}", toolJson.Length > 100 ? toolJson.Substring(0, 100) + "..." : toolJson
                            );
                        }

                        bool removeTool = !string.IsNullOrEmpty(toolName) && _shouldRemoveTool(toolName);
                        _logger.LogDebug("Tool '{ToolName}' removal decision: {RemoveTool}", toolName, removeTool);

                        if (!removeTool)
                        {
                            StringBuilder toolToWrite = new StringBuilder();
                            if (_firstToolWrittenInArray)
                            {
                                toolToWrite.Append(",");
                            }
                            toolToWrite.Append(toolJson);
                            await WriteStringToInnerAsync(toolToWrite.ToString(), cancellationToken);
                            _firstToolWrittenInArray = true;
                            _logger.LogDebug("Kept tool: {ToolName}", toolName);
                        }
                        else
                        {
                            _logger.LogInformation("Removed tool: {ToolName}", toolName);
                        }

                        processedOffset += (toolJsonEndIndex - toolJsonStartIndex + 1);

                        // Consume the following comma if present (and any whitespace before it)
                        if (_dataBuffer.Length > processedOffset)
                        {
                            string remainingAfterTool = _dataBuffer.ToString(
                                processedOffset,
                                _dataBuffer.Length - processedOffset
                            );
                            int k = 0;
                            while (k < remainingAfterTool.Length && char.IsWhiteSpace(remainingAfterTool[k]))
                                k++;
                            if (k < remainingAfterTool.Length && remainingAfterTool[k] == ',')
                            {
                                processedOffset += (k + 1);
                                _logger.LogTrace("Consumed trailing comma after tool");
                            }
                        }
                        continue;
                    }
                    else
                    {
                        _logger.LogTrace("Tool JSON incomplete, waiting for more data");
                        if (!isFlushing)
                            break;
                    } // Tool JSON incomplete, wait for more data if not flushing
                }
                else if (arrayEndMarkerIndex != -1) // Found end of array "]}"
                {
                    _logger.LogDebug("Found end of tools array at index {ArrayEndMarkerIndex}", arrayEndMarkerIndex);
                    int consumeUntil = arrayEndMarkerIndex + "]}".Length;
                    string suffix = currentContent.Substring(0, consumeUntil);
                    await WriteStringToInnerAsync(suffix, cancellationToken);
                    processedOffset += consumeUntil;
                    _inToolsArray = false;
                    _logger.LogInformation("Exited tools array - filtering mode deactivated.");
                    continue;
                }
                else
                {
                    _logger.LogTrace("No tool start or array end found, waiting for more data");
                    if (!isFlushing)
                        break;
                }
            }
            // If we are here and isFlushing, it means we couldn't process the rest.
            // The FlushAsync method will handle remaining _dataBuffer content.
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
        } // End while

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
