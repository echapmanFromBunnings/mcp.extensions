# mcp.extensions

A collection of reusable .NET middleware, attributes, and services to extend and enhance ASP.NET Core applications working with the Model Context Protocol.

Built against ModelContextProtocol SDK v0.4.0-preview.3.

## Features

- **Audience-Based Segmentation** 🎯
  - `McpAudienceAttribute`: Declarative attribute for audience-based access control on MCP tools, resources, and prompts
  - **Streaming Middleware**: Real-time filtering of MCP responses based on client audience credentials
    - `StreamingToolFilteringMiddleware`: Filters tools in real-time based on audience
    - `StreamingResourceFilteringMiddleware`: Filters resources in real-time based on audience
    - `StreamingPromptFilteringMiddleware`: Filters prompts in real-time based on audience
  - `IAudienceFilterService`: Unified service for managing audience-to-resource mappings
  - 📖 **[Complete Guide](docs/McpAudienceAttribute-Streaming-Guide.md)**: Comprehensive documentation on audience segmentation and streaming implementation

- **Logging Middleware**
  - `HeaderLoggingMiddleware`: Logs HTTP request headers for diagnostics and auditing
  - `RequestBodyLoggingMiddleware`: Logs incoming HTTP request bodies for debugging and traceability
  - `ResponseBodyLoggingMiddleware`: Logs outgoing HTTP response bodies for monitoring and troubleshooting

- **Legacy Services** (Deprecated)
  - `IToolAudienceService`: Legacy interface for tool audience management (use `IAudienceFilterService` instead)

## Getting Started

### Quick Start: Audience-Based Segmentation

The easiest way to enable audience-based filtering for your MCP server:

```csharp
// 1. Register services and scan your assembly for MCP attributes
builder.Services.AddMcpAudienceFiltering(Assembly.GetExecutingAssembly());

// 2. Apply all filtering middlewares
app.UseMcpAudienceFiltering();

// 3. Decorate your MCP tools, resources, and prompts with audience restrictions
public class MyMcpTools
{
    [McpServerTool("admin_tool")]
    [McpAudience("ADMIN")]
    public async Task<Result> AdminOnlyTool() { }
    
    [McpServerTool("public_tool")]
    [McpAudience("PUBLIC", "ADMIN")]
    public async Task<Result> PublicTool() { }
}
```

Clients send their audience type via the `X-AGENT-MODE` header:
```http
GET /mcp/tools
X-AGENT-MODE: ADMIN
```

**📖 For a complete guide with examples, best practices, and technical details, see the [McpAudienceAttribute and Streaming Implementation Guide](docs/McpAudienceAttribute-Streaming-Guide.md)**

### Advanced Usage

**Logging Middleware:**
```csharp
app.UseMiddleware<HeaderLoggingMiddleware>();
app.UseMiddleware<RequestBodyLoggingMiddleware>();
app.UseMiddleware<ResponseBodyLoggingMiddleware>();
```

## Contributing

Contributions are welcome! Please open issues or submit pull requests for new features, bug fixes, or improvements.

## License

[MIT](LICENSE)
