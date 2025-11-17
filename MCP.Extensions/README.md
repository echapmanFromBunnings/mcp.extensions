# MCP.Extensions

Extensions and middleware for ModelContextProtocol in ASP.NET Core.

## Overview
This package provides useful middleware and extension methods for working with the [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) in ASP.NET Core applications. It is designed to help with request/response logging, audience filtering, and other common tasks when using ModelContextProtocol.

**Key Feature: Audience-Based Segmentation** - The `McpAudienceAttribute` and its streaming implementation allow you to efficiently segment your MCP server resources (tools, resources, prompts) by audience type. This enables a single MCP server to serve multiple client types with different permission levels, maximizing resource utilization while maintaining strict security boundaries.

📖 **[Read the Complete Guide](../docs/McpAudienceAttribute-Streaming-Guide.md)** for detailed information on audience segmentation, streaming implementation, benefits, and best practices.

This package is built against ModelContextProtocol SDK v0.4.0-preview.3.

## Features
- Middleware for logging HTTP headers, request bodies, and response bodies
- **Audience-based filtering middleware** for tools, resources, and prompts with real-time streaming
- **Attribute-based audience targeting** via `[McpAudience]` for declarative security
- Service abstractions for unified audience management across all MCP resource types
- Efficient streaming implementation that filters responses in real-time without buffering

## Installation
Install via NuGet Package Manager:

```
dotnet add package MCP.Extensions
```

Or via the NuGet UI in Visual Studio.

## Usage
Add the desired middleware to your ASP.NET Core pipeline in `Startup.cs` or `Program.cs`:

```csharp
// Recommended: Use the unified audience filtering (includes tools, resources, and prompts)
builder.Services.AddMcpAudienceFiltering(Assembly.GetExecutingAssembly());
app.UseMcpAudienceFiltering();

// Optional: Debugging and logging middleware
app.UseMiddleware<HeaderLoggingMiddleware>();
app.UseMiddleware<RequestBodyLoggingMiddleware>();
app.UseMiddleware<ResponseBodyLoggingMiddleware>();
```

Use the `[McpAudience]` attribute to restrict tools, resources, or prompts to specific audiences:

```csharp
[McpServerTool("admin_tool")]
[McpAudience("ADMIN")]
public async Task<Result> AdminOnlyTool() 
{
    // Only accessible to clients with X-AGENT-MODE: ADMIN
}

[McpServerResource("sensitive-data", UriTemplate = "data://sensitive/{id}")]
[McpAudience("FINANCE", "ADMIN")]
public async Task<Data> GetSensitiveData(string id)
{
    // Only accessible to FINANCE and ADMIN audiences
}
```

For comprehensive guidance, examples, and best practices, see the **[Complete Guide](../docs/McpAudienceAttribute-Streaming-Guide.md)**.

## Requirements
- .NET 9.0 or later
- [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) v0.4.0-preview.3
- [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) v0.4.0-preview.3

## License
MIT

## Repository
[https://github.com/echapmanFromBunnings/mcp.extensions](https://github.com/echapmanFromBunnings/mcp.extensions)

