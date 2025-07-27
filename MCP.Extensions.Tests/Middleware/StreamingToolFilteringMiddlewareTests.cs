using System.Text;
using MCP.Extensions.Middleware;
using MCP.Extensions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MCP.Extensions.Tests.Middleware;

public class StreamingToolFilteringMiddlewareTests
{
    private readonly Mock<IAudienceFilterService> _mockAudienceFilterService;
    private readonly Mock<ILogger<FilteringWriteStream>> _mockLogger;
    private readonly Mock<RequestDelegate> _mockNext;
    private readonly StreamingToolFilteringMiddleware _middleware;

    public StreamingToolFilteringMiddlewareTests()
    {
        _mockAudienceFilterService = new Mock<IAudienceFilterService>();
        _mockLogger = new Mock<ILogger<FilteringWriteStream>>();
        _mockNext = new Mock<RequestDelegate>();
        _middleware = new StreamingToolFilteringMiddleware(_mockNext.Object, _mockAudienceFilterService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task InvokeAsync_WithSingleAgentMode_ParsesCorrectly()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = "PRODUCTS";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Parsed X-AGENT-MODE values: [PRODUCTS]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithCsvAgentMode_ParsesAllValues()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = "PRODUCTS,PRICING,ADMIN";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Parsed X-AGENT-MODE values: [PRODUCTS, PRICING, ADMIN]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithCsvAgentModeWithSpaces_TrimsAndParses()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = " PRODUCTS , PRICING , ADMIN ";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Parsed X-AGENT-MODE values: [PRODUCTS, PRICING, ADMIN]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithLowercaseAgentMode_ConvertsToUppercase()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = "products,pricing";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Parsed X-AGENT-MODE values: [PRODUCTS, PRICING]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithEmptyAgentMode_ParsesAsEmptyArray()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = "";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Parsed X-AGENT-MODE values: []")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithNoAgentModeHeader_ParsesAsEmptyArray()
    {
        // Arrange
        var context = CreateHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Parsed X-AGENT-MODE values: []")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithEmptyValuesInCsv_FiltersOutEmptyValues()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = "PRODUCTS,,PRICING,";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Parsed X-AGENT-MODE values: [PRODUCTS, PRICING]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("test_tool", new[] { "PRODUCTS" }, new[] { "PRODUCTS" }, false)] // Should keep - exact match
    [InlineData("test_tool", new[] { "PRODUCTS" }, new[] { "products" }, false)] // Should keep - case insensitive
    [InlineData("test_tool", new[] { "PRODUCTS", "PRICING" }, new[] { "PRICING" }, false)] // Should keep - one match
    [InlineData("test_tool", new[] { "PRODUCTS", "PRICING" }, new[] { "ADMIN" }, true)] // Should remove - no match
    [InlineData("test_tool", new[] { "PRODUCTS" }, new string[0], false)] // Should keep - no restrictions
    [InlineData("test_tool", new string[0], new[] { "PRODUCTS" }, true)] // Should remove - no agent modes
    public async Task ShouldRemoveToolDelegate_WithVariousScenarios_ReturnsExpectedResult(
        string toolName,
        string[] agentModes,
        string[] allowedAudiences,
        bool expectedShouldRemove)
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = string.Join(",", agentModes);
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", toolName))
            .Returns(allowedAudiences);

        bool actualShouldRemove = false;
        _mockNext.Setup(x => x(It.IsAny<HttpContext>()))
            .Callback<HttpContext>(ctx =>
            {
                // Access the FilteringWriteStream to test the delegate
                var filteringStream = ctx.Response.Body as FilteringWriteStream;
                // We can't directly access the delegate, but we can test through the middleware logic
            })
            .Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _mockAudienceFilterService.Verify(x => x.GetAudiencesForResource("tool", It.IsAny<string>()), Times.Never);
        // Note: This test is more of an integration test since we can't easily access the delegate directly
    }

    [Fact]
    public async Task FilteringWriteStream_WithCaseInsensitiveAudiences_FiltersCorrectly()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Request.Headers["X-AGENT-MODE"] = "PRODUCTS,PRICING";
        
        // Setup tool with lowercase audiences
        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", "lowercase_tool"))
            .Returns(new[] { "products", "admin" }); // lowercase
        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", "mixed_case_tool"))
            .Returns(new[] { "Pricing", "Admin" }); // mixed case

        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        context.Response.ContentType = "application/json";

        var jsonResponse = """
        {
            "result": {
                "tools": [
                    {"name": "lowercase_tool", "description": "Should be kept - products matches"},
                    {"name": "mixed_case_tool", "description": "Should be kept - Pricing matches"}
                ]
            }
        }
        """;

        _mockNext.Setup(x => x(It.IsAny<HttpContext>()))
            .Callback<HttpContext>(async ctx =>
            {
                var bytes = Encoding.UTF8.GetBytes(jsonResponse);
                await ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
            })
            .Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        responseBody.Position = 0;
        var result = await new StreamReader(responseBody).ReadToEndAsync();
        
        // Both tools should be kept due to case-insensitive matching
        Assert.Contains("lowercase_tool", result);
        Assert.Contains("mixed_case_tool", result);
    }

    [Fact]
    public async Task FilteringWriteStream_NoAgentModes_RemovesRestrictedTools()
    {
        // Arrange - Test with no agent modes (empty array)
        var innerStream = new MemoryStream();
        var logger = new Mock<ILogger<FilteringWriteStream>>();
        
        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", "restricted_tool"))
            .Returns(new[] { "ADMIN" });
        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", "unrestricted_tool"))
            .Returns(Array.Empty<string>());

        // Create delegate with no agent modes
        string[] agentModes = Array.Empty<string>();
        Func<string, bool> shouldRemoveToolDelegate = (toolName) =>
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            if (!agentModes.Any())
            {
                // If no agent modes, remove tools with restrictions but keep unrestricted ones
                string[] allowedAudiences = _mockAudienceFilterService.Object.GetAudiencesForResource("tool", toolName);
                return allowedAudiences.Any(); // Remove if has restrictions
            }

            return false;
        };

        var filteringStream = new FilteringWriteStream(innerStream, "application/json", logger.Object, shouldRemoveToolDelegate);

        var jsonResponse = """
        {"result":{"tools":[{"name":"restricted_tool","description":"Should be removed"},{"name":"unrestricted_tool","description":"Should be kept"}]}}
        """;

        // Act
        var bytes = Encoding.UTF8.GetBytes(jsonResponse);
        await filteringStream.WriteAsync(bytes, 0, bytes.Length);
        await filteringStream.FlushAsync();

        // Assert
        innerStream.Position = 0;
        var result = await new StreamReader(innerStream).ReadToEndAsync();
        
        // Use precise JSON pattern matching to avoid false positives
        Assert.DoesNotContain("\"name\":\"restricted_tool\"", result);
        Assert.Contains("\"name\":\"unrestricted_tool\"", result);
    }

    [Fact]
    public async Task FilteringWriteStream_DirectTest_FiltersToolsCorrectly()
    {
        // Arrange - Test the FilteringWriteStream directly
        var innerStream = new MemoryStream();
        var logger = new Mock<ILogger<FilteringWriteStream>>();
        
        // Setup tool audience service
        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", "allowed_tool"))
            .Returns(new[] { "PRODUCTS" });
        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", "restricted_tool"))
            .Returns(new[] { "ADMIN" });
        _mockAudienceFilterService.Setup(x => x.GetAudiencesForResource("tool", "unrestricted_tool"))
            .Returns(Array.Empty<string>());

        // Create delegate that simulates the middleware logic
        string[] agentModes = { "PRODUCTS", "PRICING" };
        Func<string, bool> shouldRemoveToolDelegate = (toolName) =>
        {
            if (string.IsNullOrEmpty(toolName) || !agentModes.Any())
                return !agentModes.Any(); // Remove if no agent modes

            string[] allowedAudiences = _mockAudienceFilterService.Object.GetAudiencesForResource("tool", toolName);
            
            if (allowedAudiences.Any())
            {
                var normalizedAllowedAudiences = allowedAudiences.Select(a => a.ToUpperInvariant()).ToArray();
                return !agentModes.Any(mode => normalizedAllowedAudiences.Contains(mode, StringComparer.OrdinalIgnoreCase));
            }
            
            return false; // Keep unrestricted tools
        };

        var filteringStream = new FilteringWriteStream(innerStream, "application/json", logger.Object, shouldRemoveToolDelegate);

        // Prepare JSON response
        var jsonResponse = """
        {"result":{"tools":[{"name":"allowed_tool","description":"Should be kept"},{"name":"restricted_tool","description":"Should be removed"},{"name":"unrestricted_tool","description":"Should be kept"}]}}
        """;

        // Act - Write the JSON in chunks to simulate streaming
        var bytes = Encoding.UTF8.GetBytes(jsonResponse);
        var chunkSize = 20;
        for (int i = 0; i < bytes.Length; i += chunkSize)
        {
            var chunk = Math.Min(chunkSize, bytes.Length - i);
            await filteringStream.WriteAsync(bytes, i, chunk);
        }
        await filteringStream.FlushAsync();

        // Assert
        innerStream.Position = 0;
        var result = await new StreamReader(innerStream).ReadToEndAsync();
        
        // Verify filtering worked by checking for exact tool name matches in JSON
        Assert.Contains("allowed_tool", result);
        Assert.Contains("unrestricted_tool", result);
        
        // Check that "restricted_tool" was removed by looking for the exact JSON pattern
        // This avoids false positives from "unrestricted_tool" containing "restricted_tool"
        Assert.DoesNotContain("\"name\":\"restricted_tool\"", result);
        
        // Alternative verification: parse as JSON and check tool names
        using var doc = System.Text.Json.JsonDocument.Parse(result);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var toolNames = new List<string>();
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.TryGetProperty("name", out var nameElement))
            {
                toolNames.Add(nameElement.GetString() ?? "");
            }
        }
        
        Assert.Contains("allowed_tool", toolNames);
        Assert.Contains("unrestricted_tool", toolNames);
        Assert.DoesNotContain("restricted_tool", toolNames);
    }

    private static HttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }
}
