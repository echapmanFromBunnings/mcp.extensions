using System.Text;
using MCP.Extensions.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging; 

namespace MCP.Extensions.Middleware;

/// <summary>
/// Middleware that filters the list of prompts in the response stream based on the agent mode and prompt audience.
/// Removes prompts from the JSON response that are not allowed for the current agent mode.
/// </summary>
public class StreamingPromptFilteringMiddleware(
    RequestDelegate next,
    IAudienceFilterService audienceFilterService,
    ILogger<FilteringPromptWriteStream> loggerFilteringStream)
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

        Func<string, bool> shouldRemovePromptDelegate = (promptName) =>
        {
            loggerFilteringStream.LogDebug("Evaluating prompt '{PromptName}' for removal...", promptName);

            if (string.IsNullOrEmpty(promptName))
            {
                loggerFilteringStream.LogDebug("Prompt name is empty, keeping prompt");
                return false;
            }

            if (!agentModes.Any())
            {
                loggerFilteringStream.LogInformation(
                    "No X-AGENT-MODE header values. Prompt '{PromptName}' will be removed for security.", promptName
                );
                return true;
            }

            string[] actualAllowedAudiences = audienceFilterService.GetAudiencesForResource("prompt", promptName);
            loggerFilteringStream.LogDebug(
                "Prompt '{PromptName}' has audiences: [{Join}], Agent modes: [{AgentModes}]", promptName, string.Join(", ", actualAllowedAudiences), string.Join(", ", agentModes)
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
                        "Prompt '{PromptName}' will be removed. None of the agent modes [{AgentModes}] are in allowed audiences [{Join}].", promptName, string.Join(", ", agentModes), string.Join(", ", normalizedAllowedAudiences)
                    );
                    return true;
                }
            }
            loggerFilteringStream.LogDebug(
                "Prompt '{PromptName}' will be kept. At least one agent mode is allowed or no audience restrictions for this prompt.", promptName
            );
            return false;
        };

        using var filteringStream = new FilteringPromptWriteStream(
            originalBody,
            context.Response.ContentType,
            loggerFilteringStream,
            shouldRemovePromptDelegate
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

public static class PromptFilteringMiddlewareExtensions
{
    public static IApplicationBuilder UsePromptFiltering(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<StreamingPromptFilteringMiddleware>();
    }
}

public class FilteringPromptWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly string? _contentType;
    private readonly ILogger<FilteringPromptWriteStream> _logger;
    private readonly Func<string, bool> _shouldRemovePrompt;

    private readonly StringBuilder _dataBuffer = new StringBuilder();
    private bool _inPromptsArray = false;
    private bool _firstPromptWrittenInArray = false;

    public FilteringPromptWriteStream(
        Stream inner,
        string? contentType,
        ILogger<FilteringPromptWriteStream> logger,
        Func<string, bool> shouldRemovePrompt
    )
    {
        _inner = inner;
        _contentType = contentType;
        _logger = logger;
        _shouldRemovePrompt = shouldRemovePrompt;
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
            $"ProcessBufferedDataAsync called. Buffer length: {_dataBuffer.Length}, isFlushing: {isFlushing}, inPromptsArray: {_inPromptsArray}"
        );
        int processedOffset = 0;

        while (true)
        {
            if (processedOffset >= _dataBuffer.Length)
                break;

            string currentContent = _dataBuffer.ToString(processedOffset, _dataBuffer.Length - processedOffset);

            if (!_inPromptsArray)
            {
                int promptsArrayStartIndex = currentContent.IndexOf("\"prompts\":[");
                _logger.LogTrace(
                    $"Looking for prompts array start in content: {currentContent.Substring(0, Math.Min(currentContent.Length, 200))}..."
                );
                if (promptsArrayStartIndex != -1)
                {
                    int consumeUntil = promptsArrayStartIndex + "\"prompts\":[".Length;
                    string prefix = currentContent.Substring(0, consumeUntil);
                    _logger.LogDebug($"Found prompts array at index {promptsArrayStartIndex}, writing prefix: {prefix}");
                    await WriteStringToInnerAsync(prefix, cancellationToken);
                    processedOffset += consumeUntil;
                    _inPromptsArray = true;
                    _firstPromptWrittenInArray = false;
                    _logger.LogInformation("Entered prompts array - filtering mode activated.");
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

            if (_inPromptsArray)
            {
                currentContent = _dataBuffer.ToString(processedOffset, _dataBuffer.Length - processedOffset);
                _logger.LogTrace(
                    $"In prompts array, processing content: {currentContent.Substring(0, Math.Min(currentContent.Length, 200))}..."
                );

                int promptStartMarkerIndex = currentContent.IndexOf("{\"name\":\"", StringComparison.Ordinal);
                int arrayEndMarkerIndex = currentContent.IndexOf("]", StringComparison.Ordinal);

                _logger.LogTrace(
                    "Prompt start marker index: {PromptStartMarkerIndex}, Array end marker index: {ArrayEndMarkerIndex}", promptStartMarkerIndex, arrayEndMarkerIndex
                );

                if (
                    promptStartMarkerIndex != -1
                    && (arrayEndMarkerIndex == -1 || promptStartMarkerIndex < arrayEndMarkerIndex)
                )
                {
                    _logger.LogDebug("Found prompt start at index {PromptStartMarkerIndex}, parsing prompt JSON...", promptStartMarkerIndex);
                    int promptJsonStartIndex = promptStartMarkerIndex;
                    int openBraceCount = 0;
                    int promptJsonEndIndex = -1;

                    for (int i = promptJsonStartIndex; i < currentContent.Length; i++)
                    {
                        if (currentContent[i] == '{')
                            openBraceCount++;
                        else if (currentContent[i] == '}')
                            openBraceCount--;

                        if (openBraceCount == 0 && i >= promptJsonStartIndex)
                        {
                            promptJsonEndIndex = i;
                            break;
                        }
                    }

                    if (promptJsonEndIndex != -1)
                    {
                        string promptJson = currentContent.Substring(
                            promptJsonStartIndex,
                            promptJsonEndIndex - promptJsonStartIndex + 1
                        );
                        _logger.LogDebug(
                            "Extracted complete prompt JSON: {PromptJson}", promptJson.Length > 150 ? promptJson.Substring(0, 150) + "..." : promptJson
                        );

                        string promptName = string.Empty;
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(promptJson);
                            if (doc.RootElement.TryGetProperty("name", out var nameEl))
                            {
                                promptName = nameEl.GetString() ?? "";
                                _logger.LogDebug("Parsed prompt name: '{PromptName}'", promptName);
                            }
                            else
                            {
                                _logger.LogWarning("Prompt JSON does not contain 'name' property");
                            }
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to parse prompt JSON: {PromptJson}", promptJson.Length > 100 ? promptJson.Substring(0, 100) + "..." : promptJson
                            );
                        }

                        bool removePrompt = !string.IsNullOrEmpty(promptName) && _shouldRemovePrompt(promptName);
                        _logger.LogDebug("Prompt '{PromptName}' removal decision: {RemovePrompt}", promptName, removePrompt);

                        if (!removePrompt)
                        {
                            StringBuilder promptToWrite = new StringBuilder();
                            if (_firstPromptWrittenInArray)
                            {
                                promptToWrite.Append(",");
                            }
                            promptToWrite.Append(promptJson);
                            await WriteStringToInnerAsync(promptToWrite.ToString(), cancellationToken);
                            _firstPromptWrittenInArray = true;
                            _logger.LogDebug("Kept prompt: {PromptName}", promptName);
                        }
                        else
                        {
                            _logger.LogInformation("Removed prompt: {PromptName}", promptName);
                        }

                        processedOffset += (promptJsonEndIndex - promptJsonStartIndex + 1);

                        // Consume the following comma if present (and any whitespace before it)
                        if (_dataBuffer.Length > processedOffset)
                        {
                            string remainingAfterPrompt = _dataBuffer.ToString(
                                processedOffset,
                                _dataBuffer.Length - processedOffset
                            );
                            int k = 0;
                            while (k < remainingAfterPrompt.Length && char.IsWhiteSpace(remainingAfterPrompt[k]))
                                k++;
                            if (k < remainingAfterPrompt.Length && remainingAfterPrompt[k] == ',')
                            {
                                processedOffset += (k + 1);
                                _logger.LogTrace("Consumed trailing comma after prompt");
                            }
                        }
                        continue;
                    }
                    else
                    {
                        _logger.LogTrace("Prompt JSON incomplete, waiting for more data");
                        if (!isFlushing)
                            break;
                    }
                }
                else if (arrayEndMarkerIndex != -1) // Found end of array "]"
                {
                    _logger.LogDebug("Found end of prompts array at index {ArrayEndMarkerIndex}", arrayEndMarkerIndex);
                    int consumeUntil = arrayEndMarkerIndex + 1;
                    string suffix = currentContent.Substring(0, consumeUntil);
                    await WriteStringToInnerAsync(suffix, cancellationToken);
                    processedOffset += consumeUntil;
                    _inPromptsArray = false;
                    _logger.LogInformation("Exited prompts array - filtering mode deactivated.");
                    continue;
                }
                else
                {
                    _logger.LogTrace("No prompt start or array end found, waiting for more data");
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
